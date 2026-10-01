using System.Windows;
using ClipStack.Services;

namespace ClipStack.UI;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow(AppController app)
    {
        ThemeMode = ThemeManager.ToThemeMode(app.Settings.Theme);
        InitializeComponent();
        ShortcutRun.Text = app.HotkeyText;
        Logo.Source = Imaging.ToImageSource(IconFactory.CreateAppBitmap(128));
    }

    private void OnGetStarted(object sender, RoutedEventArgs e) => Close();
}
