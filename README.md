# SAP 登录管理器

Windows 桌面工具（C# WPF / .NET 8）：把 SAP Logon 的连接与账号密码管起来，双击直达系统。

> 面向使用者的完整说明见 [使用说明.md](使用说明.md)

> 另有一个 **Python 写的简易版**（13.5MB 单文件，模拟前台自动填写账号密码 + 点登录）：
> 产物 `SAP登录器-简易版.exe`（项目根目录），源码与说明见 [py/README.md](py/README.md)。
> 两者互不干扰：C# 版是「连接/凭据管理器」，Python 版是「一键登录小工具」。

## 功能

- **自动获取现有配置**：解析 `SAPUILandscape.xml`（含 `<Includes>` 递归、Router、消息服务器）与旧版 `saplogon.ini`，一键同步增删改
- **一键/批量登录**：调用 SAP 官方 `sapshcut.exe`，按「显示名称」精确定位，规避同名 SID 串系统
- **凭据安全**：主密码 + PBKDF2 + AES-256-GCM；可选 DPAPI「记住主密码」；闲置自动锁定
- **组织能力**：分组、搜索、排序、收藏、环境徽标（生产红色）、事务码直达
- **迁移协作**：导出/导入 JSON（凭据可选加密），右键复制不含密码的连接卡片
- **脚本**：管理 VBS 并绑定到连接，登录后自动执行
- **集成**：托盘常驻、全局热键快速搜索、开机自启、单实例、中英双语、深浅主题

## 目录结构

```
src/SapLauncher/          WPF 工程
  Models/                 数据模型
  Services/               解析、同步、加密、启动、批量、脚本、导出导入
  Views/                  各窗口
  Themes/                 Dark.xaml / Light.xaml / Controls.xaml
  Resources/              Strings.zh-CN.xaml / Strings.en-US.xaml
  Interop/                DPAPI、ANSI 编码、窗口枚举、热键
tools/make_icon.py        生成应用图标
build/publish.ps1         打包单文件 EXE
dist/                     产物
```

## 构建

```powershell
# 需要 .NET 8 SDK（脚本默认使用本地 SDK 路径，可按需修改 $sdkRoot）
powershell -ExecutionPolicy Bypass -File build\publish.ps1 "SAP登录管理器.exe"
```

产物：`dist\SAP登录管理器.exe`（单文件、自包含、压缩）

## 开发期自检

```powershell
# 核心逻辑 + 全部窗口 XAML 实例化检查，报告写入数据目录 selftest-latest.txt
SapLauncher.exe --datadir=<临时目录> --selftest

# 离屏渲染各窗口 PNG，便于视觉核对（不弹窗）
SapLauncher.exe --datadir=<临时目录> --shots=<输出目录>
```

其它参数：`--sync`（无界面同步）、`--minimized`（托盘启动）、`--init-vault=<pwd>`（仅在凭据库不存在时初始化，用于自动化测试）。

## 关键实现约定

1. **定位系统一律用 `Description`（SAP Logon 显示名）**。本机存在同名 SID（S4D / S4Q 各两条），用 `-system=<SID>` 可能连到另一套系统。
2. 临时 `.sap` 采用 **ANSI（CP936）+ CRLF** 编码，与 SAP GUI 8.0 实测一致；中文系统名依赖此编码。
3. `sapshcut.exe` 是 GUI 子系统程序，退出码不可靠，登录结果以「是否出现 `SAP_FRONTEND_SESSION` 窗口」判定。
4. 密码默认不进命令行；走临时 `.sap` 文件，用后覆写并删除。

## 免责

独立开发的第三方工具，与 SAP SE 无关联。SAP、SAP GUI 为 SAP SE 的商标。
