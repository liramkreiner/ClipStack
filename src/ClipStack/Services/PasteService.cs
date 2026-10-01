using ClipStack.Core;
using ClipStack.Native;

namespace ClipStack.Services;

/// <summary>Returns focus to the window the user was working in and sends Ctrl+V to it.</summary>
public static class PasteService
{
    /// <summary>
    /// Must be called while our popup is still the foreground window (Windows only lets the
    /// foreground process hand focus to another window).
    /// </summary>
    public static bool RestoreFocus(IntPtr target) =>
        target != IntPtr.Zero && NativeMethods.ForceForeground(target);

    public static async Task PasteIntoAsync(IntPtr target)
    {
        // The user may still be holding modifiers (e.g. Shift from Shift+Enter). Combined with our
        // Ctrl+V they'd form a different shortcut, so wait for them to be released.
        var deadline = DateTime.UtcNow.AddMilliseconds(1500);
        while (NativeMethods.AnyModifierDown() && DateTime.UtcNow < deadline)
            await Task.Delay(15);

        for (int i = 0; i < 30 && target != IntPtr.Zero && NativeMethods.GetForegroundWindow() != target; i++)
        {
            if (i == 10) NativeMethods.ForceForeground(target);
            await Task.Delay(10);
        }

        if (target != IntPtr.Zero && NativeMethods.GetForegroundWindow() != target)
            Log.Info("Target window did not regain focus; pasting into the current foreground window");

        // Let the target finish processing its activation before the keystrokes arrive.
        await Task.Delay(30);
        NativeMethods.SendCtrlV();
    }
}
