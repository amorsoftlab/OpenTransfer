import SwiftUI
import UniformTypeIdentifiers

public struct FileListView: View {
    public let items: [AndroidFileItem]
    public let currentPath: String
    public let selectedSerial: String
    @Binding var selection: Set<AndroidFileItem.ID>
    public let isGridView: Bool
    public let onNavigate: (String) -> Void
    public let onRefresh: () -> Void

    @ObservedObject var settingsService = SettingsService.shared
    @State private var isTargeted: Bool = false
    @State private var showConflictAlert = false
    @State private var conflictingPaths: [String] = []
    @State private var allDroppedPaths: [String] = []

    public init(
        items: [AndroidFileItem],
        currentPath: String,
        selectedSerial: String,
        selection: Binding<Set<AndroidFileItem.ID>>,
        isGridView: Bool = false,
        onNavigate: @escaping (String) -> Void,
        onRefresh: @escaping () -> Void
    ) {
        self.items = items
        self.currentPath = currentPath
        self.selectedSerial = selectedSerial
        self._selection = selection
        self.isGridView = isGridView
        self.onNavigate = onNavigate
        self.onRefresh = onRefresh
    }

    public var folderColor: Color {
        switch settingsService.settings.folderColor {
        case "Blue":   return .blue
        case "Green":  return .green
        case "Purple": return .purple
        case "Red":    return .red
        case "Orange": return .orange
        default:       return Color(red: 0.95, green: 0.75, blue: 0.1)
        }
    }

    public func folderIconName(for item: AndroidFileItem, index: Int = 0) -> String {
        guard item.isDirectory else { return "doc.fill" }
        switch settingsService.settings.folderIconPack {
        case "colorful":
            let variants = ["folder.fill", "folder.badge.plus", "folder.badge.person.crop",
                            "folder.badge.gearshape", "folder.badge.questionmark"]
            return variants[index % variants.count]
        case "fluent":      return "folder"
        case "minimal":     return "square"
        case "opentransfer": return "shippingbox.fill"
        default:            return "folder.fill" // macos-native
        }
    }

    public func folderIconColor(for item: AndroidFileItem, index: Int = 0) -> Color {
        guard item.isDirectory else { return .gray }
        switch settingsService.settings.folderIconPack {
        case "colorful":
            let colors: [Color] = [folderColor, .blue, .purple, .green, .orange, .pink]
            return colors[index % colors.count]
        case "fluent":       return Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0))
        case "minimal":      return .primary.opacity(0.6)
        case "opentransfer": return Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0))
        default:             return folderColor
        }
    }

    public var body: some View {
        ZStack {
            if isGridView {
                ScrollView {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 100, maximum: 120), spacing: 16)], spacing: 16) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { idx, item in
                            VStack(spacing: 8) {
                                Image(systemName: folderIconName(for: item, index: idx))
                                    .font(.system(size: 40))
                                    .foregroundColor(folderIconColor(for: item, index: idx))
                                Text(item.name)
                                    .font(.caption)
                                    .multilineTextAlignment(.center)
                                    .lineLimit(2)
                                    .frame(maxWidth: .infinity)
                            }
                            .padding(8)
                            .background(selection.contains(item.id) ? Color.accentColor.opacity(0.2) : Color.clear)
                            .cornerRadius(8)
                            .contentShape(Rectangle())
                            .simultaneousGesture(TapGesture(count: 2).onEnded {
                                if item.isDirectory {
                                    onNavigate(item.path)
                                }
                            })
                            .simultaneousGesture(TapGesture(count: 1).onEnded {
                                // Basic single click selection
                                // Native modifier tracking for multi-select requires NSEvent which is complex here
                                selection = [item.id]
                            })
                            .contextMenu {
                                if item.isDirectory {
                                    Button("Open Folder") {
                                        onNavigate(item.path)
                                    }
                                }
                                Button("Download to Mac") {
                                    downloadItem(item: item)
                                }
                                Divider()
                                Button("Delete", role: .destructive) {
                                    deleteItem(item: item)
                                }
                            }
                        }
                    }
                    .padding()
                }
                .onTapGesture {
                    selection.removeAll()
                }
            } else {
                Table(items, selection: $selection) {
                    TableColumn("Name") { item in
                        let idx = items.firstIndex(where: { $0.id == item.id }) ?? 0
                        HStack(spacing: 8) {
                            Image(systemName: folderIconName(for: item, index: idx))
                                .foregroundColor(folderIconColor(for: item, index: idx))
                            Text(item.name)
                                .fontWeight(item.isDirectory ? .semibold : .regular)
                        }
                    }
                    .width(min: 200, ideal: 300)

                    TableColumn("Size") { item in
                        Text(item.formattedSize)
                            .foregroundColor(.secondary)
                    }
                    .width(min: 100, ideal: 140)

                    TableColumn("Modified Date") { item in
                        Text(item.modifiedDate)
                            .foregroundColor(.secondary)
                    }
                    .width(min: 160, ideal: 200)
                }
                .contextMenu(forSelectionType: AndroidFileItem.ID.self) { selectedIds in
                    if let id = selectedIds.first, let item = items.first(where: { $0.id == id }) {
                        if item.isDirectory {
                            Button("Open Folder") {
                                onNavigate(item.path)
                            }
                        }
                        Button("Download to Mac") {
                            downloadItem(item: item)
                        }
                        Divider()
                        Button("Delete", role: .destructive) {
                            deleteItem(item: item)
                        }
                    }
                } primaryAction: { selectedIds in
                    if let id = selectedIds.first, let item = items.first(where: { $0.id == id }) {
                        if item.isDirectory {
                            onNavigate(item.path)
                        }
                    }
                }
            }

            // Drag & Drop visual overlay when dragging files over table
            if isTargeted {
                RoundedRectangle(cornerRadius: 12)
                    .stroke(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)), lineWidth: 3)
                    .background(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 0.15)))
                    .overlay(
                        VStack(spacing: 8) {
                            Image(systemName: "square.and.arrow.down.fill")
                                .font(.system(size: 44))
                                .foregroundColor(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)))
                            Text("Drop Files Here to Upload to Phone")
                                .font(.headline)
                                .foregroundColor(Color(NSColor(red: 0.06, green: 0.73, blue: 0.51, alpha: 1.0)))
                        }
                    )
                    .allowsHitTesting(false)
            }
        }
        .onDrop(of: [.fileURL], isTargeted: $isTargeted) { providers in
            return handleDrop(providers: providers)
        }
        .alert("Items Already Exist", isPresented: $showConflictAlert) {
            Button("Replace All") {
                CopyEngine.shared.enqueueUploadBatch(serial: selectedSerial, localPaths: allDroppedPaths, remoteDestinationDir: currentPath)
            }
            Button("Skip Existing") {
                let existingNames = Set(items.map { $0.name })
                let nonConflicting = allDroppedPaths.filter { !existingNames.contains(($0 as NSString).lastPathComponent) }
                if !nonConflicting.isEmpty {
                    CopyEngine.shared.enqueueUploadBatch(serial: selectedSerial, localPaths: nonConflicting, remoteDestinationDir: currentPath)
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("One or more items you are trying to copy already exist in the destination. Do you want to replace the existing items or skip them?")
        }
    }

    private func handleDrop(providers: [NSItemProvider]) -> Bool {
        isTargeted = false
        guard !selectedSerial.isEmpty else { return false }
        let group = DispatchGroup()
        var droppedPaths: [String] = []
        let lock = NSLock()

        for provider in providers {
            group.enter()
            _ = provider.loadObject(ofClass: URL.self) { url, error in
                if let url = url {
                    lock.lock()
                    droppedPaths.append(url.path)
                    lock.unlock()
                }
                group.leave()
            }
        }

        group.notify(queue: .main) {
            if !droppedPaths.isEmpty {
                let existingNames = Set(self.items.map { $0.name })
                let conflicts = droppedPaths.filter { existingNames.contains(($0 as NSString).lastPathComponent) }

                if !conflicts.isEmpty {
                    self.allDroppedPaths = droppedPaths
                    self.conflictingPaths = conflicts
                    self.showConflictAlert = true
                } else {
                    CopyEngine.shared.enqueueUploadBatch(serial: self.selectedSerial, localPaths: droppedPaths, remoteDestinationDir: self.currentPath)
                    LoggerService.shared.log("Dropped \(droppedPaths.count) file(s) for upload.")
                }
            }
        }
        return true
    }

    private func downloadItem(item: AndroidFileItem) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.prompt = "Select Destination"

        if panel.runModal() == .OK, let url = panel.url {
            CopyEngine.shared.enqueueDownloadBatch(serial: selectedSerial, remoteItems: [item], localDestinationDir: url.path)
        }
    }


    private func deleteItem(item: AndroidFileItem) {
        Task {
            do {
                try await AdbService.shared.deleteFile(serial: selectedSerial, remotePath: item.path, isDirectory: item.isDirectory)
                LoggerService.shared.log("Deleted \(item.name)")
                onRefresh()
            } catch {
                LoggerService.shared.log("Failed to delete \(item.name): \(error.localizedDescription)")
            }
        }
    }
}
