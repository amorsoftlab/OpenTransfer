import Foundation

public class AdbService {
    public static let shared = AdbService()

    public var customAdbPath: String = ""

    private init() {}

    private func findAdbPath() -> String {
        if !customAdbPath.isEmpty, FileManager.default.fileExists(atPath: customAdbPath) {
            return customAdbPath
        }

        let home = NSHomeDirectory()
        let defaultLocations = [
            "\(home)/Library/Android/sdk/platform-tools/adb",
            "/opt/homebrew/bin/adb",
            "/usr/local/bin/adb",
            "/usr/bin/adb"
        ]

        for path in defaultLocations {
            if FileManager.default.fileExists(atPath: path) {
                return path
            }
        }

        // Try discovering via shell
        let process = Process()
        let pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: "/bin/zsh")
        process.arguments = ["-l", "-c", "which adb"]
        process.standardOutput = pipe

        try? process.run()
        process.waitUntilExit()

        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        let discovered = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if !discovered.isEmpty, FileManager.default.fileExists(atPath: discovered) {
            return discovered
        }

        return "\(home)/Library/Android/sdk/platform-tools/adb"
    }


    public func executeCommand(args: [String]) async throws -> String {
        return try await Task.detached {
            let process = Process()
            let pipe = Pipe()

            process.executableURL = URL(fileURLWithPath: self.findAdbPath())
            process.arguments = args
            process.standardOutput = pipe
            process.standardError = pipe

            try process.run()
            process.waitUntilExit()

            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            let output = String(data: data, encoding: .utf8) ?? ""
            return output.trimmingCharacters(in: .whitespacesAndNewlines)
        }.value
    }

    public func restartAdbServer() async {
        _ = try? await executeCommand(args: ["kill-server"])
        _ = try? await executeCommand(args: ["start-server"])
    }

    public func isAdbAvailable() async -> Bool {
        do {
            let result = try await executeCommand(args: ["version"])
            return result.contains("Android Debug Bridge")
        } catch {
            return false
        }
    }


    public func getDevices() async -> [DeviceItem] {
        do {
            let output = try await executeCommand(args: ["devices", "-l"])
            var devices: [DeviceItem] = []
            let lines = output.components(separatedBy: .newlines)

            for line in lines {
                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                if trimmed.isEmpty || trimmed.hasPrefix("List of devices attached") {
                    continue
                }

                let parts = trimmed.components(separatedBy: .whitespaces).filter { !$0.isEmpty }
                if parts.count >= 2 {
                    let serial = parts[0]
                    let state = parts[1]

                    var model = ""
                    var deviceName = ""

                    for part in parts.dropFirst(2) {
                        if part.hasPrefix("model:") {
                            model = String(part.dropFirst(6))
                        } else if part.hasPrefix("device:") {
                            deviceName = String(part.dropFirst(7))
                        }
                    }

                    devices.append(DeviceItem(serial: serial, name: deviceName, state: state, model: model))
                }
            }
            return devices
        } catch {
            return []
        }
    }

    public func listFiles(serial: String, remotePath: String, showHidden: Bool = false) async -> [AndroidFileItem] {
        do {
            var pathToList = remotePath
            if !pathToList.hasSuffix("/") {
                pathToList += "/"
            }
            let sanitizedPath = pathToList.replacingOccurrences(of: "'", with: "\\'")
            let command = "ls -la '\(sanitizedPath)'"
            let output = try await executeCommand(args: ["-s", serial, "shell", command])

            var items: [AndroidFileItem] = []
            let lines = output.components(separatedBy: .newlines)

            for line in lines {
                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                if trimmed.isEmpty || trimmed.hasPrefix("total ") {
                    continue
                }

                let parsed = parseLsLine(line: trimmed, basePath: remotePath)
                if let item = parsed {
                    if !showHidden && item.name.hasPrefix(".") && item.name != "." && item.name != ".." {
                        continue
                    }
                    if item.name != "." && item.name != ".." {
                        items.append(item)
                    }
                }
            }


            return items.sorted { a, b in
                if a.isDirectory != b.isDirectory {
                    return a.isDirectory && !b.isDirectory
                }
                return a.name.localizedStandardCompare(b.name) == .orderedAscending
            }
        } catch {
            return []
        }
    }

    private func parseLsLine(line: String, basePath: String) -> AndroidFileItem? {
        let parts = line.components(separatedBy: .whitespaces).filter { !$0.isEmpty }
        guard parts.count >= 7 else { return nil }

        let permissions = parts[0]
        let isDir = permissions.hasPrefix("d")
        let isLink = permissions.hasPrefix("l")

        let fileType: FileType = isDir ? .folder : (isLink ? .link : .file)

        // Finding name part (handling spaces in filename)
        // typical format: drwxr-xr-x 2 root root 4096 2026-07-31 08:00 filename
        var dateIdx = -1
        for i in 0..<parts.count {
            if parts[i].contains(":") && parts[i].count == 5 { // e.g. 08:00
                dateIdx = i
                break
            }
        }

        var size: Int64 = 0
        var name = ""
        var modifiedDate = ""

        if dateIdx != -1 && dateIdx >= 2 {
            modifiedDate = "\(parts[dateIdx - 2]) \(parts[dateIdx - 1]) \(parts[dateIdx])"
            size = Int64(parts[dateIdx - 3]) ?? 0
            name = parts.dropFirst(dateIdx + 1).joined(separator: " ")
        } else {
            name = parts.dropFirst(6).joined(separator: " ")
        }

        if isLink, let arrowIdx = name.range(of: " -> ") {
            name = String(name[..<arrowIdx.lowerBound])
        }

        guard !name.isEmpty else { return nil }

        let fullPath = (basePath as NSString).appendingPathComponent(name)
        return AndroidFileItem(
            name: name,
            path: fullPath,
            size: size,
            type: fileType,
            permissions: permissions,
            modifiedDate: modifiedDate
        )
    }

    public func pullFile(serial: String, remotePath: String, localPath: String) async throws {
        _ = try await executeCommand(args: ["-s", serial, "pull", remotePath, localPath])
    }

    public func pushFile(serial: String, localPath: String, remotePath: String) async throws {
        _ = try await executeCommand(args: ["-s", serial, "push", localPath, remotePath])
    }

    public func deleteFile(serial: String, remotePath: String, isDirectory: Bool) async throws {
        let cmd = isDirectory ? "rm -rf '\(remotePath)'" : "rm -f '\(remotePath)'"
        _ = try await executeCommand(args: ["-s", serial, "shell", cmd])
    }

    public func createDirectory(serial: String, remotePath: String) async throws {
        _ = try await executeCommand(args: ["-s", serial, "shell", "mkdir -p '\(remotePath)'"])
    }
}
