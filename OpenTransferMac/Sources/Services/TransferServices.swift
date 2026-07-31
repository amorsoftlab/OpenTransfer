import Foundation
import Combine

public class SettingsService: ObservableObject {
    public static let shared = SettingsService()

    @Published public var settings: AppSettings {
        didSet {
            saveSettings()
        }
    }

    private let userDefaultsKey = "OpenTransferAppSettings"

    private init() {
        if let data = UserDefaults.standard.data(forKey: userDefaultsKey),
           let decoded = try? JSONDecoder().decode(AppSettings.self, from: data) {
            self.settings = decoded
        } else {
            self.settings = AppSettings.default
        }
    }

    private func saveSettings() {
        if let encoded = try? JSONEncoder().encode(settings) {
            UserDefaults.standard.set(encoded, forKey: userDefaultsKey)
        }
    }
}

public class LoggerService: ObservableObject {
    public static let shared = LoggerService()

    @Published public var logs: [String] = []

    private init() {
        log("OpenTransfer macOS (v1.2.2) started.")
        // Check for updates silently in the background on every launch
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
            UpdateService.shared.checkForUpdatesInBackground()
        }
    }

    public func log(_ message: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        let timestamp = formatter.string(from: Date())
        let formattedMsg = "[\(timestamp)] \(message)"

        DispatchQueue.main.async {
            self.logs.append(formattedMsg)
        }
    }

    public func clear() {
        DispatchQueue.main.async {
            self.logs.removeAll()
        }
    }
}

public class CopyEngine: ObservableObject {
    public static let shared = CopyEngine()

    @Published public var queue: [TransferQueueItem] = []
    @Published public var isTransferring: Bool = false
    @Published public var currentTransfer: TransferQueueItem?
    @Published public var completedCount: Int = 0

    private init() {}

    private func getSubfolderName(batchIndex: Int, batchSize: Int, format: String) -> String {
        let startIdx = ((batchIndex - 1) * batchSize) + 1
        let endIdx = batchIndex * batchSize

        switch format {
        case "Underscore":
            return "folder_\(batchIndex)"
        case "RangeHyphen":
            return "\(startIdx)-\(endIdx)"
        case "RangeUnderscore":
            return "\(startIdx)_\(endIdx)"
        default: // "Hyphen"
            return "folder-\(batchIndex)"
        }
    }

    @MainActor
    public func enqueueDownloadBatch(serial: String, remoteItems: [AndroidFileItem], localDestinationDir: String) {
        let settings = SettingsService.shared.settings
        let totalCount = remoteItems.count
        let shouldSplit = settings.autoSplitOnTransfer && settings.autoSplitBatchSize > 0 && totalCount > settings.autoSplitBatchSize

        for (idx, remoteItem) in remoteItems.enumerated() {
            var destinationDir = localDestinationDir
            if shouldSplit {
                let batchIndex = (idx / settings.autoSplitBatchSize) + 1
                let subFolder = getSubfolderName(batchIndex: batchIndex, batchSize: settings.autoSplitBatchSize, format: settings.autoSplitNamingFormat)
                destinationDir = (localDestinationDir as NSString).appendingPathComponent(subFolder)
                try? FileManager.default.createDirectory(atPath: destinationDir, withIntermediateDirectories: true)
            }

            let destinationPath = (destinationDir as NSString).appendingPathComponent(remoteItem.name)
            var item = TransferQueueItem(
                fileName: remoteItem.name,
                sourcePath: remoteItem.path,
                destinationPath: destinationPath,
                direction: .download
            )
            item.totalBytes = remoteItem.size
            item.currentFileIndex = idx + 1
            item.totalFileCount = totalCount

            self.queue.append(item)
            LoggerService.shared.log("Queued download: \(remoteItem.name) -> \(destinationPath)")
        }

        processQueue(serial: serial)
    }

    @MainActor
    public func enqueueUploadBatch(serial: String, localPaths: [String], remoteDestinationDir: String) {
        let settings = SettingsService.shared.settings
        let totalCount = localPaths.count
        let shouldSplit = settings.autoSplitOnTransfer && settings.autoSplitBatchSize > 0 && totalCount > settings.autoSplitBatchSize

        for (idx, localPath) in localPaths.enumerated() {
            let fileName = (localPath as NSString).lastPathComponent
            var effectiveRemoteDir = remoteDestinationDir

            if shouldSplit {
                let batchIndex = (idx / settings.autoSplitBatchSize) + 1
                let subFolder = getSubfolderName(batchIndex: batchIndex, batchSize: settings.autoSplitBatchSize, format: settings.autoSplitNamingFormat)
                effectiveRemoteDir = (remoteDestinationDir as NSString).appendingPathComponent(subFolder)
            }

            let destinationPath = (effectiveRemoteDir as NSString).appendingPathComponent(fileName)
            let fileAttributes = try? FileManager.default.attributesOfItem(atPath: localPath)
            let fileSize = (fileAttributes?[.size] as? Int64) ?? 0

            var item = TransferQueueItem(
                fileName: fileName,
                sourcePath: localPath,
                destinationPath: destinationPath,
                direction: .upload
            )
            item.totalBytes = fileSize
            item.currentFileIndex = idx + 1
            item.totalFileCount = totalCount

            self.queue.append(item)
            LoggerService.shared.log("Queued upload: \(fileName) -> \(destinationPath)")
        }

        processQueue(serial: serial)
    }


    private func processQueue(serial: String) {
        guard !isTransferring else { return }
        isTransferring = true

        Task {
            let startTime = Date()
            
            while true {
                guard let index = await MainActor.run(body: {
                    return self.queue.firstIndex(where: { $0.status == .queued })
                }) else {
                    break
                }

                let item = await MainActor.run(body: { () -> TransferQueueItem in
                    self.queue[index].status = .transferring
                    self.queue[index].progress = 0.1
                    self.currentTransfer = self.queue[index]
                    return self.queue[index]
                })

                LoggerService.shared.log("Starting \(item.direction.rawValue): \(item.fileName)")

                do {
                    if item.direction == .download {
                        try await AdbService.shared.pullFile(serial: serial, remotePath: item.sourcePath, localPath: item.destinationPath)
                    } else {
                        var isDirectory: ObjCBool = false
                        FileManager.default.fileExists(atPath: item.sourcePath, isDirectory: &isDirectory)

                        if isDirectory.boolValue {
                            try await AdbService.shared.createDirectory(serial: serial, remotePath: item.destinationPath)
                            try await AdbService.shared.pushFile(serial: serial, localPath: item.sourcePath + "/.", remotePath: item.destinationPath + "/")
                        } else {
                            let targetDir = (item.destinationPath as NSString).deletingLastPathComponent
                            try await AdbService.shared.createDirectory(serial: serial, remotePath: targetDir)
                            try await AdbService.shared.pushFile(serial: serial, localPath: item.sourcePath, remotePath: item.destinationPath)
                        }
                    }

                    let elapsed = Date().timeIntervalSince(startTime)
                    let speedMb = elapsed > 0 ? (Double(item.totalBytes) / (1024 * 1024)) / elapsed : 0.0

                    await MainActor.run {
                        if index < self.queue.count {
                            self.queue[index].progress = 1.0
                            self.queue[index].status = .completed
                            self.queue[index].bytesTransferred = item.totalBytes
                            self.queue[index].speedMbPerSec = speedMb
                            self.currentTransfer = self.queue[index]
                        }
                    }
                    LoggerService.shared.log("Successfully completed \(item.fileName)")
                } catch {
                    let errStr = error.localizedDescription
                    await MainActor.run {
                        if index < self.queue.count {
                            self.queue[index].status = .failed
                            self.queue[index].errorMessage = errStr
                            self.currentTransfer = self.queue[index]
                        }
                    }
                    LoggerService.shared.log("Failed \(item.fileName): \(errStr)")
                }
            }

            await MainActor.run {
                self.isTransferring = false
                self.currentTransfer = nil
                self.completedCount += 1
            }
        }
    }
}
