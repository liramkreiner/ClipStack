using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ClipStack.Core;

public static class AppPaths
{
    public const string AppName = "ClipStack";

    public static string DataDirectory { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string KeyFile => Path.Combine(DataDirectory, "hash.key");
    public static string LogFile => Path.Combine(DataDirectory, "clipstack.log");

    public static string DatabaseFile(AppSettings s) =>
        Path.Combine(string.IsNullOrWhiteSpace(s.DatabaseDirectory) ? DataDirectory : s.DatabaseDirectory!, "history.db");

    /// <summary>Used by tests to redirect all storage.</summary>
    internal static void OverrideDataDirectory(string dir) => DataDirectory = dir;

    /// <summary>
    /// Create the directory and restrict it to the current user (plus SYSTEM), with no inherited ACEs,
    /// so other accounts on the machine cannot read the clipboard history.
    /// </summary>
    public static void EnsurePrivateDirectory(string dir)
    {
        var info = Directory.CreateDirectory(dir);
        try
        {
            var user = WindowsIdentity.GetCurrent().User!;
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not restrict permissions on {dir}", ex);
        }
    }
}
