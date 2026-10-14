import Foundation

/// Public project facts, read from `project.json` in the app bundle (the same file the website
/// and the Windows/Linux app read). Empty values hide what needs them.
struct ProjectInfo: Decodable {
    var version: String?
    var reportEmail: String?
    var repositoryUrl: String?

    static let current: ProjectInfo = {
        guard let url = Bundle.main.url(forResource: "project", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let info = try? JSONDecoder().decode(ProjectInfo.self, from: data)
        else { return ProjectInfo() }
        return info
    }()

    var reportAddress: String? { reportEmail.flatMap { $0.trimmingCharacters(in: .whitespaces).isEmpty ? nil : $0 } }
    var repository: String? { repositoryUrl.flatMap { $0.trimmingCharacters(in: .whitespaces).isEmpty ? nil : $0 } }
}

/// One report, in the format every OpenParallel surface uses (Mac, Windows, Linux, website),
/// so reports arrive the same way whatever sent them. Nothing here sends anything: the
/// person's own mail app or browser does, after they have read it.
struct ProblemReport: Equatable {
    enum Kind: String, CaseIterable, Identifiable {
        case bug, separate, idea, other
        var id: String { rawValue }
        var title: String {
            switch self {
            case .bug: "Something doesn\u{2019}t work"
            case .separate: "An app doesn\u{2019}t keep its data separate"
            case .idea: "An idea or an app to support"
            case .other: "Something else"
            }
        }
    }

    var kind: Kind = .bug
    var app = ""
    var appVersion = ""
    /// Recipe family and status of the instance the report started from, e.g. "Electron, Tested".
    var appDetail = ""
    var details = ""
    var steps = ""
    var includeSystem = true
    var system = ProblemReport.systemDescription
    var openParallelVersion = ProblemReport.appVersion

    static var systemDescription: String {
        let v = ProcessInfo.processInfo.operatingSystemVersion
        #if arch(arm64)
        let arch = "Apple silicon"
        #else
        let arch = "Intel"
        #endif
        return "macOS \(v.majorVersion).\(v.minorVersion)\(v.patchVersion > 0 ? ".\(v.patchVersion)" : "") (\(arch))"
    }

    static var appVersion: String {
        let bundle = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        return "\(bundle ?? ProjectInfo.current.version ?? "development build") (macOS)"
    }

    var subject: String {
        let name = app.trimmingCharacters(in: .whitespacesAndNewlines)
        return "[OpenParallel] \(kind.title)\(name.isEmpty ? "" : ": \(name)")"
    }

    var body: String {
        var lines = ["What happened: \(kind.title)"]
        let appLine = [app, appVersion].map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }.joined(separator: " ")
        if !appLine.isEmpty { lines.append("App: \(appLine)\(appDetail.isEmpty ? "" : " (\(appDetail))")") }
        if includeSystem {
            lines.append("OpenParallel: \(openParallelVersion)")
            lines.append("System: \(system)")
        }
        lines.append("---")
        let text = details.trimmingCharacters(in: .whitespacesAndNewlines)
        lines.append(text.isEmpty ? "(what happened)" : text)
        let how = steps.trimmingCharacters(in: .whitespacesAndNewlines)
        if !how.isEmpty { lines += ["", "How to see it again:", how] }
        return lines.joined(separator: "\n")
    }

    var isComplete: Bool { !details.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    /// The full text, for Copy Report.
    var text: String { "Subject: \(subject)\n\n\(body)" }

    /// Mail apps drop long `mailto:` bodies, so a long report is trimmed there and the full
    /// text goes to the clipboard (the caller copies it when `isTrimmed(for:)`).
    static let mailLimit = 1800

    func mailURL(to address: String) -> URL? {
        let full = body
        let mailBody = full.count > Self.mailLimit
            ? String(full.prefix(Self.mailLimit)) + "\n\n(Trimmed. The full report is on your clipboard; paste it here.)"
            : full
        var components = URLComponents()
        components.scheme = "mailto"
        components.path = address
        components.queryItems = [URLQueryItem(name: "subject", value: subject), URLQueryItem(name: "body", value: mailBody)]
        return components.url
    }

    var isTrimmedForMail: Bool { body.count > Self.mailLimit }

    func issueURL(repository: String) -> URL? {
        var base = repository
        while base.hasSuffix("/") { base.removeLast() }
        var components = URLComponents(string: "\(base)/issues/new")
        components?.queryItems = [
            URLQueryItem(name: "title", value: subject.replacingOccurrences(of: "[OpenParallel] ", with: "")),
            URLQueryItem(name: "body", value: body),
        ]
        return components?.url
    }
}
