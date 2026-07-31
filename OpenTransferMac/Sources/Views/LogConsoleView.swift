import SwiftUI

public struct LogConsoleView: View {
    @ObservedObject var logger = LoggerService.shared
    @Binding var isExpanded: Bool

    public var body: some View {
        VStack(spacing: 0) {
            HStack {
                Image(systemName: "terminal.fill")
                    .foregroundColor(.accentColor)
                Text("Activity Log")
                    .font(.headline)
                Spacer()
                
                Button(action: {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(logger.logs.joined(separator: "\n"), forType: .string)
                    logger.log("Logs copied to clipboard.")
                }) {
                    Label("Copy", systemImage: "doc.on.doc")
                }
                .buttonStyle(.borderless)
                
                Button(action: {
                    logger.clear()
                }) {
                    Label("Clear", systemImage: "trash")
                }
                .buttonStyle(.borderless)

                Button(action: {
                    withAnimation {
                        isExpanded.toggle()
                    }
                }) {
                    Image(systemName: isExpanded ? "chevron.down" : "chevron.up")
                }
                .buttonStyle(.borderless)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(Color(NSColor.windowBackgroundColor))

            if isExpanded {
                Divider()
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 4) {
                            ForEach(Array(logger.logs.enumerated()), id: \.offset) { idx, logItem in
                                Text(logItem)
                                    .font(.system(.caption, design: .monospaced))
                                    .foregroundColor(.secondary)
                                    .id(idx)
                            }
                        }
                        .padding(8)
                    }
                    .background(Color(NSColor.textBackgroundColor))
                    .onChange(of: logger.logs.count) { newCount in
                        if newCount > 0 {
                            proxy.scrollTo(newCount - 1, anchor: .bottom)
                        }
                    }
                }
                .frame(height: 120)
            }
        }
    }
}
