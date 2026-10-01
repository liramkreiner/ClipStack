using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using ClipStack.Core;

namespace ClipStack.UI;

/// <summary>System tray icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _statusItem;
    private Icon? _current;

    public TrayIcon(AppController app)
    {
        _app = app;
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        menu.Items.Add(new ToolStripLabel(AppPaths.AppName) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, System.Drawing.FontStyle.Bold) });
        _statusItem = new ToolStripMenuItem("Monitoring clipboard") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        _openItem = new ToolStripMenuItem("Open Clipboard", null, (_, _) => _app.ShowPopup(fromTray: true));
        menu.Items.Add(_openItem);
        _pauseItem = new ToolStripMenuItem("Pause Monitoring", null, (_, _) => _app.TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Clear History", null, (_, _) => _app.ClearHistory(includePinned: false)));
        menu.Items.Add(new ToolStripMenuItem("Clear Everything Including Pinned Items", null, (_, _) => _app.ClearHistory(includePinned: true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => _app.OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("About", null, (_, _) => _app.OpenSettings(page: "About")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => _app.Quit()));
        menu.Opening += (_, _) => UpdateMenu();

        _icon = new NotifyIcon { ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _app.ShowPopup(fromTray: true);
        };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Refresh();
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    /// <summary>Redraw for the current paused state and taskbar theme.</summary>
    public void Refresh()
    {
        bool paused = _app.Settings.Paused;
        var size = SystemInformation.SmallIconSize.Width;
        var old = _current;
        _current = Services.IconFactory.CreateTrayIcon(size, TaskbarIsLight(), paused);
        _icon.Icon = _current;
        old?.Dispose();
        _icon.Text = paused ? $"{AppPaths.AppName} — paused" : $"{AppPaths.AppName} — {_app.HotkeyText} to open";
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        bool paused = _app.Settings.Paused;
        _statusItem.Text = paused ? "Monitoring paused" : "Monitoring clipboard";
        _statusItem.Checked = !paused;
        _pauseItem.Text = paused ? "Resume Monitoring" : "Pause Monitoring";
        _openItem.ShortcutKeyDisplayString = _app.HotkeyText;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(5000, title, text, icon);
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
