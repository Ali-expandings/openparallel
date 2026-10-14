import AppKit
import Foundation

struct InstalledApp: Identifiable, Sendable {
    let url: URL
    let name: String
    let version: String
    let bundleIdentifier: String
    let profile: CompatibilityProfile

    var id: String { url.path }

    /// How the picker groups apps: what an instance of this app actually gets.
    var support: Support { Support(profile) }

    enum Support: Int, CaseIterable, Sendable {
        case separate, sharedData, unsupported

        init(_ profile: CompatibilityProfile) {
            if !profile.multiInstance {
                self = .unsupported
            } else if profile.supportsProfileIsolation || profile.requiresProfileIsolation {
                self = .separate
            } else {
                self = .sharedData
            }
        }

        var title: String {
            switch self {
            case .separate: "Separate login and data"
            case .sharedData: "Opens again, shares data"
            case .unsupported: "Not supported"
            }
        }
    }
}

enum InstalledAppScanner {
    /// Apps in /Applications, /Applications/Utilities and ~/Applications (one
    /// folder level deep), minus OpenParallel itself and its own shortcuts.
    static func scan(excluding shortcutsDirectory: URL) async -> [InstalledApp] {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let roots = [
            URL(fileURLWithPath: "/Applications", isDirectory: true),
            URL(fileURLWithPath: "/Applications/Utilities", isDirectory: true),
            home.appendingPathComponent("Applications", isDirectory: true)
        ]
        let excluded = shortcutsDirectory.standardizedFileURL.path

        var bundles: [URL] = []
        var seen = Set<String>()
        for root in roots {
            for url in appBundles(in: root, depth: 1) where !url.standardizedFileURL.path.hasPrefix(excluded) {
                if seen.insert(url.standardizedFileURL.path).inserted { bundles.append(url) }
            }
        }

        return await withTaskGroup(of: InstalledApp?.self) { group in
            for url in bundles {
                group.addTask { describe(url) }
            }
            var apps: [InstalledApp] = []
            for await app in group {
                if let app { apps.append(app) }
            }
            return apps.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        }
    }

    static func describe(_ url: URL) -> InstalledApp? {
        guard let bundle = Bundle(url: url), let bundleID = bundle.bundleIdentifier else { return nil }
        // Skip OpenParallel and anything it generated.
        if bundleID == "dev.openparallel.app" || bundleID.hasPrefix("dev.openparallel.") { return nil }
        if bundle.object(forInfoDictionaryKey: "OpenParallelLaunchMode") != nil { return nil }
        return InstalledApp(
            url: url,
            name: preferredAppName(at: url) ?? url.deletingPathExtension().lastPathComponent,
            version: bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            bundleIdentifier: bundleID,
            profile: CompatibilityProfile.resolved(kind: .app, targetPath: url.path)
        )
    }

    private static func appBundles(in directory: URL, depth: Int) -> [URL] {
        let entries = (try? FileManager.default.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles, .skipsPackageDescendants]
        )) ?? []
        var result: [URL] = []
        for entry in entries {
            if entry.pathExtension.lowercased() == "app" {
                result.append(entry)
            } else if depth > 0, (try? entry.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true {
                result.append(contentsOf: appBundles(in: entry, depth: depth - 1))
            }
        }
        return result
    }
}

/// App icons are requested for every list row; NSWorkspace is fast but not free.
@MainActor
enum IconCache {
    private static var cache: [String: NSImage] = [:]

    static func icon(for url: URL?) -> NSImage {
        guard let url else { return NSWorkspace.shared.icon(for: .applicationBundle) }
        if let cached = cache[url.path] { return cached }
        let icon = NSWorkspace.shared.icon(forFile: url.path)
        cache[url.path] = icon
        return icon
    }

    static func invalidate(_ url: URL?) {
        guard let url else { return }
        cache[url.path] = nil
    }

    /// An app instance shows its source app's icon (the wrapper carries a copy
    /// of it anyway, and the source is never missing a binary); a website shows
    /// its shortcut's icon.
    static func icon(for record: InstanceRecord) -> NSImage {
        if !record.isWebsite, record.sourceExists { return icon(for: URL(fileURLWithPath: record.sourcePath)) }
        return icon(for: record.wrapperURL)
    }
}
