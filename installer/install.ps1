$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\ClickBind'
$zip = Join-Path $PSScriptRoot 'ClickBind.zip'

Write-Host ''
Write-Host '  ClickBind setup' -ForegroundColor Cyan
Write-Host '  ---------------'
Write-Host "  Installing to $target"
Write-Host ''

New-Item -ItemType Directory -Force -Path $target | Out-Null
Expand-Archive -LiteralPath $zip -DestinationPath $target -Force

$exe = Join-Path $target 'ClickBind.exe'
$shell = New-Object -ComObject WScript.Shell

$shortcutDirs = @(
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)

foreach ($dir in $shortcutDirs) {
    if (-not (Test-Path $dir)) { continue }
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

# Refresh the run-at-sign-in entry so it points at the installed copy.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -Path $runKey -Name ClickBind -ErrorAction SilentlyContinue)) {
    Set-ItemProperty -Path $runKey -Name ClickBind -Value ('"' + $exe + '" --tray')
}

if (-not (Get-Process ClickBind -ErrorAction SilentlyContinue)) {
    Start-Process $exe
}

Write-Host '  Done. ClickBind is in your Start menu and on the desktop.'
Write-Host '  It runs in the system tray and works in every app.'
Write-Host ''
