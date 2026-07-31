import SwiftUI
import AppKit

class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }
}

@main
struct OpenTransferApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        WindowGroup("OpenTransfer") {
            MainView()
                .frame(minWidth: 950, minHeight: 650)
        }
        .windowStyle(.titleBar)
        .windowToolbarStyle(.unified)
    }
}

