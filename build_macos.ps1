# build_macos.ps1
# Automates publishing and packaging of Fujifier for macOS (Apple Silicon and Intel)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish"
$distDir = Join-Path $repoRoot "dist"
$releaseDir = Join-Path $repoRoot "release"
$dotnet = "dotnet"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$exifToolScript = @"
#!/bin/sh
if command -v exiftool >/dev/null 2>&1; then
    exec exiftool "`$@"
fi
if [ -x /opt/homebrew/bin/exiftool ]; then
    exec /opt/homebrew/bin/exiftool "`$@"
fi
if [ -x /usr/local/bin/exiftool ]; then
    exec /usr/local/bin/exiftool "`$@"
fi
if [ -x /usr/bin/exiftool ]; then
    exec /usr/bin/exiftool "`$@"
fi
if [ -x /opt/local/bin/exiftool ]; then
    exec /opt/local/bin/exiftool "`$@"
fi
exec exiftool "`$@"
"@

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Fujifier</string>
    <key>CFBundleDisplayName</key>
    <string>Fujifier</string>
    <key>CFBundleIdentifier</key>
    <string>dev.ipweb.fujifier</string>
    <key>CFBundleVersion</key>
    <string>2.1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>2.1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>Fujifier</string>
    <key>CFBundleIconFile</key>
    <string>logo.ico</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
</dict>
</plist>
"@

$macDngLab = if (Test-Path "$repoRoot\Fujify\libs\osx-arm64\dnglab") {
    "$repoRoot\Fujify\libs\osx-arm64\dnglab"
} elseif (Test-Path "$repoRoot\build_assets\tools\mac\dnglab") {
    "$repoRoot\build_assets\tools\mac\dnglab"
} else {
    $null
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Building Fujifier for macOS (Apple Silicon)..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -f net10.0 -r osx-arm64 --self-contained true -o "$publishDir\osx-arm64"

# 1. Package macOS Apple Silicon (.app bundle)
$macAppDir = Join-Path $distDir "Fujifier.app"
if (Test-Path $macAppDir) { Remove-Item -Recurse -Force $macAppDir }

$contentsDir = Join-Path $macAppDir "Contents"
$macOsDir = Join-Path $contentsDir "MacOS"
$resourcesDir = Join-Path $contentsDir "Resources"

New-Item -ItemType Directory -Force -Path $macOsDir | Out-Null
New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null

Copy-Item -Path "$publishDir\osx-arm64\*" -Destination $macOsDir -Recurse -Force

$macLibs = Join-Path $macOsDir "libs"
New-Item -ItemType Directory -Force -Path $macLibs | Out-Null
Remove-Item -Path (Join-Path $macLibs "dnglab_ffi.dll") -Force -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path (Join-Path $macLibs "linux-x64") -ErrorAction SilentlyContinue

if ($macDngLab) {
    Copy-Item -Path $macDngLab -Destination (Join-Path $macLibs "dnglab") -Force
    Copy-Item -Path $macDngLab -Destination (Join-Path $macOsDir "dnglab") -Force
}
[IO.File]::WriteAllText((Join-Path $macLibs "exiftool"), $exifToolScript.Replace("`r`n", "`n"))
[IO.File]::WriteAllText((Join-Path $macOsDir "exiftool"), $exifToolScript.Replace("`r`n", "`n"))

[IO.File]::WriteAllText((Join-Path $contentsDir "Info.plist"), $infoPlist.Replace("`r`n", "`n"))
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logo.ico" -Destination (Join-Path $resourcesDir "logo.ico") -Force
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logos.png" -Destination (Join-Path $resourcesDir "logo.png") -Force

# Archive macOS Apple Silicon
$macArmArchive = Join-Path $releaseDir "Fujifier-v2.1.0-macOS-AppleSilicon.zip"
if (Test-Path $macArmArchive) { Remove-Item -Force $macArmArchive }
& tar -a -cf $macArmArchive -C $distDir "Fujifier.app"
Write-Host " Created macOS Apple Silicon Archive: $macArmArchive" -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Building Fujifier for macOS (Intel)..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -f net10.0 -r osx-x64 --self-contained true -o "$publishDir\osx-x64"

# 2. Package macOS Intel (.app bundle)
$macIntelAppDir = Join-Path $distDir "Fujifier-Intel.app"
if (Test-Path $macIntelAppDir) { Remove-Item -Recurse -Force $macIntelAppDir }

$intelContentsDir = Join-Path $macIntelAppDir "Contents"
$intelMacOsDir = Join-Path $intelContentsDir "MacOS"
$intelResourcesDir = Join-Path $intelContentsDir "Resources"

New-Item -ItemType Directory -Force -Path $intelMacOsDir | Out-Null
New-Item -ItemType Directory -Force -Path $intelResourcesDir | Out-Null

Copy-Item -Path "$publishDir\osx-x64\*" -Destination $intelMacOsDir -Recurse -Force

$intelMacLibs = Join-Path $intelMacOsDir "libs"
New-Item -ItemType Directory -Force -Path $intelMacLibs | Out-Null
Remove-Item -Path (Join-Path $intelMacLibs "dnglab_ffi.dll") -Force -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path (Join-Path $intelMacLibs "linux-x64") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path (Join-Path $intelMacLibs "osx-arm64") -ErrorAction SilentlyContinue

# For Intel Mac: DO NOT copy ARM64 binary into intelMacLibs or intelMacOsDir!
$macIntelDngLab = if (Test-Path "$repoRoot\Fujify\libs\osx-x64\dnglab") {
    "$repoRoot\Fujify\libs\osx-x64\dnglab"
} elseif (Test-Path "$repoRoot\build_assets\tools\mac_x64\dnglab") {
    "$repoRoot\build_assets\tools\mac_x64\dnglab"
} else {
    $null
}

if ($macIntelDngLab) {
    Copy-Item -Path $macIntelDngLab -Destination (Join-Path $intelMacLibs "dnglab") -Force
    Copy-Item -Path $macIntelDngLab -Destination (Join-Path $intelMacOsDir "dnglab") -Force
} else {
    # If no standalone x86_64 binary is bundled, provide an intelligent launcher script
    # that delegates to the system-installed dnglab (from Homebrew or Cargo)
    # without failing with "Bad CPU type in executable".
    $dngLabLauncherScript = @"
#!/bin/sh
if command -v dnglab >/dev/null 2>&1; then
    exec dnglab "`$@"
fi
if [ -x /usr/local/bin/dnglab ]; then
    exec /usr/local/bin/dnglab "`$@"
fi
if [ -x "`$HOME/.cargo/bin/dnglab" ]; then
    exec "`$HOME/.cargo/bin/dnglab" "`$@"
fi
if [ -x /usr/bin/dnglab ]; then
    exec /usr/bin/dnglab "`$@"
fi
if [ -x /opt/homebrew/bin/dnglab ]; then
    exec /opt/homebrew/bin/dnglab "`$@"
fi
echo "Error: dnglab not found on system. On Intel Mac, please run: brew install dnglab" >&2
exit 127
"@
    [IO.File]::WriteAllText((Join-Path $intelMacLibs "dnglab"), $dngLabLauncherScript.Replace("`r`n", "`n"))
    [IO.File]::WriteAllText((Join-Path $intelMacOsDir "dnglab"), $dngLabLauncherScript.Replace("`r`n", "`n"))
}
[IO.File]::WriteAllText((Join-Path $intelMacLibs "exiftool"), $exifToolScript.Replace("`r`n", "`n"))
[IO.File]::WriteAllText((Join-Path $intelMacOsDir "exiftool"), $exifToolScript.Replace("`r`n", "`n"))

[IO.File]::WriteAllText((Join-Path $intelContentsDir "Info.plist"), $infoPlist.Replace("`r`n", "`n"))
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logo.ico" -Destination (Join-Path $intelResourcesDir "logo.ico") -Force
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logos.png" -Destination (Join-Path $intelResourcesDir "logo.png") -Force

# Archive macOS Intel
$macIntelArchive = Join-Path $releaseDir "Fujifier-v2.1.0-macOS-Intel.zip"
if (Test-Path $macIntelArchive) { Remove-Item -Force $macIntelArchive }
& tar -a -cf $macIntelArchive -C $distDir "Fujifier-Intel.app"
Write-Host " Created macOS Intel Archive: $macIntelArchive" -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Green
Write-Host " macOS build completed successfully!" -ForegroundColor Green
Write-Host " - Apple Silicon Archive: $macArmArchive" -ForegroundColor Green
Write-Host " - Intel Archive:         $macIntelArchive" -ForegroundColor Green
Write-Host " - App Bundle:            $macAppDir" -ForegroundColor Green
Write-Host " - Intel App Bundle:      $macIntelAppDir" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

