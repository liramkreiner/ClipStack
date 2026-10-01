using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipStack.Core;
using ClipStack.Services;

namespace ClipStack.UI;

/// <summary>Display data for one row of the history popup.</summary>
public sealed class RowViewModel
{
    // Segoe Fluent Icons / Segoe MDL2 Assets code points
    private const string GlyphText = "";
    private const string GlyphLink = "";
    private const string GlyphImage = "";
    private const string GlyphFile = "";
    private const string GlyphFolder = "";
    private const string GlyphRich = "";

    private readonly HistoryService _history;
    private readonly AppSettings _settings;
    private ImageSource? _thumb;
    private bool _thumbLoaded;

    public RowViewModel(ClipItem item, HistoryService history, AppSettings settings, string? header, int index)
    {
        Item = item;
        _history = history;
        _settings = settings;
        Header = header;
        ShortcutHint = index < 9 ? $"Ctrl+{index + 1}" : null;
        Title = BuildTitle(item, settings.PreviewLength);
        Subtitle = BuildSubtitle(item, settings.ShowTimestamps);
    }

    public ClipItem Item { get; }
    public string? Header { get; }
    public bool HasHeader => Header is not null;
    public string Title { get; }
    public string Subtitle { get; }
    public string? ShortcutHint { get; }
    public bool IsPinned => Item.IsPinned;
    public bool IsImage => Item.Kind == ClipKind.Image;

    public string Glyph => Item.Kind switch
    {
        ClipKind.Image => GlyphImage,
        // Avoid touching the disk here (paths may be on slow network shares): guess from the extension.
        ClipKind.Files => Item.Files is { Length: 1 } f && Path.HasExtension(f[0]) ? GlyphFile : GlyphFolder,
        _ when Item.IsUrl => GlyphLink,
        _ when Item.HasRichText => GlyphRich,
        _ => GlyphText,
    };

    public ImageSource? AppIcon => _settings.ShowAppIcons ? AppInfo.GetIcon(Item.SourcePath) : null;
    public bool ShowGlyph => AppIcon is null;

    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbLoaded || !IsImage) return _thumb;
            _thumbLoaded = true;
            try
            {
                var bytes = _history.LoadThumbnail(Item);
                if (bytes is not null)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.EndInit();
                    bmp.Freeze();
                    _thumb = bmp;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not decode thumbnail", ex);
            }
            return _thumb;
        }
    }

    internal static string BuildTitle(ClipItem item, int maxLength)
    {
        maxLength = Math.Clamp(maxLength, 20, 1000);
        switch (item.Kind)
        {
            case ClipKind.Image:
                return "Image";
            case ClipKind.Files:
                var files = item.Files ?? [];
                return files.Length == 1 ? Truncate(DisplayName(files[0]), maxLength) : $"{files.Length} files";
            default:
                if (item.TryGetUrl(out var uri))
                    return Truncate(uri!.Host + uri.PathAndQuery.TrimEnd('/'), maxLength);
                return Truncate(FirstLine(item.Text ?? ""), maxLength);
        }
    }

    internal static string BuildSubtitle(ClipItem item, bool showTime)
    {
        var parts = new List<string>();
        switch (item.Kind)
        {
            case ClipKind.Image:
                parts.Add($"{item.ImageWidth} × {item.ImageHeight}");
                break;
            case ClipKind.Files:
                var files = item.Files ?? [];
                if (files.Length == 1) parts.Add(Path.GetDirectoryName(files[0].TrimEnd('\\')) ?? files[0]);
                else parts.Add(Truncate(string.Join(", ", files.Select(DisplayName)), 80));
                break;
            default:
                int lines = CountLines(item.Text);
                if (lines > 1) parts.Add($"{lines} lines");
                break;
        }
        if (!string.IsNullOrEmpty(item.SourceApp)) parts.Add(item.SourceApp!);
        if (showTime) parts.Add(RelativeTime(item.UpdatedAt));
        return string.Join("  ·  ", parts);
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>First non-blank line with runs of whitespace collapsed.</summary>
    internal static string FirstLine(string text)
    {
        var sb = new StringBuilder();
        bool space = false;
        bool started = false;
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                if (started) break;
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                space = started;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            started = true;
            sb.Append(ch);
            if (sb.Length > 1000) break;
        }
        return sb.ToString();
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int n = 1;
        foreach (var ch in text.AsSpan().Trim()) if (ch == '\n') n++;
        return n;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    internal static string RelativeTime(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 45) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)Math.Max(1, Math.Round(d.TotalMinutes))} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} d ago";
        return t.ToString(t.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy");
    }
}
