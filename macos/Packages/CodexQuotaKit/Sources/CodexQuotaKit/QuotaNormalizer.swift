import Foundation

public enum QuotaNormalizer {
    public static func availablePercent(from usedPercent: Int) -> Int {
        min(100, max(0, 100 - usedPercent))
    }

    public static func normalize(_ snapshot: RawQuotaSnapshot) -> QuotaDisplayState {
        let windows = [snapshot.primary, snapshot.secondary].compactMap(mapWindow)
        return QuotaDisplayState(
            windows: windows,
            connection: .live,
            updatedAt: snapshot.receivedAt,
            message: nil)
    }

    private static func mapWindow(_ source: RawQuotaWindow?) -> QuotaWindowDisplay? {
        guard let source else {
            return nil
        }
        if source.isUnlimited {
            return QuotaWindowDisplay(
                label: source.label,
                availablePercent: nil,
                windowDurationMinutes: source.windowDurationMinutes,
                resetsAt: source.resetsAt,
                state: .unlimited)
        }
        guard let usedPercent = source.usedPercent else {
            return QuotaWindowDisplay(
                label: source.label,
                availablePercent: nil,
                windowDurationMinutes: source.windowDurationMinutes,
                resetsAt: source.resetsAt,
                state: .unavailable)
        }
        let available = availablePercent(from: usedPercent)
        return QuotaWindowDisplay(
            label: source.label,
            availablePercent: available,
            windowDurationMinutes: source.windowDurationMinutes,
            resetsAt: source.resetsAt,
            state: state(for: available))
    }

    private static func state(for availablePercent: Int) -> QuotaWindowState {
        if availablePercent == 0 {
            return .exhausted
        }
        if availablePercent <= 20 {
            return .critical
        }
        if availablePercent <= 50 {
            return .notice
        }
        return .healthy
    }
}
