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

        if (_mode == VaultMode.Setup)
        {
            AppServices.Vault.Initialize(password);
            AppServices.Settings.RememberMasterKey = RememberBox.IsChecked == true;
            if (AppServices.Settings.RememberMasterKey) KeyProtection.SavePassword(password);
            AppServices.Manifest.Save();
            Log.Info("已创建凭据库");
            DialogResult = true;
            Close();
            return;
        }

        // Change
        if (!AppServices.Vault.Verify(CurrentBox.Password))
        {
            ShowError(Localizer.Get("Unlock.Wrong"));
            return;
        }

        if (!AppServices.Vault.ChangePassword(password, out var changeError))
        {
            ShowError(changeError);
            return;
        }

        if (AppServices.Settings.RememberMasterKey) KeyProtection.SavePassword(password);
        else KeyProtection.Clear();

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
