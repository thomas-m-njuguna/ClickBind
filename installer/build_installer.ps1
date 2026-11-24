# Builds a single-file Windows installer (ClickBindSetup.exe) from dist\.
# Requires the app to be built first (build.cmd) and uses IExpress, which ships
# with Windows, so no extra tooling or admin rights are needed.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $root 'build\installer'
$setup = Join-Path $stage 'ClickBindSetup.exe'

if (-not (Test-Path (Join-Path $dist 'ClickBind.exe'))) {
    throw 'dist\ClickBind.exe is missing. Run build.cmd first.'
}

Write-Host 'Staging installer payload...'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$payload = Join-Path $stage 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null

Copy-Item (Join-Path $dist '*') $payload -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall.cmd') $payload -Force
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath (Join-Path $stage 'ClickBind.zip') -CompressionLevel Optimal -Force

Copy-Item (Join-Path $PSScriptRoot 'install.cmd') $stage -Force
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $stage -Force

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=ClickBind is installed. Open it from the desktop shortcut or the tray icon.
TargetName=$setup
FriendlyName=ClickBind Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0="ClickBind.zip"
FILE1="install.cmd"
FILE2="install.ps1"
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@

$sedPath = Join-Path $stage 'clickbind.sed'
$sed | Set-Content -Path $sedPath -Encoding ASCII -Force

Write-Host 'Building ClickBindSetup.exe with IExpress...'
& "$env:WINDIR\System32\iexpress.exe" /N /Q $sedPath
# IExpress reports 1 even when the package is produced successfully, so trust
# the output file rather than its exit code.
if (-not (Test-Path $setup)) {
    throw 'IExpress did not produce ClickBindSetup.exe.'
}

$size = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host "Built $setup ($size MB)"
