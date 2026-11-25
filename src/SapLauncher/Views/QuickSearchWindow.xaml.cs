using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class QuickSearchWindow : Window
{
    private readonly List<SapSystem> _all;
    /// <summary>登录回调：chosen 为右击菜单选定的账号，languageOverride 为二级菜单选定的语言；
    /// 二者为 null 时按默认凭据与配置语言登录（原 Enter/双击行为）。</summary>
    private readonly Func<SapSystem, Credential?, string?, Task> _onLogin;

    /// <summary>
    /// 关闭流程标志。WPF 在 Window.Close() 执行期间（_isClosing=true）禁止
    /// 再次调用 Close/Show/ShowDialog/EnsureHandle，否则抛 InvalidOperationException。
    /// </summary>
    private bool _closing;

    /// <summary>防止 Enter 连击 / 双击与回车叠加导致重复登录。</summary>
    private bool _loginFired;

    /// <summary>右击菜单当前指向的账号（事件触发时读取，避免闭包捕获过期对象）。</summary>
    private Credential? _menuDefaultAccount;
    private SapSystem? _menuSystem;
    private readonly List<MenuItem> _menuDynamicItems = new();

    public QuickSearchWindow(IReadOnlyList<SapSystem> systems, Func<SapSystem, Credential?, string?, Task> onLogin)
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

    // ==================== 整行右击：账号 + 语言登录 ====================

    /// <summary>右击整行任意位置先选中该行，供菜单构建使用（与主窗口右键一致）。</summary>
    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(Results, src) is not ListBoxItem item) return;
        item.IsSelected = true;
    }

    /// <summary>
    /// 右击菜单打开时按当前行重建账号列表。结构与主窗口一致：默认登录项带 Enter，
    /// 其后平铺其余账号；每项挂 ZH/EN 语言二级菜单，交互由共享构建器提供。
    /// </summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in _menuDynamicItems) menu.Items.Remove(item);
        _menuDynamicItems.Clear();

        var system = Results.SelectedItem as SapSystem;
        _menuSystem = system;
        _menuDefaultAccount = null;

        if (system is null || !AppServices.Vault.IsUnlocked)
        {
            // 未解锁：只留默认登录入口（按配置），不暴露账号列表
            menu.Items.Clear();
            var plain = new MenuItem { Header = Localizer.Get("Ctx.Login") };
            plain.Click += (_, _) => Login();
            menu.Items.Add(plain);
            _menuDynamicItems.Add(plain);
            return;
        }

        void CloseMenu() => menu.IsOpen = false;
        AccountLoginMenu.LoginHandler login = (s, a, lang) =>
        {
            LoginWith(s, a, lang);
            return Task.CompletedTask;
        };

        var accounts = AccountService.GroupByClient(AppServices.Vault.Items, system.Uuid)
            .SelectMany(g => g.Accounts).ToList();
        var def = AppServices.Vault.GetAny(system.Uuid) ?? accounts.FirstOrDefault();
        if (def is null)
        {
            menu.Items.Clear();
            var plain = new MenuItem { Header = Localizer.Get("Ctx.Login") };
            plain.Click += (_, _) => Login();
            menu.Items.Add(plain);
            _menuDynamicItems.Add(plain);
            return;
        }

        menu.Items.Clear();
        _menuDefaultAccount = def;

        var defaultItem = new MenuItem
        {
            Header = AccountService.FormatEntryLineNoLang(def),
            InputGestureText = "Enter"
        };
        // 每次菜单打开都重建 MenuItem，hooked 用局部标记即可（主窗口默认项常驻才需字段防重复）
        var defaultHooked = false;
        AccountLoginMenu.AttachLanguageMenu(defaultItem, () => _menuSystem, () => _menuDefaultAccount,
            login, CloseMenu, ref defaultHooked);
        menu.Items.Add(defaultItem);
        _menuDynamicItems.Add(defaultItem);

        foreach (var item in AccountLoginMenu.BuildAdditionalAccountItems(
                     AppServices.Vault.Items, system.Uuid, def, () => _menuSystem, login, CloseMenu))
        {
            menu.Items.Add(item);
            _menuDynamicItems.Add(item);
        }
    }

    private void Login() => LoginWith(Results.SelectedItem as SapSystem, null, null);

    /// <summary>
    /// 统一登录出口：按选定账号/语言登录；未指定时沿用原默认行为。
    /// 登录流程可能弹生产确认、不可达提示或账号编辑窗口，必须等本窗口完全关闭后再启动，
    /// 避免新对话框仍以「正在关闭的窗口」为所有者。
    /// </summary>
    private void LoginWith(SapSystem? system, Credential? chosen, string? languageOverride)
    {
        if (_loginFired) return;
        if (system is null) return;
        _loginFired = true;

        void AfterClosed(object? sender, EventArgs e)
        {
            Closed -= AfterClosed;
            Dispatcher.BeginInvoke(new Action(() => _ = _onLogin(system, chosen, languageOverride)),
                DispatcherPriority.Normal);
        }

        if (_closing && !IsLoaded)
        {
            // 极端情况：窗口已经关闭，直接排队执行登录
            Dispatcher.BeginInvoke(new Action(() => _ = _onLogin(system, chosen, languageOverride)),
                DispatcherPriority.Normal);
            return;
        }

        Closed += AfterClosed;
        RequestClose();
    }
}
