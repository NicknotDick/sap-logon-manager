using System.Drawing;
using System.Windows.Forms;
using SapLauncher.Services;

namespace SapLauncher.Services;

/// <summary>托盘图标与菜单。</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? ShowRequested;
    public event Action? SearchRequested;
    public event Action? SyncRequested;
    public event Action? LockRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        var show = new ToolStripMenuItem(Localizer.Get("App.Title")) { Font = new Font("Microsoft YaHei UI", 9f, System.Drawing.FontStyle.Bold) };
        show.Click += (_, _) => ShowRequested?.Invoke();
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());

        var search = new ToolStripMenuItem("快速搜索");
        search.Click += (_, _) => SearchRequested?.Invoke();
        menu.Items.Add(search);

        var sync = new ToolStripMenuItem(Localizer.Get("Btn.Sync"));
        sync.Click += (_, _) => SyncRequested?.Invoke();
        menu.Items.Add(sync);

        var lockItem = new ToolStripMenuItem("锁定");
        lockItem.Click += (_, _) => LockRequested?.Invoke();
        menu.Items.Add(lockItem);
        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        Icon? icon = null;
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path)) icon = Icon.ExtractAssociatedIcon(path!);
        }
        catch { }

        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = icon ?? SystemIcons.Application,
            Text = Localizer.Get("App.Title"),
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            _icon.ShowBalloonTip(3000);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
    }
}
