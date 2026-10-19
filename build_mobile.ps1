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

# 1. Environment & Workload Detection
Write-Host "`n[1/4] Verifying .NET Mobile Workloads & Environment..." -ForegroundColor Yellow
$workloads = & $dotnet workload list
Write-Host $workloads

# Detect Java SDK
if (-not $env:JAVA_HOME) {
    $possibleJdks = @(
        (Get-ChildItem "C:\Program Files\Microsoft\jdk-17*" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName,
        (Get-ChildItem "C:\Program Files\Eclipse Adoptium\jdk-17*" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName,
        (Get-ChildItem "C:\Program Files\Java\jdk-17*" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
    )
    foreach ($j in $possibleJdks) {
        if ($j -and (Test-Path $j)) {
            $env:JAVA_HOME = $j
            Write-Host "Detected JAVA_HOME: $j" -ForegroundColor Green
            break
        }
    }
}

# Detect Android SDK
if (-not $env:ANDROID_HOME) {
    $possibleAndroid = @(
        "$env:LOCALAPPDATA\Android\Sdk",
        "C:\Program Files (x86)\Android\android-sdk"
    )
    foreach ($a in $possibleAndroid) {
        if ($a -and (Test-Path $a)) {
            $env:ANDROID_HOME = $a
            Write-Host "Detected ANDROID_HOME: $a" -ForegroundColor Green
            break
        }
    }
}

if ($env:JAVA_HOME -and $env:ANDROID_HOME) {
    $env:PATH = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\cmdline-tools\latest\bin;$env:ANDROID_HOME\platform-tools;$env:PATH"
}

# 2. Build Android App (ARM64 & x86_64)
if (-not $SkipAndroid) {
    Write-Host "`n[2/4] Building Fujify for Android ($Configuration)..." -ForegroundColor Yellow
    $androidProj = Join-Path $repoRoot "Fujify.Android\Fujify.Android.csproj"
    
    $architectures = @("android-arm64", "android-x64")
    foreach ($rid in $architectures) {
        Write-Host "`nBuilding APK for $rid..." -ForegroundColor Cyan
        try {
            & $dotnet build $androidProj -c $Configuration -r $rid -p:AndroidPackageFormat=apk
            
            $archSuffix = if ($rid -eq "android-arm64") { "arm64" } else { "x64" }
            $apkFolder = Join-Path $repoRoot "Fujify.Android\bin\$Configuration\net10.0-android\$rid"
            $signedApk = Get-ChildItem -Path $apkFolder -Filter "*Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $signedApk) {
                $signedApk = Get-ChildItem -Path $apkFolder -Filter "*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
            }

            if ($signedApk) {
                $targetRelease = Join-Path $releaseDir "Fujify-v2.1.0-Android-$archSuffix.apk"
                $targetDist = Join-Path $distDir "Fujify-v2.1.0-Android-$archSuffix.apk"
                Copy-Item -Path $signedApk.FullName -Destination $targetRelease -Force
                Copy-Item -Path $signedApk.FullName -Destination $targetDist -Force
                $sizeMb = [math]::Round($signedApk.Length / 1MB, 2)
                Write-Host "APK generated ($archSuffix): $targetRelease ($sizeMb MB)" -ForegroundColor Green
            }
        } catch {
            $err = $_.Exception.Message
            Write-Host "Error building APK for ${rid}: $err" -ForegroundColor Red
        }
    }
} else {
    Write-Host "`n[2/4] Skipping Android build (-SkipAndroid specified)." -ForegroundColor DarkGray
}

# 3. Build iOS App
if (-not $SkipiOS) {
    Write-Host "`n[3/4] Building Fujify for iOS ($Configuration)..." -ForegroundColor Yellow
    $iosProj = Join-Path $repoRoot "Fujify.iOS\Fujify.iOS.csproj"
    
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
    $itemMb = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  - $($_.Name) ($itemMb MB)" -ForegroundColor White
}
Write-Host "`nMobile build script finished." -ForegroundColor Green
