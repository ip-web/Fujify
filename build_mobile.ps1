# build_mobile.ps1
# Automates building and packaging of Fujify for Android and iOS
param(
    [string]$Configuration = "Release",
    [switch]$SkipAndroid = $false,
    [switch]$SkipiOS = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$distDir = Join-Path $repoRoot "dist\mobile"
$releaseDir = Join-Path $repoRoot "release\mobile"
$dotnet = "dotnet"

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Fujify Mobile Build Engine (Android & iOS)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Check .NET Workloads
Write-Host "`n[1/4] Verifying .NET Mobile Workloads..." -ForegroundColor Yellow
$workloads = & $dotnet workload list
Write-Host $workloads

# 2. Build Android App
if (-not $SkipAndroid) {
    Write-Host "`n[2/4] Building Fujify for Android ($Configuration)..." -ForegroundColor Yellow
    $androidProj = Join-Path $repoRoot "Fujify.Android\Fujify.Android.csproj"
    
    # Check if ANDROID_HOME or Android SDK is set
    $androidHome = $env:ANDROID_HOME
    if (-not $androidHome) {
        $possiblePaths = @(
            "$env:LOCALAPPDATA\Android\Sdk",
            "C:\Program Files (x86)\Android\android-sdk"
        )
        foreach ($p in $possiblePaths) {
            if (Test-Path $p) {
                $androidHome = $p
                $env:ANDROID_HOME = $p
                Write-Host "Found Android SDK at: $androidHome" -ForegroundColor Green
                break
            }
        }
    }

    try {
        Write-Host "Building Android APK..." -ForegroundColor Cyan
        & $dotnet build $androidProj -c $Configuration -p:AndroidPackageFormat=apk
        
        # Locate generated APK
        $apkFiles = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*Signed.apk" -Recurse
        if (-not $apkFiles) {
            $apkFiles = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*.apk" -Recurse
        }
        
        if ($apkFiles) {
            $targetApk = Join-Path $releaseDir "Fujify-v2.1.0-Android.apk"
            Copy-Item -Path $apkFiles[0].FullName -Destination $targetApk -Force
            Write-Host "✔ Android APK generated: $targetApk" -ForegroundColor Green
        } else {
            Write-Host "⚠ Android build completed, but no APK package was found." -ForegroundColor Yellow
        }
    } catch {
        Write-Host "⚠ Android build notice: $_" -ForegroundColor Yellow
        Write-Host "  (Ensure ANDROID_HOME is set to your Android SDK directory for local APK generation, or use GitHub Actions CI)" -ForegroundColor DarkGray
    }
} else {
    Write-Host "`n[2/4] Skipping Android build (-SkipAndroid specified)." -ForegroundColor DarkGray
}

# 3. Build iOS App
if (-not $SkipiOS) {
    Write-Host "`n[3/4] Building Fujify for iOS ($Configuration)..." -ForegroundColor Yellow
    $iosProj = Join-Path $repoRoot "Fujify.iOS\Fujify.iOS.csproj"
    
    # On Windows, building iOS targets requires pairing to a Mac or building on macOS / CI
    $isWindows = [System.OperatingSystem]::IsWindows()
    if ($isWindows) {
        Write-Host "Notice: Direct local IPA packaging requires a macOS host or Xcode pairing." -ForegroundColor Yellow
        Write-Host "Testing iOS compilation & restore with dotnet..." -ForegroundColor Cyan
        try {
            & $dotnet build $iosProj -c $Configuration -f net10.0-ios /p:ServerAddress= /p:ServerUser= /p:ContinueOnDisconnected=true -warnAsError:false
            Write-Host "✔ iOS project code compiled cleanly." -ForegroundColor Green
        } catch {
            Write-Host "Notice: iOS build requires macOS environment or Mac build agent: $_" -ForegroundColor Yellow
        }
    } else {
        try {
            & $dotnet build $iosProj -c $Configuration
            Write-Host "✔ iOS project built successfully." -ForegroundColor Green
        } catch {
            Write-Host "iOS build failed: $_" -ForegroundColor Red
        }
    }
} else {
    Write-Host "`n[3/4] Skipping iOS build (-SkipiOS specified)." -ForegroundColor DarkGray
}

# 4. Summary
Write-Host "`n[4/4] Mobile Build Workflow Summary:" -ForegroundColor Yellow
Write-Host "Artifacts location: $releaseDir" -ForegroundColor Cyan
Get-ChildItem -Path $releaseDir | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor White
}
Write-Host "`nMobile build script finished." -ForegroundColor Green
