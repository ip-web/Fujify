# build_windows_arm64.ps1
# Automates building, publishing, and packaging Fujifier for Windows on ARM (ARM64)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish\win-arm64"
$distDir = Join-Path $repoRoot "dist\Fujifier-v2.1.0-Windows-ARM64"
$releaseDir = Join-Path $repoRoot "release"
$dotnet = "dotnet"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Fujifier for Windows on ARM (win-arm64)..." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# 1. Compile Native dnglab_ffi for ARM64 if needed
$llvmMingwPath = Join-Path $env:USERPROFILE ".llvm-mingw\bin"
if (Test-Path (Join-Path $llvmMingwPath "aarch64-w64-mingw32-clang.exe")) {
    $env:PATH = "$llvmMingwPath;" + $env:PATH
}

$armDllSource = Join-Path $repoRoot "native\dnglab_ffi\target\aarch64-pc-windows-gnullvm\release\dnglab_ffi.dll"
if (-not (Test-Path $armDllSource)) {
    Write-Host "Compiling native dnglab_ffi for aarch64-pc-windows-gnullvm..." -ForegroundColor Yellow
    Push-Location (Join-Path $repoRoot "native\dnglab_ffi")
    cargo build --release --target aarch64-pc-windows-gnullvm
    Pop-Location
}

# Ensure libs\win-arm64 has up-to-date ARM64 binaries
$libsArm64 = Join-Path $repoRoot "Fujify\libs\win-arm64"
New-Item -ItemType Directory -Force -Path $libsArm64 | Out-Null
if (Test-Path $armDllSource) {
    Copy-Item $armDllSource (Join-Path $libsArm64 "dnglab_ffi.dll") -Force
}
$unwindSource = Join-Path $env:USERPROFILE ".llvm-mingw\aarch64-w64-mingw32\bin\libunwind.dll"
if (Test-Path $unwindSource) {
    Copy-Item $unwindSource (Join-Path $libsArm64 "libunwind.dll") -Force
}

# 2. Publish .NET 10 ARM64 App
Write-Host "Publishing .NET application for win-arm64..." -ForegroundColor Cyan
& $dotnet publish "$repoRoot\Fujify\Fujify.csproj" -c Release -f net10.0 -r win-arm64 --self-contained true -o $publishDir

# 3. Clean up non-Windows native libs from publish output
$pubLibs = Join-Path $publishDir "libs"
Remove-Item -Recurse -Force -Path (Join-Path $pubLibs "linux-x64") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force -Path (Join-Path $pubLibs "osx-arm64") -ErrorAction SilentlyContinue

# Ensure native ARM64 DLLs are present in both root and libs
if (Test-Path $armDllSource) {
    Copy-Item $armDllSource (Join-Path $publishDir "dnglab_ffi.dll") -Force
    Copy-Item $armDllSource (Join-Path $pubLibs "dnglab_ffi.dll") -Force
}
if (Test-Path $unwindSource) {
    Copy-Item $unwindSource (Join-Path $publishDir "libunwind.dll") -Force
    Copy-Item $unwindSource (Join-Path $pubLibs "libunwind.dll") -Force
}

# 4. Create Standalone Portable .zip
Write-Host "Creating Portable ZIP package..." -ForegroundColor Cyan
$portableZip = Join-Path $releaseDir "Fujifier-v2.1.0-Windows-ARM64-Portable.zip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item -Path "$publishDir\*" -Destination $distDir -Recurse -Force
& tar.exe -a -cf $portableZip -C (Join-Path $repoRoot "dist") "Fujifier-v2.1.0-Windows-ARM64"
Write-Host "-> Portable ZIP created: $portableZip" -ForegroundColor Green

# 5. Compile Inno Setup Installer
$isccPaths = @(
    "C:\Users\isisk\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Users\isisk\AppData\Local\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "Compiling Inno Setup ARM64 installer using $iscc..." -ForegroundColor Cyan
    & $iscc `
        /DMyAppSourceDir="publish\win-arm64" `
        /DMyOutputBaseFilename="Fujifier-v2.1.0-Setup-ARM64" `
        /DMyArchAllowed="arm64" `
        "$repoRoot\installer.iss"
    Write-Host "-> Installer created: $(Join-Path $releaseDir 'Fujifier-v2.1.0-Setup-ARM64.exe')" -ForegroundColor Green
} else {
    Write-Warning "ISCC.exe (Inno Setup) not found. Skipped installer build."
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Windows ARM64 build and packaging complete!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
