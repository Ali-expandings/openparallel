import AppKit
import Foundation

/// Reads back the instance manifests that ShortcutBuilder writes, so the app
/// can present the instances that already exist instead of only building new
/// ones. The manifest (`instance.json` inside each instance data root) is the
/// single source of truth; the library never guesses.
struct InstanceRecord: Identifiable, Equatable {
    let manifestURL: URL
    let instanceName: String
    let instanceSlug: String
    let family: String
    let launchMode: String
    let sourcePath: String
    let sourceBundleIdentifier: String
    let sourceVersionAtCreation: String
    let profileIsolation: Bool
    let vendorBundleModified: Bool
    let updatePolicy: String?
    let createdAt: String?
    let webURL: String?
    /// The launchable .app, when it could be found.
    var wrapperURL: URL?

    var id: String { instanceSlug }
    var isWebsite: Bool { launchMode == "web-frame" }
    var dataRoot: URL { manifestURL.deletingLastPathComponent() }

    var sourceExists: Bool {
        !sourcePath.isEmpty && FileManager.default.fileExists(atPath: sourcePath)
    }

    var createdDate: Date? {
        createdAt.flatMap { ISO8601DateFormatter().date(from: $0) }
    }
}

@MainActor
final class InstanceLibrary: ObservableObject {
    @Published private(set) var instances: [InstanceRecord] = []
    /// Shortcut apps in the shortcuts folder that no manifest describes:
    /// made by an older OpenParallel and not manageable here.
    @Published private(set) var legacyShortcuts: [URL] = []
    @Published private(set) var shortcutsDirectory: URL

    /// Where instance manifests live, and the user Library whose Group
    /// Containers, WebKit, HTTPStorages, Caches and Logs hold the rest of an
    /// instance's data. Injectable so tests never touch the real Library.
    let dataRoot: URL
    let libraryDirectory: URL
    var groupContainersRoot: URL { libraryDirectory.appendingPathComponent("Group Containers", isDirectory: true) }

    /// How deleted items leave the disk. Moving to the Trash keeps every delete
    /// recoverable; tests remove directly.
    private let remove: (URL) throws -> Void

    init(
        shortcutsDirectory: URL? = nil,
        dataRoot: URL? = nil,
        libraryDirectory: URL? = nil,
        remover: ((URL) throws -> Void)? = nil
    ) {
        let home = FileManager.default.homeDirectoryForCurrentUser
        self.shortcutsDirectory = shortcutsDirectory ?? Self.defaultShortcutsDirectory()
        self.dataRoot = dataRoot ?? Self.instanceDataRoots()
        self.libraryDirectory = libraryDirectory ?? home.appendingPathComponent("Library", isDirectory: true)
        self.remove = remover ?? { url in try FileManager.default.trashItem(at: url, resultingItemURL: nil) }
        reload()
    }

    static func defaultShortcutsDirectory() -> URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Applications/OpenParallel Shortcuts", isDirectory: true)
    }

    static func instanceDataRoots() -> URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/Instances", isDirectory: true)
    }

    func setShortcutsDirectory(_ url: URL) {
        shortcutsDirectory = url
        reload()
    }

    func record(withSlug slug: String) -> InstanceRecord? {
        instances.first { $0.instanceSlug == slug }
    }

    /// Reloads every `instance.json` under the instance data root. Manifests
    /// that fail to parse are skipped rather than surfacing an error: the
    /// library is a presentation layer, not a validator.
    func reload() {
        let fileManager = FileManager.default
        let wrappers = shortcutApps()

        var records: [InstanceRecord] = []
        let entries = (try? fileManager.contentsOfDirectory(
            at: dataRoot,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        )) ?? []
        for directory in entries {
            let manifestURL = directory.appendingPathComponent("instance.json")
            guard
                let data = try? Data(contentsOf: manifestURL),
                let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
            else { continue }
            var record = Self.record(from: object, manifestURL: manifestURL)
            record.wrapperURL = resolveWrapper(for: record, manifest: object, wrappers: wrappers)
            records.append(record)
        }

        let claimed = Set(records.compactMap { $0.wrapperURL?.standardizedFileURL.path })
        legacyShortcuts = wrappers
            .filter { !claimed.contains($0.url.standardizedFileURL.path) }
            .map(\.url)
            .sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
        instances = records.sorted { a, b in
            a.instanceName.localizedStandardCompare(b.instanceName) == .orderedAscending
        }
    }

    private static func record(from manifest: [String: Any], manifestURL: URL) -> InstanceRecord {
        let folderName = manifestURL.deletingLastPathComponent().lastPathComponent
        return InstanceRecord(
            manifestURL: manifestURL,
            instanceName: manifest["instanceName"] as? String ?? folderName,
            instanceSlug: manifest["instanceSlug"] as? String ?? folderName,
            family: manifest["family"] as? String ?? "",
            launchMode: manifest["launchMode"] as? String ?? "",
            sourcePath: manifest["sourcePath"] as? String ?? "",
            sourceBundleIdentifier: manifest["sourceBundleIdentifier"] as? String ?? "",
            sourceVersionAtCreation: manifest["sourceVersionAtCreation"] as? String ?? "",
            profileIsolation: manifest["profileIsolation"] as? Bool ?? false,
            vendorBundleModified: manifest["vendorBundleModified"] as? Bool ?? false,
            updatePolicy: manifest["updatePolicy"] as? String,
            createdAt: manifest["createdAt"] as? String,
            webURL: manifest["webURL"] as? String,
            wrapperURL: nil
        )
    }

    private struct ShortcutApp {
        let url: URL
        let slug: String?
    }

    /// Every .app in the shortcuts folder, with the instance slug its Info.plist records.
    private func shortcutApps() -> [ShortcutApp] {
        let entries = (try? FileManager.default.contentsOfDirectory(
            at: shortcutsDirectory,
            includingPropertiesForKeys: nil,
            options: [.skipsHiddenFiles]
        )) ?? []
        return entries
            .filter { $0.pathExtension.lowercased() == "app" }
            .map { url in
                let info = NSDictionary(contentsOf: url.appendingPathComponent("Contents/Info.plist"))
                return ShortcutApp(url: url, slug: info?["OpenParallelInstanceSlug"] as? String)
            }
    }

    /// The manifest's recorded path wins; then the instance slug the wrapper
    /// carries (survives a Finder rename); then the instance name.
    private func resolveWrapper(for record: InstanceRecord, manifest: [String: Any], wrappers: [ShortcutApp]) -> URL? {
        if let path = manifest["wrapperPath"] as? String, FileManager.default.fileExists(atPath: path) {
            return URL(fileURLWithPath: path, isDirectory: true)
        }
        if let match = wrappers.first(where: { $0.slug == record.instanceSlug }) {
            return match.url
        }
        return wrappers.first { $0.url.deletingPathExtension().lastPathComponent == record.instanceName }?.url
    }

    func launch(_ record: InstanceRecord) {
        guard let appURL = record.wrapperURL else { return }
        NSWorkspace.shared.openApplication(at: appURL, configuration: NSWorkspace.OpenConfiguration())
    }

    /// True when the vendor source has been updated past the version recorded
    /// in the instance manifest. Copies must then be rebuilt; launcher
    /// wrappers follow the source automatically and never drift.
    func sourceDrifted(_ record: InstanceRecord) -> Bool {
        guard record.vendorBundleModified, record.updatePolicy == "rebuild-required" else { return false }
        guard let currentVersion = Self.currentVersion(of: record) else { return false }
        return currentVersion != record.sourceVersionAtCreation
    }

    static func currentVersion(of record: InstanceRecord) -> String? {
        guard !record.sourcePath.isEmpty, let bundle = Bundle(url: URL(fileURLWithPath: record.sourcePath)) else { return nil }
        let version = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        return version?.isEmpty == false ? version : nil
    }

    /// Every location that belongs to this instance and only to it.
    func dataLocations(for record: InstanceRecord) -> [URL] {
        var urls: [URL] = []
        let instanceRoot = record.dataRoot.standardizedFileURL
        if instanceRoot.deletingLastPathComponent().path == dataRoot.standardizedFileURL.path {
            urls.append(instanceRoot)
        }

        if record.isWebsite, let bundleID = wrapperBundleIdentifier(record), bundleID.hasPrefix("dev.openparallel.shortcut.") {
            for folder in ["WebKit", "HTTPStorages", "Caches"] {
                urls.append(libraryDirectory.appendingPathComponent(folder, isDirectory: true).appendingPathComponent(bundleID, isDirectory: true))
            }
            urls.append(libraryDirectory.appendingPathComponent("Saved Application State/\(bundleID).savedState", isDirectory: true))
            urls.append(libraryDirectory.appendingPathComponent("Preferences/\(bundleID).plist"))
        }

        if record.vendorBundleModified,
           let data = try? Data(contentsOf: record.manifestURL),
           let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            for path in object["groupContainerPaths"] as? [String] ?? [] {
                let url = URL(fileURLWithPath: path, isDirectory: true).standardizedFileURL
                if isRemovableGroupContainer(url) { urls.append(url) }
            }
        }

        let logs = libraryDirectory.appendingPathComponent("Logs/OpenParallel", isDirectory: true)
        urls.append(logs.appendingPathComponent("\(record.instanceSlug).log"))
        urls.append(logs.appendingPathComponent("\(record.instanceSlug)-web.log"))
        return urls.filter { FileManager.default.fileExists(atPath: $0.path) }
    }

    private func wrapperBundleIdentifier(_ record: InstanceRecord) -> String? {
        guard let wrapper = record.wrapperURL else { return nil }
        return NSDictionary(contentsOf: wrapper.appendingPathComponent("Contents/Info.plist"))?["CFBundleIdentifier"] as? String
    }

    /// Removes the wrapper .app and every data location of the instance.
    /// The vendor source app is never touched.
    func delete(_ record: InstanceRecord) throws {
        // Collect data locations first: they depend on the wrapper and manifest.
        let locations = dataLocations(for: record)
        if let wrapper = record.wrapperURL, FileManager.default.fileExists(atPath: wrapper.path) {
            try remove(wrapper)
        }
        for url in locations {
            try remove(url)
        }
        reload()
    }

    func trashLegacyShortcut(_ url: URL) throws {
        guard url.deletingLastPathComponent().standardizedFileURL.path == shortcutsDirectory.standardizedFileURL.path else { return }
        try remove(url)
        reload()
    }

    /// A manifest is user-writable data, so a group path is only removed when
    /// it is a direct child of the Group Containers root, names an app group,
    /// and is not one of the vendor's original WhatsApp groups. The original
    /// account's container must survive any manifest, however malformed.
    func isRemovableGroupContainer(_ url: URL) -> Bool {
        let name = url.lastPathComponent
        return url.deletingLastPathComponent().path == groupContainersRoot.standardizedFileURL.path
            && name.hasPrefix("group.")
            && !Self.protectedGroupContainers.contains(name)
    }

    /// The vendor's own WhatsApp app groups. They hold the original account and
    /// are never removed, whatever a manifest lists.
    static let protectedGroupContainers: Set<String> = [
        "group.net.whatsapp.WhatsApp.shared",
        "group.net.whatsapp.WhatsAppSMB.shared",
        "group.net.whatsapp.WhatsApp.private",
        "group.net.whatsapp.family",
        "group.com.facebook.family"
    ]
}
