import CodexQuotaKit
import SwiftUI

struct QuotaDetailsView: View {
    let state: QuotaDisplayState
    let settings: AppSettings
    let customTheme: QuotaTheme?
    let onPointerChanged: (Bool) -> Void

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Codex 可用额度")
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Color(hexRGBA: theme.textPrimary))
            ForEach(Array(state.windows.enumerated()), id: \.offset) { item in
                let window = item.element
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(window.label)
                            .font(.system(size: 12, weight: .medium))
                        Text(resetText(window.resetsAt))
                            .font(.system(size: 9))
                            .foregroundStyle(Color(hexRGBA: theme.textSecondary))
                    }
                    Spacer()
                    Text(valueText(window))
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(valueColor(window))
                        .monospacedDigit()
                }
            }
            Text(updatedText)
                .font(.system(size: 9))
                .foregroundStyle(Color(hexRGBA: theme.textSecondary))
        }
        .foregroundStyle(Color(hexRGBA: theme.textPrimary))
        .padding(12)
        .background(Color(hexRGBA: theme.surface))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay {
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color(hexRGBA: theme.border), lineWidth: 1)
        }
        .onHover(perform: onPointerChanged)
        .accessibilityElement(children: .contain)
    }

    private var theme: QuotaTheme {
        switch settings.theme {
        case .dark:
            .dark
        case .light:
            .light
        case .automatic:
            colorScheme == .dark ? .dark : .light
        case .custom:
            customTheme ?? .dark
        }
    }

    private func valueText(_ window: QuotaWindowDisplay) -> String {
        switch window.state {
        case .unlimited:
            "无限"
        case .unavailable:
            "暂不可用"
        default:
            "可用 \(window.availablePercent ?? 0)%"
        }
    }

    private func valueColor(_ window: QuotaWindowDisplay) -> Color {
        guard let percent = window.availablePercent else {
            return Color(hexRGBA: theme.textSecondary)
        }
        return Color(quotaRGB: QuotaColor.rgb(for: percent))
    }

    private func resetText(_ date: Date?) -> String {
        guard let date else {
            return "重置时间未知"
        }
        return "重置于 \(date.formatted(date: .omitted, time: .shortened))"
    }

    private var updatedText: String {
        guard let updatedAt = state.updatedAt else {
            return "尚未更新"
        }
        return "更新于 \(updatedAt.formatted(date: .omitted, time: .standard))"
    }
}
