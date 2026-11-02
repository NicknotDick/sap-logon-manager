using System.IO;

namespace SapLauncher.Services;

/// <summary>
/// 「切换为便携模式」：把当前数据（清单/凭据库/自动解锁/脚本/备份）复制到 exe 同目录的 data\，
/// 之后重启程序即以便携模式运行（ConfigurePortable 会识别 data\）。
/// </summary>
public static class PortableService
{
    public static string TargetDir => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>成功返回 true，并在 message 中给出目标目录；失败时 message 为原因。</summary>
    public static bool TrySwitch(out string message)
    {
        if (AppPaths.IsPortable)
        {
            message = "当前已是便携模式。";
            return false;
        }

        var target = TargetDir;
        try
        {
            Directory.CreateDirectory(target);
            var probe = Path.Combine(target, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            message = "程序所在目录不可写（可能放在 Program Files），请把程序移到可写目录再试。\n" + ex.Message;
            return false;
        }

        try
        {
            AppServices.Manifest.Save(); // 先落盘，确保复制的是最新数据
            var src = AppPaths.Root;

            foreach (var f in new[] { "manifest.json", "vault.dat", "autologin.bin" })
            {
                var s = Path.Combine(src, f);
                if (File.Exists(s)) File.Copy(s, Path.Combine(target, f), true);
            }

            foreach (var d in new[] { "scripts", "backup" })
            {
                var sd = Path.Combine(src, d);
                if (Directory.Exists(sd)) CopyDir(sd, Path.Combine(target, d));
            }

            message = target;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
