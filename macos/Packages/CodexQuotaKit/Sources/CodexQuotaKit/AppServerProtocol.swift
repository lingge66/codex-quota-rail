import Foundation

public enum AppServerError: Error, Equatable, Sendable {
    case invalidResponse
    case requestFailed(code: Int)
    case authenticationRequired
    case processClosed
    case unsupported
}

private struct RateLimitsReadResponse: Decodable {
    let rateLimits: RateLimitBucket?
}

private struct RateLimitBucket: Decodable {
    let primary: RateLimitWindow?
    let secondary: RateLimitWindow?
    let credits: RateLimitCredits?
}

private struct RateLimitWindow: Decodable {
    let usedPercent: Int?
    let windowDurationMins: Int?
    let resetsAt: Int64?
}

private struct RateLimitCredits: Decodable {
    let unlimited: Bool?
}

public enum RateLimitMapper {
    private static let minimumUnixSeconds: Int64 = -62_135_596_800
    private static let maximumUnixSeconds: Int64 = 253_402_300_799

    public static func map(_ data: Data, receivedAt: Date) throws -> RawQuotaSnapshot {
        let response: RateLimitsReadResponse
        do {
            response = try JSONDecoder().decode(RateLimitsReadResponse.self, from: data)
        } catch {
            throw AppServerError.invalidResponse
        }
        guard let limits = response.rateLimits, let primary = limits.primary else {
            throw AppServerError.invalidResponse
        }
        let unlimited = limits.credits?.unlimited == true
        return RawQuotaSnapshot(
            primary: mapWindow(primary, fallbackLabel: "主额度", unlimited: unlimited),
            secondary: limits.secondary.map {
                mapWindow($0, fallbackLabel: "次额度", unlimited: unlimited)
            },
            receivedAt: receivedAt)
    }

    private static func mapWindow(
        _ source: RateLimitWindow,
        fallbackLabel: String,
        unlimited: Bool
    ) -> RawQuotaWindow {
        RawQuotaWindow(
            label: displayLabel(source.windowDurationMins, fallback: fallbackLabel),
            usedPercent: source.usedPercent,
            windowDurationMinutes: source.windowDurationMins.flatMap { $0 >= 0 ? $0 : nil },
            resetsAt: safeDate(source.resetsAt),
            isUnlimited: unlimited)
    }

    private static func displayLabel(_ durationMinutes: Int?, fallback: String) -> String {
        switch durationMinutes {
        case 300:
            "5 小时"
        case 10_080:
            "本周"
        default:
            fallback
        }
    }

    private static func safeDate(_ seconds: Int64?) -> Date? {
        guard let seconds, (minimumUnixSeconds ... maximumUnixSeconds).contains(seconds) else {
            return nil
        }
        return Date(timeIntervalSince1970: TimeInterval(seconds))
    }
}
