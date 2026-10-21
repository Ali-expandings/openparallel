import AppKit
import WebKit

@MainActor
enum WebRunner {
    private static var retainedDelegate: WebRunnerDelegate?
    private static var terminationSource: DispatchSourceSignal?

    static func run(arguments: [String]) -> Never {
        let options = parse(arguments)
        guard
            let rawURL = options["url"],
            let url = URL(string: rawURL),
            let rawStore = options["store"],
            let storeIdentifier = UUID(uuidString: rawStore)
        else {
            FileHandle.standardError.write(Data("usage: OpenParallel web-run --url <https-url> --store <uuid> --name <name> [--log <path>]\n".utf8))
            exit(64)
        }

        let delegate = WebRunnerDelegate(
            url: url,
            storeIdentifier: storeIdentifier,
            name: options["name"] ?? url.host ?? "OpenParallel Web",
            logPath: options["log"]
        )
        retainedDelegate = delegate

        // OpenParallel's Quit falls back to SIGTERM; quit through AppKit
        // instead of dying mid-write so website data is saved.
        signal(SIGTERM, SIG_IGN)
        let source = DispatchSource.makeSignalSource(signal: SIGTERM, queue: .main)
        source.setEventHandler { NSApplication.shared.terminate(nil) }
        source.resume()
        terminationSource = source

        let application = NSApplication.shared
        application.setActivationPolicy(.regular)
        application.delegate = delegate
        application.run()
        exit(0)
    }

    /// The end of the user agent: Safari's own "Version/x Safari/605.1.15". WebKit's default user
    /// agent leaves it out, and some sites then refuse the page as an outdated Safari (WhatsApp
    /// Web: "WhatsApp works with Safari 15+"). The web view is the WebKit the installed Safari
    /// uses, so it says Safari's version.
    static func safariApplicationName(safariInfoPlist: URL = URL(fileURLWithPath: "/Applications/Safari.app/Contents/Info.plist")) -> String {
        let installed = (NSDictionary(contentsOf: safariInfoPlist)?["CFBundleShortVersionString"] as? String)?
            .split(separator: ".").prefix(2).joined(separator: ".")
        let version = installed.flatMap { Double($0) != nil ? $0 : nil } ?? "18.0"
        return "Version/\(version) Safari/605.1.15"
    }

    private static func parse(_ arguments: [String]) -> [String: String] {
        var result: [String: String] = [:]
        var index = 0
        while index + 1 < arguments.count {
            let token = arguments[index]
            if token.hasPrefix("--") {
                result[String(token.dropFirst(2))] = arguments[index + 1]
                index += 2
            } else {
                index += 1
            }
        }
        return result
    }
}

@MainActor
private final class WebRunnerDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate {
    private let url: URL
    private let storeIdentifier: UUID
    private let name: String
    private let logPath: String?
    private var mainWindow: NSWindow?
    private var popupWindows: [ObjectIdentifier: NSWindow] = [:]

    init(url: URL, storeIdentifier: UUID, name: String, logPath: String?) {
        self.url = url
        self.storeIdentifier = storeIdentifier
        self.name = name
        self.logPath = logPath
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let configuration = WKWebViewConfiguration()
        if #available(macOS 14.0, *) {
            configuration.websiteDataStore = WKWebsiteDataStore(forIdentifier: storeIdentifier)
        } else {
            // Unique shortcut bundle identifiers keep default stores separate on macOS 13.
            configuration.websiteDataStore = .default()
        }
        configuration.applicationNameForUserAgent = WebRunner.safariApplicationName()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true
        configuration.allowsAirPlayForMediaPlayback = true

        let webView = configuredWebView(configuration: configuration)
        let window = makeWindow(contentView: webView, title: name, size: NSSize(width: 1180, height: 820))
        mainWindow = window
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        webView.load(URLRequest(url: url))
        appendLog("START url=\(url.absoluteString) store=\(storeIdentifier.uuidString)")
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        appendLog("LOADED url=\(webView.url?.absoluteString ?? "") title=\(webView.title ?? "") store=\(storeIdentifier.uuidString)")
        if webView === mainWindow?.contentView {
            mainWindow?.title = webView.title?.isEmpty == false ? webView.title! : name
        }
    }

    func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        guard navigationAction.targetFrame == nil else { return nil }
        let popup = configuredWebView(configuration: configuration)
        let window = makeWindow(contentView: popup, title: name, size: NSSize(width: 760, height: 720))
        popupWindows[ObjectIdentifier(popup)] = window
        window.makeKeyAndOrderFront(nil)
        if let request = navigationAction.request.url {
            popup.load(URLRequest(url: request))
        }
        return popup
    }

    func webViewDidClose(_ webView: WKWebView) {
        popupWindows.removeValue(forKey: ObjectIdentifier(webView))?.close()
    }

    func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationAction: WKNavigationAction,
        decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void
    ) {
        guard let destination = navigationAction.request.url else {
            decisionHandler(.cancel)
            return
        }
        if let scheme = destination.scheme?.lowercased(), !["http", "https", "about", "blob", "data"].contains(scheme) {
            NSWorkspace.shared.open(destination)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    private func configuredWebView(configuration: WKWebViewConfiguration) -> WKWebView {
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsMagnification = true
        return webView
    }

    private func makeWindow(contentView: NSView, title: String, size: NSSize) -> NSWindow {
        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = title
        window.contentView = contentView
        window.center()
        window.delegate = self
        return window
    }

    private func appendLog(_ line: String) {
        guard let logPath else { return }
        let url = URL(fileURLWithPath: logPath)
        let timestamp = ISO8601DateFormatter().string(from: Date())
        let data = Data("\(timestamp) \(line)\n".utf8)
        if !FileManager.default.fileExists(atPath: logPath) {
            FileManager.default.createFile(atPath: logPath, contents: data)
            return
        }
        guard let handle = try? FileHandle(forWritingTo: url) else { return }
        defer { try? handle.close() }
        _ = try? handle.seekToEnd()
        try? handle.write(contentsOf: data)
    }
}
