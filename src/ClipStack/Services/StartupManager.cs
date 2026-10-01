using Microsoft.Win32;
using ClipStack.Core;

namespace ClipStack.Services;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppPaths.AppName) is string v && v.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(AppPaths.AppName, Command);
            else if (key.GetValue(AppPaths.AppName) is not null) key.DeleteValue(AppPaths.AppName);
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the Windows startup entry", ex);
        }
    }
}
