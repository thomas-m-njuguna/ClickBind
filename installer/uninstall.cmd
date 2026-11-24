@echo off
setlocal
set "TARGET=%LOCALAPPDATA%\Programs\ClickBind"

echo Removing ClickBind...
taskkill /IM ClickBind.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ClickBind /f >nul 2>&1
del "%USERPROFILE%\Desktop\ClickBind.lnk" >nul 2>&1
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\ClickBind.lnk" >nul 2>&1
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Uninstall ClickBind.lnk" >nul 2>&1
rmdir /S /Q "%APPDATA%\ClickBind" >nul 2>&1

cd /d "%TEMP%"
rmdir /S /Q "%TARGET%" >nul 2>&1

echo ClickBind has been removed, including its settings.
timeout /t 3 >nul
