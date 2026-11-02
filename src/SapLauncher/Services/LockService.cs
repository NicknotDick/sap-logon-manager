using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SapLauncher.Interop;
using SapLauncher.Services;

namespace SapLauncher.Services;

/// <summary>系统级闲置检测：离开键盘鼠标超过设定时间后自动锁定凭据库。</summary>
public sealed class LockService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private DateTime _lastLockCheck = DateTime.MinValue;

    public event Action? Locked;

    public LockService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        _lastLockCheck = DateTime.Now;
        _timer.Start();
    }

    private void Check()
    {
        var minutes = AppServices.Settings.IdleLockMinutes;
        if (minutes <= 0) return;
        if (!AppServices.Vault.IsUnlocked) return;

        var idle = NativeMethods.GetSystemIdleSeconds();
        if (idle >= minutes * 60)
        {
            AppServices.Vault.Lock();
            Log.Info($"闲置 {minutes} 分钟，凭据库已自动锁定");
            Locked?.Invoke();
        }
    }

    public void Dispose() => _timer.Stop();
}
