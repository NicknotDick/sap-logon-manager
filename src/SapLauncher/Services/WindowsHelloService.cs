using Windows.Security.Credentials.UI;

namespace SapLauncher.Services;

/// <summary>
/// Windows Hello（PIN / 指纹 / 面容）验证，用于替代手输主密码。
/// 依赖 WinRT（需目标框架 net8.0-windows10.0.19041.0）。
/// </summary>
public static class WindowsHelloService
{
    /// <summary>当前设备是否已配置可用的 Windows Hello。</summary>
    public static async System.Threading.Tasks.Task<bool> IsAvailableAsync()
    {
        try
        {
            var r = await UserConsentVerifier.CheckAvailabilityAsync().AsTask().ConfigureAwait(false);
            return r == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>弹出 Windows Hello 验证；返回是否通过。</summary>
    public static async System.Threading.Tasks.Task<bool> VerifyAsync(string message)
    {
        try
        {
            var r = await UserConsentVerifier.RequestVerificationAsync(message).AsTask().ConfigureAwait(false);
            return r == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>同步封装：在后台线程等待，避免阻塞/死锁 UI 线程。</summary>
    public static bool VerifySync(string message, int timeoutMs = 60000)
    {
        try
        {
            var task = System.Threading.Tasks.Task.Run(() => VerifyAsync(message));
            return task.Wait(timeoutMs) && task.Result;
        }
        catch
        {
            return false;
        }
    }
}
