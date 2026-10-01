using System.Windows;
using ClipStack.Core;
using ClipStack.Services;

namespace ClipStack.UI;

public partial class DetailsWindow : Window
{
    private readonly AppController _app;
    private readonly ClipItem _item;

    public DetailsWindow(AppController app, ClipItem item)
    {
        _app = app;
        _item = item;
        ThemeMode = ThemeManager.ToThemeMode(app.Settings.Theme);
        InitializeComponent();

        Heading.Text = item.Kind switch
        {
            ClipKind.Image => "Image",
            ClipKind.Files => item.Files is { Length: 1 } ? "File" : $"{item.Files?.Length ?? 0} files",
            _ when item.IsUrl => "Link",
            _ => item.HasRichText ? "Formatted text" : "Text",
        };

        var meta = new List<string>();
        if (item.SourceApp is not null) meta.Add($"Copied from {item.SourceApp}");
        meta.Add($"First copied {item.CreatedAt:g}");
        if (item.UpdatedAt != item.CreatedAt) meta.Add($"last {item.UpdatedAt:g}");
        if (item.CopyCount > 1) meta.Add($"copied {item.CopyCount}×");
        if (item.UseCount > 0) meta.Add($"pasted {item.UseCount}×");
        switch (item.Kind)
        {
            case ClipKind.Text:
                meta.Add($"{item.Text?.Length ?? 0:N0} characters");
                break;
            case ClipKind.Image:
                meta.Add($"{item.ImageWidth} × {item.ImageHeight} px");
                break;
        }
        Meta.Text = string.Join("  ·  ", meta);

        if (item.Kind == ClipKind.Image)
        {
            ContentText.Visibility = Visibility.Collapsed;
            ImageScroller.Visibility = Visibility.Visible;
            if (app.History.LoadImage(item) is { } png) ContentImage.Source = Imaging.FromPng(png);
        }
        else
        {
            ContentText.Text = item.PlainText;
        }

        CopyPlainButton.Visibility = item.HasRichText || item.Kind == ClipKind.Files ? Visibility.Visible : Visibility.Collapsed;
        UpdatePinButton();
    }

    private void UpdatePinButton() => PinButton.Content = _item.IsPinned ? "Unpin" : "Pin";

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _app.History.TogglePin(_item);
        UpdatePinButton();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        _app.History.Delete(_item);
        Close();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        _app.CopyToClipboard(_item);
        Close();
    }

    private void OnCopyPlain(object sender, RoutedEventArgs e)
    {
        _app.CopyToClipboard(_item, plain: true);
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
