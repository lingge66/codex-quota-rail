import AppKit
import CodexQuotaKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var model: ApplicationModel!
    private var permissionService: AccessibilityPermissionService!
    private var launchAtLoginService: LaunchAtLoginService!
    private var externalActions: ExternalActionService!
    private var applicationLog: ApplicationLog!
    private var railController: RailPanelController!
    private var menuController: MenuBarController!
    private var windowTracker: AccessibilityWindowTracker?
    private var rateCoordinator: RateLimitCoordinator?
    private var windowTrackingTask: Task<Void, Never>?
    private var settingsWindow: SettingsWindowController?
    private var onboardingWindow: OnboardingWindowController?
    private var settingsCancellable: AnyCancellable?
    private var workspaceObservers: [NSObjectProtocol] = []
    private var activationObserver: NSObjectProtocol?
    private var isTerminating = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard SingleInstanceService().claimPrimaryInstance() else {
            NSApp.terminate(nil)
            return
        }
        NSApp.setActivationPolicy(.accessory)
        let directory = applicationSupportDirectory()
        model = ApplicationModel(settingsDirectoryURL: directory)
        permissionService = AccessibilityPermissionService()
        launchAtLoginService = LaunchAtLoginService()
        externalActions = ExternalActionService(
            logDirectoryURL: directory.appendingPathComponent("Logs", isDirectory: true),
            updateRepository: Bundle.main.object(
                forInfoDictionaryKey: "UpdateRepository") as? String
                ?? "lingge66/codex-quota-rail")
        applicationLog = ApplicationLog(
            directoryURL: directory.appendingPathComponent("Logs", isDirectory: true))
        railController = RailPanelController(model: model)
        menuController = MenuBarController(model: model) { [weak self] command in
            self?.handle(command)
        }
        observeSettings()
        observeSystemTransitions()
        observeSecondaryActivation()
        if CommandLine.arguments.contains("--rail-preview") {
            showPreview()
        } else {
            startServices()
            showOnboardingIfNeeded()
        }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if isTerminating {
            return .terminateLater
        }
        isTerminating = true
        Task { [weak self] in
            guard let self else {
                sender.reply(toApplicationShouldTerminate: true)
                return
            }
            await self.stopServices()
            self.removeObservers()
            self.railController.close()
            self.menuController.close()
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }

    private func handle(_ command: MenuCommand) {
        switch command {
        case .openSettings:
            showSettings()
        case .refresh:
            rateCoordinator?.refresh()
        case .toggleFollow:
            model.mutateSettings { $0.followPaused.toggle() }
        case .themeAutomatic:
            model.mutateSettings { $0.theme = .automatic }
        case .themeDark:
            model.mutateSettings { $0.theme = .dark }
        case .themeLight:
            model.mutateSettings { $0.theme = .light }
        case .toggleReduceMotion:
            model.mutateSettings { $0.reduceMotion.toggle() }
        case .toggleLaunchAtLogin:
            setLaunchAtLogin(!model.settings.launchAtLogin)
        case .openLogs:
            externalActions.openLogs()
        case .troubleshoot:
            externalActions.showTroubleshooting()
        case .openWebsite:
            externalActions.openWebsite(model.settings.websiteURL)
        case .checkUpdates:
            externalActions.checkUpdates()
        case .quit:
            NSApp.terminate(nil)
        }
    }

    private func startServices() {
        guard windowTracker == nil, rateCoordinator == nil else {
            return
        }
        let locator = MacCodexLocator(
            targetBundleIdentifiers: model.settings.targetBundleIdentifiers)
        let tracker = AccessibilityWindowTracker(
            targetBundleIdentifiers: model.settings.targetBundleIdentifiers,
            permissionService: permissionService)
        windowTracker = tracker
        let coordinator = RateLimitCoordinator(
            model: model,
            locator: locator,
            log: applicationLog)
        rateCoordinator = coordinator
        windowTrackingTask = Task { [weak self] in
            await tracker.start()
            for await snapshot in tracker.snapshots {
                guard !Task.isCancelled else {
                    return
                }
                self?.model.apply(windowSnapshot: snapshot)
            }
        }
        coordinator.start()
    }

    private func stopServices() async {
        windowTrackingTask?.cancel()
        windowTrackingTask = nil
        if let windowTracker {
            await windowTracker.stop()
        }
        if let rateCoordinator {
            await rateCoordinator.stop()
        }
        windowTracker = nil
        rateCoordinator = nil
        model.apply(windowSnapshot: nil)
    }

    private func restartServices() {
        Task { [weak self] in
            guard let self else {
                return
            }
            await self.stopServices()
            self.startServices()
        }
    }

    private func observeSettings() {
        settingsCancellable = model.$settings
            .map(\.targetBundleIdentifiers)
            .removeDuplicates()
            .dropFirst()
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.restartServices() }
    }

    private func observeSystemTransitions() {
        let center = NSWorkspace.shared.notificationCenter
        workspaceObservers.append(
            center.addObserver(
                forName: NSWorkspace.willSleepNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                Task { @MainActor in await self?.stopServices() }
            })
        workspaceObservers.append(
            center.addObserver(
                forName: NSWorkspace.didWakeNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                Task { @MainActor in self?.startServices() }
            })
    }

    private func observeSecondaryActivation() {
        activationObserver = DistributedNotificationCenter.default().addObserver(
            forName: SingleInstanceService.activationNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.rateCoordinator?.refresh() }
        }
    }

    private func removeObservers() {
        let center = NSWorkspace.shared.notificationCenter
        workspaceObservers.forEach { center.removeObserver($0) }
        workspaceObservers.removeAll()
        if let activationObserver {
            DistributedNotificationCenter.default().removeObserver(activationObserver)
        }
        activationObserver = nil
        settingsCancellable = nil
    }

    private func showSettings() {
        if settingsWindow == nil {
            settingsWindow = SettingsWindowController(
                model: model,
                accessibilityPermission: permissionService,
                launchAtLoginService: launchAtLoginService,
                onLaunchAtLoginChanged: { [weak self] enabled in
                    self?.setLaunchAtLogin(enabled)
                })
        }
        settingsWindow?.show()
    }

    private func showOnboardingIfNeeded() {
        guard !model.settings.hasCompletedOnboarding else {
            reconcileLaunchAtLogin()
            return
        }
        let controller = OnboardingWindowController(
            accessibilityPermission: permissionService,
            initialLaunchAtLogin: model.settings.launchAtLogin
        ) { [weak self] launchAtLogin in
            guard let self else {
                return
            }
            self.setLaunchAtLogin(launchAtLogin)
            self.model.mutateSettings { $0.hasCompletedOnboarding = true }
            self.onboardingWindow?.close()
            self.onboardingWindow = nil
        }
        onboardingWindow = controller
        controller.show()
    }

    private func reconcileLaunchAtLogin() {
        if model.settings.launchAtLogin,
           launchAtLoginService.status == .disabled
        {
            setLaunchAtLogin(true)
        }
    }

    private func setLaunchAtLogin(_ enabled: Bool) {
        do {
            try launchAtLoginService.setEnabled(enabled)
            model.mutateSettings { $0.launchAtLogin = enabled }
            model.setError(nil)
            if launchAtLoginService.status == .requiresApproval {
                launchAtLoginService.openSystemSettings()
            }
        } catch {
            model.setError("登录项设置失败，请在系统设置中检查。")
        }
    }

    private func showPreview() {
        let now = Date()
        model.apply(snapshot: RawQuotaSnapshot(
            primary: RawQuotaWindow(
                label: "5 小时",
                usedPercent: 10,
                windowDurationMinutes: 300,
                resetsAt: now.addingTimeInterval(7_200),
                isUnlimited: false),
            secondary: RawQuotaWindow(
                label: "本周",
                usedPercent: 42,
                windowDurationMinutes: 10_080,
                resetsAt: now.addingTimeInterval(300_000),
                isUnlimited: false),
            receivedAt: now))
        let screen = NSScreen.main?.frame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        model.apply(windowSnapshot: TrackedMacWindowSnapshot(
            frame: MacRect(
                x: Double(screen.midX - 500),
                y: Double(screen.midY - 300),
                width: 1000,
                height: 600),
            screenFrame: MacRect(
                x: Double(screen.minX),
                y: Double(screen.minY),
                width: Double(screen.width),
                height: Double(screen.height)),
            isVisible: true,
            isMinimized: false,
            isFullScreen: false,
            isFocused: true,
            windowNumber: NSApp.mainWindow?.windowNumber ?? 0))
    }

    private func applicationSupportDirectory() -> URL {
        let base = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support", isDirectory: true)
        return base.appendingPathComponent("CodexQuotaRail", isDirectory: true)
    }
}
