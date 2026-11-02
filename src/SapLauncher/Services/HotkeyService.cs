using System.Windows;
using System.Windows.Interop;
using SapLauncher.Interop;

namespace SapLauncher.Services;

/// <summary>全局热键（默认 Ctrl+Alt+S），注册失败时不影响主功能。</summary>
public sealed class HotkeyService : IDisposable
{
    private HwndSource? _source;
    private readonly int _id = 0x5A01;
    private bool _registered;

    public event Action? Pressed;

    public bool TryRegister(Window window, string gesture, out string error)
    {
        error = "";
        try
        {
            _source = PresentationSource.FromVisual(window) as HwndSource;
            if (_source is null)
            {
                error = "窗口尚未完成初始化";
                return false;
            }

            if (!TryParse(gesture, out var mods, out var vk))
            {
                error = "无法识别快捷键：" + gesture;
                return false;
            }

            if (!NativeMethods.RegisterHotKey(_source.Handle, _id, mods, vk))
            {
                error = $"快捷键 {gesture} 已被其它程序占用";
                return false;
            }

            _source.AddHook(Hook);
            _registered = true;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        var key = parts[^1].ToUpperInvariant();
        if (key.Length == 1)
        {
            var ch = key[0];
            if (char.IsLetterOrDigit(ch))
            {
                virtualKey = ch;
                return modifiers != 0;
            }
        }

        if (key.StartsWith('F') && int.TryParse(key[1..], out var fn) && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1);
            return modifiers != 0;
        }

        virtualKey = key switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "HOME" => 0x24,
            "END" => 0x23,
            _ => 0
        };
        return virtualKey != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        try
        {
            if (_registered && _source is not null)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, _id);
                _source.RemoveHook(Hook);
            }
        }
        catch { }
        _registered = false;
        _source = null;
    }
}
