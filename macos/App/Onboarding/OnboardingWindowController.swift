import AppKit
import SwiftUI

@MainActor
final class OnboardingWindowController: NSWindowController {
    init(
        accessibilityPermission: AccessibilityPermissionService,
        initialLaunchAtLogin: Bool,
        onComplete: @escaping (Bool) -> Void
    ) {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 300),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = "设置 Codex 可用额度"
        window.contentView = NSHostingView(
            rootView: OnboardingView(
                accessibilityPermission: accessibilityPermission,
                initialLaunchAtLogin: initialLaunchAtLogin,
                onComplete: onComplete))
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
    }

    required init?(coder: NSCoder) {
        return nil
    }

    func show() {
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
    }
}
