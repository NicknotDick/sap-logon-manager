using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SapLauncher.Models;
using SapLauncher.Services;
using SapLauncher.Views;

namespace SapLauncher;

public partial class MainWindow : Window
{
    private enum NavMode
    {
        All,
        Favorites,
        Production,
        Group,
        Ungrouped
    }

    private readonly ObservableCollection<SapSystem> _visible = new();
    private readonly Dictionary<SapSystem, PropertyChangedEventHandler> _handlers = new();
    private NavMode _nav = NavMode.All;
    private string? _navGroup;
    private string _search = "";
    private CancellationTokenSource? _probeCts;
    private TrayService? _tray;
    private HotkeyService? _hotkey;
    private LandscapeWatcher? _watcher;
    private LockService? _locker;
    private DispatcherTimer? _toastTimer;
    private bool _syncing;
    private bool _forceClose;

    // 长按拖拽排序
    private readonly Dictionary<RadioButton, GroupDef> _groupOfRb = new();
    private DispatcherTimer? _pressTimer;
    private Point _pressPoint;
    private SapSystem? _pressItem;
    private bool _dragging;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        DataContext = _visible;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        SystemList.PreviewMouseRightButtonDown += OnListRightButtonDown;
        SystemList.PreviewMouseLeftButtonDown += OnListPressDown;
        SystemList.PreviewMouseMove += OnListMouseMove;
        SystemList.PreviewMouseLeftButtonUp += OnListPressUp;

        SortBox.SelectedIndex = AppServices.Settings.SortMode switch
        {
            "Name" => 1,
            "Recent" => 2,
            "Custom" => 3,
            _ => 0
        };

        foreach (var s in AppServices.Manifest.Data.Systems) HookSystem(s);
        RefreshGuiText();
        BuildGroups();
        ApplyFilter();
        RefreshDefaultUsers();
        UpdateLockState();
    }

    // ==================== 生命周期 ====================

    public void StartHidden()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Show();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hotkey = new HotkeyService();
        if (!_hotkey.TryRegister(this, AppServices.Settings.Hotkey, out var error))
        {
            Log.Warn("全局热键注册失败：" + error);
            _hotkey = null;
        }
        else
        {
            _hotkey.Pressed += ShowQuickSearch;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.StartMinimized) Hide();

        _tray = new TrayService();
        _tray.ShowRequested += RestoreFromTray;
        _tray.SearchRequested += ShowQuickSearch;
        _tray.SyncRequested += async () => await SyncAsync(true);
        _tray.LockRequested += () => { LockVault(); ShowToast("凭据库已锁定"); };
        _tray.ExitRequested += ExitApp;

        _watcher = new LandscapeWatcher();
        _watcher.Changed += () => Dispatcher.BeginInvoke(new Action(async () => await SyncAsync(false)));
        _watcher.Start();

        _locker = new LockService();
        _locker.Locked += () =>
        {
            UpdateLockState();
            ShowToast("闲置超时，凭据库已锁定");
        };
        _locker.Start();

        RefreshDefaultUsers();
        await SyncAsync(false);
        _ = ProbeAllAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_forceClose && AppServices.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _tray?.ShowBalloon(Localizer.Get("App.Title"), "已最小化到托盘，按 " + AppServices.Settings.Hotkey + " 快速搜索");
            return;
        }
        Cleanup();
    }

    private void ExitApp()
    {
        _forceClose = true;
        Cleanup();
        Application.Current.Shutdown();
    }

    private void Cleanup()
    {
        try { _hotkey?.Dispose(); } catch { }
        try { _watcher?.Dispose(); } catch { }
        try { _locker?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _probeCts?.Cancel(); } catch { }
        AppServices.Manifest.Save();
        AppPaths.CleanTemp();
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ==================== 过滤 / 排序 ====================

    private void ApplyFilter()
    {
        var systems = AppServices.Manifest.Data.Systems;
        IEnumerable<SapSystem> query = systems;

        switch (_nav)
        {
            case NavMode.Favorites:
                query = query.Where(s => s.Favorite);
                break;
            case NavMode.Production:
                query = query.Where(s => s.IsProduction);
                break;
            case NavMode.Group:
                query = query.Where(s => string.Equals(s.Group, _navGroup, StringComparison.CurrentCultureIgnoreCase));
                break;
            case NavMode.Ungrouped:
                query = query.Where(s => string.IsNullOrWhiteSpace(s.Group));
                break;
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var key = _search.Trim();
            query = query.Where(s => Matches(s, key));
        }

        IOrderedEnumerable<SapSystem> ordered = AppServices.Settings.SortMode switch
        {
            "Name" => query.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Recent" => query.OrderByDescending(s => s.LastLogin ?? DateTime.MinValue),
            "Custom" => query.OrderBy(s => s.SortOrder),
            _ => query.OrderByDescending(s => s.Favorite).ThenBy(s => s.SortOrder)
        };

        // 失效条目统一沉底
        query = ordered.ThenBy(s => s.IsStale);

        _visible.Clear();
        foreach (var s in query) _visible.Add(s);

        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionUi();
    }

    private static bool Matches(SapSystem s, string key)
        => s.DisplayName.Contains(key, StringComparison.CurrentCultureIgnoreCase)
           || s.Name.Contains(key, StringComparison.CurrentCultureIgnoreCase)
           || s.Sid.Contains(key, StringComparison.OrdinalIgnoreCase)
           || s.Host.Contains(key, StringComparison.OrdinalIgnoreCase)
           || s.MessageServer.Contains(key, StringComparison.OrdinalIgnoreCase)
           || s.Memo.Contains(key, StringComparison.CurrentCultureIgnoreCase)
           || s.LastClient.Contains(key, StringComparison.OrdinalIgnoreCase);

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            AppServices.Settings.SortMode = mode;
            AppServices.Manifest.Save();
            if (IsLoaded) ApplyFilter();
        }
    }

    private void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (NavAll.IsChecked == true) { _nav = NavMode.All; _navGroup = null; }
        else if (NavFav.IsChecked == true) { _nav = NavMode.Favorites; _navGroup = null; }
        else if (NavProd.IsChecked == true) { _nav = NavMode.Production; _navGroup = null; }
        ApplyFilter();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        if (IsLoaded) ApplyFilter();
    }

    // ==================== 长按拖拽排序 ====================

    /// <summary>仅当「全部」视图且无搜索时可拖动（此时 _visible 即全部条目，顺序可安全固化）。</summary>
    private bool CanReorder => IsLoaded && string.IsNullOrWhiteSpace(_search) && _nav == NavMode.All;

    private void OnListPressDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanReorder) return;
        var item = ItemUnder(e.GetPosition(SystemList));
        if (item is null) return;

        _pressItem = item;
        _pressPoint = e.GetPosition(SystemList);
        _dragging = false;

        _pressTimer?.Stop();
        _pressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _pressTimer.Tick += (_, _) =>
        {
            _pressTimer?.Stop();
            BeginDrag();
        };
        _pressTimer.Start();
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(SystemList);

        if (!_dragging)
        {
            // 松手前明显移动 → 当普通点击/双击处理，取消长按
            if (_pressTimer is not null && _pressItem is not null &&
                (Math.Abs(pos.X - _pressPoint.X) > 8 || Math.Abs(pos.Y - _pressPoint.Y) > 8))
                CancelPress();
            return;
        }

        var target = ItemUnder(pos);
        if (target is null || ReferenceEquals(target, _pressItem)) return;

        int from = _visible.IndexOf(_pressItem!);
        int to = _visible.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;

        _visible.Move(from, to);                       // ObservableCollection 实时刷新列表
        for (int i = 0; i < _visible.Count; i++) _visible[i].SortOrder = i;
    }

    private void OnListPressUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            SystemList.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            AppServices.Manifest.Save();
            ShowToast("排序已保存（自定义）");
        }
        CancelPress();
    }

    private SapSystem? ItemUnder(Point pos)
    {
        var hit = SystemList.InputHitTest(pos) as DependencyObject;
        if (hit is null) return null;
        return (ItemsControl.ContainerFromElement(SystemList, hit) as ListBoxItem)?.DataContext as SapSystem;
    }

    private void BeginDrag()
    {
        if (_pressItem is null || !_visible.Contains(_pressItem)) return;
        EnsureCustomOrder();
        _dragging = true;
        SystemList.CaptureMouse();
        Cursor = Cursors.SizeAll;
        ShowToast("拖动到目标位置以调整顺序，松手保存");
    }

    /// <summary>若不是自定义排序，则把当前显示顺序固化为 SortOrder，并切到「自定义」。</summary>
    private void EnsureCustomOrder()
    {
        if (string.Equals(AppServices.Settings.SortMode, "Custom", StringComparison.Ordinal)) return;
        for (int i = 0; i < _visible.Count; i++) _visible[i].SortOrder = i;
        AppServices.Settings.SortMode = "Custom";
        SortBox.SelectedIndex = 3;
        AppServices.Manifest.Save();
    }

    private void CancelPress()
    {
        _pressTimer?.Stop();
        _pressTimer = null;
        if (!_dragging) _pressItem = null;
    }

    /// <summary>给分组项挂上长按拖拽（松手时按落点重排分组）。</summary>
    private void AttachGroupDrag(RadioButton rb, GroupDef group)
    {
        DispatcherTimer? timer = null;
        bool dragging = false;

        rb.PreviewMouseLeftButtonDown += (_, _) =>
        {
            dragging = false;
            timer?.Stop();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
            timer.Tick += (_, _) =>
            {
                timer?.Stop();
                dragging = true;
                rb.CaptureMouse();
                Cursor = Cursors.SizeAll;
                ShowToast("拖到目标分组位置以调整顺序，松手保存");
            };
            timer.Start();
        };

        rb.PreviewMouseLeftButtonUp += (_, e) =>
        {
            timer?.Stop();
            if (!dragging) return;
            dragging = false;
            rb.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;

            var hit = GroupPanel.InputHitTest(e.GetPosition(GroupPanel)) as DependencyObject;
            var targetRb = FindAncestor<RadioButton>(hit);
            if (targetRb is null || !_groupOfRb.TryGetValue(targetRb, out var targetGroup) ||
                ReferenceEquals(targetGroup, group)) return;

            if (ReorderUtil.MoveGroup(AppServices.Manifest.Data.Groups, group, targetGroup))
            {
                AppServices.Manifest.Save();
                BuildGroups();
                ShowToast("分组顺序已保存");
            }
        };
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    // ==================== 分组 ====================

    private void BuildGroups()
    {
        GroupPanel.Children.Clear();
        _groupOfRb.Clear();
        var navStyle = (Style)FindResource("NavButton");

        foreach (var group in AppServices.Manifest.Data.Groups.OrderBy(g => g.SortOrder))
        {
            var name = group.Name;
            var rb = new RadioButton
            {
                Style = navStyle,
                GroupName = "nav",
                Tag = "·",
                Content = name
            };
            rb.Checked += (_, _) =>
            {
                _nav = NavMode.Group;
                _navGroup = name;
                ApplyFilter();
            };

            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "重命名分组" };
            rename.Click += (_, _) =>
            {
                var dlg = new InputDialog("重命名分组", "分组名称", name) { Owner = this };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
                foreach (var s in AppServices.Manifest.Data.Systems)
                    if (string.Equals(s.Group, name, StringComparison.CurrentCultureIgnoreCase)) s.Group = dlg.Value;
                group.Name = dlg.Value;
                AppServices.Manifest.Save();
                BuildGroups();
                ApplyFilter();
            };
            var remove = new MenuItem { Header = "删除分组" };
            remove.Click += (_, _) =>
            {
                foreach (var s in AppServices.Manifest.Data.Systems)
                    if (string.Equals(s.Group, name, StringComparison.CurrentCultureIgnoreCase)) s.Group = "";
                AppServices.Manifest.Data.Groups.Remove(group);
                AppServices.Manifest.Save();
                _nav = NavMode.All;
                NavAll.IsChecked = true;
                BuildGroups();
                ApplyFilter();
            };
            menu.Items.Add(rename);
            menu.Items.Add(remove);
            rb.ContextMenu = menu;
            _groupOfRb[rb] = group;
            AttachGroupDrag(rb, group);

            if (_nav == NavMode.Group && string.Equals(_navGroup, name, StringComparison.CurrentCultureIgnoreCase))
                rb.IsChecked = true;

            GroupPanel.Children.Add(rb);
        }

        var ungrouped = new RadioButton
        {
            Style = navStyle,
            GroupName = "nav",
            Tag = "·",
            Content = Localizer.Get("Editor.GroupNone")
        };
        ungrouped.Checked += (_, _) =>
        {
            _nav = NavMode.Ungrouped;
            _navGroup = null;
            ApplyFilter();
        };
        if (_nav == NavMode.Ungrouped) ungrouped.IsChecked = true;
        GroupPanel.Children.Add(ungrouped);
    }

    private void OnNewGroupClick(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog(Localizer.Get("Nav.NewGroup"), Localizer.Get("Msg.GroupName"), "") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        if (AppServices.Manifest.Data.Groups.Any(g => string.Equals(g.Name, dlg.Value, StringComparison.CurrentCultureIgnoreCase)))
            return;
        AppServices.Manifest.Data.Groups.Add(new GroupDef
        {
            Name = dlg.Value,
            SortOrder = AppServices.Manifest.Data.Groups.Count
        });
        AppServices.Manifest.Save();
        BuildGroups();
    }

    // ==================== 同步 ====================

    private async void OnSyncClick(object sender, RoutedEventArgs e) => await SyncAsync(true);

    private async Task SyncAsync(bool manual)
    {
        if (_syncing) return;
        _syncing = true;
        SyncButton.IsEnabled = false;
        var originalContent = SyncButton.Content;
        SyncButton.Content = "同步中…";

        try
        {
            AppServices.RefreshGui();
            RefreshGuiText();

            var scan = await Task.Run(LandscapeReader.Scan).ConfigureAwait(true);

            if (scan.Entries.Count == 0)
            {
                var reason = scan.Warnings.Count > 0 ? string.Join("；", scan.Warnings.Take(2)) : "未找到 SAP Logon 配置文件";
                Log.Warn("同步未获得条目：" + reason);
                if (manual) ShowToast(Localizer.Get("Msg.SyncFailed") + "：" + reason, true);
                return;
            }

            AppServices.Manifest.Snapshot();
            var result = ListSyncService.Merge(AppServices.Manifest.Data.Systems, scan, AppServices.Settings);
            AppServices.Settings.LastSyncUtc = DateTime.UtcNow;

            bool changed = result.Added + result.Updated + result.Renamed + result.Stale + result.Revived > 0;
            if (changed) AppServices.Manifest.Save();

            foreach (var s in AppServices.Manifest.Data.Systems) HookSystem(s);
            BuildGroups();
            ApplyFilter();
            RefreshGuiText();

            // SAP GUI 启动会话时会回写配置，导致监听频繁触发；无变化时不刷日志
            if (changed) Log.Info("同步完成：" + result.Summary);
            if (manual) ShowToast(result.Summary);
            _ = ProbeAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("同步失败", ex);
            if (manual) ShowToast(Localizer.Get("Msg.SyncFailed") + "：" + ex.Message, true);
        }
        finally
        {
            _syncing = false;
            SyncButton.IsEnabled = true;
            SyncButton.Content = originalContent;
        }
    }

    private async Task ProbeAllAsync()
    {
        try { _probeCts?.Cancel(); } catch { }
        _probeCts = new CancellationTokenSource();
        var token = _probeCts.Token;
        try
        {
            var targets = AppServices.Manifest.Data.Systems.Where(s => !s.IsStale).ToList();
            await ConnectivityProbe.ProbeAllAsync(targets, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warn("连通性探测异常：" + ex.Message);
        }
    }

    private void RefreshGuiText()
    {
        GuiText.Text = AppServices.Gui.IsInstalled
            ? AppServices.Gui.Version
            : Localizer.Get("Status.GuiMissing");
        // 最左下角状态栏：悬停可看安装路径与配置来源
        GuiText.ToolTip = AppServices.Gui.IsInstalled
            ? AppServices.Gui.InstallDir + (string.IsNullOrWhiteSpace(AppServices.Gui.LandscapePath)
                ? "" : "\n配置：" + AppServices.Gui.LandscapePath)
            : Localizer.Get("Status.GuiMissing");

        SyncText.Text = Localizer.Get("Status.LastSync") + "：" +
                        (AppServices.Settings.LastSyncUtc?.ToLocalTime().ToString("MM-dd HH:mm")
                         ?? Localizer.Get("Status.NeverSynced"));
    }

    private void UpdateLockState()
    {
        LockText.Text = AppServices.Vault.IsUnlocked
            ? Localizer.Get("Status.Unlocked")
            : Localizer.Get("Status.Locked");
        LockText.Foreground = AppServices.Vault.IsUnlocked
            ? (Brush)FindResource("Brush.Success")
            : (Brush)FindResource("Brush.Warn");
    }

    private void LockVault()
    {
        AppServices.Vault.Lock();
        UpdateLockState();
    }

    private bool EnsureUnlocked()
    {
        if (AppServices.Vault.IsUnlocked) return true;
        var dlg = new VaultDialog(VaultMode.Unlock) { Owner = this };
        var ok = dlg.ShowDialog() == true;
        UpdateLockState();
        if (ok) RefreshDefaultUsers();
        return ok;
    }

    // ==================== 登录 ====================

    private Credential? ResolveCredential(SapSystem system, out string client)
    {
        Credential? cred = null;
        if (AppServices.Vault.IsUnlocked)
        {
            cred = AppServices.Vault.GetAny(system.Uuid);
        }

        client = cred?.Client is { Length: > 0 } c ? c
            : !string.IsNullOrWhiteSpace(system.LastClient) ? system.LastClient
            : AppServices.Settings.DefaultClient;

        if (cred is not null) system.DefaultUser = cred.Display;
        return cred;
    }

    /// <summary>该连接默认使用的客户端（用于展示账号列表）。</summary>
    private string ResolveClientFor(SapSystem system)
    {
        if (AppServices.Vault.IsUnlocked)
        {
            var any = AppServices.Vault.GetAny(system.Uuid);
            if (any is not null && !string.IsNullOrWhiteSpace(any.Client)) return any.Client;
        }
        return !string.IsNullOrWhiteSpace(system.LastClient) ? system.LastClient : AppServices.Settings.DefaultClient;
    }

    /// <summary>刷新各行显示的默认账号名。</summary>
    private void RefreshDefaultUsers()
    {
        foreach (var s in AppServices.Manifest.Data.Systems)
            s.DefaultUser = AppServices.Vault.IsUnlocked ? (AppServices.Vault.GetAny(s.Uuid)?.Display ?? "") : "";
    }

    private async Task LoginAsync(SapSystem system, Credential? chosen = null, bool fromBatch = false)
    {
        if (!fromBatch && system.IsProduction && AppServices.Settings.ConfirmProduction)
        {
            var ok = MessageBox.Show(string.Format(Localizer.Get("Msg.ProdConfirm"), system.DisplayName),
                Localizer.Get("App.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        if (system.Probe == ProbeState.Offline && !fromBatch)
        {
            var ep = ConnectivityProbe.ResolveEndpoint(system);
            var go = MessageBox.Show($"{system.DisplayName} 的 {ep.host}:{ep.port} 当前不可达，仍要尝试登录？",
                Localizer.Get("App.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (go != MessageBoxResult.OK) return;
        }

        var cred = chosen;
        var client = chosen?.Client ?? "";

        if (cred is null)
        {
            cred = ResolveCredential(system, out client);

            if (cred is null || string.IsNullOrEmpty(cred.Password))
            {
                if (!EnsureUnlocked()) return;
                var editor = new EntryEditorWindow(system, cred) { Owner = this };
                if (editor.ShowDialog() != true) return;
                cred = editor.ResultCredential;
                client = editor.ResultClient;
                RefreshDefaultUsers();
                ApplyFilter();
                AppServices.Manifest.Save();
            }
        }

        ShowToast("正在登录 " + system.DisplayName + " …");
        var result = await AppServices.Launch.LaunchAsync(system, cred, AppServices.Settings, client,
            cred?.Language ?? "ZH", cred?.LogonGroup, CancellationToken.None);

        if (result.Outcome is LaunchOutcome.Success or LaunchOutcome.LaunchedWithoutProof)
        {
            system.LastLogin = DateTime.Now;
            system.LoginCount++;
            system.LastClient = client;
            system.IsNew = false;
            if (cred is not null) system.DefaultUser = cred.Display;
            AppServices.Manifest.Save();
            ApplyFilter();
            if (!fromBatch) ShowToast(system.DisplayName + " · " + result.Message);
            await RunBoundScriptsAsync(system);
        }
        else if (!fromBatch)
        {
            ShowToast(result.Message, result.Outcome == LaunchOutcome.Failed);
        }
    }

    private async Task RunBoundScriptsAsync(SapSystem system)
    {
        if (system.Scripts.Count == 0) return;
        foreach (var id in system.Scripts.ToList())
        {
            var def = AppServices.Scripts.Find(id);
            if (def is null) continue;
            var (ok, output) = await AppServices.Scripts.RunAsync(def);
            if (!ok)
            {
                Log.Warn($"脚本 {def.Name} 执行失败：{output}");
                ShowToast(Localizer.Get("Msg.ScriptFail") + "：" + def.Name, true);
                break;
            }
        }
    }

    private async void OnRowLogin(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SapSystem system)
        {
            SystemList.SelectedItem = system;
            await LoginAsync(system);
        }
    }

    private async void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            ItemsControl.ContainerFromElement(SystemList, src) is ListBoxItem)
        {
            if (SystemList.SelectedItem is SapSystem system) await LoginAsync(system);
        }
    }

    private async void OnLoginSelected(object sender, RoutedEventArgs e)
    {
        var items = AppServices.Manifest.Data.Systems.Where(s => s.IsSelected).ToList();
        foreach (var s in items) s.IsSelected = false;
        foreach (var s in items) await LoginAsync(s, null, true);
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        foreach (var s in AppServices.Manifest.Data.Systems) s.IsSelected = false;
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        int count = AppServices.Manifest.Data.Systems.Count(s => s.IsSelected);
        BatchBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatchText.Text = string.Format(Localizer.Get("Status.Selected"), count);
        LoginSelectedButton.Content = Localizer.Get("Ctx.Login") + " (" + count + ")";
        SelectionText.Text = count > 0 ? string.Format(Localizer.Get("Status.Selected"), count) : "";
        BatchButton.IsEnabled = count > 0;
    }

    private void HookSystem(SapSystem system)
    {
        if (_handlers.ContainsKey(system)) return;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SapSystem.IsSelected):
                    UpdateSelectionUi();
                    break;
                case nameof(SapSystem.Favorite):
                    ApplyFilter();
                    break;
            }
        };
        system.PropertyChanged += handler;
        _handlers[system] = handler;
    }

    // ==================== 批量登录 ====================

    private void OnBatchClick(object sender, RoutedEventArgs e)
    {
        var selected = AppServices.Manifest.Data.Systems.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowToast(Localizer.Get("Batch.None"));
            return;
        }

        var prod = selected.Where(s => s.IsProduction).Select(s => s.DisplayName).ToList();
        if (prod.Count > 0 && AppServices.Settings.ConfirmProduction)
        {
            var ok = MessageBox.Show(string.Format(Localizer.Get("Batch.ProdConfirm"), string.Join("、", prod)),
                Localizer.Get("App.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
        }

        var window = new BatchWindow(selected) { Owner = this };
        window.ShowDialog();
        ApplyFilter();
    }

    // ==================== 右键菜单 ====================

    private void OnListRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(SystemList, src) is not ListBoxItem item) return;

        item.IsSelected = true;

        // 右键未勾选的行：只选中它，避免把已有勾选项一起纳入右键操作
        if (item.DataContext is SapSystem system && !system.IsSelected)
        {
            foreach (var s in AppServices.Manifest.Data.Systems) s.IsSelected = false;
            system.IsSelected = true;
        }
    }

    /// <summary>右键/批量操作的目标集合：优先所有勾选项，否则当前选中行。</summary>
    private List<SapSystem> Targets()
    {
        var checkedItems = AppServices.Manifest.Data.Systems.Where(s => s.IsSelected).ToList();
        if (checkedItems.Count > 0) return checkedItems;
        return Current is { } current ? new List<SapSystem> { current } : new List<SapSystem>();
    }

    private readonly List<MenuItem> _accountMenuItems = new();

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var targets = Targets();
        int count = targets.Count;
        var suffix = count > 1 ? $"  （{count}）" : "";

        MenuLogin.Header = Localizer.Get("Ctx.Login") + suffix;
        MenuFav.Header = Localizer.Get("Ctx.Favorite") + suffix;
        MenuGroup.Header = Localizer.Get("Ctx.MoveGroup") + suffix;
        MenuClearNew.Header = Localizer.Get("Ctx.ClearNew") + suffix;
        MenuDelete.Header = Localizer.Get("Ctx.Delete") + suffix;

        bool single = count <= 1;
        MenuCred.IsEnabled = single;
        MenuEdit.IsEnabled = single;
        MenuCopy.IsEnabled = single;
        MenuSap.IsEnabled = single;
        MenuScripts.IsEnabled = single;

        BuildAccountMenuItems(Current);
    }

    /// <summary>在「登录」项后插入「以 XX 登录」，多账号时才出现；默认账号带标注。</summary>
    private void BuildAccountMenuItems(SapSystem? system)
    {
        var menu = SystemList.ContextMenu;
        if (menu is null) return;

        foreach (var item in _accountMenuItems) menu.Items.Remove(item);
        _accountMenuItems.Clear();

        if (system is null || !AppServices.Vault.IsUnlocked) return;

        var client = ResolveClientFor(system);
        var accounts = AppServices.Vault.GetAll(system.Uuid, client);
        if (accounts.Count <= 1)
        {
            // 单账号时把默认账号名体现在「登录」标题上
            var only = accounts.FirstOrDefault();
            MenuLogin.Header = only is null
                ? Localizer.Get("Ctx.Login")
                : string.Format(Localizer.Get("Ctx.LoginDefault"), only.Display);
            return;
        }

        MenuLogin.Header = string.Format(Localizer.Get("Ctx.LoginDefault"),
            accounts.FirstOrDefault(a => a.IsDefault)?.Display ?? accounts[0].Display);

        var insertAt = menu.Items.IndexOf(MenuLogin) + 1;
        foreach (var account in accounts)
        {
            var header = string.Format(Localizer.Get("Ctx.LoginAs"), account.Display) +
                         (account.IsDefault ? Localizer.Get("Ctx.DefaultSuffix") : "");
            var item = new MenuItem { Header = header };
            var captured = account;
            item.Click += async (_, _) => await LoginAsync(system, captured);
            menu.Items.Insert(insertAt++, item);
            _accountMenuItems.Add(item);
        }
    }

    private SapSystem? Current => SystemList.SelectedItem as SapSystem;

    private async void OnCtxLogin(object sender, RoutedEventArgs e)
    {
        if (Current is { } s) await LoginAsync(s);
    }

    private void OnCtxCredential(object sender, RoutedEventArgs e)
    {
        if (Current is not { } s) { ShowToast(Localizer.Get("Msg.SelectSystem")); return; }
        if (!EnsureUnlocked()) return;
        var cred = AppServices.Vault.GetAny(s.Uuid);
        var dlg = new EntryEditorWindow(s, cred, credentialOnly: true) { Owner = this };
        if (dlg.ShowDialog() == true) AppServices.Manifest.Save();
        RefreshDefaultUsers();
        ApplyFilter();
    }

    private void OnCtxEdit(object sender, RoutedEventArgs e)
    {
        if (Current is not { } s) { ShowToast(Localizer.Get("Msg.SelectSystem")); return; }
        Credential? cred = AppServices.Vault.IsUnlocked ? AppServices.Vault.GetAny(s.Uuid) : null;
        var dlg = new EntryEditorWindow(s, cred) { Owner = this };
        if (dlg.ShowDialog() == true) AppServices.Manifest.Save();
        ApplyFilter();
    }

    private void OnCtxCopyInfo(object sender, RoutedEventArgs e)
    {
        if (Current is not { } s) return;
        var user = AppServices.Vault.IsUnlocked ? AppServices.Vault.GetAny(s.Uuid)?.User : null;
        try
        {
            Clipboard.SetText(ExportImportService.BuildShareText(s, user));
            ShowToast(Localizer.Get("Msg.Copied"));
        }
        catch (Exception ex)
        {
            Log.Warn("复制失败：" + ex.Message);
        }
    }

    private void OnCtxExportSap(object sender, RoutedEventArgs e)
    {
        if (Current is not { } s) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SAP 快捷方式 (*.sap)|*.sap",
            FileName = $"{s.Name} {s.LastClient}.sap".Replace("  ", " ")
        };
        if (dlg.ShowDialog() != true) return;

        var cred = AppServices.Vault.IsUnlocked ? AppServices.Vault.GetAny(s.Uuid) : null;
        var safeCred = cred is null ? null : new Credential
        {
            User = cred.User,
            Client = cred.Client,
            Language = cred.Language,
            Tcode = cred.Tcode,
            LogonGroup = cred.LogonGroup,
            Password = "" // 分享文件不含密码
        };
        var content = ShortcutFileBuilder.BuildContent(s, safeCred, AppServices.Settings,
            cred?.Client ?? AppServices.Settings.DefaultClient, cred?.Language ?? "ZH");
        System.IO.File.WriteAllBytes(dlg.FileName, Interop.NativeMethods.ToAnsi(content));
        ShowToast(Localizer.Get("Msg.ExportDone") + "：" + dlg.FileName);
    }

    private void OnCtxScripts(object sender, RoutedEventArgs e)
    {
        if (Current is not { } s) return;
        var dlg = new ScriptsWindow(s) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnCtxFavorite(object sender, RoutedEventArgs e)
    {
        var targets = Targets();
        if (targets.Count == 0) return;

        // 批量时统一成同一种状态：只要有未收藏的就全部置为收藏
        var makeFavorite = targets.Any(s => !s.Favorite);
        BatchOps.SetFavorite(targets, makeFavorite);

        AppServices.Manifest.Save();
        ApplyFilter();
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SapSystem s) return;
        s.Favorite = !s.Favorite;
        AppServices.Manifest.Save();
        ApplyFilter();
    }

    private void OnCtxMoveGroup(object sender, RoutedEventArgs e)
    {
        var targets = Targets();
        if (targets.Count == 0)
        {
            ShowToast(Localizer.Get("Msg.SelectSystem"));
            return;
        }

        var dlg = new GroupPickerDialog(
            AppServices.Manifest.Data.Groups.Select(g => g.Name).ToList(), targets[0].Group)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true) return;

        var moved = BatchOps.MoveToGroup(targets, dlg.SelectedGroup, AppServices.Manifest.Data.Groups);
        AppServices.Manifest.Save();
        ApplyFilter();

        var groupName = dlg.SelectedGroup ?? Localizer.Get("Editor.GroupNone");
        ShowToast($"已移动 {moved} 个连接到「{groupName}」");
    }

    private void OnCtxClearNew(object sender, RoutedEventArgs e)
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        BatchOps.ClearNew(targets);
        AppServices.Manifest.Save();
        ApplyFilter();
    }

    private void OnCtxDelete(object sender, RoutedEventArgs e)
    {
        var targets = Targets();
        if (targets.Count == 0) return;

        var message = targets.Count == 1
            ? Localizer.Get("Msg.ConfirmDelete")
            : $"确认删除选中的 {targets.Count} 个连接？凭据将同时删除，不可撤销。";

        if (MessageBox.Show(message, Localizer.Get("App.Title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        foreach (var s in targets)
        {
            if (_handlers.TryGetValue(s, out var handler)) s.PropertyChanged -= handler;
            _handlers.Remove(s);
            if (AppServices.Vault.IsUnlocked) AppServices.Vault.Remove(s.Uuid);
        }

        BatchOps.Delete(AppServices.Manifest.Data.Systems, targets);
        AppServices.Manifest.Save();
        ApplyFilter();
        ShowToast($"已删除 {targets.Count} 个连接");
    }

    // ==================== 新增 ====================

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked()) return;
        var dlg = new EntryEditorWindow(null, null) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AppServices.Manifest.Data.Systems.Add(dlg.ResultSystem);
        AppServices.Manifest.Save();
        HookSystem(dlg.ResultSystem);
        BuildGroups();
        RefreshDefaultUsers();
        ApplyFilter();
        ShowToast("已新增 " + dlg.ResultSystem.DisplayName);
    }

    // ==================== 设置 / 关于 / 快速搜索 ====================

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(this) { Owner = this };
        dlg.ShowDialog();
        ReloadFromManifest();
        ThemeManager.Apply(AppServices.Settings.Theme);
        Localizer.Apply(AppServices.Settings.Language);
        WindowTheme.Refresh(this);
    }

    /// <summary>清单被外部替换（回滚/导入）后整体重建，避免持有失效引用。</summary>
    public void ReloadFromManifest()
    {
        foreach (var pair in _handlers) pair.Key.PropertyChanged -= pair.Value;
        _handlers.Clear();

        foreach (var s in AppServices.Manifest.Data.Systems) HookSystem(s);

        _nav = NavMode.All;
        NavAll.IsChecked = true;
        BuildGroups();
        ApplyFilter();
        RefreshDefaultUsers();
        RefreshGuiText();
        UpdateLockState();
        UpdateSelectionUi();
        _ = ProbeAllAsync();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    public void ShowQuickSearch()
    {
        if (!AppServices.Vault.IsUnlocked && VaultExists())
        {
            RestoreFromTray();
            if (!EnsureUnlocked()) return;
        }

        var systems = AppServices.Manifest.Data.Systems.Where(s => !s.IsStale).ToList();
        var win = new QuickSearchWindow(systems, async s => await LoginAsync(s));
        win.ShowDialog();
    }

    private static bool VaultExists() => AppServices.Vault.Exists;

    // ==================== 键盘 / 提示 ====================

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await SyncAsync(true);
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }

        if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Clear();
            return;
        }

        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            var first = _visible.FirstOrDefault();
            if (first is not null) await LoginAsync(first);
        }
    }

    private void ShowToast(string message, bool warning = false)
    {
        ToastText.Text = message;
        ToastText.Foreground = (Brush)FindResource(warning ? "Brush.Danger" : "Brush.Text");
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
            Toast.BeginAnimation(OpacityProperty, fade);
        };
        _toastTimer.Start();
    }
}
