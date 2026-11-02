@echo off
chcp 936 >nul
cd /d "%~dp0"
rem 关闭正在运行的程序，避免 exe 被占用导致复制失败
taskkill /f /im "SAP登录管理器.exe" >nul 2>&1
taskkill /f /im "SapLauncher.exe" >nul 2>&1
echo.
echo 正在重新打包 SAP 登录管理器（含图标 / 作者 / 登录修复）...
echo 输出：dist\SAP登录管理器.exe
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "build\publish.ps1" "SAP登录管理器.exe"
if exist "dist\SAP登录管理器.exe" (
  echo.
  echo 打包完成，正在刷新桌面图标...
  ie4uinit.exe -cleariconcache >nul 2>&1
  ie4uinit.exe -show >nul 2>&1
  echo 完成：dist\SAP登录管理器.exe
) else (
  echo.
  echo 打包可能失败，请查看上方输出。
)
echo.
pause
