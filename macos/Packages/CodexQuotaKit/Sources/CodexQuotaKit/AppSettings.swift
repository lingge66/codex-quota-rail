import Foundation

public enum ThemePreference: String, Codable, CaseIterable, Sendable {
    case automatic
    case dark
    case light
    case custom
}

public struct AppSettings: Codable, Equatable, Sendable {
    public static let defaults = AppSettings()

    public var schemaVersion: Int
    public var theme: ThemePreference
    public var reduceMotion: Bool
    public var launchAtLogin: Bool
    public var hasCompletedOnboarding: Bool
    public var followPaused: Bool
    public var focusedOpacity: Double
    public var unfocusedOpacity: Double
    public var railHeight: Double
    public var compactHeight: Double
    public var cornerRadius: Double
    public var customThemePath: String?
    public var websiteURL: URL
    public var targetBundleIdentifiers: [String]

    public init(
        schemaVersion: Int = 1,
        theme: ThemePreference = .automatic,
        reduceMotion: Bool = false,
        launchAtLogin: Bool = true,
        hasCompletedOnboarding: Bool = false,
        followPaused: Bool = false,
        focusedOpacity: Double = 1,
        unfocusedOpacity: Double = 0.52,
        railHeight: Double = 22,
        compactHeight: Double = 4,
        cornerRadius: Double = 8,
        customThemePath: String? = nil,
        websiteURL: URL = URL(string: "https://lingge66.pages.dev/")!,
        targetBundleIdentifiers: [String] = ["com.openai.codex"]
    ) {
        self.schemaVersion = schemaVersion
        self.theme = theme
        self.reduceMotion = reduceMotion
        self.launchAtLogin = launchAtLogin
        self.hasCompletedOnboarding = hasCompletedOnboarding
        self.followPaused = followPaused
        self.focusedOpacity = focusedOpacity
        self.unfocusedOpacity = unfocusedOpacity
        self.railHeight = railHeight
        self.compactHeight = compactHeight
        self.cornerRadius = cornerRadius
        self.customThemePath = customThemePath
        self.websiteURL = websiteURL
        self.targetBundleIdentifiers = targetBundleIdentifiers
    }

    public init(from decoder: Decoder) throws {
        let defaults = AppSettings()
        let container = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try container.decodeIfPresent(Int.self, forKey: .schemaVersion)
            ?? defaults.schemaVersion
        theme = try container.decodeIfPresent(ThemePreference.self, forKey: .theme)
            ?? defaults.theme
        reduceMotion = try container.decodeIfPresent(Bool.self, forKey: .reduceMotion)
            ?? defaults.reduceMotion
        launchAtLogin = try container.decodeIfPresent(Bool.self, forKey: .launchAtLogin)
            ?? defaults.launchAtLogin
        hasCompletedOnboarding = try container.decodeIfPresent(
            Bool.self,
            forKey: .hasCompletedOnboarding) ?? defaults.hasCompletedOnboarding
        followPaused = try container.decodeIfPresent(Bool.self, forKey: .followPaused)
            ?? defaults.followPaused
        focusedOpacity = try container.decodeIfPresent(Double.self, forKey: .focusedOpacity)
            ?? defaults.focusedOpacity
        unfocusedOpacity = try container.decodeIfPresent(Double.self, forKey: .unfocusedOpacity)
            ?? defaults.unfocusedOpacity
        railHeight = try container.decodeIfPresent(Double.self, forKey: .railHeight)
            ?? defaults.railHeight
        compactHeight = try container.decodeIfPresent(Double.self, forKey: .compactHeight)
            ?? defaults.compactHeight
        cornerRadius = try container.decodeIfPresent(Double.self, forKey: .cornerRadius)
            ?? defaults.cornerRadius
        customThemePath = try container.decodeIfPresent(String.self, forKey: .customThemePath)
        websiteURL = try container.decodeIfPresent(URL.self, forKey: .websiteURL)
            ?? defaults.websiteURL
        targetBundleIdentifiers = try container.decodeIfPresent(
            [String].self,
            forKey: .targetBundleIdentifiers) ?? defaults.targetBundleIdentifiers
    }

    public func validated() -> Self {
        var value = self
        value.schemaVersion = 1
        value.focusedOpacity = value.focusedOpacity.clamped(to: 0.2 ... 1)
        value.unfocusedOpacity = value.unfocusedOpacity.clamped(to: 0.2 ... 1)
        value.railHeight = value.railHeight.clamped(to: 4 ... 40)
        value.compactHeight = value.compactHeight.clamped(to: 1 ... 8)
        value.cornerRadius = value.cornerRadius.clamped(to: 0 ... 20)
        value.customThemePath = value.customThemePath?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if value.customThemePath?.isEmpty == true
            || value.customThemePath?.hasPrefix("/") == false
        {
            value.customThemePath = nil
            if value.theme == .custom {
                value.theme = .automatic
            }
        }
        if value.websiteURL.scheme?.lowercased() != "https" || value.websiteURL.host == nil {
            value.websiteURL = Self.defaults.websiteURL
        }
        value.targetBundleIdentifiers = value.targetBundleIdentifiers
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .uniqued()
        if value.targetBundleIdentifiers.isEmpty {
            value.targetBundleIdentifiers = Self.defaults.targetBundleIdentifiers
        }
        return value
    }
}

private extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(range.upperBound, max(range.lowerBound, self))
    }
}

private extension Array where Element: Hashable {
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}
