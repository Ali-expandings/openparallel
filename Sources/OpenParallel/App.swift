import AppKit
import SwiftUI

// Headless commands exercise the same launcher pipeline as the app. Vendor app
// bundles are never modified.
@main
struct OpenParallelMain {
    static func main() {
        switch CommandLine.arguments.dropFirst().first {
        case "build":
            BuildCLI.run(arguments: Array(CommandLine.arguments.dropFirst(2)))
        case "inspect":
            InspectCLI.run(arguments: Array(CommandLine.arguments.dropFirst(2)))
        case "web-build":
            WebBuildCLI.run(arguments: Array(CommandLine.arguments.dropFirst(2)))
        case "web-run":
            WebRunner.run(arguments: Array(CommandLine.arguments.dropFirst(2)))
        default:
            OpenParallelApp.main()
        }
    }
}

struct OpenParallelApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var state = AppState.shared
    @AppStorage(SettingsKey.showMenuBarExtra) private var showMenuBarExtra = true

    var body: some Scene {
        Window("OpenParallel", id: "main") {
            MainWindow()
                .environmentObject(state)
                .frame(minWidth: 720, minHeight: 460)
        }
        .defaultSize(width: 940, height: 620)
        .commands { OpenParallelCommands(state: state) }

        Settings {
            SettingsView()
                .environmentObject(state)
        }

        Window("Report a Problem", id: "report") {
            ReportProblemView()
                .environmentObject(state)
        }
        .defaultSize(width: 600, height: 760)
        .windowResizability(.contentMinSize)

        MenuBarExtra(isInserted: $showMenuBarExtra) {
            MenuBarMenu()
                .environmentObject(state)
        } label: {
            // The symbol's own accessibility name is "Tabs"; name the image so
            // VoiceOver reads the menu bar item as OpenParallel.
            Image(nsImage: NSImage(systemSymbolName: "square.on.square", accessibilityDescription: "OpenParallel")
                ?? NSImage(named: NSImage.applicationIconName)!)
        }
    }
}

/// Dropping an app on the Dock icon starts a new instance of it.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func application(_ application: NSApplication, open urls: [URL]) {
        guard let app = urls.first(where: { $0.pathExtension.lowercased() == "app" }) else { return }
        AppState.shared.requestNewInstance(NewInstanceRequest(source: app))
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { AppState.shared.showMainWindow() }
        return true
    }
}

/// The standard About window, with who made OpenParallel and their links under the version.
@MainActor
enum AboutPanel {
    static func show() {
        NSApp.orderFrontStandardAboutPanel(options: credits(ProjectInfo.current).map { [.credits: $0] } ?? [:])
    }

    static func credits(_ project: ProjectInfo) -> NSAttributedString? {
        guard let signature = project.signature else { return nil }
        let centered = NSMutableParagraphStyle()
        centered.alignment = .center
        let plain: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: NSFont.smallSystemFontSize),
            .foregroundColor: NSColor.secondaryLabelColor,
            .paragraphStyle: centered,
        ]
        let text = NSMutableAttributedString(string: signature, attributes: plain)
        for (index, link) in project.authorLinks.enumerated() {
            text.append(NSAttributedString(string: index == 0 ? "\n" : "  \u{00B7}  ", attributes: plain))
            var linked = plain
            linked[.link] = link.url
            text.append(NSAttributedString(string: link.label, attributes: linked))
        }
        return text
    }
}

struct OpenParallelCommands: Commands {
    @ObservedObject var state: AppState
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        CommandGroup(replacing: .appInfo) {
            Button("About OpenParallel") { AboutPanel.show() }
        }

        CommandGroup(replacing: .newItem) {
            Button("New Instance\u{2026}") { state.requestNewInstance() }
                .keyboardShortcut("n")
            Button("New Website Instance\u{2026}") { state.requestNewInstance(NewInstanceRequest(kind: .web)) }
                .keyboardShortcut("n", modifiers: [.command, .shift])
        }

        SidebarCommands()

        CommandGroup(after: .pasteboard) {
            Divider()
            Button("Find Instance\u{2026}") { state.searchFocusRequest += 1 }
                .keyboardShortcut("f")
                .disabled(!ProcessInfo.processInfo.isOperatingSystemAtLeast(OperatingSystemVersion(majorVersion: 15, minorVersion: 0, patchVersion: 0)))
        }

        CommandGroup(after: .sidebar) {
            Button("Refresh") { state.refresh() }
                .keyboardShortcut("r")
            Divider()
        }

        CommandGroup(replacing: .help) {
            Button("Report a Problem\u{2026}") { openWindow(id: "report") }
        }

        CommandMenu("Instance") {
            let record = state.selectedRecord
            let running = record.map(state.isRunning) ?? false
            Button(running ? "Show" : "Open") { record.map(state.open) }
                .keyboardShortcut("o")
                .disabled(record == nil)
            Button("Quit Instance") { record.map(state.quit) }
                .keyboardShortcut(".", modifiers: .command)
                .disabled(!running)
            Divider()
            Button("Show in Finder") { record.map(state.showInFinder) }
                .keyboardShortcut("r", modifiers: [.command, .shift])
                .disabled(record == nil)
            Button("Show Data Folder") { record.map(state.showDataFolder) }
                .keyboardShortcut("d", modifiers: [.command, .shift])
                .disabled(record == nil)
            Divider()
            Button("Move to Trash\u{2026}") { record.map(state.confirmDelete) }
                .keyboardShortcut(.delete, modifiers: .command)
                .disabled(record == nil || running)
            Divider()
            Button("Report a Problem with This Instance\u{2026}") {
                if let record {
                    state.reportContext = ReportContext(app: record.isWebsite ? record.instanceName : record.sourceName,
                                                        version: record.sourceVersionAtCreation, detail: record.family)
                }
                openWindow(id: "report")
            }
            .disabled(record == nil)
        }
    }
}
