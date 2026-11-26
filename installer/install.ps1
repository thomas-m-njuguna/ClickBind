# ClickBind setup.
# IExpress runs this script with its console hidden, so anything written to the
# console is invisible to the person running the setup. Every problem is
# therefore reported with a message box and written to %TEMP%\ClickBind-setup.log.

$ErrorActionPreference = 'Stop'

$log     = Join-Path $env:TEMP 'ClickBind-setup.log'
$target  = Join-Path $env:LOCALAPPDATA 'Programs\ClickBind'
$zip     = Join-Path $PSScriptRoot 'ClickBind.zip'
$title   = 'ClickBind setup'

Add-Type -AssemblyName System.Windows.Forms

function Write-Log([string]$text) {
    $line = '{0}  {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $text
    try { Add-Content -LiteralPath $log -Value $line -Encoding UTF8 -ErrorAction Stop } catch { }
}

function Fail([string]$text) {
    Write-Log ('failed: ' + $text)
    $body = $text + "`n`nA log with the details was written to:`n" + $log
    [System.Windows.Forms.MessageBox]::Show($body, $title + ' failed', 'OK', 'Error') | Out-Null
    exit 1
}

Write-Log '--- ClickBind setup started ---'
Write-Log ('powershell ' + $PSVersionTable.PSVersion.ToString())
Write-Log ('os ' + [Environment]::OSVersion.VersionString)
Write-Log ('64-bit os ' + [Environment]::Is64BitOperatingSystem)
Write-Log ('temp dir ' + $PSScriptRoot)

# --- prerequisites ---------------------------------------------------------
# A clean PC can be missing these, and then the app installs but never shows a
# window, which looks exactly like "the setup did not work".

$problems = New-Object System.Collections.ArrayList

if (-not [Environment]::Is64BitOperatingSystem) {
    [void]$problems.Add('This PC is running 32-bit Windows. ClickBind needs 64-bit Windows 10 or Windows 11.')
}

$netRelease = 0
try { $netRelease = [int](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release } catch { $netRelease = 0 }
Write-Log ('.NET Framework release key ' + $netRelease)
if ($netRelease -lt 528040) {
    [void]$problems.Add('.NET Framework 4.8 is missing. Download it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and run this setup again.')
}

$webviewKeys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
)
$webviewVersion = ''
foreach ($key in $webviewKeys) {
    $pv = ''
    try { $pv = (Get-ItemProperty -LiteralPath $key -ErrorAction Stop).pv } catch { $pv = '' }
    if ($pv) { $webviewVersion = $pv; break }
}
Write-Log ('WebView2 runtime ' + $webviewVersion)

if (-not $webviewVersion) {
    [void]$problems.Add('The Microsoft Edge WebView2 Runtime is missing, and ClickBind needs it to draw its window. Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and run this setup again.')
}

if ($problems.Count -gt 0) {
    $text = "ClickBind needs one more thing before it can run on this PC:`n`n"
    foreach ($p in $problems) { $text += '  -  ' + $p + "`n`n" }
    Write-Log ('missing prerequisites: ' + ($problems -join ' | '))
    [System.Windows.Forms.MessageBox]::Show($text, 'ClickBind needs a prerequisite', 'OK', 'Warning') | Out-Null
    exit 1
}

# --- install ---------------------------------------------------------------
$running = Get-Process -Name 'ClickBind' -ErrorAction SilentlyContinue
if ($running) {
    Write-Log 'closing the running ClickBind'
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 900
}

Write-Log ('installing to ' + $target)
try {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
} catch {
    Fail ("The files could not be copied to`n" + $target + "`n`n" + $_.Exception.Message + "`n`nClose any open ClickBind window and run the setup again.")
}

$exe = Join-Path $target 'ClickBind.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Fail ('ClickBind.exe was not found in ' + $target + ' after copying the files.')
}

# --- shortcuts -------------------------------------------------------------
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcutDirs = @(
        [Environment]::GetFolderPath('Desktop'),
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
    )
    foreach ($dir in $shortcutDirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $lnk = $shell.CreateShortcut((Join-Path $dir 'ClickBind.lnk'))
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $target
        $lnk.IconLocation = $exe
        $lnk.Description = 'ClickBind mouse shortcuts'
        $lnk.Save()
    }

    $menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Uninstall ClickBind.lnk'
    $uninstall = $shell.CreateShortcut($menu)
    $uninstall.TargetPath = "$env:SystemRoot\System32\cmd.exe"
    $uninstall.Arguments = "/c `"$target\uninstall.cmd`""
    $uninstall.IconLocation = "$env:SystemRoot\System32\shell32.dll,131"
    $uninstall.Save()
    Write-Log 'shortcuts created'
} catch {
    Write-Log ('shortcut creation failed (not fatal): ' + $_.Exception.Message)
}

# Refresh the run-at-sign-in entry so it points at the installed copy.
try {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Get-ItemProperty -LiteralPath $runKey -Name ClickBind -ErrorAction SilentlyContinue) {
        Set-ItemProperty -LiteralPath $runKey -Name ClickBind -Value ('"' + $exe + '" --tray')
        Write-Log 'run-at-sign-in entry updated'
    }
} catch {
    Write-Log ('run entry update failed (not fatal): ' + $_.Exception.Message)
}

# --- start ----------------------------------------------------------------
Write-Log ('starting ' + $exe)
try {
    Start-Process -FilePath $exe -ErrorAction Stop
} catch {
    Fail ('ClickBind was installed to`n' + $target + "`nbut Windows refused to start it.`n`n" + $_.Exception.Message)
}

Start-Sleep -Seconds 4
$started = Get-Process -Name 'ClickBind' -ErrorAction SilentlyContinue
if (-not $started) {
    Fail ('ClickBind was installed to`n' + $target + "`nbut it closed immediately instead of staying open.`n`nRun it from the desktop shortcut, then check the log above for the reason.")
}

Write-Log '--- setup finished ---'
