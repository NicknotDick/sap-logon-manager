using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private bool _loading;

    public SettingsWindow(MainWindow owner)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _owner = owner;

        ThemeBox.Items.Add(new ComboBoxItem { Content = Localizer.Get("Settings.Dark"), Tag = ThemeManager.Dark });
        ThemeBox.Items.Add(new ComboBoxItem { Content = Localizer.Get("Settings.Light"), Tag = ThemeManager.Light });
        LangBox.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        LangBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });

        LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;
        var s = AppServices.Settings;

        Select(ThemeBox, s.Theme);
        Select(LangBox, s.Language);
        IdleLockBox.Text = s.IdleLockMinutes.ToString();
        AllowCmdPwCheck.IsChecked = s.AllowPasswordOnCommandLine;
        WindowsHelloCheck.IsChecked = s.UseWindowsHello;
        DefaultClientBox.Text = s.DefaultClient;
        WorkDirBox.Text = s.WorkDir;
        BatchIntervalBox.Text = s.BatchIntervalSeconds.ToString();
        ReuseCheck.IsChecked = s.ReuseSession;
        AutoFillCheck.IsChecked = s.AutoFillLogonDialog;
        SkipCheck.IsChecked = s.SkipUnreachableInBatch;
        ProdCheck.IsChecked = s.ConfirmProduction;
        HotkeyBox.Text = s.Hotkey;
        StartupCheck.IsChecked = StartupManager.IsEnabled;
        TrayCheck.IsChecked = s.MinimizeToTray;

        var mode = Localizer.Get(AppPaths.IsPortable ? "Settings.ModePortable" : "Settings.ModeNormal");
        DataDirText.Text = Localizer.Get("Settings.DataDir") + ": " + AppPaths.Root + "\n" + mode;

        _loading = false;
    }

    private static void Select(ComboBox box, string tag)
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

    private static string TagOf(ComboBox box, string fallback)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private async void OnWindowsHelloChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (WindowsHelloCheck.IsChecked != true) return;

        var ok = await WindowsHelloService.IsAvailableAsync();
        if (ok) return;

        _loading = true;
        WindowsHelloCheck.IsChecked = false;
        _loading = false;
        MessageBox.Show(this, Localizer.Get("Settings.WindowsHelloNA"), Localizer.Get("App.Title"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        ThemeManager.Apply(TagOf(ThemeBox, ThemeManager.Dark));
        WindowTheme.Refresh(this);
        WindowTheme.Refresh(_owner);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        Localizer.Apply(TagOf(LangBox, "zh-CN"));
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var s = AppServices.Settings;
        s.Theme = TagOf(ThemeBox, ThemeManager.Dark);
        s.Language = TagOf(LangBox, "zh-CN");
        s.IdleLockMinutes = int.TryParse(IdleLockBox.Text.Trim(), out var idle) ? Math.Clamp(idle, 0, 720) : 10;
        s.AllowPasswordOnCommandLine = AllowCmdPwCheck.IsChecked == true;
        s.UseWindowsHello = WindowsHelloCheck.IsChecked == true;
        s.DefaultClient = string.IsNullOrWhiteSpace(DefaultClientBox.Text) ? "100" : DefaultClientBox.Text.Trim();
        s.WorkDir = WorkDirBox.Text.Trim();
        s.BatchIntervalSeconds = int.TryParse(BatchIntervalBox.Text.Trim(), out var interval) ? Math.Clamp(interval, 1, 10) : 3;
        s.ReuseSession = ReuseCheck.IsChecked == true;
        s.AutoFillLogonDialog = AutoFillCheck.IsChecked == true;
        s.SkipUnreachableInBatch = SkipCheck.IsChecked == true;
        s.ConfirmProduction = ProdCheck.IsChecked == true;
        s.Hotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? "Ctrl+Alt+S" : HotkeyBox.Text.Trim();
        s.MinimizeToTray = TrayCheck.IsChecked == true;

        StartupManager.Set(StartupCheck.IsChecked == true);

        AppServices.Manifest.Save();
        ThemeManager.Apply(s.Theme);
        Localizer.Apply(s.Language);
        WindowTheme.Refresh(this);
        WindowTheme.Refresh(_owner);

        StatusText.Text = Localizer.Get("Settings.Saved");
        MessageBox.Show(this, Localizer.Get("Settings.Saved") + "\n\n热键修改需重启程序生效。",
            Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnChangePassword(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked()) return;
        var dlg = new VaultDialog(VaultMode.Change) { Owner = this };
        if (dlg.ShowDialog() == true)
            MessageBox.Show(this, Localizer.Get("Settings.PwdChanged"), Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool EnsureUnlocked()
    {
        if (AppServices.Vault.IsUnlocked) return true;
        var dlg = new VaultDialog(VaultMode.Unlock) { Owner = this };
        return dlg.ShowDialog() == true;
    }

    private void OnClearCredentials(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked()) return;
        if (MessageBox.Show(this, Localizer.Get("Settings.ClearConfirm"), Localizer.Get("App.Title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        AppServices.Vault.ClearAll();
        MessageBox.Show(this, Localizer.Get("Msg.Saved"), Localizer.Get("App.Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked()) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"sap-connections-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dlg.ShowDialog() != true) return;

        string? exportPassword = null;
        var include = MessageBox.Show(this, "是否同时导出凭据（用独立密码加密）？\n选择「否」则只导出连接信息。",
            Localizer.Get("Btn.Export"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (include == MessageBoxResult.Cancel) return;
        if (include == MessageBoxResult.Yes)
        {
            var pwd = new InputDialog("导出密码", "请设置导出密码（至少 6 位）", "", secret: true) { Owner = this };
            if (pwd.ShowDialog() != true || pwd.Value.Length < 6)
            {
                MessageBox.Show(this, "导出密码至少 6 位，已取消。", Localizer.Get("App.Title"));
                return;
            }
            exportPassword = pwd.Value;
        }

        try
        {
            ExportImportService.Export(dlg.FileName, AppServices.Manifest.Data.Systems,
                AppServices.Manifest.Data.Groups, AppServices.Vault, exportPassword);
            StatusText.Text = Localizer.Get("Msg.ExportDone") + "：" + dlg.FileName;
            MessageBox.Show(this, Localizer.Get("Msg.ExportDone") + "\n" + dlg.FileName, Localizer.Get("Btn.Export"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("导出失败", ex);
            MessageBox.Show(this, Localizer.Get("Msg.ExportFailed") + "\n" + ex.Message, Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnlocked()) return;
        var dlg = new ImportWindow { Owner = this };
        if (dlg.ShowDialog() == true)
            _owner.ReloadFromManifest();
    }

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureAll();
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("打开数据目录失败：" + ex.Message);
        }
    }

    private void OnSwitchPortable(object sender, RoutedEventArgs e)
    {
        if (AppPaths.IsPortable)
        {
            MessageBox.Show(this, Localizer.Get("Settings.ModePortable"), Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, Localizer.Get("Settings.PortableConfirm"), Localizer.Get("App.Title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        if (!PortableService.TrySwitch(out var info))
        {
            MessageBox.Show(this, Localizer.Get("Settings.PortableFail") + info, Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this, Localizer.Get("Settings.PortableDone") + "\n\n" + info, Localizer.Get("App.Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
        RestartApp();
    }

    private static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            // 单文件 exe 时 ProcessPath 就是本程序；framework-dependent 下是 dotnet.exe，跳过自动重启
            if (!string.IsNullOrEmpty(exe) && !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception ex)
        {
            Log.Warn("重启程序失败：" + ex.Message);
        }
        Application.Current.Shutdown();
    }

    private void OnDiagnostic(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Diagnostics.Build());
            StatusText.Text = Localizer.Get("Msg.Copied");
        }
        catch (Exception ex)
        {
            Log.Warn("复制诊断信息失败：" + ex.Message);
        }
    }

    private void OnRollback(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确认回滚到最近一次同步前的快照？", Localizer.Get("Settings.Rollback"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        if (AppServices.Manifest.RestoreLatestBackup(out var message))
        {
            _owner.ReloadFromManifest();
            MessageBox.Show(this, message, Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, message, Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDesktopShortcut(object sender, RoutedEventArgs e)
    {
        var ok = StartupManager.CreateDesktopShortcut(out var message);
        MessageBox.Show(this, message, Localizer.Get("App.Title"), MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
