# Installs the unsigned MSIX layout for local testing (requires Windows developer mode) and starts the app.
# Use -Remove to uninstall the test package again.
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$config = Get-Content msix\store.config.json -Raw | ConvertFrom-Json
$dev = Join-Path $env:LOCALAPPDATA 'SidetoneControlDev'

$existing = Get-AppxPackage -Name $config.IdentityName
if ($existing) { Remove-AppxPackage $existing.PackageFullName; Write-Host "Removed $($existing.PackageFullName)" }
if ($Remove) { Remove-Item -Recurse -Force $dev -ErrorAction SilentlyContinue; return }

# Register from outside the build folder; the deployment service writes package metadata next to the manifest.
Remove-Item -Recurse -Force $dev -ErrorAction SilentlyContinue
Copy-Item -Recurse build\msix-layout $dev
Remove-Item -Recurse -Force (Join-Path $dev 'microsoft.system.package.metadata') -ErrorAction SilentlyContinue
Add-AppxPackage -Register (Join-Path $dev 'AppxManifest.xml')
$pkg = Get-AppxPackage -Name $config.IdentityName
Write-Host "Registered $($pkg.PackageFullName)"
Write-Host "Install location: $($pkg.InstallLocation)"
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!SidetoneControl"
Write-Host 'Started Sidetone Control for HyperX (packaged).'
