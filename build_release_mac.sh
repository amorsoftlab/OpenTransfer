#!/bin/bash
# ============================================================
# OpenTransfer macOS — Automated Build, Package & Release Script
# Usage: ./build_release_mac.sh [version]
# Example: ./build_release_mac.sh 1.2.2
# ============================================================

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# ── Configuration ────────────────────────────────────────────
REPO_URL="https://github.com/amorsoftlab/OpenTransfer.git"
GITHUB_REPO="amorsoftlab/OpenTransfer"
APP_NAME="OpenTransfer"
MAC_PROJECT_DIR="$SCRIPT_DIR/OpenTransferMac"
BUILD_DIR="$SCRIPT_DIR/mac_build"
PKG_OUTPUT_DIR="$SCRIPT_DIR/mac_output"

# ── Version Resolution ───────────────────────────────────────
VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  VERSION=$(grep -o 'v[0-9]\+\.[0-9]\+\.[0-9]\+' "$MAC_PROJECT_DIR/Sources/Services/TransferServices.swift" | head -1 | tr -d 'v')
  if [ -z "$VERSION" ]; then
    echo "❌ Could not auto-detect version. Pass it as an argument: ./build_release_mac.sh 1.2.2"
    exit 1
  fi
fi
TAG="v$VERSION"

echo "=================================================="
echo "🚀 OpenTransfer macOS Build & Release Pipeline"
echo "   Version: $TAG"
echo "=================================================="

# ── Step 1: Update version strings in source ─────────────────
echo ""
echo "[1/6] 🔧 Syncing version $VERSION into source files..."

sed -i '' "s/OpenTransfer macOS (v[0-9.]*)/OpenTransfer macOS (v$VERSION)/g" \
    "$MAC_PROJECT_DIR/Sources/Services/TransferServices.swift"

sed -i '' "s/Version [0-9.]* (Native macOS SwiftUI)/Version $VERSION (Native macOS SwiftUI)/g" \
    "$MAC_PROJECT_DIR/Sources/Views/SettingsView.swift"

echo "   ✅ Version strings updated to $VERSION."

# ── Step 2: Build the Swift CLI binary (release mode) ────────
echo ""
echo "[2/6] 🔨 Building Swift release binary..."
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

swift build -c release --package-path "$MAC_PROJECT_DIR"
BINARY="$MAC_PROJECT_DIR/.build/release/OpenTransferMac"
if [ ! -f "$BINARY" ]; then
  echo "❌ Build failed — binary not found at $BINARY"
  exit 1
fi
echo "   ✅ Binary built: $BINARY"

# ── Step 3: Create .app Bundle ────────────────────────────────
echo ""
echo "[3/6] 📦 Creating $APP_NAME.app bundle..."
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"
CONTENTS="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"

rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

cp "$BINARY" "$MACOS_DIR/$APP_NAME"
chmod +x "$MACOS_DIR/$APP_NAME"

cat > "$CONTENTS/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>com.amorsoftlab.opentransfer</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>OpenTransfer</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>SUFeedURL</key>
    <string>https://raw.githubusercontent.com/amorsoftlab/OpenTransfer/main/mac_appcast.xml</string>
</dict>
</plist>
PLIST

echo "   ✅ .app bundle created at $APP_BUNDLE"

# ── Step 4: Build .pkg Installer ─────────────────────────────
echo ""
echo "[4/6] 📦 Building .pkg installer..."
rm -rf "$PKG_OUTPUT_DIR"
mkdir -p "$PKG_OUTPUT_DIR"

COMPONENT_PKG="$BUILD_DIR/$APP_NAME-component.pkg"
FINAL_PKG="$PKG_OUTPUT_DIR/${APP_NAME}_${TAG}_mac.pkg"

pkgbuild \
    --root "$BUILD_DIR" \
    --install-location "/Applications" \
    --identifier "com.amorsoftlab.opentransfer" \
    --version "$VERSION" \
    "$COMPONENT_PKG"

productbuild \
    --distribution /dev/stdin \
    --package-path "$BUILD_DIR" \
    "$FINAL_PKG" << DISTXML
<?xml version="1.0" encoding="UTF-8"?>
<installer-gui-script minSpecVersion="1">
    <title>OpenTransfer $VERSION</title>
    <organization>com.amorsoftlab</organization>
    <domains enable_localSystem="true"/>
    <options customize="never" require-scripts="false" rootVolumeOnly="true"/>
    <choices-outline>
        <line choice="default">
            <line choice="com.amorsoftlab.opentransfer"/>
        </line>
    </choices-outline>
    <choice id="default"/>
    <choice id="com.amorsoftlab.opentransfer" visible="false">
        <pkg-ref id="com.amorsoftlab.opentransfer"/>
    </choice>
    <pkg-ref id="com.amorsoftlab.opentransfer" version="$VERSION" onConclusion="none">$APP_NAME-component.pkg</pkg-ref>
</installer-gui-script>
DISTXML

echo "   ✅ .pkg installer: $FINAL_PKG"

ZIP_PATH="$PKG_OUTPUT_DIR/${APP_NAME}_${TAG}_mac.zip"
ditto -c -k --keepParent "$APP_BUNDLE" "$ZIP_PATH"
echo "   ✅ .zip archive: $ZIP_PATH"

# ── Step 5: Update Appcast XML (in-app auto-update feed) ─────
echo ""
echo "[5/6] 📡 Updating mac_appcast.xml..."
PKG_SIZE=$(stat -f%z "$FINAL_PKG")
PUB_DATE=$(date -u +"%a, %d %b %Y %H:%M:%S +0000")

cat > "$SCRIPT_DIR/mac_appcast.xml" << XML
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
    <channel>
        <title>OpenTransfer macOS Updates</title>
        <link>https://github.com/amorsoftlab/OpenTransfer</link>
        <description>OpenTransfer macOS Latest Releases</description>
        <language>en</language>
        <item>
            <title>OpenTransfer $VERSION</title>
            <pubDate>$PUB_DATE</pubDate>
            <sparkle:version>$VERSION</sparkle:version>
            <sparkle:shortVersionString>$VERSION</sparkle:shortVersionString>
            <sparkle:releaseNotesLink>https://github.com/amorsoftlab/OpenTransfer/releases/tag/$TAG</sparkle:releaseNotesLink>
            <enclosure
                url="https://github.com/amorsoftlab/OpenTransfer/releases/download/$TAG/${APP_NAME}_${TAG}_mac.pkg"
                sparkle:version="$VERSION"
                length="$PKG_SIZE"
                type="application/octet-stream"/>
        </item>
    </channel>
</rss>
XML
echo "   ✅ mac_appcast.xml updated."

# ── Step 6: Git Commit, Tag & Push + GitHub Release ──────────
echo ""
echo "[6/6] 🚀 Syncing code & publishing GitHub Release..."

REMOTES=$(git remote -v 2>/dev/null || true)
if ! echo "$REMOTES" | grep -q "amorsoftlab/OpenTransfer"; then
  git remote remove origin 2>/dev/null || true
  git remote add origin "$REPO_URL"
fi

git add -A
STATUS=$(git status --porcelain 2>/dev/null || true)
TIMESTAMP=$(date +"%Y-%m-%d %H:%M:%S")
if [ -n "$STATUS" ]; then
  git commit -m "Release $TAG ($TIMESTAMP): macOS source sync & build output"
  echo "   ✅ Committed latest changes."
else
  echo "   Working tree clean. No new commits needed."
fi

git branch -M main
git push -u origin main -f

git tag -f "$TAG"
git push origin "$TAG" -f
echo "   ✅ Tag $TAG pushed to GitHub."

if command -v gh &>/dev/null; then
  RELEASE_NOTES="## OpenTransfer $VERSION — macOS Release

### What's New
- Grid View / List View toggle
- Breadcrumb address bar (Device name + path shown separately)
- Right-click context menus on all items
- Conflict resolution dialog (Replace / Skip / Cancel) on drag & drop
- Nested folder copy bug fix
- Drag & drop overlay stuck bug fixed
- Full row click selection in file list

### Download
- \`${APP_NAME}_${TAG}_mac.pkg\` — macOS Installer (installs to /Applications)
- \`${APP_NAME}_${TAG}_mac.zip\` — Portable App Bundle (.app zip)"

  echo "   Publishing GitHub Release $TAG..."
  gh release create "$TAG" \
    "$FINAL_PKG" \
    "$ZIP_PATH" \
    --title "OpenTransfer $TAG (macOS)" \
    --notes "$RELEASE_NOTES" \
    --repo "$GITHUB_REPO" 2>/dev/null \
  || gh release upload "$TAG" \
    "$FINAL_PKG" \
    "$ZIP_PATH" \
    --clobber \
    --repo "$GITHUB_REPO"

  echo "   🎉 GitHub Release published!"
  echo "   👉 https://github.com/$GITHUB_REPO/releases/tag/$TAG"
else
  echo "   ⚠️  'gh' CLI not found. Tag $TAG pushed to GitHub."
  echo "   Upload manually at: https://github.com/$GITHUB_REPO/releases/tag/$TAG"
  echo "   Files ready:"
  echo "     - $FINAL_PKG"
  echo "     - $ZIP_PATH"
fi

echo ""
echo "=================================================="
echo "✅ SUCCESS: OpenTransfer macOS $TAG Release Complete!"
echo "=================================================="
