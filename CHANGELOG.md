# 📜 OpenTransfer Changelog

All notable changes to **OpenTransfer** will be documented in this file.

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
