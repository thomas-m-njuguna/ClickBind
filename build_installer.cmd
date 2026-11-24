@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\build_installer.ps1"
if errorlevel 1 exit /b 1
