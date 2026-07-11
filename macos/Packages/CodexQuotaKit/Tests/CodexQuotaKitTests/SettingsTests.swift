import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("设置与主题")
struct SettingsTests {
    @Test("拒绝非 HTTPS 网站")
    func rejectsNonHTTPSWebsite() {
        let settings = AppSettings(websiteURL: URL(string: "javascript:alert(1)")!)

        let validated = settings.validated()

        #expect(validated.websiteURL == AppSettings.defaults.websiteURL)
    }

    @Test("约束视觉设置的安全范围")
    func clampsVisualValues() {
        let settings = AppSettings(
            focusedOpacity: 4,
            unfocusedOpacity: -1,
            railHeight: 500,
            compactHeight: 0)

        let validated = settings.validated()

        #expect(validated.focusedOpacity == 1)
        #expect(validated.unfocusedOpacity == 0.2)
        #expect(validated.railHeight == 40)
        #expect(validated.compactHeight == 1)
    }

    @Test("空目标应用列表恢复默认值")
    func emptyTargetApplicationsUseDefaults() {
        let settings = AppSettings(targetBundleIdentifiers: [])

        #expect(settings.validated().targetBundleIdentifiers == AppSettings.defaults.targetBundleIdentifiers)
    }

    @Test("设置文件可以往返保存")
    func settingsRoundTrip() throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = JSONSettingsStore(directoryURL: directory)
        let expected = AppSettings(theme: .dark, reduceMotion: true, launchAtLogin: false)

        try store.save(expected)
        let actual = try store.load()

        #expect(actual == expected.validated())
    }

    @Test("损坏设置被隔离并恢复默认值")
    func corruptedSettingsRecover() throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try Data("not-json".utf8).write(to: directory.appendingPathComponent("settings.json"))
        let store = JSONSettingsStore(directoryURL: directory)

        let settings = try store.load()

        #expect(settings == .defaults)
        let files = try FileManager.default.contentsOfDirectory(atPath: directory.path)
        #expect(files.contains(where: { $0.hasPrefix("settings.corrupt-") }))
    }

    @Test("主题颜色必须是八位十六进制")
    func invalidThemeFallsBack() {
        let theme = QuotaTheme(
            surface: "purple",
            trackBase: "#2A2A27FF",
            textPrimary: "#F4F4EFFF",
            textSecondary: "#A8AAA2FF",
            border: "#32322EFF")

        #expect(theme.validated() == .dark)
    }
}
