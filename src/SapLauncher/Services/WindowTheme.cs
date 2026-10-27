using System.Windows;
using System.Windows.Interop;
using SapLauncher.Interop;

namespace SapLauncher.Services;

public static class WindowTheme
{
    private static bool IsLight
        => string.Equals(AppServices.Settings.Theme, ThemeManager.Light, StringComparison.OrdinalIgnoreCase);

    /// <summary>按当前主题设置标题栏深浅（Windows 10 1809+ 支持）。</summary>
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) => Refresh(window);
    }

    /// <summary>主题切换后刷新标题栏。</summary>
    public static void Refresh(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            NativeMethods.ApplyDarkTitleBar(hwnd, !IsLight);
        }
        catch { }
    }
}
