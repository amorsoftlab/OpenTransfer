import Foundation
import SwiftUI

public struct DeviceItem: Identifiable, Hashable, Codable {
    public var id: String { serial }
    public var serial: String
    public var name: String
    public var state: String
    public var model: String

    public var displayName: String {
        if !name.isEmpty {
            return "\(name) (\(serial))"
        } else if !model.isEmpty {
            return "\(model) (\(serial))"
        } else {
            return serial
        }
    }

    public init(serial: String, name: String = "", state: String = "device", model: String = "") {
        self.serial = serial
        self.name = name
        self.state = state
        self.model = model
    }
}

public enum TransferDirection: String, Codable {
    case download = "Download"
    case upload = "Upload"
}

public enum TransferStatus: String, Codable {
    case queued = "Queued"
    case transferring = "Transferring"
    case completed = "Completed"
    case failed = "Failed"
    case cancelled = "Cancelled"
}

public struct TransferQueueItem: Identifiable, Hashable {
    public let id = UUID()
    public var fileName: String
    public var sourcePath: String
    public var destinationPath: String
    public var direction: TransferDirection
    public var progress: Double // 0.0 to 1.0
    public var status: TransferStatus
    public var errorMessage: String?
    
    // Live Stats (v1.2.1)
    public var currentFileIndex: Int = 1
    public var totalFileCount: Int = 1
    public var bytesTransferred: Int64 = 0
    public var totalBytes: Int64 = 0
    public var speedMbPerSec: Double = 0.0
    public var etaSeconds: Int = 0

    public var remainingBytes: Int64 {
        return max(0, totalBytes - bytesTransferred)
    }

    public init(fileName: String, sourcePath: String, destinationPath: String, direction: TransferDirection, progress: Double = 0.0, status: TransferStatus = .queued, errorMessage: String? = nil) {
        self.fileName = fileName
        self.sourcePath = sourcePath
        self.destinationPath = destinationPath
        self.direction = direction
        self.progress = progress
        self.status = status
        self.errorMessage = errorMessage
    }
}

public struct AppSettings: Codable {
    // --- GENERAL & DEVICE ---
    public var customAdbPath: String
    public var refreshIntervalSeconds: Int
    public var defaultDownloadPath: String
    public var showHiddenFiles: Bool

    // --- APPEARANCE ---
    public var folderColor: String // "Yellow", "Blue", "Green", "Purple", "Red", "Orange"
    public var folderIconPack: String // "macos-native", "colorful", "fluent", "minimal", "opentransfer"
    
    // --- AUTO-SPLIT ON TRANSFER (v1.2.1) ---
    public var autoSplitOnTransfer: Bool
    public var autoSplitBatchSize: Int // 100, 250, 500, 1000
    public var autoSplitNamingFormat: String // "Hyphen", "Underscore", "RangeHyphen", "RangeUnderscore"

    public static var `default`: AppSettings {
        AppSettings(
            customAdbPath: "",
            refreshIntervalSeconds: 3,
            defaultDownloadPath: FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first?.path ?? "/Users",
            showHiddenFiles: false,
            folderColor: "Yellow",
            folderIconPack: "macos-native",
            autoSplitOnTransfer: false,
            autoSplitBatchSize: 500,
            autoSplitNamingFormat: "Hyphen"
        )
    }
}

