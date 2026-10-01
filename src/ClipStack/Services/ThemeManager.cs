using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ClipStack.Core;

namespace ClipStack.Services;

/// <summary>Light/dark palette for the popup, following the Windows app theme and accent color by default.</summary>
public static class ThemeManager
{
    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static bool IsDark(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dark => true,
        ThemeChoice.Light => false,
        _ => SystemUsesDarkTheme(),
    };

    public static Color AccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                return c;
            }
        }
        catch { /* fall through */ }
        return Color.FromRgb(0x00, 0x67, 0xC0);
    }

#pragma warning disable WPF0001
    public static ThemeMode ToThemeMode(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dark => ThemeMode.Dark,
        ThemeChoice.Light => ThemeMode.Light,
        _ => ThemeMode.System,
    };
#pragma warning restore WPF0001

    public static void Apply(ResourceDictionary resources, ThemeChoice choice)
    {
        bool dark = IsDark(choice);
        var accent = AccentColor();

        void Set(string key, Color c) => resources[key] = Freeze(new SolidColorBrush(c));

        if (dark)
        {
            Set("Popup.Background", Color.FromRgb(0x20, 0x20, 0x20));
            Set("Popup.Border", Color.FromArgb(0x66, 0x5A, 0x5A, 0x5A));
            Set("Popup.Foreground", Color.FromRgb(0xF2, 0xF2, 0xF2));
            Set("Popup.Muted", Color.FromRgb(0x9E, 0x9E, 0x9E));
            Set("Popup.Hover", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            Set("Popup.Selected", Color.FromArgb(0x55, accent.R, accent.G, accent.B));
            Set("Popup.SearchBackground", Color.FromRgb(0x2B, 0x2B, 0x2B));
            Set("Popup.Separator", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            Set("Popup.Badge", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            Set("Popup.Background", Color.FromRgb(0xF9, 0xF9, 0xF9));
            Set("Popup.Border", Color.FromArgb(0x33, 0x00, 0x00, 0x00));
            Set("Popup.Foreground", Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set("Popup.Muted", Color.FromRgb(0x6B, 0x6B, 0x6B));
            Set("Popup.Hover", Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
            Set("Popup.Selected", Color.FromArgb(0x38, accent.R, accent.G, accent.B));
            Set("Popup.SearchBackground", Colors.White);
            Set("Popup.Separator", Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            Set("Popup.Badge", Color.FromArgb(0x10, 0x00, 0x00, 0x00));
        }
        Set("Popup.Accent", accent);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
