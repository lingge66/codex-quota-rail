public enum RailMode: String, Equatable, Sendable {
    case externalRail
    case compactRail
    case hidden
}

public struct RailPlacement: Equatable, Sendable {
    public let frame: MacRect
    public let mode: RailMode
    public let opacity: Double
    public let relativeWindowNumber: Int

    public init(
        frame: MacRect,
        mode: RailMode,
        opacity: Double,
        relativeWindowNumber: Int
    ) {
        self.frame = frame
        self.mode = mode
        self.opacity = opacity
        self.relativeWindowNumber = relativeWindowNumber
    }

    public static let hidden = RailPlacement(
        frame: MacRect(x: 0, y: 0, width: 0, height: 0),
        mode: .hidden,
        opacity: 0,
        relativeWindowNumber: 0)
}

public enum RailPlacementCalculator {
    public static func calculate(
        snapshot: TrackedMacWindowSnapshot,
        settings: AppSettings
    ) -> RailPlacement {
        guard snapshot.isVisible,
              !snapshot.isMinimized,
              !settings.followPaused,
              snapshot.frame.width > 0,
              snapshot.frame.height > 0
        else {
            return .hidden
        }
        let opacity = snapshot.isFocused
            ? settings.focusedOpacity
            : settings.unfocusedOpacity
        let externalFits = snapshot.frame.maxY + settings.railHeight <= snapshot.screenFrame.maxY
        if !snapshot.isFullScreen && externalFits {
            return RailPlacement(
                frame: MacRect(
                    x: snapshot.frame.x,
                    y: snapshot.frame.maxY,
                    width: snapshot.frame.width,
                    height: settings.railHeight),
                mode: .externalRail,
                opacity: opacity,
                relativeWindowNumber: snapshot.windowNumber)
        }
        return RailPlacement(
            frame: MacRect(
                x: snapshot.frame.x,
                y: snapshot.frame.maxY - settings.compactHeight,
                width: snapshot.frame.width,
                height: settings.compactHeight),
            mode: .compactRail,
            opacity: opacity,
            relativeWindowNumber: snapshot.windowNumber)
    }
}
