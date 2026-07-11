public struct MacRect: Codable, Equatable, Sendable {
    public let x: Double
    public let y: Double
    public let width: Double
    public let height: Double

    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
    }

    public var maxX: Double { x + width }
    public var maxY: Double { y + height }
}

public struct TrackedMacWindowSnapshot: Equatable, Sendable {
    public let frame: MacRect
    public let screenFrame: MacRect
    public let isVisible: Bool
    public let isMinimized: Bool
    public let isFullScreen: Bool
    public let isFocused: Bool
    public let windowNumber: Int

    public init(
        frame: MacRect,
        screenFrame: MacRect,
        isVisible: Bool,
        isMinimized: Bool,
        isFullScreen: Bool,
        isFocused: Bool,
        windowNumber: Int
    ) {
        self.frame = frame
        self.screenFrame = screenFrame
        self.isVisible = isVisible
        self.isMinimized = isMinimized
        self.isFullScreen = isFullScreen
        self.isFocused = isFocused
        self.windowNumber = windowNumber
    }
}

@MainActor
public protocol TargetWindowTracking: AnyObject {
    var snapshots: AsyncStream<TrackedMacWindowSnapshot?> { get }
    func start() async
    func stop() async
}
