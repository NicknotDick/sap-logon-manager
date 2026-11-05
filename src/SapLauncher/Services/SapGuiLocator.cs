using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SapLauncher.Services;

public record SapGuiInfo(
    bool IsInstalled,
    string InstallDir,
    string SapLogonPath,
    string SapShcutPath,
    string Version,
    string? LandscapePath);

/// <summary>定位 SAP GUI 安装位置与版本，并找到登录配置文件。</summary>
public static class SapGuiLocator
{
    private static readonly string[] RegistryKeys =
    {
        @"SOFTWARE\WOW6432Node\SAP\SAPGUI Front\SAP Frontend Server",
        @"SOFTWARE\SAP\SAPGUI Front\SAP Frontend Server",
        @"SOFTWARE\WOW6432Node\SAP\SAPGUI",
        @"SOFTWARE\SAP\SAPGUI",
        @"SOFTWARE\WOW6432Node\SAP\SAP Shared",
        @"SOFTWARE\SAP\SAP Shared"
    };

    private static readonly string[] PathValueNames =
    {
        "SAPguiInstallDir", "InstallDir", "InstallPath", "Path", "SAPGUIPath", "GUI", "SAPGUIPath64"
    };

    private static readonly string[] UninstallKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    /// <summary>最近一次定位实际尝试过的路径，供诊断页展示（便于排查「未检测到」）。</summary>
    public static IReadOnlyList<string> LastProbed { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 定位 SAP GUI。<paramref name="overrideDir"/> 为设置页手动指定的目录，优先使用；
    /// 未指定时按「注册表 → 卸载项 → PATH → 常见路径 → 各盘符」顺序探测。
    /// </summary>
    public static SapGuiInfo Locate(string? overrideDir = null)
    {
        var candidates = new List<string>();
        var probed = new List<string>();

        if (!string.IsNullOrWhiteSpace(overrideDir)) candidates.Add(overrideDir!.Trim().Trim('"'));

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var keyPath in RegistryKeys)
            {
                try
                {
                    using var key = root.OpenSubKey(keyPath);
                    if (key is null) continue;
                    foreach (var name in PathValueNames)
                    {
                        if (key.GetValue(name) is string v && !string.IsNullOrWhiteSpace(v))
                            candidates.Add(v);
                    }
                }
                catch { }
            }
        }

        candidates.AddRange(FromUninstallEntries());
        candidates.AddRange(FromPathEnvironment());

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SAP", "FrontEnd", "SAPgui"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SAP", "FrontEnd", "SAPgui"));
        candidates.Add(@"C:\Program Files\SAP\FrontEnd\SAPgui");
        candidates.Add(@"C:\Program Files (x86)\SAP\FrontEnd\SAPgui");
        candidates.AddRange(FromDriveRoots());

        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var dir = NormalizeDir(raw);
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (probed.Contains(dir, StringComparer.OrdinalIgnoreCase)) continue;
            probed.Add(dir);

            var logon = Path.Combine(dir, "saplogon.exe");
            var shcut = Path.Combine(dir, "sapshcut.exe");
            var hasLogon = File.Exists(logon);
            var hasShcut = File.Exists(shcut);
            if (!hasLogon && !hasShcut) continue;

            var version = "未知";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(hasLogon ? logon : shcut);
                version = FormatVersion(vi.ProductVersion ?? vi.FileVersion ?? "未知");
            }
            catch { }

            LastProbed = probed;
            return new SapGuiInfo(
                true,
                dir,
                hasLogon ? logon : "",
                hasShcut ? shcut : "",
                version,
                FindLandscape());
        }

        LastProbed = probed;
        return new SapGuiInfo(false, "", "", "", "未检测到", FindLandscape());
    }

    /// <summary>把 InstallLocation/DisplayIcon 之类的值归一化成目录。</summary>
    private static string NormalizeDir(string raw)
    {
        var s = raw.Trim().Trim('"');
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var idx = s.LastIndexOf(',');
            if (idx > 0) s = s[..idx];
            try { s = Path.GetDirectoryName(s.Trim().Trim('"')) ?? ""; } catch { return ""; }
        }
        s = Environment.ExpandEnvironmentVariables(s);
        return s;
    }

    /// <summary>扫描卸载项：DisplayName 含 SAP GUI / SAP Front End 的安装位置。</summary>
    private static IEnumerable<string> FromUninstallEntries()
    {
        var found = new List<string>();
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var keyPath in UninstallKeys)
            {
                try
                {
                    using var uninstall = root.OpenSubKey(keyPath);
                    if (uninstall is null) continue;
                    foreach (var sub in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var app = uninstall.OpenSubKey(sub);
                            if (app is null) continue;
                            var name = app.GetValue("DisplayName") as string ?? "";
                            if (!IsSapGuiProduct(name)) continue;

                            if (app.GetValue("InstallLocation") is string loc && !string.IsNullOrWhiteSpace(loc))
                                found.Add(loc);
                            if (app.GetValue("DisplayIcon") is string icon && !string.IsNullOrWhiteSpace(icon))
                                found.Add(icon);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        return found;
    }

    private static bool IsSapGuiProduct(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;
        var n = displayName.ToUpperInvariant();
        if (n.Contains("SAP GUI FOR JAVA")) return false;
        return n.Contains("SAP GUI") || n.Contains("SAP FRONT END") || n.Contains("SAP FRONTEND");
    }

    /// <summary>从 PATH 环境变量里找 SAP GUI 目录（部分环境未写注册表）。</summary>
    private static IEnumerable<string> FromPathEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = part.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (!dir.Contains("SAP", StringComparison.OrdinalIgnoreCase)) continue;
            yield return dir;
        }

        var env = Environment.GetEnvironmentVariable("SAPGUI_DIR");
        if (!string.IsNullOrWhiteSpace(env)) yield return env!;
    }

    /// <summary>各盘符下的常见安装位置（应对装在 D:/E: 等非系统盘的情况）。</summary>
    private static IEnumerable<string> FromDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                roots.Add(d.RootDirectory.FullName);
            }
        }
        catch { }

        foreach (var r in roots)
        {
            yield return Path.Combine(r, "SAP", "FrontEnd", "SAPgui");
            yield return Path.Combine(r, "Program Files", "SAP", "FrontEnd", "SAPgui");
            yield return Path.Combine(r, "Program Files (x86)", "SAP", "FrontEnd", "SAPgui");
        }
    }

    /// <summary>把 "8000.1.15.155" / "800 Final Release - 64bit" 归一化为 "SAP GUI 8.0"。</summary>
    private static string FormatVersion(string raw)
    {
        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length >= 3 && int.TryParse(digits, out var n))
        {
            double major, minor;
            if (digits.Length >= 4)
            {
                major = n / 1000;
                minor = (n % 1000) / 10.0;
            }
            else
            {
                major = n / 100;
                minor = n % 100;
            }
            return $"SAP GUI {major}.{minor:0.##}";
        }
        return raw;
    }

    /// <summary>返回主配置文件路径（新版 XML 优先，旧版 ini 兜底）。</summary>
    public static string? FindLandscape()
    {
        foreach (var p in LandscapeCandidates())
            if (File.Exists(p)) return p;
        return null;
    }

    public static IEnumerable<string> LandscapeCandidates()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        yield return Path.Combine(appData, "SAP", "Common", "SAPUILandscape.xml");
        yield return Path.Combine(common, "SAP", "Common", "SAPUILandscape.xml");
        yield return Path.Combine(appData, "SAP", "Common", "SAPUILandscapeGlobal.xml");
        yield return Path.Combine(common, "SAP", "Common", "SAPUILandscapeGlobal.xml");
        yield return Path.Combine(win, "saplogon.ini");
        yield return Path.Combine(appData, "SAP", "Common", "saplogon.ini");
    }
}
