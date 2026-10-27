# -*- coding: utf-8 -*-
"""生成两个 GBK 编码的双击脚本：
  - 一键打包.bat：重新打包 + 自动刷新桌面图标缓存
  - 刷新桌面图标.bat：仅刷新桌面图标缓存
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def write_bat(name, text):
    text = text.replace('\r\n', '\n').replace('\n', '\r\n')
    path = os.path.join(ROOT, name)
    with open(path, 'wb') as f:
        f.write(text.encode('gbk'))
    print('written:', path)


pack_bat = '''@echo off
chcp 936 >nul
cd /d "%~dp0"
rem 关闭正在运行的程序，避免 exe 被占用导致复制失败
taskkill /f /im "SAP登录管理器.exe" >nul 2>&1
taskkill /f /im "SapLauncher.exe" >nul 2>&1
echo.
echo 正在重新打包 SAP 登录管理器（含图标 / 作者 / 登录修复）...
echo 输出：dist\\SAP登录管理器.exe
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "build\\publish.ps1" "SAP登录管理器.exe"
if exist "dist\\SAP登录管理器.exe" (
  echo.
  echo 打包完成，正在刷新桌面图标...
  ie4uinit.exe -cleariconcache >nul 2>&1
  ie4uinit.exe -show >nul 2>&1
  echo 完成：dist\\SAP登录管理器.exe
) else (
  echo.
  echo 打包可能失败，请查看上方输出。
)
echo.
pause
'''

refresh_bat = '''@echo off
chcp 936 >nul
echo 正在刷新桌面图标缓存...
ie4uinit.exe -cleariconcache >nul 2>&1
ie4uinit.exe -show >nul 2>&1
echo.
echo 完成。若桌面图标仍是旧的，请按 F5 刷新桌面，或注销后重新登录一次。
pause
'''

write_bat('一键打包.bat', pack_bat)
write_bat('刷新桌面图标.bat', refresh_bat)
