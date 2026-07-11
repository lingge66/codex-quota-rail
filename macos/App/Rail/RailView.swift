import CodexQuotaKit
import SwiftUI

struct RailView: View {
    let state: QuotaDisplayState
    let mode: RailMode
    let settings: AppSettings
    let customTheme: QuotaTheme?
    let onClick: () -> Void
    let onPointerChanged: (Bool) -> Void

    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        Group {
            if mode == .compactRail {
                VStack(spacing: 0) {
                    ForEach(Array(state.windows.prefix(2).enumerated()), id: \.offset) { item in
                        QuotaTrackView(
                            window: item.element,
                            theme: theme,
                            compact: true,
                            reduceMotion: settings.reduceMotion)
                    }
                }
            } else {
                HStack(spacing: 8) {
                    Text("CODEX")
                        .font(.system(size: 9, weight: .semibold, design: .monospaced))
                        .foregroundStyle(Color(hexRGBA: theme.textSecondary))
                    if state.windows.isEmpty {
                        Text(state.message ?? "额度暂不可用")
                            .font(.system(size: 10, weight: .regular))
                            .foregroundStyle(Color(hexRGBA: theme.textSecondary))
                            .lineLimit(1)
                    } else {
                        ForEach(Array(state.windows.prefix(2).enumerated()), id: \.offset) { item in
                            QuotaTrackView(
                                window: item.element,
                                theme: theme,
                                compact: false,
                                reduceMotion: settings.reduceMotion)
                        }
                    }
                }
                .padding(.horizontal, 8)
                .contentShape(Rectangle())
                .onTapGesture(perform: onClick)
            }
        }
        .background(Color(hexRGBA: theme.surface))
        .clipShape(RoundedRectangle(cornerRadius: mode == .compactRail ? 0 : settings.cornerRadius))
        .overlay {
            RoundedRectangle(cornerRadius: mode == .compactRail ? 0 : settings.cornerRadius)
                .stroke(Color(hexRGBA: theme.border), lineWidth: mode == .compactRail ? 0 : 1)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Codex 可用额度")
        .onHover(perform: onPointerChanged)
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
}
