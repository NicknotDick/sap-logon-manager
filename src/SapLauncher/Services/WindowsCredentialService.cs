using System.Runtime.InteropServices;

namespace SapLauncher.Services;

/// <summary>
/// 校验「当前 Windows 账户密码」：用 advapi32!LogonUser(NETWORK) 验证。
/// 用于「忘记主密码」时的身份确认（验证通过后再用 DPAPI 保存的主密码恢复解锁）。
/// </summary>
public static class WindowsCredentialService
{
    private const int LOGON32_LOGON_NETWORK = 3;
    private const int LOGON32_PROVIDER_DEFAULT = 0;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string userName, string? domain, string password,
        int logonType, int logonProvider, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>验证当前登录用户的 Windows 密码是否正确。</summary>
    public static bool VerifyCurrentUserPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        var user = Environment.UserName;
        var domains = new[] { Environment.UserDomainName, ".", Environment.MachineName };
        foreach (var domain in domains)
        {
            if (string.IsNullOrWhiteSpace(domain)) continue;
            IntPtr token;
            if (LogonUser(user, domain, password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, out token))
            {
                CloseHandle(token);
                return true;
            }
        }
        return false;
    }
}
