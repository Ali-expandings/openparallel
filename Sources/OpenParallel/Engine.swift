import AppKit
import Foundation

enum ShortcutKind: String, CaseIterable, Identifiable {
    case app = "App"
    case web = "Web"
    case file = "File"
    case folder = "Folder"
    case command = "Command"

    var id: String { rawValue }

    var hint: String {
        switch self {
        case .app:
            "Launch another instance of a macOS app."
        case .web:
            "Run a website in a persistent, isolated WebKit profile."
        case .file:
            "Open a document from a Dock shortcut."
        case .folder:
            "Open a folder from a Dock shortcut."
        case .command:
            "Run a shell command from a Dock shortcut."
        }
    }
}

struct EnvironmentPair: Identifiable, Hashable, Sendable {
    let id = UUID()
    var key = ""
    var value = ""
}

enum CompatibilityStatus: String {
    /// Confirmed by a real account test (e.g. signing out one profile leaves the
    /// original account signed in). Earned, not assumed.
    case verified = "Verified"
    /// Heuristic match: we recognise the app family and apply the right isolation
    /// flags, but isolation has NOT been proven by a real test. Expected to work.
    case detected = "Detected"
    /// Launcher and isolated profile were mechanically exercised on this Mac.
    /// Account-level sign-out isolation still needs user confirmation.
    case tested = "Tested"
    case partial = "Partial"
    case limited = "Limited"
    /// Recognised but isolation strategy is unproven / best-effort.
    case experimental = "Experimental"

    /// One-line meaning shown under the badge so the label never overclaims.
    var caption: String {
        switch self {
        case .verified:
            "Proven by a recorded account-level isolation test."
        case .detected:
            "App family recognised; isolation flags applied but not yet proven by a real test."
        case .tested:
            "Signed source app launched with its own profile on this Mac. Confirm account separation before relying on it."
        case .partial:
            "Launches separately, but some state (keychain/containers) may stay shared."
        case .limited:
            "macOS constrains isolation for this app; expect shared data."
        case .experimental:
            "Best-effort isolation. Behaviour unverified — test before relying on it."
        }
    }
}

enum DataStorageMode: String {
    case auto = "Auto"
    case home = "HOME Override"
    case `default` = "Default"
    case webFrame = "Web Frame"
    /// A standalone copy that owns its own containers. Provided only by an
    /// optional private extension; the public build never selects it.
    case identityCopy = "Identity Copy"
    case sandboxed = "Sandboxed"
    case unsupported = "Unsupported"
}

enum AppFamily: String {
    case claude = "Claude"
    case codex = "Codex"
    case antigravity = "Antigravity"
    case chromium = "Chromium"
    case firefox = "Firefox"
    case electron = "Electron"
    case discord = "Discord"
    case spotify = "Spotify"
    case toDesktop = "ToDesktop"
    case jetBrains = "JetBrains"
    case eclipse = "Eclipse"
    case vscode = "VS Code"
    case telegramDesktop = "Telegram Desktop"
    case whatsAppNative = "WhatsApp Native"
    case homeProfile = "HOME Profile"
    case terminal = "Terminal"
    case apple = "Apple"
    case sandboxed = "Sandboxed"
    case generic = "Generic"
}

/// Tracks explicit account-level verification markers. Process/profile smoke tests
/// live in `RealAppApprovalLedger` and require an exact bundle ID and version.
enum VerificationLedger {
    static var directory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/verified", isDirectory: true)
    }

    static func markerURL(for family: AppFamily) -> URL {
        directory.appendingPathComponent("\(family.rawValue.lowercased()).ok")
    }

    static func isVerified(_ family: AppFamily) -> Bool {
        FileManager.default.fileExists(atPath: markerURL(for: family).path)
    }

    static func status(for family: AppFamily, appPath: String) -> CompatibilityStatus? {
        if isVerified(family) { return .verified }
        if RealAppApprovalLedger.approval(forAppAt: appPath) != nil { return .tested }
        return nil
    }
}

struct CompatibilityProfile {
    var family: AppFamily
    var status: CompatibilityStatus
    var dataMode: DataStorageMode
    var multiInstance: Bool
    var separateData: Bool
    var menuBarNewWindow: Bool
    var notes: String
    /// True when OpenParallel has a profile-directory recipe for this family.
    var supportsProfileIsolation: Bool = false

    /// True when the generated instance runs with its own HOME instead of the
    /// user's.
    var usesHomeOverride: Bool { family == .homeProfile || family == .whatsAppNative }

    /// Isolation is not optional for a standalone copy: without it the copy
    /// would be a slower duplicate of the original with no separate account.
    var requiresProfileIsolation: Bool { family == .whatsAppNative }

    /// How the generated instance reaches the vendor app. Recorded in the
    /// instance Info.plist and manifest so tooling never has to guess.
    var launchMode: String {
        family == .whatsAppNative ? "native-identity-copy" : "linked-signed-source"
    }

    static let generic = CompatibilityProfile(
        family: .generic,
        status: .experimental,
        dataMode: .auto,
        multiInstance: true,
        separateData: false,
        menuBarNewWindow: false,
        notes: "Generic app. OpenParallel can launch another process, but does not redirect HOME or claim data separation without a known profile flag."
    )

    static func analyze(kind: ShortcutKind, targetPath: String) -> CompatibilityProfile {
        guard kind == .app, !targetPath.trimmed.isEmpty else {
            return CompatibilityProfile(
                family: .generic,
                status: .experimental,
                dataMode: .default,
                multiInstance: true,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Only app shortcuts receive automatic compatibility detection."
            )
        }

        let targetURL = URL(fileURLWithPath: targetPath)
        let lower = targetPath.lowercased()
        let bundle = Bundle(url: targetURL)
        let bundleID = (bundle?.bundleIdentifier ?? "").lowercased()
        let catalogRecord = CompatibilityCatalog.record(matchingAppAt: targetPath)

        if bundleID == "com.tdesktop.telegram" {
            return CompatibilityProfile(
                family: .telegramDesktop,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Official Telegram Desktop direct build. Uses -many with a dedicated -workdir. Telegram Lite/App Store build is a different sandboxed app.",
                supportsProfileIsolation: true
            )
        }

        if bundleID == "org.telegram.desktop" {
            return CompatibilityProfile(
                family: .sandboxed,
                status: .limited,
                dataMode: .sandboxed,
                multiInstance: false,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Telegram Lite/App Store build enforces one sandbox container and rejected parallel launches on this Mac. Install official Telegram Desktop from desktop.telegram.org for separate instances."
            )
        }

        // WhatsApp hardcodes one bundle identifier and App Group set, so a
        // second process opens the same account. Checked ahead of the generic
        // sandbox rule below so the note can point at the Web alternative.
        if bundleID == "net.whatsapp.whatsapp" {
            #if OPENPARALLEL_PRIVATE
            return NativeWhatsApp.compatibilityProfile
            #else
            return CompatibilityProfile(
                family: .sandboxed,
                status: .limited,
                dataMode: .sandboxed,
                multiInstance: false,
                separateData: false,
                menuBarNewWindow: false,
                notes: "WhatsApp hardcodes one bundle identifier and App Group, so a second process opens the same account. Create a Web shortcut for https://web.whatsapp.com instead; each Web shortcut keeps its own login."
            )
            #endif
        }

        if bundleID == "com.hnc.discord" {
            return CompatibilityProfile(
                family: .discord,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Discord-specific mode. Uses its supported --multi-instance flag and DISCORD_USER_DATA_DIR before Discord initializes user data.",
                supportsProfileIsolation: true
            )
        }

        if bundleID == "com.spotify.client" {
            return CompatibilityProfile(
                family: .spotify,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Spotify-specific mode. Uses its supported --mu instance flag plus dedicated CEF user-data and cache directories.",
                supportsProfileIsolation: true
            )
        }

        if bundleID.hasPrefix("com.apple.") || lower.contains("/system/applications/") {
            return CompatibilityProfile(
                family: .apple,
                status: .partial,
                dataMode: .sandboxed,
                multiInstance: true,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Apple/sandboxed apps usually keep system-managed data containers."
            )
        }

        if appHasSandboxEntitlement(targetPath) {
            return CompatibilityProfile(
                family: .sandboxed,
                status: .partial,
                dataMode: .sandboxed,
                multiInstance: true,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Sandbox entitlement detected. macOS may force shared container data."
            )
        }

        if bundleID == "com.anthropic.claudefordesktop" || lower.contains("claude.app") {
            return CompatibilityProfile(
                family: .claude,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "Runs Anthropic's current signed app with a separate Electron profile. Updates and virtualization entitlements stay intact; normal HOME and terminal access stay available.",
                supportsProfileIsolation: true
            )
        }

        if bundleID == "com.openai.codex" || lower.contains("codex.app") {
            return CompatibilityProfile(
                family: .codex,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "Runs OpenAI's current signed Codex app with separate browser data and CODEX_HOME. Host HOME, files, terminal, and network remain normal.",
                supportsProfileIsolation: true
            )
        }

        if bundleID == "com.google.antigravity" || bundleID == "com.google.antigravity-ide" || lower.contains("antigravity") {
            return CompatibilityProfile(
                family: .antigravity,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "Runs Google's current signed Antigravity app with a separate Electron or IDE profile. Host HOME and developer tools remain available.",
                supportsProfileIsolation: true
            )
        }

        if isChromiumFamily(lower, bundleID: bundleID) || isChromiumEmbeddedApp(targetPath) {
            return CompatibilityProfile(
                family: .chromium,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "Uses the vendor-signed browser with an isolated --user-data-dir. Apps with bundle-wide single-instance enforcement may remain limited.",
                supportsProfileIsolation: true
            )
        }

        if isFirefoxFamily(lower, bundleID: bundleID) {
            return CompatibilityProfile(
                family: .firefox,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "Firefox profile mode. Uses -profile with -no-remote.",
                supportsProfileIsolation: true
            )
        }

        if ["visual studio code", "vscode", "cursor", "windsurf", "codex"].contains(where: lower.contains) {
            return CompatibilityProfile(
                family: .vscode,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "VS Code-family mode. Uses --user-data-dir and --extensions-dir while keeping normal HOME.",
                supportsProfileIsolation: true
            )
        }

        if isToDesktopApp(targetPath) {
            return CompatibilityProfile(
                family: .toDesktop,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: true,
                notes: "ToDesktop/Electron detected. Uses vendor-signed app plus isolated user-data-dir/cache. Real HOME is preserved.",
                supportsProfileIsolation: true
            )
        }

        if isElectronApp(targetPath) {
            return CompatibilityProfile(
                family: .electron,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Electron detected. Uses vendor-signed app plus isolated user-data-dir/cache. Real HOME is preserved.",
                supportsProfileIsolation: true
            )
        }

        if ["jetbrains", "intellij", "pycharm", "webstorm", "clion", "rubymine", "goland", "datagrip", "android studio"].contains(where: lower.contains) {
            return CompatibilityProfile(
                family: .jetBrains,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "JetBrains mode. Uses isolated config/system/plugins/logs directories.",
                supportsProfileIsolation: true
            )
        }

        if ["eclipse", "dbeaver", "springtoolsuite"].contains(where: lower.contains) {
            return CompatibilityProfile(
                family: .eclipse,
                status: .detected,
                dataMode: .auto,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Eclipse mode. Uses isolated workspace/configuration paths.",
                supportsProfileIsolation: true
            )
        }

        if ["iterm", "ghostty", "terminal"].contains(where: lower.contains) {
            if catalogRecord?.referenceStatus == .verified {
                return CompatibilityProfile(
                    family: .homeProfile,
                    status: .experimental,
                    dataMode: .home,
                    multiInstance: true,
                    separateData: true,
                    menuBarNewWindow: false,
                    notes: "Recognized terminal compatibility target using experimental per-instance HOME. Shell starts in that profile; common personal folders remain linked to the real home.",
                    supportsProfileIsolation: true
                )
            }
            return CompatibilityProfile(
                family: .terminal,
                status: .partial,
                dataMode: .auto,
                multiInstance: true,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Terminal apps launch separately, but profiles/keychains may remain shared."
            )
        }

        if catalogRecord?.referenceStatus == .verified {
            return CompatibilityProfile(
                family: .homeProfile,
                status: .experimental,
                dataMode: .home,
                multiInstance: true,
                separateData: true,
                menuBarNewWindow: false,
                notes: "Recognized compatibility target without a stronger app-specific flag. Uses a per-instance HOME as an experimental fallback; common personal folders remain linked to the real home. Test account separation and app features before relying on it.",
                supportsProfileIsolation: true
            )
        }

        if catalogRecord?.referenceStatus == .partial {
            return CompatibilityProfile(
                family: .generic,
                status: .partial,
                dataMode: .default,
                multiInstance: true,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Cataloged partial target. OpenParallel can request another process but has no safe separate-data recipe."
            )
        }

        if catalogRecord?.referenceStatus == .web {
            return CompatibilityProfile(
                family: .generic,
                status: .limited,
                dataMode: .webFrame,
                multiInstance: false,
                separateData: false,
                menuBarNewWindow: false,
                notes: "Cataloged web-app target. Use OpenParallel Web mode rather than cloning its native app."
            )
        }

        if catalogRecord?.referenceStatus == .limited {
            return CompatibilityProfile(
                family: .generic,
                status: .limited,
                dataMode: .unsupported,
                multiInstance: false,
                separateData: false,
                menuBarNewWindow: false,
                notes: "iOS/iPadOS app mode is limited by macOS and cannot provide separate data or a dedicated native instance."
            )
        }

        return .generic
    }

    private static func isChromiumFamily(_ lowerPath: String, bundleID: String) -> Bool {
        let appName = URL(fileURLWithPath: lowerPath).deletingPathExtension().lastPathComponent
        let names = [
            "arc", "google chrome", "chrome", "chromium", "brave browser", "brave",
            "microsoft edge", "edge", "vivaldi", "dia browser", "dia",
            "perplexity comet", "comet", "openai atlas", "atlas", "opera"
        ]
        let ids = [
            "company.thebrowser.browser",
            "company.thebrowser.arc",
            "com.thebrowsercompany.arc",
            "com.google.chrome",
            "org.chromium.chromium",
            "com.brave.browser",
            "com.microsoft.edgemac",
            "com.vivaldi.vivaldi",
            "com.operasoftware.opera"
        ]
        let nameMatch = names.contains { appName == $0 || appName.hasPrefix("\($0) ") }
        return nameMatch || ids.contains(where: bundleID.contains)
    }

    private static func isFirefoxFamily(_ lowerPath: String, bundleID: String) -> Bool {
        let appName = URL(fileURLWithPath: lowerPath).deletingPathExtension().lastPathComponent
        let names = ["firefox", "mozilla firefox", "zen", "zen browser", "tor browser", "librewolf", "floorp"]
        let ids = ["org.mozilla.firefox", "app.zen-browser.zen", "org.torproject.torbrowser"]
        let nameMatch = names.contains { appName == $0 || appName.hasPrefix("\($0) ") }
        return nameMatch || ids.contains(where: bundleID.contains)
    }
}

private nonisolated(unsafe) let sandboxEntitlementCache = NSCache<NSString, NSNumber>()

func appHasSandboxEntitlement(_ targetPath: String) -> Bool {
    let cacheKey = targetPath as NSString
    if let cached = sandboxEntitlementCache.object(forKey: cacheKey) {
        return cached.boolValue
    }

    let appURL = URL(fileURLWithPath: targetPath)
    let receiptURL = appURL.appendingPathComponent("Contents/_MASReceipt/receipt")
    if FileManager.default.fileExists(atPath: receiptURL.path) {
        sandboxEntitlementCache.setObject(NSNumber(value: true), forKey: cacheKey)
        return true
    }

    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/codesign")
    process.arguments = ["-d", "--entitlements", ":-", targetPath]
    let output = Pipe()
    process.standardOutput = output
    process.standardError = FileHandle.nullDevice

    guard (try? process.run()) != nil else {
        sandboxEntitlementCache.setObject(NSNumber(value: false), forKey: cacheKey)
        return false
    }
    let data = output.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    let entitlements = (try? PropertyListSerialization.propertyList(from: data, options: [], format: nil)) as? [String: Any]
    let isSandboxed = entitlements?["com.apple.security.app-sandbox"] as? Bool == true
    sandboxEntitlementCache.setObject(NSNumber(value: isSandboxed), forKey: cacheKey)
    return isSandboxed
}

func isElectronApp(_ targetPath: String) -> Bool {
    let appURL = URL(fileURLWithPath: targetPath)
    guard appURL.pathExtension == "app" else { return false }

    let resourcesURL = appURL.appendingPathComponent("Contents/Resources", isDirectory: true)
    let asarURL = resourcesURL.appendingPathComponent("app.asar")
    let electronFrameworkURL = appURL
        .appendingPathComponent("Contents/Frameworks", isDirectory: true)
        .appendingPathComponent("Electron Framework.framework", isDirectory: true)

    return FileManager.default.fileExists(atPath: asarURL.path)
        || FileManager.default.fileExists(atPath: electronFrameworkURL.path)
}

func isChromiumEmbeddedApp(_ targetPath: String) -> Bool {
    let frameworkURL = URL(fileURLWithPath: targetPath)
        .appendingPathComponent("Contents/Frameworks/Chromium Embedded Framework.framework", isDirectory: true)
    return FileManager.default.fileExists(atPath: frameworkURL.path)
}

func isToDesktopApp(_ targetPath: String) -> Bool {
    let lower = targetPath.lowercased()
    if lower.contains("todesktop") { return true }

    let resourcesURL = URL(fileURLWithPath: targetPath).appendingPathComponent("Contents/Resources", isDirectory: true)
    let possibleMarkers = [
        resourcesURL.appendingPathComponent("todesktop.json"),
        resourcesURL.appendingPathComponent("app-update.yml")
    ]

    return possibleMarkers.contains { FileManager.default.fileExists(atPath: $0.path) }
}

struct ShortcutSpec: Sendable {
    var name: String
    var kind: ShortcutKind
    var targetPath: String
    var arguments: String
    var workingDirectory: String
    var outputDirectory: URL
    var environment: [EnvironmentPair]
    var isolateData: Bool
    var freshLaunch: Bool
    var backgroundLaunch: Bool
    var hiddenLaunch: Bool
    var autoBrowserProfile: Bool
}

func displaySlug(_ value: String) -> String {
    var mapped = ""
    for scalar in value.lowercased().unicodeScalars {
        switch scalar.value {
        case 48...57, 97...122:
            mapped.unicodeScalars.append(scalar)
        default:
            if scalar.isASCII {
                mapped.append("-")
            } else {
                mapped.append("-u\(String(scalar.value, radix: 16))-")
            }
        }
    }
    let slug = mapped.split(separator: "-").joined(separator: "-")
    return slug.isEmpty ? "shortcut" : slug
}

func preferredAppName(at url: URL) -> String? {
    guard url.pathExtension.lowercased() == "app" else { return nil }
    let plistURL = url.appendingPathComponent("Contents/Info.plist")
    guard
        let data = try? Data(contentsOf: plistURL),
        let object = try? PropertyListSerialization.propertyList(from: data, options: [], format: nil),
        let plist = object as? [String: Any]
    else { return nil }

    if (plist["CFBundleIdentifier"] as? String)?.lowercased() == "com.openai.codex" {
        return "Codex"
    }

    return (plist["CFBundleDisplayName"] as? String)?.trimmed.nonEmpty
        ?? (plist["CFBundleName"] as? String)?.trimmed.nonEmpty
}


extension CompatibilityProfile {
    /// The profile a user should see: recipe detection, upgraded to Tested or
    /// Verified when the exact installed version has a recorded result.
    static func resolved(kind: ShortcutKind, targetPath: String) -> CompatibilityProfile {
        var profile = analyze(kind: kind, targetPath: targetPath)
        if [.detected, .experimental].contains(profile.status),
           let recorded = VerificationLedger.status(for: profile.family, appPath: targetPath) {
            profile.status = recorded
        }
        return profile
    }
}

extension ShortcutBuilder {
    /// Default instance name for a source: `<App> Parall` for apps, the page
    /// title-ish host name for websites.
    static func defaultName(for url: URL, kind: ShortcutKind) -> String {
        let baseName = preferredAppName(at: url) ?? url.deletingPathExtension().lastPathComponent
        return kind == .app ? "\(baseName) Parall" : baseName
    }
}

enum InspectCLI {
    static func run(arguments: [String]) {
        guard let sourceIndex = arguments.firstIndex(of: "--src"), sourceIndex + 1 < arguments.count else {
            FileHandle.standardError.write(Data("usage: OpenParallel inspect --src <app> [--json]\n".utf8))
            exit(64)
        }

        let path = arguments[sourceIndex + 1]
        let url = URL(fileURLWithPath: path)
        guard FileManager.default.fileExists(atPath: path), let bundle = Bundle(url: url) else {
            FileHandle.standardError.write(Data("INSPECT FAILED: app bundle not found: \(path)\n".utf8))
            exit(1)
        }

        var profile = CompatibilityProfile.analyze(kind: .app, targetPath: path)
        let approval = RealAppApprovalLedger.approval(forAppAt: path)
        if [.detected, .experimental].contains(profile.status),
           let testedStatus = VerificationLedger.status(for: profile.family, appPath: path) {
            profile.status = testedStatus
        }
        let result: [String: Any] = [
            "path": path,
            "bundleIdentifier": bundle.bundleIdentifier ?? "",
            "version": bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            "family": profile.family.rawValue,
            "status": profile.status.rawValue,
            "multiInstance": profile.multiInstance,
            "separateData": profile.separateData,
            "supportsProfileIsolation": profile.supportsProfileIsolation,
            "launchMode": profile.launchMode,
            "normalHome": !profile.usesHomeOverride,
            "dataMode": profile.dataMode.rawValue,
            "referenceApp": CompatibilityCatalog.record(matchingAppAt: path)?.name ?? "",
            "referenceStatus": CompatibilityCatalog.record(matchingAppAt: path)?.referenceStatus.rawValue ?? "Unlisted",
            "processProfileApproval": approval == nil ? "none" : "exact-version",
            "approvalTestedAt": approval?.testedAt ?? "",
            "approvalInstances": approval?.instances ?? 0,
            "notes": profile.notes
        ]

        if arguments.contains("--json"),
           let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]),
           let text = String(data: data, encoding: .utf8) {
            print(text)
        } else {
            print("family=\(profile.family.rawValue)")
            print("status=\(profile.status.rawValue)")
            print("multiInstance=\(profile.multiInstance)")
            print("separateData=\(profile.separateData)")
            print("launchMode=\(profile.launchMode)")
            print("normalHome=\(!profile.usesHomeOverride)")
            print("dataMode=\(profile.dataMode.rawValue)")
            print("notes=\(profile.notes)")
        }
        exit(0)
    }
}

enum CompatibilityCatalogCLI {
    static func run(arguments: [String]) {
        let counts = CompatibilityCatalog.counts()
        let summary: [String: Any] = [
            "updated": CompatibilityCatalog.updated,
            "source": CompatibilityCatalog.sourceURL,
            "total": CompatibilityCatalog.records.count,
            "verified": counts[.verified, default: 0],
            "partial": counts[.partial, default: 0],
            "web": counts[.web, default: 0],
            "limited": counts[.limited, default: 0]
        ]

        if arguments.contains("--json") {
            let records: [[String: String]] = CompatibilityCatalog.records.map {
                [
                    "name": $0.name,
                    "referenceStatus": $0.referenceStatus.rawValue,
                    "openParallelPlan": $0.openParallelMode.rawValue
                ]
            }
            let output: [String: Any] = ["summary": summary, "records": records]
            guard
                let data = try? JSONSerialization.data(withJSONObject: output, options: [.prettyPrinted, .sortedKeys]),
                let text = String(data: data, encoding: .utf8)
            else {
                FileHandle.standardError.write(Data("CATALOG FAILED: could not encode JSON\n".utf8))
                exit(1)
            }
            print(text)
        } else {
            print("OpenParallel compatibility reference")
            print("updated=\(CompatibilityCatalog.updated)")
            print("total=\(CompatibilityCatalog.records.count)")
            print("verified=\(counts[.verified, default: 0])")
            print("partial=\(counts[.partial, default: 0])")
            print("web=\(counts[.web, default: 0])")
            print("limited=\(counts[.limited, default: 0])")
            for record in CompatibilityCatalog.records {
                print("\(record.name)\t\(record.referenceStatus.rawValue)\t\(record.openParallelMode.rawValue)")
            }
        }
        exit(0)
    }
}

/// Headless driver for the same update-safe signed-source shortcut pipeline as the GUI.
///   OpenParallel build --src <app> --name <name> --out <dir> [--shared-profile]
enum BuildCLI {
    static func run(arguments: [String]) {
        var opts: [String: String] = [:]
        var flags: Set<String> = []
        var index = 0
        while index < arguments.count {
            let token = arguments[index]
            if token.hasPrefix("--") {
                let key = String(token.dropFirst(2))
                if index + 1 < arguments.count, !arguments[index + 1].hasPrefix("--") {
                    opts[key] = arguments[index + 1]
                    index += 2
                } else {
                    flags.insert(key)
                    index += 1
                }
            } else {
                index += 1
            }
        }

        guard let src = opts["src"] else {
            FileHandle.standardError.write(Data("usage: OpenParallel build --src <app> --name <name> --out <dir> [--shared-profile]\n".utf8))
            exit(64)
        }
        let sourceURL = URL(fileURLWithPath: src)
        let sourceName = preferredAppName(at: sourceURL) ?? sourceURL.deletingPathExtension().lastPathComponent
        let name = opts["name"] ?? "\(sourceName) Parall"
        let outDir = opts["out"].map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications/OpenParallel Shortcuts", isDirectory: true)

        let spec = ShortcutSpec(
            name: name,
            kind: .app,
            targetPath: src,
            arguments: opts["args"] ?? "",
            workingDirectory: "",
            outputDirectory: outDir,
            environment: [],
            isolateData: !flags.contains("shared-profile"),
            freshLaunch: true,
            backgroundLaunch: false,
            hiddenLaunch: false,
            autoBrowserProfile: true
        )

        do {
            let appURL = try ShortcutBuilder.create(spec: spec)
            print("OK")
            print("wrapper=\(appURL.path)")
            print("launchMode=\(CompatibilityProfile.analyze(kind: .app, targetPath: src).launchMode)")
            exit(0)
        } catch {
            FileHandle.standardError.write(Data("BUILD FAILED: \(error.localizedDescription)\n".utf8))
            exit(1)
        }
    }

}

enum WebBuildCLI {
    static func run(arguments: [String]) {
        var options: [String: String] = [:]
        var index = 0
        while index + 1 < arguments.count {
            if arguments[index].hasPrefix("--") {
                options[String(arguments[index].dropFirst(2))] = arguments[index + 1]
                index += 2
            } else {
                index += 1
            }
        }

        guard let url = options["url"] else {
            FileHandle.standardError.write(Data("usage: OpenParallel web-build --url <https-url> --name <name> --out <dir>\n".utf8))
            exit(64)
        }
        let host = URL(string: url)?.host ?? "Web"
        let output = options["out"].map { URL(fileURLWithPath: $0, isDirectory: true) }
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications/OpenParallel Shortcuts", isDirectory: true)
        let spec = ShortcutSpec(
            name: options["name"] ?? "\(host) Parall",
            kind: .web,
            targetPath: url,
            arguments: "",
            workingDirectory: "",
            outputDirectory: output,
            environment: [],
            isolateData: true,
            freshLaunch: true,
            backgroundLaunch: false,
            hiddenLaunch: false,
            autoBrowserProfile: true
        )

        do {
            let appURL = try ShortcutBuilder.create(spec: spec)
            print("OK")
            print("wrapper=\(appURL.path)")
            print("launchMode=web-frame")
            exit(0)
        } catch {
            FileHandle.standardError.write(Data("WEB BUILD FAILED: \(error.localizedDescription)\n".utf8))
            exit(1)
        }
    }
}

enum ShortcutBuilder {
    /// One definition of "this instance gets its own data", shared by the
    /// launcher, the Info.plist, the manifest, and the sidebar so they can never
    /// disagree with each other.
    static func usesProfileIsolation(spec: ShortcutSpec, profile: CompatibilityProfile) -> Bool {
        if spec.kind == .web { return true }
        guard spec.kind == .app else { return false }
        if profile.requiresProfileIsolation { return true }
        return spec.isolateData && spec.autoBrowserProfile && profile.supportsProfileIsolation
    }

    static func create(spec: ShortcutSpec) throws -> URL {
        try validate(spec)

        let fileManager = FileManager.default

        try fileManager.createDirectory(at: spec.outputDirectory, withIntermediateDirectories: true)

        // Reserve final name first so wrapper name and profile directory always match.
        let appURL = try reserveUniqueAppURL(
            in: spec.outputDirectory,
            named: spec.name,
            reservesInstanceData: spec.kind == .app || spec.kind == .web
        )
        var resolvedSpec = spec
        resolvedSpec.name = appURL.deletingPathExtension().lastPathComponent
        let instanceRoot = instanceDataRoot(for: resolvedSpec)
        var completed = false
        defer {
            if !completed {
                try? fileManager.removeItem(at: appURL)
                if resolvedSpec.kind == .app || resolvedSpec.kind == .web {
                    try? fileManager.removeItem(at: instanceRoot)
                }
            }
        }
        let profile = CompatibilityProfile.analyze(kind: resolvedSpec.kind, targetPath: resolvedSpec.targetPath)
        #if OPENPARALLEL_PRIVATE
        if resolvedSpec.kind == .app, profile.family == .whatsAppNative {
            // Standalone copies are complete apps rather than launcher
            // wrappers, so they skip the icon/Info.plist/launcher pipeline.
            let nativeCopy = try NativeWhatsApp.build(
                sourceApp: URL(fileURLWithPath: resolvedSpec.targetPath),
                outputApp: appURL,
                instanceRoot: instanceRoot,
                displayName: resolvedSpec.name,
                instanceSlug: bundleSlug(resolvedSpec.name)
            )
            try writeInstanceManifest(spec: resolvedSpec, appURL: appURL, copyManifestFields: nativeCopy.manifestFields)
            completed = true
            return appURL
        }
        #endif

        let contentsURL = appURL.appendingPathComponent("Contents", isDirectory: true)
        let macOSURL = contentsURL.appendingPathComponent("MacOS", isDirectory: true)
        let resourcesURL = contentsURL.appendingPathComponent("Resources", isDirectory: true)

        try fileManager.createDirectory(at: macOSURL, withIntermediateDirectories: true)
        try fileManager.createDirectory(at: resourcesURL, withIntermediateDirectories: true)

        let copiedIcon = resolvedSpec.kind == .web
            ? copyWebIconIfPossible(for: resolvedSpec.targetPath, to: resourcesURL)
            : copyIconIfPossible(from: resolvedSpec.targetPath, to: resourcesURL)
        if resolvedSpec.kind == .web {
            try embedWebRunner(to: macOSURL.appendingPathComponent("OpenParallelWebRunner"))
        }
        try writeInfoPlist(spec: resolvedSpec, appURL: appURL, iconFile: copiedIcon?.lastPathComponent)
        try writeLauncher(spec: resolvedSpec, to: macOSURL.appendingPathComponent("openparallel-launch"))
        try writeInstanceManifest(spec: resolvedSpec, appURL: appURL)
        try signShortcutApp(appURL)

        completed = true
        return appURL
    }

    /// Build-time absolute path of the per-shortcut data root. Mirrors the
    /// `$HOME/Library/Application Support/OpenParallel/Instances/<slug>` path the
    /// generated launcher recomputes at runtime (same user ⇒ same path).
    private static func instanceDataRoot(for spec: ShortcutSpec) -> URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(bundleSlug(spec.name))", isDirectory: true)
    }

    private static func validate(_ spec: ShortcutSpec) throws {
        if spec.name.trimmed.isEmpty {
            throw ShortcutError.validation("Shortcut name required.")
        }

        if spec.targetPath.trimmed.isEmpty {
            throw ShortcutError.validation(spec.kind == .command ? "Command required." : "Target path required.")
        }

        if spec.kind != .command && spec.kind != .web && !FileManager.default.fileExists(atPath: spec.targetPath) {
            throw ShortcutError.validation("Target does not exist: \(spec.targetPath)")
        }

        if spec.kind == .app {
            let targetURL = URL(fileURLWithPath: spec.targetPath)
            guard targetURL.pathExtension.lowercased() == "app" else {
                throw ShortcutError.validation("App target must be a .app bundle.")
            }
            guard appExecutablePath(for: spec.targetPath) != nil else {
                throw ShortcutError.validation("App has no runnable CFBundleExecutable: \(spec.targetPath)")
            }
            let profile = CompatibilityProfile.analyze(kind: .app, targetPath: spec.targetPath)
            guard profile.multiInstance else {
                throw ShortcutError.validation(profile.notes)
            }
        }

        if spec.kind == .web {
            guard
                let url = URL(string: spec.targetPath),
                let scheme = url.scheme?.lowercased(),
                ["http", "https"].contains(scheme),
                url.host != nil
            else {
                throw ShortcutError.validation("Web target must be a valid http or https URL.")
            }
        }

        for pair in spec.environment where !pair.key.trimmed.isEmpty {
            let key = pair.key.trimmed
            guard key.range(of: #"^[A-Za-z_][A-Za-z0-9_]*$"#, options: .regularExpression) != nil else {
                throw ShortcutError.validation("Invalid environment variable name: \(key)")
            }
        }
    }

    private static func reserveUniqueAppURL(
        in outputDirectory: URL,
        named name: String,
        reservesInstanceData: Bool
    ) throws -> URL {
        let baseName = sanitizedFileName(name)
        var candidate = outputDirectory.appendingPathComponent("\(baseName).app", isDirectory: true)
        var suffix = 2
        while true {
            if !FileManager.default.fileExists(atPath: candidate.path)
                && !(reservesInstanceData && instanceDataExists(for: candidate.deletingPathExtension().lastPathComponent)) {
                do {
                    try FileManager.default.createDirectory(at: candidate, withIntermediateDirectories: false)
                    return candidate
                } catch CocoaError.fileWriteFileExists {
                    // Another builder won the same name between check and reservation.
                }
            }
            candidate = outputDirectory.appendingPathComponent("\(baseName) \(suffix).app", isDirectory: true)
            suffix += 1
        }
    }

    /// A folder blocks a name only when it holds data: an orphaned profile
    /// with files must never be adopted by a new instance, but an empty
    /// skeleton (folders a launcher created and nothing ever wrote to) can be.
    private static func instanceDataExists(for name: String) -> Bool {
        let root = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(bundleSlug(name))", isDirectory: true)
        guard FileManager.default.fileExists(atPath: root.path) else { return false }
        guard let enumerator = FileManager.default.enumerator(
            at: root,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [],
            errorHandler: { _, _ in true }
        ) else { return true }
        for case let url as URL in enumerator {
            if (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) != true { return true }
        }
        return false
    }

    private static func writeInfoPlist(spec: ShortcutSpec, appURL: URL, iconFile: String?) throws {
        let sourceBundle = Bundle(url: URL(fileURLWithPath: spec.targetPath))
        let profile = CompatibilityProfile.analyze(kind: spec.kind, targetPath: spec.targetPath)
        let usesProfileIsolation = usesProfileIsolation(spec: spec, profile: profile)
        let launchMode = spec.kind == .web ? "web-frame" : profile.launchMode
        var plist: [String: Any] = [
            "CFBundleExecutable": "openparallel-launch",
            "CFBundleIdentifier": "dev.openparallel.shortcut.\(bundleSlug(spec.name)).\(UUID().uuidString.lowercased())",
            "CFBundleName": spec.name,
            "CFBundleDisplayName": spec.name,
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": "1.0",
            "CFBundleVersion": "1",
            "LSMinimumSystemVersion": "10.13",
            "LSUIElement": spec.kind != .web,
            "NSHighResolutionCapable": true,
            "OpenParallelLaunchMode": launchMode,
            "OpenParallelInstanceSlug": bundleSlug(spec.name),
            "OpenParallelSourcePath": spec.targetPath,
            "OpenParallelSourceBundleIdentifier": sourceBundle?.bundleIdentifier ?? "",
            "OpenParallelSourceVersion": sourceBundle?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            "OpenParallelAppFamily": profile.family.rawValue,
            "OpenParallelProfileIsolation": usesProfileIsolation,
            "OpenParallelNormalHome": !profile.usesHomeOverride
        ]

        if spec.kind == .web {
            plist["OpenParallelWebDataStoreIdentifier"] = UUID().uuidString
            plist["OpenParallelWebURL"] = spec.targetPath
            plist["NSCameraUsageDescription"] = "Allow this website to use camera when you approve access."
            plist["NSMicrophoneUsageDescription"] = "Allow this website to use microphone when you approve access."
        }

        if let iconFile {
            plist["CFBundleIconFile"] = iconFile
        }

        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: appURL.appendingPathComponent("Contents/Info.plist"))
    }

    private static func writeInstanceManifest(
        spec: ShortcutSpec,
        appURL: URL,
        copyManifestFields: [String: Any]? = nil
    ) throws {
        guard spec.kind == .app || spec.kind == .web else { return }

        let sourceBundle = Bundle(url: URL(fileURLWithPath: spec.targetPath))
        let profile = CompatibilityProfile.analyze(kind: spec.kind, targetPath: spec.targetPath)
        let usesProfileIsolation = usesProfileIsolation(spec: spec, profile: profile)
        let plist = NSDictionary(contentsOf: appURL.appendingPathComponent("Contents/Info.plist"))
        var manifest: [String: Any] = [
            "schemaVersion": 1,
            "instanceName": spec.name,
            "instanceSlug": bundleSlug(spec.name),
            "wrapperPath": appURL.path,
            "launchMode": spec.kind == .web ? "web-frame" : profile.launchMode,
            "sourcePath": spec.targetPath,
            "sourceBundleIdentifier": sourceBundle?.bundleIdentifier ?? "",
            "sourceVersionAtCreation": sourceBundle?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
            "family": profile.family.rawValue,
            "profileIsolation": usesProfileIsolation,
            "normalHome": !profile.usesHomeOverride,
            "vendorBundleModified": copyManifestFields != nil,
            "createdAt": ISO8601DateFormatter().string(from: Date())
        ]

        if spec.kind == .web {
            manifest["webURL"] = spec.targetPath
            manifest["webDataStoreIdentifier"] = plist?["OpenParallelWebDataStoreIdentifier"] as? String ?? ""
        }

        if let copyManifestFields {
            manifest.merge(copyManifestFields) { _, copyValue in copyValue }
        }

        let root = instanceDataRoot(for: spec)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: root.path)
        let data = try JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: root.appendingPathComponent("instance.json"), options: .atomic)
    }

    private static func writeLauncher(spec: ShortcutSpec, to url: URL) throws {
        let script = launcherScript(for: spec)
        try script.write(to: url, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path)
    }

    private static func launcherScript(for spec: ShortcutSpec) -> String {
        let shortcutSlug = bundleSlug(spec.name)
        let profile = CompatibilityProfile.analyze(kind: spec.kind, targetPath: spec.targetPath)
        let usesProfileIsolation = usesProfileIsolation(spec: spec, profile: profile)
        let envPairs = spec.environment
            .filter { !$0.key.trimmed.isEmpty }
            .map { "envs+=(\(($0.key.trimmed + "=" + $0.value).zshQuoted))" }
            .joined(separator: "\n")

        let workingDirectoryBlock = spec.workingDirectory.trimmed.isEmpty
            ? ""
            : "cd \(spec.workingDirectory.zshQuoted)\n"

        let isolationBlock: String
        if usesProfileIsolation {
            isolationBlock = """
            DATA_ROOT="$HOME/Library/Application Support/OpenParallel/Instances/\(shortcutSlug)"
            mkdir -p "$DATA_ROOT"
            chmod 700 "$DATA_ROOT"
            """
        } else {
            isolationBlock = """
            DATA_ROOT=""
            """
        }

        let userArgsBlock = spec.arguments.trimmed.isEmpty
            ? ""
            : "user_args=\(spec.arguments.zshQuoted)\nargs+=( ${(z)user_args} )"

        let sourceBundle = Bundle(url: URL(fileURLWithPath: spec.targetPath))
        let targetBundleID = sourceBundle?.bundleIdentifier ?? ""
        let targetExecutableName = sourceBundle?.object(forInfoDictionaryKey: "CFBundleExecutable") as? String ?? ""
        let profileTracking = usesProfileIsolation

        let appProfileBlock = spec.autoBrowserProfile && spec.kind == .app
            ? profileBlock(targetPath: spec.targetPath, profile: profile)
            : ""

        let openFlags = [
            (spec.kind != .folder && (spec.freshLaunch || profileTracking)) ? "-n" : nil,
            spec.backgroundLaunch ? "-g" : nil,
            spec.hiddenLaunch ? "-j" : nil
        ]
        .compactMap { $0 }
        .map { "\"\($0)\"" }
        .joined(separator: " ")

        let openFlagsLine = "open_flags=(\(openFlags))"

        switch spec.kind {
        case .app:
            return """
            #!/bin/zsh
            set -euo pipefail

            target_app=\(spec.targetPath.zshQuoted)
            target_bundle_id=\(targetBundleID.zshQuoted)
            target_executable_name=\(targetExecutableName.zshQuoted)
            profile_tracking=\(profileTracking ? "1" : "0")
            envs=()
            args=()

            # Never pass an enclosing agent/sandbox network policy into vendor apps.
            # Explicit Environment rows below can opt back into any desired variable.
            unset HTTP_PROXY HTTPS_PROXY ALL_PROXY http_proxy https_proxy all_proxy NO_PROXY no_proxy
            unset CLAUDE_CODE_MANAGED_SETTINGS_PATH CLAUDE_CONFIG_DIR CODEX_HOME
            unset ELECTRON_RUN_AS_NODE NODE_OPTIONS

            \(isolationBlock)
            LOG_DIR="$HOME/Library/Logs/OpenParallel"
            mkdir -p "$LOG_DIR"
            LOG_FILE="$LOG_DIR/\(shortcutSlug).log"

            fail_launch() {
              print -r -- "$(date -u +%Y-%m-%dT%H:%M:%SZ) $1" >> "$LOG_FILE"
              /usr/bin/osascript -e 'display alert "OpenParallel could not launch this instance" message "Source app is missing, damaged, or rejected its isolated profile. Check ~/Library/Logs/OpenParallel for details." as critical' >/dev/null 2>&1 || true
              exit 1
            }

            if [[ ! -d "$target_app" && -n "$target_bundle_id" ]]; then
              while IFS= read -r candidate; do
                if [[ -d "$candidate" ]]; then
                  target_app="$candidate"
                  break
                fi
              done < <(/usr/bin/mdfind "kMDItemCFBundleIdentifier == '$target_bundle_id'" 2>/dev/null)
            fi

            [[ -d "$target_app" ]] || fail_launch "Source app not found: $target_app"
            current_executable_name=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$target_app/Contents/Info.plist" 2>/dev/null || true)
            if [[ -n "$current_executable_name" ]]; then
              target_executable_name="$current_executable_name"
            fi
            target_exe="$target_app/Contents/MacOS/$target_executable_name"
            [[ -x "$target_exe" ]] || fail_launch "Source executable not found: $target_exe"

            find_instance_pid() {
              [[ "$profile_tracking" == "1" && -n "$DATA_ROOT" ]] || return 1
              local candidate_pid command_line extended_command
              while read -r candidate_pid command_line; do
                if [[ "$command_line" == "$target_exe"* ]]; then
                  if [[ "$command_line" == *"$DATA_ROOT/"* ]]; then
                    print -r -- "$candidate_pid"
                    return 0
                  fi
                  extended_command=$(/bin/ps eww -p "$candidate_pid" -o command= 2>/dev/null || true)
                  if [[ "$extended_command" == *"$DATA_ROOT/"* ]]; then
                    print -r -- "$candidate_pid"
                    return 0
                  fi
                fi
              done < <(/bin/ps -axo pid=,command=)
              return 1
            }

            activate_instance() {
              local instance_pid="$1"
              /usr/bin/osascript -e "tell application \"System Events\" to set frontmost of first process whose unix id is $instance_pid to true" >/dev/null 2>&1 || true
            }

            if [[ "$profile_tracking" == "1" ]]; then
              existing_pid=$(find_instance_pid || true)
              if [[ -n "$existing_pid" ]]; then
                activate_instance "$existing_pid"
                exit 0
              fi

              launch_lock="$DATA_ROOT/.launching"
              if ! /bin/mkdir "$launch_lock" 2>/dev/null; then
                for _ in {1..40}; do
                  /bin/sleep 0.25
                  existing_pid=$(find_instance_pid || true)
                  if [[ -n "$existing_pid" ]]; then
                    activate_instance "$existing_pid"
                    exit 0
                  fi
                done
                /bin/rmdir "$launch_lock" 2>/dev/null || true
                /bin/mkdir "$launch_lock" 2>/dev/null || fail_launch "Could not acquire launch lock: $launch_lock"
              fi
              trap '/bin/rmdir "$launch_lock" 2>/dev/null || true' EXIT
            fi

            \(appProfileBlock)
            \(userArgsBlock)
            \(envPairs)
            \(workingDirectoryBlock)\(openFlagsLine)

            cmd=(/usr/bin/open)
            cmd+=("${open_flags[@]}")
            for env_pair in "${envs[@]}"; do
              cmd+=(--env "$env_pair")
            done
            cmd+=(-a "$target_app")
            if [[ ${#args[@]} -gt 0 ]]; then
              cmd+=(--args "${args[@]}")
            fi
            if ! "${cmd[@]}" >> "$LOG_FILE" 2>&1; then
              fail_launch "LaunchServices rejected source app: $target_app"
            fi

            if [[ "$profile_tracking" == "1" ]]; then
              for _ in {1..80}; do
                /bin/sleep 0.25
                existing_pid=$(find_instance_pid || true)
                if [[ -n "$existing_pid" ]]; then
                  exit 0
                fi
              done
              fail_launch "Source app did not start with profile: $DATA_ROOT"
            fi
            exit 0
            """
        case .web:
            return """
            #!/bin/zsh
            set -euo pipefail

            contents_dir="${0:A:h:h}"
            info_plist="$contents_dir/Info.plist"
            runner="$contents_dir/MacOS/OpenParallelWebRunner"
            store_id=$(/usr/libexec/PlistBuddy -c 'Print :OpenParallelWebDataStoreIdentifier' "$info_plist")
            data_root="$HOME/Library/Application Support/OpenParallel/Instances/\(shortcutSlug)"
            log_dir="$HOME/Library/Logs/OpenParallel"
            mkdir -p "$data_root" "$log_dir"
            chmod 700 "$data_root"

            unset HTTP_PROXY HTTPS_PROXY ALL_PROXY http_proxy https_proxy all_proxy NO_PROXY no_proxy
            [[ -x "$runner" ]] || exit 1
            exec "$runner" web-run \
              --url \(spec.targetPath.zshQuoted) \
              --store "$store_id" \
              --name \(spec.name.zshQuoted) \
              --log "$log_dir/\(shortcutSlug)-web.log"
            """
        case .file:
            return """
            #!/bin/zsh
            set -euo pipefail

            target_file=\(spec.targetPath.zshQuoted)
            envs=()

            \(isolationBlock)
            \(envPairs)
            \(workingDirectoryBlock)\(openFlagsLine)

            cmd=(/usr/bin/open)
            cmd+=("${open_flags[@]}")
            for env_pair in "${envs[@]}"; do
              cmd+=(--env "$env_pair")
            done
            cmd+=("$target_file")
            exec "${cmd[@]}"
            """
        case .folder:
            return """
            #!/bin/zsh
            set -euo pipefail

            target_folder=\(spec.targetPath.zshQuoted)
            \(openFlagsLine)
            cmd=(/usr/bin/open)
            cmd+=("${open_flags[@]}")
            cmd+=("$target_folder")
            exec "${cmd[@]}"
            """
        case .command:
            return """
            #!/bin/zsh
            set -euo pipefail

            envs=()
            args=()

            \(isolationBlock)
            \(userArgsBlock)
            \(envPairs)
            for env_pair in "${envs[@]}"; do
              export "$env_pair"
            done
            \(workingDirectoryBlock)
            command_text=\(spec.targetPath.zshQuoted)
            if [[ ${#args[@]} -gt 0 ]]; then
              command_text="$command_text ${(q)args[@]}"
            fi
            exec /bin/zsh -lc "$command_text"
            """
        }
    }

    private static func profileBlock(targetPath: String, profile: CompatibilityProfile) -> String {
        switch profile.family {
        case .chromium:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/chromium-profile" "$DATA_ROOT/cache/chromium"
              args+=("--user-data-dir=$DATA_ROOT/chromium-profile")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
            fi
            """

        case .firefox:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/firefox-profile"
              args+=("-profile" "$DATA_ROOT/firefox-profile" "-no-remote")
            fi
            """

        case .claude:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/electron-user-data" "$DATA_ROOT/cache/chromium"
              args+=("--user-data-dir=$DATA_ROOT/electron-user-data")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
            fi
            """

        case .codex:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/codex-user-data" "$DATA_ROOT/codex-home" "$DATA_ROOT/cache/chromium"
              args+=("--user-data-dir=$DATA_ROOT/codex-user-data")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
              envs+=("CODEX_HOME=$DATA_ROOT/codex-home")
            fi
            """

        case .antigravity:
            let bundleID = Bundle(url: URL(fileURLWithPath: targetPath))?.bundleIdentifier?.lowercased()
            if bundleID == "com.google.antigravity-ide" {
                return """
                if [[ -n "$DATA_ROOT" ]]; then
                  mkdir -p "$DATA_ROOT/antigravity-ide/user-data" "$DATA_ROOT/antigravity-ide/extensions"
                  args+=("--user-data-dir" "$DATA_ROOT/antigravity-ide/user-data")
                  args+=("--extensions-dir" "$DATA_ROOT/antigravity-ide/extensions")
                  args+=("--new-window")
                fi
                """
            }
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/antigravity-user-data" "$DATA_ROOT/cache/chromium"
              args+=("--user-data-dir=$DATA_ROOT/antigravity-user-data")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
            fi
            """

        case .electron, .toDesktop:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/electron-user-data" "$DATA_ROOT/cache/chromium"
              args+=("--user-data-dir=$DATA_ROOT/electron-user-data")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
            fi
            """

        case .discord:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/discord-root" "$DATA_ROOT/cache/chromium"
              envs+=("DISCORD_USER_DATA_DIR=$DATA_ROOT/discord-root")
              args+=("--multi-instance")
              args+=("--disk-cache-dir=$DATA_ROOT/cache/chromium")
            fi
            """

        case .spotify:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/spotify-user-data" "$DATA_ROOT/cache/spotify"
              spotify_instance="${DATA_ROOT:t}"
              args+=("--mu=$spotify_instance")
              args+=("--user-data-dir=$DATA_ROOT/spotify-user-data")
              args+=("--cache-path=$DATA_ROOT/cache/spotify")
            fi
            """

        case .jetBrains:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/jetbrains/config" "$DATA_ROOT/jetbrains/system" "$DATA_ROOT/jetbrains/plugins" "$DATA_ROOT/jetbrains/logs"
              envs+=("IDEA_PROPERTIES=$DATA_ROOT/jetbrains/idea.properties")
              cat > "$DATA_ROOT/jetbrains/idea.properties" <<EOF
            idea.config.path=$DATA_ROOT/jetbrains/config
            idea.system.path=$DATA_ROOT/jetbrains/system
            idea.plugins.path=$DATA_ROOT/jetbrains/plugins
            idea.log.path=$DATA_ROOT/jetbrains/logs
            EOF
            fi
            """

        case .eclipse:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/eclipse-workspace" "$DATA_ROOT/eclipse-config"
              args+=("-data" "$DATA_ROOT/eclipse-workspace")
              args+=("-configuration" "$DATA_ROOT/eclipse-config")
            fi
            """

        case .vscode:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/user-data" "$DATA_ROOT/extensions"
              args+=("--user-data-dir" "$DATA_ROOT/user-data")
              args+=("--extensions-dir" "$DATA_ROOT/extensions")
              args+=("--new-window")
            fi
            """

        case .telegramDesktop:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              mkdir -p "$DATA_ROOT/telegram-workdir"
              args+=("-many" "-workdir" "$DATA_ROOT/telegram-workdir")
            fi
            """

        case .homeProfile:
            return """
            if [[ -n "$DATA_ROOT" ]]; then
              REAL_HOME="$HOME"
              PROFILE_HOME="$DATA_ROOT/home"
              mkdir -p "$PROFILE_HOME/Library"
              chmod 700 "$PROFILE_HOME"
              for linked_name in Desktop Documents Downloads Movies Music Pictures Public; do
                if [[ -e "$REAL_HOME/$linked_name" && ! -e "$PROFILE_HOME/$linked_name" ]]; then
                  ln -s "$REAL_HOME/$linked_name" "$PROFILE_HOME/$linked_name"
                fi
              done
              envs+=("OPENPARALLEL_REAL_HOME=$REAL_HOME")
              envs+=("HOME=$PROFILE_HOME")
              envs+=("CFFIXED_USER_HOME=$PROFILE_HOME")
            fi
            """

        // Native WhatsApp copies never reach this launcher: they are generated as
        // complete apps with an isolated HOME baked into LSEnvironment.
        case .generic, .terminal, .apple, .sandboxed, .whatsAppNative:
            return ""
        }
    }

    private static func appExecutablePath(for targetPath: String) -> String? {
        guard
            let bundle = Bundle(url: URL(fileURLWithPath: targetPath)),
            let executableName = bundle.object(forInfoDictionaryKey: "CFBundleExecutable") as? String
        else {
            return nil
        }

        let executablePath = URL(fileURLWithPath: targetPath)
            .appendingPathComponent("Contents/MacOS", isDirectory: true)
            .appendingPathComponent(executableName)
            .path

        return FileManager.default.isExecutableFile(atPath: executablePath) ? executablePath : nil
    }

    private static func signShortcutApp(_ appURL: URL) throws {
        _ = runShortcutProcess("/usr/bin/xattr", ["-cr", appURL.path])
        let sign = runShortcutProcess("/usr/bin/codesign", ["--force", "--deep", "--sign", "-", appURL.path])
        guard sign.code == 0 else {
            throw ShortcutError.validation("Could not sign shortcut app:\n\(sign.output)")
        }
    }

    private static func embedWebRunner(to destinationURL: URL) throws {
        let fileManager = FileManager.default
        let root = URL(fileURLWithPath: fileManager.currentDirectoryPath, isDirectory: true)
        let candidates = [
            ProcessInfo.processInfo.environment["OPENPARALLEL_RUNNER_BINARY"].map { URL(fileURLWithPath: $0) },
            Bundle.main.executableURL,
            URL(fileURLWithPath: CommandLine.arguments[0]),
            root.appendingPathComponent(".build/debug/OpenParallel"),
            root.appendingPathComponent(".build/arm64-apple-macosx/debug/OpenParallel")
        ].compactMap { $0 }

        guard let sourceURL = candidates.first(where: {
            $0.lastPathComponent == "OpenParallel" && fileManager.isExecutableFile(atPath: $0.path)
        }) else {
            throw ShortcutError.validation("OpenParallel web runner binary could not be located. Build or install OpenParallel first.")
        }
        try fileManager.copyItem(at: sourceURL, to: destinationURL)
        try fileManager.setAttributes([.posixPermissions: 0o755], ofItemAtPath: destinationURL.path)
    }

    private static func copyControllerIconIfPossible(to resourcesURL: URL) -> URL? {
        guard let sourceURL = Bundle.main.url(forResource: "OpenParallel", withExtension: "icns") else {
            return nil
        }
        let destinationURL = resourcesURL.appendingPathComponent("AppIcon.icns")
        do {
            try FileManager.default.copyItem(at: sourceURL, to: destinationURL)
            return destinationURL
        } catch {
            return nil
        }
    }

    private static func copyWebIconIfPossible(for targetURL: String, to resourcesURL: URL) -> URL? {
        let host = URL(string: targetURL)?.host?.lowercased()
        let nativeAppCandidates: [String]
        switch host {
        case "web.whatsapp.com":
            nativeAppCandidates = ["/Applications/WhatsApp.app"]
        case "teams.microsoft.com":
            nativeAppCandidates = ["/Applications/Microsoft Teams.app"]
        default:
            nativeAppCandidates = []
        }

        for appPath in nativeAppCandidates where FileManager.default.fileExists(atPath: appPath) {
            if let icon = copyIconIfPossible(from: appPath, to: resourcesURL) {
                return icon
            }
        }
        return copyControllerIconIfPossible(to: resourcesURL)
    }

    private static func runShortcutProcess(_ launchPath: String, _ arguments: [String]) -> (code: Int32, output: String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: launchPath)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        do {
            try process.run()
        } catch {
            return (-1, error.localizedDescription)
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return (process.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }

    private static func copyIconIfPossible(from targetPath: String, to resourcesURL: URL) -> URL? {
        let targetURL = URL(fileURLWithPath: targetPath)
        guard targetURL.pathExtension == "app" else { return nil }
        guard
            let bundle = Bundle(url: targetURL),
            let iconName = bundle.object(forInfoDictionaryKey: "CFBundleIconFile") as? String
        else {
            return nil
        }

        let normalizedIconName = iconName.hasSuffix(".icns") ? iconName : "\(iconName).icns"
        let sourceURL = targetURL
            .appendingPathComponent("Contents/Resources", isDirectory: true)
            .appendingPathComponent(normalizedIconName)
        let destinationURL = resourcesURL.appendingPathComponent("AppIcon.icns")

        guard FileManager.default.fileExists(atPath: sourceURL.path) else { return nil }

        do {
            try FileManager.default.copyItem(at: sourceURL, to: destinationURL)
            return destinationURL
        } catch {
            return nil
        }
    }

    private static func sanitizedFileName(_ value: String) -> String {
        let forbidden = CharacterSet.controlCharacters
            .union(CharacterSet(charactersIn: "/:"))
        let characters = value.unicodeScalars.map { forbidden.contains($0) ? Character("-") : Character($0) }
        let cleaned = String(characters).trimmed
        let bounded = String(cleaned.prefix(120)).trimmed
        return bounded.isEmpty || bounded == "." || bounded == ".." ? "OpenParallel Shortcut" : bounded
    }

    private static func bundleSlug(_ value: String) -> String {
        displaySlug(value)
    }
}

enum ShortcutError: LocalizedError {
    case validation(String)

    var errorDescription: String? {
        switch self {
        case .validation(let message):
            message
        }
    }
}

extension String {
    var trimmed: String {
        trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var nonEmpty: String? {
        isEmpty ? nil : self
    }

    var zshQuoted: String {
        "'" + replacingOccurrences(of: "'", with: "'\\''") + "'"
    }
}
