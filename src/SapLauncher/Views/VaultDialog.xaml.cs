using System.Windows;
using System.Windows.Input;
using SapLauncher.Services;

namespace SapLauncher.Views;

public enum VaultMode
{
    Setup,
    Unlock,
    Change
}

public partial class VaultDialog : Window
{
    private static int _failedAttempts;
    private static DateTime _lockUntil = DateTime.MinValue;

    private readonly VaultMode _mode;

    public VaultDialog(VaultMode mode)
    {
        InitializeComponent();
        _mode = mode;
        WindowTheme.Apply(this);
        ApplyMode();
        ProtectCheck.Checked += OnProtectChanged;
        ProtectCheck.Unchecked += OnProtectChanged;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
        Loaded += async (_, _) =>
        {
            (CurrentPanel.Visibility == Visibility.Visible ? CurrentBox : MainBox).FocusInput();

            // 窗口已显示后再异步验证 Windows Hello（不阻塞启动）
            if (_mode != VaultMode.Unlock || !AppServices.Settings.UseWindowsHello || !KeyProtection.Exists)
                return;

            var saved = KeyProtection.LoadPassword();
            if (string.IsNullOrEmpty(saved)) return;

            try
            {
                var ok = await WindowsHelloService.VerifyAsync("验证以解锁 SAP 登录管理器");
                if (!ok) return;
                if (AppServices.Vault.Unlock(saved, out _))
                {
                    Log.Info("Windows Hello 验证通过，自动解锁");
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Windows Hello 解锁失败：" + ex.Message);
            }
        };
    }

    private void ApplyMode()
    {
        switch (_mode)
        {
            case VaultMode.Setup:
                HeaderText.Text = Localizer.Get("Unlock.SetupTitle");
                Title = HeaderText.Text;
                ConfirmPanel.Visibility = Visibility.Visible;
                CurrentPanel.Visibility = Visibility.Collapsed;
                RememberBox.Visibility = Visibility.Visible;
                RememberBox.IsChecked = true;
                OkButton.Content = Localizer.Get("Btn.Save");
                RecoverButton.Visibility = Visibility.Collapsed;
                // 首次使用：主密码为可选项，默认不启用（打开即用）
                ProtectPanel.Visibility = Visibility.Visible;
                ProtectCheck.IsChecked = false;
                ApplyProtectState();
                break;

            case VaultMode.Change:
                HeaderText.Text = Localizer.Get("Settings.ChangePassword");
                Title = HeaderText.Text;
                CurrentPanel.Visibility = Visibility.Visible;
                ConfirmPanel.Visibility = Visibility.Visible;
                RememberBox.Visibility = Visibility.Collapsed;
                MainLabel.Text = Localizer.Get("Unlock.Password") + "（新）";
                OkButton.Content = Localizer.Get("Btn.Save");
                HintText.Text = Localizer.Get("Unlock.Hint");
                RecoverButton.Visibility = Visibility.Collapsed;
                break;

            default:
                HeaderText.Text = Localizer.Get("Unlock.Title");
                Title = HeaderText.Text;
                ConfirmPanel.Visibility = Visibility.Collapsed;
                CurrentPanel.Visibility = Visibility.Collapsed;
                RememberBox.Visibility = Visibility.Visible;
                RememberBox.IsChecked = AppServices.Settings.RememberMasterKey;
                OkButton.Content = Localizer.Get("Unlock.Unlock");
                // 只有存在 DPAPI 保存的主密码时才提供恢复入口
                RecoverButton.Visibility = KeyProtection.Exists ? Visibility.Visible : Visibility.Collapsed;
                break;
        }
    }

    /// <summary>勾选状态决定是否启用主密码输入（未勾选 = 不设主密码，打开即用）。</summary>
    private void ApplyProtectState()
    {
        var on = ProtectCheck.IsChecked == true;
        PasswordPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RememberBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnProtectChanged(object sender, RoutedEventArgs e)
    {
        if (_mode != VaultMode.Setup) return;
        ApplyProtectState();
        if (ProtectCheck.IsChecked == true) MainBox.FocusInput();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (_mode == VaultMode.Unlock && DateTime.Now < _lockUntil)
        {
            ShowError(string.Format(Localizer.Get("Unlock.Cooldown"),
                Math.Max(1, (int)(_lockUntil - DateTime.Now).TotalSeconds)));
            return;
        }

        var password = MainBox.Password;

        if (_mode == VaultMode.Unlock)
        {
            // 凭据库未启用主密码：直接以空密码解锁
            if (AppServices.Settings.MasterPasswordRequired == false)
            {
                if (!AppServices.Vault.Unlock("", out var errNone))
                {
                    ShowError(errNone);
                    return;
                }
                _failedAttempts = 0;
                AppServices.Settings.RememberMasterKey = false;
                KeyProtection.Clear();
                AppServices.Manifest.Save();
                DialogResult = true;
                Close();
                return;
            }

            if (string.IsNullOrEmpty(password)) { ShowError(Localizer.Get("Unlock.TooShort")); return; }
            if (AppServices.Vault.Unlock(password, out var error))
            {
                _failedAttempts = 0;
                AppServices.Settings.RememberMasterKey = RememberBox.IsChecked == true;
                if (AppServices.Settings.RememberMasterKey) KeyProtection.SavePassword(password);
                else KeyProtection.Clear();
                AppServices.Manifest.Save();
                DialogResult = true;
                Close();
            }
            else
            {
                _failedAttempts++;
                if (_failedAttempts >= 5)
                {
                    _lockUntil = DateTime.Now.AddSeconds(30);
                    _failedAttempts = 0;
                }
                ShowError(error);
                MainBox.Clear();
                MainBox.FocusInput();
            }
            return;
        }

        if (_mode == VaultMode.Setup)
        {
            // 主密码为可选项：未勾选「启用主密码保护」时以空密码创建凭据库，打开即用
            var usePassword = ProtectCheck.IsChecked == true;

            if (usePassword)
            {
                if (password.Length < 6)
                {
                    ShowError(Localizer.Get("Unlock.TooShort"));
                    return;
                }
                if (ConfirmBox.Password != password)
                {
                    ShowError(Localizer.Get("Unlock.Mismatch"));
                    return;
                }
            }

            AppServices.Vault.Initialize(usePassword ? password : "");
            AppServices.Settings.MasterPasswordRequired = usePassword;
            AppServices.Settings.RememberMasterKey = usePassword && RememberBox.IsChecked == true;
            if (AppServices.Settings.RememberMasterKey) KeyProtection.SavePassword(password);
            else KeyProtection.Clear();
            AppServices.Manifest.Save();
            Log.Info(usePassword ? "已创建凭据库（启用主密码保护）" : "已创建凭据库（未启用主密码保护）");
            DialogResult = true;
            Close();
            return;
        }

        // Change：设置/修改主密码（原库可能未启用主密码，此时「当前主密码」留空即可）
        if (!AppServices.Vault.Verify(CurrentBox.Password))
        {
            ShowError(Localizer.Get("Unlock.Wrong"));
            return;
        }

        if (password.Length < 6)
        {
            ShowError(Localizer.Get("Unlock.TooShort"));
            return;
        }

        if (ConfirmBox.Password != password)
        {
            ShowError(Localizer.Get("Unlock.Mismatch"));
            return;
        }

        if (!AppServices.Vault.ChangePassword(password, out var changeError))
        {
            ShowError(changeError);
            return;
        }

        AppServices.Settings.MasterPasswordRequired = true;
        if (AppServices.Settings.RememberMasterKey) KeyProtection.SavePassword(password);
        else KeyProtection.Clear();
        AppServices.Manifest.Save();

        Log.Info("主密码已更新");
        DialogResult = true;
        Close();
    }

    private void OnRecoverWithWindows(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (!KeyProtection.Exists)
        {
            ShowError(Localizer.Get("Unlock.RecoverNoKey"));
            return;
        }

        var dlg = new InputDialog("用 Windows 密码解锁", "请输入当前 Windows 账户密码", "", secret: true) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.Value)) return;

        if (!WindowsCredentialService.VerifyCurrentUserPassword(dlg.Value))
        {
            ShowError(Localizer.Get("Unlock.WindowsPwdWrong"));
            return;
        }

        var saved = KeyProtection.LoadPassword();
        if (string.IsNullOrEmpty(saved))
        {
            ShowError(Localizer.Get("Unlock.RecoverNoKey"));
            return;
        }

        if (AppServices.Vault.Unlock(saved, out var err))
        {
            AppServices.Settings.RememberMasterKey = true;
            AppServices.Manifest.Save();
            Log.Info("通过 Windows 账户密码恢复解锁");
            DialogResult = true;
            Close();
        }
        else
        {
            ShowError(err);
        }
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
