using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class AboutWindow : Window
{
    /// <summary>GitHub 项目地址。</summary>
    private static readonly Uri ProjectUri = new("https://github.com/NicknotDick/sap-logon-manager");

    /// <summary>防止「查找更新」被连点导致并发请求。</summary>
    private bool _checking;

    public AboutWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        VersionText.Text = $"{Localizer.Get("About.Version")} {BuildVersion()}  ·  {AppServices.Gui.Version}";
        var mode = Localizer.Get(AppPaths.IsPortable ? "Settings.ModePortable" : "Settings.ModeNormal");

        // 数据目录：路径做成可点击链接，点击在资源管理器中打开对应位置
        PathText.Inlines.Clear();
        PathText.Inlines.Add(new Run(Localizer.Get("Settings.DataDir") + ":  "));
        var dirLink = new Hyperlink(new Run(AppPaths.Root));
        dirLink.Click += OnOpenDataDir;
        PathText.Inlines.Add(dirLink);
        PathText.Inlines.Add(new Run("  ·  " + mode));

        // GitHub 项目链接（点击用浏览器打开）
        var link = new Hyperlink(new Run("github.com/NicknotDick/sap-logon-manager")) { NavigateUri = ProjectUri };
        link.Click += OnOpenGitHub;
        GitHubText.Inlines.Add(new Run(Localizer.Get("About.GitHub") + "  "));
        GitHubText.Inlines.Add(link);
    }

    private void OnOpenGitHub(object sender, RoutedEventArgs e) => OpenBrowser(ProjectUri.AbsoluteUri);

    /// <summary>用系统默认浏览器打开链接（含更新下载）。</summary>
    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("打开浏览器失败：" + ex.Message);
            MessageBox.Show(Localizer.Get("About.BrowserFailed"), Localizer.Get("App.Title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 「查找更新」：异步读取 GitHub 最新 Release，比较版本后提示用户；
    /// 用户确认时按当前安装包形态（zip / 单文件）用浏览器下载对应附件。
    /// </summary>
    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_checking) return;
        _checking = true;
        var original = UpdateButton.Content;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = Localizer.Get("About.Checking");
        try
        {
            var release = await UpdateChecker.GetLatestAsync();
            if (UpdateChecker.CompareTags(UpdateChecker.CurrentTag, release.Tag) <= 0)
            {
                MessageBox.Show(this,
                    string.Format(Localizer.Get("About.UpToDate"), UpdateChecker.CurrentTag),
                    Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var notes = string.Join("\n", (release.Body ?? "").Split('\n')
                .Select(l => l.Trim().TrimStart('#', '*', '-', '>', '|').Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("---")));
            if (notes.Length > 360) notes = notes[..360] + "…";
            var prompt = string.Format(Localizer.Get("About.UpdateFound"), release.Tag,
                release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd"), notes);
            if (MessageBox.Show(this, prompt, Localizer.Get("App.Title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var url = UpdateChecker.PickDownloadUrl(release, UpdateChecker.DetectPackageKind());
            OpenBrowser(url ?? UpdateChecker.ReleasesUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Log.Warn("检查更新失败：" + ex.Message);
            var prompt = string.Format(Localizer.Get("About.UpdateFailed"), ex.Message);
            if (MessageBox.Show(this, prompt, Localizer.Get("App.Title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                OpenBrowser(UpdateChecker.ReleasesUri.AbsoluteUri);
        }
        finally
        {
            UpdateButton.Content = original;
            UpdateButton.IsEnabled = true;
            _checking = false;
        }
    }

    /// <summary>点击「关于」中的数据目录链接：在资源管理器中打开数据目录。</summary>
    private void OnOpenDataDir(object sender, RoutedEventArgs e)
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
