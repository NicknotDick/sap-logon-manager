using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using SapLauncher.Interop;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>自检：验证配置解析、加密往返、快捷方式生成与各窗口 XAML 能否正常实例化。</summary>
public static class SelfTest
{
    public static string Run()
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;

        void Check(string name, Action action)
        {
            try
            {
                action();
                pass++;
                sb.AppendLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                fail++;
                var detail = ex.InnerException is null ? "" : " ← " + ex.InnerException.Message;
                sb.AppendLine($"[FAIL] {name} :: {ex.GetType().Name}: {ex.Message}{detail}");
            }
        }

        sb.AppendLine("===== 自检报告 =====");
        sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("数据目录：" + AppPaths.Root);
        sb.AppendLine();

        // 1. SAP GUI 定位
        Check("SAP GUI 定位", () =>
        {
            var gui = SapGuiLocator.Locate();
            sb.AppendLine($"      已安装={gui.IsInstalled} 版本={gui.Version}");
            sb.AppendLine($"      目录={gui.InstallDir}");
            sb.AppendLine($"      saplogon={(File.Exists(gui.SapLogonPath) ? "存在" : "缺失")} saplogon={(File.Exists(gui.SapShcutPath) ? "存在" : "缺失")}");
            if (!gui.IsInstalled) throw new InvalidOperationException("未检测到 SAP GUI");
        });

        // 2. 配置解析
        LandscapeScanResult? scan = null;
        Check("SAP Logon 配置解析", () =>
        {
            scan = LandscapeReader.Scan();
            sb.AppendLine($"      来源文件 {scan.Sources.Count} 个，条目 {scan.Entries.Count} 条");
            foreach (var f in scan.Sources) sb.AppendLine("        · " + f);
            foreach (var w in scan.Warnings) sb.AppendLine("      警告：" + w);
            if (scan.Entries.Count == 0) throw new InvalidOperationException("未解析到任何系统");
        });

        // 3. 合并逻辑
        if (scan is not null)
        {
            Check("清单合并（新增/更新/失效）", () =>
            {
                var list = new List<SapSystem>();
                var r1 = ListSyncService.Merge(list, scan, new AppSettings());
                sb.AppendLine($"      首次合并：{r1.Summary}");

                // 模拟改名
                var first = list[0];
                var original = first.Name;
                first.Name = original + "-old";
                var r2 = ListSyncService.Merge(list, scan, new AppSettings());
                sb.AppendLine($"      模拟改名后：{r2.Summary}");
                if (r2.Renamed < 1) throw new InvalidOperationException("改名未被识别");

                // 模拟条目消失
                var removed = list[1];
                var fake = new LandscapeScanResult();
                foreach (var e in scan.Entries.Where(x => x.Uuid != removed.Uuid)) fake.Entries.Add(e);
                var r3 = ListSyncService.Merge(list, fake, new AppSettings());
                sb.AppendLine($"      模拟失效后：{r3.Summary}");
                if (!removed.IsStale) throw new InvalidOperationException("失效标记未生效");
                if (list.Count != scan.Entries.Count) throw new InvalidOperationException("条目数异常（不应删除）");
            });

            // 4. 备注线索识别
            Check("备注账号线索识别", () =>
            {
                int withCandidates = 0, totalCandidates = 0;
                foreach (var e in scan.Entries)
                {
                    var c = MemoCredentialParser.Parse(e.Memo);
                    if (c.Count > 0) withCandidates++;
                    totalCandidates += c.Count;
                }
                sb.AppendLine($"      {withCandidates} 个系统识别出线索，共 {totalCandidates} 条");
            });
        }

        // 5. 快捷方式生成
        Check("生成 .sap 内容（GBK + CRLF）", () =>
        {
            var sys = new SapSystem
            {
                Uuid = "test",
                Name = "兆威500",
                Sid = "S4Q",
                Host = "10.1.160.197",
                Port = 3200,
                Kind = ConnKind.Direct
            };
            var cred = new Credential { User = "TESTUSER", Password = "P@ss&1:x!$", Tcode = "SE80" };
            var content = ShortcutFileBuilder.BuildContent(sys, cred, new AppSettings(), "300", "ZH");
            var bytes = Interop.NativeMethods.ToAnsi(content);

            if (!content.Contains("\r\n")) throw new InvalidOperationException("缺少 CRLF");
            if (!content.Contains("Description=兆威500")) throw new InvalidOperationException("缺少 Description");
            // SAP GUI 7.40+ 已废除 .sap 密码字段：内容里绝不允许出现密码
            if (content.Contains("Password=") || content.Contains("P@ss&1:x!$"))
                throw new InvalidOperationException(".sap 不应包含密码");
            if (!content.Contains("Name=TESTUSER")) throw new InvalidOperationException("缺少用户名");
            if (!content.Contains("[Options]")) throw new InvalidOperationException("缺少 [Options]");

            // 中文应编码为 GBK（兆 = D5 D7）
            bool gbk = bytes.Length > 2 && bytes.Contains((byte)0xD5) && bytes.Contains((byte)0xD7);
            sb.AppendLine($"      字节数 {bytes.Length}，GBK 编码={gbk}");
            if (!gbk) throw new InvalidOperationException("中文未按 GBK 编码");
        });

        // 9. 事务码开关与稳定文件名（避免 SAP 快捷方式安全确认框每次弹）
        Check("事务码开关 / 临时文件名稳定", () =>
        {
            var sandbox = Path.Combine(AppPaths.Root, "selftest-shortcut");
            var original = AppPaths.Root;
            try
            {
                AppPaths.OverrideRoot(sandbox);
                Directory.CreateDirectory(AppPaths.Temp);

                var sys = new SapSystem { Uuid = "u-x", Name = "兆威500", Sid = "S4Q", Host = "10.1.160.197", Port = 3200 };
                var cred = new Credential { User = "SV01", Password = "p@ss", Tcode = "SE16N" };
                var settings = new AppSettings();

                // a) 关闭（默认）：不得出现 OK 代码
                var off = ShortcutFileBuilder.BuildContent(sys, cred, settings, "500", "ZH", false);
                if (off.Contains("Command=") || off.Contains("Type=Transaction"))
                    throw new InvalidOperationException("关闭事务码时不应写入 OK 代码");
                if (off.Contains("SE16N")) throw new InvalidOperationException("关闭事务码时不应出现事务码");

                // b) 开启：应出现
                var on = ShortcutFileBuilder.BuildContent(sys, cred, settings, "500", "ZH", true);
                if (!on.Contains("Command=SE16N") || !on.Contains("Type=Transaction"))
                    throw new InvalidOperationException("开启事务码时应写入 OK 代码");

                // c) 文件名稳定：同系统+客户端+账号 → 同路径；换账号 → 不同路径
                var p1 = ShortcutFileBuilder.CreateSapFile(sys, cred, settings, "500", "ZH", false, out _);
                var p2 = ShortcutFileBuilder.CreateSapFile(sys, cred, settings, "500", "ZH", false, out _);
                if (p1 != p2) throw new InvalidOperationException("同账号两次登录的临时文件名应一致（否则 SAP 无法记住决定）");
                if (Path.GetFileName(p1)!.Contains(Guid.NewGuid().ToString("N")))
                    throw new InvalidOperationException("临时文件名不应含随机 GUID");

                var other = new Credential { User = "SD02", Password = "p@ss", Tcode = "SE16N" };
                var p3 = ShortcutFileBuilder.CreateSapFile(sys, other, settings, "500", "ZH", false, out _);
                if (p3 == p1) throw new InvalidOperationException("不同账号应使用不同临时文件");

                sb.AppendLine($"      关闭无 OK 代码、开启有；文件名 {Path.GetFileName(p1)}");
            }
            finally
            {
                AppPaths.OverrideRoot(original);
                try { Directory.Delete(sandbox, true); } catch { }
            }
        });

        // 6. 连接串生成
        Check("连接串生成（直连/Router/负载均衡）", () =>
        {
            var direct = new SapSystem { Kind = ConnKind.Direct, Host = "10.1.1.1", Port = 3200 };
            var router = new SapSystem
            {
                Kind = ConnKind.Router,
                Host = "192.168.36.250",
                Port = 3200,
                Router = "/H/116.1.202.228"
            };
            var lb = new SapSystem { Kind = ConnKind.LoadBalanced, MessageServer = "s4prdascs.derucci.com" };

            var a = ShortcutFileBuilder.BuildConnectionString(direct);
            var b = ShortcutFileBuilder.BuildConnectionString(router);
            var c = ShortcutFileBuilder.BuildConnectionString(lb, "SPACE");
            sb.AppendLine("      直连：" + a);
            sb.AppendLine("      Router：" + b);
            sb.AppendLine("      负载均衡：" + c);

            if (a != "/H/10.1.1.1/S/3200") throw new InvalidOperationException("直连串错误");
            if (b != "/H/116.1.202.228/S/3299/H/192.168.36.250/S/3200") throw new InvalidOperationException("Router 串错误");
            if (!c.StartsWith("/M/s4prdascs.derucci.com/S/3600/G/", StringComparison.Ordinal)) throw new InvalidOperationException("负载均衡串错误");
        });

        // 6b. 命令行定位参数：唯一 SID 必须用 -system，且绝不带 -guiparm / -sysname
        Check("命令行定位：唯一 SID 用 -system 且不带 guiparm", () =>
        {
            var sys = new SapSystem
            {
                Uuid = "u2",
                Name = "DeRUCCI PRD",
                Sid = "PRD",
                Kind = ConnKind.LoadBalanced,
                MessageServer = "s4prdascs.derucci.com"
            };
            var cred = new Credential { User = "89000592", Password = "pw", Tcode = "SE16N" };

            var unique = LaunchService.BuildShortcutArgs(sys, cred, "800", "ZH", true);
            sb.AppendLine("      唯一SID：" + string.Join(" ", unique));
            if (!unique.Contains("-system=PRD")) throw new InvalidOperationException("唯一 SID 未用 -system");
            if (unique.Any(x => x.StartsWith("-sysname", StringComparison.Ordinal)))
                throw new InvalidOperationException("唯一 SID 不应使用 -sysname（XML landscape 下会弹「系统标识未知」）");
            if (unique.Any(x => x.StartsWith("-guiparm", StringComparison.Ordinal)))
                throw new InvalidOperationException("不应带 -guiparm（会覆盖 landscape 解析，正是「系统标识未知」来源）");

            var dup = LaunchService.BuildShortcutArgs(sys, cred, "800", "ZH", false);
            sb.AppendLine("      重名SID：" + string.Join(" ", dup));
            if (!dup.Any(x => x.StartsWith("-sysname=", StringComparison.Ordinal)))
                throw new InvalidOperationException("重名 SID 应用 -sysname");
            if (dup.Any(x => x.StartsWith("-system=", StringComparison.Ordinal)))
                throw new InvalidOperationException("重名 SID 不应使用 -system");

            // 勾选「登录后直达」+ 填了事务码 → 命令行必须带 -command / -type=Transaction
            var sysT = new SapSystem
            {
                Uuid = "u3", Name = "DeRUCCI PRD", Sid = "PRD",
                Kind = ConnKind.LoadBalanced, DirectTcode = true
            };
            var credT = new Credential { User = "89000592", Password = "pw", Tcode = "SE16N" };
            var withCmd = LaunchService.BuildShortcutArgs(sysT, credT, "800", "ZH", true);
            sb.AppendLine("      直达事务码：" + string.Join(" ", withCmd));
            if (!withCmd.Contains("-command=SE16N"))
                throw new InvalidOperationException("勾选直达事务码却未带 -command");
            if (!withCmd.Contains("-type=Transaction"))
                throw new InvalidOperationException("勾选直达事务码却未带 -type=Transaction");

            // 未勾选 → 不带 -command
            var noCmd = LaunchService.BuildShortcutArgs(
                new SapSystem { Uuid = "u4", Name = "X", Sid = "PRD", DirectTcode = false },
                credT, "800", "ZH", true);
            if (noCmd.Any(x => x.StartsWith("-command", StringComparison.Ordinal)))
                throw new InvalidOperationException("未勾选直达却带了 -command");
        });

        // 7. 加密往返（隔离目录，不触碰真实凭据）
        Check("AES-256-GCM 加解密与篡改检测", () =>
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = VaultService.DeriveKey("unit-test-password", salt, 120_000);
            var plain = Encoding.UTF8.GetBytes("hello 世界 12345");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
            var back = new byte[plain.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, back);
            if (!back.SequenceEqual(plain)) throw new InvalidOperationException("解密结果不一致");

            tag[0] ^= 0xFF;
            bool detected = false;
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, new byte[plain.Length]);
            }
            catch (CryptographicException) { detected = true; }
            if (!detected) throw new InvalidOperationException("篡改未被检测");
        });

        // 8. 凭据库完整往返（仅在隔离数据目录内进行）
        Check("凭据库写入/锁定/解锁", () =>
        {
            var original = AppPaths.Root;
            var sandbox = Path.Combine(original, "selftest-vault");
            try
            {
                AppPaths.OverrideRoot(sandbox);
                Directory.CreateDirectory(sandbox);

                var vault = new VaultService();
                vault.Initialize("unit-test-password");
                vault.Upsert(new Credential
                {
                    SystemUuid = "uuid-1",
                    Client = "300",
                    User = "TESTUSER",
                    Password = "P@ss&1:x!$",
                    Remember = true
                });
                vault.Lock();

                var reopened = new VaultService();
                if (!reopened.Unlock("unit-test-password", out var err))
                    throw new InvalidOperationException("正确密码解锁失败：" + err);
                var item = reopened.Get("uuid-1", "300");
                if (item is null || item.Password != "P@ss&1:x!$")
                    throw new InvalidOperationException("凭据内容不一致");

                var wrong = new VaultService();
                if (wrong.Unlock("bad-password", out _))
                    throw new InvalidOperationException("错误密码竟然解锁成功");

                sb.AppendLine("      写入→锁定→解锁→校验 通过，错误密码被拒绝");
            }
            finally
            {
                AppPaths.OverrideRoot(original);
                try { Directory.Delete(sandbox, true); } catch { }
            }
        });

        // 9. 导出 / 导入往返
        Check("导出/导入往返（连接 + 加密凭据）", () =>
        {
            var original = AppPaths.Root;
            var sandbox = Path.Combine(original, "selftest-export");
            try
            {
                Directory.CreateDirectory(sandbox);
                AppPaths.OverrideRoot(Path.Combine(sandbox, "data"));
                Directory.CreateDirectory(AppPaths.Root);

                var vault = new VaultService();
                vault.Initialize("export-pwd");
                vault.Upsert(new Credential
                {
                    SystemUuid = "u1",
                    Client = "300",
                    User = "EXPORTUSER",
                    Password = "P@ss&1:x!$",
                    Remember = true
                });

                var systems = new List<SapSystem>
                {
                    new() { Uuid = "u1", Name = "导出测试", Sid = "S4Q", Host = "1.2.3.4", Port = 3200, Group = "G1",
                            DirectTcode = true, LoginMethod = "CmdPw" }
                };
                var groups = new List<GroupDef> { new() { Name = "G1" } };
                var file = Path.Combine(sandbox, "bundle.json");

                ExportImportService.Export(file, systems, groups, vault, "export-pwd");
                var bundle = ExportImportService.Read(file);
                if (bundle is null) throw new InvalidOperationException("导出文件无法解析");
                if (!bundle.IncludesCredentials) throw new InvalidOperationException("凭据未写入");

                var target = new List<SapSystem>();
                var targetGroups = new List<GroupDef>();
                var (added, updated, skipped) = ExportImportService.Apply(bundle, target, targetGroups, new ImportOptions());
                if (added != 1 || target.Count != 1) throw new InvalidOperationException("连接导入数量异常");
                if (!targetGroups.Any(g => g.Name == "G1")) throw new InvalidOperationException("分组未导入");
                var t0 = target[0];
                if (!t0.DirectTcode) throw new InvalidOperationException("导出/导入未保住 DirectTcode（登录后直达事务码）");
                if (!string.Equals(t0.LoginMethod, "CmdPw", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("导出/导入未保住 LoginMethod");

                var fresh = new VaultService();
                fresh.Initialize("other-pwd");
                var imported = ExportImportService.ImportCredentials(bundle, fresh, "export-pwd");
                if (imported != 1) throw new InvalidOperationException("凭据导入失败");
                var restored = fresh.Get("u1", "300");
                if (restored is null || restored.Password != "P@ss&1:x!$")
                    throw new InvalidOperationException("凭据内容不一致");

                var wrong = ExportImportService.ImportCredentials(bundle, fresh, "bad-pwd");
                if (wrong != -1) throw new InvalidOperationException("错误导出密码未被拒绝");

                sb.AppendLine($"      连接 新增{added}/更新{updated}/跳过{skipped}，凭据 1 条，错误密码被拒绝");
            }
            finally
            {
                AppPaths.OverrideRoot(original);
                try { Directory.Delete(sandbox, true); } catch { }
            }
        });

        // 9. 批量操作（多选移动分组）与客户端显示
        Check("批量移动分组 / 客户端显示", () =>
        {
            var all = new List<SapSystem>
            {
                new() { Uuid = "a", Name = "A", IsSelected = true },
                new() { Uuid = "b", Name = "B", IsSelected = true },
                new() { Uuid = "c", Name = "C", IsSelected = false },
            };
            var groups = new List<GroupDef>();

            var selected = all.Where(s => s.IsSelected).ToList();
            var moved = BatchOps.MoveToGroup(selected, "统一分组", groups);
            if (moved != 2) throw new InvalidOperationException("处理数量不是 2");
            if (all[0].Group != "统一分组" || all[1].Group != "统一分组")
                throw new InvalidOperationException("勾选项未全部移动");
            if (!string.IsNullOrEmpty(all[2].Group))
                throw new InvalidOperationException("未勾选项被误移动");
            if (!groups.Any(g => g.Name == "统一分组"))
                throw new InvalidOperationException("分组未登记到清单");

            // 已存在分组不重复登记
            BatchOps.MoveToGroup(selected, "统一分组", groups);
            if (groups.Count(g => g.Name == "统一分组") != 1)
                throw new InvalidOperationException("分组重复登记");

            all[0].LastClient = "300";
            if (all[0].ClientText != "300") throw new InvalidOperationException("客户端未显示");
            if (all[2].ClientText != "\u2014") throw new InvalidOperationException("空客户端占位异常");

            var deleted = BatchOps.Delete(all, selected);
            if (deleted != 2 || all.Count != 1 || all[0].Name != "C")
                throw new InvalidOperationException("批量删除数量异常");

            sb.AppendLine("      \u62d2\u9009 2 \u9879\u79fb\u52a8\u6210\u529f\uff0c\u672a\u52d8\u9009\u9879\u672a\u53d7\u5f71\u54cd\uff0c\u5ba2\u6237\u7aef\u663e\u793a\u6b63\u5e38");
        });

        // 9. 运行时枚举 SAP 会话窗口（验证 P/Invoke 与实时数据）
        Check("SAP 会话窗口枚举（实时）", () =>
        {
            var sessions = NativeMethods.GetSessionWindows();
            sb.AppendLine($"      \u5f53\u524d SAP \u4f1a\u8bdd\u7a97\u53e3 {sessions.Count} \u4e2a");
            foreach (var (_, title) in sessions.Take(6))
                sb.AppendLine("        \u00b7 " + title);

            foreach (var (_, title) in sessions)
            {
                var m = System.Text.RegularExpressions.Regex.Match(title, @"\b([A-Z0-9]{3})\s*\(\d+\)\s*$");
                if (m.Success && !LaunchService.TitleMatchesSid(title, m.Groups[1].Value))
                    throw new InvalidOperationException("标题中的 SID 未被识别：" + title);
            }

            // 登录框识别（类 #32770，标题即系统名）
            sb.AppendLine("      \u767b\u5f55\u6846\u68c0\u6d4b\u51fd\u6570\u53ef\u7528\uff0c\u5f53\u524d\u53ef\u89c1=" + NativeMethods.IsSapLogonDialogVisible("DeRUCCI PRD"));
        });

        // 9. 会话窗口标题与 SID 匹配（登录成功判定依赖此逻辑）
        Check("会话标题 SID 匹配", () =>
        {
            var cases = new (string title, string sid, bool expect)[]
            {
                ("SAP PRD (1)", "PRD", true),
                ("General Table Display S4Q (1)", "S4Q", true),
                ("\u9500\u552e\u4e3b\u7ba1\u63d0\u6210\u8ba1\u7b97\u62a5\u8868  [\u6761\u76ee\u6570\uff1a3] QAS (1)", "QAS", true),
                ("SAP Easy Access", "S4Q", false),
                ("DEV (1)", "DEV", true),
                ("DEVELOPMENT TOOLS (1)", "DEV", false),
                ("", "S4Q", false),
                ("S4Q (1)", "", false),
                ("sap prd (1)", "PRD", true),
            };

            foreach (var (title, sid, expect) in cases)
            {
                var actual = LaunchService.TitleMatchesSid(title, sid);
                if (actual != expect)
                    throw new InvalidOperationException($"标题 [{title}] 与 SID [{sid}] 期望 {expect}，实际 {actual}");
            }
            sb.AppendLine("      \u4e09\u7c7b\u5b9e\u6d4b\u6807\u9898\u683c\u5f0f\u5747\u6b63\u786e\u8bc6\u522b");
        });

        // 9. 登录弹框守护：标题解析 + 目标匹配 + 次数上限（自动填充的安全阀）
        Check("登录弹框守护（解析/匹配/上限）", () =>
        {
            var parseCases = new (string title, bool parse, string sid, string client)[]
            {
                ("SAP GUI \u5feb\u6377\u65b9\u5f0f - \u767b\u5f55 (PRD, 800, ZH, )", true, "PRD", "800"),
                ("SAP GUI Shortcut - Logon (S4Q, 300, EN, )", true, "S4Q", "300"),
                ("SAP GUI \u5feb\u6377\u65b9\u5f0f - \u767b\u5f55 (PRD, 800, ZH, 89000592)", true, "PRD", "800"),
                ("SAP GUI", false, "", ""),
                ("SAP GUI \u5feb\u6377\u65b9\u5f0f\u7684\u90e8\u5206\u6570\u636e\u4e0d\u53ef\u7528\uff1a\u7cfb\u7edf\u6807\u8bc6\u672a\u77e5\u3002", false, "", ""),
                ("SAP Easy Access (1)", false, "", ""),
                ("", false, "", ""),
            };
            foreach (var (title, parse, sid, client) in parseCases)
            {
                var ok = LogonDialogGuard.TryParseLogonTitle(title, out var info);
                if (ok != parse)
                    throw new InvalidOperationException($"标题 [{title}] 解析期望 {parse}，实际 {ok}");
                if (ok && (info.Sid != sid || info.Client != client))
                    throw new InvalidOperationException(
                        $"标题 [{title}] 解析出 ({info.Sid},{info.Client})，期望 ({sid},{client})");
            }

            // SID 或客户端任一不符都必须拒绝，否则会把 PRD 的密码填进 QAS
            var guard = new LogonDialogGuard("PRD", "800", "u1", "p1", null);
            var matchCases = new (string sid, string client, bool expect)[]
            {
                ("PRD", "800", true),
                ("prd", "800", true),
                ("S4Q", "800", false),
                ("PRD", "300", false),
            };
            foreach (var (sid, client, expect) in matchCases)
            {
                var actual = guard.Matches(new LogonDialogInfo(sid, client, "ZH", ""), out var why);
                if (actual != expect)
                    throw new InvalidOperationException(
                        $"目标 ({sid},{client}) 期望 {expect}，实际 {actual}（{why}）");
            }

            // 没有密码 / 额度用完 → 绝不接管
            if (new LogonDialogGuard("PRD", "800", "u1", "", null).CanFill)
                throw new InvalidOperationException("空密码不应允许填充");
            if (!guard.CanFill)
                throw new InvalidOperationException("有密码时应允许填充");
            if (LogonDialogGuard.MaxFillsPerLaunch != 1)
                throw new InvalidOperationException("单次登录的回填上限必须为 1");

            sb.AppendLine("      标题解析 7 例、目标匹配 4 例、额度规则均正确；单次上限=1");
        });

        // 9. 多账号规则：上限 8、默认账号、改名/删除差异同步
        Check("多账号规则（上限8/默认/改名/删除）", () =>
        {
            var original = AppPaths.Root;
            var sandbox = Path.Combine(original, "selftest-accounts");
            try
            {
                AppPaths.OverrideRoot(Path.Combine(sandbox, "data"));
                Directory.CreateDirectory(AppPaths.Root);
                var vault = new VaultService();
                vault.Initialize("multi-account-test");

                const string uuid = "sys-1";
                const string client = "300";

                // a) 首个账号自动成为默认
                AccountService.Save(vault, new Credential { SystemUuid = uuid, Client = client, User = "U01", Password = "p1" }, out var e1);
                if (!string.IsNullOrEmpty(e1)) throw new InvalidOperationException("首个账号保存失败：" + e1);
                if (vault.Get(uuid, client)?.User != "U01" || !vault.Get(uuid, client)!.IsDefault)
                    throw new InvalidOperationException("首个账号应自动成为默认");

                // b) 上限 8
                for (int i = 2; i <= 8; i++)
                    AccountService.Save(vault, new Credential { SystemUuid = uuid, Client = client, User = "U0" + i }, out _);
                if (AccountService.Count(vault, uuid, client) != 8)
                    throw new InvalidOperationException("账号数应为 8");
                if (AccountService.Save(vault, new Credential { SystemUuid = uuid, Client = client, User = "U09" }, out var eLimit))
                    throw new InvalidOperationException("第 9 个账号应被拒绝");
                if (string.IsNullOrEmpty(eLimit)) throw new InvalidOperationException("拒绝时应给出原因");

                // c) 设为默认
                AccountService.SetDefault(vault, uuid, client, "U05");
                if (vault.Get(uuid, client)?.User != "U05") throw new InvalidOperationException("默认账号切换失败");
                if (vault.Items.Count(c => c.IsDefault) != 1) throw new InvalidOperationException("默认账号必须唯一");

                // d) 删除默认 → 自动提升
                AccountService.Delete(vault, uuid, client, "U05");
                if (vault.Get(uuid, client)?.User == "U05") throw new InvalidOperationException("默认账号删除后应换人");
                if (vault.Items.Count(c => c.IsDefault) != 1) throw new InvalidOperationException("提升后默认账号应唯一");
                if (AccountService.Count(vault, uuid, client) != 7) throw new InvalidOperationException("删除后应为 7 个");

                // e) 改名：旧名不在新集合中 → 删旧建新，数量不变
                var work = vault.GetAll(uuid, client).Select(c => new Credential
                {
                    SystemUuid = c.SystemUuid, Client = c.Client, User = c.User, Password = c.Password,
                    IsDefault = c.IsDefault, Remember = c.Remember, Language = c.Language,
                    Tcode = c.Tcode, LogonGroup = c.LogonGroup
                }).ToList();
                var renamed = work.First();
                renamed.User = "RENAMED";
                var keep = work.Select(c => c.User).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var old in vault.GetAll(uuid, client).Where(o => !keep.Contains(o.User)).ToList())
                    AccountService.Delete(vault, uuid, client, old.User);
                foreach (var a in work) AccountService.Save(vault, a, out _);

                if (AccountService.Count(vault, uuid, client) != 7)
                    throw new InvalidOperationException("改名后数量应不变");
                if (vault.GetByUser(uuid, client, "RENAMED") is null)
                    throw new InvalidOperationException("改名后新账号应存在");
                if (vault.GetByUser(uuid, client, renamed.User) is null) throw new InvalidOperationException("占位异常");

                // f) GetAll 默认账号在前
                var ordered = vault.GetAll(uuid, client);
                if (!ordered.First().IsDefault) throw new InvalidOperationException("GetAll 应把默认账号排在前面");

                sb.AppendLine($"      \u4e0a\u96508\u62d2\u7b2c9\u3001\u9ed8\u8ba4\u5207\u6362\u3001\u5220\u9ed8\u8ba4\u81ea\u52a8\u63d0\u5347\u3001\u6539\u540d\u5dee\u5f02\u540c\u6b65\u5747\u6b63\u786e");
            }
            finally
            {
                AppPaths.OverrideRoot(original);
                try { Directory.Delete(sandbox, true); } catch { }
            }
        });

        // 9. 命令行密码：默认开启 + 一次性迁移 + 尊重用户手动关闭
        // 9. 登录框标题两种格式 + .sap 不携带密码
        Check("登录框标题识别与 .sap 无密码", () =>
        {
            // 格式①：命令行 -system 启动（实测标题）
            if (!LogonDialogGuard.TryParseLogonTitle("SAP GUI 快捷方式 - 登录 (PRD, 800, ZH, )", out var a) ||
                a.Sid != "PRD" || a.Client != "800" || a.Language != "ZH")
                throw new InvalidOperationException("格式①解析失败");

            // 客户端必须是数字，否则是别种弹框
            if (LogonDialogGuard.TryParseLogonTitle("系统标识未知 (PRD, abc)", out _))
                throw new InvalidOperationException("非数字客户端不应被接受");
            if (LogonDialogGuard.TryParseLogonTitle("SAP Easy Access", out _))
                throw new InvalidOperationException("无括号标题不应被接受");

            // 格式②：.sap 启动，标题即显示名（实测 "DeRUCCI PRD"）
            var guard = new LogonDialogGuard("PRD", "800", "89000592", "secret", "C:////SAP", "DeRUCCI PRD");
            var info = guard.MatchTitle("DeRUCCI PRD");
            if (info is null) throw new InvalidOperationException("格式②（显示名）未被识别");
            if (info.Value.Sid != "PRD" || info.Value.Client != "800")
                throw new InvalidOperationException("格式② 补全的 SID/客户端不正确");

            // 大小写与首尾空格应容忍
            if (guard.MatchTitle("  derucci prd ") is null)
                throw new InvalidOperationException("显示名匹配应忽略大小写与首尾空格");

            // 别的系统的显示名不应命中
            var other = new LogonDialogGuard("QAS", "300", "u", "p", "C:////SAP", "GXSLBUS_QAS");
            if (other.MatchTitle("DeRUCCI PRD") is not null)
                throw new InvalidOperationException("不同系统的显示名被误匹配");

            // 回归：未传入 displayName 时，纯显示名标题（.sap 路径）必须匹配失败。
            // 这正是 v9 发布后「仍弹密码框」的根因——LaunchAsync 构造 guard 漏传了 displayName。
            var noName = new LogonDialogGuard("PRD", "800", "89000592", "secret", "C:////SAP");
            if (noName.MatchTitle("DeRUCCI PRD") is not null)
                throw new InvalidOperationException("未传 displayName 时不应匹配显示名标题（会导致 .sap 路径无法自动填密码）");

            // 目标一致性：SID 与客户端不符必须拒绝
            if (guard.Matches(new LogonDialogInfo("QAS", "800", "", ""), out _))
                throw new InvalidOperationException("SID 不符却被接受");
            if (guard.Matches(new LogonDialogInfo("PRD", "300", "", ""), out _))
                throw new InvalidOperationException("客户端不符却被接受");

            // .sap 不得携带密码
            var sys = new SapSystem { Uuid = "u1", Name = "兆威500", Sid = "S4Q", Host = "10.1.160.197", Port = 3200 };
            var content = ShortcutFileBuilder.BuildContent(sys,
                new Credential { User = "SV01", Password = "TOP-SECRET" }, new AppSettings(), "500", "ZH", false);
            if (content.Contains("Password="))
                throw new InvalidOperationException(".sap 不应包含密码行");
            if (content.Contains("TOP-SECRET"))
                throw new InvalidOperationException(".sap 不应包含密码内容");
            if (!content.Contains("Name=SV01")) throw new InvalidOperationException("用户名应保留");
            if (!content.Contains("Description=兆威500")) throw new InvalidOperationException("Description 应保留");

            sb.AppendLine("      两种标题格式均识别、跨系统不误匹配、.sap 无密码");
        });

        // 9. 复用会话判定回归（本次用户反馈的核心缺陷）
        Check("复用会话时仍能判定登录成功", () =>
        {
            var sessions = NativeMethods.GetSessionWindows();
            var existing = sessions.FirstOrDefault(w => LaunchService.TitleMatchesSid(w.title, "PRD"));
            if (existing.title is null)
            {
                sb.AppendLine("      \u5f53\u524d\u65e0 PRD \u4f1a\u8bdd\uff0c\u8df3\u8fc7\u5b9e\u65f6\u6821\u9a8c");
                return;
            }

            var before = sessions.Select(w => w.handle).ToHashSet();
            var system = new SapSystem { Name = "DeRUCCI PRD", Sid = "PRD" };

            // 场景：会话已存在，启动器进程已退出交接，且不会开出新窗口
            var launcher = Process.Start("cmd.exe", "/c exit");
            launcher?.WaitForExit();

            var (ok, how) = LaunchService.WaitForSessionAsync(before, true, system, launcher, 8000,
                CancellationToken.None).GetAwaiter().GetResult();

            sb.AppendLine($"      \u5df2\u6709\u4f1a\u8bdd\u3001\u65e0\u65b0\u7a97\u53e3\u3001\u542f\u52a8\u5668\u5df2\u9000\u51fa \u2192 \u5224\u5b9a={how}");
            if (!ok) throw new InvalidOperationException("复用场景被误判为失败，会错误降级到 L3 丢掉密码");
        });

        // 9. 可编辑下拉（客户端手工输入）
        Check("客户端下拉支持手工输入", () =>
        {
            // 独立创建的控件不会自动套用 Application 级隐式样式，显式取出再赋
            var style = Application.Current.TryFindResource(typeof(System.Windows.Controls.ComboBox)) as Style;
            if (style is null) throw new InvalidOperationException("未找到 ComboBox 隐式样式");

            var editableCombo = new System.Windows.Controls.ComboBox { IsEditable = true, Style = style };
            editableCombo.ItemsSource = new[] { "100", "200", "300" };
            editableCombo.ApplyTemplate();

            var editableBox = editableCombo.Template.FindName("PART_EditableTextBox", editableCombo)
                as System.Windows.Controls.TextBox;
            if (editableBox is null)
                throw new InvalidOperationException("模板缺少 PART_EditableTextBox，IsEditable 无效，无法手工输入");
            if (editableBox.Visibility != Visibility.Visible)
                throw new InvalidOperationException("编辑态下输入框未显示，实际 " + editableBox.Visibility);

            var plain = new System.Windows.Controls.ComboBox { Style = style };
            plain.ApplyTemplate();
            var plainBox = plain.Template.FindName("PART_EditableTextBox", plain) as System.Windows.Controls.TextBox;
            if (plainBox is not null && plainBox.Visibility == Visibility.Visible)
                throw new InvalidOperationException("非编辑态输入框应隐藏");

            // 手工输入应同步到 Text
            editableBox.Text = "205";
            if (editableCombo.Text != "205")
                throw new InvalidOperationException("手工输入未同步到 ComboBox.Text，实际 " + editableCombo.Text);

            sb.AppendLine("      模板含 PART_EditableTextBox，输入 205 同步成功");
        });

        // 9. 窗口 XAML 实例化
        MainWindow? main = null;
        Check("主窗口 XAML", () => { main = new MainWindow(); });
        Check("解锁/设置主密码对话框", () =>
        {
            _ = new Views.VaultDialog(Views.VaultMode.Unlock);
            _ = new Views.VaultDialog(Views.VaultMode.Setup);
            _ = new Views.VaultDialog(Views.VaultMode.Change);
        });
        Check("连接属性对话框", () => { _ = new Views.EntryEditorWindow(null, null); });
        if (main is not null) Check("设置对话框", () => { _ = new Views.SettingsWindow(main!); });
        Check("批量登录窗口", () => { _ = new Views.BatchWindow(new List<SapSystem>()); });
        Check("快速搜索窗口", () => { _ = new Views.QuickSearchWindow(new List<SapSystem>(), _ => Task.CompletedTask); });
        Check("脚本管理窗口", () => { _ = new Views.ScriptsWindow(null); });
        Check("导入窗口", () => { _ = new Views.ImportWindow(); });
        Check("关于窗口", () => { _ = new Views.AboutWindow(); });

        // 密码框「小眼睛」：按住显示明文、松开隐藏、密码不丢
        Check("密码框小眼睛按住显示/松开隐藏", () =>
        {
            var pf = new Views.PasswordField();
            pf.Password = "Secret123";
            if (pf.Password != "Secret123") throw new InvalidOperationException("密码读写异常");

            var eye = (System.Windows.Controls.Button)pf.FindName("EyeButton")!;
            var plain = (System.Windows.Controls.TextBox)pf.FindName("InnerPlain")!;

            System.Windows.Input.MouseButtonEventArgs Evt(System.Windows.RoutedEvent re)
                => new(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = re, Source = eye };

            eye.RaiseEvent(Evt(System.Windows.UIElement.PreviewMouseDownEvent));
            if (plain.Visibility != Visibility.Visible || plain.Text != "Secret123")
                throw new InvalidOperationException("按住眼睛未以明文显示");
            if (pf.Password != "Secret123") throw new InvalidOperationException("按住时取值异常");

            eye.RaiseEvent(Evt(System.Windows.UIElement.PreviewMouseUpEvent));
            if (plain.Visibility == Visibility.Visible)
                throw new InvalidOperationException("松开后未隐藏");
            if (pf.Password != "Secret123") throw new InvalidOperationException("隐藏后密码丢失");
        });
        // 长按排序：移动后重排 SortOrder
        Check("长按排序：移动到目标并重排 SortOrder", () =>
        {
            var a = new SapSystem { Name = "A", SortOrder = 0 };
            var b = new SapSystem { Name = "B", SortOrder = 1 };
            var c = new SapSystem { Name = "C", SortOrder = 2 };
            var list = new List<SapSystem> { a, b, c };
            if (!ReorderUtil.MoveSystem(list, c, a)) throw new InvalidOperationException("系统移动未生效");
            if (c.SortOrder != 0 || a.SortOrder != 1 || b.SortOrder != 2)
                throw new InvalidOperationException("系统 SortOrder 重排错误");

            var g1 = new GroupDef { Name = "g1", SortOrder = 0 };
            var g2 = new GroupDef { Name = "g2", SortOrder = 1 };
            var g3 = new GroupDef { Name = "g3", SortOrder = 2 };
            var gl = new List<GroupDef> { g1, g2, g3 };
            if (!ReorderUtil.MoveGroup(gl, g1, g3)) throw new InvalidOperationException("分组移动未生效");
            if (g2.SortOrder != 0 || g3.SortOrder != 1 || g1.SortOrder != 2)
                throw new InvalidOperationException("分组 SortOrder 重排错误");
        });

        Check("账号显示：姓名(账号) / 无姓名仅账号", () =>
        {
            var withName = new Credential { User = "89000592", DisplayName = "张三" };
            if (withName.Display != "张三 (89000592)")
                throw new InvalidOperationException("有姓名时显示格式错误：" + withName.Display);

            var noName = new Credential { User = "89000592" };
            if (noName.Display != "89000592")
                throw new InvalidOperationException("无姓名时应仅显示账号：" + noName.Display);
        });

        Check("凭据克隆保留姓名等所有字段", () =>
        {
            var c = new Credential { User = "u1", DisplayName = "张三", Tcode = "SE16N", IsDefault = true };
            var k = c.Clone();
            if (k.User != "u1" || k.DisplayName != "张三" || k.Tcode != "SE16N" || !k.IsDefault)
                throw new InvalidOperationException("克隆丢失字段（姓名/事务码/默认标记）");
        });

        Check("凭据姓名/密码写盘后能读回", () =>
        {
            var original = AppPaths.Root;
            var sandbox = Path.Combine(original, "selftest-persist");
            try
            {
                Directory.CreateDirectory(sandbox);
                AppPaths.OverrideRoot(Path.Combine(sandbox, "data"));
                Directory.CreateDirectory(AppPaths.Root);

                var v = new VaultService();
                v.Initialize("pwd-123456");
                AccountService.Save(v, new Credential
                {
                    SystemUuid = "u9", Client = "300", User = "U9",
                    Password = "secret", DisplayName = "张三", Remember = true
                }, out var saveErr);
                if (!string.IsNullOrEmpty(saveErr)) throw new InvalidOperationException(saveErr);

                var v2 = new VaultService();
                if (!v2.Unlock("pwd-123456", out var unlockErr))
                    throw new InvalidOperationException("解锁失败：" + unlockErr);

                var got = v2.GetAll("u9", "300").FirstOrDefault();
                if (got is null) throw new InvalidOperationException("读不到账号");
                if (got.DisplayName != "张三")
                    throw new InvalidOperationException("姓名未持久化：" + got.DisplayName);
                if (got.Password != "secret")
                    throw new InvalidOperationException("密码未持久化");
            }
            finally
            {
                AppPaths.OverrideRoot(original);
            }
        });

        Check("输入框 / 分组选择", () =>
        {
            _ = new Views.InputDialog("测试", "名称", "值");
            _ = new Views.GroupPickerDialog(new[] { "A", "B" }, "A");
        });

        sb.AppendLine();
        sb.AppendLine($"===== 结果：通过 {pass} 项，失败 {fail} 项 =====");

        var report = sb.ToString();
        try
        {
            AppPaths.EnsureAll();
            var file = Path.Combine(AppPaths.Logs, $"selftest-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, report, Encoding.UTF8);
            File.WriteAllText(Path.Combine(AppPaths.Root, "selftest-latest.txt"), report, Encoding.UTF8);
            Log.Info($"自检完成：通过 {pass}，失败 {fail}，报告 {file}");
        }
        catch { }

        return report;
    }

    /// <summary>把各窗口离屏渲染成 PNG，用于视觉核对（不弹窗、不影响用户当前操作）。</summary>
    public static List<string> CaptureShots(string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new List<string>();

        void Shot(string name, Func<Window> factory, double width)
        {
            try
            {
                var win = factory();
                var background = win.Background;
                var content = (FrameworkElement)win.Content;
                var dataContext = content.DataContext; // 重新挂载会丢失继承的 DataContext
                win.Content = null;

                // 内容根节点通常透明，离屏渲染需补窗口底色
                var host = new System.Windows.Controls.Border { Background = background, Child = content, DataContext = dataContext };
                // 用窗口自身尺寸布局：Grid 的 * 行在无限高度测量下会塌成 0
                var w = win.Width > 0 ? win.Width : width;
                host.Measure(new Size(w, double.PositiveInfinity));
                // SizeToContent 的窗口 Height 为 NaN，改用期望高度（受 MaxHeight 约束）
                var desired = host.DesiredSize.Height;
                var cap = double.IsNaN(win.MaxHeight) ? double.PositiveInfinity : win.MaxHeight;
                var h = !double.IsNaN(win.Height) && win.Height > 0
                    ? win.Height
                    : Math.Min(desired, cap);
                host.Arrange(new Rect(0, 0, w, h));
                host.UpdateLayout();

                var bmp = new RenderTargetBitmap((int)w, (int)Math.Ceiling(h), 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(host);

                var file = Path.Combine(dir, name + ".png");
                using var fs = File.Create(file);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                encoder.Save(fs);
                files.Add(file);
            }
            catch (Exception ex)
            {
                files.Add($"[FAIL] {name}: {ex.Message}");
            }
        }

        var main = new MainWindow();
        Shot("main", () => main, 1140);

        // 多账号编辑器：沙箱凭据库，不触碰真实数据
        var savedRoot = AppPaths.Root;
        try
        {
            AppPaths.OverrideRoot(Path.Combine(dir, "shot-vault"));
            Directory.CreateDirectory(AppPaths.Root);
            AppServices.Vault.Initialize("shot-only");

            var sys = new SapSystem { Uuid = "shot-sys", Name = "兆威500", Sid = "S4Q", LastClient = "300" };
            AppServices.Vault.Upsert(new Credential { SystemUuid = sys.Uuid, Client = "300", User = "SV01", Password = "x", Remember = true });
            AppServices.Vault.Upsert(new Credential { SystemUuid = sys.Uuid, Client = "300", User = "JT_SD1", Password = "x", Remember = true, IsDefault = true });
            AppServices.Vault.Upsert(new Credential { SystemUuid = sys.Uuid, Client = "300", User = "SD02", Password = "x", Remember = true });
            sys.DefaultUser = "JT_SD1";

            Shot("editor-multi", () => new Views.EntryEditorWindow(sys, AppServices.Vault.Get("shot-sys", "300")), 720);
        }
        finally
        {
            AppPaths.OverrideRoot(savedRoot);
            try { Directory.Delete(Path.Combine(dir, "shot-vault"), true); } catch { }
        }
        Shot("vault-setup", () => new Views.VaultDialog(Views.VaultMode.Setup), 440);
        Shot("entry-editor", () => new Views.EntryEditorWindow(null, null), 720);
        Shot("settings", () => new Views.SettingsWindow(main), 720);
        Shot("scripts", () => new Views.ScriptsWindow(null), 880);
        Shot("batch", () => new Views.BatchWindow(new List<SapSystem>()), 660);
        Shot("import", () => new Views.ImportWindow(), 640);
        Shot("about", () => new Views.AboutWindow(), 520);
        Shot("quicksearch", () => new Views.QuickSearchWindow(new List<SapSystem>(), _ => Task.CompletedTask), 604);

        return files;
    }
}
