# build_windows.ps1
# Automates building, publishing, and packaging Fujifier installers and portable ZIPs for Windows (x64 and ARM64)

param (
    [ValidateSet("All", "x64", "arm64")]
    [string]$Architecture = "All",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$releaseDir = Join-Path $repoRoot "release"
$distDir = Join-Path $repoRoot "dist"
$dotnet = "dotnet"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Locate Inno Setup Compiler (ISCC)
$isccPaths = @(
    "C:\Users\isisk\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Users\isisk\AppData\Local\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmdIscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmdIscc) {
        $iscc = $cmdIscc.Source
    }
}

if ($iscc) {
    Write-Host "Found Inno Setup Compiler: $iscc" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not found. Installers (.exe) will be skipped, only portable ZIPs will be created."
}

# ==============================================================================
# 1. Build Windows x64
# ==============================================================================
if ($Architecture -eq "All" -or $Architecture -eq "x64") {
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host " Building Fujifier for Windows x64 (win-x64)..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    $publishDirX64 = Join-Path $repoRoot "publish\win-x64"
    $distDirX64 = Join-Path $distDir "Fujifier-v2.0.0-Windows-x64"

    New-Item -ItemType Directory -Force -Path $publishDirX64 | Out-Null

    Write-Host "Publishing .NET application for win-x64..." -ForegroundColor Cyan
    & $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c $Configuration -f net10.0 -r win-x64 --self-contained true -o $publishDirX64

    # Clean non-Windows libs
    $pubLibsX64 = Join-Path $publishDirX64 "libs"
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsX64 "linux-x64") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsX64 "osx-arm64") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsX64 "win-arm64") -ErrorAction SilentlyContinue

    # Ensure dnglab_ffi.dll is placed in root and libs
    $nativeDllX64 = Join-Path $repoRoot "Fujify\libs\win-x64\dnglab_ffi.dll"
    if (Test-Path $nativeDllX64) {
        Copy-Item $nativeDllX64 (Join-Path $publishDirX64 "dnglab_ffi.dll") -Force
        Copy-Item $nativeDllX64 (Join-Path $pubLibsX64 "dnglab_ffi.dll") -Force
    }

    # Create Portable ZIP
    Write-Host "Creating Portable ZIP package for win-x64..." -ForegroundColor Cyan
    $portableZipX64 = Join-Path $releaseDir "Fujifier-v2.0.0-Windows-x64-Portable.zip"
    if (Test-Path $portableZipX64) { Remove-Item -Force $portableZipX64 }
    if (Test-Path $distDirX64) { Remove-Item -Recurse -Force $distDirX64 }

    New-Item -ItemType Directory -Force -Path $distDirX64 | Out-Null
    Copy-Item -Path "$publishDirX64\*" -Destination $distDirX64 -Recurse -Force
    & tar.exe -a -cf $portableZipX64 -C $distDir "Fujifier-v2.0.0-Windows-x64"
    Write-Host "-> Portable ZIP created: $portableZipX64" -ForegroundColor Green

    # Compile Inno Setup Installer
    if ($iscc) {
        Write-Host "Compiling Inno Setup x64 installer..." -ForegroundColor Cyan
        & $iscc `
            /DMyAppSourceDir="publish\win-x64" `
            /DMyOutputBaseFilename="Fujifier-v2.0.0-Setup" `
            /DMyArchAllowed="x64compatible" `
            "$repoRoot\installer.iss"
        Write-Host "-> Installer created: $(Join-Path $releaseDir 'Fujifier-v2.0.0-Setup.exe')" -ForegroundColor Green
    }
}

# ==============================================================================
# 2. Build Windows ARM64
# ==============================================================================
if ($Architecture -eq "All" -or $Architecture -eq "arm64") {
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host " Building Fujifier for Windows on ARM (win-arm64)..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan

    $publishDirArm64 = Join-Path $repoRoot "publish\win-arm64"
    $distDirArm64 = Join-Path $distDir "Fujifier-v2.0.0-Windows-ARM64"

    New-Item -ItemType Directory -Force -Path $publishDirArm64 | Out-Null

    Write-Host "Publishing .NET application for win-arm64..." -ForegroundColor Cyan
    & $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c $Configuration -f net10.0 -r win-arm64 --self-contained true -o $publishDirArm64

    # Clean non-Windows libs
    $pubLibsArm64 = Join-Path $publishDirArm64 "libs"
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsArm64 "linux-x64") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsArm64 "osx-arm64") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force -Path (Join-Path $pubLibsArm64 "win-x64") -ErrorAction SilentlyContinue

    # Ensure dnglab_ffi.dll and libunwind.dll are placed in root and libs
    $nativeDllArm64 = Join-Path $repoRoot "Fujify\libs\win-arm64\dnglab_ffi.dll"
    if (Test-Path $nativeDllArm64) {
        Copy-Item $nativeDllArm64 (Join-Path $publishDirArm64 "dnglab_ffi.dll") -Force
        Copy-Item $nativeDllArm64 (Join-Path $pubLibsArm64 "dnglab_ffi.dll") -Force
    }
    $unwindDllArm64 = Join-Path $repoRoot "Fujify\libs\win-arm64\libunwind.dll"
    if (Test-Path $unwindDllArm64) {
        Copy-Item $unwindDllArm64 (Join-Path $publishDirArm64 "libunwind.dll") -Force
        Copy-Item $unwindDllArm64 (Join-Path $pubLibsArm64 "libunwind.dll") -Force
    }

    # Create Portable ZIP
    Write-Host "Creating Portable ZIP package for win-arm64..." -ForegroundColor Cyan
    $portableZipArm64 = Join-Path $releaseDir "Fujifier-v2.0.0-Windows-ARM64-Portable.zip"
    if (Test-Path $portableZipArm64) { Remove-Item -Force $portableZipArm64 }
    if (Test-Path $distDirArm64) { Remove-Item -Recurse -Force $distDirArm64 }

    New-Item -ItemType Directory -Force -Path $distDirArm64 | Out-Null
    Copy-Item -Path "$publishDirArm64\*" -Destination $distDirArm64 -Recurse -Force
    & tar.exe -a -cf $portableZipArm64 -C $distDir "Fujifier-v2.0.0-Windows-ARM64"
    Write-Host "-> Portable ZIP created: $portableZipArm64" -ForegroundColor Green

    # Compile Inno Setup Installer
    if ($iscc) {
        Write-Host "Compiling Inno Setup ARM64 installer..." -ForegroundColor Cyan
        & $iscc `
            /DMyAppSourceDir="publish\win-arm64" `
            /DMyOutputBaseFilename="Fujifier-v2.0.0-Setup-ARM64" `
            /DMyArchAllowed="arm64" `
            "$repoRoot\installer.iss"
        Write-Host "-> Installer created: $(Join-Path $releaseDir 'Fujifier-v2.0.0-Setup-ARM64.exe')" -ForegroundColor Green
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Windows build and packaging completed successfully!" -ForegroundColor Green
Write-Host " Installers and archives located in: $releaseDir" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

