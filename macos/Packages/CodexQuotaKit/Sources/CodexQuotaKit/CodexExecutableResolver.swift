import Foundation

public protocol FileSystemAccess: Sendable {
    func canonicalURL(for url: URL) -> URL?
    func isExecutableFile(at url: URL) -> Bool
}

public struct LocalFileSystemAccess: FileSystemAccess {
    public init() {}

    public func canonicalURL(for url: URL) -> URL? {
        guard url.isFileURL, url.path.hasPrefix("/") else {
            return nil
        }
        return url.resolvingSymlinksInPath().standardizedFileURL
    }

    public func isExecutableFile(at url: URL) -> Bool {
        FileManager.default.isExecutableFile(atPath: url.path)
    }
}

public enum CodexExecutableResolution: Equatable, Sendable {
    case found(URL)
    case missing
    case unsupported(String)
}

public struct ProcessLaunchSpec: Equatable, Sendable {
    public let executableURL: URL
    public let arguments: [String]

    public init(executableURL: URL, arguments: [String]) {
        self.executableURL = executableURL
        self.arguments = arguments
    }
}

public struct CodexExecutableResolver<FileSystem: FileSystemAccess>: Sendable {
    private let fileSystem: FileSystem
    private let environment: [String: String]
    private let desktopCandidates: [URL]

    public init(
        fileSystem: FileSystem,
        environment: [String: String],
        desktopCandidates: [URL]
    ) {
        self.fileSystem = fileSystem
        self.environment = environment
        self.desktopCandidates = desktopCandidates
    }

    public func resolve() -> CodexExecutableResolution {
        if let override = environment["CODEX_QUOTA_RAIL_CODEX_PATH"], !override.isEmpty {
            guard override.hasPrefix("/") else {
                return .unsupported("自定义 Codex 路径必须是绝对可执行文件。")
            }
            return resolvedCandidate(URL(fileURLWithPath: override))
                ?? .unsupported("自定义 Codex 路径必须是绝对可执行文件。")
        }
        for candidate in pathCandidates() + desktopCandidates {
            if let resolved = resolvedCandidate(candidate) {
                return resolved
            }
        }
        return .missing
    }

    public func launchSpec() -> ProcessLaunchSpec? {
        guard case let .found(executableURL) = resolve() else {
            return nil
        }
        return ProcessLaunchSpec(
            executableURL: executableURL,
            arguments: ["app-server", "--listen", "stdio://"])
    }

    private func pathCandidates() -> [URL] {
        (environment["PATH"] ?? "")
            .split(separator: ":")
            .map(String.init)
            .filter { !$0.isEmpty && $0.hasPrefix("/") }
            .map { URL(fileURLWithPath: $0).appendingPathComponent("codex") }
    }

    private func resolvedCandidate(_ candidate: URL) -> CodexExecutableResolution? {
        guard let canonical = fileSystem.canonicalURL(for: candidate),
              fileSystem.isExecutableFile(at: canonical)
        else {
            return nil
        }
        return .found(canonical)
    }
}
