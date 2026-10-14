import Foundation

enum ReferenceCompatibilityStatus: String, Codable, CaseIterable {
    case verified = "Verified"
    case partial = "Partial support"
    case web = "Web app shortcut"
    case limited = "Limited support"
}

enum OpenParallelSupportMode: String, Codable {
    case runtimeDetection = "Runtime detection required"
    case experimentalHome = "Experimental HOME profile"
    case launchOnly = "Launch only"
    case webRequired = "Web shortcut required"
    case macOSLimited = "Limited by macOS"
}

struct CompatibilityCatalogRecord: Codable, Equatable {
    let name: String
    let referenceStatus: ReferenceCompatibilityStatus

    var openParallelMode: OpenParallelSupportMode {
        switch referenceStatus {
        case .verified:
            .runtimeDetection
        case .partial:
            .launchOnly
        case .web:
            .webRequired
        case .limited:
            .macOSLimited
        }
    }
}

/// Public compatibility matrix used as a clean-room test target. Entries are
/// metadata only; OpenParallel never downloads or copies Parall presets/code.
enum CompatibilityCatalog {
    static let sourceURL = "https://parall.app/compatibility/"
    static let updated = "2026-08-18"

    static let records: [CompatibilityCatalogRecord] = source
        .split(separator: "\n")
        .compactMap { line in
            let fields = line.split(separator: "|", maxSplits: 1).map(String.init)
            guard fields.count == 2, let status = ReferenceCompatibilityStatus(rawValue: fields[1]) else {
                return nil
            }
            return CompatibilityCatalogRecord(name: fields[0], referenceStatus: status)
        }

    static func record(matchingAppAt targetPath: String) -> CompatibilityCatalogRecord? {
        let url = URL(fileURLWithPath: targetPath)
        let bundle = Bundle(url: url)
        let candidates = [
            url.deletingPathExtension().lastPathComponent,
            bundle?.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String,
            bundle?.object(forInfoDictionaryKey: "CFBundleName") as? String
        ].compactMap { $0 }

        for rawName in candidates {
            let normalizedName = normalized(rawName)
            let alias = aliases[normalizedName] ?? normalizedName
            if let record = records.first(where: { normalized($0.name) == alias }) {
                return record
            }
        }
        return nil
    }

    static func counts() -> [ReferenceCompatibilityStatus: Int] {
        Dictionary(grouping: records, by: \.referenceStatus).mapValues(\.count)
    }

    private static func normalized(_ value: String) -> String {
        value.lowercased().unicodeScalars
            .filter { CharacterSet.alphanumerics.contains($0) }
            .map(String.init)
            .joined()
    }

    private static let aliases: [String: String] = [
        "chrome": "googlechrome",
        "firefox": "mozillafirefox",
        "edge": "microsoftedge",
        "codex": "openaicodex",
        "atlas": "openaiatlas",
        "comet": "perplexitycomet",
        "antigravity": "googleantigravity",
        "antigravityide": "googleantigravity",
        "code": "visualstudiocode",
        "iterm": "iterm2",
        "zoomus": "zoom",
        "zen": "zenbrowser",
        "floorpbrowser": "floorp",
        "tor": "torbrowser",
        "telegram": "telegramdesktop",
        "wechatdevtools": "wechatdevtools"
    ]

    private static let source = #"""
8x8 Work|Verified
AdsPower|Verified
Amazon WorkSpaces|Verified
Android Studio|Verified
Anki|Verified
Any iOS or iPadOS app|Limited support
Arduino IDE|Verified
Asana|Verified
Audacity|Verified
Beeper Desktop|Verified
Beyond Compare|Verified
Bitwarden|Verified
Blender|Verified
Blocs|Verified
Brave|Verified
Capacities|Verified
Charles|Verified
ChatGPT|Verified
ChatGPT Classic|Web app shortcut
Chrome-based apps|Verified
Chrome-based browsers|Verified
Chromium|Verified
Claude|Verified
Close.io|Verified
Compressor|Partial support
Conductor|Verified
Cursor|Verified
DBeaver|Verified
Dia Browser|Verified
Discord|Verified
Dropbox|Verified
Eagle.cool|Verified
Eclipse|Verified
Eclipse-based apps|Verified
Electron-based apps|Verified
Emacs|Verified
Evernote|Verified
FileMaker Pro|Verified
Firefox-based Apps|Verified
Floorp|Verified
FlowFinder|Partial support
Fluent Reader|Verified
Fork|Verified
FreeCAD|Verified
Gemini|Verified
Ghostty|Verified
Git Tower|Verified
GlotShot: Screenshot Maker|Verified
Google Antigravity|Verified
Google Chrome|Verified
IPTVBoss|Verified
iTerm2|Verified
JetBrains|Verified
Jump Desktop (Website)|Verified
Kadeck Desktop|Verified
Keynote|Partial support
KiCad|Verified
LightBurn|Verified
Lightkey|Partial support
Mattermost Desktop|Verified
Meru|Verified
Microsoft Edge|Verified
Microsoft Outlook|Web app shortcut
Microsoft Teams|Web app shortcut
MikroTik WinBox|Verified
Monday.com|Partial support
Mozilla Firefox|Verified
Notion|Verified
Numbers|Partial support
OBS|Verified
Obsidian|Verified
OpenAI Atlas|Verified
OpenAI Codex|Verified
Opera|Verified
OrcaSlicer|Verified
Orion Browser|Verified
Pages|Partial support
Perplexity Comet|Verified
Philips Hue Sync|Verified
Pixelmator Pro|Partial support
Plex|Verified
QQ|Verified
Qt Creator|Verified
RemNote|Verified
Safari|Partial support
Sandboxed apps - other platforms|Partial support
Sandboxed Chrome apps|Verified
Sandboxed Eclipse apps|Verified
Sandboxed Electron apps|Verified
Sandboxed Firefox apps|Verified
SatDump|Verified
Scapple|Partial support
Scrivener|Partial support
Signal|Verified
Slack|Verified
Spark Desktop|Verified
Spotify|Verified
Strawberry Browser|Verified
Sublime Merge|Verified
Sublime Text|Verified
Telegram Desktop|Verified
Terminal|Partial support
TeXstudio|Verified
ToDesktop-based apps|Verified
Tor Browser|Verified
TradingView|Verified
UpNote|Verified
Viber|Verified
Visual Studio Code|Verified
Vivaldi|Verified
Web App Shortcuts|Web app shortcut
Wechat Devtools|Verified
WhatsApp|Web app shortcut
Windsurf|Verified
Xcode|Verified
Yuanbao (Website)|Partial support
Zed|Partial support
Zen Browser|Verified
Zoom|Verified
"""#
}
