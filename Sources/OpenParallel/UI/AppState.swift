import AppKit
import Combine
import SwiftUI

enum SidebarItem: Hashable {
    case instance(String)
    case legacy(URL)
}

/// What the New Instance sheet should start with.
struct NewInstanceRequest: Identifiable {
    let id = UUID()
    var kind: ShortcutKind = .app
    var source: URL?
    var webAddress: String?
    var name: String?
}

enum SettingsKey {
    static let shortcutsDirectory = "shortcutsDirectory"
    static let showMenuBarExtra = "showMenuBarExtra"
    static let openAfterCreating = "openAfterCreating"
}

@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()

    let library: InstanceLibrary
    let monitor = RunningMonitor()

    @Published var selection: SidebarItem?
    @Published var newInstance: NewInstanceRequest?
    @Published var pendingDeletion: InstanceRecord?
    @Published var alertMessage: String?
    /// Bumped by Edit › Find to move the cursor into the sidebar search.
    @Published var searchFocusRequest = 0
    /// Set by an instance's Report a Problem button; the report window takes it once.
    @Published var reportContext: ReportContext?

    /// Captured from the main window so the Dock and the menu bar can bring it back.
    var openMainWindow: (() -> Void)?

    private var subscriptions: Set<AnyCancellable> = []

    init() {
        let stored = UserDefaults.standard.string(forKey: SettingsKey.shortcutsDirectory)
        #if DEBUG
        // Lets a debug build show a sample library without touching real data.
        let environment = ProcessInfo.processInfo.environment
        let shortcuts = (environment["OPENPARALLEL_DEBUG_SHORTCUTS"] ?? stored).map { URL(fileURLWithPath: $0, isDirectory: true) }
        let instances = environment["OPENPARALLEL_DEBUG_INSTANCES"].map { URL(fileURLWithPath: $0, isDirectory: true) }
        #else
        let shortcuts = stored.map { URL(fileURLWithPath: $0, isDirectory: true) }
        let instances: URL? = nil
        #endif
        library = InstanceLibrary(shortcutsDirectory: shortcuts, dataRoot: instances)
        library.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }.store(in: &subscriptions)
        monitor.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }.store(in: &subscriptions)
        library.$instances.sink { [weak self] records in self?.monitor.track(records) }.store(in: &subscriptions)
    }

    var shortcutsDirectory: URL { library.shortcutsDirectory }

    var selectedRecord: InstanceRecord? {
        guard case .instance(let slug) = selection else { return nil }
        return library.record(withSlug: slug)
    }

    func isRunning(_ record: InstanceRecord) -> Bool { monitor.isRunning(record) }

    // MARK: Actions

    func requestNewInstance(_ request: NewInstanceRequest = NewInstanceRequest()) {
        showMainWindow()
        newInstance = request
    }

    func showMainWindow() {
        NSApp.activate(ignoringOtherApps: true)
        openMainWindow?()
    }

    func open(_ record: InstanceRecord) {
        if isRunning(record), monitor.activate(record) { return }
        guard record.wrapperURL != nil else {
            alertMessage = "The app for \u{201C}\(record.instanceName)\u{201D} is missing from the shortcuts folder."
            return
        }
        library.launch(record)
        monitor.refreshSoon()
    }

    func quit(_ record: InstanceRecord) {
        monitor.quit(record)
    }

    func showInFinder(_ record: InstanceRecord) {
        NSWorkspace.shared.activateFileViewerSelecting([record.wrapperURL ?? record.dataRoot])
    }

    func showDataFolder(_ record: InstanceRecord) {
        NSWorkspace.shared.activateFileViewerSelecting([record.dataRoot])
    }

    func confirmDelete(_ record: InstanceRecord) {
        pendingDeletion = record
    }

    func delete(_ record: InstanceRecord) {
        do {
            try library.delete(record)
            if selection == .instance(record.instanceSlug) { selection = nil }
        } catch {
            alertMessage = "\u{201C}\(record.instanceName)\u{201D} could not be moved to the Trash: \(error.localizedDescription)"
        }
    }

    func trashLegacyShortcut(_ url: URL) {
        do {
            try library.trashLegacyShortcut(url)
            if selection == .legacy(url) { selection = nil }
        } catch {
            alertMessage = "\u{201C}\(url.lastPathComponent)\u{201D} could not be moved to the Trash: \(error.localizedDescription)"
        }
    }

    func instanceCreated(at appURL: URL, open: Bool) {
        library.reload()
        let slug = displaySlug(appURL.deletingPathExtension().lastPathComponent)
        selection = .instance(slug)
        if open, let record = library.record(withSlug: slug) {
            self.open(record)
        }
    }

    func setShortcutsDirectory(_ url: URL) {
        UserDefaults.standard.set(url.path, forKey: SettingsKey.shortcutsDirectory)
        library.setShortcutsDirectory(url)
    }

    func refresh() {
        library.reload()
        monitor.refresh()
    }
}
