import AppKit
import CodexQuotaKit
import Combine

@MainActor
final class MenuBarController: NSObject {
    private let model: ApplicationModel
    private let onCommand: (MenuCommand) -> Void
    private let statusItem: NSStatusItem
    private var cancellables = Set<AnyCancellable>()

    init(model: ApplicationModel, onCommand: @escaping (MenuCommand) -> Void) {
        self.model = model
        self.onCommand = onCommand
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        configureStatusItem()
        observeModel()
        render()
    }

    func close() {
        NSStatusBar.system.removeStatusItem(statusItem)
    }

    private func configureStatusItem() {
        statusItem.autosaveName = "CodexQuotaRail.StatusItem"
        let brandImage: NSImage?
        if let url = Bundle.main.url(forResource: "LingGe", withExtension: "icns") {
            brandImage = NSImage(contentsOf: url)
        } else {
            brandImage = nil
        }
        brandImage?.size = NSSize(width: 18, height: 18)
        statusItem.button?.image = brandImage ?? NSImage(
            systemSymbolName: "gauge.with.dots.needle.67percent",
            accessibilityDescription: "Codex 可用额度")
        statusItem.button?.imagePosition = .imageLeading
    }

    private func observeModel() {
        model.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] in
                DispatchQueue.main.async { self?.render() }
            }
            .store(in: &cancellables)
    }

    private func render() {
        statusItem.button?.title = statusTitle
        statusItem.button?.toolTip = "Codex 可用额度 · \(statusText)"
        statusItem.menu = buildMenu()
    }

    private func buildMenu() -> NSMenu {
        let menu = NSMenu(title: "Codex 可用额度")
        menu.autoenablesItems = false
        menu.addItem(disabledItem("状态：\(statusText)"))
        menu.addItem(commandItem("设置…", .openSettings))
        menu.addItem(commandItem("立即刷新", .refresh))
        menu.addItem(commandItem(
            model.settings.followPaused ? "恢复窗口跟随" : "暂停窗口跟随",
            .toggleFollow))
        menu.addItem(.separator())
        menu.addItem(themeMenuItem())
        menu.addItem(checkedCommandItem("减少动画", .toggleReduceMotion, model.settings.reduceMotion))
        menu.addItem(checkedCommandItem("登录时启动", .toggleLaunchAtLogin, model.settings.launchAtLogin))
        menu.addItem(.separator())
        menu.addItem(commandItem("检查更新", .checkUpdates))
        menu.addItem(commandItem("打开日志目录", .openLogs))
        menu.addItem(commandItem("故障排查", .troubleshoot))
        menu.addItem(commandItem("领哥个人网站", .openWebsite))
        menu.addItem(.separator())
        menu.addItem(commandItem("退出", .quit))
        return menu
    }

    private func themeMenuItem() -> NSMenuItem {
        let item = NSMenuItem(title: "主题", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "主题")
        submenu.addItem(checkedCommandItem(
            "自动",
            .themeAutomatic,
            model.settings.theme == .automatic))
        submenu.addItem(checkedCommandItem(
            "深色",
            .themeDark,
            model.settings.theme == .dark))
        submenu.addItem(checkedCommandItem(
            "浅色",
            .themeLight,
            model.settings.theme == .light))
        item.submenu = submenu
        return item
    }

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    private func commandItem(_ title: String, _ command: MenuCommand) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(handleCommand(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = command.rawValue
        item.isEnabled = true
        return item
    }

    private func checkedCommandItem(
        _ title: String,
        _ command: MenuCommand,
        _ checked: Bool
    ) -> NSMenuItem {
        let item = commandItem(title, command)
        item.state = checked ? .on : .off
        return item
    }

    @objc private func handleCommand(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let command = MenuCommand(rawValue: rawValue)
        else {
            return
        }
        onCommand(command)
    }

    private var statusTitle: String {
        let available = model.displayState.windows.compactMap(\.availablePercent).min()
        return available.map { " \($0)%" } ?? ""
    }

    private var statusText: String {
        switch model.displayState.connection {
        case .connecting:
            model.displayState.message ?? "正在连接 Codex"
        case .live:
            "额度已更新"
        case .stale:
            "连接暂时中断"
        case .authenticationRequired:
            "请先登录 Codex"
        case .unsupported:
            "当前 Codex 版本暂不支持"
        case .unavailable:
            "Codex 暂不可用"
        }
    }
}
