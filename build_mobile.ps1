# build_mobile.ps1
# Automates building and packaging of Fujify for Android and iOS
param(
    [string]$Configuration = "Release",
    [switch]$SkipAndroid = $false,
    [switch]$SkipiOS = $false,
    [string]$KeystorePath = "",
    [string]$KeystorePass = $(if ($env:KEYSTORE_PASSWORD) { $env:KEYSTORE_PASSWORD } else { "" }),
    [string]$KeyAlias = "fujifier"
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
    
    # Detect release upload keystore if not explicitly provided
    if (-not $KeystorePath) {
        $candidateKeystores = @(
            (Join-Path $repoRoot "fujifier-upload.keystore"),
            "$env:USERPROFILE\.android\fujifier-upload.keystore"
        )
        foreach ($k in $candidateKeystores) {
            if (Test-Path $k) {
                $KeystorePath = $k
                Write-Host "Using release upload keystore: $k" -ForegroundColor Green
                break
            }
        }
    }

    if (-not $KeystorePass) {
        $candidateKeyNotes = @(
            (Join-Path $repoRoot "keyandroid.txt")
        )
        foreach ($kn in $candidateKeyNotes) {
            if (Test-Path $kn) {
                $keyText = Get-Content $kn -Raw
                if ($keyText -match "Password:\s*([^\r\n]+)") {
                    $KeystorePass = $matches[1].Trim()
                    Write-Host "Loaded keystore password from local configuration." -ForegroundColor Green
                    break
                }
            }
        }
    }

    $globalVersionCode = 6
    $architectures = @(
        @{ Rid = "android-arm64"; Arch = "arm64" },
        @{ Rid = "android-x64"; Arch = "x64" }
    )
    foreach ($target in $architectures) {
        $rid = $target.Rid
        $archSuffix = $target.Arch
        $versionCode = $globalVersionCode

        # 2a. Build APK (for local device testing & direct download)
        Write-Host "`nBuilding APK for $rid (versionCode $versionCode)..." -ForegroundColor Cyan
        try {
            & $dotnet build $androidProj -c $Configuration -r $rid -p:AndroidPackageFormat=apk -p:ApplicationVersion=$versionCode
            
            $signedApk = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*Signed.apk" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\$rid\*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (-not $signedApk) {
                $signedApk = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*.apk" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\$rid\*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            }

            if ($signedApk) {
                $targetRelease = Join-Path $releaseDir "Fujifier-v2.0.0-Android-$archSuffix.apk"
                $targetDist = Join-Path $distDir "Fujifier-v2.0.0-Android-$archSuffix.apk"
                Copy-Item -Path $signedApk.FullName -Destination $targetRelease -Force
                Copy-Item -Path $signedApk.FullName -Destination $targetDist -Force
                $sizeMb = [math]::Round($signedApk.Length / 1MB, 2)
                Write-Host "APK generated ($archSuffix): $targetRelease ($sizeMb MB)" -ForegroundColor Green
            }
        } catch {
            $err = $_.Exception.Message
            Write-Host "Error building APK for ${rid}: $err" -ForegroundColor Red
        }

        # 2b. Build Android App Bundle (.aab) for Google Play Store Release
        Write-Host "`nBuilding Android App Bundle (AAB) for $rid (versionCode $versionCode)..." -ForegroundColor Cyan
        try {
            & $dotnet build $androidProj -c $Configuration -r $rid -p:AndroidPackageFormat=aab -p:ApplicationVersion=$versionCode

            # Locate the unsigned or signed AAB
            $rawAab = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*.aab" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\$rid\*" -and $_.Name -notlike "*Signed*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (-not $rawAab) {
                $rawAab = Get-ChildItem -Path (Join-Path $repoRoot "Fujify.Android\bin\$Configuration") -Filter "*.aab" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\$rid\*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            }

            if ($rawAab) {
                $targetReleaseAab = Join-Path $releaseDir "Fujifier-v2.0.0-Android-$archSuffix.aab"
                $targetDistAab = Join-Path $distDir "Fujifier-v2.0.0-Android-$archSuffix.aab"
                Copy-Item -Path $rawAab.FullName -Destination $targetReleaseAab -Force

                # Sign AAB with release upload key if available
                if ($KeystorePath -and (Test-Path $KeystorePath)) {
                    Write-Host "Signing AAB with release upload key ($KeystorePath)..." -ForegroundColor Yellow
                    & jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 -keystore $KeystorePath -storepass $KeystorePass -tsa "http://timestamp.digicert.com" $targetReleaseAab $KeyAlias | Out-Null
                    Write-Host "[OK] AAB signed with release key '$KeyAlias'." -ForegroundColor Green
                } else {
                    Write-Warning "No release keystore found. AAB left with default/debug signature."
                }

                Copy-Item -Path $targetReleaseAab -Destination $targetDistAab -Force
                $sizeMb = [math]::Round((Get-Item $targetReleaseAab).Length / 1MB, 2)
                Write-Host "AAB bundle generated ($archSuffix): $targetReleaseAab ($sizeMb MB)" -ForegroundColor Green
            }
        } catch {
            $err = $_.Exception.Message
            Write-Host "Error building AAB for ${rid}: $err" -ForegroundColor Red
        }
    }

    # 2c. Assemble Universal Multi-Architecture AAB (both ARM64 & x86_64 in one bundle, versionCode 3)
    Write-Host "`nAssembling Universal Multi-Architecture Android App Bundle (AAB) with both ARM64 and x64 (versionCode $globalVersionCode)..." -ForegroundColor Cyan
    $arm64AabPath = Join-Path $releaseDir "Fujifier-v2.0.0-Android-arm64.aab"
    $x64AabPath = Join-Path $releaseDir "Fujifier-v2.0.0-Android-x64.aab"
    $universalAabPath = Join-Path $releaseDir "Fujifier-v2.0.0-Android.aab"
    $bundletoolJar = "C:\Program Files\dotnet\packs\Microsoft.Android.Sdk.Windows\36.1.2\tools\bundletool.jar"
    if (-not (Test-Path $bundletoolJar)) {
        $bundletoolJar = (Get-ChildItem -Path "C:\Program Files\dotnet\packs" -Filter "bundletool*.jar" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
    }

    if ((Test-Path $arm64AabPath) -and (Test-Path $x64AabPath) -and ($bundletoolJar -and (Test-Path $bundletoolJar))) {
        $tempCombine = Join-Path $repoRoot "obj\temp_bundle_combine"
        if (Test-Path $tempCombine) { Remove-Item -Path $tempCombine -Recurse -Force }
        New-Item -ItemType Directory -Path $tempCombine -Force | Out-Null

        Add-Type -AssemblyName System.IO.Compression.FileSystem

        # 1. Extract base from arm64 AAB
        [System.IO.Compression.ZipFile]::ExtractToDirectory($arm64AabPath, $tempCombine)
        $baseDir = Join-Path $tempCombine "base"

        # 2. Extract x86_64 native libraries from x64 AAB
        $x64Zip = [System.IO.Compression.ZipFile]::OpenRead($x64AabPath)
        $x86TargetDir = Join-Path $baseDir "lib\x86_64"
        New-Item -ItemType Directory -Path $x86TargetDir -Force | Out-Null
        foreach ($entry in $x64Zip.Entries) {
            if ($entry.FullName.StartsWith("base/lib/x86_64/")) {
                $subFile = $entry.FullName.Substring("base/lib/x86_64/".Length)
                if ($subFile) {
                    $destPath = Join-Path $x86TargetDir $subFile
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destPath, $true)
                }
            }
        }
        $x64Zip.Dispose()

        # 3. Create module zip for bundletool (ensure forward slashes for Linux/Java zip format)
        $baseZip = Join-Path $tempCombine "base.zip"
        if (Test-Path $baseZip) { Remove-Item -Force $baseZip }
        $zipStream = [System.IO.File]::Open($baseZip, [System.IO.FileMode]::Create)
        $zipArchive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
        $allFiles = Get-ChildItem -Path $baseDir -Recurse -File
        foreach ($file in $allFiles) {
            $relPath = $file.FullName.Substring($baseDir.Length).TrimStart('\', '/').Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipArchive, $file.FullName, $relPath) | Out-Null
        }
        $zipArchive.Dispose()
        $zipStream.Dispose()

        # 4. Run bundletool build-bundle
        & java -jar $bundletoolJar build-bundle --modules=$baseZip --output=$universalAabPath --overwrite | Out-Null

        # 5. Sign the universal AAB with release upload keystore
        if ($KeystorePath -and (Test-Path $KeystorePath)) {
            Write-Host "Signing Universal AAB with release upload key ($KeystorePath)..." -ForegroundColor Yellow
            & jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 -keystore $KeystorePath -storepass $KeystorePass -tsa "http://timestamp.digicert.com" $universalAabPath $KeyAlias | Out-Null
            Write-Host "[OK] Universal AAB signed with release key '$KeyAlias'." -ForegroundColor Green
        }

        # 6. Copy to dist
        Copy-Item -Path $universalAabPath -Destination (Join-Path $distDir "Fujifier-v2.0.0-Android.aab") -Force
        $uniSizeMb = [math]::Round((Get-Item $universalAabPath).Length / 1MB, 2)
        Write-Host "Universal Multi-Architecture AAB generated: $universalAabPath ($uniSizeMb MB)" -ForegroundColor Green

        # Cleanup
        Remove-Item -Path $tempCombine -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "`n[2/4] Skipping Android build (-SkipAndroid specified)." -ForegroundColor DarkGray
}

# 3. Build iOS App & Package IPA
if (-not $SkipiOS) {
    Write-Host "`n[3/4] Building Fujifier for iOS & Packaging IPA ($Configuration)..." -ForegroundColor Yellow
    $iosProj = Join-Path $repoRoot "Fujify.iOS\Fujify.iOS.csproj"
    
    try {
        & $dotnet build $iosProj -c $Configuration -r ios-arm64
        Write-Host "[OK] iOS project code compiled cleanly for ios-arm64." -ForegroundColor Green

        # Package IPA payload
        $ipaStaging = Join-Path $distDir "ipa_staging"
        if (Test-Path $ipaStaging) { Remove-Item -Recurse -Force $ipaStaging }
        $appDir = Join-Path $ipaStaging "Payload\Fujifier.app"
        New-Item -ItemType Directory -Force -Path $appDir | Out-Null

        $srcBin = Join-Path $repoRoot "Fujify.iOS\bin\$Configuration\net10.0-ios\ios-arm64"
        if (-not (Test-Path $srcBin)) {
            $srcBin = (Get-ChildItem -Path (Join-Path $repoRoot "Fujify.iOS\bin\$Configuration") -Filter "ios-arm64" -Directory -Recurse | Select-Object -First 1).FullName
        }
        Copy-Item -Path "$srcBin\*" -Destination $appDir -Recurse -Force

        # Ensure Info.plist & AppIcon are included
        Copy-Item -Path (Join-Path $repoRoot "Fujify.iOS\Info.plist") -Destination (Join-Path $appDir "Info.plist") -Force
        $iconSrc = Join-Path $repoRoot "Fujify.iOS\Resources\AppIcon.png"
        if (Test-Path $iconSrc) {
            Copy-Item -Path $iconSrc -Destination (Join-Path $appDir "AppIcon.png") -Force
        }

        # Remove all static library archives (.a) which trigger Sideloadly signing errors
        Get-ChildItem -Path $appDir -Filter "*.a" -Recurse | Remove-Item -Force

        $machoBinary = Join-Path $appDir "Fujify.iOS"
        if (-not (Test-Path $machoBinary)) {
            Write-Warning "Native Mach-O executable 'Fujify.iOS' is not present in the app bundle. Windows builds MSIL assemblies only and requires a Mac build host (or GitHub Actions macos runner) to produce an installable iOS IPA."
        }

        $targetIpa = Join-Path $releaseDir "Fujifier-v2.0.0-iOS.ipa"
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
