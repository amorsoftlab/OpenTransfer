import Foundation

public class LocalFileService {
    public static let shared = LocalFileService()

    private init() {}

    public func listFiles(atPath path: String, showHidden: Bool = false) -> [AndroidFileItem] {
        let fileManager = FileManager.default
        let url = URL(fileURLWithPath: path)

        guard let contents = try? fileManager.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isDirectoryKey, .fileSizeKey, .contentModificationDateKey], options: showHidden ? [] : [.skipsHiddenFiles]) else {
            return []
        }

        var items: [AndroidFileItem] = []

        for fileUrl in contents {
            let resourceValues = try? fileUrl.resourceValues(forKeys: [.isDirectoryKey, .fileSizeKey, .contentModificationDateKey])
            let isDir = resourceValues?.isDirectory ?? false
            let size = Int64(resourceValues?.fileSize ?? 0)

            let dateString: String
            if let modDate = resourceValues?.contentModificationDate {
                let formatter = DateFormatter()
                formatter.dateFormat = "yyyy-MM-dd HH:mm"
                dateString = formatter.string(from: modDate)
            } else {
                dateString = ""
            }

            let item = AndroidFileItem(
                name: fileUrl.lastPathComponent,
                path: fileUrl.path,
                size: isDir ? 0 : size,
                type: isDir ? .folder : .file,
                permissions: isDir ? "drwxr-xr-x" : "-rw-r--r--",
                modifiedDate: dateString
            )
            items.append(item)
        }

        return items.sorted { a, b in
            if a.isDirectory != b.isDirectory {
                return a.isDirectory && !b.isDirectory
            }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
    }

    public func getMountedVolumes() -> [String] {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsRemovableKey]
        let paths = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes])
        return paths?.map { $0.path } ?? ["/Volumes"]
    }
}
