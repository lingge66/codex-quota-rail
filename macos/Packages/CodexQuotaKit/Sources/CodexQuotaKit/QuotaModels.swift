import Foundation

public enum QuotaConnectionState: String, Codable, Sendable {
    case connecting
    case live
    case stale
    case authenticationRequired
    case unsupported
    case unavailable
}

public enum QuotaWindowState: String, Codable, Sendable {
    case healthy
    case notice
    case critical
    case exhausted
    case unlimited
    case unavailable
}

public struct RawQuotaWindow: Equatable, Sendable {
    public let label: String
    public let usedPercent: Int?
    public let windowDurationMinutes: Int?
    public let resetsAt: Date?
    public let isUnlimited: Bool

    public init(
        label: String,
        usedPercent: Int?,
        windowDurationMinutes: Int?,
        resetsAt: Date?,
        isUnlimited: Bool
    ) {
        self.label = label
        self.usedPercent = usedPercent
        self.windowDurationMinutes = windowDurationMinutes
        self.resetsAt = resetsAt
        self.isUnlimited = isUnlimited
    }
}

public struct RawQuotaSnapshot: Equatable, Sendable {
    public let primary: RawQuotaWindow?
    public let secondary: RawQuotaWindow?
    public let receivedAt: Date

    public init(primary: RawQuotaWindow?, secondary: RawQuotaWindow?, receivedAt: Date) {
        self.primary = primary
        self.secondary = secondary
        self.receivedAt = receivedAt
    }
}

public struct QuotaWindowDisplay: Equatable, Sendable {
    public let label: String
    public let availablePercent: Int?
    public let windowDurationMinutes: Int?
    public let resetsAt: Date?
    public let state: QuotaWindowState

    public init(
        label: String,
        availablePercent: Int?,
        windowDurationMinutes: Int?,
        resetsAt: Date?,
        state: QuotaWindowState
    ) {
        self.label = label
        self.availablePercent = availablePercent
        self.windowDurationMinutes = windowDurationMinutes
        self.resetsAt = resetsAt
        self.state = state
    }
}

public struct QuotaDisplayState: Equatable, Sendable {
    public let windows: [QuotaWindowDisplay]
    public let connection: QuotaConnectionState
    public let updatedAt: Date?
    public let message: String?

    public init(
        windows: [QuotaWindowDisplay],
        connection: QuotaConnectionState,
        updatedAt: Date?,
        message: String?
    ) {
        self.windows = windows
        self.connection = connection
        self.updatedAt = updatedAt
        self.message = message
    }

    public static func waiting(_ message: String) -> Self {
        Self(windows: [], connection: .connecting, updatedAt: nil, message: message)
    }
}
