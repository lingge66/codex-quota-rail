import AppKit

@MainActor
final class MacCodexLocator {
    private let targetBundleIdentifiers: Set<String>

    init(targetBundleIdentifiers: [String]) {
        self.targetBundleIdentifiers = Set(targetBundleIdentifiers)
    }

    func runningApplication() -> NSRunningApplication? {
        NSWorkspace.shared.runningApplications
            .filter { application in
                guard let identifier = application.bundleIdentifier else {
                    return false
                }
                return targetBundleIdentifiers.contains(identifier)
            }
            .sorted { left, right in
                if left.isActive != right.isActive {
                    return left.isActive
                }
                return left.processIdentifier < right.processIdentifier
            }
            .first
    }

    func appServerCandidates(for application: NSRunningApplication?) -> [URL] {
        guard let bundleURL = application?.bundleURL else {
            return []
        }
        return [
            bundleURL.appendingPathComponent("Contents/Resources/codex"),
            bundleURL.appendingPathComponent("Contents/MacOS/codex"),
        ]
    }
}
