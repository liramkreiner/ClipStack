using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClipStack.Core;
using ClipStack.Native;
using ClipStack.Services;
using ClipStack.UI;

namespace ClipStack;

/// <summary>Composition root: creates the services and coordinates popup, tray, settings and paste.</summary>
public sealed class AppController : IDisposable
{
    private MessageWindow _messages = null!;
    private ClipboardMonitor _monitor = null!;
    private ClipboardWriter _writer = null!;
    private HotkeyManager _hotkeys = null!;
    private ForegroundTracker _foreground = null!;
    private TrayIcon _tray = null!;
    private PopupWindow _popup = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer _cleanupTimer = null!;

    public AppSettings Settings { get; private set; } = null!;
    public HistoryService History { get; private set; } = null!;
    public HotkeyManager Hotkeys => _hotkeys;

    public string HotkeyText =>
        Hotkey.TryParse(Settings.Hotkey, out var hk) ? hk.ToString() : Settings.Hotkey;

    public bool Start()
    {
        // For development/testing: keep a separate profile.
        if (Environment.GetEnvironmentVariable("CLIPSTACK_DATA_DIR") is { Length: > 0 } devDir)
            AppPaths.OverrideDataDirectory(devDir);
        AppPaths.EnsurePrivateDirectory(AppPaths.DataDirectory);
        Settings = AppSettings.Load(AppPaths.SettingsFile);
        Log.Verbose = Settings.EnableLogging;
        Log.Info("Starting");
        ThemeManager.Apply(Application.Current.Resources, Settings.Theme);

        try
        {
            var dbFile = AppPaths.DatabaseFile(Settings);
            if (!string.IsNullOrWhiteSpace(Settings.DatabaseDirectory))
                AppPaths.EnsurePrivateDirectory(Settings.DatabaseDirectory!);
            History = new HistoryService(new HistoryStore(dbFile), new Protector(AppPaths.KeyFile), () => Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the history database", ex);
            MessageBox.Show($"ClipStack could not open its history database:\n\n{ex.Message}\n\nSee {AppPaths.LogFile}",
                AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        _messages = new MessageWindow();
        _monitor = new ClipboardMonitor(_messages, () => Settings) { Paused = Settings.Paused };
        _monitor.Captured += OnCaptured;
        _writer = new ClipboardWriter(History, _monitor);
        _hotkeys = new HotkeyManager(_messages);
        _hotkeys.Pressed += () => ShowPopup(fromTray: false);
        _foreground = new ForegroundTracker();

        _popup = new PopupWindow(this);
        _popup.Prepare();
        _tray = new TrayIcon(this) { Visible = Settings.ShowTrayIcon };

        _monitor.Start();
        RegisterHotkey(notifyOnFailure: true);

        History.EnforceLimits();
        _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _cleanupTimer.Tick += (_, _) => History.EnforceLimits();
        _cleanupTimer.Start();

        if (!Settings.FirstRunCompleted)
        {
            StartupManager.Apply(Settings.StartWithWindows);
            Settings.FirstRunCompleted = true;
            SaveSettings();
            new OnboardingWindow(this).Show();
        }
        else if (Settings.StartWithWindows && !StartupManager.IsEnabled())
        {
            StartupManager.Apply(true); // keep the startup entry pointing at the current exe location
        }
        Log.Info($"Started with {History.Items.Count} item(s)");
        return true;
    }

    private void RegisterHotkey(bool notifyOnFailure)
    {
        if (!Hotkey.TryParse(Settings.Hotkey, out var hk))
        {
            hk = new Hotkey(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.V);
            Settings.Hotkey = hk.ToSetting();
        }
        if (!_hotkeys.Register(hk))
        {
            Log.Error($"Hotkey {hk} is already in use");
            if (notifyOnFailure)
                _tray.ShowBalloon("Shortcut unavailable",
                    $"{hk} is used by another application. Open ClipStack from the tray icon and choose a different shortcut in Settings.",
                    System.Windows.Forms.ToolTipIcon.Warning);
        }
        _tray.Refresh();
    }

    /// <summary>Temporarily release the hotkey (while the user records a new one).</summary>
    public void SuspendHotkey() => _hotkeys.Unregister();

    public void ResumeHotkey()
    {
        if (_hotkeys is { Current: null }) RegisterHotkey(notifyOnFailure: false);
    }

    /// <summary>Minimal initialization used by <see cref="DevPreview"/> (no hotkey, tray or clipboard hooks).</summary>
    internal void InitializeForPreview(AppSettings settings, HistoryService history)
    {
        Settings = settings;
        History = history;
    }

    private void OnCaptured(CapturedClip clip)
    {
        if (Settings.Paused) return;
        var item = History.Add(clip, History.ComputeHash(clip));
        Log.Info($"Captured {clip.Kind} from {clip.SourceApp ?? "unknown app"} (copies: {item.CopyCount})");
    }

    // ------------------------------------------------------------------ popup

    public void ShowPopup(bool fromTray)
    {
        if (_popup.IsVisible)
        {
            _popup.HidePopup();
            return;
        }
        var target = fromTray ? _foreground.LastExternalWindow : NativeMethods.GetForegroundWindow();
        _popup.ShowFor(target);
    }

    public async void Choose(ClipItem item, ChooseMode mode)
    {
        bool plain = mode == ChooseMode.PastePlain;
        if (!_writer.Write(item, plain))
        {
            _popup.HidePopup();
            return;
        }
        History.MarkUsed(item);

        var target = _popup.PreviousWindow;
        bool paste = mode != ChooseMode.Copy && Settings.PasteOnSelect && target != IntPtr.Zero && NativeMethods.IsWindow(target);
        if (!paste)
        {
            _popup.HidePopup();
            return;
        }

        // Hand focus back while we're still the foreground app, then close and paste.
        PasteService.RestoreFocus(target);
        _popup.HidePopup();
        try
        {
            await PasteService.PasteIntoAsync(target);
            if (mode == ChooseMode.PasteKeepOpen)
            {
                await Task.Delay(150);
                _popup.ShowFor(target);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Paste failed", ex);
        }
    }

    public void ShowDetails(ClipItem item)
    {
        _popup.HidePopup();
        var w = new DetailsWindow(this, item);
        w.Show();
        w.Activate();
    }

    public void CopyToClipboard(ClipItem item, bool plain = false)
    {
        if (_writer.Write(item, plain)) History.MarkUsed(item);
    }

    // ------------------------------------------------------------------ tray actions

    public void TogglePause()
    {
        Settings.Paused = !Settings.Paused;
        _monitor.Paused = Settings.Paused;
        SaveSettings();
        _tray.Refresh();
    }

    public void ClearHistory(bool includePinned)
    {
        var message = includePinned
            ? "Delete your entire clipboard history, including pinned items?\n\nThis cannot be undone."
            : "Delete your clipboard history? Pinned items will be kept.\n\nThis cannot be undone.";
        if (MessageBox.Show(message, AppPaths.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        History.Clear(includePinned);
    }

    public void OpenSettings(string? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (page is not null) _settingsWindow.ShowPage(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    // ------------------------------------------------------------------ settings

    public void SaveSettings() => Settings.Save(AppPaths.SettingsFile);

    /// <summary>Apply settings edited in the settings window.</summary>
    public async Task ApplySettingsAsync(AppSettings updated)
    {
        var old = Settings;
        // Runtime state isn't edited in the settings window.
        updated.Paused = old.Paused;
        updated.FirstRunCompleted = old.FirstRunCompleted;
        Settings = updated;

        Log.Verbose = updated.EnableLogging;
        if (updated.Hotkey != old.Hotkey) RegisterHotkey(notifyOnFailure: false);
        if (updated.StartWithWindows != old.StartWithWindows) StartupManager.Apply(updated.StartWithWindows);
        _tray.Visible = updated.ShowTrayIcon;
        ThemeManager.Apply(Application.Current.Resources, updated.Theme);
        SaveSettings();

        if (updated.EncryptHistory != old.EncryptHistory)
        {
            int n = await History.SetEncryptionAsync(updated.EncryptHistory);
            Log.Info($"Re-encoded {n} item(s); encryption {(updated.EncryptHistory ? "on" : "off")}");
        }

        var newDb = AppPaths.DatabaseFile(updated);
        if (!string.Equals(newDb, History.DatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                AppPaths.EnsurePrivateDirectory(Path.GetDirectoryName(newDb)!);
                await History.MoveDatabaseAsync(newDb);
            }
            catch (Exception ex)
            {
                Log.Error("Moving the database failed", ex);
                Settings.DatabaseDirectory = old.DatabaseDirectory;
                SaveSettings();
                MessageBox.Show($"Could not move the database:\n{ex.Message}", AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        History.EnforceLimits();
        _tray.Refresh();
    }

    public void Quit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer?.Stop();
        SaveSettings();
        _hotkeys?.Dispose();
        _monitor?.Dispose();
        _foreground?.Dispose();
        _tray?.Dispose();
        History?.Dispose();
        _messages?.Dispose();
        Log.Info("Stopped");
    }
}
