# 📜 OpenTransfer Changelog

All notable changes to **OpenTransfer** will be documented in this file.

---

## [1.2.7] - 2026-11-07

### 🛠️ Hotfix
- Expanded Appearance to 12 premium theme families, each with Light, Dark, and Follow Windows modes: Emerald Glass, VS Code, Nord Aurora, Dracula, Tokyo Night, Catppuccin, Rosé Pine, Solarized, One Dark, GitHub, Monokai, and Cyberpunk.
- No-device header controls now show disabled placeholders instead of blank combo boxes and empty dropdown popups.
- USB-only detection is now the default; wireless ADB/mDNS devices require an explicit Advanced setting and are labeled as Wi-Fi.
- Connection footer now reflects the real state instead of always showing a hard-coded Connected badge.
- Removed the hard-coded Redmi Note 7 Pro startup log and title.
- Offline and unauthorized ADB entries are no longer treated as connected devices.

---

## [1.2.6] - 2026-11-07

### 🛠️ Hotfix
- Fixed the `1.2.5` startup crash caused by mutating frozen WPF theme brushes.
- Converted all theme-dependent controls to dynamic resources so premium themes update live without restarting or crashing.
- Update dialog now converts GitHub Markdown into clean readable text instead of showing raw formatting syntax.
- Verified startup and live theme switching against a connected Redmi Note 7 Pro.

---

## [1.2.5] - 2026-11-07

### 🎨 Added
- **Premium Theme System:** Emerald Glass, VS Code, Nord Aurora, and Dracula palettes, each with Light, Dark, and Follow Windows modes plus instant full-UI preview.
- **Animated Transfer Experience:** Direction-aware phone/PC file animation, flowing particles, pause/resume synchronization, human-readable ETA, live speed, remaining bytes, and completion notifications.
- **Quick Access & Favorites:** Automatically detected Camera, Screenshots, Downloads, WhatsApp, and Telegram locations plus custom pinned Android folders.
- **Explorer Productivity:** Internal/removable storage selector, search operators (`ext:`, `min:`, `max:`, `after:`), persistent name/size/date sorting, and local file preview.
- **Transfer Reliability:** Serialized transfer queue, persistent transfer history, automatic retry, failed-file review, disconnect warnings, post-copy verification, and conflict policies.
- **Settings & Diagnostics:** Export/import/reset, launch-at-startup, tray support, ADB timeout, debug logging, and configurable log retention/cleanup.

### 🛠️ Fixed & Improved
- **Reliable Metadata & Sizes:** Replaced device-specific `ls` parsing with stable `find`/`stat`; file names with spaces, file sizes, optional folder sizes, and modified dates now resolve correctly.
- **Live Storage Information:** Free capacity refreshes during polling, manual refreshes, navigation, and transfer completion; removable SD cards are detected automatically.
- **Settings Wiring:** Folder colors, explorer visibility, refresh interval, reconnect, comparison, retry, verification, metadata preservation, and structure settings now affect runtime behavior.
- **Navigation Stability:** Stale asynchronous directory results can no longer overwrite newer navigation, and overlapping device polling is suppressed.
- **Version Consistency:** About, update checks, project metadata, installer, and release automation now share version `1.2.5`.
- **English-only Cleanup:** Removed language, transfer-mode, and buffer-size controls that did not map to real ADB capabilities.
- **Safer Releases:** Release automation uses normal branch pushes and annotated tags instead of force-pushing Git history.

---

## [1.2.4] - 2026-08-03

### 🛠️ Fixed & Improved
- **Auto-Split Logic Enhancement:** Split subfolders are now created within their respective leaf directories instead of at the root level, preserving the original folder structure during auto-split transfers. Naming has been updated to include the parent folder name for better context.
- **Clean Empty Folders:** Added a new context menu option in the File Explorer to recursively clean and delete empty folders from the Android device, similar to macOS folder cleanup behaviors.
- **Empty Folder Deep Clean & FUSE Fix:** Upgraded folder cleanup to automatically destroy invisible macOS/Windows/Android junk files (`.DS_Store`, `Thumbs.db`, `*.DAT`, and `.trashed-*` recycle bin files). Added a loop delay to bypass Android FUSE cache lagging during recursive deletes.
- **Custom Success Dialog:** Implemented a new custom Fluent-styled `SuccessDialog` to replace the generic Windows MessageBox upon completion.

---

## [1.2.3] - 2026-08-02

### 🎨 Added
- **Version 1.2.3 Release Update:**
  - Synchronized application versioning across core WPF components, update services, release automation scripts, and installer configs.

---

## [1.2.1] - 2026-07-31

###  Added
- **Auto-Split Folders/Files on Transfer Setting:**
  - Added option under `Settings -> Explorer` to automatically split files into batch subfolders during transfer.
  - Custom batch sizes supported: 100, 250, 500, or 1000 files per subfolder.
  - Custom folder naming formats supported: `photo-1, photo-2...` or `day 1-1, day 1-2...`.
- **Folder Accent Color Customization:** Added option under `Settings -> Appearance` to choose custom Folder Accent Colors (Yellow, Blue, Green, Purple, Red, Orange).
- **Real-Time Transfer Size & Remaining Stats:**
  - Added live display of current file copied size (`12.5 MB / 45.0 MB`).
  - Added live display of total job copied size vs total job size (`245.0 MB / 1.2 GB`).
  - Added live calculation of exact remaining size (`📦 Remaining: 975.0 MB`).
  - Added live item count ratio (`14 / 45 items`).

### 🛠️ Fixed & Improved
- Version metadata updated across C# services, Assembly metadata, installer scripts, and Github automation scripts.

---

## [1.2.0] - 2026-07-31

### 🚀 Added
- **In-App Auto-Update System:** Automatically checks GitHub Releases for new updates and provides live stream download with installer launcher.
- **Inno Setup Executable Packaging:** Added setup script generating `output/OpenTransfer_Setup_v1.2.0.exe`.
- **One-Click Release Automation:** Added `publish_release.bat` & `publish_release.ps1` script to build, package, commit, push, and release binaries.
- **Modern Fluent InputDialog:** Native WPF input dialog for folder creation and file renaming.
- **Single Window In-App Settings View:** Embedded Settings interface inside main window.
