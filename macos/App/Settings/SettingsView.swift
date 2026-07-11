import CodexQuotaKit
import SwiftUI

struct SettingsView: View {
    @ObservedObject var model: ApplicationModel
    let accessibilityPermission: AccessibilityPermissionService
    let launchAtLoginService: LaunchAtLoginService
    let onLaunchAtLoginChanged: (Bool) -> Void

    @State private var websiteText: String
    @State private var bundleIdentifiersText: String
    @State private var customThemePathText: String

    init(
        model: ApplicationModel,
        accessibilityPermission: AccessibilityPermissionService,
        launchAtLoginService: LaunchAtLoginService,
        onLaunchAtLoginChanged: @escaping (Bool) -> Void
    ) {
        self.model = model
        self.accessibilityPermission = accessibilityPermission
        self.launchAtLoginService = launchAtLoginService
        self.onLaunchAtLoginChanged = onLaunchAtLoginChanged
        _websiteText = State(initialValue: model.settings.websiteURL.absoluteString)
        _bundleIdentifiersText = State(
            initialValue: model.settings.targetBundleIdentifiers.joined(separator: ", "))
        _customThemePathText = State(initialValue: model.settings.customThemePath ?? "")
    }

    var body: some View {
        Form {
            Picker("主题", selection: themeBinding) {
                Text("自动").tag(ThemePreference.automatic)
                Text("深色").tag(ThemePreference.dark)
                Text("浅色").tag(ThemePreference.light)
                Text("自定义").tag(ThemePreference.custom)
            }
            Toggle("减少动画", isOn: reduceMotionBinding)
            Toggle("登录时启动", isOn: launchAtLoginBinding)
            LabeledContent("前台透明度") {
                Slider(value: focusedOpacityBinding, in: 0.2 ... 1, step: 0.01)
                    .frame(width: 220)
            }
            LabeledContent("失焦透明度") {
                Slider(value: unfocusedOpacityBinding, in: 0.2 ... 1, step: 0.01)
                    .frame(width: 220)
            }
            LabeledContent("普通轨高度") {
                Slider(value: railHeightBinding, in: 16 ... 40, step: 1)
                    .frame(width: 220)
            }
            LabeledContent("紧凑轨高度") {
                Slider(value: compactHeightBinding, in: 1 ... 8, step: 1)
                    .frame(width: 220)
            }
            TextField("个人网站 HTTPS 地址", text: $websiteText)
                .onSubmit(saveWebsite)
            TextField("自定义主题 JSON 绝对路径", text: $customThemePathText)
                .onSubmit(saveCustomTheme)
            TextField("目标 Bundle Identifier，用逗号分隔", text: $bundleIdentifiersText)
                .onSubmit(saveBundleIdentifiers)
            HStack {
                Text(accessibilityPermission.isTrusted ? "辅助功能权限已启用" : "辅助功能权限未启用")
                    .foregroundStyle(accessibilityPermission.isTrusted ? Color.secondary : Color.red)
                Spacer()
                Button("打开系统设置") {
                    accessibilityPermission.openSystemSettings()
                }
            }
            if let error = model.lastErrorMessage {
                Text(error)
                    .foregroundStyle(.red)
            }
        }
        .formStyle(.grouped)
        .padding(12)
        .frame(width: 500, height: 440)
        .onDisappear {
            saveWebsite()
            saveCustomTheme()
            saveBundleIdentifiers()
        }
    }

    private var themeBinding: Binding<ThemePreference> {
        Binding(
            get: { model.settings.theme },
            set: { value in model.mutateSettings { $0.theme = value } })
    }

    private var reduceMotionBinding: Binding<Bool> {
        Binding(
            get: { model.settings.reduceMotion },
            set: { value in model.mutateSettings { $0.reduceMotion = value } })
    }

    private var launchAtLoginBinding: Binding<Bool> {
        Binding(
            get: { model.settings.launchAtLogin },
            set: onLaunchAtLoginChanged)
    }

    private var focusedOpacityBinding: Binding<Double> {
        Binding(
            get: { model.settings.focusedOpacity },
            set: { value in model.mutateSettings { $0.focusedOpacity = value } })
    }

    private var unfocusedOpacityBinding: Binding<Double> {
        Binding(
            get: { model.settings.unfocusedOpacity },
            set: { value in model.mutateSettings { $0.unfocusedOpacity = value } })
    }

    private var railHeightBinding: Binding<Double> {
        Binding(
            get: { model.settings.railHeight },
            set: { value in model.mutateSettings { $0.railHeight = value } })
    }

    private var compactHeightBinding: Binding<Double> {
        Binding(
            get: { model.settings.compactHeight },
            set: { value in model.mutateSettings { $0.compactHeight = value } })
    }

    private func saveWebsite() {
        guard let url = URL(string: websiteText), ExternalURLPolicy.allows(url) else {
            websiteText = model.settings.websiteURL.absoluteString
            return
        }
        model.mutateSettings { $0.websiteURL = url }
    }

    private func saveBundleIdentifiers() {
        let values = bundleIdentifiersText
            .split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        model.mutateSettings { $0.targetBundleIdentifiers = values }
        bundleIdentifiersText = model.settings.targetBundleIdentifiers.joined(separator: ", ")
    }

    private func saveCustomTheme() {
        let path = customThemePathText.trimmingCharacters(in: .whitespacesAndNewlines)
        model.mutateSettings {
            $0.customThemePath = path.isEmpty ? nil : path
            if !path.isEmpty {
                $0.theme = .custom
            }
        }
        customThemePathText = model.settings.customThemePath ?? ""
    }
}
