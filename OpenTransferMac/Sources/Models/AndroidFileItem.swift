import Foundation

public enum FileType: String, Codable, Hashable {
    case folder = "Folder"
    case file = "File"
    case link = "Link"
}

public struct AndroidFileItem: Identifiable, Hashable, Codable {
    public var id: String { path }
    public var name: String
    public var path: String
    public var size: Int64
    public var type: FileType
    public var permissions: String
    public var modifiedDate: String

    public var isDirectory: Bool {
        return type == .folder
    }

    public var formattedSize: String {
        if isDirectory {
            return "--"
        }
        let b = Double(size)
        if b < 1024 {
            return "\(size) B"
        } else if b < 1024 * 1024 {
            return String(format: "%.1f KB", b / 1024)
        } else if b < 1024 * 1024 * 1024 {
            return String(format: "%.1f MB", b / (1024 * 1024))
        } else {
            return String(format: "%.2f GB", b / (1024 * 1024 * 1024))
        }
    }

    public init(name: String, path: String, size: Int64 = 0, type: FileType = .file, permissions: String = "", modifiedDate: String = "") {
        self.name = name
        self.path = path
        self.size = size
        self.type = type
        self.permissions = permissions
        self.modifiedDate = modifiedDate
    }
}
