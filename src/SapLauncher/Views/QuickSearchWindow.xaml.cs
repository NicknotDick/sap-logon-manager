using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class QuickSearchWindow : Window
{
    private readonly List<SapSystem> _all;
    private readonly Func<SapSystem, Task> _onLogin;

    /// <summary>
    /// 关闭流程标志。WPF 在 Window.Close() 执行期间（_isClosing=true）禁止
    /// 再次调用 Close/Show/ShowDialog/EnsureHandle，否则抛 InvalidOperationException。
    /// </summary>
    private bool _closing;

    /// <summary>防止 Enter 连击 / 双击与回车叠加导致重复登录。</summary>
    private bool _loginFired;

    public QuickSearchWindow(IReadOnlyList<SapSystem> systems, Func<SapSystem, Task> onLogin)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _all = systems.ToList();
        _onLogin = onLogin;

        Loaded += (_, _) =>
        {
            SearchInput.Text = "";
            Apply("");
            SearchInput.Focus();
            Keyboard.Focus(SearchInput);
        };

        // 失活时不要在窗口过程内同步 Close：Close() 自身会触发 Deactivated，
        // 若处理器直接再调 Close() 就是本次报错的根因。改为排队到下一轮消息循环。
        Deactivated += (_, _) => RequestCloseDeferred();
        Closed += (_, _) => _closing = true;
        SearchInput.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void Apply(string key)
    {
        var list = string.IsNullOrWhiteSpace(key)
            ? _all.OrderByDescending(s => s.Favorite).ThenBy(s => s.SortOrder).ToList()
            : _all.Where(s => s.DisplayName.Contains(key, StringComparison.CurrentCultureIgnoreCase)
                              || s.Sid.Contains(key, StringComparison.OrdinalIgnoreCase)
                              || (s.Alias?.Contains(key, StringComparison.CurrentCultureIgnoreCase) ?? false))
                .OrderByDescending(s => s.Favorite).ThenBy(s => s.SortOrder).ToList();

        Results.ItemsSource = list;
        if (list.Count > 0) Results.SelectedIndex = 0;
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Apply(SearchInput.Text);
    }

    /// <summary>统一关闭入口：可重入，重复调用只生效一次。</summary>
    internal void RequestClose()
    {
        if (_closing) return;
        _closing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException ex)
        {
            // 窗口已进入 closing 时不再强行操作；窗口会自行完成关闭
            Log.Warn("快速搜索窗口关闭请求被忽略：" + ex.Message);
        }
    }

    /// <summary>失活事件延后处理，且回调时再校验一次窗口状态。</summary>
    private void RequestCloseDeferred()
    {
        if (_closing || !IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closing && IsLoaded) RequestClose();
        }), DispatcherPriority.Background);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                RequestClose();
                e.Handled = true;
                break;
            case Key.Down:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = Math.Min(Results.Items.Count - 1, Results.SelectedIndex + 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                Login();
                e.Handled = true;
                break;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => Login();

    private void Login()
    {
        if (_loginFired) return;
        if (Results.SelectedItem is not SapSystem system) return;
        _loginFired = true;

        // 登录流程可能弹生产确认、不可达提示或账号编辑窗口。
        // 必须等本窗口完全关闭后再启动，避免新对话框仍以「正在关闭的窗口」为所有者。
        void AfterClosed(object? sender, EventArgs e)
        {
            Closed -= AfterClosed;
            Dispatcher.BeginInvoke(new Action(() => _ = _onLogin(system)), DispatcherPriority.Normal);
        }

        if (_closing && !IsLoaded)
        {
            // 极端情况：窗口已经关闭，直接排队执行登录
            Dispatcher.BeginInvoke(new Action(() => _ = _onLogin(system)), DispatcherPriority.Normal);
            return;
        }

        Closed += AfterClosed;
        RequestClose();
    }
}
