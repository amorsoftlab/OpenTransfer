// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "OpenTransferMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(
            name: "OpenTransferMac",
            targets: ["OpenTransferMac"]
        )
    ],
    targets: [
        .executableTarget(
            name: "OpenTransferMac",
            path: "Sources"
        )
    ]
)
