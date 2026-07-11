import CodexQuotaKit
import SwiftUI

struct QuotaTrackView: View {
    let window: QuotaWindowDisplay
    let theme: QuotaTheme
    let compact: Bool
    let reduceMotion: Bool

    var body: some View {
        if compact {
            track
                .frame(height: 2)
        } else {
            HStack(spacing: 4) {
                Text(window.label)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(Color(hexRGBA: theme.textSecondary))
                    .lineLimit(1)
                track
                    .frame(minWidth: 72, maxWidth: .infinity)
                    .frame(height: 4)
                Text(valueText)
                    .font(.system(size: 11, weight: .semibold, design: .rounded))
                    .foregroundStyle(valueColor)
                    .monospacedDigit()
                    .lineLimit(1)
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(window.label)，\(valueText)")
        }
    }

    private var track: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule()
                    .fill(Color(hexRGBA: theme.trackBase))
                Capsule()
                    .fill(valueColor)
                    .scaleEffect(x: fillScale, y: 1, anchor: .leading)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: fillScale)
            }
            .frame(width: geometry.size.width, height: geometry.size.height)
        }
    }

    private var fillScale: Double {
        guard let percent = window.availablePercent else {
            return window.state == .unlimited ? 1 : 0
        }
        if percent == 0 {
            return 0
        }
        return max(1 / 300, Double(percent) / 100)
    }

    private var valueText: String {
        switch window.state {
        case .unlimited:
            "无限"
        case .unavailable:
            "暂不可用"
        default:
            "可用 \(window.availablePercent ?? 0)%"
        }
    }

    private var valueColor: Color {
        guard let percent = window.availablePercent else {
            return Color(hexRGBA: theme.textSecondary)
        }
        return Color(quotaRGB: QuotaColor.rgb(for: percent))
    }
}
