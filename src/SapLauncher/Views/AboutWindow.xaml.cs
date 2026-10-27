using System.Reflection;
using System.Windows;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        VersionText.Text = $"{Localizer.Get("About.Version")} {BuildVersion()}  ·  {AppServices.Gui.Version}";
        var mode = Localizer.Get(AppPaths.IsPortable ? "Settings.ModePortable" : "Settings.ModeNormal");
        PathText.Text = Localizer.Get("Settings.DataDir") + ": " + AppPaths.Root + "  ·  " + mode;
    }

    /// <summary>版本号：GURURU + YYYYMMDDHHMM（年月日时分，紧凑格式），取程序文件的构建时间。</summary>
    private static string BuildVersion()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                return "GURURU" + System.IO.File.GetLastWriteTime(path).ToString("yyyyMMddHHmm");
        }
        catch { }
        return "GURURU" + DateTime.Now.ToString("yyyyMMddHHmm");
    }

    private void OnCopyDiagnostic(object sender, RoutedEventArgs e)
    {
        var text = Diagnostics.Build();
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show(this, Localizer.Get("Msg.Copied"), Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Warn("复制诊断信息失败：" + ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
