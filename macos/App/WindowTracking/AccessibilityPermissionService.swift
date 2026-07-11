import AppKit
import ApplicationServices

@MainActor
final class AccessibilityPermissionService {
    var isTrusted: Bool {
        AXIsProcessTrusted()
    }

    func requestIfNeeded() -> Bool {
        let options = [
            kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true,
        ] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    func openSystemSettings() {
        guard let url = URL(
            string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
        else {
            return
        }
        NSWorkspace.shared.open(url)
    }
}
