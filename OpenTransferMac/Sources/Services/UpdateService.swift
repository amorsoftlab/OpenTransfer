import Foundation
import AppKit

/// UpdateService checks GitHub Releases API on app launch and alerts the user
/// if a newer version of OpenTransfer macOS is available.
public class UpdateService {
    public static let shared = UpdateService()

    // The current version of this app build (keep in sync with build_release_mac.sh)
    public static let currentVersion = "1.2.1"

    private let githubApiURL = "https://api.github.com/repos/amorsoftlab/OpenTransfer/releases/latest"
    private let releasesPageURL = "https://github.com/amorsoftlab/OpenTransfer/releases/latest"

    private init() {}

    /// Call this once at app startup to silently check for updates.
    public func checkForUpdatesInBackground() {
        Task.detached(priority: .background) { [weak self] in
            guard let self = self else { return }
            await self.performUpdateCheck(silent: true)
        }
    }

    /// Call this when the user explicitly taps "Check for Updates" in Settings.
    public func checkForUpdatesManually() {
        Task.detached(priority: .userInitiated) { [weak self] in
            guard let self = self else { return }
            await self.performUpdateCheck(silent: false)
        }
    }

    private func performUpdateCheck(silent: Bool) async {
        guard let url = URL(string: githubApiURL) else { return }

        do {
            var request = URLRequest(url: url, timeoutInterval: 10)
            request.setValue("application/vnd.github.v3+json", forHTTPHeaderField: "Accept")
            // GitHub requires User-Agent for its API
            request.setValue("OpenTransfer-macOS/\(UpdateService.currentVersion)", forHTTPHeaderField: "User-Agent")

            let (data, _) = try await URLSession.shared.data(for: request)

            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tagName = json["tag_name"] as? String else {
                if !silent {
                    await showAlert(title: "Update Check Failed", message: "Could not parse update information from GitHub.", isError: true)
                }
                return
            }

            // Strip 'v' prefix to compare versions
            let latestVersion = tagName.trimmingCharacters(in: CharacterSet(charactersIn: "v "))
            let currentVersion = UpdateService.currentVersion

            LoggerService.shared.log("Update check: current=\(currentVersion), latest=\(latestVersion)")

            if isNewerVersion(latestVersion, than: currentVersion) {
                await showUpdateAvailableAlert(latestVersion: latestVersion, tagName: tagName)
            } else if !silent {
                await showAlert(
                    title: "You're Up to Date",
                    message: "OpenTransfer \(currentVersion) is the latest version.",
                    isError: false
                )
            }
        } catch {
            if !silent {
                await showAlert(title: "Update Check Failed", message: "Network error: \(error.localizedDescription)", isError: true)
            }
        }
    }

    @MainActor
    private func showUpdateAvailableAlert(latestVersion: String, tagName: String) {
        let alert = NSAlert()
        alert.messageText = "Update Available"
        alert.informativeText = "OpenTransfer \(latestVersion) is now available.\nYou are currently running version \(UpdateService.currentVersion).\n\nWould you like to download the latest version?"
        alert.alertStyle = .informational
        alert.addButton(withTitle: "Download Update")
        alert.addButton(withTitle: "Later")

        let response = alert.runModal()
        if response == .alertFirstButtonReturn {
            if let url = URL(string: "https://github.com/amorsoftlab/OpenTransfer/releases/tag/\(tagName)") {
                NSWorkspace.shared.open(url)
            }
        }
    }

    @MainActor
    private func showAlert(title: String, message: String, isError: Bool) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.alertStyle = isError ? .warning : .informational
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }

    /// Compare two version strings like "1.2.1" vs "1.3.0"
    private func isNewerVersion(_ candidate: String, than current: String) -> Bool {
        let candidateParts = candidate.split(separator: ".").compactMap { Int($0) }
        let currentParts = current.split(separator: ".").compactMap { Int($0) }

        let maxLen = max(candidateParts.count, currentParts.count)
        for i in 0..<maxLen {
            let a = i < candidateParts.count ? candidateParts[i] : 0
            let b = i < currentParts.count ? currentParts[i] : 0
            if a > b { return true }
            if a < b { return false }
        }
        return false // equal
    }
}
