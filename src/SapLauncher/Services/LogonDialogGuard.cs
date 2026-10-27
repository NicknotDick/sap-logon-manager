using System.Text.RegularExpressions;
using SapLauncher.Interop;

namespace SapLauncher.Services;

/// <summary>登录弹框标题解析结果。实测格式：<c>SAP GUI 快捷方式 - 登录 (PRD, 800, ZH, )</c>。</summary>
public readonly record struct LogonDialogInfo(string Sid, string Client, string Language, string User);

/// <summary>
/// 登录弹框守护：当 SAP 弹出「快捷方式 - 登录」对话框时，用凭据库里的密码自动填充并点击登录。
///
/// 设计要点（三条安全底线，改动前请先读）：
///  1. <b>只认可信进程</b>：窗口所属进程必须是 SAP 安装目录下的 saplogon.exe / sapgui.exe，
///     否则任何伪造的同结构弹框都能骗走密码。
///  2. <b>只认本次登录目标</b>：标题里的 SID 与客户端必须与本次登录一致，
///     防止把 PRD 的密码填进 QAS 的框里。
///  3. <b>单次封顶</b>：一次登录尝试最多回填 1 次。弹框出现往往意味着 SAP 已经拒绝了密码，
///     回填同一个密码毫无意义，反复尝试还会把账号试锁。
/// </summary>
public sealed class LogonDialogGuard
{
    /// <summary>单次登录允许的回填次数。</summary>
    public const int MaxFillsPerLaunch = 1;

    private readonly string _sid;
    private readonly string _client;
    private readonly string _displayName;
    private readonly string? _user;
    private readonly string? _password;
    private readonly string? _sapGuiDir;
    private readonly HashSet<long> _handled = new();

    public LogonDialogGuard(string? sid, string? client, string? user, string? password, string? sapGuiDir,
        string? displayName = null)
    {
        _sid = (sid ?? "").Trim();
        _client = (client ?? "").Trim();
        _displayName = (displayName ?? "").Trim();
        _user = user;
        _password = password;
        _sapGuiDir = sapGuiDir;
    }

    /// <summary>已回填次数（0 表示从未接管过弹框）。</summary>
    public int FillCount { get; private set; }

    /// <summary>是否还有回填额度。没有密码或额度用完时为 false。</summary>
    public bool CanFill => FillCount < MaxFillsPerLaunch && !string.IsNullOrEmpty(_password);

    /// <summary>
    /// 检查一次当前可见弹框，命中则回填密码并点登录。返回 true 表示本次确实动过界面。
    /// 同一窗口句柄只处理一次，避免重复点击。
    /// </summary>
    public bool TryHandleOnce()
    {
        if (!CanFill) return false;

        foreach (var (handle, title) in NativeMethods.GetVisibleDialogWindows())
        {
            if (_handled.Contains(handle.ToInt64())) continue;

            // 两种标题格式都支持：
            //   ① 命令行 -system 启动："SAP GUI 快捷方式 - 登录 (PRD, 800, ZH, )"
            //   ② .sap 启动：标题即 SAP Logon 显示名，如 "DeRUCCI PRD"（不含 SID/客户端）
            LogonDialogInfo info;
            if (TryParseLogonTitle(title, out var parsed)) info = parsed;
            else if (TryMatchDisplayName(title, out var byName)) info = byName;
            else continue;

            if (!Matches(info, out var why))
            {
                Log.Warn($"登录弹框不匹配本次目标，已跳过：{title}（{why}）");
                continue;
            }

            if (!NativeMethods.IsTrustedSapWindow(handle, _sapGuiDir, out var image, out var reason))
            {
                Log.Warn($"登录弹框来自不可信进程，拒绝填充：{title}（{reason}）");
                _handled.Add(handle.ToInt64());
                continue;
            }

            _handled.Add(handle.ToInt64());
            if (!NativeMethods.FillLogonDialog(handle, _user, _password!, out var detail))
            {
                Log.Warn($"自动填充登录框失败：{title}（{detail}）");
                continue;
            }

            FillCount++;
            Log.Info($"已自动填充登录框并点击登录：{title} / 进程 {image} / 客户端 {info.Client} / {detail}");
            return true;
        }

        return false;
    }

    /// <summary>当前是否存在「快捷方式 - 登录」弹框（与是否接管无关，用于判定失败原因）。</summary>
    public static bool AnyLogonDialogVisible()
    {
        foreach (var (_, title) in NativeMethods.GetVisibleDialogWindows())
            if (TryParseLogonTitle(title, out _)) return true;
        return false;
    }

    /// <summary>解析标题中的 (SID, 客户端, 语言, 用户)。前缀随界面语言变化，只取末尾括号内的内容。</summary>
    public static bool TryParseLogonTitle(string? title, out LogonDialogInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(title)) return false;

        var m = Regex.Match(title, @"\(([^()]*)\)\s*$");
        if (!m.Success) return false;

        var parts = m.Groups[1].Value.Split(',');
        if (parts.Length < 2) return false;

        var sid = parts[0].Trim();
        var client = parts[1].Trim();
        var lang = parts.Length > 2 ? parts[2].Trim() : "";
        var user = parts.Length > 3 ? parts[3].Trim() : "";

        // 客户端一定是数字；否则多半是别种弹框（例如「系统标识未知」提示），不参与填充
        if (client.Length == 0 || !client.All(char.IsDigit)) return false;

        info = new LogonDialogInfo(sid, client, lang, user);
        return true;
    }

    /// <summary>
    /// 把窗口标题解析为登录信息，支持两种实测格式：
    ///   ① 命令行 -system 启动：<c>SAP GUI 快捷方式 - 登录 (PRD, 800, ZH, )</c>
    ///   ② .sap 启动：标题即 SAP Logon 显示名，如 <c>DeRUCCI PRD</c>（不含 SID/客户端）
    /// 返回 null 表示该标题不是可识别的登录框。
    /// </summary>
    public LogonDialogInfo? MatchTitle(string? title)
    {
        if (TryParseLogonTitle(title, out var parsed)) return parsed;
        if (TryMatchDisplayName(title!, out var byName)) return byName;
        return null;
    }

    /// <summary>
    /// 格式②：.sap 启动时登录框标题就是 SAP Logon 里的显示名（实测 "DeRUCCI PRD"），
    /// 不含 SID 与客户端，因此只能按显示名精确匹配，并用本次目标补全 info。
    /// </summary>
    private bool TryMatchDisplayName(string title, out LogonDialogInfo info)
    {
        info = default;
        if (_displayName.Length == 0) return false;
        if (!string.Equals((title ?? "").Trim(), _displayName, StringComparison.CurrentCultureIgnoreCase))
            return false;

        info = new LogonDialogInfo(_sid, _client, "", "");
        return true;
    }

    /// <summary>SID 与客户端是否与本次登录目标一致。SID 为空时只比客户端。</summary>
    public bool Matches(LogonDialogInfo info, out string reason)
    {
        if (_sid.Length > 0 && info.Sid.Length > 0 &&
            !string.Equals(info.Sid, _sid, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"SID 不符（弹框 {info.Sid} ≠ 目标 {_sid}）";
            return false;
        }

        if (_client.Length > 0 && !string.Equals(info.Client, _client, StringComparison.Ordinal))
        {
            reason = $"客户端不符（弹框 {info.Client} ≠ 目标 {_client}）";
            return false;
        }

        reason = "";
        return true;
    }
}
