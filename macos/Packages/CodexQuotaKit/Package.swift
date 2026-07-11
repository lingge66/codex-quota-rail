// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CodexQuotaKit",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "CodexQuotaKit", targets: ["CodexQuotaKit"]),
    ],
    targets: [
        .target(name: "CodexQuotaKit"),
        .testTarget(name: "CodexQuotaKitTests", dependencies: ["CodexQuotaKit"]),
    ]
)
