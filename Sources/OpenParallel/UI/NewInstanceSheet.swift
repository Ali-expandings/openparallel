import SwiftUI
import UniformTypeIdentifiers

struct NewInstanceSheet: View {
    @EnvironmentObject private var state: AppState
    @Environment(\.dismiss) private var dismiss
    @AppStorage(SettingsKey.openAfterCreating) private var openAfterCreating = true

    @State private var kind: ShortcutKind
    @State private var source: InstalledApp?
    @State private var choosingApp: Bool

    // Instance settings
    @State private var name: String
    @State private var nameEdited: Bool
    @State private var separateData = true
    @State private var webAddress: String
    @State private var arguments = ""
    @State private var environment: [EnvironmentPair] = []
    @State private var alwaysNewProcess = true
    @State private var openInBackground = false
    @State private var openHidden = false
    @State private var showAdvanced = false

    @State private var creating = false
    @FocusState private var addressFocused: Bool
    @State private var errorText: String?

    init(request: NewInstanceRequest) {
        _kind = State(initialValue: request.kind == .web ? .web : .app)
        let app = request.source.flatMap(InstalledAppScanner.describe)
        _source = State(initialValue: app)
        _choosingApp = State(initialValue: app == nil)
        _webAddress = State(initialValue: request.webAddress ?? "")
        let initialName = request.name ?? app.map { ShortcutBuilder.defaultName(for: $0.url, kind: .app, in: AppState.shared.shortcutsDirectory) } ?? ""
        _name = State(initialValue: initialName)
        _nameEdited = State(initialValue: request.name != nil)
        _separateData = State(initialValue: app.map { $0.profile.supportsProfileIsolation || $0.profile.requiresProfileIsolation } ?? true)
    }

    var body: some View {
        VStack(spacing: 0) {
            Picker("Kind", selection: $kind) {
                Text("App").tag(ShortcutKind.app)
                Text("Website").tag(ShortcutKind.web)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            .help("App \u{2318}1 \u{00B7} Website \u{2318}2")
            .padding(.top, 16)
            .padding(.bottom, 4)
            .disabled(creating)

            Group {
                if kind == .web {
                    websiteForm
                } else if choosingApp || source == nil {
                    AppPicker(selection: $source) { continueWithApp() }
                } else if let source {
                    appForm(source)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)

            Divider()
            footer
        }
        .frame(width: 600, height: 560)
        .background { keyboardShortcuts }
        .onChange(of: kind) { _, _ in
            errorText = nil
            if !nameEdited { name = kind == .web ? suggestedWebName : source.map { ShortcutBuilder.defaultName(for: $0.url, kind: .app, in: state.shortcutsDirectory) } ?? "" }
        }
        .onChange(of: webAddress) { _, _ in
            if kind == .web, !nameEdited { name = suggestedWebName }
        }
    }

    /// Shortcuts without a visible button of their own.
    private var keyboardShortcuts: some View {
        Group {
            Button("App") { kind = .app }
                .keyboardShortcut("1", modifiers: .command)
            Button("Website") { kind = .web }
                .keyboardShortcut("2", modifiers: .command)
        }
        .disabled(creating)
        .opacity(0)
        .accessibilityHidden(true)
    }

    // MARK: Footer

    private var footer: some View {
        HStack(spacing: 10) {
            if kind == .app, !choosingApp, source != nil {
                Button("Back") { choosingApp = true; nameEdited = false; errorText = nil }
                    .keyboardShortcut("[", modifiers: .command)
                    .help("Back \u{2318}[")
                    .disabled(creating)
            }
            if let errorText {
                Label(errorText, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(.red)
                    .font(.callout)
                    .lineLimit(2)
            } else if creating {
                ProgressView().controlSize(.small)
                Text(creatingText).foregroundStyle(.secondary).font(.callout)
            }
            Spacer()
            Button("Cancel", role: .cancel) { dismiss() }
                .keyboardShortcut(.cancelAction)
                .disabled(creating)
            Button(primaryTitle) { primaryAction() }
                .keyboardShortcut(.defaultAction)
                .disabled(!canProceed || creating)
        }
        .padding(16)
    }

    private var primaryTitle: String {
        if kind == .app && (choosingApp || source == nil) { return "Continue" }
        #if OPENPARALLEL_PRIVATE
        if kind == .app, source?.profile.family == .extensionCopy { return PrivateExtensions.createButtonTitle }
        #endif
        return "Create"
    }

    private var creatingText: String {
        if kind == .app, source?.profile.requiresProfileIsolation == true {
            return "Copying the app. This can take a minute\u{2026}"
        }
        return "Creating\u{2026}"
    }

    private var canProceed: Bool {
        switch kind {
        case .web:
            return !name.trimmed.isEmpty && webURL != nil
        default:
            if choosingApp || source == nil { return source != nil }
            return !name.trimmed.isEmpty && source?.profile.multiInstance == true
        }
    }

    private func primaryAction() {
        if kind == .app && (choosingApp || source == nil) {
            continueWithApp()
        } else {
            create()
        }
    }

    private func continueWithApp() {
        guard let source else { return }
        if !nameEdited { name = ShortcutBuilder.defaultName(for: source.url, kind: .app, in: state.shortcutsDirectory) }
        separateData = source.profile.supportsProfileIsolation || source.profile.requiresProfileIsolation
        choosingApp = false
    }

    // MARK: App form

    private func appForm(_ app: InstalledApp) -> some View {
        let status = StatusPresentation(app.profile)
        let canSeparate = app.profile.supportsProfileIsolation && !app.profile.requiresProfileIsolation
        return Form {
            Section {
                HStack(spacing: 12) {
                    Image(nsImage: IconCache.icon(for: app.url))
                        .resizable()
                        .frame(width: 40, height: 40)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(app.name).font(.headline)
                        Text(app.version.isEmpty ? app.bundleIdentifier : "Version \(app.version)")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    StatusLabel(status: status)
                }
                Text(isolationText(app, status: status))
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                if !app.profile.multiInstance, app.bundleIdentifier.lowercased() == "net.whatsapp.whatsapp" {
                    Button("Create a WhatsApp Web Instance Instead") {
                        webAddress = "https://web.whatsapp.com"
                        name = "WhatsApp Web"
                        nameEdited = true
                        kind = .web
                    }
                }
            }

            if app.profile.multiInstance {
                Section {
                    TextField("Name", text: $name, prompt: Text("Instance name"))
                        .onChange(of: name) { _, _ in nameEdited = true }
                    Toggle("Separate login and data", isOn: $separateData)
                        .disabled(!canSeparate)
                    Toggle("Open after creating", isOn: $openAfterCreating)
                }

                Section {
                    DisclosureGroup(isExpanded: $showAdvanced) {
                        LabeledContent {
                            TextField("Launch arguments", text: $arguments, prompt: Text("--incognito"))
                                .labelsHidden()
                                .font(.body.monospaced())
                                .frame(maxWidth: 220)
                        } label: {
                            Text("Launch arguments")
                            Text("Extra flags passed to \(app.name) each time this instance opens.")
                        }
                        LabeledContent {
                            Button {
                                environment.append(EnvironmentPair())
                            } label: {
                                Label("Add", systemImage: "plus")
                            }
                        } label: {
                            Text("Environment variables")
                            Text("Settings for this instance only, for example LANG = ar_EG.UTF-8.")
                        }
                        ForEach($environment) { $pair in
                            HStack(spacing: 6) {
                                TextField("Name", text: $pair.key, prompt: Text("NAME"))
                                    .labelsHidden()
                                    .font(.body.monospaced())
                                Text("=").foregroundStyle(.secondary)
                                TextField("Value", text: $pair.value, prompt: Text("value"))
                                    .labelsHidden()
                                    .font(.body.monospaced())
                                Button {
                                    environment.removeAll { $0.id == pair.id }
                                } label: {
                                    Image(systemName: "minus.circle.fill").foregroundStyle(.secondary)
                                }
                                .buttonStyle(.borderless)
                                .help("Remove this variable")
                            }
                        }
                        Toggle(isOn: $alwaysNewProcess) {
                            Text("Always start a new process")
                            Text(separateData
                                ? "Always on: an instance with its own data runs as its own process."
                                : "When off and the app is already running, opening this shortcut switches to it.")
                        }
                        .disabled(separateData)
                        Toggle(isOn: $openInBackground) {
                            Text("Open in the background")
                            Text("Opens without taking you away from the app you are using.")
                        }
                        Toggle(isOn: $openHidden) {
                            Text("Open hidden")
                            Text("Opens with its windows hidden, as if you pressed \u{2318}H.")
                        }
                    } label: {
                        Button {
                            withAnimation(.snappy(duration: 0.2)) { showAdvanced.toggle() }
                        } label: {
                            Text("Advanced launch options")
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .keyboardShortcut("a", modifiers: [.command, .option])
                        .help(showAdvanced ? "Hide advanced options (\u{2325}\u{2318}A)" : "Show advanced options (\u{2325}\u{2318}A)")
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func isolationText(_ app: InstalledApp, status: StatusPresentation) -> String {
        #if OPENPARALLEL_PRIVATE
        if app.profile.family == .extensionCopy { return PrivateExtensions.isolationDescription }
        #endif
        return status.detail
    }

    // MARK: Website form

    private var websiteForm: some View {
        Form {
            Section {
                TextField("Address", text: $webAddress, prompt: Text("https://web.whatsapp.com"))
                    .focused($addressFocused)
                TextField("Name", text: $name, prompt: Text("Instance name"))
                    .onChange(of: name) { _, newValue in if newValue != suggestedWebName { nameEdited = true } }
                Toggle("Open after creating", isOn: $openAfterCreating)
            } footer: {
                Text(StatusPresentation.website.detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            Section("Common") {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 8)], spacing: 8) {
                    ForEach(WebPreset.all) { preset in
                        Button {
                            webAddress = preset.address
                            name = preset.name
                            nameEdited = true
                        } label: {
                            Text(preset.name).frame(maxWidth: .infinity)
                        }
                    }
                }
                .padding(.vertical, 4)
            }
        }
        .formStyle(.grouped)
        .onAppear { addressFocused = true }
    }

    private var webURL: URL? {
        var text = webAddress.trimmed
        guard !text.isEmpty else { return nil }
        if !text.contains("://") { text = "https://" + text }
        guard let url = URL(string: text), let scheme = url.scheme?.lowercased(),
              ["http", "https"].contains(scheme), url.host?.isEmpty == false else { return nil }
        return url
    }

    private var suggestedWebName: String {
        guard let host = webURL?.host else { return "" }
        let trimmed = host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
        return WebPreset.all.first { URL(string: $0.address)?.host == host }?.name ?? trimmed
    }

    // MARK: Create

    private func create() {
        let target: String
        switch kind {
        case .web:
            guard let url = webURL else { return }
            target = url.absoluteString
        default:
            guard let source else { return }
            target = source.url.path
        }
        let spec = ShortcutSpec(
            name: name.trimmed,
            kind: kind == .web ? .web : .app,
            targetPath: target,
            arguments: kind == .web ? "" : arguments,
            workingDirectory: "",
            outputDirectory: state.shortcutsDirectory,
            environment: kind == .web ? [] : environment,
            isolateData: separateData,
            freshLaunch: alwaysNewProcess,
            backgroundLaunch: openInBackground,
            hiddenLaunch: openHidden,
            autoBrowserProfile: true
        )
        let openAfter = openAfterCreating
        creating = true
        errorText = nil
        Task {
            do {
                let appURL = try await Task.detached(priority: .userInitiated) {
                    try ShortcutBuilder.create(spec: spec)
                }.value
                creating = false
                state.instanceCreated(at: appURL, open: openAfter)
                dismiss()
            } catch {
                creating = false
                errorText = error.localizedDescription
            }
        }
    }
}

// MARK: - App picker

private struct AppPicker: View {
    @EnvironmentObject private var state: AppState
    @Binding var selection: InstalledApp?
    let onContinue: () -> Void

    @State private var apps: [InstalledApp]?
    @State private var query = ""
    @State private var selectedID: String?
    @FocusState private var searchFocused: Bool

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Search apps", text: $query)
                    .textFieldStyle(.plain)
                    .focused($searchFocused)
                    .onKeyPress(.downArrow) { moveSelection(by: 1); return .handled }
                    .onKeyPress(.upArrow) { moveSelection(by: -1); return .handled }
                    .help("Type to search, \u{2191}\u{2193} to choose, Return to continue")
            }
            .padding(7)
            .background(.quaternary.opacity(0.6), in: RoundedRectangle(cornerRadius: 7))
            .padding(.horizontal, 16)
            .padding(.top, 12)

            if let apps {
                ScrollViewReader { proxy in
                List(selection: $selectedID) {
                    ForEach(InstalledApp.Support.allCases, id: \.self) { support in
                        let rows = filtered(apps).filter { $0.support == support }
                        if !rows.isEmpty {
                            Section(support.title) {
                                ForEach(rows) { app in
                                    AppPickerRow(app: app).tag(app.id).id(app.id)
                                }
                            }
                        }
                    }
                }
                .contextMenu(forSelectionType: String.self) { _ in
                    EmptyView()
                } primaryAction: { ids in
                    if let id = ids.first, let app = apps.first(where: { $0.id == id }) {
                        selection = app
                        onContinue()
                    }
                }
                .onChange(of: selectedID) { _, id in
                    selection = apps.first { $0.id == id }
                    if let id { withAnimation { proxy.scrollTo(id) } }
                }
                }
            } else {
                Spacer()
                ProgressView("Looking for apps\u{2026}")
                Spacer()
            }

            HStack {
                Button("Choose Another App\u{2026}") { chooseOther() }
                    .keyboardShortcut("o", modifiers: .command)
                    .help("Choose an app anywhere on this Mac (\u{2318}O)")
                Spacer()
                Text("You can also drop an app here.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 10)
        }
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            AppDrop.load(providers) { url in pick(url) }
        }
        .task {
            guard apps == nil else { return }
            let folder = state.shortcutsDirectory
            apps = await Task.detached(priority: .userInitiated) {
                await InstalledAppScanner.scan(excluding: folder)
            }.value
            selectedID = selection?.id
        }
        .onAppear { searchFocused = true }
        .onChange(of: query) { _, _ in
            // Like Spotlight: the first match is ready for Return.
            guard let apps else { return }
            let visible = ordered(apps)
            if !visible.contains(where: { $0.id == selectedID }) {
                selectedID = query.trimmed.isEmpty ? nil : visible.first?.id
            }
        }
    }

    /// Rows in the order the list shows them.
    private func ordered(_ apps: [InstalledApp]) -> [InstalledApp] {
        let matches = filtered(apps)
        return InstalledApp.Support.allCases.flatMap { support in matches.filter { $0.support == support } }
    }

    private func moveSelection(by step: Int) {
        guard let apps else { return }
        let visible = ordered(apps)
        guard !visible.isEmpty else { return }
        let current = visible.firstIndex { $0.id == selectedID }
        let next = current.map { min(max($0 + step, 0), visible.count - 1) } ?? (step > 0 ? 0 : visible.count - 1)
        selectedID = visible[next].id
    }

    private func filtered(_ apps: [InstalledApp]) -> [InstalledApp] {
        let text = query.trimmed
        guard !text.isEmpty else { return apps }
        return apps.filter { $0.name.localizedCaseInsensitiveContains(text) }
    }

    private func chooseOther() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.applicationBundle]
        panel.allowsMultipleSelection = false
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.prompt = "Choose"
        if panel.runModal() == .OK, let url = panel.url { pick(url) }
    }

    private func pick(_ url: URL) {
        guard let app = InstalledAppScanner.describe(url) else { return }
        selection = app
        onContinue()
    }
}

private struct AppPickerRow: View {
    let app: InstalledApp

    var body: some View {
        HStack(spacing: 10) {
            Image(nsImage: IconCache.icon(for: app.url))
                .resizable()
                .frame(width: 24, height: 24)
            VStack(alignment: .leading, spacing: 1) {
                Text(app.name)
                if !app.version.isEmpty {
                    Text(app.version)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            StatusLabel(status: StatusPresentation(app.profile), compact: true)
        }
        .padding(.vertical, 2)
    }
}

// MARK: - Website presets

struct WebPreset: Identifiable {
    let name: String
    let address: String
    var id: String { address }

    static let all: [WebPreset] = [
        WebPreset(name: "WhatsApp Web", address: "https://web.whatsapp.com"),
        WebPreset(name: "Telegram Web", address: "https://web.telegram.org"),
        WebPreset(name: "Gmail", address: "https://mail.google.com"),
        WebPreset(name: "Outlook", address: "https://outlook.live.com"),
        WebPreset(name: "Slack", address: "https://app.slack.com"),
        WebPreset(name: "Discord", address: "https://discord.com/app"),
        WebPreset(name: "Instagram", address: "https://www.instagram.com"),
        WebPreset(name: "X", address: "https://x.com"),
        WebPreset(name: "Messenger", address: "https://www.messenger.com")
    ]
}

