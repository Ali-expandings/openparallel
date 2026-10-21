import SwiftUI

struct SettingsView: View {
    @Environment(\.openWindow) private var openWindow
    @EnvironmentObject private var state: AppState
    @AppStorage(SettingsKey.showMenuBarExtra) private var showMenuBarExtra = true
    @AppStorage(SettingsKey.openAfterCreating) private var openAfterCreating = true
    private let project = ProjectInfo.current

    var body: some View {
        Form {
            Section {
                LabeledContent("Shortcuts folder") {
                    HStack(spacing: 8) {
                        Text(state.shortcutsDirectory.path.replacingOccurrences(of: FileManager.default.homeDirectoryForCurrentUser.path, with: "~"))
                            .lineLimit(1)
                            .truncationMode(.middle)
                            .foregroundStyle(.secondary)
                        Button("Choose\u{2026}") { chooseFolder() }
                    }
                }
            } footer: {
                Text("Each instance is a small app in this folder. Drag one to the Dock to keep it there.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }

            Section {
                Toggle("Show OpenParallel in the menu bar", isOn: $showMenuBarExtra)
                Toggle("Open new instances after creating them", isOn: $openAfterCreating)
            }

            Section {
                Text("OpenParallel sends nothing anywhere, runs nothing in the background, and never changes the apps you copy.")
                    .foregroundStyle(.secondary)
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "development build")
                if let signature = project.signature {
                    LabeledContent(signature) {
                        HStack(spacing: 12) {
                            ForEach(project.authorLinks) { Link($0.label, destination: $0.url) }
                        }
                    }
                }
                LabeledContent("Something wrong?") {
                    Button("Report a Problem\u{2026}") { openWindow(id: "report") }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 480)
        .fixedSize(horizontal: false, vertical: true)
    }

    private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.directoryURL = state.shortcutsDirectory
        panel.prompt = "Use Folder"
        if panel.runModal() == .OK, let url = panel.url {
            state.setShortcutsDirectory(url)
        }
    }
}

struct MenuBarMenu: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        let running = state.library.instances.filter(state.isRunning)
        let idle = state.library.instances.filter { !state.isRunning($0) }

        if state.library.instances.isEmpty {
            Text("No instances yet")
        }
        if !running.isEmpty {
            Section("Running") {
                ForEach(running) { record in
                    Button(record.instanceName) { state.open(record) }
                }
            }
        }
        if !idle.isEmpty {
            Section(running.isEmpty ? "Instances" : "Not Running") {
                ForEach(idle) { record in
                    Button(record.instanceName) { state.open(record) }
                }
            }
        }
        Divider()
        Button("New Instance\u{2026}") { state.requestNewInstance() }
        Button("Open OpenParallel") { state.showMainWindow() }
        SettingsLink { Text("Settings\u{2026}") }
        Divider()
        Button("Quit OpenParallel") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }
}
