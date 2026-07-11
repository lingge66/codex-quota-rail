import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("Codex 可执行文件发现")
struct CodexExecutableResolverTests {
    @Test("从 PATH 发现可执行文件且不使用 Shell")
    func resolvesFromPath() {
        let fileSystem = FakeFileSystem(executables: ["/opt/homebrew/bin/codex"])
        let resolver = CodexExecutableResolver(
            fileSystem: fileSystem,
            environment: ["PATH": "/usr/bin:/opt/homebrew/bin"],
            desktopCandidates: [])

        #expect(resolver.resolve() == .found(URL(fileURLWithPath: "/opt/homebrew/bin/codex")))
        #expect(resolver.launchSpec()?.arguments == ["app-server", "--listen", "stdio://"])
    }

    @Test("显式覆盖必须是绝对可执行路径")
    func rejectsUnsafeOverride() {
        let fileSystem = FakeFileSystem(executables: ["/tmp/codex"])
        let resolver = CodexExecutableResolver(
            fileSystem: fileSystem,
            environment: ["CODEX_QUOTA_RAIL_CODEX_PATH": "codex;rm -rf /"],
            desktopCandidates: [])

        #expect(resolver.resolve() == .unsupported("自定义 Codex 路径必须是绝对可执行文件。"))
    }

    @Test("没有候选时返回明确状态")
    func reportsMissing() {
        let resolver = CodexExecutableResolver(
            fileSystem: FakeFileSystem(executables: []),
            environment: [:],
            desktopCandidates: [])

        #expect(resolver.resolve() == .missing)
    }
}

private struct FakeFileSystem: FileSystemAccess {
    let executables: Set<String>

    init(executables: Set<String>) {
        self.executables = executables
    }

    func canonicalURL(for url: URL) -> URL? {
        url.isFileURL && url.path.hasPrefix("/") ? url.standardizedFileURL : nil
    }

    func isExecutableFile(at url: URL) -> Bool {
        executables.contains(url.path)
    }
}
