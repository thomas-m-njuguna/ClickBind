@echo off
rem IExpress runs this with its window hidden, so install.ps1 reports every
rem problem with a message box instead of writing to this console.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
exit /b %errorlevel%
