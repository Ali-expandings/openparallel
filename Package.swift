// swift-tools-version: 6.0

import Foundation
import PackageDescription

// Optional private extensions live in `Private/` folders inside the targets and are
// not part of the published source. When present, they are compiled in and the
// build defines OPENPARALLEL_PRIVATE; without them OpenParallel builds and tests on
// its own.
let hasPrivateExtensions = FileManager.default.fileExists(
    atPath: Context.packageDirectory + "/Sources/OpenParallel/Private"
)
let privateSettings: [SwiftSetting] = hasPrivateExtensions ? [.define("OPENPARALLEL_PRIVATE")] : []

let package = Package(
    name: "OpenParallel",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "OpenParallel", targets: ["OpenParallel"])
    ],
    targets: [
        .executableTarget(
            name: "OpenParallel",
            path: "Sources/OpenParallel",
            swiftSettings: privateSettings
        ),
        .testTarget(
            name: "OpenParallelTests",
            dependencies: ["OpenParallel"],
            path: "Tests/OpenParallelTests",
            swiftSettings: privateSettings
        )
    ]
)
