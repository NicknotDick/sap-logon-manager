using System.IO;
using System.Windows;
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

    public static void Apply(string theme)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;
            var uri = new Uri(theme.Equals(Light, StringComparison.OrdinalIgnoreCase)
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
