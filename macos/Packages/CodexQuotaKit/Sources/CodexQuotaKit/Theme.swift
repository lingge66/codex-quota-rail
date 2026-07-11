import Foundation

public struct QuotaTheme: Codable, Equatable, Sendable {
    public static let dark = QuotaTheme(
        surface: "#10100EF2",
        trackBase: "#2A2A27FF",
        textPrimary: "#F4F4EFFF",
        textSecondary: "#A8AAA2FF",
        border: "#32322EFF")

    public static let light = QuotaTheme(
        surface: "#F8F8F6F2",
        trackBase: "#E3E5DFFF",
        textPrimary: "#171815FF",
        textSecondary: "#5F625BFF",
        border: "#D7DAD2FF")

    public var surface: String
    public var trackBase: String
    public var textPrimary: String
    public var textSecondary: String
    public var border: String

    public init(
        surface: String,
        trackBase: String,
        textPrimary: String,
        textSecondary: String,
        border: String
    ) {
        self.surface = surface
        self.trackBase = trackBase
        self.textPrimary = textPrimary
        self.textSecondary = textSecondary
        self.border = border
    }

    public func validated(fallback: Self = .dark) -> Self {
        let colors = [surface, trackBase, textPrimary, textSecondary, border]
        return colors.allSatisfy(Self.isHexColor) ? self : fallback
    }

    private static func isHexColor(_ value: String) -> Bool {
        guard value.count == 9, value.first == "#" else {
            return false
        }
        return value.dropFirst().unicodeScalars.allSatisfy { scalar in
            CharacterSet(charactersIn: "0123456789abcdefABCDEF").contains(scalar)
        }
    }
}
