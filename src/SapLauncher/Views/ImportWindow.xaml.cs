using System.Windows;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class ImportWindow : Window
{
    private ExportBundle? _bundle;

    public ImportWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SAP 连接包 (*.json;*.sapsync)|*.json;*.sapsync|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        PathBox.Text = dlg.FileName;
        _bundle = ExportImportService.Read(dlg.FileName);
        if (_bundle is null)
        {
            MessageBox.Show(this, Localizer.Get("Import.Failed"), Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ImportButton.IsEnabled = false;
            return;
        }

        var options = new ImportOptions { OverwriteExisting = OverwriteCheck.IsChecked == true };
        var preview = ExportImportService.Preview(_bundle, AppServices.Manifest.Data.Systems, options);
        PreviewList.ItemsSource = preview;
        ImportButton.IsEnabled = preview.Count > 0;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (_bundle is null) return;

        var options = new ImportOptions
        {
            OverwriteExisting = OverwriteCheck.IsChecked == true,
            ImportGroups = GroupsCheck.IsChecked == true
        };

        var (added, updated, skipped) = ExportImportService.Apply(_bundle, AppServices.Manifest.Data.Systems,
            AppServices.Manifest.Data.Groups, options);

        int credCount = 0;
        if (_bundle.IncludesCredentials && !string.IsNullOrWhiteSpace(ExportPasswordBox.Password))
        {
            credCount = ExportImportService.ImportCredentials(_bundle, AppServices.Vault, ExportPasswordBox.Password);
            if (credCount < 0)
            {
                MessageBox.Show(this, "导出密码错误，凭据未导入（连接信息已导入）。", Localizer.Get("App.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                credCount = 0;
            }
        }

        AppServices.Manifest.Save();
        var message = string.Format(Localizer.Get("Import.Done"), added, updated, skipped);
        if (credCount > 0) message += $"\n凭据导入 {credCount} 条";
        MessageBox.Show(this, message, Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
