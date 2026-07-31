# 📜 OpenTransfer Changelog

All notable changes to **OpenTransfer** will be documented in this file.

---

## [1.2.1] - 2026-07-31

### 🎨 Added
- **Folder Icon Pack Selector (macOS):** 5 icon packs selectable in `Settings → Appearance`:
  - `macOS Native` — System SF Symbols (default)
  - `Colorful` — Distinct color per folder, cycling palette
  - `Fluent` — Windows Fluent style (Emerald Green outlines)
  - `Minimal` — Monochrome square icons for clean UI
  - `OpenTransfer` — Custom branded shipping-box premium icons
  - Live preview strip of 5 icons shown per pack inside settings.
- **Grid View / List View Toggle:** Segmented button in toolbar to switch between List (Table) and Icon Grid layouts instantly.
- **Windows-style Address Bar:** Device name on top, breadcrumb path (`Internal Storage > DCIM > Camera`) on the bottom row.
- **Redesigned About Panel (macOS):** Matches Windows version with Version card, Developer Info card, platform details, Instagram/GitHub clickable links, Check for Updates button, and Open Logs Folder button.
- **Auto-Update System (macOS):** `UpdateService` checks GitHub Releases API silently on every launch. Shows native alert if a newer version is available.
- **macOS Release Script:** `build_release_mac.sh` — One command to build `.app` + `.pkg` installer, update `mac_appcast.xml`, and publish GitHub Release.
- **Conflict Resolution Dialog:** Native alert on Drag & Drop when files/folders already exist — Replace All / Skip Existing / Cancel.
- **Custom Subfolder Naming Formats:** Hyphen, Underscore, Range-Hyphen, Range-Underscore — configurable in `Settings → Explorer`.
- **Auto-Split Threshold Fix:** Split folders now only created when file count strictly exceeds the batch size limit.

### 🛠️ Fixed & Improved
- **Nested Folder Copy Bug Fixed:** Dragging `Panchayth/` folder into `/sdcard/` where `Panchayth/` exists no longer creates `Panchayth/Panchayth/`. ADB push now uses `source/.` to merge contents.
- **Drop Overlay Stuck Fixed:** Added `.allowsHitTesting(false)` to drag overlay, preventing it from intercepting mouse events after drop.
- **Row Click Selection Fixed:** Replaced `onTapGesture` with `.contextMenu(forSelectionType:)` — now clicking anywhere on a row selects it, double-clicking anywhere opens folders.

---



## [1.2.0] - 2026-07-31

### 🚀 Added
- **In-App Auto-Update System:** Automatically checks GitHub Releases for new updates and provides live stream download with installer launcher.
- **Inno Setup Executable Packaging:** Added setup script generating `output/OpenTransfer_Setup_v1.2.0.exe`.
- **One-Click Release Automation:** Added `publish_release.bat` & `publish_release.ps1` script to build, package, commit, push, and release binaries.
- **Modern Fluent InputDialog:** Native WPF input dialog for folder creation and file renaming.
- **Single Window In-App Settings View:** Embedded Settings interface inside main window.
