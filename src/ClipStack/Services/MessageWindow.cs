using System.Windows.Interop;

namespace ClipStack.Services;

/// <summary>Hidden message-only window that receives clipboard-update and hotkey messages.</summary>
public sealed class MessageWindow : IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private readonly HwndSource _source;

    public event Func<int, IntPtr, IntPtr, bool>? Message;

    public MessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("ClipStackMessageWindow")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (Message is { } m)
        {
            foreach (Func<int, IntPtr, IntPtr, bool> h in m.GetInvocationList())
                if (h(msg, wParam, lParam)) { handled = true; break; }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
