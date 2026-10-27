@echo off
chcp 936 >nul
echo 正在刷新桌面图标缓存...
ie4uinit.exe -cleariconcache >nul 2>&1
ie4uinit.exe -show >nul 2>&1
echo.
echo 完成。若桌面图标仍是旧的，请按 F5 刷新桌面，或注销后重新登录一次。
pause
