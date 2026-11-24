# -*- coding: utf-8 -*-
"""创建 GitHub Release v1.6.4 并上传 4 个 ASCII 名附件（幂等）。

凭据从用户级私有文件读取，不打印、不写入日志。
用户已在会话中明确授权本次 Release。
"""
import pathlib

import requests

REPO = "NicknotDick/sap-logon-manager"
API = "https://api.github.com"
TAG = "v1.6.4"
ROOT = pathlib.Path(r"E:\WorkBuddy\SAP 登陆器")
TOKEN_FILE = pathlib.Path(r"C:\Users\camyo\.workbuddy\secrets\github_pat.txt")

# (附件名, 本地路径, Content-Type)
ASSETS = [
    ("SAPLogonManager-v1.6.4.zip",
     ROOT / "release" / "SAP登录管理器-v1.6.4.zip", "application/zip"),
    ("SAPLogonManager-v1.6.4-SingleFile.exe",
     ROOT / "release" / "SAP登录管理器-v1.6.4-单文件版.exe", "application/octet-stream"),
    ("SAPLogonManager-User-Manual-v1.6.4.pdf",
     ROOT / "SAP登录管理器-操作使用手册-User-Manual.pdf", "application/pdf"),
    ("SAPLogonManager-Distribution-Notes-v1.6.4.txt",
     ROOT / "release" / "SAP登录管理器-v1.6.4" / "分发说明-必读.txt", "text/plain"),
]

BODY = """## SAP 登录管理器 v1.6.4

本版新增主题「跟随系统」模式，并同步更新操作手册至 v1.6.4（24 页，含 9.10 主题章节）。

### 主要更新

**主题模式：跟随系统 / 深色 / 浅色**
- 「设置 → 通用 → 主题」新增「跟随系统」选项
- 选择后按 Windows 应用颜色模式自动选择界面主题；在系统设置中切换深浅色时，
  已打开的窗口即时跟随，无需重启，标题栏配色同步刷新
- 手动选择深色或浅色则固定，不随 Windows 设置变化
- 系统颜色设置不可读时回退深色；仅影响界面配色，不影响连接、登录或凭据

### 下载与使用

| 附件 | 说明 |
|---|---|
| `SAPLogonManager-v1.6.4.zip` | **推荐**。解压后运行 `SAP登录管理器.exe`；5 个 WPF 原生 DLL 必须与 exe 同目录 |
| `SAPLogonManager-v1.6.4-SingleFile.exe` | 单文件版，原生库首次运行时自解压到临时目录；受安全策略限制时改用 zip |
| `SAPLogonManager-User-Manual-v1.6.4.pdf` | 操作使用手册（24 页，13 张纯演示数据截图，中英双语，含版本迭代栏目） |
| `SAPLogonManager-Distribution-Notes-v1.6.4.txt` | 分发说明（必读）：升级步骤、整包分发要求 |

> 附件名使用 ASCII 是 GitHub Release 对非 ASCII 文件名过滤所致；zip 内文件仍为中文名。

### 升级提示

1. 先在**系统托盘退出旧实例**（旧实例可能不响应唤出指令，表现为「双击无反应」）。
2. 用新文件**整体替换**原目录，不要只拷 exe。
3. 保留原有数据目录 `%LOCALAPPDATA%\\SapLauncher`，连接与凭据不会丢失。

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
            "name": "SAP 登录管理器 v1.6.4",
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
