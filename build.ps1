# Builds Sidetone Control for HyperX with the C# compiler that ships with Windows (no SDK needed)
# and packs dist\SidetoneControlForHyperX-<version>.zip.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
. .\scripts\Icon.ps1
$version = '1.3.1'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force build, dist | Out-Null

if (-not (Test-Path build\app.ico)) { Save-Ico (Join-Path $PSScriptRoot 'build\app.ico') }

& $csc /nologo /codepage:65001 /optimize /out:build\SidetoneHelper.exe src\Helper.cs
if ($LASTEXITCODE) { throw 'Helper build failed' }
& $csc /nologo /codepage:65001 /optimize /target:winexe /win32icon:build\app.ico `
    /resource:build\SidetoneHelper.exe,SidetoneHelper.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:build\SidetoneControl.exe src\App.cs
if ($LASTEXITCODE) { throw 'App build failed' }

# Short staging name: deep checkout folders plus long file names can exceed MAX_PATH.
$stage = Join-Path $PSScriptRoot 'build\zip'
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item build\SidetoneControl.exe, README.txt -Destination $stage
$zip = Join-Path 'dist' "SidetoneControlForHyperX-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Built $zip"
