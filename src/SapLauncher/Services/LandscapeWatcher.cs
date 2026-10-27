using System.IO;

namespace SapLauncher.Services;

/// <summary>监听 SAP Logon 配置目录变化，防抖后触发静默同步。</summary>
public sealed class LandscapeWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private readonly object _gate = new();
    private bool _pending;

    public event Action? Changed;

    public void Start()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SAP", "Common");
            if (!Directory.Exists(dir)) return;

            _watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnAny;
            _watcher.Created += OnAny;
            _watcher.Deleted += OnAny;
            _watcher.Renamed += OnAny;
        }
        catch (Exception ex)
        {
            Log.Warn("配置文件监听启动失败：" + ex.Message);
        }
    }

    private void OnAny(object sender, FileSystemEventArgs e)
    {
        var name = e.Name ?? "";
        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_gate)
        {
            if (_pending) return;
            _pending = true;
        }

        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ =>
        {
            lock (_gate) { _pending = false; }
            try { Changed?.Invoke(); }
            catch (Exception ex) { Log.Warn("配置变化回调异常：" + ex.Message); }
        }, null, 800, Timeout.Infinite);
    }

    public void Dispose()
    {
        try
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnAny;
                _watcher.Created -= OnAny;
                _watcher.Deleted -= OnAny;
                _watcher.Renamed -= OnAny;
                _watcher.Dispose();
            }
        }
        catch { }
        _debounce?.Dispose();
    }
}
