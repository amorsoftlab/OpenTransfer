# AGENTS.md - OpenTransfer Repository Rules & Directives

## Mandatory Directives

1. **CHANGELOG Maintenance (CRITICAL):**
   - Whenever implementing ANY new feature, UI update, performance enhancement, or bug fix, ALWAYS update `CHANGELOG.md` under the current version section.
   - Categorize entries cleanly under `### 🎨 Added`, `### 🛠️ Fixed & Improved`, `### 🚀 Performance`, etc.

2. **Single Window In-App Architecture:**
   - Do not open secondary taskbar popup windows for Settings or navigation views. All sub-views must be rendered as embedded in-app panels inside `MainWindow.xaml`.

3. **Accent Color & Style Alignment:**
   - Selected items and hover states in Settings tabs must use Emerald Green (`#10B981`).

4. **Release Automation:**
   - Keep `publish_release.ps1`, `installer.iss`, `UpdateService.cs`, and `openTransferWPF.csproj` version tags strictly in sync.
