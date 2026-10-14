import AppKit
import SwiftUI

/// What an instance's "Report a Problem" button hands to the report window.
struct ReportContext: Equatable {
    var app: String
    var version: String
    var detail: String
    var kind: ProblemReport.Kind = .separate
}

/// Help › Report a Problem: build a report, read it, then send it from your own mail app (or
/// as a GitHub issue once the project is public), or copy it.
struct ReportProblemView: View {
    @EnvironmentObject private var state: AppState
    @Environment(\.openURL) private var openURL
    @State private var report = ProblemReport()
    @State private var copied = false
    @State private var showMissing = false
    @FocusState private var detailsFocused: Bool

    private let project = ProjectInfo.current

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    Picker("What happened?", selection: $report.kind) {
                        ForEach(ProblemReport.Kind.allCases) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.radioGroup)
                }

                Section {
                    TextField("App", text: $report.app, prompt: Text("For example Discord"))
                    TextField("Its version", text: $report.appVersion, prompt: Text("In the app\u{2019}s About window"))
                } header: {
                    Text("App involved")
                } footer: {
                    if !report.appDetail.isEmpty {
                        Text("From the instance: \(report.appDetail).").foregroundStyle(.secondary)
                    }
                }

                Section {
                    TextEditor(text: $report.details)
                        .font(.body)
                        .frame(minHeight: 80)
                        .focused($detailsFocused)
                        .accessibilityLabel("What happened, and what you expected instead")
                } header: {
                    Text("What happened, and what you expected instead")
                } footer: {
                    if showMissing && !report.isComplete {
                        Text("Say what happened, in a sentence or two.").foregroundStyle(.red)
                    }
                }

                Section("How to see it again (optional)") {
                    TextEditor(text: $report.steps)
                        .font(.body)
                        .frame(minHeight: 52)
                        .accessibilityLabel("How to see it again")
                }

                Section {
                    Toggle("Include the system and version lines", isOn: $report.includeSystem)
                    LabeledContent("Your system", value: report.system)
                    LabeledContent("OpenParallel", value: report.openParallelVersion)
                }

                Section("Your report") {
                    Text(report.text)
                        .font(.system(.callout, design: .monospaced))
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            .formStyle(.grouped)

            Divider()
            HStack(spacing: 10) {
                Text(note)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 12)
                Button(copied ? "Copied" : "Copy Report", action: copy)
                    .keyboardShortcut("c", modifiers: [.command, .shift])
                if let repository = project.repository {
                    Button("Open GitHub Issue") { send { report.issueURL(repository: repository) } }
                }
                Button("Send by Email") { send { project.reportAddress.flatMap(report.mailURL(to:)) } }
                    .keyboardShortcut(.defaultAction)
                    .disabled(project.reportAddress == nil)
                    .help(project.reportAddress == nil ? "The project\u{2019}s report address isn\u{2019}t set in this build." : "Opens your mail app with this report")
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
        }
        .frame(minWidth: 560, idealWidth: 600, minHeight: 560, idealHeight: 760)
        .onAppear(perform: takeContext)
        .onChange(of: state.reportContext) { _, _ in takeContext() }
        .onExitCommand { NSApp.keyWindow?.performClose(nil) }
    }

    private var note: String {
        if project.reportAddress == nil {
            return "The report address isn\u{2019}t set in this build yet. Copy the report for now."
        }
        return "Your mail app opens with this report. Nothing is sent until you press Send there."
    }

    private func takeContext() {
        guard let context = state.reportContext else { return }
        report.app = context.app
        report.appVersion = context.version
        report.appDetail = context.detail
        report.kind = context.kind
        state.reportContext = nil
        detailsFocused = true
    }

    private func copy() {
        guard checked() else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(report.text, forType: .string)
        copied = true
        Task {
            try? await Task.sleep(for: .seconds(2))
            copied = false
        }
    }

    private func send(_ url: () -> URL?) {
        guard checked(), let url = url() else { return }
        if report.isTrimmedForMail {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(report.text, forType: .string)
        }
        openURL(url)
    }

    private func checked() -> Bool {
        showMissing = !report.isComplete
        if showMissing { detailsFocused = true }
        return report.isComplete
    }
}
