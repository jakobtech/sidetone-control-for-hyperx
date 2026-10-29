# Builds the Microsoft Store package dist\SidetoneControlForHyperX-<version>-x64.msix.
# Needs makeappx.exe from the Windows SDK. The Store signs the package on submission, so it stays unsigned here.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
. .\scripts\Icon.ps1
$version = '1.3.1.0'

& powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE) { throw 'build.ps1 failed' }

$config = Get-Content msix\store.config.json -Raw -Encoding UTF8 | ConvertFrom-Json  # UTF8: keeps "ü" in the publisher name intact
if ($config.Publisher -like '*REPLACE*' -or $config.IdentityName -like '*REPLACE*') {
    Write-Warning 'msix\store.config.json still has placeholder values. The package builds, but Partner Center will reject it until the identity matches.'
}

# Package layout: both exes, the helper's runtime config, logos and the manifest.
$layout = 'build\msix-layout'
Remove-Item -Recurse -Force $layout -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$layout\Assets" | Out-Null
Copy-Item build\SidetoneControl.exe, build\SidetoneHelper.exe $layout
'{"runtimeOptions":{"tfm":"net8.0","rollForward":"LatestMajor","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' |
    Out-File -Encoding ascii "$layout\SidetoneHelper.runtimeconfig.json"

$assets = Join-Path $PSScriptRoot "$layout\Assets"
Save-Png (New-IconBitmap 50) "$assets\StoreLogo.png"
Save-Png (New-IconBitmap 44) "$assets\Square44x44Logo.png"
Save-Png (New-IconBitmap 150) "$assets\Square150x150Logo.png"
# Wide tile: artwork on the left, spaced wordmark on the right.
$wide = New-Object Drawing.Bitmap 310, 150
$g = [Drawing.Graphics]::FromImage($wide)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([Drawing.Color]::FromArgb(255, 20, 20, 22))
Draw-Glyph $g 86 18 32 $false
$font = New-Object Drawing.Font 'Bahnschrift SemiBold', 15
$g.DrawString('SIDETONE', $font, [Drawing.Brushes]::White, 116, 40)
$g.DrawString('CONTROL', $font, [Drawing.Brushes]::White, 116, 64)
$small = New-Object Drawing.Font 'Bahnschrift', 9
$g.DrawString('FOR HYPERX', $small, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 150, 150, 154))), 118, 92)
$g.Dispose()
Save-Png $wide "$assets\Wide310x150Logo.png"

$manifest = Get-Content msix\AppxManifest.template.xml -Raw -Encoding UTF8
foreach ($pair in @{ IdentityName = $config.IdentityName; Publisher = $config.Publisher; PublisherDisplayName = $config.PublisherDisplayName; DisplayName = $config.DisplayName; Version = $version }.GetEnumerator()) {
    $manifest = $manifest.Replace('{' + $pair.Key + '}', [Security.SecurityElement]::Escape([string]$pair.Value))
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot "$layout\AppxManifest.xml"), $manifest, (New-Object Text.UTF8Encoding $false))
[xml](Get-Content "$layout\AppxManifest.xml" -Raw) | Out-Null  # fails here if the manifest is not well-formed XML

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) {
    Write-Warning "Package layout is ready in $layout, but makeappx.exe was not found. Install the Windows SDK, then run this script again."
    exit 2
}
$msix = "dist\SidetoneControlForHyperX-$version-x64.msix"
& $makeappx.FullName pack /o /d $layout /p $msix
if ($LASTEXITCODE) { throw 'makeappx failed' }
Write-Host "Built $msix"
