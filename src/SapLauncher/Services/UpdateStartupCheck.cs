using System.IO;
using SapLauncher.Views;

namespace SapLauncher.Services;

/// <summary>
/// 每天首次启动的更新检查：用日期标记保证每天最多请求一次；
/// 有新版本才提示，无新版本或检查失败均静默，不打扰用户。
/// </summary>
public static class UpdateStartupCheck
{
    private static string StampPath => Path.Combine(AppPaths.Root, "update-check.txt");

    /// <summary>今天是否尚未检查过（标记缺失/不可读时按未检查处理）。</summary>
    public static bool ShouldCheckToday(string? path = null)
    {
        try
        {
            var p = path ?? StampPath;
            if (!File.Exists(p)) return true;
            return File.ReadAllText(p).Trim() != DateTime.Now.ToString("yyyy-MM-dd");
        }
        catch { return true; }
    }

    /// <summary>记录今天已检查；写盘失败不影响主流程。</summary>
    public static void MarkChecked(string? path = null)
    {
        try
        {
            File.WriteAllText(path ?? StampPath, DateTime.Now.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            Log.Warn("记录更新检查日期失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 启动主窗口显示后异步调用：请求一次 Releases API，发现新版本才弹提示窗；
    /// 用户选「查看新版本」则走 UpdateChecker.OpenDownload（与「关于 → 查找更新」同一套逻辑）。
    /// 网络失败、解析失败、窗口异常一律只记日志，不影响程序启动。
    /// </summary>
    public static async Task RunAsync()
    {
        if (!ShouldCheckToday()) return;
        MarkChecked(); // 发起即标记：无论结果如何，每天最多请求一次
        try
        {
            var release = await UpdateChecker.GetLatestAsync();
            if (UpdateChecker.CompareTags(UpdateChecker.CurrentTag, release.Tag) <= 0) return;

            var prompt = new UpdatePromptWindow(release);
            if (prompt.ShowDialog() == true)
                UpdateChecker.OpenDownload(release);
        }
        catch (Exception ex)
        {
            Log.Warn("启动时检查更新失败（已静默）：" + ex.Message);
        }
    }
}
