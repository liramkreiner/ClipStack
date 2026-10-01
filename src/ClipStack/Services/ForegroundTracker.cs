using System.Runtime.InteropServices;
using System.Text;
using ClipStack.Native;

namespace ClipStack.Services;

/// <summary>
/// Remembers the last foreground window that belongs to another application (not the taskbar/tray),
/// so opening the popup from the tray icon can still paste into the app the user was using.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "Progman", "WorkerW",
    };

    private readonly WinEventProc _proc; // keep the delegate alive
    private readonly IntPtr _hook;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public IntPtr LastExternalWindow { get; private set; }

    public ForegroundTracker()
    {
        _proc = OnForeground;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        OnForeground(IntPtr.Zero, 0, NativeMethods.GetForegroundWindow(), 0, 0, 0, 0);
    }

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid) return;
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        if (ShellClasses.Contains(sb.ToString())) return;
        LastExternalWindow = hwnd;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }
}
