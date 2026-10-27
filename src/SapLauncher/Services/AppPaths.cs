using System.IO;

namespace SapLauncher.Services;

/// <summary>应用数据目录（绿色 EXE，数据保存在当前用户目录，权限隔离）。</summary>
public static class AppPaths
{
    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SapLauncher");

    public static string Root { get; private set; } = DefaultRoot;

    /// <summary>测试/便携场景可切换数据根目录（须在首次读写前调用）。</summary>
    public static void OverrideRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        Root = Path.GetFullPath(root);
    }

    /// <summary>当前是否处于便携模式（数据存放在 exe 同目录的 data\ 下）。</summary>
    public static bool IsPortable { get; private set; }

    /// <summary>
    /// 便携模式：若 exe 同目录存在 <c>portable.marker</c> 文件，或 <c>data\manifest.json</c> 已存在，
    /// 则把数据根切到 exe 旁的 <c>data\</c>。这样「拷贝整个程序文件夹到另一台电脑」即可带走全部数据。
    /// 目录不可写（如放在 Program Files）时自动退回默认的 %LOCALAPPDATA%。
    /// </summary>
    public static void ConfigurePortable()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(baseDir, "data");
            var marker = Path.Combine(baseDir, "portable.marker");
            bool want = Directory.Exists(dataDir) || File.Exists(marker);
            if (!want) return;

            Directory.CreateDirectory(dataDir);
            var probe = Path.Combine(dataDir, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);

            Root = dataDir;
            IsPortable = true;
        }
        catch
        {
            IsPortable = false; // 不可写 → 保持默认
        }
    }

    public static string Manifest => Path.Combine(Root, "manifest.json");
    public static string Vault => Path.Combine(Root, "vault.dat");
    public static string AutoLogin => Path.Combine(Root, "autologin.bin");
    public static string Temp => Path.Combine(Root, "tmp");
    public static string Scripts => Path.Combine(Root, "scripts");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Backups => Path.Combine(Root, "backup");

    public static void EnsureAll()
    {
        foreach (var dir in new[] { Root, Temp, Scripts, Logs, Backups })
            Directory.CreateDirectory(dir);
    }

    public static void CleanTemp()
    {
        try
        {
            if (!Directory.Exists(Temp)) return;
            foreach (var f in Directory.GetFiles(Temp, "*.sap"))
            {
                try { File.Delete(f); } catch { /* 被占用则留待下次 */ }
            }
        }
        catch { /* 清理失败不影响主流程 */ }
    }
}

/// <summary>轻量日志：按日轮转，保留 7 天。绝不写入凭据。</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var file = Path.Combine(AppPaths.Logs, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败不影响功能 */ }
    }

    public static void Purge()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Logs)) return;
            var limit = DateTime.Now.AddDays(-7);
            foreach (var f in Directory.GetFiles(AppPaths.Logs, "*.log"))
                if (File.GetLastWriteTime(f) < limit) File.Delete(f);
        }
        catch { }
    }
}
