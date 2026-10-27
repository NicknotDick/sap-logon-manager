using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using SapLauncher.Models;
using SapLauncher.Services;
using SapLauncher.Views;

namespace SapLauncher;

/// <summary>全局服务容器（单例，进程内共享）。</summary>
public static class AppServices
{
    public static ManifestStore Manifest { get; } = new();
    public static VaultService Vault { get; } = new();
    public static ScriptService Scripts { get; private set; } = null!;
    public static LaunchService Launch { get; private set; } = null!;
    public static SapGuiInfo Gui { get; private set; } = null!;

    public static AppSettings Settings => Manifest.Data.Settings;
    public static bool VaultReady => Vault.IsUnlocked;

    public static void Initialize()
    {
        Gui = SapGuiLocator.Locate();
        Scripts = new ScriptService(Manifest.Data);
        Scripts.Refresh();
        Launch = new LaunchService(Gui);
    }

    public static void RefreshGui()
    {
        Gui = SapGuiLocator.Locate();
        Launch.Refresh();
    }
}

public partial class App : Application
{
    private SingleInstance? _single;
    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理异常", args.ExceptionObject as Exception);

        var dataDir = GetArg(e.Args, "--datadir");
        if (!string.IsNullOrWhiteSpace(dataDir)) AppPaths.OverrideRoot(dataDir!);
        else AppPaths.ConfigurePortable();

        AppPaths.EnsureAll();
        AppPaths.CleanTemp();
        Log.Purge();
        Log.Info("应用启动，参数：" + string.Join(" ", e.Args));

        StartMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        bool syncOnly = e.Args.Any(a => a.Equals("--sync", StringComparison.OrdinalIgnoreCase));

        AppServices.Manifest.Load();
        ThemeManager.Apply(AppServices.Settings.Theme);
        Localizer.Apply(AppServices.Settings.Language);
        AppServices.Initialize();

        // 自动化/测试：仅在凭据库不存在时按给定密码创建（等同首次设置，不构成后门）
        var initPassword = GetArg(e.Args, "--init-vault");
        if (!string.IsNullOrWhiteSpace(initPassword) && !AppServices.Vault.Exists)
        {
            AppServices.Vault.Initialize(initPassword!);
            AppServices.Settings.RememberMasterKey = true;
            KeyProtection.SavePassword(initPassword!);
            AppServices.Manifest.Save();
            Log.Info("已按参数创建凭据库");
        }

        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var report = SelfTest.Run();
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppPaths.Root, "selftest-latest.txt"), report, System.Text.Encoding.UTF8);
            Shutdown();
            return;
        }

        // 开发诊断：把参数里的口令填进当前可见的 SAP 登录框（验证弹框接管链路用）
        var fillArg = GetArg(e.Args, "--filldialog");
        if (!string.IsNullOrWhiteSpace(fillArg))
        {
            var parts = fillArg!.Split('|');
            var probeUser = parts.Length > 0 ? parts[0] : "";
            var probePwd = parts.Length > 1 ? parts[1] : "";
            var sb2 = new System.Text.StringBuilder();
            var gui = SapGuiLocator.Locate();
            foreach (var (dialog, title) in Interop.NativeMethods.GetVisibleDialogWindows())
            {
                sb2.AppendLine("对话框: " + title);
                var trusted = Interop.NativeMethods.IsTrustedSapWindow(dialog, gui.InstallDir, out var img, out var why);
                sb2.AppendLine($"  进程={img}");
                sb2.AppendLine($"  可信={trusted} {why}");
                var shaped = Interop.NativeMethods.LooksLikeLogonDialog(dialog, out var shape);
                sb2.AppendLine($"  结构={shaped} {shape}");
                if (shaped && trusted)
                {
                    var filled = Interop.NativeMethods.FillLogonDialog(dialog, probeUser, probePwd, out var detail);
                    sb2.AppendLine($"  填充={filled} {detail}");
                }
            }
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppPaths.Root, "filldialog.txt"), sb2.ToString(), System.Text.Encoding.UTF8);
            Shutdown();
            return;
        }

        var shotsDir = GetArg(e.Args, "--shots");
        if (!string.IsNullOrWhiteSpace(shotsDir))
        {
            if (!AppServices.Vault.IsUnlocked)
            {
                var saved = KeyProtection.LoadPassword();
                if (!string.IsNullOrEmpty(saved)) AppServices.Vault.Unlock(saved, out _);
            }
            var files = SelfTest.CaptureShots(shotsDir!);
            Log.Info("离屏渲染完成：" + string.Join("; ", files));
            Shutdown();
            return;
        }

        if (syncOnly)
        {
            RunHeadlessSync();
            Shutdown();
            return;
        }

        _single = new SingleInstance();
        if (!_single.Acquire())
        {
            // 已被占用：给出提醒，并允许结束占用进程后继续启动
            var others = OtherInstances();
            var choice = MessageBox.Show(
                "检测到「SAP 登录管理器」已经在运行（可能是上次没有正常退出的进程）。\n\n" +
                "· 是：切换到已运行的窗口\n" +
                "· 否：结束占用进程并重新启动\n" +
                "· 取消：退出",
                "SAP 登录管理器", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes)
            {
                SingleInstance.NotifyExisting("show");
                Shutdown();
                return;
            }
            if (choice != MessageBoxResult.No)
            {
                Shutdown();
                return;
            }

            KillOthers(others);
            System.Threading.Thread.Sleep(700);

            _single.Dispose();
            _single = new SingleInstance();
            if (!_single.Acquire())
            {
                MessageBox.Show("仍无法启动：占用进程可能未结束，请稍后重试或重启电脑。",
                    "SAP 登录管理器", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
        }
        _single.CommandReceived += OnExternalCommand;

        if (!EnsureVaultUnlocked())
        {
            Shutdown();
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        if (StartMinimized) main.StartHidden();
        else main.Show();
    }

    /// <summary>查找同名的其他实例进程（排除自身；framework-dependent 下的 dotnet 宿主不处理）。</summary>
    private static Process[] OtherInstances()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var name = string.IsNullOrEmpty(exe) ? "" : Path.GetFileNameWithoutExtension(exe);
            if (string.IsNullOrWhiteSpace(name) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<Process>();

            return Process.GetProcessesByName(name).Where(p => p.Id != Environment.ProcessId).ToArray();
        }
        catch
        {
            return Array.Empty<Process>();
        }
    }

    /// <summary>结束指定的占用进程。</summary>
    private static void KillOthers(Process[] procs)
    {
        foreach (var p in procs)
        {
            try
            {
                p.Kill(true);
                p.WaitForExit(1500);
            }
            catch { }
        }
    }

    private static bool EnsureVaultUnlocked()
    {
        bool needSetup = !AppServices.Vault.Exists;

        if (!needSetup)
        {
            // 仅「未启用 Windows Hello」时静默解锁；启用 Hello 时进入解锁对话框，
            // 由对话框在窗口显示后再异步弹出 Hello，避免启动阶段阻塞导致「双击无反应」。
            var saved = KeyProtection.LoadPassword();
            if (!string.IsNullOrEmpty(saved) && !AppServices.Settings.UseWindowsHello &&
                AppServices.Vault.Unlock(saved, out _))
            {
                Log.Info("使用已保存的主密码自动解锁");
                return true;
            }
        }

        var dlg = new VaultDialog(needSetup ? VaultMode.Setup : VaultMode.Unlock);
        var ok = dlg.ShowDialog() == true;
        if (!ok) Log.Info("用户取消解锁，应用退出");
        return ok;
    }

    private static void RunHeadlessSync()
    {
        try
        {
            var scan = LandscapeReader.Scan();
            var result = ListSyncService.Merge(AppServices.Manifest.Data.Systems, scan, AppServices.Settings);
            AppServices.Settings.LastSyncUtc = DateTime.UtcNow;
            AppServices.Manifest.Save();
            Log.Info("无界面同步完成：" + result.Summary);
        }
        catch (Exception ex)
        {
            Log.Error("无界面同步失败", ex);
        }
    }

    private void OnExternalCommand(string command)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow main)
            {
                if (command.Equals("search", StringComparison.OrdinalIgnoreCase)) main.ShowQuickSearch();
                else main.RestoreFromTray();
            }
        });
    }

    private static string? GetArg(string[] args, string name)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return a[(name.Length + 1)..];
        }
        return null;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("界面未处理异常", e.Exception);
        MessageBox.Show("发生未处理的错误：" + e.Exception.Message + "\n\n详细信息已写入日志。",
            "SAP 登录管理器", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppServices.Manifest.Save();
            AppPaths.CleanTemp();
            _single?.Dispose();
            Log.Info("应用退出");
        }
        catch { }
        base.OnExit(e);
    }
}
