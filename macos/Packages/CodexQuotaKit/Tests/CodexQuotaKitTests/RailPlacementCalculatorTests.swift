import Testing
@testable import CodexQuotaKit

@Suite("macOS 边缘轨放置")
struct RailPlacementCalculatorTests {
    @Test("在普通窗口顶部外侧放置轨道")
    func placesExternalRailAboveWindow() {
        let snapshot = TrackedMacWindowSnapshot(
            frame: MacRect(x: -800, y: 100, width: 700, height: 600),
            screenFrame: MacRect(x: -900, y: 0, width: 900, height: 900),
            isVisible: true,
            isMinimized: false,
            isFullScreen: false,
            isFocused: true,
            windowNumber: 42)

        let placement = RailPlacementCalculator.calculate(snapshot: snapshot, settings: .defaults)

        #expect(placement.mode == .externalRail)
        #expect(placement.frame == MacRect(x: -800, y: 700, width: 700, height: 22))
        #expect(placement.opacity == 1)
    }

    @Test("全屏使用四像素紧凑轨")
    func fullScreenUsesCompactRail() {
        let snapshot = TrackedMacWindowSnapshot(
            frame: MacRect(x: 0, y: 0, width: 1512, height: 982),
            screenFrame: MacRect(x: 0, y: 0, width: 1512, height: 982),
            isVisible: true,
            isMinimized: false,
            isFullScreen: true,
            isFocused: true,
            windowNumber: 7)

        let placement = RailPlacementCalculator.calculate(snapshot: snapshot, settings: .defaults)

        #expect(placement.mode == .compactRail)
        #expect(placement.frame == MacRect(x: 0, y: 978, width: 1512, height: 4))
    }

    @Test("顶部空间不足时安全降级为紧凑轨")
    func noExternalSpaceUsesCompactRail() {
        let snapshot = TrackedMacWindowSnapshot(
            frame: MacRect(x: 100, y: 100, width: 1000, height: 800),
            screenFrame: MacRect(x: 0, y: 0, width: 1200, height: 900),
            isVisible: true,
            isMinimized: false,
            isFullScreen: false,
            isFocused: false,
            windowNumber: 8)

        let placement = RailPlacementCalculator.calculate(snapshot: snapshot, settings: .defaults)

        #expect(placement.mode == .compactRail)
        #expect(placement.opacity == 0.52)
    }

    @Test("最小化和暂停跟随时隐藏")
    func minimizedAndPausedAreHidden() {
        let minimized = TrackedMacWindowSnapshot(
            frame: MacRect(x: 0, y: 0, width: 800, height: 600),
            screenFrame: MacRect(x: 0, y: 0, width: 1200, height: 900),
            isVisible: true,
            isMinimized: true,
            isFullScreen: false,
            isFocused: false,
            windowNumber: 9)
        var pausedSettings = AppSettings.defaults
        pausedSettings.followPaused = true
        let visible = TrackedMacWindowSnapshot(
            frame: minimized.frame,
            screenFrame: minimized.screenFrame,
            isVisible: true,
            isMinimized: false,
            isFullScreen: false,
            isFocused: true,
            windowNumber: 9)

        #expect(RailPlacementCalculator.calculate(snapshot: minimized, settings: .defaults).mode == .hidden)
        #expect(RailPlacementCalculator.calculate(snapshot: visible, settings: pausedSettings).mode == .hidden)
    }
}
