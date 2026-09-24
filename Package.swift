// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MacGuard",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "MacGuard",
            path: "Sources/MacGuard"
        )
    ]
)
