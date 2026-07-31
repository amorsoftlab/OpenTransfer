import SwiftUI

public enum SettingsCategory: String, CaseIterable, Identifiable {
    case general = "General"
    case appearance = "Appearance"
    case transfers = "Transfers"
    case device = "Device"
    case explorer = "Explorer"
    case advanced = "Advanced"
    case about = "About"

    public var id: String { rawValue }

    public var iconName: String {
        switch self {
        case .general: return "gearshape"
        case .appearance: return "paintpalette"
        case .transfers: return "arrow.triangle.2.circlepath"
        case .device: return "iphone"
        case .explorer: return "folder"
        case .advanced: return "wrench.and.screwdriver"
        case .about: return "info.circle"
        }
    }
}

public struct SettingsView: View {
    @Binding var isPresented: Bool
    @ObservedObject var settingsService = SettingsService.shared

    @State private var selectedCategory: SettingsCategory = .general

    // General
    @State private var autoDetectDevices: Bool = true
    @State private var autoReconnect: Bool = true

    // Appearance
    @State private var folderColor: String = "Yellow"
    @State private var folderIconPack: String = "macos-native"
    @State private var theme: String = "System"
    @State private var language: String = "English"

    // Transfers
    @State private var conflictResolution: String = "Skip"
    @State private var compareMethod: String = "FilenameSize"
    @State private var transferMode: String = "Balanced"
    @State private var verifyCopiedFiles: Bool = true
    @State private var retryFailedTransfers: Bool = true

    // Device
    @State private var defaultAndroidStorage: String = "/sdcard"
    @State private var refreshIntervalSeconds: Int = 3

    // Explorer & Auto-Split
    @State private var showHiddenFiles: Bool = false
    @State private var preserveModifiedDate: Bool = true
    @State private var autoSplitOnTransfer: Bool = false
    @State private var autoSplitBatchSize: Int = 500
    @State private var autoSplitNamingFormat: String = "Photo"

    // Advanced
    @State private var customAdbPath: String = ""
    @State private var enableDebugLogging: Bool = false

    public var body: some View {
        HStack(spacing: 0) {
            // Left Sidebar Category List
            VStack(alignment: .leading, spacing: 4) {
                Text("Settings")
                    .font(.headline)
                    .padding(.horizontal, 12)
                    .padding(.top, 12)
                    .padding(.bottom, 6)

                ForEach(SettingsCategory.allCases) { cat in
                    HStack(spacing: 10) {
                        Image(systemName: cat.iconName)
                            .frame(width: 18)
                        Text(cat.rawValue)
                            .font(.system(size: 13, weight: selectedCategory == cat ? .bold : .medium))
                        Spacer()
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .background(selectedCategory == cat ? Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)) : Color.clear) // Emerald Green #10B981
                    .foregroundColor(selectedCategory == cat ? .white : .primary)
                    .cornerRadius(6)
                    .contentShape(Rectangle())
                    .onTapGesture {
                        selectedCategory = cat
                    }
                }
                Spacer()
            }
            .padding(8)
            .frame(width: 170)
            .background(Color(NSColor.controlBackgroundColor))

            Divider()

            // Right Category Content View
            VStack(spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        switch selectedCategory {
                        case .general:
                            generalCategoryView
                        case .appearance:
                            appearanceCategoryView
                        case .transfers:
                            transfersCategoryView
                        case .device:
                            deviceCategoryView
                        case .explorer:
                            explorerCategoryView
                        case .advanced:
                            advancedCategoryView
                        case .about:
                            aboutCategoryView
                        }
                    }
                    .padding(20)
                }

                Divider()

                // Footer Buttons
                HStack {
                    Spacer()
                    Button("Cancel") {
                        isPresented = false
                    }

                    Button("Save Settings") {
                        saveAllSettings()
                        isPresented = false
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0))) // Emerald Green #10B981
                }
                .padding(12)
                .background(Color(NSColor.windowBackgroundColor))
            }
        }
        .frame(width: 680, height: 480)
        .onAppear {
            loadAllSettings()
        }
    }

    // 0. General Panel
    private var generalCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("⚙️ General Settings")
                .font(.title3).bold()

            Toggle("Auto detect Android USB devices", isOn: $autoDetectDevices)
            Toggle("Auto reconnect device when plugged in", isOn: $autoReconnect)
        }
    }

    // 1. Appearance Panel
    private var appearanceCategoryView: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("🎨 Appearance Settings")
                .font(.title3).bold()

            Picker("Folder Accent Color:", selection: $folderColor) {
                Text("Yellow 🟡").tag("Yellow")
                Text("Blue 🔵").tag("Blue")
                Text("Green 🟢").tag("Green")
                Text("Purple 🟣").tag("Purple")
                Text("Red 🔴").tag("Red")
                Text("Orange 🟠").tag("Orange")
            }

            Divider()

            Text("📦 Folder Icon Pack")
                .font(.headline)

            let packs: [(id: String, name: String, desc: String, icons: [String])] = [
                ("macos-native", "macOS Native", "Apple system icons · No download",
                 ["folder.fill", "folder.fill", "folder.fill", "folder.fill", "folder.fill"]),
                ("colorful", "Colorful", "Bright, distinct folder colors",
                 ["folder.fill", "folder.badge.plus", "folder.badge.person.crop", "folder.badge.gearshape", "folder.badge.questionmark"]),
                ("fluent", "Fluent", "Windows Fluent style icons",
                 ["folder", "folder", "folder", "folder", "folder"]),
                ("minimal", "Minimal", "Clean monochrome design",
                 ["square", "square", "square", "square", "square"]),
                ("opentransfer", "OpenTransfer", "Custom premium branded icons",
                 ["shippingbox.fill", "shippingbox.fill", "shippingbox.fill", "shippingbox.fill", "shippingbox.fill"]),
            ]

            VStack(spacing: 8) {
                ForEach(packs, id: \.id) { pack in
                    HStack(spacing: 12) {
                        // Radio-style selection indicator
                        ZStack {
                            Circle().stroke(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)), lineWidth: 2).frame(width: 18, height: 18)
                            if folderIconPack == pack.id {
                                Circle().fill(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0))).frame(width: 10, height: 10)
                            }
                        }
                        .onTapGesture { folderIconPack = pack.id }

                        VStack(alignment: .leading, spacing: 2) {
                            Text(pack.name).fontWeight(.semibold).font(.system(size: 13))
                            Text(pack.desc).font(.caption).foregroundColor(.secondary)
                        }
                        .onTapGesture { folderIconPack = pack.id }

                        Spacer()

                        // Preview strip of 5 icons
                        HStack(spacing: 6) {
                            ForEach(Array(pack.icons.prefix(5).enumerated()), id: \.offset) { idx, icon in
                                let colors: [Color] = [folderColorValue, .blue, .purple, .green, .orange]
                                Image(systemName: icon)
                                    .font(.system(size: 20))
                                    .foregroundColor(
                                        pack.id == "minimal" ? .primary.opacity(0.6) :
                                        pack.id == "fluent"  ? Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)) :
                                        pack.id == "colorful" ? colors[idx % colors.count] :
                                        pack.id == "opentransfer" ? Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)) :
                                        folderColorValue
                                    )
                            }
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(folderIconPack == pack.id ? Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 0.1)) : Color(NSColor.controlBackgroundColor))
                        .cornerRadius(8)
                    }
                    .padding(10)
                    .background(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(folderIconPack == pack.id ? Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)) : Color.gray.opacity(0.2), lineWidth: folderIconPack == pack.id ? 2 : 1)
                    )
                    .contentShape(Rectangle())
                    .onTapGesture { folderIconPack = pack.id }
                }
            }

            Divider()

            Picker("Application Theme:", selection: $theme) {
                Text("System Default").tag("System")
                Text("Light Theme").tag("Light")
                Text("Dark Theme").tag("Dark")
            }

            Picker("Language:", selection: $language) {
                Text("English").tag("English")
                Text("Malayalam").tag("Malayalam")
            }
        }
    }

    private var folderColorValue: Color {
        switch folderColor {
        case "Blue": return .blue
        case "Green": return .green
        case "Purple": return .purple
        case "Red": return .red
        case "Orange": return .orange
        default: return Color(red: 0.95, green: 0.75, blue: 0.1)
        }
    }

    // 2. Transfers Panel
    private var transfersCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("🚀 File Transfer & Conflict Policies")
                .font(.title3).bold()

            Picker("Conflict Resolution (When File Exists):", selection: $conflictResolution) {
                Text("Skip Existing (Default)").tag("Skip")
                Text("Replace Existing").tag("Replace")
                Text("Ask Every Time").tag("Ask")
            }

            Picker("Compare Method:", selection: $compareMethod) {
                Text("Filename + Size (Default)").tag("FilenameSize")
                Text("Filename Only").tag("FilenameOnly")
            }

            Picker("Performance Mode:", selection: $transferMode) {
                Text("Balanced (Default)").tag("Balanced")
                Text("Maximum Speed").tag("MaxSpeed")
                Text("Maximum Compatibility").tag("MaxCompatibility")
            }

            Toggle("Verify copied files after transfer", isOn: $verifyCopiedFiles)
            Toggle("Retry failed transfers automatically", isOn: $retryFailedTransfers)
        }
    }

    // 3. Device Panel
    private var deviceCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("📱 Device Settings")
                .font(.title3).bold()

            HStack {
                Text("Default Android Root:")
                TextField("/sdcard", text: $defaultAndroidStorage)
                    .textFieldStyle(.roundedBorder)
            }

            Picker("Auto Refresh Device Interval:", selection: $refreshIntervalSeconds) {
                Text("1 Second").tag(1)
                Text("3 Seconds").tag(3)
                Text("5 Seconds").tag(5)
            }
        }
    }

    // 4. Explorer Panel
    private var explorerCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("📁 Explorer & Auto-Split Settings")
                .font(.title3).bold()

            Toggle("Show Hidden Files (.filenames)", isOn: $showHiddenFiles)
            Toggle("Preserve Original File Modified Dates", isOn: $preserveModifiedDate)

            Divider()

            Toggle("Enable Auto-Split Folders on Transfer", isOn: $autoSplitOnTransfer)

            if autoSplitOnTransfer {
                Picker("Batch Size (Files per folder):", selection: $autoSplitBatchSize) {
                    Text("100 Files").tag(100)
                    Text("250 Files").tag(250)
                    Text("500 Files").tag(500)
                    Text("1000 Files").tag(1000)
                }

                Picker("Folder Naming Format:", selection: $autoSplitNamingFormat) {
                    Text("Hyphen (folder-1, folder-2...)").tag("Hyphen")
                    Text("Underscore (folder_1, folder_2...)").tag("Underscore")
                    Text("Range Hyphen (1-500, 501-1000...)").tag("RangeHyphen")
                    Text("Range Underscore (1_500, 501_1000...)").tag("RangeUnderscore")
                }

            }
        }
    }

    // 5. Advanced Panel
    private var advancedCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("🛠️ Advanced Configurations")
                .font(.title3).bold()

            VStack(alignment: .leading, spacing: 6) {
                Text("Custom ADB Executable Path:")
                HStack {
                    TextField("System Default", text: $customAdbPath)
                        .textFieldStyle(.roundedBorder)

                    Button("Browse...") {
                        let panel = NSOpenPanel()
                        panel.canChooseFiles = true
                        panel.canChooseDirectories = false
                        if panel.runModal() == .OK, let path = panel.url?.path {
                            customAdbPath = path
                        }
                    }
                }
            }

            Toggle("Enable Detailed Debug Logging", isOn: $enableDebugLogging)
        }
    }

    // 6. About Panel (Windows-parity redesign)
    private var aboutCategoryView: some View {
        VStack(alignment: .leading, spacing: 14) {
            // Header row
            HStack(spacing: 14) {
                Image(systemName: "square.and.arrow.up.fill")
                    .font(.system(size: 44))
                    .foregroundColor(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)))
                VStack(alignment: .leading, spacing: 4) {
                    Text("OpenTransfer")
                        .font(.title2).bold()
                    Text("Native High-Speed Android USB File Transfer")
                        .font(.subheadline)
                        .foregroundColor(.secondary)
                }
            }
            .padding(.bottom, 4)

            // Version card
            GroupBox {
                VStack(alignment: .leading, spacing: 6) {
                    infoRow(label: "Version",   value: UpdateService.currentVersion)
                    infoRow(label: "Build",     value: buildDate)
                    infoRow(label: "Platform",  value: "macOS \(ProcessInfo.processInfo.operatingSystemVersionString)")
                    infoRow(label: "ADB",       value: "Platform Tools 35.x")
                    infoRow(label: "Framework", value: "SwiftUI · macOS 13+")
                }
            }

            // Developer info card
            GroupBox(label: Label("👨‍💻 Developer Information", systemImage: "person.fill")) {
                VStack(alignment: .leading, spacing: 8) {
                    infoRow(label: "👤 Developer",   value: "Jaseem Mhd")
                    HStack {
                        Text("📸 Instagram:").foregroundColor(.secondary).frame(width: 110, alignment: .leading)
                        Link("magical_world_i_see",
                             destination: URL(string: "https://instagram.com/magical_world_i_see")!)
                    }
                    HStack {
                        Text("🐙 GitHub:").foregroundColor(.secondary).frame(width: 110, alignment: .leading)
                        Link("amorsoftlab/OpenTransfer",
                             destination: URL(string: "https://github.com/amorsoftlab/OpenTransfer")!)
                    }
                }
                .padding(.top, 4)
            }

            // Action buttons
            HStack(spacing: 10) {
                Button {
                    UpdateService.shared.checkForUpdatesManually()
                } label: {
                    Label("Check for Updates", systemImage: "arrow.down.circle")
                }
                .buttonStyle(.borderedProminent)
                .tint(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)))

                Button {
                    openLogsFolder()
                } label: {
                    Label("Open Logs Folder", systemImage: "folder")
                }
                .buttonStyle(.bordered)
            }

            Text("© 2026 OpenTransfer · amorsoftlab. All rights reserved.")
                .font(.caption)
                .foregroundColor(.secondary)
        }
    }

    private var buildDate: String {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy.MM.dd"
        return formatter.string(from: Date())
    }

    private func infoRow(label: String, value: String) -> some View {
        HStack {
            Text("\(label):").foregroundColor(.secondary).frame(width: 100, alignment: .leading)
            Text(value).fontWeight(.semibold)
        }
    }

    private func openLogsFolder() {
        let logDir = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first?.appendingPathComponent("Logs/OpenTransfer")
        if let dir = logDir {
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            NSWorkspace.shared.open(dir)
        }
    }

    private func loadAllSettings() {
        let cur = settingsService.settings
        customAdbPath = cur.customAdbPath
        refreshIntervalSeconds = cur.refreshIntervalSeconds
        showHiddenFiles = cur.showHiddenFiles
        folderColor = cur.folderColor
        folderIconPack = cur.folderIconPack
        autoSplitOnTransfer = cur.autoSplitOnTransfer
        autoSplitBatchSize = cur.autoSplitBatchSize
        autoSplitNamingFormat = cur.autoSplitNamingFormat
    }

    private func saveAllSettings() {
        settingsService.settings.customAdbPath = customAdbPath
        settingsService.settings.refreshIntervalSeconds = refreshIntervalSeconds
        settingsService.settings.showHiddenFiles = showHiddenFiles
        settingsService.settings.folderColor = folderColor
        settingsService.settings.folderIconPack = folderIconPack
        settingsService.settings.autoSplitOnTransfer = autoSplitOnTransfer
        settingsService.settings.autoSplitBatchSize = autoSplitBatchSize
        settingsService.settings.autoSplitNamingFormat = autoSplitNamingFormat

        AdbService.shared.customAdbPath = customAdbPath
        LoggerService.shared.log("Saved updated settings across all categories.")
    }
}
