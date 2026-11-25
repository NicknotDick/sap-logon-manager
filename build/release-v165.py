# -*- coding: utf-8 -*-
"""创建 GitHub Release v1.6.5 并上传 4 个 ASCII 名附件（幂等）。

凭据从用户级私有文件读取，不打印、不写入日志。
用户已在会话中明确授权本次 Release。
"""
import pathlib

import requests

REPO = "NicknotDick/sap-logon-manager"
API = "https://api.github.com"
TAG = "v1.6.5"
ROOT = pathlib.Path(r"E:\WorkBuddy\SAP 登陆器")
TOKEN_FILE = pathlib.Path(r"C:\Users\camyo\.workbuddy\secrets\github_pat.txt")

# (附件名, 本地路径, Content-Type)
ASSETS = [
    ("SAPLogonManager-v1.6.5.zip",
     ROOT / "release" / "SAP登录管理器-v1.6.5.zip", "application/zip"),
    ("SAPLogonManager-v1.6.5-SingleFile.exe",
     ROOT / "release" / "SAP登录管理器-v1.6.5-单文件版.exe", "application/octet-stream"),
    ("SAPLogonManager-User-Manual-v1.6.5.pdf",
     ROOT / "SAP登录管理器-操作使用手册-User-Manual.pdf", "application/pdf"),
    ("SAPLogonManager-Distribution-Notes-v1.6.5.txt",
     ROOT / "release" / "SAP登录管理器-v1.6.5" / "分发说明-必读.txt", "text/plain"),
]

BODY = """## SAP 登录管理器 v1.6.5

本版新增「查找更新」与每日启动检查，并同步更新操作手册至 v1.6.5（24 页，含 9.11 检查更新章节）。

### 主要更新

**查找更新（关于窗口 + 启动检查）**
- 「关于」窗口新增「查找更新」按钮：读取 GitHub 最新发布信息；发现新版本时显示版本、发布时间与更新摘要，确认后用默认浏览器下载
- 每天首次启动在后台静默检查一次：有新版本才弹提示窗（「查看新版本」/「稍后再说」），无新版本或网络失败均不提示
- 两种入口共用同一下载逻辑：按当前运行形态自动匹配 zip / 单文件安装包，找不到对应附件时打开发布页
- 检查只读取 GitHub 公开发布信息，不上传任何本机连接、账号或凭据；每日检查日期存于数据目录 `update-check.txt`

**快速搜索账号 + 语言**
- 结果行整行右击即可选账号与 ZH/EN 登录语言，与主窗口右键菜单共用同一套逻辑

### 下载与使用

| 附件 | 说明 |
|---|---|
| `SAPLogonManager-v1.6.5.zip` | **推荐**。解压后运行 `SAP登录管理器.exe`；5 个 WPF 原生 DLL 必须与 exe 同目录 |
| `SAPLogonManager-v1.6.5-SingleFile.exe` | 单文件版，原生库首次运行时自解压到临时目录；受安全策略限制时改用 zip |
| `SAPLogonManager-User-Manual-v1.6.5.pdf` | 操作使用手册（24 页，13 张纯演示数据截图，中英双语，含版本迭代栏目） |
| `SAPLogonManager-Distribution-Notes-v1.6.5.txt` | 分发说明（必读）：升级步骤、整包分发要求 |

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
            "name": "SAP 登录管理器 v1.6.5",
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
