import Foundation

public struct JSONSettingsStore: Sendable {
    public let directoryURL: URL
    public let settingsURL: URL
    private let defaultSettings: AppSettings

    public init(directoryURL: URL, defaultSettings: AppSettings = .defaults) {
        self.directoryURL = directoryURL
        settingsURL = directoryURL.appendingPathComponent("settings.json", isDirectory: false)
        self.defaultSettings = defaultSettings.validated()
    }

    public func load() throws -> AppSettings {
        try FileManager.default.createDirectory(
            at: directoryURL,
            withIntermediateDirectories: true)
        guard FileManager.default.fileExists(atPath: settingsURL.path) else {
            return defaultSettings
        }
        do {
            let data = try Data(contentsOf: settingsURL)
            return try JSONDecoder().decode(AppSettings.self, from: data).validated()
        } catch {
            try isolateCorruptedSettings()
            return defaultSettings
        }
    }

    public func save(_ settings: AppSettings) throws {
        try FileManager.default.createDirectory(
            at: directoryURL,
            withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        let data = try encoder.encode(settings.validated())
        try data.write(to: settingsURL, options: .atomic)
    }

    private func isolateCorruptedSettings() throws {
        let timestamp = Int(Date().timeIntervalSince1970)
        let candidate = directoryURL.appendingPathComponent(
            "settings.corrupt-\(timestamp)-\(UUID().uuidString).json")
        try FileManager.default.moveItem(at: settingsURL, to: candidate)
    }
}
