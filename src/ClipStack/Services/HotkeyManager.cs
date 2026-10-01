using System.Windows.Input;
using ClipStack.Native;

namespace ClipStack.Services;

public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    /// <summary>Compact form used in settings.json, e.g. "Ctrl+Shift+V".</summary>
    public string ToSetting() => ToString().Replace(" + ", "+");

    private static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        Key.OemTilde => "`",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.Space => "Space",
        _ => k.ToString(),
    };

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                case "`": key = Key.OemTilde; break;
                case ".": key = Key.OemPeriod; break;
                case ",": key = Key.OemComma; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Key>(raw, true, out var k)) key = k;
                    else return false;
                    break;
            }
        }
        if (key is null || mods == ModifierKeys.None) return false;
        hotkey = new Hotkey(mods, key.Value);
        return true;
    }
}

/// <summary>Registers one system-wide hotkey via RegisterHotKey.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xC11B;
    private readonly MessageWindow _window;
    private bool _registered;

    public event Action? Pressed;
    public Hotkey? Current { get; private set; }

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    /// <summary>Register the hotkey; returns false if another application already owns it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        if (!Native.NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, ToNative(hotkey.Modifiers) | NativeMethods.MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
            return false;
        _registered = true;
        Current = hotkey;
        return true;
    }

    /// <summary>Check whether a hotkey is free without keeping it.</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        if (Current == hotkey) return true;
        const int probeId = HotkeyId + 1;
        if (!NativeMethods.RegisterHotKey(_window.Handle, probeId, ToNative(hotkey.Modifiers), (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
            return false;
        NativeMethods.UnregisterHotKey(_window.Handle, probeId);
        return true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY || wParam.ToInt32() != HotkeyId) return false;
        Pressed?.Invoke();
        return true;
    }

    private static uint ToNative(ModifierKeys m)
    {
        uint r = 0;
        if (m.HasFlag(ModifierKeys.Alt)) r |= NativeMethods.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Control)) r |= NativeMethods.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Shift)) r |= NativeMethods.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) r |= NativeMethods.MOD_WIN;
        return r;
    }

    public void Dispose()
    {
        Unregister();
        _window.Message -= OnMessage;
    }
}
