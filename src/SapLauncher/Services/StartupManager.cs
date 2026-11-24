using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SapLauncher.Services;

/// <summary>开机自启：HKCU Run 键，无需管理员权限。</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SapLauncher";

    private static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path!;
            return Path.Combine(AppContext.BaseDirectory, "SapLauncher.exe");
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s && s.Contains("--minimized", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\" --minimized");
            else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, false);
            Log.Info("开机自启：" + (enabled ? "开启" : "关闭"));
        }
        catch (Exception ex)
        {
            Log.Warn("设置开机自启失败：" + ex.Message);
        }
    }

    /// <summary>创建桌面快捷方式（绿色版可选动作）。</summary>
    public static bool CreateDesktopShortcut(out string message)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "SAP 登录管理器.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) { message = "系统不支持创建快捷方式"; return false; }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = ExecutablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(ExecutablePath) ?? "";
            shortcut.IconLocation = ExecutablePath + ",0";
            shortcut.Description = "SAP 登录管理器";
            shortcut.Save();

            message = "已创建桌面快捷方式";
            return true;
        }
        catch (Exception ex)
        {
            message = "创建快捷方式失败：" + ex.Message;
            return false;
        }
    }
}

public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    /// <summary>系统当前是否使用浅色应用主题（注册表 AppsUseLightTheme：1=浅色，0=深色）。</summary>
    private static bool SystemUsesLight
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
            }
            catch { }
            return false; // 读不到（异常/旧系统）时按深色处理，与历史默认一致
        }
    }

    /// <summary>把设置值（Dark/Light/System）解析为实际主题 Dark/Light。</summary>
    public static string Resolve(string theme)
    {
        if (theme.Equals(System, StringComparison.OrdinalIgnoreCase))
            return SystemUsesLight ? Light : Dark;
        return theme.Equals(Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;
    }

    /// <summary>当前生效主题（已解析 System）。</summary>
    public static string Effective => Resolve(AppServices.Settings.Theme);

    public static void Apply(string theme)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;
            var uri = new Uri(Resolve(theme).Equals(Light, StringComparison.Ordinal)
                ? "Themes/Light.xaml"
                : "Themes/Dark.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };

            if (app.Resources.MergedDictionaries.Count > 0)
                app.Resources.MergedDictionaries[0] = dict;
            else
                app.Resources.MergedDictionaries.Add(dict);
        }
        catch (Exception ex)
        {
            Log.Warn("切换主题失败：" + ex.Message);
        }
    }
}

/// <summary>
/// 系统主题变化监听：Windows 在深浅色切换时广播 WM_SETTINGCHANGE（lParam 指向 "ImmersiveColorSet"）。
/// 仅当设置值为「跟随系统」时重新应用主题并刷新所有已打开窗口的标题栏。
/// </summary>
public static class SystemThemeWatcher
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private static HwndSourceHook? _hook;
    private static bool _attached;

    /// <summary>在主窗口句柄建立后挂接消息钩子（仅需挂一次）。</summary>
    public static void Attach(Window window)
    {
        if (_attached) return;
        var source = (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle));
        if (source is null) return;

        _hook = Hook;
        source.AddHook(_hook);
        _attached = true;
        window.Closed += (_, _) => Detach(source);
    }

    private static void Detach(HwndSource source)
    {
        if (_hook != null) source.RemoveHook(_hook);
        _attached = false;
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_SETTINGCHANGE) return IntPtr.Zero;
        try
        {
            var param = Marshal.PtrToStringAuto(lParam);
            if (!string.Equals(param, "ImmersiveColorSet", StringComparison.Ordinal))
                return IntPtr.Zero;

            if (!AppServices.Settings.Theme.Equals(ThemeManager.System, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero; // 手动指定深/浅色：不跟随系统

            ThemeManager.Apply(ThemeManager.System);
            foreach (Window w in Application.Current.Windows)
                WindowTheme.Refresh(w);
        }
        catch (Exception ex)
        {
            Log.Warn("响应系统主题变化失败：" + ex.Message);
        }
        return IntPtr.Zero;
    }
}

public static class Localizer
{
    public static string Current { get; private set; } = "zh-CN";

    public static void Apply(string language)
    {
        try
        {
            Current = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
            var app = Application.Current;
            if (app is null) return;

            var uri = new Uri($"Resources/Strings.{Current}.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };

            var merged = app.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d =>
                d.Source is not null && d.Source.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase));

            if (existing is not null) merged[merged.IndexOf(existing)] = dict;
            else merged.Add(dict);
        }
        catch (Exception ex)
        {
            Log.Warn("切换语言失败：" + ex.Message);
        }
    }

    public static string Get(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;
}
