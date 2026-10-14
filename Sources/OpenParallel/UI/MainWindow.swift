import SwiftUI
import UniformTypeIdentifiers

struct MainWindow: View {
    @EnvironmentObject private var state: AppState
    @Environment(\.openWindow) private var openWindow
    @State private var search = ""

    var body: some View {
        NavigationSplitView {
            Sidebar(search: search)
                .navigationSplitViewColumnWidth(min: 210, ideal: 240, max: 320)
        } detail: {
            DetailRouter()
        }
        .searchable(text: $search, placement: .sidebar, prompt: "Search")
        .modifier(SearchFocus(request: state.searchFocusRequest))
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    state.requestNewInstance()
                } label: {
                    Label("New Instance", systemImage: "plus")
                }
                .help("New Instance")
            }
        }
        .sheet(item: $state.newInstance) { request in
            NewInstanceSheet(request: request)
        }
        .confirmationDialog(
            deletionTitle,
            isPresented: Binding(get: { state.pendingDeletion != nil }, set: { if !$0 { state.pendingDeletion = nil } }),
            titleVisibility: .visible,
            presenting: state.pendingDeletion
        ) { record in
            Button("Move to Trash", role: .destructive) { state.delete(record) }
            Button("Cancel", role: .cancel) {}
        } message: { record in
            Text(record.isWebsite
                ? "Its app, sign-ins and website data move to the Trash."
                : "Its app and its data folder, including any account signed in to it, move to the Trash. \(record.sourceName) itself is not touched.")
        }
        .alert(
            "Something went wrong",
            isPresented: Binding(get: { state.alertMessage != nil }, set: { if !$0 { state.alertMessage = nil } })
        ) {
            Button("OK") {}
        } message: {
            Text(state.alertMessage ?? "")
        }
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            AppDrop.load(providers) { url in state.requestNewInstance(NewInstanceRequest(source: url)) }
        }
        .onAppear {
            state.openMainWindow = { openWindow(id: "main") }
            state.refresh()
        }
    }

    private var deletionTitle: String {
        "Move \u{201C}\(state.pendingDeletion?.instanceName ?? "")\u{201D} to the Trash?"
    }
}

/// Moves the cursor into the sidebar search when Edit › Find is chosen.
/// Focusing a search field from code needs macOS 15; on 14 Find is disabled.
private struct SearchFocus: ViewModifier {
    let request: Int
    @FocusState private var focused: Bool

    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content
                .searchFocused($focused)
                .onChange(of: request) { _, _ in focused = true }
        } else {
            content
        }
    }
}

/// Accepts a dropped .app from Finder.
enum AppDrop {
    static func load(_ providers: [NSItemProvider], handler: @escaping @MainActor (URL) -> Void) -> Bool {
        guard let provider = providers.first(where: { $0.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) }) else {
            return false
        }
        _ = provider.loadObject(ofClass: URL.self) { url, _ in
            guard let url, url.pathExtension.lowercased() == "app" else { return }
            Task { @MainActor in handler(url) }
        }
        return true
    }
}

// MARK: - Sidebar

private struct Sidebar: View {
    @EnvironmentObject private var state: AppState
    let search: String

    var body: some View {
        List(selection: $state.selection) {
            ForEach(groups, id: \.title) { group in
                Section(group.title) {
                    ForEach(group.records) { record in
                        SidebarRow(record: record, running: state.isRunning(record))
                            .tag(SidebarItem.instance(record.instanceSlug))
                    }
                }
            }
            if !legacy.isEmpty {
                Section("Older Shortcuts") {
                    ForEach(legacy, id: \.self) { url in
                        Label {
                            Text(url.deletingPathExtension().lastPathComponent)
                        } icon: {
                            Image(systemName: "exclamationmark.triangle")
                                .foregroundStyle(.orange)
                        }
                        .tag(SidebarItem.legacy(url))
                    }
                }
            }
        }
        .listStyle(.sidebar)
        // Right-click acts on the clicked row; double-click or Return opens it.
        .contextMenu(forSelectionType: SidebarItem.self) { items in
            switch items.first {
            case .instance(let slug)?:
                if let record = state.library.record(withSlug: slug) {
                    InstanceMenuItems(record: record)
                }
            case .legacy(let url)?:
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                Button("Move to Trash") { state.trashLegacyShortcut(url) }
            case nil:
                Button("New Instance\u{2026}") { state.requestNewInstance() }
                Button("New Website Instance\u{2026}") { state.requestNewInstance(NewInstanceRequest(kind: .web)) }
            }
        } primaryAction: { items in
            switch items.first {
            case .instance(let slug)?:
                if let record = state.library.record(withSlug: slug) { state.open(record) }
            case .legacy(let url)?:
                NSWorkspace.shared.activateFileViewerSelecting([url])
            case nil:
                break
            }
        }
    }

    private var filtered: [InstanceRecord] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return state.library.instances }
        return state.library.instances.filter {
            $0.instanceName.localizedCaseInsensitiveContains(query) || $0.sourceName.localizedCaseInsensitiveContains(query)
        }
    }

    private var legacy: [URL] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return state.library.legacyShortcuts }
        return state.library.legacyShortcuts.filter { $0.lastPathComponent.localizedCaseInsensitiveContains(query) }
    }

    /// One section per source app; websites together at the end.
    private var groups: [(title: String, records: [InstanceRecord])] {
        let grouped = Dictionary(grouping: filtered) { $0.isWebsite ? "" : $0.sourceName }
        var result = grouped
            .filter { !$0.key.isEmpty }
            .map { (title: $0.key, records: $0.value) }
            .sorted { $0.title.localizedStandardCompare($1.title) == .orderedAscending }
        if let websites = grouped[""] {
            result.append((title: "Websites", records: websites))
        }
        return result
    }
}

private struct SidebarRow: View {
    let record: InstanceRecord
    let running: Bool

    var body: some View {
        Label {
            HStack(spacing: 6) {
                Text(record.instanceName)
                    .lineLimit(1)
                Spacer(minLength: 4)
                if running {
                    Circle()
                        .fill(.green)
                        .frame(width: 6, height: 6)
                        .help("Running")
                        .accessibilityLabel("Running")
                }
            }
        } icon: {
            Image(nsImage: IconCache.icon(for: record))
                .resizable()
                .interpolation(.high)
                .frame(width: 18, height: 18)
        }
    }
}

/// The same actions in the context menu, the Instance menu and the menu bar.
struct InstanceMenuItems: View {
    @EnvironmentObject private var state: AppState
    let record: InstanceRecord

    var body: some View {
        Button("Open") { state.open(record) }
        if state.isRunning(record) {
            Button("Quit") { state.quit(record) }
        }
        Divider()
        Button("Show in Finder") { state.showInFinder(record) }
        Button("Show Data Folder") { state.showDataFolder(record) }
        Divider()
        Button("Move to Trash…") { state.confirmDelete(record) }
            .disabled(state.isRunning(record))
    }
}

// MARK: - Detail

private struct DetailRouter: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        switch state.selection {
        case .instance(let slug):
            if let record = state.library.record(withSlug: slug) {
                InstanceDetailView(record: record)
                    .id(record.instanceSlug)
            } else {
                noSelection
            }
        case .legacy(let url):
            LegacyShortcutView(url: url)
        case nil:
            if state.library.instances.isEmpty {
                EmptyLibraryView()
            } else {
                noSelection
            }
        }
    }

    private var noSelection: some View {
        ContentUnavailableView("No Instance Selected", systemImage: "sidebar.left",
                               description: Text("Select an instance in the sidebar."))
    }
}

private struct EmptyLibraryView: View {
    @EnvironmentObject private var state: AppState

    var body: some View {
        ContentUnavailableView {
            Label("No Instances", systemImage: "square.on.square")
        } description: {
            Text("An instance is a second copy of an app with its own login and data. The original app is never changed.")
        } actions: {
            Button("New Instance…") { state.requestNewInstance() }
                .buttonStyle(.borderedProminent)
            Button("New Website Instance…") { state.requestNewInstance(NewInstanceRequest(kind: .web)) }
        }
    }
}

private struct LegacyShortcutView: View {
    @EnvironmentObject private var state: AppState
    let url: URL

    var body: some View {
        Form {
            Section {
                HStack(spacing: 14) {
                    Image(nsImage: IconCache.icon(for: url))
                        .resizable()
                        .frame(width: 48, height: 48)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(url.deletingPathExtension().lastPathComponent)
                            .font(.title3.weight(.semibold))
                        Text("Made by an older version of OpenParallel")
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 4)
            }
            Section {
                Text("OpenParallel can\u{2019}t manage this shortcut, and it may no longer open. To keep using it, create a new instance of the same app, then move this one to the Trash.")
                    .foregroundStyle(.secondary)
                LabeledContent("Location") {
                    Text(url.path)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .textSelection(.enabled)
                }
            }
            Section {
                HStack {
                    Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                    Spacer()
                    Button("Move to Trash", role: .destructive) { state.trashLegacyShortcut(url) }
                }
            }
        }
        .formStyle(.grouped)
    }
}

extension InstanceRecord {
    /// The source app's name as the user knows it.
    var sourceName: String {
        if isWebsite { return webURL.flatMap { URL(string: $0)?.host } ?? "Website" }
        let url = URL(fileURLWithPath: sourcePath)
        return preferredAppName(at: url) ?? url.deletingPathExtension().lastPathComponent
    }
}
