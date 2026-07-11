import SwiftUI

struct OnboardingView: View {
    let accessibilityPermission: AccessibilityPermissionService
    let initialLaunchAtLogin: Bool
    let onComplete: (Bool) -> Void

    @State private var launchAtLogin: Bool

    init(
        accessibilityPermission: AccessibilityPermissionService,
        initialLaunchAtLogin: Bool,
        onComplete: @escaping (Bool) -> Void
    ) {
        self.accessibilityPermission = accessibilityPermission
        self.initialLaunchAtLogin = initialLaunchAtLogin
        self.onComplete = onComplete
        _launchAtLogin = State(initialValue: initialLaunchAtLogin)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("欢迎使用 Codex 可用额度")
                .font(.system(size: 22, weight: .semibold))
            Text("应用只读取 Codex 窗口的位置、大小和焦点，用于显示边缘额度轨。不会读取屏幕、聊天、项目或账号令牌。")
                .font(.system(size: 13))
                .foregroundStyle(.secondary)
            GroupBox("辅助功能权限") {
                HStack {
                    Text(accessibilityPermission.isTrusted ? "已授权" : "需要授权")
                    Spacer()
                    Button("请求授权") {
                        _ = accessibilityPermission.requestIfNeeded()
                    }
                    Button("打开系统设置") {
                        accessibilityPermission.openSystemSettings()
                    }
                }
                .padding(4)
            }
            Toggle("登录时自动启动", isOn: $launchAtLogin)
            HStack {
                Spacer()
                Button("开始使用") {
                    onComplete(launchAtLogin)
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 520, height: 300)
    }
}
