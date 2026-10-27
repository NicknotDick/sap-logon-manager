using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SapLauncher.Services;

/// <summary>诊断信息：用于排障，不含任何凭据内容。</summary>
public static class Diagnostics
{
    public static string Build()
    {
        var sb = new StringBuilder();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        sb.AppendLine("===== SAP 登录管理器 诊断信息 =====");
        sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("程序版本：" + version);
        sb.AppendLine("操作系统：" + Environment.OSVersion.VersionString + " / " + RuntimeInformation.OSArchitecture);
        sb.AppendLine("运行环境：" + RuntimeInformation.FrameworkDescription);
        sb.AppendLine();

        sb.AppendLine("--- SAP GUI ---");
        sb.AppendLine("版本：" + AppServices.Gui.Version);
        sb.AppendLine("安装目录：" + (string.IsNullOrEmpty(AppServices.Gui.InstallDir) ? "未检测到" : AppServices.Gui.InstallDir));
        sb.AppendLine("saplogon：" + (File.Exists(AppServices.Gui.SapLogonPath) ? "存在" : "缺失"));
        sb.AppendLine("sapshcut：" + (File.Exists(AppServices.Gui.SapShcutPath) ? "存在" : "缺失"));
        sb.AppendLine();

        sb.AppendLine("--- 配置文件 ---");
        foreach (var path in SapGuiLocator.LandscapeCandidates())
        {
            if (!File.Exists(path)) continue;
            sb.AppendLine($"{path}  修改于 {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}");
        }
        sb.AppendLine();

        sb.AppendLine("--- 清单 ---");
        var systems = AppServices.Manifest.Data.Systems;
        sb.AppendLine($"条目总数：{systems.Count}，收藏 {systems.Count(s => s.Favorite)}，失效 {systems.Count(s => s.IsStale)}，本地自建 {systems.Count(s => s.IsLocal)}");
        sb.AppendLine($"分组：{AppServices.Manifest.Data.Groups.Count}，脚本：{AppServices.Manifest.Data.Scripts.Count}");
        sb.AppendLine("上次同步：" + (AppServices.Settings.LastSyncUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未同步"));
        sb.AppendLine();

        sb.AppendLine("--- 凭据库 ---");
        sb.AppendLine("文件：" + (File.Exists(AppPaths.Vault) ? "存在" : "未创建"));
        sb.AppendLine("状态：" + (AppServices.Vault.IsUnlocked ? "已解锁" : "已锁定"));
        if (AppServices.Vault.IsUnlocked) sb.AppendLine("凭据条数：" + AppServices.Vault.Items.Count);
        sb.AppendLine();

        sb.AppendLine("--- 设置 ---");
        var st = AppServices.Settings;
        sb.AppendLine($"主题 {st.Theme}，语言 {st.Language}，闲置锁定 {st.IdleLockMinutes} 分钟，记住主密码 {st.RememberMasterKey}");
        sb.AppendLine($"默认客户端 {st.DefaultClient}，复用会话 {st.ReuseSession}，允许命令行密码 {st.AllowPasswordOnCommandLine}");
        sb.AppendLine($"批量间隔 {st.BatchIntervalSeconds}s，跳过不可达 {st.SkipUnreachableInBatch}，生产确认 {st.ConfirmProduction}，热键 {st.Hotkey}");
        sb.AppendLine();

        sb.AppendLine("--- 数据目录 ---");
        sb.AppendLine(AppPaths.Root);

        return sb.ToString();
    }
}
