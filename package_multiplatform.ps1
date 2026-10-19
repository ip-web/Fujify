# package_multiplatform.ps1
# Automates publishing and packaging of Fujify for Linux and macOS

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish"
$distDir = Join-Path $repoRoot "dist"
$releaseDir = Join-Path $repoRoot "release"
$dotnet = "C:\Users\isisk\.dotnet\dotnet.exe"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Building Fujify for Linux (x64)..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -r linux-x64 --self-contained true -o "$publishDir\linux-x64"

# 1. Package Linux x64
$linuxDist = Join-Path $distDir "Fujify-v1.1-Linux-x64"
if (Test-Path $linuxDist) { Remove-Item -Recurse -Force $linuxDist }
New-Item -ItemType Directory -Force -Path $linuxDist | Out-Null

Copy-Item -Path "$publishDir\linux-x64\*" -Destination $linuxDist -Recurse -Force

# Setup Linux native binaries in libs
$linuxLibs = Join-Path $linuxDist "libs"
# Remove Windows-specific exes from libs
Remove-Item -Path (Join-Path $linuxLibs "dnglab.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $linuxLibs "exiftool.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $linuxLibs "libraw\simple_dcraw.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $linuxLibs "libraw\libraw.dll") -Force -ErrorAction SilentlyContinue

# Copy Linux dnglab binary
Copy-Item -Path "$repoRoot\build_assets\tools\dnglab_linux_x64" -Destination (Join-Path $linuxLibs "dnglab") -Force

# Create Linux exiftool launcher script
$exifToolScript = @"
#!/bin/sh
DIR="`$(cd "`$(dirname "`$0")" && pwd)"
if [ -f "`$DIR/exiftool_files/exiftool.pl" ]; then
    exec perl "`$DIR/exiftool_files/exiftool.pl" "`$@"
fi
exec exiftool "`$@"
"@
[IO.File]::WriteAllText((Join-Path $linuxLibs "exiftool"), $exifToolScript.Replace("`r`n", "`n"))

# Add Desktop file & icon
$desktopEntry = @"
[Desktop Entry]
Name=Fujify
Comment=Fujifilm Film Simulation Profiles for Any RAW Photo
Exec=Fujify
Icon=fujify
Terminal=false
Type=Application
Categories=Graphics;Photography;
"@
[IO.File]::WriteAllText((Join-Path $linuxDist "Fujify.desktop"), $desktopEntry.Replace("`r`n", "`n"))
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logos.png" -Destination (Join-Path $linuxDist "fujify.png") -Force

# Add run script
$runScript = @"
#!/bin/sh
DIR="`$(cd "`$(dirname "`$0")" && pwd)"
chmod +x "`$DIR/Fujify" "`$DIR/libs/dnglab" "`$DIR/libs/exiftool" 2>/dev/null
exec "`$DIR/Fujify" "`$@"
"@
[IO.File]::WriteAllText((Join-Path $linuxDist "run.sh"), $runScript.Replace("`r`n", "`n"))

# Archive Linux tar.gz
$linuxArchive = Join-Path $releaseDir "Fujify-v1.1-Linux-x64.tar.gz"
if (Test-Path $linuxArchive) { Remove-Item -Force $linuxArchive }
& tar -czf $linuxArchive -C $distDir "Fujify-v1.1-Linux-x64"
Write-Host " Created Linux Archive: $linuxArchive" -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Building Fujify for macOS (Apple Silicon)..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -r osx-arm64 --self-contained true -o "$publishDir\osx-arm64"

# 2. Package macOS Apple Silicon (.app bundle)
$macAppDir = Join-Path $distDir "Fujify.app"
if (Test-Path $macAppDir) { Remove-Item -Recurse -Force $macAppDir }

$contentsDir = Join-Path $macAppDir "Contents"
$macOsDir = Join-Path $contentsDir "MacOS"
$resourcesDir = Join-Path $contentsDir "Resources"

New-Item -ItemType Directory -Force -Path $macOsDir | Out-Null
New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null

# Copy published binaries into MacOS folder
Copy-Item -Path "$publishDir\osx-arm64\*" -Destination $macOsDir -Recurse -Force

# Setup Mac native binaries in libs
$macLibs = Join-Path $macOsDir "libs"
Remove-Item -Path (Join-Path $macLibs "dnglab.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $macLibs "exiftool.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $macLibs "libraw\simple_dcraw.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $macLibs "libraw\libraw.dll") -Force -ErrorAction SilentlyContinue

Copy-Item -Path "$repoRoot\build_assets\tools\mac\dnglab" -Destination (Join-Path $macLibs "dnglab") -Force
[IO.File]::WriteAllText((Join-Path $macLibs "exiftool"), $exifToolScript.Replace("`r`n", "`n"))

# Info.plist
$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Fujify</string>
    <key>CFBundleDisplayName</key>
    <string>Fujify</string>
    <key>CFBundleIdentifier</key>
    <string>dev.ipweb.fujify</string>
    <key>CFBundleVersion</key>
    <string>1.1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.1</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>Fujify</string>
    <key>CFBundleIconFile</key>
    <string>logo.ico</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
</dict>
</plist>
"@
[IO.File]::WriteAllText((Join-Path $contentsDir "Info.plist"), $infoPlist.Replace("`r`n", "`n"))
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logo.ico" -Destination (Join-Path $resourcesDir "logo.ico") -Force
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logos.png" -Destination (Join-Path $resourcesDir "logo.png") -Force

# Archive macOS Apple Silicon
$macArmArchive = Join-Path $releaseDir "Fujify-v1.1-macOS-AppleSilicon.zip"
if (Test-Path $macArmArchive) { Remove-Item -Force $macArmArchive }
& tar -a -cf $macArmArchive -C $distDir "Fujify.app"
Write-Host " Created macOS Apple Silicon Archive: $macArmArchive" -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Building Fujify for macOS (Intel)..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -r osx-x64 --self-contained true -o "$publishDir\osx-x64"

# 3. Package macOS Intel (.app bundle)
$macIntelAppDir = Join-Path $distDir "Fujify-Intel.app"
if (Test-Path $macIntelAppDir) { Remove-Item -Recurse -Force $macIntelAppDir }

$intelContentsDir = Join-Path $macIntelAppDir "Contents"
$intelMacOsDir = Join-Path $intelContentsDir "MacOS"
$intelResourcesDir = Join-Path $intelContentsDir "Resources"

New-Item -ItemType Directory -Force -Path $intelMacOsDir | Out-Null
New-Item -ItemType Directory -Force -Path $intelResourcesDir | Out-Null

Copy-Item -Path "$publishDir\osx-x64\*" -Destination $intelMacOsDir -Recurse -Force

$intelMacLibs = Join-Path $intelMacOsDir "libs"
Remove-Item -Path (Join-Path $intelMacLibs "dnglab.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $intelMacLibs "exiftool.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $intelMacLibs "libraw\simple_dcraw.exe") -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $intelMacLibs "libraw\libraw.dll") -Force -ErrorAction SilentlyContinue

# For Intel Mac, dnglab can be installed via brew or used via system PATH, or Rosetta 2
Copy-Item -Path "$repoRoot\build_assets\tools\mac\dnglab" -Destination (Join-Path $intelMacLibs "dnglab") -Force
[IO.File]::WriteAllText((Join-Path $intelMacLibs "exiftool"), $exifToolScript.Replace("`r`n", "`n"))

[IO.File]::WriteAllText((Join-Path $intelContentsDir "Info.plist"), $infoPlist.Replace("`r`n", "`n"))
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logo.ico" -Destination (Join-Path $intelResourcesDir "logo.ico") -Force
Copy-Item -Path "$repoRoot\Fujify\Assets\images\logos.png" -Destination (Join-Path $intelResourcesDir "logo.png") -Force

# Archive macOS Intel
$macIntelArchive = Join-Path $releaseDir "Fujify-v1.1-macOS-Intel.zip"
if (Test-Path $macIntelArchive) { Remove-Item -Force $macIntelArchive }
& tar -a -cf $macIntelArchive -C $distDir "Fujify-Intel.app"
Write-Host " Created macOS Intel Archive: $macIntelArchive" -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Green
Write-Host " Multiplatform build completed successfully!" -ForegroundColor Green
Write-Host " Release archives located in: $releaseDir" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
