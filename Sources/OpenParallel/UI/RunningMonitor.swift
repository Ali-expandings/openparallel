import AppKit
import Combine
import Foundation

/// Which process belongs to which instance. Mirrors the launcher's own rule: a
/// process is an instance's when it runs the source app's main executable and
/// its arguments or environment point inside that instance's data folder.
/// Helper processes (renderers, GPU) live under Frameworks and never match.
enum InstanceProcessMatcher {
    struct Target: Sendable, Equatable {
        let slug: String
        /// The command must start with this, e.g. `/Applications/Claude.app/Contents/MacOS/`.
        let executablePrefix: String
        /// Must appear in arguments or environment, e.g. `<data root>/`. Empty
        /// means the executable alone identifies the instance (website runners
        /// live inside their own wrapper).
        let marker: String
    }

    struct Process: Sendable, Equatable {
        let pid: Int32
        let command: String
    }

    static func targets(for records: [InstanceRecord]) -> [Target] {
        records.compactMap { record in
            if record.isWebsite, let wrapper = record.wrapperURL {
                return Target(
                    slug: record.instanceSlug,
                    executablePrefix: wrapper.appendingPathComponent("Contents/MacOS/OpenParallelWebRunner").path,
                    marker: ""
                )
            }
            // Without its own data folder an instance's process is
            // indistinguishable from the original app.
            guard record.profileIsolation, !record.sourcePath.isEmpty else { return nil }
            let executableDirectory = URL(fileURLWithPath: record.sourcePath)
                .appendingPathComponent("Contents/MacOS", isDirectory: true).path + "/"
            return Target(slug: record.instanceSlug, executablePrefix: executableDirectory, marker: record.dataRoot.path + "/")
        }
    }

    /// Matches processes by command line. Returns the matches and the PIDs that
    /// run a target's executable but whose arguments did not name a data folder,
    /// so their environment must be checked (recipes that only set env vars).
    static func match(_ targets: [Target], in processes: [Process]) -> (running: [String: [Int32]], needsEnvironment: [Int32]) {
        var running: [String: [Int32]] = [:]
        var pending: [Int32] = []
        for process in processes {
            let candidates = targets.filter { process.command.hasPrefix($0.executablePrefix) }
            guard !candidates.isEmpty else { continue }
            if let target = candidates.first(where: { $0.marker.isEmpty || process.command.contains($0.marker) }) {
                running[target.slug, default: []].append(process.pid)
            } else {
                pending.append(process.pid)
            }
        }
        return (running, pending)
    }

    static func parse(psOutput: String) -> [Process] {
        psOutput.split(separator: "\n").compactMap { line in
            let trimmed = line.drop { $0 == " " }
            guard let space = trimmed.firstIndex(of: " "), let pid = Int32(trimmed[..<space]) else { return nil }
            return Process(pid: pid, command: String(trimmed[trimmed.index(after: space)...]))
        }
    }

    /// Runs `ps` (twice at most) and returns running PIDs per instance slug.
    static func scan(_ targets: [Target]) -> [String: [Int32]] {
        guard !targets.isEmpty else { return [:] }
        let processes = parse(psOutput: ps(["-axww", "-o", "pid=,command="]))
        var (running, pending) = match(targets, in: processes)
        if !pending.isEmpty {
            let pidList = pending.map(String.init).joined(separator: ",")
            let withEnvironment = parse(psOutput: ps(["eww", "-p", pidList, "-o", "pid=,command="]))
            let extra = match(targets.filter { !$0.marker.isEmpty }, in: withEnvironment).running
            for (slug, pids) in extra { running[slug, default: []].append(contentsOf: pids) }
        }
        return running
    }

    private static func ps(_ arguments: [String]) -> String {
        let process = Foundation.Process()
        process.executableURL = URL(fileURLWithPath: "/bin/ps")
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        guard (try? process.run()) != nil else { return "" }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return String(decoding: data, as: UTF8.self)
    }
}

/// Keeps `running` current: refreshes when any app launches or quits, when
/// OpenParallel comes forward, and on a slow timer as a fallback.
@MainActor
final class RunningMonitor: ObservableObject {
    @Published private(set) var running: [String: [Int32]] = [:]

    private var targets: [InstanceProcessMatcher.Target] = []
    private var observers: [NSObjectProtocol] = []
    private var timer: Timer?
    private var pending: Task<Void, Never>?

    init() {
        let center = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refresh(after: 0.6) }
            })
        }
        observers.append(NotificationCenter.default.addObserver(
            forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        })
        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    func track(_ records: [InstanceRecord]) {
        let next = InstanceProcessMatcher.targets(for: records)
        guard next != targets else { return }
        targets = next
        refresh()
    }

    func isRunning(_ record: InstanceRecord) -> Bool {
        !(running[record.instanceSlug] ?? []).isEmpty
    }

    /// Launchers wait for the app to settle, so check again a few times.
    func refreshSoon() {
        for delay in [1.0, 3.0, 7.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                MainActor.assumeIsolated { self?.refresh() }
            }
        }
    }

    func refresh(after delay: TimeInterval = 0) {
        pending?.cancel()
        let targets = self.targets
        pending = Task { [weak self] in
            if delay > 0 { try? await Task.sleep(for: .seconds(delay)) }
            guard !Task.isCancelled else { return }
            let result = await Task.detached(priority: .utility) { InstanceProcessMatcher.scan(targets) }.value
            guard !Task.isCancelled else { return }
            self?.running = result
        }
    }

    /// Brings a running instance to the front using the process this monitor
    /// already identified. Needs no Automation permission, unlike the
    /// launcher's System Events fallback. Returns false if nothing came forward.
    @discardableResult
    func activate(_ record: InstanceRecord) -> Bool {
        for pid in running[record.instanceSlug] ?? [] {
            guard let app = NSRunningApplication(processIdentifier: pid), !app.isTerminated else { continue }
            NSApp.yieldActivation(to: app)
            if app.activate(from: .current, options: [.activateAllWindows]) { return true }
            if app.activate(options: [.activateAllWindows]) { return true }
        }
        return false
    }

    /// Asks the instance to quit the way the Dock does. A website window is
    /// started by its launcher script and does not receive that request, so
    /// whatever is still identified as this instance two seconds later gets
    /// SIGTERM, the standard polite quit signal. Only processes the matcher
    /// re-identifies as this instance are ever signalled.
    func quit(_ record: InstanceRecord) {
        for pid in running[record.instanceSlug] ?? [] {
            NSRunningApplication(processIdentifier: pid)?.terminate()
        }
        let slug = record.instanceSlug
        let targets = self.targets.filter { $0.slug == slug }
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(2))
            let survivors = await Task.detached(priority: .utility) {
                InstanceProcessMatcher.scan(targets)[slug] ?? []
            }.value
            for pid in survivors { kill(pid, SIGTERM) }
            self?.refresh(after: survivors.isEmpty ? 0 : 1)
        }
        refresh(after: 1.0)
    }
}
