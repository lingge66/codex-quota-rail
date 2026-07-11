// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CodexQuotaRailMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CodexQuotaRailMac", targets: ["CodexQuotaRailMac"]),
    ],
    dependencies: [
        .package(path: "Packages/CodexQuotaKit"),
    ],
    targets: [
        .executableTarget(
            name: "CodexQuotaRailMac",
            dependencies: [
                .product(name: "CodexQuotaKit", package: "CodexQuotaKit"),
            ],
            path: "App",
            exclude: ["Resources"]),
    ]
)
