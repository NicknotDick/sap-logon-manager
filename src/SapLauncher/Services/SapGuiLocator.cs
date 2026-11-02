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
        @"SOFTWARE\SAP\SAPGUI"
    };

    private static readonly string[] PathValueNames =
    {
        "SAPguiInstallDir", "InstallDir", "InstallPath", "Path", "SAPGUIPath", "GUI"
    };

    public static SapGuiInfo Locate()
    {
        var candidates = new List<string>();

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

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SAP", "FrontEnd", "SAPgui"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SAP", "FrontEnd", "SAPgui"));
        candidates.Add(@"C:\Program Files\SAP\FrontEnd\SAPgui");
        candidates.Add(@"C:\Program Files (x86)\SAP\FrontEnd\SAPgui");

        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var logon = Path.Combine(dir, "saplogon.exe");
            if (!File.Exists(logon)) continue;

            var shcut = Path.Combine(dir, "sapshcut.exe");
            var version = "未知";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(logon);
                version = FormatVersion(vi.ProductVersion ?? vi.FileVersion ?? "未知");
            }
            catch { }

            return new SapGuiInfo(true, dir, logon, File.Exists(shcut) ? shcut : "", version, FindLandscape());
        }

        return new SapGuiInfo(false, "", "", "", "未检测到", FindLandscape());
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
