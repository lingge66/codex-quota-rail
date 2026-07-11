public struct BackoffSchedule: Sendable {
    private static let delays: [Duration] = [
        .seconds(2),
        .seconds(5),
        .seconds(15),
        .seconds(30),
        .seconds(60),
    ]

    private var index = 0

    public init() {}

    public mutating func nextDelay() -> Duration {
        let delay = Self.delays[min(index, Self.delays.count - 1)]
        if index < Self.delays.count - 1 {
            index += 1
        }
        return delay
    }

    public mutating func reset() {
        index = 0
    }
}
