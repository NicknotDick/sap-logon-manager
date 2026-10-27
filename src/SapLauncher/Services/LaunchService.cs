using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SapLauncher.Interop;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 启动 SAP GUI 并自动登录。
/// 依据本机取证结论设计降级链（sapshcut 为 GUI 子系统，退出码不可靠，统一用「会话窗口」判定）：
///   L0  临时 .sap + sapshcut -open          —— 密码不进命令行（首选）
///   L0b 临时 .sap 交给 shell 关联启动        —— -open 不可用时
///   L1  sapshcut -sysname -pw               —— 需用户显式允许（密码出现在命令行）
///   L2  sapshcut -system + -guiparm -pw     —— landscape 描述失配时
///   L3  saplogon /SHORTCUT（不带密码）      —— 由 SAP 自己弹密码框
///   L4  仅启动 saplogon                     —— 手动选择
///
/// 成功判定必须同时覆盖三种情况，否则会误判失败并降级到 L3（丢掉密码弹登录框）：
///   1) 新开出会话窗口            —— 句柄此前不存在
///   2) 会话已存在但被复用        —— 窗口被激活到前台，标题含 SID
///   3) 原本没有该 SID 的会话     —— 轮询中出现标题含 SID 的会话窗口
/// </summary>
public sealed class LaunchService
{
    private SapGuiInfo _gui;

    public LaunchService(SapGuiInfo gui) => _gui = gui;

    public SapGuiInfo Gui => _gui;

    public void Refresh() => _gui = SapGuiLocator.Locate();

    /// <summary>会话窗口标题是否对应指定 SID（实测标题形如 "SAP PRD (1)"、"General Table Display S4Q (1)"）。</summary>
    public static bool TitleMatchesSid(string? title, string? sid)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sid)) return false;
        return Regex.IsMatch(title, $@"\b{Regex.Escape(sid.Trim())}\b", RegexOptions.IgnoreCase);
    }

    public async Task<LaunchResult> LaunchAsync(SapSystem system, Credential? credential, AppSettings settings,
        string client, string language, string? logonGroup, CancellationToken ct = default,
        IReadOnlyList<SapSystem>? siblings = null)
    {
        if (!_gui.IsInstalled || string.IsNullOrWhiteSpace(_gui.SapLogonPath))
            return new LaunchResult
            {
                Outcome = LaunchOutcome.Failed,
                Level = "L-1",
                Message = "未检测到 SAP GUI，请先安装 SAP GUI for Windows"
            };

        ct.ThrowIfCancellationRequested();

        var clientText = string.IsNullOrWhiteSpace(client) ? settings.DefaultClient : client;
        var lang = string.IsNullOrWhiteSpace(language) ? "ZH" : language;

        // 登录弹框守护：SAP 7.40+ 已废弃 .sap 里的密码字段，走 .sap 必然弹登录框。
        // 这里在等待会话期间接管该弹框，回填密码并点登录（单次登录最多 1 次）。
        // displayName 必须传入：.sap 启动的登录框标题是 SAP Logon 显示名（如 "DeRUCCI PRD"），
        // 不含 SID/客户端，guard 只能靠显示名匹配。遗漏此参数会导致 .sap 路径永远无法自动填密码。
        // 用命名参数显式写出，避免日后又漏掉这个可选参数。
        var guard = settings.AutoFillLogonDialog && !string.IsNullOrEmpty(credential?.Password)
            ? new LogonDialogGuard(
                sid: system.Sid,
                client: clientText,
                user: credential!.User,
                password: credential.Password,
                sapGuiDir: _gui.InstallDir,
                displayName: system.Name)
            : null;

        // SID 是否唯一。重名 SID（本机有 3 个 S4D、2 个 S4Q）不能用 -system=<SID> 启动，
        // 那会随机落到另一套系统；这类系统只能按显示名定位（.sap 的 Description）。
        var sidUnique = siblings is null ||
                        siblings.Count(x => string.Equals(x.Sid, system.Sid, StringComparison.OrdinalIgnoreCase)) <= 1;

        // 启动前快照：句柄集合 + 是否已有该 SID 的会话（复用场景判定用）
        var beforeHandles = NativeMethods.GetSessionWindows().Select(w => w.handle).ToHashSet();
        var hadSessionForSid = NativeMethods.GetSessionWindows()
            .Any(w => TitleMatchesSid(w.title, system.Sid));

        string? tempFile = null;

        // 登录方式覆盖（按连接设置）
        var method = (system.LoginMethod ?? "Auto").Trim();
        if (method.Equals("SapOnly", StringComparison.OrdinalIgnoreCase))
        {
            Log.Info($"按连接设置跳过自动登录，交给 SAP Logon：{system.Name}");
            // SapOnly 表示「密码由 SAP 自己管」，此处不接管弹框
            return await LaunchViaSapLogonOnlyAsync(system, credential, clientText, lang, beforeHandles, hadSessionForSid, null, ct);
        }

        try
        {
            var hasPassword = credential is not null && !string.IsNullOrEmpty(credential.Password);
            Log.Info($"登录凭据：user={credential?.User}，有密码={hasPassword}，记住={credential?.Remember}，客户端={clientText}，方式={method}");

            // CmdPw：绕过临时文件，直接用命令行带密码
            if (hasPassword && method.Equals("CmdPw", StringComparison.OrdinalIgnoreCase))
            {
                var (okPw, levelPw, howPw, msgPw) = await TryCommandLineAsync(system, credential!, clientText, lang,
                    logonGroup, beforeHandles, hadSessionForSid, sidUnique, ct);
                if (okPw)
                {
                    Log.Info($"登录成功（{levelPw}，连接指定）：{system.Name} / 判定={howPw}");
                    return Ok(levelPw, msgPw);
                }
                Log.Warn($"连接指定的命令行登录未成功：{system.Name}");
            }

            if (hasPassword)
            {
                // .sap 的 Description 让 SAP 从本机 landscape 精确定位系统（消息服务器 /
                // 登录组 / Router 全对，重名 SID 也安全）。密码**不写进 .sap**：
                // SAP GUI 7.40 PL1 起已废除该字段（SAP Note 146173），写了只会白冒
                // 磁盘明文的风险。密码改由 guard 在登录框出现后填入。
                var preferCmd = settings.AllowPasswordOnCommandLine;
                if (preferCmd) Log.Info($"按设置走命令行登录：{system.Name}");

                if (!preferCmd)
                {
                var effective = new Credential
                {
                    SystemUuid = system.Uuid,
                    Client = clientText,
                    User = credential!.User,
                    Password = "",
                    Language = lang,
                    Tcode = credential.Tcode,
                    LogonGroup = string.IsNullOrWhiteSpace(logonGroup) ? credential.LogonGroup : logonGroup,
                    Remember = credential.Remember
                };

                try
                {
                    tempFile = ShortcutFileBuilder.CreateSapFile(system, effective, settings, clientText, lang,
                        system.DirectTcode, out _);
                }
                catch (Exception ex)
                {
                    Log.Warn("生成 .sap 临时文件失败：" + ex.Message);
                }

                if (tempFile is not null)
                {
                    // L0：sapshcut -open
                    if (TryStart(_gui.SapShcutPath, new[] { "-open", tempFile }, out var err0, out var proc0))
                    {
                        var (ok, how) = await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, proc0, 20000, ct, guard);
                        if (ok)
                        {
                            Log.Info($"登录成功（L0）：{system.Name} / 客户端 {clientText} / 判定={how}");
                            return Ok("L0", "已登录 " + system.DisplayName);
                        }
                        if (guard is { FillCount: > 0 })
                        {
                            // 弹框已被接管但会话仍未建立 → 密码被拒，别再降级重复尝试
                            Log.Warn($"已自动填充登录框，SAP 仍未建立会话：{system.Name}（密码可能已失效）");
                            return new LaunchResult
                            {
                                Outcome = LaunchOutcome.NeedsManualLogin,
                                Level = "L0",
                                Message = $"已自动向登录框填入 {credential!.User} 的密码，但 SAP 未接受。" +
                                          "请右键连接 → 编辑凭据 更新密码，或联系管理员确认账号状态。"
                            };
                        }
                        if (NativeMethods.IsSapLogonDialogVisible(system.Name) || LogonDialogGuard.AnyLogonDialogVisible())
                        {
                            var dialogs = NativeMethods.GetVisibleDialogTitles();
                            Log.Warn($"L0 后 SAP 弹出登录框：{system.Name}；可见对话框：[{string.Join(" | ", dialogs)}]");
                            // 不在此返回：SAP GUI 7.40+ 已忽略 .sap 的密码字段，
                            // 这是预期行为而非异常，继续按降级链尝试其它方式。
                        }
                    }
                    else
                    {
                        Log.Warn("sapshcut -open 启动失败：" + err0);
                    }

                    // L0b：shell 关联
                    if (TryShellOpen(tempFile, out var err1))
                    {
                        var (ok, how) = await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, null, 15000, ct, guard);
                        if (ok)
                        {
                            Log.Info($"登录成功（L0b）：{system.Name} / 判定={how}");
                            return Ok("L0b", "已登录 " + system.DisplayName);
                        }
                    }
                    else
                    {
                        Log.Warn("shell 打开 .sap 失败：" + err1);
                    }
                }
                else
                {
                    Log.Info("已关闭命令行密码，且 .sap 无法带密码（SAP GUI 7.40+），降级 L3：" + system.Name);
                }
                }

                // L1 / L2：命令行密码（SAP Note 103019 仍支持 -pw 参数）
                {
                    var (okCmd, levelCmd, howCmd, msgCmd) = await TryCommandLineAsync(system, credential!, clientText,
                        lang, logonGroup, beforeHandles, hadSessionForSid, sidUnique, ct);
                    if (okCmd)
                    {
                        Log.Info($"登录成功（{levelCmd}）：{system.Name} / 判定={howCmd}");
                        return Ok(levelCmd, msgCmd);
                    }

                    // 密码已经通过命令行送出，SAP 仍然弹登录框 → 这组凭据被拒绝。
                    // 此时不再降级到 L3：那会再弹一个框，且结论相同。
                    if (NativeMethods.IsSapLogonDialogVisible(system.Name) || LogonDialogGuard.AnyLogonDialogVisible())
                    {
                        var dialogs = NativeMethods.GetVisibleDialogTitles();
                        var extra = dialogs
                            .Where(d => !string.Equals(d, system.Name, StringComparison.CurrentCultureIgnoreCase))
                            .ToList();
                        Log.Warn($"命令行带密码后 SAP 仍弹出登录框：{system.Name}；对话框：[{string.Join(" | ", dialogs)}]");

                        var reason = extra.Any(d => d.Contains("密码", StringComparison.OrdinalIgnoreCase) ||
                                                    d.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                                                    d.Contains("Change", StringComparison.OrdinalIgnoreCase) ||
                                                    d.Contains("修改", StringComparison.OrdinalIgnoreCase))
                            ? "SAP 提示需要修改密码，账号密码已过期"
                            : "密码错误、已被修改，或账号被锁定";

                        return new LaunchResult
                        {
                            Outcome = LaunchOutcome.NeedsManualLogin,
                            Level = levelCmd == "-" ? "L1" : levelCmd,
                            Message = $"SAP 拒绝了账号 {credential!.User}（客户端 {clientText}）：{reason}。" +
                                      "请在登录框中输入，或右键连接 → 编辑凭据 更新密码。"
                        };
                    }

                    Log.Warn($"命令行登录未检出会话：{system.Name}");
                }
            }

            return await LaunchViaSapLogonOnlyAsync(system, credential, clientText, lang, beforeHandles, hadSessionForSid, guard, ct);
        }
        catch (OperationCanceledException)
        {
            return new LaunchResult { Outcome = LaunchOutcome.Failed, Level = "-", Message = "已取消" };
        }
        catch (Exception ex)
        {
            Log.Error("启动登录流程异常", ex);
            return new LaunchResult { Outcome = LaunchOutcome.Failed, Level = "-", Message = "启动失败：" + ex.Message };
        }
        finally
        {
            ShortcutFileBuilder.DeleteQuietly(tempFile, 8000);
        }
    }

    /// <summary>
    /// 构造命令行登录的首选参数（L1）。抽成纯函数便于自检。
    /// 关键规则（决定性，勿改）：
    ///  - SID 唯一 → 用 <c>-system=&lt;SID&gt;</c>。SAP 会拿 SID 去 landscape 反查完整连接
    ///    （消息服务器 + 登录组），不会弹「SAP GUI 快捷方式的部分数据不可用：系统标识未知」。
    ///  - SID 重名 → 只能用 <c>-sysname=&lt;显示名&gt;</c>（-system 会随机落到同名系统）。
    ///  - **一律不带 <c>-guiparm</c>**：guiparm 会覆盖 landscape 解析，且连接串内没有 SID，
    ///    正是「系统标识未知」的直接来源。
    /// </summary>
    internal static List<string> BuildShortcutArgs(SapSystem system, Credential credential,
        string clientText, string lang, bool sidUnique)
    {
        var args = new List<string>();
        if (sidUnique && !string.IsNullOrWhiteSpace(system.Sid)) args.Add("-system=" + system.Sid);
        else args.Add("-sysname=" + system.Name);
        args.Add("-client=" + clientText);
        args.Add("-language=" + lang);
        if (!string.IsNullOrWhiteSpace(credential.User)) args.Add("-user=" + credential.User);
        args.Add("-pw=" + credential.Password);
        if (system.DirectTcode && !string.IsNullOrWhiteSpace(credential.Tcode))
        {
            // 与 SAP 官方示例一致：-type=Transaction 后接 -command=<tcode>（command 放最后）
            args.Add("-type=Transaction");
            args.Add("-command=" + credential.Tcode);
        }
        return args;
    }

    /// <summary>命令行带密码登录：优先 -system（SID 唯一），失败再 -system + -guiparm 兜底。</summary>
    private async Task<(bool ok, string level, string how, string message)> TryCommandLineAsync(
        SapSystem system, Credential credential, string clientText, string lang, string? logonGroup,
        HashSet<IntPtr> beforeHandles, bool hadSessionForSid, bool sidUnique, CancellationToken ct)
    {
        var args = BuildShortcutArgs(system, credential, clientText, lang, sidUnique);

        if (TryStart(_gui.SapShcutPath, args, out _))
        {
            var (ok, how) = await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, null, 15000, ct);
            if (ok) return (true, "L1", how, "已登录 " + system.DisplayName + "（密码曾短暂出现在命令行）");
        }

        var conn = ShortcutFileBuilder.BuildConnectionString(system,
            string.IsNullOrWhiteSpace(logonGroup) ? credential.LogonGroup : logonGroup);
        var args2 = new List<string>
        {
            "-system=" + system.Sid,
            "-guiparm=" + conn,
            "-client=" + clientText,
            "-language=" + lang
        };
        if (!string.IsNullOrWhiteSpace(credential.User)) args2.Add("-user=" + credential.User);
        args2.Add("-pw=" + credential.Password);

        if (TryStart(_gui.SapShcutPath, args2, out _))
        {
            var (ok, how) = await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, null, 15000, ct);
            if (ok) return (true, "L2", how, "已登录 " + system.DisplayName + "（通过连接串）");
        }

        return (false, "-", "", "");
    }

    /// <summary>交给 SAP Logon 自己处理：可带用户名预填，但绝不带密码。</summary>
    private async Task<LaunchResult> LaunchViaSapLogonOnlyAsync(SapSystem system, Credential? credential,
        string clientText, string lang, HashSet<IntPtr> beforeHandles, bool hadSessionForSid,
        LogonDialogGuard? guard, CancellationToken ct)
    {
        var shortcutArg = BuildShortcutArg(system, clientText, credential?.User, lang);
        if (TryStart(_gui.SapLogonPath, new[] { "/SHORTCUT=" + shortcutArg }, out var err3))
        {
            var (opened, howL3) = await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, null, 15000, ct, guard);
            Log.Info($"L3：{system.Name}，窗口出现={opened} / 判定={howL3} / 弹框回填={guard?.FillCount ?? 0}");

            // 弹框守护接管后登录成功：这是真正的自动登录，不该再报「需要手工输入」
            if (opened && guard is { FillCount: > 0 })
                return Ok("L3+", "已登录 " + system.DisplayName + "（自动填充登录框，未走命令行密码）");

            return new LaunchResult
            {
                Outcome = LaunchOutcome.NeedsManualLogin,
                Level = "L3",
                Message = opened
                    ? "已打开登录界面，请输入密码完成登录（可在 SAP 登录框勾选保存密码，之后即可全自动）"
                    : "已向 SAP Logon 发送登录请求，请手动完成登录"
            };
        }
        Log.Warn("saplogon /SHORTCUT 启动失败：" + err3);

        if (TryStart(_gui.SapLogonPath, Array.Empty<string>(), out var err4))
        {
            await WaitForSessionAsync(beforeHandles, hadSessionForSid, system, null, 10000, ct);
            return new LaunchResult
            {
                Outcome = LaunchOutcome.NeedsManualLogin,
                Level = "L4",
                Message = $"已打开 SAP Logon，请手动选择 {system.DisplayName}（{system.Sid}）"
            };
        }

        return new LaunchResult
        {
            Outcome = LaunchOutcome.Failed,
            Level = "L4",
            Message = "无法启动 SAP GUI：" + err4
        };
    }

    private static LaunchResult Ok(string level, string message) => new()
    {
        Outcome = LaunchOutcome.Success,
        Level = level,
        Message = message
    };

    /// <summary>
    /// 等待会话就绪。覆盖：新窗口 / 新建会话（标题含 SID）/ 复用并激活到前台 / 启动器已交接。
    /// SAP 一旦弹出登录框立即返回失败，避免白等超时。
    /// 返回 (是否成功, 判定依据) 便于日志诊断。
    /// </summary>
    internal static async Task<(bool ok, string how)> WaitForSessionAsync(HashSet<IntPtr> beforeHandles,
        bool hadSessionForSid, SapSystem system, Process? launcher, int timeoutMs, CancellationToken ct,
        LogonDialogGuard? guard = null)
    {
        var sw = Stopwatch.StartNew();
        long graceUntilMs = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (ct.IsCancellationRequested) return (false, "已取消");
            try
            {
                var sessions = NativeMethods.GetSessionWindows();
                var foreground = NativeMethods.GetForegroundWindow();

                foreach (var (handle, title) in sessions)
                {
                    if (!beforeHandles.Contains(handle))
                        return (true, "新开会话窗口");

                    if (!TitleMatchesSid(title, system.Sid)) continue;

                    if (!hadSessionForSid) return (true, "该 SID 会话已建立");
                    if (foreground == handle) return (true, "复用并激活已有会话");
                }

                // 启动器进程已退出且目标 SID 会话存在 → 已交接给 SAP GUI
                // （等 2 秒后再采信，避免启动器瞬时崩溃被误判为成功）
                if (sw.ElapsedMilliseconds >= 2000 && launcher is { HasExited: true } &&
                    sessions.Any(w => TitleMatchesSid(w.title, system.Sid)))
                    return (true, "启动器已交接");

                // 弹框守护：能接管就先接管，随后给一段宽限期，避免"刚填完密码"被当成失败
                if (guard is not null && guard.TryHandleOnce())
                {
                    graceUntilMs = sw.ElapsedMilliseconds + 10000;
                    try { await Task.Delay(400, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return (false, "已取消"); }
                    continue;
                }

                // SAP 弹出登录框：交由上层给出准确提示
                var dialogVisible = NativeMethods.IsSapLogonDialogVisible(system.Name) ||
                                    LogonDialogGuard.AnyLogonDialogVisible();
                if (dialogVisible && sw.ElapsedMilliseconds >= graceUntilMs)
                    return (false, "SAP 弹出登录框");
            }
            catch { }

            try { await Task.Delay(400, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return (false, "已取消"); }
        }
        return (false, "超时未检出");
    }

    private static bool TryStart(string exe, IEnumerable<string> args, out string error)
        => TryStart(exe, args, out error, out _);

    private static bool TryStart(string exe, IEnumerable<string> args, out string error, out Process? process)
    {
        error = "";
        process = null;
        try
        {
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                error = "程序不存在：" + exe;
                return false;
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            process = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryShellOpen(string path, out string error)
    {
        error = "";
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildShortcutArg(SapSystem system, string client, string? user, string language)
    {
        var parts = new List<string>
        {
            $"-sysname=\"{system.Name}\"",
            "-client=" + client,
            "-language=" + language
        };
        if (!string.IsNullOrWhiteSpace(user)) parts.Add("-user=" + user);
        return string.Join(" ", parts);
    }
}
