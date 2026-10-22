import Foundation

struct RealAppApproval: Sendable {
    let bundleIdentifier: String
    let version: String
    let appName: String
    let family: AppFamily
    let testedAt: String
    let instances: Int

    var key: String {
        "\(bundleIdentifier.lowercased())\u{0}\(version)"
    }
}

/// Exact app versions that passed multi-process, distinct-profile tests as linked
/// signed source. These are process/profile approvals, not account-login verification.
enum RealAppApprovalLedger {
    #if OPENPARALLEL_PRIVATE
    static let approvals: [RealAppApproval] = linkedSourceApprovals + PrivateExtensions.approvals
    #else
    static let approvals: [RealAppApproval] = linkedSourceApprovals
    #endif

    static let linkedSourceApprovals: [RealAppApproval] = [
        .init(bundleIdentifier: "app.zen-browser.zen", version: "1.21.10b", appName: "Zen", family: .firefox, testedAt: "2026-08-02T11:49:30Z", instances: 2),
        .init(bundleIdentifier: "com.anthropic.claudefordesktop", version: "1.24012.9", appName: "Claude", family: .claude, testedAt: "2026-08-02T11:49:39Z", instances: 2),
        .init(bundleIdentifier: "com.brave.Browser", version: "151.1.93.129", appName: "Brave Browser", family: .chromium, testedAt: "2026-08-02T12:39:42Z", instances: 2),
        .init(bundleIdentifier: "com.google.antigravity", version: "2.2.1", appName: "Antigravity", family: .antigravity, testedAt: "2026-08-02T11:49:59Z", instances: 2),
        .init(bundleIdentifier: "com.google.antigravity-ide", version: "2.1.1", appName: "Antigravity IDE", family: .antigravity, testedAt: "2026-08-02T11:50:11Z", instances: 2),
        .init(bundleIdentifier: "com.google.GeminiMacOS", version: "1.84.4.574", appName: "Gemini", family: .homeProfile, testedAt: "2026-08-02T11:52:57Z", instances: 2),
        .init(bundleIdentifier: "com.googlecode.iterm2", version: "3.6.11", appName: "iTerm2", family: .homeProfile, testedAt: "2026-08-02T12:40:30Z", instances: 2),
        .init(bundleIdentifier: "com.hnc.Discord", version: "0.0.402", appName: "Discord", family: .discord, testedAt: "2026-08-02T11:42:40Z", instances: 2),
        .init(bundleIdentifier: "com.microsoft.VSCode", version: "1.127.0", appName: "Visual Studio Code", family: .vscode, testedAt: "2026-08-02T11:39:49Z", instances: 2),
        .init(bundleIdentifier: "com.mitchellh.ghostty", version: "1.3.1", appName: "Ghostty", family: .homeProfile, testedAt: "2026-08-02T12:34:35Z", instances: 2),
        .init(bundleIdentifier: "com.openai.codex", version: "26.721.81911", appName: "Codex", family: .codex, testedAt: "2026-08-02T11:49:48Z", instances: 2),
        .init(bundleIdentifier: "com.spotify.client", version: "1.2.94.583", appName: "Spotify", family: .spotify, testedAt: "2026-08-02T11:44:28Z", instances: 2),
        .init(bundleIdentifier: "com.sublimetext.4", version: "Build 4200", appName: "Sublime Text", family: .homeProfile, testedAt: "2026-08-02T12:33:32Z", instances: 2),
        .init(bundleIdentifier: "com.tdesktop.Telegram", version: "7.0.7", appName: "Telegram Desktop", family: .telegramDesktop, testedAt: "2026-08-02T11:39:17Z", instances: 2),
        .init(bundleIdentifier: "com.todesktop.230313mzl4w4u92", version: "3.10.11", appName: "Cursor", family: .vscode, testedAt: "2026-08-02T11:40:00Z", instances: 2),
        .init(bundleIdentifier: "md.obsidian", version: "1.12.7", appName: "Obsidian", family: .electron, testedAt: "2026-08-02T11:39:43Z", instances: 2),
        .init(bundleIdentifier: "net.ankiweb.anki", version: "26.8", appName: "Anki", family: .homeProfile, testedAt: "2026-08-02T12:37:51Z", instances: 2),
        .init(bundleIdentifier: "org.audacityteam.audacity", version: "3.7.8.0", appName: "Audacity", family: .homeProfile, testedAt: "2026-08-02T12:35:21Z", instances: 2),
        .init(bundleIdentifier: "org.jkiss.dbeaver.core.product", version: "26.1.3", appName: "DBeaver Community", family: .eclipse, testedAt: "2026-08-02T12:33:24Z", instances: 2),
        .init(bundleIdentifier: "org.mozilla.firefox", version: "153.0.1", appName: "Firefox", family: .firefox, testedAt: "2026-08-02T11:39:32Z", instances: 2),
        .init(bundleIdentifier: "org.torproject.torbrowser", version: "15.0.6", appName: "Tor Browser", family: .firefox, testedAt: "2026-08-02T11:48:44Z", instances: 2),
        .init(bundleIdentifier: "org.whispersystems.signal-desktop", version: "8.21.0", appName: "Signal", family: .toDesktop, testedAt: "2026-08-02T12:36:00Z", instances: 2),
        .init(bundleIdentifier: "app.zen-browser.zen", version: "1.21.14b", appName: "Zen", family: .firefox, testedAt: "2026-08-18T12:31:00Z", instances: 2),
        .init(bundleIdentifier: "ai.opencode.desktop", version: "1.17.16", appName: "OpenCode", family: .toDesktop, testedAt: "2026-08-18T12:32:15Z", instances: 2),
        .init(bundleIdentifier: "com.anthropic.claudefordesktop", version: "1.32352.1", appName: "Claude", family: .claude, testedAt: "2026-08-18T12:31:26Z", instances: 2),
        .init(bundleIdentifier: "com.google.antigravity-ide", version: "2.5.5", appName: "Antigravity IDE", family: .antigravity, testedAt: "2026-08-18T12:31:59Z", instances: 2),
        .init(bundleIdentifier: "com.hnc.Discord", version: "0.0.407", appName: "Discord", family: .discord, testedAt: "2026-08-18T12:31:47Z", instances: 2),
        .init(bundleIdentifier: "com.openai.codex", version: "26.803.61601", appName: "Codex", family: .codex, testedAt: "2026-08-18T12:31:34Z", instances: 2),
        .init(bundleIdentifier: "com.siddharthvaddem.openscreen", version: "1.4.0", appName: "Openscreen", family: .electron, testedAt: "2026-08-18T12:32:21Z", instances: 2),
        .init(bundleIdentifier: "com.spotify.client", version: "1.2.96.518", appName: "Spotify", family: .spotify, testedAt: "2026-08-18T12:31:52Z", instances: 2),
        .init(bundleIdentifier: "com.timpler.screenstudio", version: "3.6.0-4214", appName: "Screen Studio", family: .electron, testedAt: "2026-08-18T12:32:27Z", instances: 2),
        .init(bundleIdentifier: "org.mozilla.firefox", version: "153.0.4", appName: "Firefox", family: .firefox, testedAt: "2026-08-18T12:31:20Z", instances: 2),
        .init(bundleIdentifier: "com.anthropic.claudefordesktop", version: "1.32885.1", appName: "Claude", family: .claude, testedAt: "2026-08-19T06:27:12Z", instances: 2),
        .init(bundleIdentifier: "com.brave.Browser", version: "151.1.93.136", appName: "Brave Browser", family: .chromium, testedAt: "2026-08-19T06:40:30Z", instances: 2),
        .init(bundleIdentifier: "com.googlecode.iterm2", version: "3.6.10", appName: "iTerm2", family: .homeProfile, testedAt: "2026-08-19T06:27:18Z", instances: 2),
        .init(bundleIdentifier: "com.vivaldi.Vivaldi", version: "8.1.4087.64", appName: "Vivaldi", family: .chromium, testedAt: "2026-08-19T06:44:09Z", instances: 2)
    ]

    private static let approvalsByKey = Dictionary(uniqueKeysWithValues: approvals.map { ($0.key, $0) })

    static func approval(bundleIdentifier: String?, version: String?) -> RealAppApproval? {
        guard let bundleIdentifier, !bundleIdentifier.isEmpty, let version, !version.isEmpty else { return nil }
        return approvalsByKey["\(bundleIdentifier.lowercased())\u{0}\(version)"]
    }

    /// Apps OpenParallel itself tested with a per-instance HOME (any version).
    /// Only these get the HOME recipe; an app nobody here tested gets none.
    static func isHomeProfileApp(_ bundleIdentifier: String) -> Bool {
        homeProfileBundleIDs.contains(bundleIdentifier.lowercased())
    }

    private static let homeProfileBundleIDs = Set(
        linkedSourceApprovals.filter { $0.family == .homeProfile }.map { $0.bundleIdentifier.lowercased() }
    )

    static func approval(forAppAt path: String) -> RealAppApproval? {
        guard let bundle = Bundle(url: URL(fileURLWithPath: path)) else { return nil }
        return approval(
            bundleIdentifier: bundle.bundleIdentifier,
            version: bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        )
    }
}
