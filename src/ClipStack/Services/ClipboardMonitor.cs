using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipStack.Core;
using ClipStack.Native;

namespace ClipStack.Services;

/// <summary>
/// Listens for WM_CLIPBOARDUPDATE, reads the new clipboard content on the UI thread (OLE requires STA),
/// and hands heavy work (image encoding, secret detection) to the thread pool.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int MaxTextChars = 4_000_000;
    private const long MaxImagePixels = 40_000_000;
    private const int ThumbMaxHeight = 96;
    private const int ThumbMaxWidth = 320;

    // Formats that apps (password managers, Windows itself) use to ask clipboard tools not to record content.
    private static readonly uint FmtExcludeMonitor = NativeMethods.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint FmtViewerIgnore = NativeMethods.RegisterClipboardFormat("Clipboard Viewer Ignore");
    private const string FmtCanIncludeInHistory = "CanIncludeInClipboardHistory";

    private readonly MessageWindow _window;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _debounce;
    private readonly int _ownPid = Environment.ProcessId;
    private uint _ignoreSequence;
    private string? _pendingSourcePath;
    private bool _listening;

    /// <summary>Raised on the UI thread with fully processed content.</summary>
    public event Action<CapturedClip>? Captured;

    public bool Paused { get; set; }

    public ClipboardMonitor(MessageWindow window, Func<AppSettings> settings)
    {
        _window = window;
        _settings = settings;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ReadClipboard(); };
        _window.Message += OnMessage;
    }

    public void Start()
    {
        if (_listening) return;
        _listening = NativeMethods.AddClipboardFormatListener(_window.Handle);
        if (!_listening) Log.Error($"AddClipboardFormatListener failed ({Marshal.GetLastWin32Error()})");
    }

    /// <summary>Call right after writing to the clipboard ourselves, so our own write isn't recorded again.</summary>
    public void IgnoreCurrentContent() => _ignoreSequence = NativeMethods.GetClipboardSequenceNumber();

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE) return false;
        if (Paused || NativeMethods.GetClipboardSequenceNumber() == _ignoreSequence) return true;

        // Identify the source now, while the copying app most likely still owns the clipboard / has focus.
        var owner = NativeMethods.GetClipboardOwner();
        NativeMethods.GetWindowThreadProcessId(owner, out var pid);
        if (pid == _ownPid) return true;
        _pendingSourcePath = (owner != IntPtr.Zero ? NativeMethods.GetProcessPath(pid) : null)
                             ?? NativeMethods.GetProcessPathForWindow(NativeMethods.GetForegroundWindow());

        // Apps often update the clipboard several times in a row (one per format); wait for it to settle.
        _debounce.Stop();
        _debounce.Start();
        return true;
    }

    private void ReadClipboard()
    {
        if (Paused || NativeMethods.GetClipboardSequenceNumber() == _ignoreSequence) return;
        var s = _settings();
        var sourcePath = _pendingSourcePath;

        if (IsExcludedApp(sourcePath, s))
        {
            Log.Info("Skipped clipboard change from an excluded application");
            return;
        }
        if (NativeMethods.IsClipboardFormatAvailable(FmtExcludeMonitor) || NativeMethods.IsClipboardFormatAvailable(FmtViewerIgnore))
        {
            Log.Info("Skipped clipboard change marked private by the source application");
            return;
        }

        RawClip? raw;
        try
        {
            raw = Retry(() => ReadRaw(s));
        }
        catch (Exception ex)
        {
            Log.Error("Could not read clipboard", ex);
            return;
        }
        if (raw is null) return;

        var appName = AppInfo.GetDisplayName(sourcePath);
        Task.Run(() => Process(raw, s, sourcePath, appName)).ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Error("Clipboard processing failed", t.Exception?.GetBaseException());
            else if (t.Result is { } clip) Captured?.Invoke(clip);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    internal static bool IsExcludedApp(string? path, AppSettings s)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var exe = Path.GetFileName(path);
        var noExt = Path.GetFileNameWithoutExtension(path);
        return s.ExcludedApps.Any(x =>
        {
            var e = x.Trim();
            return e.Equals(exe, StringComparison.OrdinalIgnoreCase) || e.Equals(noExt, StringComparison.OrdinalIgnoreCase) ||
                   e.Equals(path, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Clipboard data copied out of OLE on the UI thread.</summary>
    private sealed record RawClip(
        ClipKind Kind, string? Text, string? Html, string? Rtf, string[]? Files,
        byte[]? Png, byte[]? Pixels, int Width, int Height, int Stride);

    private static RawClip? ReadRaw(AppSettings s)
    {
        var data = System.Windows.Clipboard.GetDataObject();
        if (data is null) return null;

        if (data.GetDataPresent(FmtCanIncludeInHistory) &&
            data.GetData(FmtCanIncludeInHistory) is MemoryStream flag && flag.Length >= 4 &&
            BitConverter.ToInt32(flag.ToArray(), 0) == 0)
        {
            Log.Info("Skipped clipboard change excluded from clipboard history by the source application");
            return null;
        }

        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            if (!s.StoreFiles) return null;
            if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                return new RawClip(ClipKind.Files, null, null, null, files, null, null, 0, 0, 0);
        }

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextChars)
            {
                string? html = null, rtf = null;
                if (s.StoreRichText)
                {
                    if (data.GetDataPresent(DataFormats.Html)) html = data.GetData(DataFormats.Html) as string;
                    if (data.GetDataPresent(DataFormats.Rtf)) rtf = data.GetData(DataFormats.Rtf) as string;
                }
                return new RawClip(ClipKind.Text, text, NullIfEmpty(html), NullIfEmpty(rtf), null, null, null, 0, 0, 0);
            }
            if (text is { Length: > MaxTextChars }) Log.Info("Skipped very large text");
        }

        if (!s.StoreImages) return null;

        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png && png.Length > 0)
            return new RawClip(ClipKind.Image, null, null, null, null, png.ToArray(), null, 0, 0, 0);

        if (data.GetDataPresent(DataFormats.Bitmap) && System.Windows.Clipboard.GetImage() is { } bmp)
        {
            if ((long)bmp.PixelWidth * bmp.PixelHeight > MaxImagePixels) return null;
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            FixOpaqueAlpha(pixels);
            return new RawClip(ClipKind.Image, null, null, null, null, null, pixels, converted.PixelWidth, converted.PixelHeight, stride);
        }
        return null;
    }

    /// <summary>
    /// CF_BITMAP/CF_DIB data is frequently 32bpp with an all-zero alpha channel, which would render
    /// fully transparent. Treat such images as opaque.
    /// </summary>
    private static void FixOpaqueAlpha(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] != 0) return;
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
    }

    private static CapturedClip? Process(RawClip raw, AppSettings s, string? sourcePath, string? appName)
    {
        switch (raw.Kind)
        {
            case ClipKind.Text:
                if (s.IgnoreSensitive && SensitiveDetector.Detect(raw.Text) is { } kind)
                {
                    Log.Info($"Skipped sensitive clipboard content ({kind})");
                    return null;
                }
                return new CapturedClip
                {
                    Kind = ClipKind.Text, Text = raw.Text, Html = raw.Html, Rtf = raw.Rtf,
                    SourceApp = appName, SourcePath = sourcePath,
                };

            case ClipKind.Files:
                return new CapturedClip { Kind = ClipKind.Files, Files = raw.Files, SourceApp = appName, SourcePath = sourcePath };

            case ClipKind.Image:
                BitmapSource bmp;
                if (raw.Png is not null)
                {
                    var decoder = BitmapDecoder.Create(new MemoryStream(raw.Png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    bmp = decoder.Frames[0];
                    if ((long)bmp.PixelWidth * bmp.PixelHeight > MaxImagePixels) return null;
                }
                else
                {
                    bmp = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Bgra32, null, raw.Pixels, raw.Stride);
                }

                var bgra = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int stride = bgra.PixelWidth * 4;
                var pixels = raw.Pixels ?? new byte[stride * bgra.PixelHeight];
                if (raw.Pixels is null) bgra.CopyPixels(pixels, stride, 0);

                return new CapturedClip
                {
                    Kind = ClipKind.Image,
                    ImagePng = raw.Png ?? EncodePng(bgra),
                    ThumbPng = EncodePng(MakeThumbnail(bgra)),
                    ImageWidth = bgra.PixelWidth,
                    ImageHeight = bgra.PixelHeight,
                    ImageIdentity = pixels,
                    SourceApp = appName,
                    SourcePath = sourcePath,
                };
        }
        return null;
    }

    private static BitmapSource MakeThumbnail(BitmapSource src)
    {
        double scale = Math.Min(1.0, Math.Min((double)ThumbMaxHeight / src.PixelHeight, (double)ThumbMaxWidth / src.PixelWidth));
        if (scale >= 1.0) return src;
        var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        t.Freeze();
        return t;
    }

    internal static byte[] EncodePng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>The clipboard is a shared lock; another app may hold it briefly.</summary>
    internal static T Retry<T>(Func<T> action, int attempts = 6)
    {
        for (int i = 1; ; i++)
        {
            try { return action(); }
            catch (Exception ex) when (i < attempts && ex is COMException or ExternalException)
            {
                Thread.Sleep(15 * i);
            }
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_listening) NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _window.Message -= OnMessage;
    }
}

/// <summary>Friendly names and icons of applications, cached by executable path.</summary>
public static class AppInfo
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static string? GetDisplayName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Names)
        {
            if (Names.TryGetValue(path, out var cached)) return cached;
            string name;
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                name = string.IsNullOrEmpty(desc) || desc.Length > 40 ? Path.GetFileNameWithoutExtension(path) : desc;
            }
            catch
            {
                name = Path.GetFileNameWithoutExtension(path);
            }
            return Names[path] = name;
        }
    }

    /// <summary>Small icon for an executable; must be called on the UI thread.</summary>
    public static ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    img.Freeze();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info($"No icon for {Path.GetFileName(path)}: {ex.Message}");
        }
        return Icons[path] = img;
    }
}
