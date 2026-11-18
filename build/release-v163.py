# -*- coding: utf-8 -*-
"""创建 GitHub Release v1.6.3 并上传 4 个 ASCII 名附件（幂等）。

凭据从用户级私有文件读取，不打印、不写入日志。
用户已在会话中明确授权本次 Release。
"""
import pathlib

import requests

REPO = "NicknotDick/sap-logon-manager"
API = "https://api.github.com"
TAG = "v1.6.3"
ROOT = pathlib.Path(r"E:\WorkBuddy\SAP 登陆器")
TOKEN_FILE = pathlib.Path(r"C:\Users\camyo\.workbuddy\secrets\github_pat.txt")

# (附件名, 本地路径, Content-Type)
ASSETS = [
    ("SAPLogonManager-v1.6.3.zip",
     ROOT / "release" / "SAP登录管理器-v1.6.3.zip", "application/zip"),
    ("SAPLogonManager-v1.6.3-SingleFile.exe",
     ROOT / "release" / "SAP登录管理器-v1.6.3-单文件版.exe", "application/octet-stream"),
    ("SAPLogonManager-User-Manual-v1.6.3.pdf",
     ROOT / "SAP登录管理器-操作使用手册-User-Manual.pdf", "application/pdf"),
    ("SAPLogonManager-Distribution-Notes-v1.6.3.txt",
     ROOT / "release" / "SAP登录管理器-v1.6.3" / "分发说明-必读.txt", "text/plain"),
]

BODY = """## SAP 登录管理器 v1.6.3

本版围绕「多客户端 × 多账号」重构凭据管理，并重新设计了右键登录菜单。操作手册同步更新至 23 页，
新增「版本迭代」栏目（17 章）。

### 主要更新

**1. 多客户端 × 多账号，数量不限**
- 同一连接下可保存任意多个客户端，每个客户端下任意多个账号，总数不设上限
- 保存维度：客户端 + 姓名 + 账号 + 密码 + 登录语言；「客户端 + 账号」唯一，姓名/密码/语言为属性
- 各客户端各有一个默认账号；一次保存同步所有已编辑客户端，整库只加密写盘一次

**2. 右键菜单全新交互（平铺 + 语言子菜单）**
- 账号平铺显示「客户端 · 账号（姓名）」，客户端仅显示数字；带 `Enter` 标记的一行为默认账号，同一账号不重复出现
- 鼠标悬停到账号文字右方的空白/箭头区域 → 展开 ZH / EN 语言子菜单，点语言即按所选语言登录
- 直接点击账号文字 → 按该账号配置的语言登录；双击/回车 → 默认账号一键登录

**3. 删除账号即时落盘**
- 确认删除立即写盘生效，不再依赖「保存」；同名重复条目只保留带姓名的那条并合并信息（都不带姓名才整条删除），删除后不再「复活」

**4. 编辑器只显示成功登录过的客户端**
- 「登录过的客户端」由登录成功时自动记录（主窗口与批量登录均记录），历史数据自动迁移，导出/导入同步携带

**5. 备份回滚自动迁移**
- 「回滚到最近快照」与首次加载共用同一套规范化：恢复旧快照后自动完成新字段迁移与兜底

**6. 「关于」窗口数据目录可点击**
- 点击数据目录路径直接在资源管理器中打开对应文件夹；链接样式统一为主题强调色

### 质量验证

- 编译 0 错误；自检从 45 项扩充至 **49 项**（新增菜单格式/去重/语言列表/删除合并/登录客户端记录/导出保真/备份回滚迁移），全部通过
- zip 版与单文件版各自复验 49 通过 / 0 失败

### 下载与使用

| 附件 | 说明 |
|---|---|
| `SAPLogonManager-v1.6.3.zip` | **推荐**。解压后运行 `SAP登录管理器.exe`；5 个 WPF 原生 DLL 必须与 exe 同目录 |
| `SAPLogonManager-v1.6.3-SingleFile.exe` | 单文件版，原生库首次运行时自解压到临时目录；受安全策略限制时改用 zip |
| `SAPLogonManager-User-Manual-v1.6.3.pdf` | 操作使用手册（23 页，13 张纯演示数据截图，中英双语，含版本迭代栏目） |
| `SAPLogonManager-Distribution-Notes-v1.6.3.txt` | 分发说明（必读）：升级步骤、整包分发要求 |

> 附件名使用 ASCII 是 GitHub Release 对非 ASCII 文件名过滤所致；zip 内文件仍为中文名。

### 升级提示

1. 先在**系统托盘退出旧实例**（旧实例可能不响应唤出指令，表现为「双击无反应」）。
2. 用新文件**整体替换**原目录，不要只拷 exe。
3. 保留原有数据目录 `%LOCALAPPDATA%\\SapLauncher`，连接与凭据不会丢失；老数据（登录过的客户端列表）会在首次启动时自动迁移。

环境要求：Windows 10/11 x64、SAP GUI for Windows 7.40+（8.0 已实测）。

### 安全说明

- 未启用主密码时，安全性依赖 Windows 账户登录与文件权限；共享电脑建议启用主密码
- 日志只记录事件、错误码与异常堆栈，**从不记录密码**
"""


def main():
    token = TOKEN_FILE.read_text(encoding="utf-8").strip()
    h = {
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    s = requests.Session()
    s.headers.update(h)
    base = f"{API}/repos/{REPO}/releases"

    r = s.get(f"{base}/tags/{TAG}", timeout=60)
    if r.status_code == 200:
        rel = r.json()
        print("Release 已存在，复用:", rel["id"])
    elif r.status_code == 404:
        r = s.post(base, json={
            "tag_name": TAG,
            "target_commitish": "main",
            "name": "SAP 登录管理器 v1.6.3",
            "body": BODY,
            "draft": False,
            "prerelease": False,
        }, timeout=120)
        if r.status_code not in (201, 204):
            raise SystemExit(f"创建 Release 失败: HTTP {r.status_code} {r.text[:300]}")
        if r.status_code == 201:
            rel = r.json()
            print("Release 已创建:", rel["id"])
        else:  # 204 空响应：重新查询
            r = s.get(f"{base}/tags/{TAG}", timeout=60)
            rel = r.json()
            print("Release 已创建(204):", rel["id"])
    else:
        raise SystemExit(f"查询 Release 失败: HTTP {r.status_code}")

    rid = rel["id"]
    # 幂等：删掉同名旧附件
    existing = s.get(f"{base}/{rid}/assets", timeout=60).json()
    for name, _, _ in ASSETS:
        for a in existing:
            if a["name"] == name:
                rr = s.delete(f"{base}/assets/{a['id']}", timeout=60)
                print("删除同名旧附件", name, "HTTP", rr.status_code)

    for name, path, ctype in ASSETS:
        data = path.read_bytes()
        with path.open("rb") as f:
            up = dict(h)
            up["Content-Type"] = ctype
            rr = requests.post(
                f"https://uploads.github.com/repos/{REPO}/releases/{rid}/assets?name={name}",
                headers=up, data=f, timeout=(60, 600),
            )
        print("上传", name, len(data), "bytes → HTTP", rr.status_code)
        if rr.status_code not in (200, 201):
            raise SystemExit(f"上传失败 {name}: {rr.status_code} {rr.text[:300]}")

    # 校验附件清单与大小
    assets = {a["name"]: a["size"] for a in s.get(f"{base}/{rid}/assets", timeout=60).json()}
    print("最终附件清单:")
    for k, v in assets.items():
        print(" ", k, v)
    for name, path, _ in ASSETS:
        if assets.get(name) != path.stat().st_size:
            raise SystemExit("附件大小校验失败：" + name)
    print("OK: Release", TAG, "附件齐全且大小一致")
    print("URL:", f"https://github.com/{REPO}/releases/tag/{TAG}")


if __name__ == "__main__":
    main()
