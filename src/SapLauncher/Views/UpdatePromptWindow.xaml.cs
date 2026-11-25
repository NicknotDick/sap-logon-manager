using System.Windows;
using SapLauncher.Services;

namespace SapLauncher.Views;

/// <summary>
/// 启动时发现新版本的提示窗：只负责展示与返回用户选择，
/// 「查看新版本」的实际下载由调用方统一走 UpdateChecker.OpenDownload，
/// 与「关于 → 查找更新」共用同一套下载逻辑。
/// </summary>
public partial class UpdatePromptWindow : Window
{
    public UpdatePromptWindow(ReleaseInfo release)
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        VersionText.Text = string.Format(Localizer.Get("Update.Version"), release.Tag);
        PublishedText.Text = string.Format(Localizer.Get("Update.Published"),
            release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd"));
        var notes = UpdateChecker.FormatNotes(release.Body);
        NotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? Localizer.Get("Update.NoNotes") : notes;
    }

    private void OnView(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnLater(object sender, RoutedEventArgs e) => DialogResult = false;
}
