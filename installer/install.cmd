@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo ClickBind setup failed. Close any open ClickBind window and run it again.
  pause
  exit /b 1
)
