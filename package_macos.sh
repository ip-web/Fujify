#!/bin/bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$REPO_ROOT/publish"
DIST_DIR="$REPO_ROOT/dist"
RELEASE_DIR="$REPO_ROOT/release"

mkdir -p "$DIST_DIR" "$RELEASE_DIR" "$PUBLISH_DIR"

# 1. Build native dnglab_ffi for both aarch64 and x86_64
echo "=== Building native dnglab_ffi dylib for macOS ==="
cd "$REPO_ROOT/native/dnglab_ffi"
rustup target add aarch64-apple-darwin x86_64-apple-darwin || true

echo "Building dnglab_ffi for aarch64-apple-darwin (Apple Silicon)..."
cargo build --release --target aarch64-apple-darwin

echo "Building dnglab_ffi for x86_64-apple-darwin (Intel)..."
cargo build --release --target x86_64-apple-darwin

UNIVERSAL_DIR="$REPO_ROOT/dist/native-macos"
mkdir -p "$UNIVERSAL_DIR"

lipo -create -output "$UNIVERSAL_DIR/libdnglab_ffi.dylib" \
  target/aarch64-apple-darwin/release/libdnglab_ffi.dylib \
  target/x86_64-apple-darwin/release/libdnglab_ffi.dylib

echo "Universal dnglab_ffi dylib created successfully:"
file "$UNIVERSAL_DIR/libdnglab_ffi.dylib"

cd "$REPO_ROOT"

# Copy universal dylib to project libs so dotnet publish includes it
mkdir -p "$REPO_ROOT/Fujify/libs"
cp "$UNIVERSAL_DIR/libdnglab_ffi.dylib" "$REPO_ROOT/Fujify/libs/libdnglab_ffi.dylib"

# 2. Publish .NET projects
echo "=== Publishing .NET for osx-arm64 ==="
dotnet publish Fujify/Fujify.csproj -c Release -f net10.0 -r osx-arm64 --self-contained true -o "$PUBLISH_DIR/osx-arm64"

echo "=== Publishing .NET for osx-x64 ==="
dotnet publish Fujify/Fujify.csproj -c Release -f net10.0 -r osx-x64 --self-contained true -o "$PUBLISH_DIR/osx-x64"

# 3. Helper function to create .app bundle
package_app() {
    local APP_NAME="$1"
    local RID="$2"
    local ARCH_NAME="$3"
    local APP_DIR="$DIST_DIR/$APP_NAME.app"

    echo "=== Packaging $APP_NAME.app ($ARCH_NAME) ==="
    rm -rf "$APP_DIR"
    mkdir -p "$APP_DIR/Contents/MacOS/libs"
    mkdir -p "$APP_DIR/Contents/Resources"
    mkdir -p "$APP_DIR/Contents/Frameworks"

    # Copy published files
    cp -R "$PUBLISH_DIR/$RID/"* "$APP_DIR/Contents/MacOS/"

    # Copy native dylib to standard probing locations
    cp "$UNIVERSAL_DIR/libdnglab_ffi.dylib" "$APP_DIR/Contents/MacOS/libdnglab_ffi.dylib"
    cp "$UNIVERSAL_DIR/libdnglab_ffi.dylib" "$APP_DIR/Contents/MacOS/libs/libdnglab_ffi.dylib"
    cp "$UNIVERSAL_DIR/libdnglab_ffi.dylib" "$APP_DIR/Contents/Frameworks/libdnglab_ffi.dylib"

    # Cleanup Windows DLLs and Linux files
    rm -f "$APP_DIR/Contents/MacOS/libs/dnglab_ffi.dll"
    rm -rf "$APP_DIR/Contents/MacOS/libs/linux-x64"

    # dnglab CLI fallback if present
    if [ "$RID" = "osx-arm64" ]; then
        if [ -f "$REPO_ROOT/Fujify/libs/osx-arm64/dnglab" ]; then
            cp "$REPO_ROOT/Fujify/libs/osx-arm64/dnglab" "$APP_DIR/Contents/MacOS/libs/dnglab"
            cp "$REPO_ROOT/Fujify/libs/osx-arm64/dnglab" "$APP_DIR/Contents/MacOS/dnglab"
            chmod +x "$APP_DIR/Contents/MacOS/libs/dnglab" "$APP_DIR/Contents/MacOS/dnglab"
        fi
    fi

    # Create exiftool launcher script
    cat << 'EOF' > "$APP_DIR/Contents/MacOS/libs/exiftool"
#!/bin/sh
if command -v exiftool >/dev/null 2>&1; then
    exec exiftool "$@"
fi
if [ -x /opt/homebrew/bin/exiftool ]; then
    exec /opt/homebrew/bin/exiftool "$@"
fi
if [ -x /usr/local/bin/exiftool ]; then
    exec /usr/local/bin/exiftool "$@"
fi
if [ -x /usr/bin/exiftool ]; then
    exec /usr/bin/exiftool "$@"
fi
if [ -x /opt/local/bin/exiftool ]; then
    exec /opt/local/bin/exiftool "$@"
fi
exec exiftool "$@"
EOF
    chmod +x "$APP_DIR/Contents/MacOS/libs/exiftool"
    cp "$APP_DIR/Contents/MacOS/libs/exiftool" "$APP_DIR/Contents/MacOS/exiftool"

    # Info.plist
    cat << 'EOF' > "$APP_DIR/Contents/Info.plist"
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
EOF

    # Copy Icons
    cp "$REPO_ROOT/Fujify/Assets/images/logo.ico" "$APP_DIR/Contents/Resources/logo.ico"
    cp "$REPO_ROOT/Fujify/Assets/images/logos.png" "$APP_DIR/Contents/Resources/logo.png"

    # Set execute bits
    chmod +x "$APP_DIR/Contents/MacOS/Fujifier" || true
    chmod +x "$APP_DIR/Contents/MacOS/libs/exiftool" || true
    chmod +x "$APP_DIR/Contents/MacOS/exiftool" || true

    # Ad-hoc code sign
    codesign --force --deep -s - "$APP_DIR" || true

    # Create Zip
    local ZIP_PATH="$RELEASE_DIR/Fujifier-v2.1.0-macOS-$ARCH_NAME.zip"
    rm -f "$ZIP_PATH"
    (cd "$DIST_DIR" && zip -r -y "$ZIP_PATH" "$APP_NAME.app")
    echo "Created Archive: $ZIP_PATH"
}

# 4. Package Apple Silicon and Intel
package_app "Fujifier" "osx-arm64" "AppleSilicon"
package_app "Fujifier-Intel" "osx-x64" "Intel"

echo "=== macOS Packaging Complete! ==="
ls -lh "$RELEASE_DIR"
