import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("App Server 协议")
struct AppServerProtocolTests {
    @Test("映射双额度响应")
    func mapsRateLimits() throws {
        let data = Data(#"{"rateLimits":{"primary":{"usedPercent":68,"windowDurationMins":300,"resetsAt":1800000000},"secondary":{"usedPercent":2,"windowDurationMins":10080,"resetsAt":1800100000},"credits":{"unlimited":false}}}"#.utf8)

        let snapshot = try RateLimitMapper.map(
            data,
            receivedAt: Date(timeIntervalSince1970: 1_799_999_000))

        #expect(snapshot.primary?.label == "5 小时")
        #expect(snapshot.primary?.usedPercent == 68)
        #expect(snapshot.primary?.resetsAt == Date(timeIntervalSince1970: 1_800_000_000))
        #expect(snapshot.secondary?.label == "本周")
        #expect(snapshot.secondary?.usedPercent == 2)
    }

    @Test("缺少主额度时拒绝响应")
    func rejectsMissingPrimary() {
        let data = Data(#"{"rateLimits":{"secondary":{"usedPercent":2}}}"#.utf8)

        #expect(throws: AppServerError.self) {
            try RateLimitMapper.map(data, receivedAt: .now)
        }
    }

    @Test("无限额度优先于已用比例")
    func mapsUnlimitedCredits() throws {
        let data = Data(#"{"rateLimits":{"primary":{"usedPercent":99},"credits":{"unlimited":true}}}"#.utf8)

        let snapshot = try RateLimitMapper.map(data, receivedAt: .now)

        #expect(snapshot.primary?.isUnlimited == true)
    }

    @Test("退避到一分钟后保持上限")
    func backoffCapsAtOneMinute() {
        var schedule = BackoffSchedule()

        #expect(schedule.nextDelay() == .seconds(2))
        #expect(schedule.nextDelay() == .seconds(5))
        #expect(schedule.nextDelay() == .seconds(15))
        #expect(schedule.nextDelay() == .seconds(30))
        #expect(schedule.nextDelay() == .seconds(60))
        #expect(schedule.nextDelay() == .seconds(60))
        schedule.reset()
        #expect(schedule.nextDelay() == .seconds(2))
    }
}
