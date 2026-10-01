using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClipStack.Core;
using ClipStack.Native;
using ClipStack.Services;

namespace ClipStack.UI;

public enum ChooseMode { Paste, PastePlain, Copy, PasteKeepOpen }

public partial class PopupWindow : Window
{
    private readonly AppController _app;
    private List<RowViewModel> _rows = [];
    private IntPtr _hwnd;
    private bool _placing;

    /// <summary>The window that had focus before the popup opened; pastes go there.</summary>
    public IntPtr PreviousWindow { get; private set; }

    public PopupWindow(AppController app)
    {
        _app = app;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            if (Environment.OSVersion.Version.Build < 22000) Frame.BorderThickness = new Thickness(1); // Windows 10: no DWM border
        };
        Deactivated += (_, _) => { if (IsVisible) HidePopup(); };
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += (_, _) =>
        {
            if (IsVisible && !_placing)
            {
                _app.Settings.PopupWidth = Math.Round(ActualWidth);
                _app.Settings.PopupHeight = Math.Round(ActualHeight);
            }
        };
        _app.History.Changed += () => { if (IsVisible) Refresh(keepSelection: true); };
    }

    /// <summary>Create the native window up front so the first open is instant.</summary>
    public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

    public void ShowFor(IntPtr previousWindow)
    {
        PreviousWindow = previousWindow;
        ThemeManager.Apply(Application.Current.Resources, _app.Settings.Theme);
        PausedBadge.Visibility = _app.Settings.Paused ? Visibility.Visible : Visibility.Collapsed;

        SearchBox.Text = "";
        Refresh(keepSelection: false);

        _placing = true;
        Width = _app.Settings.PopupWidth;
        Height = _app.Settings.PopupHeight;
        Place();
        Show();
        _placing = false;

        Activate();
        NativeMethods.ForceForeground(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    internal void SetSearchForPreview(string text) => SearchBox.Text = text;

    public void HidePopup()
    {
        if (!IsVisible) return;
        Hide();
        _app.SaveSettings();
    }

    // ---------------------------------------------------------------- list

    public void Refresh(bool keepSelection)
    {
        var previouslySelected = keepSelection ? (List.SelectedItem as RowViewModel)?.Item : null;
        int previousIndex = List.SelectedIndex;
        var s = _app.Settings;
        var query = SearchBox.Text;
        var results = SearchEngine.Search(_app.History.Ordered(), query, s.TrackUsage);
        bool searching = !string.IsNullOrWhiteSpace(query);
        bool anyPinned = !searching && results.Count > 0 && results[0].IsPinned;

        var rows = new List<RowViewModel>(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            string? header = null;
            if (anyPinned && i == 0) header = "PINNED";
            else if (anyPinned && !results[i].IsPinned && results[i - 1].IsPinned) header = "RECENT";
            rows.Add(new RowViewModel(results[i], _app.History, s, header, i));
        }
        _rows = rows;
        List.ItemsSource = _rows;

        int select = 0;
        if (previouslySelected is not null)
        {
            int idx = _rows.FindIndex(r => r.Item == previouslySelected);
            select = idx >= 0 ? idx : Math.Min(Math.Max(previousIndex, 0), _rows.Count - 1);
        }
        SelectIndex(select);

        Placeholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = searching
            ? $"No matches for “{query.Trim()}”"
            : $"Nothing here yet.\nCopy something, then press {_app.HotkeyText} to see it.";

        int total = _app.History.Items.Count;
        FooterText.Text = (searching ? $"{_rows.Count} of {total}" : $"{total} item{(total == 1 ? "" : "s")}") +
                          "     ↵ paste   ⇧↵ plain   Ctrl+P pin   Del delete";
    }

    private RowViewModel? Selected => List.SelectedItem as RowViewModel;

    private void SelectIndex(int index)
    {
        if (_rows.Count == 0) return;
        index = Math.Clamp(index, 0, _rows.Count - 1);
        List.SelectedIndex = index;
        List.ScrollIntoView(_rows[index]);
    }

    private int PageSize => Math.Max(1, (int)(List.ActualHeight / 48) - 1);

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh(keepSelection: false);

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                HidePopup();
                break;
            case Key.Down:
                SelectIndex(List.SelectedIndex + 1);
                break;
            case Key.Up:
                SelectIndex(List.SelectedIndex - 1);
                break;
            case Key.PageDown:
                SelectIndex(List.SelectedIndex + PageSize);
                break;
            case Key.PageUp:
                SelectIndex(List.SelectedIndex - PageSize);
                break;
            case Key.Home when !shift:
                SelectIndex(0);
                break;
            case Key.End when !shift:
                SelectIndex(_rows.Count - 1);
                break;
            case Key.Enter:
                if (Selected is { } row)
                    Choose(row.Item, shift ? ChooseMode.PastePlain : ctrl ? ChooseMode.PasteKeepOpen : ChooseMode.Paste);
                break;
            case Key.Delete when shift || (SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length):
                DeleteSelected();
                break;
            case Key.P when ctrl:
                TogglePinSelected();
                break;
            case Key.F when ctrl:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case Key.C when ctrl && SearchBox.SelectionLength == 0:
                if (Selected is { } copyRow) Choose(copyRow.Item, ChooseMode.Copy);
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                ChooseIndex(key - Key.D1, shift);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when ctrl:
                ChooseIndex(key - Key.NumPad1, shift);
                break;
            case Key.Apps:
            case Key.F10 when shift:
                OpenContextMenu(atMouse: false);
                break;
            case Key.Tab:
                break; // keep focus in the search box
            default:
                return; // let the search box handle typing
        }
        e.Handled = true;
    }

    private void ChooseIndex(int index, bool plain)
    {
        if (index < _rows.Count) Choose(_rows[index].Item, plain ? ChooseMode.PastePlain : ChooseMode.Paste);
    }

    private void Choose(ClipItem item, ChooseMode mode) => _app.Choose(item, mode);

    private void DeleteSelected()
    {
        if (Selected is not { } row) return;
        int index = List.SelectedIndex;
        _app.History.Delete(row.Item);
        Refresh(keepSelection: false);
        SelectIndex(index);
    }

    private void TogglePinSelected()
    {
        if (Selected is { } row) _app.History.TogglePin(row.Item);
    }

    // ---------------------------------------------------------------- mouse & context menu

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: RowViewModel row })
        {
            e.Handled = true;
            var mods = Keyboard.Modifiers;
            Choose(row.Item, mods.HasFlag(ModifierKeys.Shift) ? ChooseMode.PastePlain : ChooseMode.Paste);
        }
    }

    private void OnRowRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            e.Handled = true;
            OpenContextMenu(atMouse: true);
        }
    }

    private void OpenContextMenu(bool atMouse)
    {
        if (Selected is not { } row) return;
        var item = row.Item;
        var menu = new ContextMenu { PlacementTarget = atMouse ? this : (UIElement?)List.ItemContainerGenerator.ContainerFromItem(row) ?? List };
        if (atMouse) menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;

        void Add(string header, string? gesture, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        Add("Paste", "Enter", () => Choose(item, ChooseMode.Paste));
        Add("Copy to Clipboard", "Ctrl+C", () => Choose(item, ChooseMode.Copy));
        if (item.Kind != ClipKind.Image)
            Add("Paste as Plain Text", "Shift+Enter", () => Choose(item, ChooseMode.PastePlain));
        menu.Items.Add(new Separator());
        Add(item.IsPinned ? "Unpin" : "Pin", "Ctrl+P", () => _app.History.TogglePin(item));
        Add("Delete", "Del", DeleteSelected);
        Add("Delete All Similar", null, () => { _app.History.DeleteSimilar(item); Refresh(keepSelection: false); });
        menu.Items.Add(new Separator());
        Add("Show Details", null, () => _app.ShowDetails(item));

        menu.IsOpen = true;
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        HidePopup();
        _app.OpenSettings();
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Position in physical pixels on the monitor under the anchor point, kept inside its work area.</summary>
    private void Place()
    {
        if (_hwnd == IntPtr.Zero) Prepare();
        _hwnd = new WindowInteropHelper(this).Handle;

        NativeMethods.GetCursorPos(out var cursor);
        var anchor = cursor;
        bool center = false;
        switch (_app.Settings.Placement)
        {
            case PopupPlacement.ActiveWindow when PreviousWindow != IntPtr.Zero && NativeMethods.GetWindowRect(PreviousWindow, out var r):
                anchor = new NativeMethods.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
                center = true;
                break;
            case PopupPlacement.ScreenCenter:
            case PopupPlacement.ActiveWindow:
                center = true;
                break;
        }

        var monitor = NativeMethods.MonitorFromPoint(anchor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var work = info.rcWork;
        double scale = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;

        int w = Math.Min((int)(Width * scale), work.Right - work.Left);
        int h = Math.Min((int)(Height * scale), work.Bottom - work.Top);
        int x, y;
        if (center && _app.Settings.Placement == PopupPlacement.ActiveWindow)
        {
            x = anchor.X - w / 2;
            y = anchor.Y - h / 2;
        }
        else if (center)
        {
            x = (work.Left + work.Right - w) / 2;
            y = (work.Top + work.Bottom - h) / 2;
        }
        else
        {
            x = cursor.X;
            y = cursor.Y;
            if (y + h > work.Bottom) y = cursor.Y - h; // open upwards near the bottom of the screen
        }
        x = Math.Clamp(x, work.Left, work.Right - w);
        y = Math.Clamp(y, work.Top, work.Bottom - h);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }
}
