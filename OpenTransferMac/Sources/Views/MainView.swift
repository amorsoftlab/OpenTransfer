import SwiftUI

public struct MainView: View {
    @AppStorage("isGridView") private var isGridView = false
    
    @State private var devices: [DeviceItem] = []
    @State private var selectedSerial: String = ""
    @State private var currentPath: String = "/sdcard"
    @State private var items: [AndroidFileItem] = []
    @State private var history: [String] = ["/sdcard"]
    @State private var historyIndex: Int = 0
    @State private var searchText: String = ""
    @State private var isLoading: Bool = false
    @State private var isLogExpanded: Bool = false
    @State private var showSettings: Bool = false
    @State private var newFolderName: String = ""
    @State private var showNewFolderAlert: Bool = false
    @State private var isLocalDriveMode: Bool = false
    @State private var selectedItems: Set<AndroidFileItem.ID> = []
    @State private var showRenameAlert: Bool = false
    @State private var newRenameName: String = ""
    @State private var showDeleteConfirmation: Bool = false

    @ObservedObject var copyEngine = CopyEngine.shared

    public var filteredItems: [AndroidFileItem] {
        if searchText.isEmpty {
            return items
        }
        return items.filter { $0.name.localizedCaseInsensitiveContains(searchText) }
    }

    private var breadcrumbPath: String {
        var displayPath = currentPath
        if displayPath.hasPrefix("/sdcard") {
            displayPath = displayPath.replacingOccurrences(of: "/sdcard", with: "Internal Storage")
        }
        let components = displayPath.split(separator: "/").filter { !$0.isEmpty }
        if components.isEmpty { return "Root" }
        return components.joined(separator: " > ")
    }

    public var body: some View {
        VStack(spacing: 0) {
            headerToolbar

            Divider()

            // Main Phone Content Area
            ZStack {
                if isLoading {
                    VStack(spacing: 12) {
                        ProgressView()
                        Text("Loading Phone Storage...")
                            .foregroundColor(.secondary)
                    }
                } else if devices.isEmpty {
                    VStack(spacing: 16) {
                        Image(systemName: "smartphone")
                            .font(.system(size: 48))
                            .foregroundColor(.orange)
                        Text("No Android Phone Connected")
                            .font(.title2)
                            .bold()
                        Text("Connect your Android phone via USB (File Transfer / MTP or USB Debugging).")
                            .foregroundColor(.secondary)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 30)

                        Button("Scan for Connected Phone") {
                            refreshDevices()
                        }
                        .buttonStyle(.borderedProminent)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    FileListView(
                        items: filteredItems,
                        currentPath: currentPath,
                        selectedSerial: selectedSerial,
                        selection: $selectedItems,
                        isGridView: isGridView,
                        onNavigate: { newPath in
                            navigateTo(newPath)
                        },
                        onRefresh: {
                            loadFiles()
                        }
                    )
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)

            // Transfer Progress Banner with v1.2.1 Live Stats
            if copyEngine.isTransferring, let active = copyEngine.currentTransfer {
                HStack(spacing: 16) {
                    ProgressView()
                        .scaleEffect(0.8)

                    VStack(alignment: .leading, spacing: 2) {
                        Text("Transferring: \(active.fileName)")
                            .font(.caption)
                            .bold()
                        Text("📦 Remaining: \(formatBytes(active.remainingBytes)) | Speed: \(String(format: "%.1f", active.speedMbPerSec)) MB/s")
                            .font(.system(.caption2, design: .monospaced))
                            .foregroundColor(.secondary)
                    }

                    Spacer()
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .background(Color.accentColor.opacity(0.15))
            }

            Divider()

            // Live Activity Log Console
            LogConsoleView(isExpanded: $isLogExpanded)
        }
        .task {
            refreshDevices()
        }
        .onChange(of: selectedSerial) { newSerial in
            if !newSerial.isEmpty {
                currentPath = isLocalDriveMode ? newSerial : "/sdcard"
                history = [currentPath]
                historyIndex = 0
                loadFiles()
            }
        }
        .onChange(of: copyEngine.completedCount) { _ in
            loadFiles()
        }

        .sheet(isPresented: $showSettings) {
            SettingsView(isPresented: $showSettings)
        }
        .alert("New Folder", isPresented: $showNewFolderAlert) {
            TextField("Folder Name", text: $newFolderName)
            Button("Cancel", role: .cancel) { newFolderName = "" }
            Button("Create") {
                let name = newFolderName.trimmingCharacters(in: .whitespacesAndNewlines)
                if !name.isEmpty {
                    createNewFolder(name: name)
                }
                newFolderName = ""
            }
        } message: {
            Text("Enter a name for the new folder inside \(currentPath)")
        }
        .alert("Rename Item", isPresented: $showRenameAlert) {
            TextField("New Name", text: $newRenameName)
            Button("Cancel", role: .cancel) { newRenameName = "" }
            Button("Rename") {
                executeRename()
            }
        } message: {
            Text("Enter a new name for the item.")
        }
        .alert("Delete Selected Items?", isPresented: $showDeleteConfirmation) {
            Button("Cancel", role: .cancel) { }
            Button("Delete", role: .destructive) {
                executeDelete()
            }
        } message: {
            Text("Are you sure you want to delete \(selectedItems.count) item(s)? This cannot be undone.")
        }
    }

    private var headerToolbar: some View {
        VStack(spacing: 12) {
            // Top Row: Navigation and Address Bar
            HStack(spacing: 10) {
                Button(action: goBack) { Image(systemName: "arrow.left") }
                    .disabled(historyIndex <= 0)
                    .buttonStyle(.borderless)
                Button(action: goForward) { Image(systemName: "arrow.right") }
                    .disabled(historyIndex >= history.count - 1)
                    .buttonStyle(.borderless)
                Button(action: goUp) { Image(systemName: "arrow.up") }
                    .disabled(currentPath == "/" || currentPath == "/sdcard" || currentPath.isEmpty)
                    .buttonStyle(.borderless)

                // Windows-style Address Bar
                HStack {
                    Image(systemName: "smartphone")
                        .foregroundColor(.accentColor)
                        .font(.system(size: 20))
                    
                    VStack(alignment: .leading, spacing: 2) {
                        if let dev = devices.first(where: { $0.serial == selectedSerial }) {
                            Text(dev.displayName).fontWeight(.semibold)
                        } else {
                            Text("No Device Selected").fontWeight(.semibold)
                        }
                        Text(breadcrumbPath)
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    Spacer()
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(Color(NSColor.controlBackgroundColor))
                .cornerRadius(6)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.gray.opacity(0.2), lineWidth: 1))

                // Search Filter
                HStack {
                    Image(systemName: "magnifyingglass").foregroundColor(.secondary)
                    TextField("Search \(currentPath.components(separatedBy: "/").last ?? "")", text: $searchText)
                        .textFieldStyle(.plain)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .background(Color(NSColor.controlBackgroundColor))
                .cornerRadius(6)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.gray.opacity(0.2), lineWidth: 1))
                .frame(width: 200)
            }

            // Bottom Row: Action Toolbar
            HStack(spacing: 16) {
                Button(action: { showNewFolderAlert = true }) {
                    Label("New Folder", systemImage: "folder.badge.plus")
                }
                .buttonStyle(.borderless)
                .disabled(devices.isEmpty)

                Divider().frame(height: 14)

                Button(action: prepareRename) {
                    Label("Rename", systemImage: "pencil")
                }
                .buttonStyle(.borderless)
                .disabled(selectedItems.count != 1)

                Button(action: { showDeleteConfirmation = true }) {
                    Label("Delete", systemImage: "trash")
                        .foregroundColor(selectedItems.isEmpty ? .secondary : .red)
                }
                .buttonStyle(.borderless)
                .disabled(selectedItems.isEmpty)

                Button(action: refreshBoth) {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                .buttonStyle(.borderless)

                Spacer()

                if !devices.isEmpty {
                    if isLocalDriveMode {
                        Text("USB Storage Mode")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(.orange)
                    } else {
                        Text("ADB Mode")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(.green)
                    }
                }

                Picker("", selection: $isGridView) {
                    Image(systemName: "list.bullet").tag(false)
                    Image(systemName: "square.grid.2x2").tag(true)
                }
                .pickerStyle(.segmented)
                .frame(width: 80)

                Button(action: { showSettings = true }) {
                    Image(systemName: "gearshape")
                }
                .buttonStyle(.borderless)
                .help("Settings")
            }
        }
        .padding(14)
        .background(Color(NSColor.windowBackgroundColor))
    }

    @State private var lastLoggedDeviceCount: Int = -1

    private func refreshDevices() {
        Task {
            var fetched = await AdbService.shared.getDevices()
            var localDriveFound = false

            if fetched.isEmpty {
                // Try ADB reset first
                await AdbService.shared.restartAdbServer()
                fetched = await AdbService.shared.getDevices()
            }

            // Fallback check for mounted MTP/USB phone drives under /Volumes
            if fetched.isEmpty {
                let volumes = LocalFileService.shared.getMountedVolumes()
                for vol in volumes {
                    let name = (vol as NSString).lastPathComponent
                    // Exclude Mac system volumes
                    if name != "/" && name != "Macintosh HD" && !vol.hasPrefix("/Volumes/Macintosh") && name != "Volumes" {
                        fetched.append(DeviceItem(serial: vol, name: name, state: "mounted", model: "MTP USB Storage"))
                        localDriveFound = true
                    }
                }
            }

            await MainActor.run {
                self.isLocalDriveMode = localDriveFound
                let count = fetched.count
                if count != self.lastLoggedDeviceCount {
                    self.lastLoggedDeviceCount = count
                    LoggerService.shared.log("Found \(count) connected phone device(s).")
                }
                self.devices = fetched
                if let first = fetched.first {
                    if self.selectedSerial.isEmpty || !fetched.contains(where: { $0.serial == self.selectedSerial }) {
                        self.selectedSerial = first.serial
                        self.currentPath = localDriveFound ? first.serial : "/sdcard"
                        self.history = [self.currentPath]
                        self.historyIndex = 0
                    }
                    self.loadFiles()
                } else {
                    self.selectedSerial = ""
                    self.items = []
                }
            }
        }
    }

    private func loadFiles() {
        guard !selectedSerial.isEmpty else { return }
        isLoading = true
        
        if isLocalDriveMode {
            let localItems = LocalFileService.shared.listFiles(atPath: currentPath)
            self.items = localItems
            self.isLoading = false
            LoggerService.shared.log("Loaded \(localItems.count) phone items in \(currentPath)")
        } else {
            Task {
                let fileList = await AdbService.shared.listFiles(serial: selectedSerial, remotePath: currentPath)
                await MainActor.run {
                    self.items = fileList
                    self.isLoading = false
                    LoggerService.shared.log("Loaded \(fileList.count) phone items in \(currentPath)")
                }
            }
        }
    }

    private func navigateTo(_ path: String) {
        currentPath = path
        if historyIndex < history.count - 1 {
            history.removeSubrange((historyIndex + 1)...)
        }
        history.append(path)
        historyIndex = history.count - 1
        loadFiles()
    }

    private func goBack() {
        guard historyIndex > 0 else { return }
        historyIndex -= 1
        currentPath = history[historyIndex]
        loadFiles()
    }

    private func goForward() {
        guard historyIndex < history.count - 1 else { return }
        historyIndex += 1
        currentPath = history[historyIndex]
        loadFiles()
    }

    private func goUp() {
        if currentPath == "/" || currentPath == "/sdcard" || currentPath.isEmpty { return }
        let components = currentPath.split(separator: "/")
        guard components.count > 1 else {
            navigateTo("/")
            return
        }
        let newPath = "/" + components.dropLast().joined(separator: "/")
        navigateTo(newPath)
    }

    private func refreshBoth() {
        refreshDevices()
        loadFiles()
    }

    private func uploadFiles() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.prompt = "Select Files to Upload"

        if panel.runModal() == .OK {
            let paths = panel.urls.map { $0.path }
            if isLocalDriveMode {
                for url in panel.urls {
                    let dest = (currentPath as NSString).appendingPathComponent(url.lastPathComponent)
                    try? FileManager.default.copyItem(atPath: url.path, toPath: dest)
                    LoggerService.shared.log("Uploaded \(url.lastPathComponent) to phone \(dest)")
                }
                loadFiles()
            } else {
                CopyEngine.shared.enqueueUploadBatch(serial: selectedSerial, localPaths: paths, remoteDestinationDir: currentPath)
            }
        }
    }


    private func formatBytes(_ bytes: Int64) -> String {
        let b = Double(bytes)
        if b < 1024 {
            return "\(bytes) B"
        } else if b < 1024 * 1024 {
            return String(format: "%.1f KB", b / 1024)
        } else if b < 1024 * 1024 * 1024 {
            return String(format: "%.1f MB", b / (1024 * 1024))
        } else {
            return String(format: "%.2f GB", b / (1024 * 1024 * 1024))
        }
    }

    private func createNewFolder(name: String) {
        let newFolderPath = (currentPath as NSString).appendingPathComponent(name)
        if isLocalDriveMode {
            try? FileManager.default.createDirectory(atPath: newFolderPath, withIntermediateDirectories: true)
            LoggerService.shared.log("Created folder on phone: \(newFolderPath)")
            loadFiles()
        } else {
            Task {
                do {
                    try await AdbService.shared.createDirectory(serial: selectedSerial, remotePath: newFolderPath)
                    LoggerService.shared.log("Created folder on phone: \(name)")
                    loadFiles()
                } catch {
                    LoggerService.shared.log("Failed to create folder on phone: \(error.localizedDescription)")
                }
            }
        }
    }

    private func prepareRename() {
        if let id = selectedItems.first, let item = items.first(where: { $0.id == id }) {
            newRenameName = item.name
            showRenameAlert = true
        }
    }

    private func executeRename() {
        guard let id = selectedItems.first, let item = items.first(where: { $0.id == id }) else { return }
        let newName = newRenameName.trimmingCharacters(in: .whitespacesAndNewlines)
        if newName.isEmpty || newName == item.name { return }
        let oldPath = item.path
        let newPath = (currentPath as NSString).appendingPathComponent(newName)

        if isLocalDriveMode {
            try? FileManager.default.moveItem(atPath: oldPath, toPath: newPath)
            LoggerService.shared.log("Renamed item on phone: \(newName)")
            loadFiles()
        } else {
            Task {
                do {
                    _ = try await AdbService.shared.executeCommand(args: ["-s", selectedSerial, "shell", "mv", "'\(oldPath)'", "'\(newPath)'"])
                    LoggerService.shared.log("Renamed item on phone: \(newName)")
                    loadFiles()
                } catch {
                    LoggerService.shared.log("Failed to rename on phone: \(error.localizedDescription)")
                }
            }
        }
    }

    private func executeDelete() {
        let itemsToDelete = items.filter { selectedItems.contains($0.id) }
        guard !itemsToDelete.isEmpty else { return }

        Task {
            var anyDeleted = false
            for item in itemsToDelete {
                if isLocalDriveMode {
                    try? FileManager.default.removeItem(atPath: item.path)
                    anyDeleted = true
                } else {
                    do {
                        try await AdbService.shared.deleteFile(serial: selectedSerial, remotePath: item.path, isDirectory: item.isDirectory)
                        anyDeleted = true
                    } catch {
                        LoggerService.shared.log("Failed to delete \(item.name): \(error.localizedDescription)")
                    }
                }
            }
            if anyDeleted {
                LoggerService.shared.log("Deleted \(itemsToDelete.count) items on phone")
                selectedItems.removeAll()
                loadFiles()
            }
        }
    }
}
