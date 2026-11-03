# scripts/build_universal_bundle.ps1
# Combines ARM64 and x86_64 AABs into a single universal multi-architecture AAB for Google Play Console

param(
    [string]$Arm64AabPath,
    [string]$X64AabPath,
    [string]$OutputAabPath,
    [string]$KeystorePath,
    [string]$KeystorePass = $(if ($env:KEYSTORE_PASSWORD) { $env:KEYSTORE_PASSWORD } else { "" }),
    [string]$KeyAlias = "fujifier"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Arm64AabPath)) {
    throw "ARM64 AAB not found at $Arm64AabPath"
}
if (-not (Test-Path $X64AabPath)) {
    throw "x64 AAB not found at $X64AabPath"
}

$bundletoolJar = "C:\Program Files\dotnet\packs\Microsoft.Android.Sdk.Windows\36.1.2\tools\bundletool.jar"
if (-not (Test-Path $bundletoolJar)) {
    $bundletoolJar = (Get-ChildItem -Path "C:\Program Files\dotnet\packs" -Filter "bundletool*.jar" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}
if (-not (Test-Path $bundletoolJar)) {
    throw "bundletool.jar not found in .NET Android SDK packs."
}

$tempCombine = Join-Path ([System.IO.Path]::GetTempPath()) "fujifier_bundle_combine_$([System.Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempCombine -Force | Out-Null

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    Write-Host "Extracting base module from ARM64 AAB..." -ForegroundColor Cyan
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Arm64AabPath, $tempCombine)
    $baseDir = Join-Path $tempCombine "base"

    Write-Host "Extracting x86_64 native libraries from x64 AAB..." -ForegroundColor Cyan
    $x64Zip = [System.IO.Compression.ZipFile]::OpenRead($X64AabPath)
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

    Write-Host "Packaging base.zip with forward-slash entry names..." -ForegroundColor Cyan
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

    Write-Host "Running bundletool build-bundle..." -ForegroundColor Cyan
    $outDir = [System.IO.Path]::GetDirectoryName($OutputAabPath)
    if ($outDir -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }

    & java -jar $bundletoolJar build-bundle --modules=$baseZip --output=$OutputAabPath --overwrite
    if ($LASTEXITCODE -ne 0) {
        throw "bundletool build-bundle failed with exit code $LASTEXITCODE"
    }

    Write-Host "Validating bundle with bundletool..." -ForegroundColor Cyan
    & java -jar $bundletoolJar validate --bundle=$OutputAabPath
    if ($LASTEXITCODE -ne 0) {
        throw "bundletool validate failed with exit code $LASTEXITCODE"
    }

    if ($KeystorePath -and (Test-Path $KeystorePath)) {
        Write-Host "Signing Universal AAB with release upload key ($KeystorePath)..." -ForegroundColor Yellow
        & jarsigner -verbose -sigalg SHA256withRSA -digestalg SHA-256 -keystore $KeystorePath -storepass $KeystorePass $OutputAabPath $KeyAlias | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "jarsigner failed to sign Universal AAB"
        }
        Write-Host "[OK] Universal AAB signed with release key '$KeyAlias'." -ForegroundColor Green
    } else {
        Write-Warning "Keystore not found. Universal AAB left unsigned."
    }

    $sizeMb = [math]::Round((Get-Item $OutputAabPath).Length / 1MB, 2)
    Write-Host "Universal Multi-Architecture AAB created: $OutputAabPath ($sizeMb MB)" -ForegroundColor Green
}
finally {
    if (Test-Path $tempCombine) {
        Remove-Item -Path $tempCombine -Recurse -Force -ErrorAction SilentlyContinue
    }
}

