import SwiftUI

/// What a status means to the person using the app. Each line states exactly
/// what was proven and nothing more: a process test is never an account test.
struct StatusPresentation {
    let title: String
    let symbol: String
    let tint: Color
    let detail: String

    static let website = StatusPresentation(
        title: "Website",
        symbol: "globe",
        tint: .blue,
        detail: "Opens in its own window with its own cookies and storage, separate from Safari and from every other website instance."
    )

    init(title: String, symbol: String, tint: Color, detail: String) {
        self.title = title
        self.symbol = symbol
        self.tint = tint
        self.detail = detail
    }

    init(_ profile: CompatibilityProfile) {
        if !profile.multiInstance {
            self.init(title: "Not supported", symbol: "nosign", tint: .red, detail: profile.notes)
            return
        }
        switch profile.status {
        case .verified:
            self.init(title: "Verified", symbol: "checkmark.seal.fill", tint: .green,
                      detail: "A real sign-in and sign-out test confirmed this app keeps accounts apart.")
        case .tested:
            self.init(title: "Tested", symbol: "checkmark.circle.fill", tint: .green,
                      detail: "This exact version ran as two separate copies with separate data on a test Mac. Signing in to two different accounts has not been checked yet.")
        case .detected:
            self.init(title: "Supported", symbol: "checkmark.circle", tint: .blue,
                      detail: "OpenParallel knows how to give this kind of app its own data folder. This exact version has not been tested yet.")
        case .partial:
            self.init(title: "Partial", symbol: "exclamationmark.triangle.fill", tint: .orange, detail: profile.notes)
        case .limited:
            self.init(title: "Not supported", symbol: "nosign", tint: .red, detail: profile.notes)
        case .experimental:
            if profile.supportsProfileIsolation || profile.requiresProfileIsolation {
                self.init(title: "Experimental", symbol: "testtube.2", tint: .orange, detail: profile.notes)
            } else {
                self.init(title: "Shares data", symbol: "arrow.triangle.2.circlepath", tint: .secondary,
                          detail: "OpenParallel can open another copy, but it has no way to give this app its own data, so every copy uses the same account.")
            }
        }
    }
}

struct StatusLabel: View {
    let status: StatusPresentation
    var compact = false

    var body: some View {
        Label(status.title, systemImage: status.symbol)
            .symbolRenderingMode(.hierarchical)
            .foregroundStyle(status.tint)
            .font(compact ? .caption : .body)
            .help(status.detail)
    }
}
