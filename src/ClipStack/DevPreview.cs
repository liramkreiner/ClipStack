using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipStack.Core;
using ClipStack.Services;
using ClipStack.UI;

namespace ClipStack;

/// <summary>
/// Developer utility (<c>ClipStack.exe --render-preview &lt;dir&gt;</c>): renders the windows with sample
/// data to PNG files, in light and dark themes, without touching the real profile or the clipboard.
/// </summary>
internal static class DevPreview
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var dataDir = Path.Combine(Path.GetTempPath(), "ClipStackPreview-" + Guid.NewGuid().ToString("N"));
        AppPaths.OverrideDataDirectory(dataDir);
        Directory.CreateDirectory(dataDir);

        var settings = new AppSettings { FirstRunCompleted = true, StartWithWindows = false, EncryptHistory = false };
        var history = new HistoryService(new HistoryStore(Path.Combine(dataDir, "history.db")), new Protector(AppPaths.KeyFile), () => settings);
        Seed(history);

        var app = new AppController();
        app.InitializeForPreview(settings, history);

        foreach (var theme in new[] { ThemeChoice.Light, ThemeChoice.Dark })
        {
            settings.Theme = theme;
            ThemeManager.Apply(Application.Current.Resources, theme);
            var name = theme.ToString().ToLowerInvariant();

            var popup = new PopupWindow(app) { Left = -20000, Top = -20000, ShowActivated = false };
            popup.ShowFor(IntPtr.Zero);
            Render(popup, Path.Combine(outDir, $"popup-{name}.png"));
            popup.SetSearchForPreview("git");
            Render(popup, Path.Combine(outDir, $"popup-search-{name}.png"));
            popup.Close();

            var settingsWindow = new SettingsWindow(app) { Left = -20000, Top = -20000, ShowActivated = false };
            settingsWindow.Show();
            foreach (var page in new[] { "General", "Privacy" })
            {
                settingsWindow.ShowPage(page);
                Render(settingsWindow, Path.Combine(outDir, $"settings-{page.ToLowerInvariant()}-{name}.png"));
            }
            settingsWindow.Close();

            var onboarding = new OnboardingWindow(app) { Left = -20000, Top = -20000, ShowActivated = false };
            onboarding.Show();
            Render(onboarding, Path.Combine(outDir, $"onboarding-{name}.png"));
            onboarding.Close();
        }

        history.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dataDir, true); } catch { /* temp */ }
    }

    private static void Seed(HistoryService h)
    {
        void Add(CapturedClip c) { h.Add(c, h.ComputeHash(c)); Thread.Sleep(3); }
        string chrome = "Google Chrome", code = "Visual Studio Code", word = "Microsoft Word";

        Add(new CapturedClip { Kind = ClipKind.Text, Text = "git log --oneline --graph --decorate --all", SourceApp = "Windows Terminal" });
        Add(new CapturedClip { Kind = ClipKind.Text, Text = "Best regards,\nLiram\nliram@example.com", SourceApp = "Outlook" });
        foreach (var pinned in h.Items.ToList()) h.TogglePin(pinned);

        Add(new CapturedClip { Kind = ClipKind.Files, Files = [@"C:\Users\Liram\Documents\report.pdf"], SourceApp = "Windows Explorer" });
        Add(new CapturedClip { Kind = ClipKind.Text, Text = "SELECT id, name FROM customers WHERE created_at > '2026-01-01' ORDER BY name;", SourceApp = code });
        Add(new CapturedClip { Kind = ClipKind.Image, ImagePng = SampleImage(640, 360), ThumbPng = SampleImage(170, 96), ImageWidth = 640, ImageHeight = 360, ImageIdentity = [1, 2, 3], SourceApp = "Snipping Tool" });
        Add(new CapturedClip { Kind = ClipKind.Text, Text = "Tomorrow's meeting is at 10:00 in room 4.\nPlease bring the Q3 numbers.", Html = "<b>x</b>", SourceApp = word });
        Add(new CapturedClip { Kind = ClipKind.Text, Text = "def calculate_total(items):\n    return sum(i.price * i.qty for i in items)", SourceApp = code });
        Add(new CapturedClip { Kind = ClipKind.Files, Files = [@"C:\a\one.txt", @"C:\a\two.png", @"C:\a\three.docx", @"C:\a\four.xlsx"], SourceApp = "Windows Explorer" });
        Add(new CapturedClip { Kind = ClipKind.Text, Text = "https://github.com/liram/clipstack/pulls", SourceApp = chrome });
    }

    private static byte[] SampleImage(int w, int h)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x46, 0x82, 0xB4), Color.FromRgb(0x1E, 0x3A, 0x5F), 45), null, new Rect(0, 0, w, h));
            dc.DrawEllipse(Brushes.Gold, null, new Point(w * 0.5, h * 0.5), h * 0.3, h * 0.3);
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return ClipboardMonitor.EncodePng(bmp);
    }

    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var root = (FrameworkElement)window.Content;
        double scale = 1.5;
        double fullW = root.ActualWidth + root.Margin.Left + root.Margin.Right;
        double fullH = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
        int w = (int)(fullW * scale), hgt = (int)(fullH * scale);
        var rtb = new RenderTargetBitmap(w, hgt, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // Paint the window background first (it isn't part of the content element).
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(window.Background is SolidColorBrush { Color.A: > 0 } b ? b : SystemBackground(window), null, new Rect(0, 0, fullW, fullH));
        rtb.Render(bg);
        rtb.Render(root);
        File.WriteAllBytes(path, ClipboardMonitor.EncodePng(rtb));
    }

    private static Brush SystemBackground(Window w) =>
        w.TryFindResource("ApplicationBackgroundBrush") as Brush ??
        (ThemeManager.IsDark(ThemeChoice.System) ? Brushes.Black : Brushes.White);
}
