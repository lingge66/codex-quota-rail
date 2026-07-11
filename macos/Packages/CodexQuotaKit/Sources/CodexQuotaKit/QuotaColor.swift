public struct QuotaRGB: Equatable, Sendable {
    public let red: Double
    public let green: Double
    public let blue: Double

    public init(red: Double, green: Double, blue: Double) {
        self.red = red
        self.green = green
        self.blue = blue
    }
}

public enum QuotaColor {
    private static let healthy = QuotaRGB(red: 0.57, green: 0.94, blue: 0.42)
    private static let notice = QuotaRGB(red: 1.00, green: 0.77, blue: 0.36)
    private static let critical = QuotaRGB(red: 1.00, green: 0.38, blue: 0.36)

    public static func rgb(for availablePercent: Int) -> QuotaRGB {
        let percent = min(100, max(0, availablePercent))
        if percent <= 20 {
            return critical
        }
        if percent == 50 {
            return notice
        }
        if percent == 100 {
            return healthy
        }
        if percent <= 50 {
            return interpolate(from: critical, to: notice, progress: Double(percent - 20) / 30)
        }
        return interpolate(from: notice, to: healthy, progress: Double(percent - 50) / 50)
    }

    private static func interpolate(from: QuotaRGB, to: QuotaRGB, progress: Double) -> QuotaRGB {
        QuotaRGB(
            red: from.red + ((to.red - from.red) * progress),
            green: from.green + ((to.green - from.green) * progress),
            blue: from.blue + ((to.blue - from.blue) * progress))
    }
}
