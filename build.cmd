@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  exit /b 1
)
if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /optimize+ /out:dist\ClickBind.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Web.Extensions.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll ^
  /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  src\*.cs
if errorlevel 1 exit /b 1

copy /Y lib\Microsoft.Web.WebView2.Core.dll dist >nul
copy /Y lib\Microsoft.Web.WebView2.WinForms.dll dist >nul
copy /Y lib\WebView2Loader.dll dist >nul
copy /Y app.config dist\ClickBind.exe.config >nul
if not exist dist\ui mkdir dist\ui
xcopy /E /I /Y ui dist\ui >nul
echo Built dist\ClickBind.exe
