import SwiftUI

struct InstanceDetailView: View {
    @EnvironmentObject private var state: AppState
    @Environment(\.openWindow) private var openWindow
    let record: InstanceRecord

    @State private var dataSize: Int64?

    private var running: Bool { state.isRunning(record) }

    private var status: StatusPresentation {
        record.isWebsite
            ? .website
            : StatusPresentation(CompatibilityProfile.resolved(kind: .app, targetPath: record.sourcePath))
    }

    var body: some View {
        Form {
            Section {
                header
            }

            Section("Status") {
                LabeledContent("Compatibility") { StatusLabel(status: status) }
                Text(status.detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                if state.library.sourceDrifted(record) {
                    Label("\(record.sourceName) was updated after this copy was made. Create the instance again to update it; its data folder is kept.",
                          systemImage: "arrow.triangle.2.circlepath")
                        .foregroundStyle(.orange)
                }
            }

            Section("Data") {
                LabeledContent("Own data", value: record.isWebsite || record.profileIsolation ? "Yes" : "No, shared with \(record.sourceName)")
                LabeledContent("Size on disk") {
                    if let dataSize {
                        Text(ByteCountFormatter.string(fromByteCount: dataSize, countStyle: .file))
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                PathRow(title: "Folder", url: record.dataRoot)
            }

            Section("Source") {
                if record.isWebsite {
                    LabeledContent("Address") {
                        Text(record.webURL ?? "")
                            .textSelection(.enabled)
                    }
                } else {
                    LabeledContent("App", value: record.sourceName)
                    LabeledContent("Version", value: versionText)
                    PathRow(title: "Location", url: URL(fileURLWithPath: record.sourcePath))
                    if !record.sourceExists {
                        Label("The source app is no longer at this location.", systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                    }
                }
            }

            Section("Shortcut") {
                if let wrapper = record.wrapperURL {
                    PathRow(title: "App", url: wrapper)
                } else {
                    Label("The shortcut app is missing from the shortcuts folder.", systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                }
                if let created = record.createdDate {
                    LabeledContent("Created", value: created.formatted(date: .abbreviated, time: .shortened))
                }
            }

            Section {
                HStack {
                    Button("Report a Problem\u{2026}") {
                        state.reportContext = ReportContext(
                            app: record.isWebsite ? (record.webURL ?? record.instanceName) : record.sourceName,
                            version: record.sourceVersionAtCreation,
                            detail: "\(record.family), \(status.title)")
                        openWindow(id: "report")
                    }
                    .help("Tell the OpenParallel team what went wrong with this instance")
                    Spacer()
                    Button("Move to Trash…", role: .destructive) { state.confirmDelete(record) }
                        .disabled(running)
                        .help(running ? "Quit the instance before moving it to the Trash." : "")
                }
            }
        }
        .formStyle(.grouped)
        .navigationTitle(record.instanceName)
        .task(id: "\(record.instanceSlug)-\(running)") {
            dataSize = nil
            let urls = state.library.dataLocations(for: record)
            dataSize = await Task.detached(priority: .utility) { DiskUsage.size(of: urls) }.value
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            Image(nsImage: IconCache.icon(for: record))
                .resizable()
                .interpolation(.high)
                .frame(width: 56, height: 56)
            VStack(alignment: .leading, spacing: 3) {
                Text(record.instanceName)
                    .font(.title2.weight(.semibold))
                HStack(spacing: 6) {
                    if running {
                        Circle().fill(.green).frame(width: 7, height: 7)
                        Text("Running")
                    } else {
                        Text("Not running")
                    }
                }
                .font(.callout)
                .foregroundStyle(.secondary)
            }
            Spacer()
            if running {
                Button("Quit") { state.quit(record) }
            }
            Button(running ? "Show" : "Open") { state.open(record) }
                .buttonStyle(.borderedProminent)
                .disabled(record.wrapperURL == nil)
        }
        .padding(.vertical, 4)
    }

    private var versionText: String {
        let current = InstanceLibrary.currentVersion(of: record) ?? "unknown"
        if record.sourceVersionAtCreation.isEmpty || record.sourceVersionAtCreation == current {
            return current
        }
        return "\(current) (created with \(record.sourceVersionAtCreation))"
    }
}

private struct PathRow: View {
    let title: String
    let url: URL

    var body: some View {
        LabeledContent(title) {
            HStack(spacing: 6) {
                Text(url.path.replacingOccurrences(of: FileManager.default.homeDirectoryForCurrentUser.path, with: "~"))
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .textSelection(.enabled)
                Button {
                    NSWorkspace.shared.activateFileViewerSelecting([url])
                } label: {
                    Image(systemName: "arrow.right.circle.fill")
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.borderless)
                .help("Show in Finder")
                .disabled(!FileManager.default.fileExists(atPath: url.path))
            }
        }
    }
}

enum DiskUsage {
    static func size(of urls: [URL]) -> Int64 {
        urls.reduce(0) { $0 + size(of: $1) }
    }

    static func size(of url: URL) -> Int64 {
        let keys: Set<URLResourceKey> = [.totalFileAllocatedSizeKey, .isRegularFileKey]
        guard let enumerator = FileManager.default.enumerator(at: url, includingPropertiesForKeys: Array(keys)) else {
            return Int64((try? url.resourceValues(forKeys: keys))?.totalFileAllocatedSize ?? 0)
        }
        var total: Int64 = Int64((try? url.resourceValues(forKeys: keys))?.totalFileAllocatedSize ?? 0)
        for case let file as URL in enumerator {
            guard let values = try? file.resourceValues(forKeys: keys), values.isRegularFile == true else { continue }
            total += Int64(values.totalFileAllocatedSize ?? 0)
        }
        return total
    }
}
