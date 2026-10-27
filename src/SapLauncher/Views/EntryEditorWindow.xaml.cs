using System.Windows;
using System.Windows.Controls;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class EntryEditorWindow : Window
{
    private readonly SapSystem _system;
    private readonly Credential? _credential;
    private readonly bool _credentialOnly;
    private readonly List<string> _clients = new();

    /// <summary>当前客户端的账号工作副本（加载时克隆，保存时按账号名集合差异同步）。</summary>
    private readonly List<Credential> _accounts = new();
    private Credential? _selectedAccount;
    private string _currentClient = "";
    private bool _syncing;

    public SapSystem ResultSystem => _system;
    public Credential? ResultCredential { get; private set; }
    public string ResultClient { get; private set; } = "";

    public EntryEditorWindow(SapSystem? system, Credential? credential, bool credentialOnly = false)
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        _credentialOnly = credentialOnly;
        _system = system ?? new SapSystem
        {
            Uuid = "local:" + Guid.NewGuid().ToString("N"),
            IsLocal = true,
            SortOrder = int.MaxValue,
            Env = EnvKind.Unknown
        };
        _credential = credential;

        TitleText.Text = credentialOnly
            ? Localizer.Get("Ctx.Credential").TrimEnd('…')
            : system is null ? Localizer.Get("Editor.NewTitle") : Localizer.Get("Editor.Title");
        Title = TitleText.Text;

        // 客户端候选
        foreach (var c in new[] { "100", "200", "300", "400", "500", "600", "700", "800", "900" })
            _clients.Add(c);
        if (!string.IsNullOrWhiteSpace(_system.LastClient) && !_clients.Contains(_system.LastClient))
            _clients.Insert(0, _system.LastClient);
        if (credential is not null && !string.IsNullOrWhiteSpace(credential.Client) && !_clients.Contains(credential.Client))
            _clients.Insert(0, credential.Client);
        ClientBox.ItemsSource = _clients;

        // 可编辑下拉框：选择后同步文本框，确保所选客户端一定可见
        ClientBox.SelectionChanged += (_, _) =>
        {
            if (ClientBox.SelectedItem is string selected) ClientBox.Text = selected;
            ReloadAccountsIfClientChanged();
        };
        ClientBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(ClientBox.Text)) ClientBox.Text = AppServices.Settings.DefaultClient;
            ReloadAccountsIfClientChanged();
        };

        foreach (var g in AppServices.Manifest.Data.Groups.OrderBy(x => x.SortOrder))
            GroupCombo.Items.Add(g.Name);

        Fill();
        BuildCandidates();

        if (!AppServices.Vault.IsUnlocked)
        {
            VaultHint.Text = Localizer.Get("Msg.Locked") + "，密码不会被保存。";
            VaultHint.Visibility = Visibility.Visible;
            PasswordBox2.IsEnabled = false;
            RememberBox.IsChecked = false;
            RememberBox.IsEnabled = false;
            SetDefaultButton.IsEnabled = false;
            DeleteAccountButton.IsEnabled = false;
        }
    }

    // ---------------- 账号列表 ----------------

    private void ReloadAccountsIfClientChanged()
    {
        if (_syncing) return;
        var client = ClientBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(client) || client == _currentClient) return;
        LoadAccounts(client);
    }

    private void LoadAccounts(string client)
    {
        CommitFields();
        _currentClient = client;
        _accounts.Clear();

        if (AppServices.Vault.IsUnlocked)
        {
            foreach (var c in AppServices.Vault.GetAll(_system.Uuid, client))
                _accounts.Add(c.Clone());
        }

        // 选中传入的账号，否则默认账号，否则第一个
        var prefer = _credential is not null && string.Equals(_credential.Client, client, StringComparison.Ordinal)
            ? _credential.User
            : null;

        // 兼容旧数据：该客户端没有任何默认账号时，把最近更新的那个设为默认
        if (_accounts.Count > 0 && !_accounts.Any(a => a.IsDefault))
            _accounts[0].IsDefault = true;

        RefreshAccountList(prefer);
    }

    private void RefreshAccountList(string? preferUser = null)
    {
        _syncing = true;
        AccountList.ItemsSource = null;
        AccountList.ItemsSource = _accounts.ToList();

        var target = preferUser is not null
            ? _accounts.FirstOrDefault(a => string.Equals(a.User, preferUser, StringComparison.OrdinalIgnoreCase))
            : null;
        target ??= _accounts.FirstOrDefault(a => a.IsDefault) ?? _accounts.FirstOrDefault();

        _selectedAccount = target;
        if (target is not null) AccountList.SelectedItem = target;

        AccountCountText.Text = string.Format(Localizer.Get("Editor.AccountCount"),
            _accounts.Count, AccountService.MaxAccountsPerClient);

        LoadFields(target);
        UpdateAccountButtons();
        _syncing = false;
    }

    private void LoadFields(Credential? account)
    {
        if (account is null)
        {
            UserBox.Text = "";
            RealNameBox.Text = "";
            PasswordBox2.Password = "";
            TcodeBox.Text = "";
            LogonGroupBox.Text = "";
            RememberBox.IsChecked = true;
            SelectText(LangBox, "ZH");
            return;
        }

        UserBox.Text = account.User;
        RealNameBox.Text = account.DisplayName;
        PasswordBox2.Password = account.Password;
        TcodeBox.Text = account.Tcode;
        LogonGroupBox.Text = account.LogonGroup;
        RememberBox.IsChecked = account.Remember || !string.IsNullOrEmpty(account.Password);
        SelectText(LangBox, string.IsNullOrWhiteSpace(account.Language) ? "ZH" : account.Language);
    }

    private void UpdateLoginMethodHint()
    {
        var tag = TagOf(LoginMethodBox);
        LoginMethodHint.Text = tag switch
        {
            "CmdPw" => Localizer.Get("Editor.LoginMethod.CmdPwHint"),
            "SapOnly" => Localizer.Get("Editor.LoginMethod.SapOnlyHint"),
            _ => Localizer.Get("Editor.LoginMethod.AutoHint")
        };
    }

    /// <summary>姓名输入即时写入当前工作副本，避免切换/刷新路径漏提交导致丢失。</summary>
    private void OnRealNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selectedAccount is null) return;
        _selectedAccount.DisplayName = RealNameBox.Text.Trim();
    }

    /// <summary>把界面字段写回当前选中的工作副本。</summary>
    private void CommitFields()
    {
        if (_selectedAccount is null) return;
        _selectedAccount.User = UserBox.Text.Trim();
        _selectedAccount.DisplayName = RealNameBox.Text.Trim();
        _selectedAccount.Password = PasswordBox2.Password;
        _selectedAccount.Remember = RememberBox.IsChecked == true;
        _selectedAccount.Language = (LangBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZH";
        _selectedAccount.Tcode = TcodeBox.Text.Trim();
        _selectedAccount.LogonGroup = LogonGroupBox.Text.Trim();
    }

    private void UpdateAccountButtons()
    {
        var has = _selectedAccount is not null;
        SetDefaultButton.IsEnabled = has && AppServices.Vault.IsUnlocked;
        DeleteAccountButton.IsEnabled = has;
    }

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        CommitFields();
        _selectedAccount = AccountList.SelectedItem as Credential;
        LoadFields(_selectedAccount);
        UpdateAccountButtons();
    }

    private void OnAddAccount(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Vault.IsUnlocked)
        {
            ShowError(Localizer.Get("Msg.Locked"));
            return;
        }

        CommitFields();
        if (!AccountService.CanAdd(AppServices.Vault, _system.Uuid, _currentClient, out var error))
        {
            ShowError(error);
            return;
        }

        var account = new Credential
        {
            SystemUuid = _system.Uuid,
            Client = _currentClient,
            Language = "ZH",
            Remember = true
        };
        _accounts.Add(account);
        RefreshAccountList(account.User);
        UserBox.Focus();
    }

    private void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is null)
        {
            ShowError(Localizer.Get("Editor.SelectAccountFirst"));
            return;
        }

        var target = _selectedAccount;
        var label = string.IsNullOrWhiteSpace(target.User) ? Localizer.Get("Editor.User") : target.User;
        if (MessageBox.Show(string.Format(Localizer.Get("Editor.DeleteAccountConfirm"), label),
                Localizer.Get("App.Title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        _accounts.Remove(target);
        RefreshAccountList();
    }

    private void OnSetDefault(object sender, RoutedEventArgs e)
    {
        if (_selectedAccount is null)
        {
            ShowError(Localizer.Get("Editor.SelectAccountFirst"));
            return;
        }

        CommitFields();
        foreach (var a in _accounts) a.IsDefault = ReferenceEquals(a, _selectedAccount);
        RefreshAccountList(_selectedAccount.User);
    }

    // ---------------- 连接字段 ----------------

    private void Fill()
    {
        NameBox.Text = _system.Name;
        SidBox.Text = _system.Sid;
        HostBox.Text = _system.Host;
        PortBox.Text = _system.Port.ToString();
        RouterBox.Text = _system.Router;
        MsBox.Text = _system.MessageServer;
        AliasBox.Text = _system.Alias;
        MemoBox.Text = _system.Memo;
        GroupCombo.Text = _system.Group;

        SelectTag(EnvBox, _system.Env.ToString());
        SelectTag(KindBox, _system.Kind.ToString());

        var startClient = _credential?.Client;
        if (string.IsNullOrWhiteSpace(startClient))
            startClient = string.IsNullOrWhiteSpace(_system.LastClient)
                ? AppServices.Settings.DefaultClient
                : _system.LastClient;

        ClientBox.Text = startClient;
        if (_clients.Contains(startClient)) ClientBox.SelectedItem = startClient;

        if (_credentialOnly)
        {
            ConnectionSection.Visibility = Visibility.Collapsed;
            MaxHeight = 700;
        }

        OnKindChanged(this, null!);
        LoadAccounts(startClient);

        DirectTcodeBox.IsChecked = _system.DirectTcode;
        SelectTag(LoginMethodBox, string.IsNullOrWhiteSpace(_system.LoginMethod) ? "Auto" : _system.LoginMethod);
        LoginMethodBox.SelectionChanged += (_, _) => UpdateLoginMethodHint();
        UpdateLoginMethodHint();
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static void SelectText(ComboBox box, string text)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Direct";

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HostBox is null) return;
        var kind = TagOf(KindBox);
        var isLb = kind == "LoadBalanced";
        var isRouter = kind == "Router";

        HostBox.IsEnabled = !isLb;
        PortBox.IsEnabled = !isLb;
        RouterBox.IsEnabled = isRouter;
        MsBox.IsEnabled = isLb;
    }

    private void BuildCandidates()
    {
        if (_credentialOnly) return;
        var candidates = MemoCredentialParser.Parse(_system.Memo);
        if (candidates.Count == 0) return;

        CandidateSection.Visibility = Visibility.Visible;
        foreach (var candidate in candidates)
        {
            var row = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Brush.SurfaceAlt"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = candidate.Summary, FontSize = 12 });
            text.Children.Add(new TextBlock
            {
                Text = candidate.Evidence,
                FontSize = 10.5,
                Foreground = (System.Windows.Media.Brush)FindResource("Brush.TextMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 480
            });

            var use = new Button
            {
                Content = Localizer.Get("Editor.UseCandidate"),
                Style = (Style)FindResource("Btn.Link"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            use.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(candidate.User)) UserBox.Text = candidate.User;
                if (!string.IsNullOrWhiteSpace(candidate.Password)) PasswordBox2.Password = candidate.Password;
                if (!string.IsNullOrWhiteSpace(candidate.Client)) ClientBox.Text = candidate.Client;
            };

            Grid.SetColumn(text, 0);
            Grid.SetColumn(use, 1);
            grid.Children.Add(text);
            grid.Children.Add(use);
            row.Child = grid;
            CandidatePanel.Children.Add(row);
        }
    }

    // ---------------- 保存 ----------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (!_credentialOnly)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ShowError(Localizer.Get("Editor.NameRequired"));
                return;
            }
            if (string.IsNullOrWhiteSpace(SidBox.Text))
            {
                ShowError(Localizer.Get("Editor.SidRequired"));
                return;
            }

            _system.Name = NameBox.Text.Trim();
            _system.Sid = SidBox.Text.Trim().ToUpperInvariant();
            _system.Host = HostBox.Text.Trim();
            _system.Port = int.TryParse(PortBox.Text.Trim(), out var port) ? port : 3200;
            _system.Router = RouterBox.Text.Trim();
            _system.MessageServer = MsBox.Text.Trim();
            _system.Alias = AliasBox.Text.Trim();
            _system.Memo = MemoBox.Text;
            _system.Group = GroupCombo.Text.Trim();
            _system.LoginMethod = TagOf(LoginMethodBox);
            _system.Kind = TagOf(KindBox) switch
            {
                "Router" => ConnKind.Router,
                "LoadBalanced" => ConnKind.LoadBalanced,
                _ => ConnKind.Direct
            };

            var envTag = TagOf(EnvBox);
            _system.EnvManual = envTag != "Unknown";
            _system.Env = envTag switch
            {
                "Dev" => EnvKind.Dev,
                "Qas" => EnvKind.Qas,
                "Prd" => EnvKind.Prd,
                _ => LandscapeReader.InferEnv(_system.Name, _system.Sid)
            };

            if (!string.IsNullOrWhiteSpace(_system.Group) &&
                !AppServices.Manifest.Data.Groups.Any(g => string.Equals(g.Name, _system.Group, StringComparison.CurrentCultureIgnoreCase)))
            {
                AppServices.Manifest.Data.Groups.Add(new GroupDef
                {
                    Name = _system.Group,
                    SortOrder = AppServices.Manifest.Data.Groups.Count
                });
            }
        }

        // 「登录后直达事务码」是系统级开关：完整编辑 / 仅编辑凭据 都必须保存
        _system.DirectTcode = DirectTcodeBox.IsChecked == true;

        CommitFields();

        // 「保存密码并自动登录」：若默认账号没有密码，而当前账号有密码且勾选了记住，
        // 则把当前账号设为默认——双击连接时才会用它自动登录（否则仍会弹登录框）。
        var defAccount = _accounts.FirstOrDefault(a => a.IsDefault);
        if (_selectedAccount is { Remember: true } sel && !string.IsNullOrEmpty(sel.Password) &&
            (defAccount is null || string.IsNullOrEmpty(defAccount.Password)))
        {
            foreach (var a in _accounts) a.IsDefault = ReferenceEquals(a, sel);
            Log.Info("自动登录：已将当前账号设为默认（原默认账号没有保存密码）");
        }

        var client = ClientBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(client)) client = AppServices.Settings.DefaultClient;
        ResultClient = client;

        // 客户端选择立即写回清单，即使尚未保存凭据也能在列表显示
        _system.LastClient = client;

        if (AppServices.Vault.IsUnlocked)
        {
            // 以工作副本为准做差异同步：删除/改名 → 先清掉不在新集合中的旧账号
            var keepUsers = _accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.User))
                .Select(a => a.User)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var old in AppServices.Vault.GetAll(_system.Uuid, client)
                         .Where(o => !keepUsers.Contains(o.User)).ToList())
            {
                AccountService.Delete(AppServices.Vault, _system.Uuid, client, old.User);
            }

            foreach (var account in _accounts)
            {
                if (string.IsNullOrWhiteSpace(account.User)) continue;
                account.SystemUuid = _system.Uuid;
                account.Client = client;
                Log.Info($"保存账号 {account.User}（客户端 {client}）：姓名={(string.IsNullOrEmpty(account.DisplayName) ? "(空)" : account.DisplayName)}，密码长度={account.Password.Length}，默认={account.IsDefault}");
                if (!AccountService.Save(AppServices.Vault, account, out var error))
                {
                    ShowError(error);
                    return;
                }
                if (ReferenceEquals(account, _selectedAccount)) ResultCredential = account;
            }

            _system.DefaultUser = AppServices.Vault.GetAny(_system.Uuid)?.Display ?? "";
        }
        else
        {
            if (_accounts.Any(a => !string.IsNullOrEmpty(a.Password)))
            {
                ShowError(Localizer.Get("Msg.Locked"));
                return;
            }
            ResultCredential = null;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
