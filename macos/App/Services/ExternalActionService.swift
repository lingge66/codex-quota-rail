import AppKit
import CodexQuotaKit

@MainActor
final class ExternalActionService {
    private let logDirectoryURL: URL
    private let releaseURL: URL

    init(logDirectoryURL: URL, updateRepository: String) {
        self.logDirectoryURL = logDirectoryURL
        releaseURL = URL(
            string: "https://github.com/\(updateRepository)/releases")
            ?? URL(string: "https://github.com/lingge66/codex-quota-rail/releases")!
    }

    func openWebsite(_ url: URL) {
        openHTTPS(url)
    }

    func checkUpdates() {
        openHTTPS(releaseURL)
    }

    func openLogs() {
        try? FileManager.default.createDirectory(
            at: logDirectoryURL,
            withIntermediateDirectories: true)
        NSWorkspace.shared.open(logDirectoryURL)
    }

    func showTroubleshooting() {
        let alert = NSAlert()
        alert.messageText = "Codex 额度故障排查"
        alert.informativeText = "1. 确认 Codex 已启动并登录。\n2. 检查辅助功能权限。\n3. 从菜单栏选择“立即刷新”。\n4. 如仍不可用，请打开日志目录。"
        alert.alertStyle = .informational
        alert.addButton(withTitle: "好")
        alert.runModal()
    }

    private func openHTTPS(_ url: URL) {
        guard ExternalURLPolicy.allows(url) else {
            return
        }
        NSWorkspace.shared.open(url)
    }
}
