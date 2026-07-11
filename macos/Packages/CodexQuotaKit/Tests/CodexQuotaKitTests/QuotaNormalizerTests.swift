import Foundation
import Testing
@testable import CodexQuotaKit

@Suite("额度归一化")
struct QuotaNormalizerTests {
    @Test("把已用额度转换为可用额度")
    func convertsUsedToAvailable() {
        let raw = RawQuotaSnapshot(
            primary: RawQuotaWindow(
                label: "5 小时",
                usedPercent: 68,
                windowDurationMinutes: 300,
                resetsAt: nil,
                isUnlimited: false),
            secondary: nil,
            receivedAt: Date(timeIntervalSince1970: 1_800_000_000))

        let state = QuotaNormalizer.normalize(raw)

        #expect(state.windows.map(\.availablePercent) == [32])
        #expect(state.connection == .live)
    }

    @Test("把异常百分比约束在零到一百")
    func clampsAvailablePercent() {
        #expect(QuotaNormalizer.availablePercent(from: -1) == 100)
        #expect(QuotaNormalizer.availablePercent(from: 0) == 100)
        #expect(QuotaNormalizer.availablePercent(from: 50) == 50)
        #expect(QuotaNormalizer.availablePercent(from: 80) == 20)
        #expect(QuotaNormalizer.availablePercent(from: 100) == 0)
        #expect(QuotaNormalizer.availablePercent(from: 101) == 0)
    }

    @Test("缺失百分比不伪造满额度")
    func missingPercentIsUnavailable() {
        let raw = RawQuotaSnapshot(
            primary: RawQuotaWindow(
                label: "主额度",
                usedPercent: nil,
                windowDurationMinutes: nil,
                resetsAt: nil,
                isUnlimited: false),
            secondary: nil,
            receivedAt: .now)

        let window = QuotaNormalizer.normalize(raw).windows[0]

        #expect(window.availablePercent == nil)
        #expect(window.state == .unavailable)
    }

    @Test("无限额度保持无限语义")
    func unlimitedStaysUnlimited() {
        let raw = RawQuotaSnapshot(
            primary: RawQuotaWindow(
                label: "5 小时",
                usedPercent: 99,
                windowDurationMinutes: 300,
                resetsAt: nil,
                isUnlimited: true),
            secondary: nil,
            receivedAt: .now)

        let window = QuotaNormalizer.normalize(raw).windows[0]

        #expect(window.availablePercent == nil)
        #expect(window.state == .unlimited)
    }

    @Test("颜色锚点从绿色过渡到红色")
    func colorAnchors() {
        #expect(QuotaColor.rgb(for: 100) == QuotaRGB(red: 0.57, green: 0.94, blue: 0.42))
        #expect(QuotaColor.rgb(for: 50) == QuotaRGB(red: 1.00, green: 0.77, blue: 0.36))
        #expect(QuotaColor.rgb(for: 20) == QuotaRGB(red: 1.00, green: 0.38, blue: 0.36))
        #expect(QuotaColor.rgb(for: 0) == QuotaRGB(red: 1.00, green: 0.38, blue: 0.36))
    }
}
