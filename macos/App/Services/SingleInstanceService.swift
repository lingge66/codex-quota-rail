import AppKit

@MainActor
final class SingleInstanceService {
    static let activationNotification = Notification.Name(
        "dev.lingge.codexquotarail.activate")

    func claimPrimaryInstance() -> Bool {
        guard let bundleIdentifier = Bundle.main.bundleIdentifier else {
            return true
        }
        let currentPID = ProcessInfo.processInfo.processIdentifier
        let duplicate = NSRunningApplication.runningApplications(
            withBundleIdentifier: bundleIdentifier)
            .contains { $0.processIdentifier != currentPID }
        if duplicate {
            DistributedNotificationCenter.default().post(
                name: Self.activationNotification,
                object: bundleIdentifier)
            return false
        }
        return true
    }
}
