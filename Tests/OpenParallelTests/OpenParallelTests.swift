import Testing
import AppKit
@testable import OpenParallel

private struct FixtureApp {
    let root: URL
    let app: URL
    let plist: URL
}

private func makeFixtureApp(
    name: String,
    bundleID: String,
    executableName: String = "Fixture",
    electron: Bool = true
) throws -> FixtureApp {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("OpenParallelTests-\(UUID().uuidString)", isDirectory: true)
    let app = root.appendingPathComponent("\(name).app", isDirectory: true)
    let contents = app.appendingPathComponent("Contents", isDirectory: true)
    let macOS = contents.appendingPathComponent("MacOS", isDirectory: true)
    let resources = contents.appendingPathComponent("Resources", isDirectory: true)
    try FileManager.default.createDirectory(at: macOS, withIntermediateDirectories: true)
    try FileManager.default.createDirectory(at: resources, withIntermediateDirectories: true)

    let plist = contents.appendingPathComponent("Info.plist")
    let values: [String: Any] = [
        "CFBundleIdentifier": bundleID,
        "CFBundleExecutable": executableName,
        "CFBundleName": name,
        "CFBundleDisplayName": name,
        "CFBundlePackageType": "APPL",
        "CFBundleShortVersionString": "9.8.7"
    ]
    let plistData = try PropertyListSerialization.data(fromPropertyList: values, format: .xml, options: 0)
    try plistData.write(to: plist)

    let executable = macOS.appendingPathComponent(executableName)
    try Data("#!/bin/sh\nexit 0\n".utf8).write(to: executable)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)

    if electron {
        try Data().write(to: resources.appendingPathComponent("app.asar"))
    }

    return FixtureApp(root: root, app: app, plist: plist)
}

func makeSpec(source: URL, output: URL, name: String = "Fixture Copy") -> ShortcutSpec {
    ShortcutSpec(
        name: name,
        kind: .app,
        targetPath: source.path,
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
}

private func makeWebSpec(url: String, output: URL, name: String) -> ShortcutSpec {
    ShortcutSpec(
        name: name,
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
}

private func launcherText(in wrapper: URL) throws -> String {
    try String(contentsOf: wrapper.appendingPathComponent("Contents/MacOS/openparallel-launch"), encoding: .utf8)
}

private func assertZshSyntax(_ wrapper: URL) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/bin/zsh")
    process.arguments = ["-n", wrapper.appendingPathComponent("Contents/MacOS/openparallel-launch").path]
    try process.run()
    process.waitUntilExit()
    #expect(process.terminationStatus == 0)
}

@Test func detectsClaudeByBundleIdentifier() throws {
    let fixture = try makeFixtureApp(name: "Anything", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .claude)
    #expect(profile.supportsProfileIsolation)
    #expect(profile.separateData)
}

@Test func detectsCodexEvenWhenBundleIsNamedChatGPT() throws {
    let fixture = try makeFixtureApp(name: "ChatGPT", bundleID: "com.openai.codex")
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .codex)
    #expect(profile.supportsProfileIsolation)
}

@Test func detectsBothAntigravityBundles() throws {
    let launcher = try makeFixtureApp(name: "Antigravity", bundleID: "com.google.antigravity")
    let ide = try makeFixtureApp(name: "Antigravity IDE", bundleID: "com.google.antigravity-ide")
    defer {
        try? FileManager.default.removeItem(at: launcher.root)
        try? FileManager.default.removeItem(at: ide.root)
    }

    #expect(CompatibilityProfile.analyze(kind: .app, targetPath: launcher.app.path).family == .antigravity)
    #expect(CompatibilityProfile.analyze(kind: .app, targetPath: ide.app.path).family == .antigravity)
}

@Test func officialTelegramDesktopUsesManyAndWorkdir() throws {
    let fixture = try makeFixtureApp(name: "Telegram", bundleID: "com.tdesktop.Telegram", executableName: "Telegram", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Telegram \(UUID().uuidString)"
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .telegramDesktop)
    #expect(profile.multiInstance)
    #expect(profile.separateData)

    let wrapper = try ShortcutBuilder.create(
        spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName)
    )
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("args+=(\"-many\" \"-workdir\" \"$DATA_ROOT/telegram-workdir\")"))
    try assertZshSyntax(wrapper)
}

@Test func telegramLiteIsBlocked() throws {
    let telegram = try makeFixtureApp(name: "Telegram Lite", bundleID: "org.telegram.desktop", electron: false)
    defer { try? FileManager.default.removeItem(at: telegram.root) }

    let telegramProfile = CompatibilityProfile.analyze(kind: .app, targetPath: telegram.app.path)
    #expect(!telegramProfile.multiInstance)

    #expect(throws: ShortcutError.self) {
        try ShortcutBuilder.create(spec: makeSpec(source: telegram.app, output: telegram.root.appendingPathComponent("Output")))
    }
}


#if !OPENPARALLEL_PRIVATE
@Test func whatsAppIsLimitedAndPointsToWeb() throws {
    let fixture = try makeFixtureApp(name: "WhatsApp", bundleID: "net.whatsapp.WhatsApp", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)

    #expect(profile.status == .limited)
    #expect(profile.multiInstance == false)
    #expect(profile.separateData == false)
    #expect(profile.notes.contains("web.whatsapp.com"))
}
#endif

@Test func webShortcutsEmbedRunnerAndUseUniquePersistentStores() throws {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("OpenParallelWebTests-\(UUID().uuidString)", isDirectory: true)
    let output = root.appendingPathComponent("Output", isDirectory: true)
    let baseName = "OpenParallel Web \(UUID().uuidString)"
    defer {
        try? FileManager.default.removeItem(at: root)
        try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))"))
        try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))-2"))
    }

    let spec = makeWebSpec(url: "https://web.whatsapp.com", output: output, name: baseName)
    let first = try ShortcutBuilder.create(spec: spec)
    let second = try ShortcutBuilder.create(spec: spec)
    let firstInfo = NSDictionary(contentsOf: first.appendingPathComponent("Contents/Info.plist"))
    let secondInfo = NSDictionary(contentsOf: second.appendingPathComponent("Contents/Info.plist"))
    let firstStore = firstInfo?["OpenParallelWebDataStoreIdentifier"] as? String
    let secondStore = secondInfo?["OpenParallelWebDataStoreIdentifier"] as? String

    #expect(firstInfo?["OpenParallelLaunchMode"] as? String == "web-frame")
    #expect(firstInfo?["LSUIElement"] as? Bool == false)
    #expect(UUID(uuidString: firstStore ?? "") != nil)
    #expect(UUID(uuidString: secondStore ?? "") != nil)
    #expect(firstStore != secondStore)
    #expect(FileManager.default.isExecutableFile(atPath: first.appendingPathComponent("Contents/MacOS/OpenParallelWebRunner").path))
    #expect(try launcherText(in: first).contains("web-run"))
    try assertZshSyntax(first)
    try assertZshSyntax(second)
}

@Test func webShortcutRejectsInvalidURL() {
    let output = FileManager.default.temporaryDirectory.appendingPathComponent("OpenParallelInvalidWeb-\(UUID().uuidString)")
    #expect(throws: ShortcutError.self) {
        try ShortcutBuilder.create(spec: makeWebSpec(url: "not-a-url", output: output, name: "Invalid Web"))
    }
}

@Test func genericAppsDoNotClaimDataIsolation() throws {
    let fixture = try makeFixtureApp(name: "Unknown", bundleID: "dev.example.unknown", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .generic)
    #expect(!profile.separateData)
    #expect(!profile.supportsProfileIsolation)
}

@Test func onlyAppsOpenParallelTestedGetTheHomeRecipe() {
    #expect(RealAppApprovalLedger.isHomeProfileApp("com.sublimetext.4"))
    #expect(RealAppApprovalLedger.isHomeProfileApp("COM.MITCHELLH.GHOSTTY"))
    #expect(!RealAppApprovalLedger.isHomeProfileApp("org.blenderfoundation.blender"))
    #expect(!RealAppApprovalLedger.isHomeProfileApp("com.hnc.discord"))
}

@Test func realAppApprovalsRequireExactBundleAndVersion() {
    let approval = RealAppApprovalLedger.approval(
        bundleIdentifier: "COM.HNC.DISCORD",
        version: "0.0.402"
    )
    #expect(approval?.family == .discord)
    #expect(approval?.instances == 2)
    #expect(RealAppApprovalLedger.approval(bundleIdentifier: "com.hnc.discord", version: "0.0.403") == nil)
    #expect(RealAppApprovalLedger.approval(bundleIdentifier: "other.bundle", version: "0.0.402") == nil)
    #expect(RealAppApprovalLedger.linkedSourceApprovals.count == 36)
}

@Test func operaUsesChromiumRecipe() throws {
    let fixture = try makeFixtureApp(name: "Opera", bundleID: "com.operasoftware.opera", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .chromium)
    #expect(profile.supportsProfileIsolation)
    #expect(!profile.usesHomeOverride)
}

@Test func appNamesDoNotTriggerBrowserSubstringMatches() throws {
    let fixture = try makeFixtureApp(name: "Obsidian", bundleID: "md.obsidian")
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .electron)
}

@Test func chromiumEmbeddedFrameworkGetsChromiumRecipe() throws {
    let fixture = try makeFixtureApp(name: "CEF Fixture", bundleID: "dev.example.cef", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let cef = fixture.app.appendingPathComponent("Contents/Frameworks/Chromium Embedded Framework.framework", isDirectory: true)
    try FileManager.default.createDirectory(at: cef, withIntermediateDirectories: true)

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .chromium)
    #expect(profile.supportsProfileIsolation)
}

@Test func discordUsesDedicatedMultiInstanceRecipe() throws {
    let fixture = try makeFixtureApp(name: "Discord", bundleID: "com.hnc.discord")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Discord \(UUID().uuidString)"
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .discord)
    #expect(profile.multiInstance)
    #expect(profile.separateData)

    let wrapper = try ShortcutBuilder.create(
        spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName)
    )
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("DISCORD_USER_DATA_DIR=$DATA_ROOT/discord-root"))
    #expect(launcher.contains("args+=(\"--multi-instance\")"))
    try assertZshSyntax(wrapper)
}

@Test func spotifyUsesDedicatedMultiClientRecipe() throws {
    let fixture = try makeFixtureApp(name: "Spotify", bundleID: "com.spotify.client", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Spotify \(UUID().uuidString)"
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .spotify)
    #expect(profile.multiInstance)
    #expect(profile.separateData)

    let wrapper = try ShortcutBuilder.create(
        spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName)
    )
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("spotify_instance=\"${DATA_ROOT:t}\""))
    #expect(launcher.contains("args+=(\"--mu=$spotify_instance\")"))
    #expect(launcher.contains("args+=(\"--cache-path=$DATA_ROOT/cache/spotify\")"))
    try assertZshSyntax(wrapper)
}

@Test func pathNameDisambiguatesChatGPTClassicFromChatGPT() throws {
    let fixture = try makeFixtureApp(name: "ChatGPT Classic", bundleID: "com.openai.chat", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family != .codex)
    #expect(profile.family == .generic)
    #expect(!profile.separateData)
}

@Test func untestedNativeAppGetsNoDataClaim() throws {
    let fixture = try makeFixtureApp(name: "Blender", bundleID: "org.blenderfoundation.blender", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .generic)
    #expect(!profile.separateData)
    #expect(!profile.usesHomeOverride)
}

@Test func nativeAppTestedWithHomeGetsExperimentalHomeProfile() throws {
    let fixture = try makeFixtureApp(name: "Sublime Text", bundleID: "com.sublimetext.4", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Sublime \(UUID().uuidString)"
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .homeProfile)
    #expect(profile.status == .experimental)
    #expect(profile.separateData)
    #expect(profile.usesHomeOverride)

    let wrapper = try ShortcutBuilder.create(
        spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName)
    )
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("PROFILE_HOME=\"$DATA_ROOT/home\""))
    #expect(launcher.contains("envs+=(\"HOME=$PROFILE_HOME\")"))
    #expect(launcher.contains("ln -s \"$REAL_HOME/$linked_name\""))
    try assertZshSyntax(wrapper)

    let info = NSDictionary(contentsOf: wrapper.appendingPathComponent("Contents/Info.plist"))
    #expect(info?["OpenParallelNormalHome"] as? Bool == false)
}

@Test func copiesAreNumberedFromTwo() throws {
    #expect(Array(ShortcutBuilder.candidateNames(for: "Discord 2").prefix(3)) == ["Discord 2", "Discord 3", "Discord 4"])
    #expect(Array(ShortcutBuilder.candidateNames(for: "Work").prefix(3)) == ["Work", "Work 2", "Work 3"])
    #expect(Array(ShortcutBuilder.candidateNames(for: "Office 365").prefix(2)) == ["Office 365", "Office 365 2"])

    let fixture = try makeFixtureApp(name: "Numbered", bundleID: "dev.example.numbered")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    try FileManager.default.createDirectory(at: output.appendingPathComponent("Numbered 2.app"), withIntermediateDirectories: true)
    #expect(ShortcutBuilder.defaultName(for: fixture.app, kind: .app) == "Numbered 2")
    #expect(ShortcutBuilder.defaultName(for: fixture.app, kind: .app, in: output) == "Numbered 3")
}

@Test func unlistedAppDoesNotClaimSeparation() throws {
    let fixture = try makeFixtureApp(name: "Pages", bundleID: "dev.example.pages", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .generic)
    #expect(!profile.separateData)
    #expect(!profile.supportsProfileIsolation)
}

@Test func sandboxedAppsDoNotClaimProfileIsolation() throws {
    let fixture = try makeFixtureApp(name: "Sandboxed", bundleID: "dev.example.sandboxed")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let receipt = fixture.app.appendingPathComponent("Contents/_MASReceipt/receipt")
    try FileManager.default.createDirectory(at: receipt.deletingLastPathComponent(), withIntermediateDirectories: true)
    try Data("fixture".utf8).write(to: receipt)

    let profile = CompatibilityProfile.analyze(kind: .app, targetPath: fixture.app.path)
    #expect(profile.family == .sandboxed)
    #expect(profile.status == .partial)
    #expect(!profile.separateData)
    #expect(!profile.supportsProfileIsolation)
}

@Test func claudeWrapperUsesSignedSourceAndNormalHome() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    let sourcePlistBefore = try Data(contentsOf: fixture.plist)
    let instanceName = "OpenParallel Test Claude \(UUID().uuidString)"

    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: output, name: instanceName))
    let launcher = try launcherText(in: wrapper)

    #expect(launcher.contains("target_app='\(fixture.app.path)'"))
    #expect(launcher.contains("--user-data-dir=$DATA_ROOT/electron-user-data"))
    #expect(launcher.contains("profile_tracking=1"))
    #expect(launcher.contains("PlistBuddy -c 'Print :CFBundleExecutable'"))
    #expect(!launcher.contains("/clone/"))
    #expect(!launcher.contains("envs+=(\"HOME="))
    #expect(!launcher.contains("XDG_CONFIG_HOME"))
    #expect(!launcher.contains("XDG_DATA_HOME"))
    #expect(!launcher.contains("TMPDIR="))
    #expect(launcher.contains("unset HTTP_PROXY HTTPS_PROXY ALL_PROXY"))
    #expect(launcher.contains("unset CLAUDE_CODE_MANAGED_SETTINGS_PATH CLAUDE_CONFIG_DIR CODEX_HOME"))
    #expect(launcher.contains("chmod 700 \"$DATA_ROOT\""))
    #expect(try Data(contentsOf: fixture.plist) == sourcePlistBefore)
    try assertZshSyntax(wrapper)

    let info = NSDictionary(contentsOf: wrapper.appendingPathComponent("Contents/Info.plist"))
    #expect(info?["OpenParallelLaunchMode"] as? String == "linked-signed-source")
    #expect(info?["OpenParallelSourceBundleIdentifier"] as? String == "com.anthropic.claudefordesktop")
    #expect(info?["LSUIElement"] as? Bool == true)

    let manifest = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))/instance.json")
    #expect(FileManager.default.fileExists(atPath: manifest.path))
    let permissions = try FileManager.default.attributesOfItem(atPath: manifest.deletingLastPathComponent().path)[.posixPermissions] as? NSNumber
    #expect(permissions?.intValue == 0o700)
    try? FileManager.default.removeItem(at: manifest.deletingLastPathComponent())
}

@Test func codexUsesProductNameInsteadOfLegacyBundleDisplayName() throws {
    let fixture = try makeFixtureApp(name: "ChatGPT", bundleID: "com.openai.codex")
    defer { try? FileManager.default.removeItem(at: fixture.root) }

    #expect(preferredAppName(at: fixture.app) == "Codex")
}

@Test func repeatedNamesGetIndependentProfileSlugs() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    let baseName = "OpenParallel Repeat \(UUID().uuidString)"
    let spec = makeSpec(source: fixture.app, output: output, name: baseName)

    let first = try ShortcutBuilder.create(spec: spec)
    let second = try ShortcutBuilder.create(spec: spec)
    #expect(first.lastPathComponent == "\(baseName).app")
    #expect(second.lastPathComponent == "\(baseName) 2.app")
    #expect(try launcherText(in: second).contains("Instances/\(displaySlug(baseName))-2"))
    try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))"))
    try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))-2"))
}

@Test func orphanedProfileForcesFreshNumericSuffix() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    let baseName = "OpenParallel Orphan \(UUID().uuidString)"
    let orphan = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))", isDirectory: true)
    let fresh = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))-2", isDirectory: true)
    try FileManager.default.createDirectory(at: orphan.appendingPathComponent("electron-user-data"), withIntermediateDirectories: true)
    try Data("session".utf8).write(to: orphan.appendingPathComponent("electron-user-data/Cookies"))
    defer {
        try? FileManager.default.removeItem(at: orphan)
        try? FileManager.default.removeItem(at: fresh)
    }

    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: output, name: baseName))
    #expect(wrapper.lastPathComponent == "\(baseName) 2.app")
    #expect(try launcherText(in: wrapper).contains("Instances/\(displaySlug(baseName))-2"))
}

@Test func emptySkeletonFolderDoesNotBlockTheName() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    let baseName = "OpenParallel Skeleton \(UUID().uuidString)"
    let skeleton = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))", isDirectory: true)
    try FileManager.default.createDirectory(at: skeleton.appendingPathComponent("cache/chromium"), withIntermediateDirectories: true)
    defer { try? FileManager.default.removeItem(at: skeleton) }

    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: output, name: baseName))
    #expect(wrapper.lastPathComponent == "\(baseName).app")
}

@Test func concurrentBuildsReserveDifferentNames() async throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let output = fixture.root.appendingPathComponent("Output", isDirectory: true)
    let baseName = "OpenParallel Concurrent \(UUID().uuidString)"
    let spec = makeSpec(source: fixture.app, output: output, name: baseName)
    let firstRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))", isDirectory: true)
    let secondRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(baseName))-2", isDirectory: true)
    defer {
        try? FileManager.default.removeItem(at: firstRoot)
        try? FileManager.default.removeItem(at: secondRoot)
    }

    async let first = Task.detached { try ShortcutBuilder.create(spec: spec) }.value
    async let second = Task.detached { try ShortcutBuilder.create(spec: spec) }.value
    let (firstURL, secondURL) = try await (first, second)
    let names = Set([firstURL.lastPathComponent, secondURL.lastPathComponent])
    #expect(names == Set(["\(baseName).app", "\(baseName) 2.app"]))
}

@Test func unicodeNamesStayReadableWithASCIISafeProfileSlug() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let name = "Claude 工作"
    let slug = displaySlug(name)
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(slug)", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }

    let wrapper = try ShortcutBuilder.create(
        spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: name)
    )
    #expect(wrapper.lastPathComponent == "\(name).app")
    #expect(slug == "claude-u5de5-u4f5c")
    #expect(slug.range(of: #"^[a-z0-9-]+$"#, options: .regularExpression) != nil)
}

@Test func commandShortcutRunsWithNormalHomeAndNoProfileDirectory() throws {
    let fixture = try makeFixtureApp(name: "Unused", bundleID: "dev.example.unused", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    var environment = EnvironmentPair()
    environment.key = "OPENPARALLEL_TEST"
    environment.value = "works"
    let spec = ShortcutSpec(
        name: "OpenParallel Command \(UUID().uuidString)",
        kind: .command,
        targetPath: #"[[ "$OPENPARALLEL_TEST" == "works" && -d "$HOME" ]]"#,
        arguments: "",
        workingDirectory: fixture.root.path,
        outputDirectory: fixture.root.appendingPathComponent("Output", isDirectory: true),
        environment: [environment],
        isolateData: true,
        freshLaunch: true,
        backgroundLaunch: false,
        hiddenLaunch: false,
        autoBrowserProfile: true
    )

    let wrapper = try ShortcutBuilder.create(spec: spec)
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("DATA_ROOT=\"\""))
    #expect(!launcher.contains("Library/Application Support/OpenParallel/Instances"))
    try assertZshSyntax(wrapper)

    let process = Process()
    process.executableURL = wrapper.appendingPathComponent("Contents/MacOS/openparallel-launch")
    try process.run()
    process.waitUntilExit()
    #expect(process.terminationStatus == 0)
}

@Test func folderShortcutUsesOnlySupportedOpenFlags() throws {
    let fixture = try makeFixtureApp(name: "Unused", bundleID: "dev.example.unused", electron: false)
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let spec = ShortcutSpec(
        name: "OpenParallel Folder \(UUID().uuidString)",
        kind: .folder,
        targetPath: fixture.root.path,
        arguments: "ignored",
        workingDirectory: "",
        outputDirectory: fixture.root.appendingPathComponent("Output", isDirectory: true),
        environment: [],
        isolateData: true,
        freshLaunch: true,
        backgroundLaunch: true,
        hiddenLaunch: true,
        autoBrowserProfile: true
    )

    let wrapper = try ShortcutBuilder.create(spec: spec)
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("open_flags=(\"-g\" \"-j\")"))
    #expect(!launcher.contains("open_flags=(\"-n\""))
    #expect(launcher.contains("cmd=(/usr/bin/open)"))
    try assertZshSyntax(wrapper)
}

@Test func codexRecipeIsolatesCodexHomeWithoutReplacingHome() throws {
    let fixture = try makeFixtureApp(name: "ChatGPT", bundleID: "com.openai.codex")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Codex \(UUID().uuidString)"
    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName))
    let launcher = try launcherText(in: wrapper)

    #expect(launcher.contains("CODEX_HOME=$DATA_ROOT/codex-home"))
    #expect(launcher.contains("--user-data-dir=$DATA_ROOT/codex-user-data"))
    #expect(!launcher.contains("envs+=(\"HOME="))
    try assertZshSyntax(wrapper)
    try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))"))
}

@Test func antigravityIDERecipeUsesVSCodeProfileFlags() throws {
    let fixture = try makeFixtureApp(name: "Antigravity IDE", bundleID: "com.google.antigravity-ide", executableName: "Electron")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Antigravity \(UUID().uuidString)"
    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName))
    let launcher = try launcherText(in: wrapper)

    #expect(launcher.contains("antigravity-ide/user-data"))
    #expect(launcher.contains("--extensions-dir"))
    #expect(launcher.contains("--new-window"))
    #expect(launcher.contains("$DATA_ROOT/antigravity-ide/user-data/User/settings.json"))
    try assertZshSyntax(wrapper)
    try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))"))
}

@Test func invalidEnvironmentNamesAreRejected() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    var pair = EnvironmentPair()
    pair.key = "BAD-NAME"
    pair.value = "value"
    var spec = makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"))
    spec.environment = [pair]

    #expect(throws: ShortcutError.self) {
        try ShortcutBuilder.create(spec: spec)
    }
}

@Test func disabledProfileRecipeCannotClaimIsolation() throws {
    let fixture = try makeFixtureApp(name: "Claude", bundleID: "com.anthropic.claudefordesktop")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Shared \(UUID().uuidString)"
    let instanceRoot = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))", isDirectory: true)
    defer { try? FileManager.default.removeItem(at: instanceRoot) }
    var spec = makeSpec(
        source: fixture.app,
        output: fixture.root.appendingPathComponent("Output", isDirectory: true),
        name: instanceName
    )
    spec.autoBrowserProfile = false

    let wrapper = try ShortcutBuilder.create(spec: spec)
    let launcher = try launcherText(in: wrapper)
    #expect(launcher.contains("DATA_ROOT=\"\""))
    #expect(!launcher.contains("--user-data-dir="))

    let info = NSDictionary(contentsOf: wrapper.appendingPathComponent("Contents/Info.plist"))
    #expect(info?["OpenParallelProfileIsolation"] as? Bool == false)
    let manifestData = try Data(contentsOf: instanceRoot.appendingPathComponent("instance.json"))
    let manifest = try JSONSerialization.jsonObject(with: manifestData) as? [String: Any]
    #expect(manifest?["profileIsolation"] as? Bool == false)
}

// MARK: - Instance library

private struct LibraryFixture {
    let root: URL
    let dataRoot: URL
    let shortcuts: URL
    let library: URL
    var groups: URL { library.appendingPathComponent("Group Containers", isDirectory: true) }
}

private func makeLibraryFixture() throws -> LibraryFixture {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("OpenParallelLibrary-\(UUID().uuidString)", isDirectory: true)
    let fixture = LibraryFixture(
        root: root,
        dataRoot: root.appendingPathComponent("Instances", isDirectory: true),
        shortcuts: root.appendingPathComponent("Shortcuts", isDirectory: true),
        library: root.appendingPathComponent("Library", isDirectory: true)
    )
    for directory in [fixture.dataRoot, fixture.shortcuts, fixture.groups] {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }
    return fixture
}

@MainActor
private func makeLibrary(_ fixture: LibraryFixture) -> InstanceLibrary {
    InstanceLibrary(shortcutsDirectory: fixture.shortcuts, dataRoot: fixture.dataRoot, libraryDirectory: fixture.library,
                    remover: { try FileManager.default.removeItem(at: $0) })
}

private func writeManifest(_ values: [String: Any], slug: String, in fixture: LibraryFixture) throws {
    let directory = fixture.dataRoot.appendingPathComponent(slug, isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let data = try JSONSerialization.data(withJSONObject: values, options: [.sortedKeys])
    try data.write(to: directory.appendingPathComponent("instance.json"))
}

@discardableResult
private func writeWrapper(named name: String, in fixture: LibraryFixture, info: [String: Any] = [:]) throws -> URL {
    let wrapper = fixture.shortcuts.appendingPathComponent("\(name).app", isDirectory: true)
    let contents = wrapper.appendingPathComponent("Contents", isDirectory: true)
    try FileManager.default.createDirectory(at: contents, withIntermediateDirectories: true)
    if !info.isEmpty {
        let data = try PropertyListSerialization.data(fromPropertyList: info, format: .xml, options: 0)
        try data.write(to: contents.appendingPathComponent("Info.plist"))
    }
    return wrapper
}

@MainActor
@Test func libraryListsManifestsAlphabeticallyAndSkipsBrokenOnes() throws {
    let fixture = try makeLibraryFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    try writeManifest(["instanceName": "Older Copy", "instanceSlug": "older-copy", "createdAt": "2026-08-01T00:00:00Z"],
                      slug: "older-copy", in: fixture)
    try writeManifest(["instanceName": "Newer Copy", "instanceSlug": "newer-copy", "createdAt": "2026-08-19T00:00:00Z",
                       "family": "Chromium", "profileIsolation": true],
                      slug: "newer-copy", in: fixture)
    let broken = fixture.dataRoot.appendingPathComponent("broken", isDirectory: true)
    try FileManager.default.createDirectory(at: broken, withIntermediateDirectories: true)
    try Data("not json".utf8).write(to: broken.appendingPathComponent("instance.json"))

    let library = makeLibrary(fixture)

    #expect(library.instances.map(\.instanceName) == ["Newer Copy", "Older Copy"])
    #expect(library.instances.first?.profileIsolation == true)
    #expect(library.instances.first?.family == "Chromium")
}

@MainActor
@Test func libraryFindsWrappersByPathSlugOrNameAndListsOlderShortcuts() throws {
    let fixture = try makeLibraryFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let elsewhere = fixture.root.appendingPathComponent("Elsewhere", isDirectory: true)
    try FileManager.default.createDirectory(at: elsewhere.appendingPathComponent("Moved Copy.app"), withIntermediateDirectories: true)
    try writeManifest(["instanceName": "Moved Copy", "instanceSlug": "moved-copy",
                       "wrapperPath": elsewhere.appendingPathComponent("Moved Copy.app").path], slug: "moved-copy", in: fixture)
    try writeManifest(["instanceName": "Renamed Copy", "instanceSlug": "renamed-copy"], slug: "renamed-copy", in: fixture)
    try writeManifest(["instanceName": "Named Copy", "instanceSlug": "named-copy"], slug: "named-copy", in: fixture)
    try writeManifest(["instanceName": "Missing Copy", "instanceSlug": "missing-copy"], slug: "missing-copy", in: fixture)
    let renamed = try writeWrapper(named: "My Work Browser", in: fixture, info: ["OpenParallelInstanceSlug": "renamed-copy"])
    let named = try writeWrapper(named: "Named Copy", in: fixture)
    let older = try writeWrapper(named: "Claude Copy", in: fixture, info: ["CFBundleIdentifier": "dev.openparallel.clone.claude"])

    let library = makeLibrary(fixture)

    #expect(library.record(withSlug: "moved-copy")?.wrapperURL?.lastPathComponent == "Moved Copy.app")
    #expect(library.record(withSlug: "renamed-copy")?.wrapperURL?.standardizedFileURL == renamed.standardizedFileURL)
    #expect(library.record(withSlug: "named-copy")?.wrapperURL?.standardizedFileURL == named.standardizedFileURL)
    #expect(library.record(withSlug: "missing-copy")?.wrapperURL == nil)
    #expect(library.legacyShortcuts.map(\.standardizedFileURL) == [older.standardizedFileURL])
}

@MainActor
@Test func extensionCopyReportsDriftOnlyAfterTheSourceVersionChanges() throws {
    let fixture = try makeLibraryFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let source = try makeFixtureApp(name: "Copied App", bundleID: "com.example.copied", electron: false)
    defer { try? FileManager.default.removeItem(at: source.root) }
    let base: [String: Any] = ["instanceName": "Copied App Copy", "instanceSlug": "copied-app-copy",
                               "sourcePath": source.app.path, "vendorBundleModified": true,
                               "updatePolicy": "rebuild-required"]

    var current = base
    current["sourceVersionAtCreation"] = "9.8.7"
    try writeManifest(current, slug: "copied-app-copy", in: fixture)
    let library = makeLibrary(fixture)
    #expect(library.sourceDrifted(try #require(library.instances.first)) == false)

    var stale = base
    stale["sourceVersionAtCreation"] = "9.8.6"
    try writeManifest(stale, slug: "copied-app-copy", in: fixture)
    library.reload()
    #expect(library.sourceDrifted(try #require(library.instances.first)) == true)

    // A launcher wrapper follows its source and never needs a rebuild.
    var wrapper = stale
    wrapper["vendorBundleModified"] = false
    wrapper.removeValue(forKey: "updatePolicy")
    try writeManifest(wrapper, slug: "copied-app-copy", in: fixture)
    library.reload()
    #expect(library.sourceDrifted(try #require(library.instances.first)) == false)
}

@MainActor
@Test func deleteRemovesOnlyTheInstanceAndItsOwnGroupContainers() throws {
    let fixture = try makeLibraryFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let fm = FileManager.default

    let ownGroup = fixture.groups.appendingPathComponent("group.net.whatsapp.ab12cd34.shared", isDirectory: true)
    let originalGroup = fixture.groups.appendingPathComponent("group.net.whatsapp.WhatsApp.shared", isDirectory: true)
    let outsideGroup = fixture.root.appendingPathComponent("Elsewhere/group.net.whatsapp.ab12cd34.private", isDirectory: true)
    let neighbour = fixture.dataRoot.appendingPathComponent("other-copy", isDirectory: true)
    let logs = fixture.library.appendingPathComponent("Logs/OpenParallel", isDirectory: true)
    for directory in [ownGroup, originalGroup, outsideGroup, neighbour, logs] {
        try fm.createDirectory(at: directory, withIntermediateDirectories: true)
    }
    try Data("log".utf8).write(to: logs.appendingPathComponent("whatsapp-copy.log"))
    try Data("log".utf8).write(to: logs.appendingPathComponent("other-copy.log"))
    try writeManifest(["instanceName": "WhatsApp Copy", "instanceSlug": "whatsapp-copy",
                       "vendorBundleModified": true,
                       "groupContainerPaths": [ownGroup.path, originalGroup.path, outsideGroup.path]],
                      slug: "whatsapp-copy", in: fixture)
    let wrapper = try writeWrapper(named: "WhatsApp Copy", in: fixture)

    let library = makeLibrary(fixture)
    try library.delete(try #require(library.instances.first))

    #expect(!fm.fileExists(atPath: wrapper.path))
    #expect(!fm.fileExists(atPath: fixture.dataRoot.appendingPathComponent("whatsapp-copy").path))
    #expect(!fm.fileExists(atPath: ownGroup.path))
    #expect(!fm.fileExists(atPath: logs.appendingPathComponent("whatsapp-copy.log").path))
    #expect(fm.fileExists(atPath: originalGroup.path))
    #expect(fm.fileExists(atPath: outsideGroup.path))
    #expect(fm.fileExists(atPath: neighbour.path))
    #expect(fm.fileExists(atPath: logs.appendingPathComponent("other-copy.log").path))
    #expect(library.instances.isEmpty)
}

@MainActor
@Test func deletingAWebsiteInstanceRemovesItsWebKitDataOnly() throws {
    let fixture = try makeLibraryFixture()
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let fm = FileManager.default
    let ownID = "dev.openparallel.shortcut.mail.1111"
    let otherID = "dev.openparallel.shortcut.chat.2222"
    let own = ["WebKit", "HTTPStorages"].map { fixture.library.appendingPathComponent("\($0)/\(ownID)", isDirectory: true) }
    let other = fixture.library.appendingPathComponent("WebKit/\(otherID)", isDirectory: true)
    for directory in own + [other] { try fm.createDirectory(at: directory, withIntermediateDirectories: true) }
    try writeManifest(["instanceName": "Mail", "instanceSlug": "mail", "launchMode": "web-frame",
                       "webURL": "https://mail.example.com", "profileIsolation": true], slug: "mail", in: fixture)
    try writeWrapper(named: "Mail", in: fixture, info: ["CFBundleIdentifier": ownID, "OpenParallelInstanceSlug": "mail"])

    let library = makeLibrary(fixture)
    let record = try #require(library.record(withSlug: "mail"))
    #expect(record.isWebsite)
    try library.delete(record)

    for directory in own { #expect(!fm.fileExists(atPath: directory.path)) }
    #expect(fm.fileExists(atPath: other.path))
}

@Test func runningMatcherKeepsInstancesWithSharedNamePrefixApart() {
    let base = "/Users/me/Library/Application Support/OpenParallel/Instances"
    let exe = "/Applications/Claude.app/Contents/MacOS/"
    let targets: [InstanceProcessMatcher.Target] = [
        .init(slug: "claude-copy", executablePrefix: exe, marker: "\(base)/claude-copy/"),
        .init(slug: "claude-copy-2", executablePrefix: exe, marker: "\(base)/claude-copy-2/"),
        .init(slug: "mail", executablePrefix: "/Shortcuts/Mail.app/Contents/MacOS/OpenParallelWebRunner", marker: "")
    ]
    let processes = InstanceProcessMatcher.parse(psOutput: """
      101 /Applications/Claude.app/Contents/MacOS/Claude --user-data-dir=\(base)/claude-copy-2/electron-user-data
      102 /Applications/Claude.app/Contents/Frameworks/Claude Helper.app/Contents/MacOS/Claude Helper --user-data-dir=\(base)/claude-copy/electron-user-data
      103 /Applications/Claude.app/Contents/MacOS/Claude
      104 /Shortcuts/Mail.app/Contents/MacOS/OpenParallelWebRunner web-run --url https://mail.example.com
    """)

    let result = InstanceProcessMatcher.match(targets, in: processes)

    #expect(processes.map(\.pid) == [101, 102, 103, 104])
    #expect(result.running["claude-copy-2"] == [101])
    #expect(result.running["claude-copy"] == nil)
    #expect(result.running["mail"] == [104])
    // The original app (no data folder in its arguments) needs an environment check.
    #expect(result.needsEnvironment == [103])
}

// MARK: - Report a Problem

@Test func problemReportUsesTheSharedFormat() {
    var report = ProblemReport()
    report.kind = .separate
    report.app = "Discord"
    report.appVersion = "0.0.407"
    report.appDetail = "Electron, Tested"
    report.details = "Both copies show my main account."
    report.steps = "1. Open Discord Copy"
    report.system = "macOS 15.6 (Apple silicon)"
    report.openParallelVersion = "1.0.0 (macOS)"
    #expect(report.subject == "[OpenParallel] An app doesn\u{2019}t keep its data separate: Discord")
    #expect(report.body == """
        What happened: An app doesn\u{2019}t keep its data separate
        App: Discord 0.0.407 (Electron, Tested)
        OpenParallel: 1.0.0 (macOS)
        System: macOS 15.6 (Apple silicon)
        ---
        Both copies show my main account.

        How to see it again:
        1. Open Discord Copy
        """)
    report.includeSystem = false
    #expect(!report.body.contains("System:"))
}

@Test func problemReportBuildsMailAndIssueLinks() throws {
    var report = ProblemReport()
    report.details = "It crashed & burned"
    let mail = try #require(report.mailURL(to: "reports@example.org"))
    #expect(mail.scheme == "mailto")
    #expect(mail.absoluteString.hasPrefix("mailto:reports@example.org?subject="))
    #expect(mail.absoluteString.contains("crashed%20%26%20burned"))
    let issue = try #require(report.issueURL(repository: "https://github.com/example/openparallel/"))
    #expect(issue.absoluteString.hasPrefix("https://github.com/example/openparallel/issues/new?title="))
    report.details = String(repeating: "x", count: ProblemReport.mailLimit + 50)
    #expect(report.isTrimmedForMail)
    #expect(try #require(report.mailURL(to: "a@b.c")).absoluteString.contains("Trimmed"))
    #expect(!ProblemReport().isComplete)
}

@Test func vsCodeFamilyCopiesLeaveUpdatesToTheOriginal() throws {
    // Windows showed it first: a Cursor copy ran its own updater against the shared install.
    let fixture = try makeFixtureApp(name: "Cursor", bundleID: "com.todesktop.230313mzl4w4u92", executableName: "Cursor")
    defer { try? FileManager.default.removeItem(at: fixture.root) }
    let instanceName = "OpenParallel Test Cursor \(UUID().uuidString)"
    let wrapper = try ShortcutBuilder.create(spec: makeSpec(source: fixture.app, output: fixture.root.appendingPathComponent("Output"), name: instanceName))
    let launcher = try launcherText(in: wrapper)

    #expect(launcher.contains("--extensions-dir"))
    #expect(launcher.contains("$DATA_ROOT/user-data/User/settings.json"))
    #expect(launcher.contains("\\\"update.mode\\\": \\\"none\\\""))
    try assertZshSyntax(wrapper)
    try? FileManager.default.removeItem(at: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/OpenParallel/Instances/\(displaySlug(instanceName))"))
}

@MainActor @Test func websiteCopiesIntroduceThemselvesAsTheInstalledSafari() throws {
    // WhatsApp Web refused WebKit's default user agent ("WhatsApp works with Safari 15+").
    let folder = FileManager.default.temporaryDirectory.appendingPathComponent("OpenParallel-Safari-\(UUID().uuidString)")
    try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    defer { try? FileManager.default.removeItem(at: folder) }
    let plist = folder.appendingPathComponent("Info.plist")
    try (["CFBundleShortVersionString": "27.0.1"] as NSDictionary).write(to: plist)
    #expect(WebRunner.safariApplicationName(safariInfoPlist: plist) == "Version/27.0 Safari/605.1.15")
    #expect(WebRunner.safariApplicationName(safariInfoPlist: folder.appendingPathComponent("missing.plist")) == "Version/18.0 Safari/605.1.15")
}

@MainActor @Test func aboutShowsTheSignatureAndOnlyWebLinks() throws {
    let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    let shipped = try JSONDecoder().decode(ProjectInfo.self, from: Data(contentsOf: root.appendingPathComponent("project.json")))
    #expect(shipped.signature == "Made by Ali Assem, The Tech Butcher")
    #expect(shipped.authorLinks.map(\.label) == ["GitHub", "LinkedIn", "Instagram", "Linktree"])

    let json = #"{ "author": { "signature": " Made by A ", "links": [ { "label": "Site", "url": "https://a.example" }, { "label": "Mail", "url": "mailto:a@b.c" }, { "label": "", "url": "https://b.example" } ] } }"#
    let project = try JSONDecoder().decode(ProjectInfo.self, from: Data(json.utf8))
    #expect(project.authorLinks.map(\.label) == ["Site"])
    let credits = try #require(AboutPanel.credits(project))
    #expect(credits.string == "Made by A\nSite")
    #expect(credits.attribute(.link, at: credits.length - 1, effectiveRange: nil) as? URL == URL(string: "https://a.example"))
    #expect(AboutPanel.credits(ProjectInfo()) == nil)
}
