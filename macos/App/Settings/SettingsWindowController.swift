import AppKit
import SwiftUI

@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    init(
        model: ApplicationModel,
        accessibilityPermission: AccessibilityPermissionService,
        launchAtLoginService: LaunchAtLoginService,
        onLaunchAtLoginChanged: @escaping @MainActor @Sendable (Bool) -> Void
    ) {
        let content = SettingsView(
            model: model,
            accessibilityPermission: accessibilityPermission,
            launchAtLoginService: launchAtLoginService,
            onLaunchAtLoginChanged: onLaunchAtLoginChanged)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 500, height: 440),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false)
        window.title = "Codex 可用额度设置"
        window.contentView = NSHostingView(rootView: content)
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        window.delegate = self
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
