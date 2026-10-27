using System.IO;
using System.Text;
using SapLauncher.Interop;

namespace SapLauncher.Services;

/// <summary>「记住主密码」：用 Windows DPAPI（当前用户 + 应用熵）封装，跨账户/跨机器不可解。</summary>
public static class KeyProtection
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SAPKEY01");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SapLauncher.autologin.v1");

    public static bool Exists => File.Exists(AppPaths.AutoLogin);

    public static void SavePassword(string password)
    {
        try
        {
            AppPaths.EnsureAll();
            var raw = Encoding.UTF8.GetBytes(password);
            var blob = NativeMethods.DpapiProtect(raw, Entropy);
            Array.Clear(raw);

            using var ms = new MemoryStream();
            ms.Write(Magic);
            ms.Write(blob);
            File.WriteAllBytes(AppPaths.AutoLogin, ms.ToArray());
        }
        catch (Exception ex)
        {
            Log.Warn("保存自动解锁密钥失败：" + ex.Message);
        }
    }

    public static string? LoadPassword()
    {
        try
        {
            if (!File.Exists(AppPaths.AutoLogin)) return null;
            var data = File.ReadAllBytes(AppPaths.AutoLogin);
            if (data.Length <= Magic.Length) return null;
            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return null;

            var blob = data[Magic.Length..];
            var raw = NativeMethods.DpapiUnprotect(blob, Entropy);
            if (raw is null) return null;
            var pwd = Encoding.UTF8.GetString(raw);
            Array.Clear(raw);
            return pwd;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(AppPaths.AutoLogin)) File.Delete(AppPaths.AutoLogin);
        }
        catch (Exception ex)
        {
            Log.Warn("删除自动解锁文件失败：" + ex.Message);
        }
    }
}
