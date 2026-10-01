using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipStack.Core;
using ClipStack.Services;

namespace ClipStack.UI;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private Hotkey? _hotkey;
    private string? _dbDirectory;

    private sealed record Option(string Label, object Value);

    public SettingsWindow(AppController app)
    {
        _app = app;
        ThemeMode = ThemeManager.ToThemeMode(app.Settings.Theme);
        InitializeComponent();

        Placement.ItemsSource = new[]
        {
            new Option("At the mouse pointer", PopupPlacement.Cursor),
            new Option("Center of the active window", PopupPlacement.ActiveWindow),
            new Option("Center of the screen", PopupPlacement.ScreenCenter),
        };
        MaxItems.ItemsSource = new[] { 50, 100, 500, 1000, 5000, 10000 }
            .Select(n => new Option(n.ToString("N0"), n)).Append(new Option("Unlimited", 0)).ToArray();
        AutoCleanup.ItemsSource = new[]
        {
            new Option("Never", CleanupAge.Never), new Option("1 hour", CleanupAge.OneHour),
            new Option("1 day", CleanupAge.OneDay), new Option("7 days", CleanupAge.SevenDays),
            new Option("30 days", CleanupAge.ThirtyDays),
        };
        Theme.ItemsSource = new[]
        {
            new Option("Use Windows setting", ThemeChoice.System), new Option("Light", ThemeChoice.Light), new Option("Dark", ThemeChoice.Dark),
        };

        LoadFrom(app.Settings);
        Nav.SelectedIndex = 0;
        Closed += (_, _) => _app.ResumeHotkey();
    }

    public void ShowPage(string page)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == page) Nav.SelectedItem = item;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string page }) return;
        foreach (var (name, panel) in new[]
                 {
                     ("General", PageGeneral), ("History", PageHistory), ("Privacy", PagePrivacy),
                     ("Appearance", PageAppearance), ("Advanced", PageAdvanced), ("About", PageAbout),
                 })
            panel.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        if (page == "About") UpdateAbout();
    }

    private void LoadFrom(AppSettings s)
    {
        _hotkey = Hotkey.TryParse(s.Hotkey, out var hk) ? hk : null;
        HotkeyBox.Text = _hotkey?.ToString() ?? "";
        StartWithWindows.IsChecked = s.StartWithWindows;
        ShowTrayIcon.IsChecked = s.ShowTrayIcon;
        PasteOnSelect.IsChecked = s.PasteOnSelect;
        Placement.SelectedValue = s.Placement;

        MaxItems.SelectedValue = s.MaxItems;
        if (MaxItems.SelectedIndex < 0)
        {
            MaxItems.ItemsSource = ((Option[])MaxItems.ItemsSource).Append(new Option(s.MaxItems.ToString("N0"), s.MaxItems)).ToArray();
            MaxItems.SelectedValue = s.MaxItems;
        }
        AutoCleanup.SelectedValue = s.AutoCleanup;
        CleanupIncludesPinned.IsChecked = s.CleanupIncludesPinned;
        AllowDuplicates.IsChecked = s.AllowDuplicates;
        TrackUsage.IsChecked = s.TrackUsage;

        IgnoreSensitive.IsChecked = s.IgnoreSensitive;
        EncryptHistory.IsChecked = s.EncryptHistory;
        StoreImages.IsChecked = s.StoreImages;
        StoreFiles.IsChecked = s.StoreFiles;
        StoreRichText.IsChecked = s.StoreRichText;
        ExcludedApps.Items.Clear();
        foreach (var app in s.ExcludedApps) ExcludedApps.Items.Add(app);

        Theme.SelectedValue = s.Theme;
        PopupWidth.Text = s.PopupWidth.ToString("0");
        PopupHeight.Text = s.PopupHeight.ToString("0");
        PreviewLength.Value = s.PreviewLength;
        ShowTimestamps.IsChecked = s.ShowTimestamps;
        ShowAppIcons.IsChecked = s.ShowAppIcons;

        _dbDirectory = s.DatabaseDirectory;
        DatabaseDirectory.Text = string.IsNullOrWhiteSpace(_dbDirectory) ? AppPaths.DataDirectory + "  (default)" : _dbDirectory;
        EnableLogging.IsChecked = s.EnableLogging;
    }

    private AppSettings? Collect()
    {
        if (_hotkey is null)
        {
            ShowPage("General");
            HotkeyStatus.Text = "Choose a shortcut first.";
            return null;
        }
        if (!double.TryParse(PopupWidth.Text, out var w) || !double.TryParse(PopupHeight.Text, out var h))
        {
            ShowPage("Appearance");
            MessageBox.Show(this, "Window size must be a number.", Title);
            return null;
        }

        var s = _app.Settings.Clone();
        s.Hotkey = _hotkey.Value.ToSetting();
        s.StartWithWindows = StartWithWindows.IsChecked == true;
        s.ShowTrayIcon = ShowTrayIcon.IsChecked == true;
        s.PasteOnSelect = PasteOnSelect.IsChecked == true;
        s.Placement = (PopupPlacement)Placement.SelectedValue;
        s.MaxItems = (int)MaxItems.SelectedValue;
        s.AutoCleanup = (CleanupAge)AutoCleanup.SelectedValue;
        s.CleanupIncludesPinned = CleanupIncludesPinned.IsChecked == true;
        s.AllowDuplicates = AllowDuplicates.IsChecked == true;
        s.TrackUsage = TrackUsage.IsChecked == true;
        s.IgnoreSensitive = IgnoreSensitive.IsChecked == true;
        s.EncryptHistory = EncryptHistory.IsChecked == true;
        s.StoreImages = StoreImages.IsChecked == true;
        s.StoreFiles = StoreFiles.IsChecked == true;
        s.StoreRichText = StoreRichText.IsChecked == true;
        s.ExcludedApps = ExcludedApps.Items.Cast<string>().ToList();
        s.Theme = (ThemeChoice)Theme.SelectedValue;
        s.PopupWidth = Math.Clamp(w, 300, 2000);
        s.PopupHeight = Math.Clamp(h, 240, 2000);
        s.PreviewLength = (int)PreviewLength.Value;
        s.ShowTimestamps = ShowTimestamps.IsChecked == true;
        s.ShowAppIcons = ShowAppIcons.IsChecked == true;
        s.DatabaseDirectory = _dbDirectory;
        s.EnableLogging = EnableLogging.IsChecked == true;
        return s;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var s = Collect();
        if (s is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            await _app.ApplySettingsAsync(s);
            ThemeMode = ThemeManager.ToThemeMode(s.Theme);
            Close();
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- hotkey capture

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _app.SuspendHotkey(); // otherwise pressing the current shortcut would open the popup
        HotkeyStatus.Text = "Press the new key combination (Esc to cancel).";
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        _app.ResumeHotkey();
        HotkeyBox.Text = _hotkey?.ToString() ?? "";
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= ModifierKeys.Windows;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            Nav.Focus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            HotkeyBox.Text = new Hotkey(mods, Key.None).ToString().Replace("None", "…");
            return;
        }
        if (mods == ModifierKeys.None || mods == ModifierKeys.Shift)
        {
            HotkeyStatus.Text = "Use at least Ctrl, Alt or Win together with a key.";
            return;
        }

        var hk = new Hotkey(mods, key);
        HotkeyBox.Text = hk.ToString();
        if (_app.Hotkeys.IsAvailable(hk))
        {
            _hotkey = hk;
            HotkeyStatus.Text = $"✓ {hk} is available.";
        }
        else
        {
            HotkeyStatus.Text = $"⚠ {hk} is already used by Windows or another application. Try another combination.";
        }
    }

    private void OnHotkeyReset(object sender, RoutedEventArgs e)
    {
        _hotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.V);
        HotkeyBox.Text = _hotkey.ToString();
        HotkeyStatus.Text = "Reset to the default shortcut.";
    }

    // ---------------------------------------------------------------- history

    private void OnClearHistory(object sender, RoutedEventArgs e) => _app.ClearHistory(includePinned: false);
    private void OnClearEverything(object sender, RoutedEventArgs e) => _app.ClearHistory(includePinned: true);

    // ---------------------------------------------------------------- exclusions

    private void OnExcludeAdd(object sender, RoutedEventArgs e) => AddExclusion(ExcludeInput.Text);

    private void OnExcludeInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddExclusion(ExcludeInput.Text);
    }

    private void AddExclusion(string? name)
    {
        name = name?.Trim().Trim('"');
        if (string.IsNullOrEmpty(name)) return;
        if (!name.Contains('\\')) name = Path.GetFileName(name);
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (!ExcludedApps.Items.Cast<string>().Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
            ExcludedApps.Items.Add(name);
        ExcludeInput.Text = "";
    }

    private void OnExcludeBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Choose an application to exclude" };
        if (dlg.ShowDialog(this) == true) AddExclusion(Path.GetFileName(dlg.FileName));
    }

    private void OnExcludeRemove(object sender, RoutedEventArgs e)
    {
        if (ExcludedApps.SelectedItem is { } item) ExcludedApps.Items.Remove(item);
    }

    // ---------------------------------------------------------------- advanced

    private void OnDbChange(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to keep the clipboard history database" };
        if (dlg.ShowDialog(this) != true) return;
        _dbDirectory = dlg.FolderName;
        DatabaseDirectory.Text = _dbDirectory;
    }

    private void OnDbDefault(object sender, RoutedEventArgs e)
    {
        _dbDirectory = null;
        DatabaseDirectory.Text = AppPaths.DataDirectory + "  (default)";
    }

    private void OnDbOpen(object sender, RoutedEventArgs e) => OpenFolder(Path.GetDirectoryName(_app.History.DatabasePath)!);
    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DataDirectory);

    private static void OpenFolder(string dir)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open folder", ex); }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restore all settings to their defaults? Click Save to apply.", Title,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            LoadFrom(new AppSettings());
    }

    private void UpdateAbout()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersion.Text = $"ClipStack {v?.ToString(3)}";
        long size = 0;
        try { size = new FileInfo(_app.History.DatabasePath).Length; } catch { /* ignore */ }
        int pinned = _app.History.Items.Count(i => i.IsPinned);
        AboutStats.Text = $"{_app.History.Items.Count:N0} items ({pinned} pinned) · {size / 1024.0 / 1024.0:0.0} MB\n{_app.History.DatabasePath}";
    }
}
