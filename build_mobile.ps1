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

# Detect Android NDK
if (-not $env:ANDROID_NDK_HOME) {
    $possibleNdk = (Get-ChildItem "$env:LOCALAPPDATA\Android\Sdk\ndk\*" -ErrorAction SilentlyContinue | Select-Object -Last 1).FullName
    if ($possibleNdk -and (Test-Path $possibleNdk)) {
        $env:ANDROID_NDK_HOME = $possibleNdk
        $env:NDK_HOME = $possibleNdk
        Write-Host "Detected ANDROID_NDK_HOME: $possibleNdk" -ForegroundColor Green
    }
}

if ($env:JAVA_HOME -and $env:ANDROID_HOME) {
    $env:PATH = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\cmdline-tools\latest\bin;$env:ANDROID_HOME\platform-tools;$env:PATH"
}

# 2. Build Android App (ARM64 & x86_64)
if (-not $SkipAndroid) {
    Write-Host "`n[2/4] Building Fujify for Android ($Configuration)..." -ForegroundColor Yellow
    $androidProj = Join-Path $repoRoot "Fujify.Android\Fujify.Android.csproj"
    
    # Ensure native dnglab_ffi libraries are present
    $arm64Lib = Join-Path $repoRoot "Fujify.Android\libs\arm64-v8a\libdnglab_ffi.so"
    $x64Lib = Join-Path $repoRoot "Fujify.Android\libs\x86_64\libdnglab_ffi.so"
    if (-not (Test-Path $arm64Lib) -or -not (Test-Path $x64Lib)) {
        Write-Host "Building dnglab_ffi native libraries for Android (arm64-v8a and x86_64)..." -ForegroundColor Cyan
        $nativeDir = Join-Path $repoRoot "native\dnglab_ffi"
        $outputLibs = Join-Path $repoRoot "Fujify.Android\libs"
        & cargo ndk -t arm64-v8a -t x86_64 -P 26 -o $outputLibs --manifest-path (Join-Path $nativeDir "Cargo.toml") build --release
    }
    
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

# 3. Build iOS App & Package IPA
if (-not $SkipiOS) {
    Write-Host "`n[3/4] Building Fujify for iOS & Packaging IPA ($Configuration)..." -ForegroundColor Yellow
    $iosProj = Join-Path $repoRoot "Fujify.iOS\Fujify.iOS.csproj"
    
    try {
        & $dotnet build $iosProj -c $Configuration -r ios-arm64
        Write-Host "[OK] iOS project code compiled cleanly for ios-arm64." -ForegroundColor Green

        # Package IPA payload
        $ipaStaging = Join-Path $distDir "ipa_staging"
        if (Test-Path $ipaStaging) { Remove-Item -Recurse -Force $ipaStaging }
        $appDir = Join-Path $ipaStaging "Payload\Fujify.app"
        New-Item -ItemType Directory -Force -Path $appDir | Out-Null

        $srcBin = Join-Path $repoRoot "Fujify.iOS\bin\$Configuration\net10.0-ios\ios-arm64"
        Copy-Item -Path "$srcBin\*" -Destination $appDir -Recurse -Force

        # Ensure Info.plist & AppIcon are included
        Copy-Item -Path (Join-Path $repoRoot "Fujify.iOS\Info.plist") -Destination (Join-Path $appDir "Info.plist") -Force
        $iconSrc = Join-Path $repoRoot "Fujify.iOS\Resources\AppIcon.png"
        if (Test-Path $iconSrc) {
            Copy-Item -Path $iconSrc -Destination (Join-Path $appDir "AppIcon.png") -Force
        }

        $targetIpa = Join-Path $releaseDir "Fujify-v2.1.0-iOS.ipa"
        if (Test-Path $targetIpa) { Remove-Item -Force $targetIpa }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($ipaStaging, $targetIpa, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        
        $ipaSizeMb = [math]::Round((Get-Item $targetIpa).Length / 1MB, 2)
        Write-Host "[OK] IPA package created: $targetIpa ($ipaSizeMb MB)" -ForegroundColor Green
    } catch {
        Write-Host "iOS build/packaging error: $_" -ForegroundColor Red
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
