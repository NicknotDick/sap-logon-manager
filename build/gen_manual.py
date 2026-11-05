# -*- coding: utf-8 -*-
"""生成《SAP 登录管理器 操作使用手册（中英双语）》PDF 排版配置。
输出 build/manual_config.json，供 pdfkit pdf_create --config 使用。
"""
import json

OUT_PDF = r"E:\WorkBuddy\SAP 登陆器\SAP登录管理器-操作使用手册-User-Manual.pdf"
FONT = r"C:\Windows\Fonts\msyh.ttc"
GREY = "#5a5a5a"

E = []


def title(text, **kw):
    d = {"type": "title", "text": text}
    d.update(kw)
    E.append(d)


def h1(zh, en):
    E.append({"type": "heading", "text": f"{zh} / {en}", "level": 1})


def h2(zh, en):
    E.append({"type": "heading", "text": f"{zh} / {en}", "level": 2})


def p(text, **kw):
    d = {"type": "paragraph", "text": text}
    d.update(kw)
    E.append(d)


def zh(text, **kw):
    p(text, **kw)


def en(text, **kw):
    kw.setdefault("font_color", GREY)
    kw.setdefault("space_after", 10)
    p(text, **kw)


def note(text_zh, text_en):
    p(text_zh, bg_color="#FFF8E1", border_color="#F0C36D")
    en(text_en)


def bq(text):
    E.append({"type": "blockquote", "text": text})


def ul(items, ordered=False):
    E.append({"type": "list", "items": items, "ordered": ordered, "font_size": 10.5})


def kv(pairs, key_width=165):
    E.append({"type": "key_value",
              "pairs": [{"key": k, "value": v} for k, v in pairs],
              "key_width": key_width})


def tbl(headers, rows, col_widths=None):
    d = {"type": "table", "headers": headers, "rows": rows,
         "body_font_size": 9.5, "header_font_size": 10}
    if col_widths:
        d["col_widths"] = col_widths
    E.append(d)


def spacer(h=10):
    E.append({"type": "spacer", "height": h})


def hr():
    E.append({"type": "hr"})


def pb():
    E.append({"type": "page_break"})


# ============================== 封面 ==============================
title("SAP 登录管理器", font_size=30)
p("操作使用手册  |  User Manual", font_size=17, alignment="center", space_after=4)
p("中英双语版 · Chinese-English Bilingual Edition",
  font_size=11, alignment="center", font_color=GREY, space_after=18)
kv([
    ("软件版本 / Version", "GURURU202611051345"),
    ("发行版本 / Release", "v1.5.3"),
    ("文档日期 / Date", "2026-11-05"),
    ("适用平台 / Platform", "Windows 10 / 11（64 位）/ 64-bit"),
    ("SAP 客户端 / SAP GUI", "7.40 及以上（已在 8.0 实测）/ 7.40 or later (verified on 8.0)"),
    ("作者 / Author", "Gururu Nick"),
], key_width=185)
spacer(6)
hr()
bq("本手册为 SAP 登录管理器（SAP Logon Manager）的中英双语操作使用手册，涵盖环境要求、日常操作、"
   "账号与安全管理、便捷功能、故障排查与数据迁移。/ This bilingual manual covers system requirements, "
   "daily operations, account and security management, productivity features, troubleshooting and data migration.")
pb()

# ============================== 目录 ==============================
h1("目录", "Contents")
ul([
    "1. 产品概述 / Product Overview",
    "2. 环境要求 / System Requirements",
    "3. 快速上手 / Getting Started",
    "4. 主界面说明 / Main Window",
    "5. 配置同步 / Configuration Sync",
    "6. 批量登录 / Batch Logon",
    "7. 凭据与账号管理 / Credentials & Account Management",
    "8. 安全机制 / Security Mechanisms",
    "9. 便捷功能 / Productivity Features",
    "10. 登录机制与故障排查 / Logon Mechanism & Troubleshooting",
    "11. 导出、导入与分享 / Export, Import & Sharing",
    "12. 脚本管理 / Script Management",
    "13. 快捷键 / Keyboard Shortcuts",
    "14. 数据存储与迁移 / Data Storage & Migration",
    "15. 隐私与免责声明 / Privacy & Disclaimer",
    "16. 从源码构建 / Build from Source",
])
pb()

# ============================== 1. 产品概述 ==============================
h1("1. 产品概述", "1. Product Overview")
zh("SAP 登录管理器是一款免安装的 Windows 桌面工具，用于集中管理 SAP Logon 中的系统连接与登录凭据，"
   "实现「双击即登录」，免去每日重复输入账号密码。")
en("SAP Logon Manager is a portable Windows desktop utility that centralizes SAP Logon connections and "
   "credentials, enabling one-click sign-on and eliminating repetitive manual logins.")
zh("核心特性：")
ul([
    "单文件绿色版：双击即用，不写注册表、不安装运行时 / Portable single executable; no registry writes, no runtime install",
    "自动读取本机 SAP Logon 配置，一键同步 / Auto-reads local SAP Logon configuration with one-click sync",
    "凭据经 AES-256-GCM 加密，仅保存在本机 / Credentials protected with AES-256-GCM and stored locally only",
    "支持批量登录、分组收藏、事务码直达、脚本绑定、导入导出 / Batch logon, groups & favorites, transaction shortcuts, script binding, import/export",
    "支持 Windows Hello 与 Windows 账户密码解锁主密码 / Windows Hello and Windows-account-password unlock",
    "支持便携模式，整包拷贝即可换机使用 / Portable mode for migration to another PC",
])
en("The tool only invokes SAP's official executables (sapshcut.exe / saplogon.exe). It does not modify "
   "SAP GUI files nor write back to SAP Logon configuration.", space_before=4)

# ============================== 2. 环境要求 ==============================
h1("2. 环境要求", "2. System Requirements")
kv([
    ("操作系统 / OS", "Windows 10 / 11（64 位）/ Windows 10 / 11 (64-bit)"),
    ("SAP 客户端 / SAP GUI", "SAP GUI for Windows 7.40 及以上（8.0 实测通过）/ 7.40 or later (verified on 8.0)"),
    ("权限 / Privileges", "普通用户即可，无需管理员权限 / Standard user; administrator rights not required"),
    ("网络 / Network", "无需联网；仅在登录 SAP 时访问你的 SAP 服务器 / No Internet access needed; only contacts your SAP server at logon"),
])
zh("本工具仅调用 SAP 官方的 sapshcut.exe / saplogon.exe，不修改 SAP GUI 任何文件，也不会回写你的 SAP Logon 配置。",
   space_before=6)
en("This tool only invokes SAP's official sapshcut.exe / saplogon.exe. It does not modify SAP GUI files "
   "and never writes back to your SAP Logon configuration.")

h2("2.1 SAP GUI 检测与手动指定", "2.1 SAP GUI Detection and Manual Override")
zh("程序启动时自动探测 SAP GUI 安装位置，覆盖以下来源：")
ul([
    "注册表：HKLM / HKCU，含 32 位与 64 位视图（WOW6432Node）。/ Registry: HKLM / HKCU, both 32- and 64-bit views (WOW6432Node).",
    "系统卸载项：显示名含 “SAP GUI” 或 “SAP Front End” 的安装条目。/ Uninstall entries whose display name contains “SAP GUI” or “SAP Front End”.",
    "PATH 环境变量与 SAPGUI_DIR。/ The PATH environment variable and SAPGUI_DIR.",
    "各固定磁盘下的 \\SAP\\FrontEnd\\SAPgui、\\Program Files\\...、\\Program Files (x86)\\...。/ Standard subfolders on every fixed drive.",
], ordered=True)
zh("若仍提示「未检测到 SAP GUI」：打开「设置 → SAP GUI」，点「浏览…」直接选中 saplogon.exe（或 sapshcut.exe），"
   "点「重新检测」确认状态后保存。该路径会被记住，后续启动直接使用。检测失败时诊断信息中会列出已尝试的全部路径。")
en("If SAP GUI is still not detected, open Settings → SAP GUI, click “Browse…” and pick saplogon.exe (or "
   "sapshcut.exe), verify with “Detect again” and save. The path is remembered. When detection fails, the "
   "diagnostics report lists every path that was tried.")

# ============================== 3. 快速上手 ==============================
h1("3. 快速上手", "3. Getting Started")
zh("首次使用约需 1 分钟：")
ul([
    "双击 SAP登录管理器.exe 启动程序。/ Double-click SAP登录管理器.exe to launch the program.",
    "设置主密码（至少 6 位）；建议勾选「记住主密码」以便下次免输。/ Set a master password (at least 6 characters); optionally enable “Remember master password”.",
    "点击右上角「自动获取现有配置」（快捷键 F5），扫描本机 SAP Logon 并列出全部系统。/ Click “Sync from SAP Logon” (F5) to scan and list all systems.",
    "双击某个系统（或点其「登录」按钮），填入账号密码，勾选「保存密码并自动登录」后保存。/ Double-click a system (or click “Log on”), enter credentials and check “Save password and log on automatically”.",
    "之后登录只需双击，即直达 SAP 系统界面。/ From then on, a double-click opens the SAP system directly.",
], ordered=True)
note("主密码遗忘后，已保存的凭据无法恢复（这是加密的设计，不是缺陷）。请在「设置 → 数据与诊断」中定期导出加密备份；"
     "也可启用 Windows Hello 或使用 Windows 账户密码解锁（见第 9 章）。",
     "If the master password is lost, stored credentials cannot be recovered by design. Export encrypted "
     "backups regularly in Settings → Data & Diagnostics; alternatively enable Windows Hello or Windows "
     "account password recovery (see Chapter 9).")

# ============================== 4. 主界面说明 ==============================
h1("4. 主界面说明", "4. Main Window")
zh("主界面由顶部工具栏（搜索、自动获取现有配置、批量登录、新增连接、排序）、左侧分组导航（全部系统 / 收藏 / 自定义分组）、"
   "中部系统列表与底部状态栏组成。")
en("The main window consists of a top toolbar (search, sync, batch logon, new connection, sorting), a left "
   "navigation pane (All Systems / Favorites / custom groups), the system list and a status bar.")

h2("4.1 系统列表", "4.1 System List")
zh("每行展示：环境徽标（DEV 绿 / QAS 橙 / PRD 红）· 系统名 · SID · 连接方式 · 上次登录时间 · 客户端 · "
   "连通性圆点 · 收藏星标 · 登录按钮。行内蓝色文字为该系统当前的默认登录账号；账号绑定姓名后显示为「姓名 (账号)」。")
en("Each row shows: environment badge (DEV green / QAS orange / PRD red), system name, SID, connection "
   "mode, last logon time, client, connectivity dot, favorite star and the logon button. Blue text is the "
   "default account; with a display name bound it renders as “Name (Account)”.")
zh("列表条目与分组均支持长按拖拽排序（见 9.5）。")
en("Items and groups support press-and-drag reordering (see 9.5).")

h2("4.2 环境徽标", "4.2 Environment Badges")
zh("按系统名称与 SID 自动推断环境类型（DEV / QAS / PRD），也可在「编辑连接」中手动指定。生产系统（PRD）默认在登录前弹出二次确认，防止误入生产。")
en("The environment type (DEV / QAS / PRD) is inferred from the system name and SID, and can be set "
   "manually in “Edit Connection”. Production systems require a confirmation before logon to prevent mistakes.")

h2("4.3 连通性指示", "4.3 Connectivity Indicator")
zh("绿色 = 可连通；红色 = 不可达；黄色 = 检测中。经 Router 的系统探测 Router 端口（3299），负载均衡系统探测消息服务器端口（3600）。")
en("Green = reachable; red = unreachable; yellow = probing. Router-based systems probe the Router port "
   "(3299); load-balanced systems probe the message server port (3600).")

# ============================== 5. 配置同步 ==============================
h1("5. 配置同步", "5. Configuration Sync")
zh("点击「自动获取现有配置」或按 F5，程序会重新读取 %APPDATA%\\SAP\\Common\\SAPUILandscape.xml"
   "（含 <Includes> 引用的文件；旧版安装自动回退读取 saplogon.ini），并与本地清单按下列规则合并：")
en("Click “Sync from SAP Logon” or press F5. The program re-reads %APPDATA%\\SAP\\Common\\SAPUILandscape.xml "
   "(including <Includes> references; older installations fall back to saplogon.ini) and merges it with the "
   "local list using these rules:")
kv([
    ("新系统 / New", "追加到列表并标注 NEW，保留你已有的分组与收藏 / Appended and tagged NEW; existing groups and favorites preserved"),
    ("连接参数变更 / Updated", "更新 host、端口、Router 与备注；凭据、收藏、分组、脚本绑定全部保留 / Connection parameters updated; credentials, favorites, groups and script bindings retained"),
    ("系统改名 / Renamed", "同步新名称并提示「已更名」，避免按名称定位失配 / Name synchronized with a notice; prevents name-based mismatch"),
    ("系统删除 / Removed", "标注「失效」并置灰置底，不删除条目；凭据仍在，可手动删除 / Marked invalid and greyed out; entries and credentials kept until deleted manually"),
    ("手工添加 / Manual", "自己手工添加的连接完全不受同步影响 / Manually added connections are never touched by sync"),
], key_width=125)
note("每次同步前自动创建快照，可在「设置 → 数据与诊断 → 回滚到最近快照」还原。配置文件读取异常时不会覆盖旧清单，"
     "只报错、不清空列表。程序启动时会静默同步一次；SAP Logon 配置变化时也会自动同步。",
     "A snapshot is taken before every sync and can be restored in Settings → Data & Diagnostics. If the "
     "configuration file cannot be parsed, the old list is kept and an error is reported — the list is never "
     "cleared. The program also syncs silently at startup and whenever SAP Logon configuration changes.")

# ============================== 6. 批量登录 ==============================
h1("6. 批量登录", "6. Batch Logon")
zh("用于掉线后一键拉起多个系统：")
ul([
    "在列表左侧勾选多个系统（可跨分组，配合搜索框快速筛选）。/ Tick multiple systems in the list (across groups; combine with search).",
    "点击底部「登录选中 (n)」或顶部「批量登录」。/ Click “Log on selected (n)” at the bottom or “Batch Logon” at the top.",
    "弹窗中点「开始登录」，逐条显示 成功 / 需手输 / 失败 / 跳过，可随时中止。/ Press “Start”; each item reports success / manual / failed / skipped; the run can be aborted at any time.",
], ordered=True)
ul([
    "默认顺序执行、间隔 3 秒（设置中 1–10 秒可调），避免会话争抢 / Sequential execution with a 3-second interval (adjustable 1–10 s) to avoid session contention",
    "勾选「跳过不可达系统」可自动跳过探测失败的条目 / Option “Skip unreachable systems”",
    "失败不会自动重试，避免账号被锁定 / Failed items are not retried automatically (prevents account lockout)",
    "结束后可「复制结果」留档 / Results can be copied for the record",
])

# ============================== 7. 凭据与账号管理 ==============================
h1("7. 凭据与账号管理", "7. Credentials & Account Management")

h2("7.1 多账号（每客户端上限 8 个）", "7.1 Multiple Accounts (up to 8 per client)")
zh("同一「系统 + 客户端」下最多可保存 8 个账号，适合多人共用一台机器、或一人在多个客户端有不同账号的场景。"
   "右键连接 →「编辑凭据…」进行管理；第一个保存的账号自动成为默认账号。")
en("Up to 8 accounts can be stored per system-and-client pair — useful for shared machines or multiple "
   "clients. Manage them via right-click → “Edit Credentials…”. The first saved account becomes the default.")
kv([
    ("新增账号 / Add", "点「新增账号」，填写账号（可绑定姓名）与密码后保存 / Click “Add account”, fill in account (optionally a display name) and password"),
    ("设为默认 / Set default", "选中账号点「设为默认」，同客户端其它账号自动取消默认 / Select an account and click “Set default”; other accounts on the same client are deselected"),
    ("删除账号 / Delete", "删除的是默认账号时，自动把剩余账号提升一个为默认，不会出现无默认状态 / When the default is deleted, the next remaining account is promoted automatically"),
    ("切换客户端 / Switch client", "客户端下拉一改，账号列表随之切换（每个客户端各自最多 8 个）/ The account list follows the selected client (8 per client)"),
    ("达到上限 / Limit reached", "第 9 个账号会被拒绝并提示，需先删除不用的账号 / A 9th account is rejected with a prompt"),
], key_width=140)

h2("7.2 账号姓名字段", "7.2 Display Name")
zh("账号可绑定「姓名（可选）」。保存后，登录菜单、主列表「上次登录」等处统一显示为「姓名 (账号)」"
   "（无姓名时仅显示账号），便于区分同一客户端下的多个账号。")
en("An optional display name can be bound to each account. Wherever the account appears (logon menu, "
   "“last logon” column, etc.) it renders as “Name (Account)”, or just the account when no name is set.")

h2("7.3 登录时选账号", "7.3 Choosing an Account at Logon")
ul([
    "右键连接，菜单显示「登录（默认 XX）」与各账号的「以 XX 登录」子项。/ Right-click a connection: “Log on (default XX)” plus one entry per account.",
    "双击行或点「登录」→ 使用默认账号。/ Double-click or “Log on” uses the default account.",
    "点「以 XX 登录」→ 使用指定账号。/ “Log on as XX” uses that specific account.",
    "批量登录始终使用各系统的默认账号。/ Batch logon always uses each system's default account.",
])
en("Saved passwords can also be updated here at any time; the change is written to the encrypted vault "
   "immediately.", space_before=4)

# ============================== 8. 安全机制 ==============================
h1("8. 安全机制", "8. Security Mechanisms")
kv([
    ("加密 / Encryption", "主密码经 PBKDF2-HMAC-SHA256（20 万次迭代）派生密钥，凭据以 AES-256-GCM 认证加密存储于 vault.dat / Credentials stored in vault.dat, encrypted with AES-256-GCM under a PBKDF2-HMAC-SHA256 (200,000 iterations) derived key"),
    ("记住主密码 / Remember", "可选。使用 Windows DPAPI 封装，仅当前 Windows 账户可解密；换电脑后需重新输入主密码 / Optional; wrapped with Windows DPAPI and bound to the current Windows account"),
    ("闲置锁定 / Auto-lock", "默认 0（不自动锁定），可调 0–720 分钟 / Disabled by default (0 min); adjustable 0–720 min"),
    ("登录方式 / Logon path", "默认允许密码走命令行（sapshcut -pw），登录最直接；可在设置中关闭，改走「临时 .sap + 登录框自动填表」，此时密码不出现在命令行 / By default the password may appear in the sapshcut command line for a direct logon; disable it in Settings to use the temporary .sap + auto-fill path instead, where the password never reaches the command line"),
    ("日志 / Logging", "仅记录事件与错误码，从不记录密码 / Events and error codes only — passwords are never logged"),
], key_width=140)

h2("8.1 自动填表安全底线", "8.1 Auto-Fill Safety Rules")
zh("密码仅在 SAP 登录框出现时注入，填入前执行三重校验，任一不满足即放弃填表：")
ul([
    "只认 SAP 进程：窗口必须属于 saplogon.exe / sapgui.exe / sapshcut.exe，且其所在目录属于可信目录集合——检测到的安装目录、手动指定的目录，以及本程序实际启动过的 SAP exe 目录。/ SAP processes only: the window must belong to saplogon.exe / sapgui.exe / sapshcut.exe located in a trusted directory — the detected install folder, the manually specified folder, and any SAP executable folder this program actually launched.",
    "只认本次目标：窗口标题中的 SID 与客户端必须与本次登录一致，防止把 PRD 的密码填进其它系统。/ Current target only: the SID and client in the window title must match this logon attempt.",
    "一次登录最多填一次：再次弹框通常意味着密码已被拒绝，重复填写只会增加锁号风险。/ At most one fill per logon: a repeated dialog means the password was rejected; retrying risks locking the account.",
], ordered=True)
en("The fill uses standard Win32 messages (WM_SETTEXT / BM_CLICK) with a read-back verification. No DLL "
   "injection, no keystroke simulation and no global hooks are involved.")
note("默认登录路径为「命令行带密码」（最直接）；关闭「允许密码出现在命令行」后改走「临时 .sap + 自动填表」——"
     "后者因 SAP GUI 7.40 PL1 起 .sap 的密码字段被官方禁用（SAP Note 146173），且命令行方式会把密码暴露在进程命令行中。",
     "The default logon path passes the password on the command line (most direct). Disabling “Allow password "
     "on the command line” switches to “temporary .sap + auto-fill”, because the .sap password field has been "
     "officially disabled since SAP GUI 7.40 PL1 (SAP Note 146173) and command-line passwords are exposed in "
     "the process command line.")

# ============================== 9. 便捷功能 ==============================
h1("9. 便捷功能", "9. Productivity Features")

h2("9.1 Windows Hello 解锁", "9.1 Windows Hello Unlock")
zh("在「设置」中启用 Windows Hello 后，解锁主密码可使用人脸、指纹或 PIN，无需键入主密码。")
en("With Windows Hello enabled in Settings, the master password can be unlocked by facial recognition, "
   "fingerprint or PIN.")

h2("9.2 Windows 账户密码恢复", "9.2 Windows Account Password Recovery")
zh("忘记主密码时，可在解锁界面使用当前 Windows 账户的登录密码完成验证解锁（需此前已成功保存过主密码）。")
en("If the master password is forgotten, unlock with the current Windows account password (requires a "
   "previously saved master password).")

h2("9.3 便携模式", "9.3 Portable Mode")
zh("在「设置 → 数据与诊断」点「切换为便携模式」，程序会把数据目录复制到 exe 同级的 data\\ 文件夹并重启；"
   "此后把「exe + data」整体拷贝到另一台装有 SAP GUI 的电脑即可直接使用。")
en("Click “Switch to portable mode” in Settings → Data & Diagnostics. Data is copied to a data\\ folder "
   "next to the executable and the program restarts. Copy the exe plus the data folder to another PC with "
   "SAP GUI installed and run directly.")

h2("9.4 密码可见性", "9.4 Password Visibility")
zh("所有密码框右侧均有小眼睛按钮：按住即显示明文，松开或移开鼠标立即隐藏，有效防止肩窥。")
en("Every password box has an eye button: hold it to reveal the plaintext, release (or move away) to hide "
   "it immediately — effective protection against shoulder surfing.")

h2("9.5 长按拖拽排序", "9.5 Drag-to-Reorder")
zh("在列表中长按某个系统或分组约 0.4 秒后拖动，即可调整顺序；程序会实时重排并写回排序值，非自定义排序模式下自动固化为自定义顺序。")
en("Press and hold a system or group for about 0.4 seconds, then drag to reorder. The new order is written "
   "back immediately and promoted to a custom order automatically.")

h2("9.6 单实例与占用提醒", "9.6 Single Instance & Occupancy Prompt")
zh("程序为单实例。若已有实例在运行，再次启动时会提示：切换到已运行的窗口、或一键结束占用进程后重启、或退出。")
en("The program runs as a single instance. Launching it again offers to switch to the running window, "
   "terminate the occupying process and restart, or exit.")

h2("9.7 版本标识", "9.7 Version Tag")
zh("状态栏左下角显示当前构建版本，格式为 GURURU+YYYYMMDDHHMM（取自构建时间，精确到分钟），「关于」窗口中同步展示。")
en("The status bar (bottom-left) shows the build version in GURURU+YYYYMMDDHHMM format (build timestamp, "
   "minute precision), also displayed in the About window.")

h2("9.8 粘贴新建", "9.8 Paste to Create")
zh("右键连接 →「复制连接信息」会生成一段不含密码的文本卡片（含名称、系统 ID、连接方式、Router、常用客户端、备注）。"
   "点工具栏「粘贴新建」，程序解析该文本（或单行连接串）并预填一个新连接，保存后即为一条新记录。")
ul([
    "支持“复制连接信息”的分享文本。/ Supports the shared text produced by “Copy connection info”.",
    "支持单行连接串：/H/主机/S/3200、/H/路由/H/主机/S/3200、/M/消息服务器/S/3600、主机:端口、SID 主机 端口。/ Single-line connection strings: /H/host/S/3200, /H/router/H/host/S/3200, /M/mshost/S/3600, host:port, SID host port.",
], ordered=True)
zh("新连接默认保存在当前显示的分组；若当前处于「全部系统」视图，则落在未分组。解析失败时会提示支持的格式。")
en("The new connection is saved into the group currently displayed; in the “All systems” view it lands in "
   "“Ungrouped”. A parse failure shows the supported formats.")

h2("9.9 三维唯一指向", "9.9 Unique System Targeting")
zh("同一台机器上可能存在系统标识（SID）相同的多套系统（例如两套都是 S4Q）。仅用 SID 启动会被 SAP 随机解析到其中一套。"
   "程序按「应用服务器 + 实例编号 + 系统标识」三元组锁定端点：命令行追加 -guiparm 连接串，临时 .sap 写入 GuiParm，"
   "确保打开的一定是目标系统。")
en("One machine may host several systems sharing the same SID (e.g. two systems both called S4Q). Starting by "
   "SID alone lets SAP resolve either one at random. The program pins the endpoint with the triple "
   "application server + instance number + system ID: -guiparm is appended to the command line and GuiParm "
   "is written into the temporary .sap file, so the intended system is always opened.")

# ============================== 10. 登录机制与故障排查 ==============================
h1("10. 登录机制与故障排查", "10. Logon Mechanism & Troubleshooting")

h2("10.1 登录链", "10.1 Logon Chain")
zh("默认路径（允许密码出现在命令行）：调用 sapshcut.exe 携带系统标识、连接串、客户端、语言、账号与密码，"
   "由 SAP 直接完成登录，不出现登录框。")
en("Default path (password on the command line): sapshcut.exe is invoked with the system ID, connection "
   "string, client, language, account and password, so SAP completes the logon without showing a dialog.")
zh("关闭该选项后的路径：")
ul([
    "生成临时 .sap 文件：含显示名、连接串与账号（不带密码）。/ Generate a temporary .sap file with description, connection parameters and account (no password).",
    "调用 sapshcut.exe -open，由 SAP 按显示名从本机 landscape 精确定位系统。/ sapshcut.exe -open lets SAP resolve the system by its display name in the local landscape.",
    "SAP 弹出登录框（标题即系统显示名）。/ SAP shows the logon dialog titled with the system display name.",
    "程序校验窗口来源后自动填入密码并点击「登录」；密码只进目标进程内存。/ After verifying the window, the program fills the password and clicks Log On; the password exists only in the target process memory.",
    "会话窗口出现即登录成功。/ The session window appearing means logon succeeded.",
], ordered=True)
bq("说明：自 SAP GUI 7.40 PL1 起，.sap 文件中的密码字段已被官方禁用（SAP Note 146173），因此密码必须在登录框阶段注入。"
   "/ Note: since SAP GUI 7.40 PL1 the password field inside .sap files has been officially disabled "
   "(SAP Note 146173); the password is therefore injected at the logon-dialog stage.")

h2("10.2 若仍弹出登录框", "10.2 If the Logon Dialog Still Appears")
zh("说明密码已送达但被 SAP 拒绝。按可能性排序：")
ul([
    "密码已过期（最常见）——右键连接 →「编辑凭据」更新密码。/ Password expired (most common) — update it via right-click → “Edit Credentials”.",
    "账号被锁定——连续输错触发，表现为「刚才还能登，突然全部不行」；请联系 BASIS 管理员解锁。/ Account locked after repeated failures; contact your BASIS administrator.",
    "选错了账号——右键菜单「以 XX 登录」可核对本次所用账号。/ Wrong account selected — check the “Log on as XX” entries.",
    "客户端不对——同一账号在不同客户端是独立存在的。/ Wrong client — the same account name may exist independently in other clients.",
], ordered=True)
zh("原因确认：日志位于 %LOCALAPPDATA%\\SapLauncher\\logs\\，失败时会记录当时屏幕上的对话框标题——出现「修改密码」即密码到期；"
   "仅有系统名则为密码错误或账号锁定。状态栏提示条也会直接给出结论。")
en("To confirm the cause, check the logs in %LOCALAPPDATA%\\SapLauncher\\logs\\. Dialog titles are recorded "
   "on failure: “Change Password” indicates expiry; a bare system name indicates a wrong password or a "
   "locked account. The status bar also reports the conclusion.")

h2("10.3 常见问题", "10.3 Frequently Asked Questions")
kv([
    ("未检测到 SAP GUI / Not detected", "未安装 SAP GUI 或安装在被裁剪的非标准目录。程序已覆盖注册表、卸载项、PATH 与各盘符常见路径；仍失败请在「设置 → SAP GUI」中手动指定含 saplogon.exe 的目录 / SAP GUI is not installed or is in a non-standard directory; the program checks registry, uninstall entries, PATH and common paths on every drive — otherwise specify the folder containing saplogon.exe in Settings → SAP GUI"),
    ("列表里没有系统 / Empty list", "点「自动获取现有配置」（F5）；确认 SAP Logon 里能看到条目；查看 logs 中的同步警告 / Press F5; verify entries exist in SAP Logon; check sync warnings in logs"),
    ("连到另一套系统 / Wrong system", "SAP Logon 中存在同名显示名。本程序按显示名精确定位，请勿手工改乱名称 / Duplicate display names in SAP Logon; keep display names unique"),
    ("点击登录没反应 / No reaction", "查看状态栏与日志。常见原因：服务器不可达、账号被锁、需要先改密码 / Check the status bar and logs; typical causes: unreachable server, locked account, password change required"),
    ("热键无反应 / Hotkey dead", "被其它程序占用；在设置中更换组合后重启程序 / The hotkey is taken by another program; reassign it in Settings and restart"),
    ("关闭窗口后找不到程序 / Window lost", "默认最小化到托盘；双击托盘图标或按热键呼出，可在设置中关闭该行为 / The window minimizes to the tray by default; reopen via tray icon or hotkey"),
    ("杀软报警 / AV alert", "程序会调用 SAP 官方程序并写临时文件；请加入信任，或在设置中关闭「允许密码出现在命令行」/ The program invokes official SAP binaries and writes temporary files; add it to the trust list"),
], key_width=170)

h2("10.4 分发与部署", "10.4 Distribution and Deployment")
zh("本程序为 .NET 8 单文件发布，但 WPF 原生渲染库以旁置文件形式随 exe 发布。转发给同事时必须整包发送：")
ul([
    "SAP登录管理器.exe（旁置 DLL 版）：与同目录的 5 个 DLL（D3DCompiler_47_cor3、PenImc_cor3、PresentationNative_cor3、vcruntime140_cor3、wpfgfx_cor3）必须保持在同一目录。/ The exe with the five side-by-side DLLs — all files must stay in the same folder.",
    "单文件版：原生库在首次运行时自解压到临时目录，可单独发送；若被本机安全策略禁止从 %TEMP% 加载，改用旁置 DLL 版。/ The single-file build extracts the native libraries to a temporary folder on first run; if that is blocked by policy, use the folder-based package.",
], ordered=True)
note("只发送 exe 会出现「双击后一直转圈、进程存在但没有界面」的现象——这是缺少原生库导致 WPF 无法初始化，并非程序逻辑问题。",
     "Sending only the exe results in an endless spinner with no window: the WPF native libraries are missing, "
     "not a program logic issue.")

# ============================== 11. 导出、导入与分享 ==============================
h1("11. 导出、导入与分享", "11. Export, Import & Sharing")
ul([
    "导出：设置 → 数据与诊断，生成 JSON。默认仅含连接信息；勾选「包含凭据」时需设置导出密码，凭据单独加密后写入。/ Export: Settings → Data & Diagnostics produces JSON. Connection data only by default; enabling “include credentials” requires an export password and encrypts credentials separately.",
    "导入：先预览（新增 / 覆盖 / 跳过），可勾选「覆盖同名连接」「同时导入分组」；导错可在设置里回滚快照。/ Import: preview first (add / overwrite / skip), with options to overwrite same-name connections and import groups; a snapshot rollback is available if something goes wrong.",
    "分享：右键连接 →「复制连接信息」，生成不含密码的文本卡片，可直接粘贴到即时通讯工具。/ Share: right-click → “Copy connection info” produces a password-free text card ready to paste into a chat tool.",
])

# ============================== 12. 脚本管理 ==============================
h1("12. 脚本管理", "12. Script Management")
zh("登录成功后可自动执行 SAP GUI 脚本（VBScript）：右键连接 →「绑定脚本…」→「新建脚本」或「导入 .vbs」；"
   "编辑器内可「试运行」，保存后点「绑定到当前连接」。下次登录检测到会话窗口后，按绑定顺序用 cscript 执行。")
en("SAP GUI scripts (VBScript) can run automatically after a successful logon: right-click a connection → "
   "“Bind Script…”, then create a new script or import a .vbs file. Scripts can be trial-run in the editor "
   "and bound to the connection; they execute via cscript once the session window appears.")
note("需要服务器端启用 sapgui/user_scripting = TRUE，且客户端勾选「启用脚本」。不满足时脚本会失败并提示，但不影响正常登录。",
     "Requires sapgui/user_scripting = TRUE on the server and “Enable scripting” on the client. If not "
     "satisfied the script fails with a notice but normal logon is unaffected.")

# ============================== 13. 快捷键 ==============================
h1("13. 快捷键", "13. Keyboard Shortcuts")
tbl(["快捷键 / Key", "功能 / Action"],
    [["F5", "自动获取现有配置 / Sync from SAP Logon"],
     ["Ctrl + F", "聚焦搜索框 / Focus the search box"],
     ["Enter", "登录当前选中项 / Log on the selected item"],
     ["Ctrl + Alt + S", "全局快速搜索窗 / Global quick-search window"],
     ["Esc", "清空搜索或关闭快速搜索 / Clear search or close quick search"]],
    col_widths=[125, 325])
zh("快速搜索窗：输入名称或 SID，↑↓ 选择，Enter 立即登录，Esc 关闭。全局热键可在设置中修改。",
   space_before=4)
en("Quick search: type a name or SID, choose with ↑↓, press Enter to log on, Esc to close. The global "
   "hotkey is configurable in Settings.")

# ============================== 14. 数据存储与迁移 ==============================
h1("14. 数据存储与迁移", "14. Data Storage & Migration")

h2("14.1 数据位置", "14.1 Data Location")
zh("默认数据目录：%LOCALAPPDATA%\\SapLauncher（便携模式下为 exe 同级的 data\\ 目录）。")
en("Default data directory: %LOCALAPPDATA%\\SapLauncher (the data\\ folder next to the executable in "
   "portable mode).")
kv([
    ("manifest.json", "连接清单、分组与设置（不含密码）/ Connections, groups and settings (no passwords)"),
    ("vault.dat", "AES-256-GCM 加密的凭据库 / AES-256-GCM encrypted credential vault"),
    ("autologin.bin", "DPAPI 封装的「记住主密码」（不可跨机使用）/ DPAPI-wrapped “remember master password” (not transferable)"),
    ("scripts/", "VBS 脚本库 / VBScript library"),
    ("logs/", "日志，按日轮转保留 7 天 / Logs, rotated daily and kept for 7 days"),
    ("backup/", "同步快照，保留最近 5 份 / Sync snapshots (last 5 kept)"),
], key_width=125)

h2("14.2 换电脑迁移", "14.2 Migrating to Another PC")
ul([
    "整包便携（推荐）：启用便携模式后，把「exe + data」文件夹整体拷到新电脑。/ Portable (recommended): after enabling portable mode, copy the exe plus data folder.",
    "复制数据目录：把 %LOCALAPPDATA%\\SapLauncher 整个文件夹复制到新电脑同一路径。/ Copy the data directory to the same path on the new PC.",
    "导出 / 导入：在程序里导出（可勾选包含凭据）→ 拷到新电脑 → 导入。/ Export (optionally with credentials) and import on the new PC.",
], ordered=True)
note("凭据用主密码加密；「记住主密码」绑定当前 Windows 账户（DPAPI），换电脑后首次需重新输入一次主密码。"
     "迁移后如连接列表不对，先在设置里点「自动获取现有配置」同步本机 SAP Logon。",
     "Credentials are encrypted with the master password. “Remember master password” is bound to the "
     "Windows account (DPAPI); the master password must be entered once on the new PC. If the list looks "
     "wrong after migration, run “Sync from SAP Logon” in Settings first.")

# ============================== 15. 隐私与免责声明 ==============================
h1("15. 隐私与免责声明", "15. Privacy & Disclaimer")
ul([
    "所有数据保存在本机，不上传、不联网校验。/ All data is stored locally; nothing is uploaded or verified online.",
    "本工具为独立开发的第三方软件，与 SAP SE 无任何关联；SAP、SAP GUI 为 SAP SE 的商标。/ This is an independently developed third-party tool, not affiliated with SAP SE. SAP and SAP GUI are trademarks of SAP SE.",
    "请遵守你所在企业的信息安全规定；如需更高合规级别，可关闭「记住密码」，改为每次由 SAP 弹出登录框。/ Comply with your organization's security policies. For stricter compliance, disable “remember passwords” and let SAP show the logon dialog each time.",
])

# ============================== 16. 从源码构建 ==============================
h1("16. 从源码构建", "16. Build from Source")
zh("需要 .NET 8 SDK。在项目根目录执行：")
bq("powershell -ExecutionPolicy Bypass -File build\\publish.ps1")
zh("产物输出至 dist\\SAP登录管理器.exe（自包含单文件，原生库与 exe 同目录）。也可双击「一键打包.bat」自动完成清理与打包。")
en("Requires the .NET 8 SDK. Run the command above in the project root; output goes to "
   "dist\\SAP登录管理器.exe (self-contained single file with native libraries alongside). Alternatively, "
   "double-click “一键打包.bat” for a one-shot clean build.")
hr()
p("SAP 登录管理器 · 操作使用手册  |  SAP Logon Manager · User Manual — GURURU202611051345",
  font_size=9, alignment="center", font_color="#888888")

# ============================== 输出配置 ==============================
config = {
    "output": OUT_PDF,
    "page_size": "A4",
    "orientation": "portrait",
    "font_path": FONT,
    "margins": {"left": 64, "right": 64, "top": 60, "bottom": 66},
    "default_style": {"font_size": 10.5, "font_color": "#333333", "line_spacing": 1.35, "alignment": "left"},
    "footer": {
        "text": "SAP 登录管理器 · User Manual",
        "font_size": 8,
        "font_color": "#999999",
        "show_page_number": True,
        "page_number_format": "第 {page} 页 / Page {page}",
    },
    "title": "SAP 登录管理器 操作使用手册 / SAP Logon Manager User Manual",
    "author": "Gururu Nick",
    "subject": "SAP Logon Manager bilingual user manual",
    "elements": E,
}

with open(r"E:\WorkBuddy\SAP 登陆器\build\manual_config.json", "w", encoding="utf-8") as f:
    json.dump(config, f, ensure_ascii=False, indent=1)

print(f"OK elements={len(E)} -> build/manual_config.json")
