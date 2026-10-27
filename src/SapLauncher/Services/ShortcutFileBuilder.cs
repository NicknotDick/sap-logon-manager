using System.IO;
using System.Text;
using SapLauncher.Interop;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 生成 SAP GUI 快捷方式文件（.sap）。
/// 关键约束（经本机取证）：
///  - [System] Name 是 SID，Description 必须与 SAP Logon 显示名逐字一致（否则退回 SID 匹配，重名时会连错系统）
///  - 编码为系统 ANSI（简体中文环境 GBK/936），换行为 CRLF
/// </summary>
public static class ShortcutFileBuilder
{
    public static string CreateSapFile(SapSystem system, Credential? credential, AppSettings settings,
        string client, string language, out string content)
        => CreateSapFile(system, credential, settings, client, language, false, out content);

    /// <param name="includeTcode">是否写入 OK 代码。写入会触发 SAP 快捷方式文件安全确认框。</param>
    public static string CreateSapFile(SapSystem system, Credential? credential, AppSettings settings,
        string client, string language, bool includeTcode, out string content)
    {
        content = BuildContent(system, credential, settings, client, language, includeTcode);
        AppPaths.EnsureAll();

        // 稳定文件名：SAP 的「记住我的决定」按文件路径生效，随机名会导致每次登录都重新确认。
        // 先写唯一临时文件再原子替换，避免并发登录同一账号时写入交错。
        var key = $"{system.Uuid}|{client}|{credential?.User}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        var path = Path.Combine(AppPaths.Temp, $"login-{hash}.sap");
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        File.WriteAllBytes(staging, NativeMethods.ToAnsi(content));
        File.Move(staging, path, true);
        return path;
    }

    public static string BuildContent(SapSystem system, Credential? credential, AppSettings settings,
        string client, string language)
        => BuildContent(system, credential, settings, client, language, false);

    public static string BuildContent(SapSystem system, Credential? credential, AppSettings settings,
        string client, string language, bool includeTcode)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append("\r\n");

        Line("[System]");
        Line("Name=" + system.Sid);
        Line("Description=" + system.Name);
        Line("Client=" + client);

        // LoadBalanced 必须带 GuiParm：landscape 里这类条目的 server 是占位符（实测 "SAPGRP"），
        // 真实连接信息只在 Messageserver 节点（host）里。不带 GuiParm 时 SAP 解析不出连接，
        // 表现为**静默失败**——连进程都不起，也不弹任何框。
        // 不指定 /G/ 登录组：由消息服务器返回可用组，避免写错组连到错误的实例。
        if (system.Kind == ConnKind.LoadBalanced && !string.IsNullOrWhiteSpace(system.MessageServer))
            Line("GuiParm=/M/" + system.MessageServer + "/S/" + system.Port);
        else if (!string.IsNullOrWhiteSpace(credential?.LogonGroup) && system.Kind == ConnKind.LoadBalanced)
            Line("GuiParm=" + BuildConnectionString(system, credential!.LogonGroup));

        Line("[User]");
        if (!string.IsNullOrWhiteSpace(credential?.User)) Line("Name=" + credential!.User);
        // 不写 Password：SAP GUI 7.40 PL1 起已废除 .sap 的密码字段（SAP Note 146173），
        // 写了只会让密码在磁盘上白躺几秒。密码由 LogonDialogGuard 在登录框出现后填入。
        Line("Language=" + (string.IsNullOrWhiteSpace(language) ? "ZH" : language));

        Line("[Function]");
        Line("Title=" + system.Name);
        // 仅在显式开启时写入 OK 代码，否则 SAP 每次都会弹安全确认框
        if (includeTcode && !string.IsNullOrWhiteSpace(credential?.Tcode))
        {
            Line("Command=" + credential!.Tcode);
            Line("Type=Transaction");
        }

        Line("[Configuration]");
        var workDir = string.IsNullOrWhiteSpace(settings.WorkDir) ? @"C:\SAPWorkDir" : settings.WorkDir;
        try { Directory.CreateDirectory(workDir); } catch { workDir = Path.GetTempPath().TrimEnd('\\'); }
        Line("WorkDir=" + workDir);
        Line("GuiSize=Maximized");

        Line("[Options]");
        Line("Reuse=" + (settings.ReuseSession ? "1" : "0"));

        return sb.ToString();
    }

    /// <summary>生成 SAP GUI 连接串（/H/、/S/、/M/、/G/），用于绕过 landscape 的兜底路径。</summary>
    public static string BuildConnectionString(SapSystem system, string? logonGroup = null)
    {
        switch (system.Kind)
        {
            case ConnKind.LoadBalanced:
            {
                var group = string.IsNullOrWhiteSpace(logonGroup) ? "SPACE" : logonGroup;
                return $"/M/{system.MessageServer}/S/3600/G/{group}";
            }
            case ConnKind.Router when !string.IsNullOrWhiteSpace(system.Router):
            {
                var routerHost = ExtractRouterHost(system.Router);
                var port = ExtractRouterPort(system.Router) ?? 3299;
                return $"/H/{routerHost}/S/{port}/H/{system.Host}/S/{system.Port}";
            }
            default:
                return $"/H/{system.Host}/S/{system.Port}";
        }
    }

    public static string ExtractRouterHost(string router)
    {
        // /H/116.1.202.228
        var idx = router.IndexOf("/H/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return router.Trim('/');
        var rest = router[(idx + 3)..];
        var end = rest.IndexOf('/');
        return end < 0 ? rest : rest[..end];
    }

    public static int? ExtractRouterPort(string router)
    {
        var idx = router.IndexOf("/S/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = router[(idx + 3)..];
        var end = rest.IndexOf('/');
        var text = end < 0 ? rest : rest[..end];
        return int.TryParse(text, out var port) ? port : null;
    }

    public static void DeleteQuietly(string? path, int delayMs = 4000)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        // 先用零字节覆写再删除，缩短明文残留窗口
                        try
                        {
                            var len = new FileInfo(path).Length;
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                var zeros = new byte[len];
                                fs.Write(zeros, 0, zeros.Length);
                                fs.Flush();
                            }
                        }
                        catch { }
                        File.Delete(path);
                    }
                    return;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}
