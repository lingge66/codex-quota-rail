import Combine
import CodexQuotaKit
import Foundation

@MainActor
final class ApplicationModel: ObservableObject {
    @Published private(set) var displayState = QuotaDisplayState.waiting("正在连接 Codex")
    @Published private(set) var placement = RailPlacement.hidden
    @Published var settings: AppSettings
    @Published private(set) var customTheme: QuotaTheme?
    @Published var isDetailsVisible = false
    @Published var lastErrorMessage: String?

    private let settingsStore: JSONSettingsStore
    private var lastWindowSnapshot: TrackedMacWindowSnapshot?

    init(settingsDirectoryURL: URL) {
        let bundledDefaults = Bundle.main.url(forResource: "Defaults", withExtension: "json")
            .flatMap { try? Data(contentsOf: $0) }
            .flatMap { try? JSONDecoder().decode(AppSettings.self, from: $0) }
            ?? .defaults
        settingsStore = JSONSettingsStore(
            directoryURL: settingsDirectoryURL,
            defaultSettings: bundledDefaults)
        settings = (try? settingsStore.load()) ?? .defaults
        customTheme = Self.loadCustomTheme(settings.customThemePath)
    }

    func apply(snapshot: RawQuotaSnapshot) {
        displayState = QuotaNormalizer.normalize(snapshot)
    }

    func apply(connection: QuotaConnectionState, message: String?) {
        displayState = QuotaDisplayState(
            windows: displayState.windows,
            connection: connection,
            updatedAt: displayState.updatedAt,
            message: message)
    }

    func apply(windowSnapshot: TrackedMacWindowSnapshot?) {
        lastWindowSnapshot = windowSnapshot
        guard let windowSnapshot else {
            placement = .hidden
            isDetailsVisible = false
            return
        }
        placement = RailPlacementCalculator.calculate(
            snapshot: windowSnapshot,
            settings: settings)
        if placement.mode != .externalRail {
            isDetailsVisible = false
        }
    }

    func mutateSettings(_ mutation: (inout AppSettings) -> Void) {
        var value = settings
        mutation(&value)
        settings = value.validated()
        do {
            try settingsStore.save(settings)
            lastErrorMessage = nil
        } catch {
            lastErrorMessage = "设置保存失败。"
        }
        apply(windowSnapshot: lastWindowSnapshot)
        customTheme = Self.loadCustomTheme(settings.customThemePath)
    }

    func toggleDetails() {
        guard placement.mode == .externalRail else {
            isDetailsVisible = false
            return
        }
        isDetailsVisible.toggle()
    }

    func closeDetails() {
        isDetailsVisible = false
    }

    func setError(_ message: String?) {
        lastErrorMessage = message
    }

    private static func loadCustomTheme(_ path: String?) -> QuotaTheme? {
        guard let path else {
            return nil
        }
        return try? JSONThemeLoader().loadTheme(
            from: URL(fileURLWithPath: path),
            fallback: .dark)
    }
}
