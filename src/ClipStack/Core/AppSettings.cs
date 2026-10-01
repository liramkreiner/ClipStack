using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipStack.Core;

public enum ThemeChoice { System, Light, Dark }
public enum PopupPlacement { Cursor, ActiveWindow, ScreenCenter }

/// <summary>Age after which unpinned history is deleted automatically.</summary>
public enum CleanupAge { Never, OneHour, OneDay, SevenDays, ThirtyDays }

public sealed class AppSettings
{
    // General
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public bool StartWithWindows { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public PopupPlacement Placement { get; set; } = PopupPlacement.Cursor;
    /// <summary>When false, choosing an item only copies it to the clipboard.</summary>
    public bool PasteOnSelect { get; set; } = true;

    // History
    /// <summary>0 = unlimited.</summary>
    public int MaxItems { get; set; } = 1000;
    public CleanupAge AutoCleanup { get; set; } = CleanupAge.Never;
    public bool CleanupIncludesPinned { get; set; }
    public bool AllowDuplicates { get; set; }
    public bool TrackUsage { get; set; } = true;

    // Privacy
    public List<string> ExcludedApps { get; set; } = new(DefaultExcludedApps);
    public bool IgnoreSensitive { get; set; } = true;
    public bool EncryptHistory { get; set; } = true;
    public bool StoreImages { get; set; } = true;
    public bool StoreFiles { get; set; } = true;
    public bool StoreRichText { get; set; } = true;

    // Appearance
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public double PopupWidth { get; set; } = 440;
    public double PopupHeight { get; set; } = 520;
    public int PreviewLength { get; set; } = 120;
    public bool ShowTimestamps { get; set; } = true;
    public bool ShowAppIcons { get; set; } = true;

    // Advanced
    /// <summary>Folder holding history.db; null means the default app data folder.</summary>
    public string? DatabaseDirectory { get; set; }
    public bool EnableLogging { get; set; }

    // State
    public bool Paused { get; set; }
    public bool FirstRunCompleted { get; set; }

    public static readonly string[] DefaultExcludedApps =
    [
        "1Password.exe", "KeePass.exe", "KeePassXC.exe", "Bitwarden.exe", "Dashlane.exe",
        "LastPass.exe", "Enpass.exe", "RoboForm.exe", "Keeper.exe", "NordPass.exe",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read settings; using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static TimeSpan? ToTimeSpan(CleanupAge age) => age switch
    {
        CleanupAge.OneHour => TimeSpan.FromHours(1),
        CleanupAge.OneDay => TimeSpan.FromDays(1),
        CleanupAge.SevenDays => TimeSpan.FromDays(7),
        CleanupAge.ThirtyDays => TimeSpan.FromDays(30),
        _ => null,
    };
}
