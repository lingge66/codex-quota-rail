import CodexQuotaKit
import SwiftUI

extension Color {
    init(hexRGBA: String) {
        let value = UInt64(hexRGBA.dropFirst(), radix: 16) ?? 0
        self.init(
            .sRGB,
            red: Double((value >> 24) & 0xFF) / 255,
            green: Double((value >> 16) & 0xFF) / 255,
            blue: Double((value >> 8) & 0xFF) / 255,
            opacity: Double(value & 0xFF) / 255)
    }

    init(quotaRGB: QuotaRGB) {
        self.init(
            .sRGB,
            red: quotaRGB.red,
            green: quotaRGB.green,
            blue: quotaRGB.blue,
            opacity: 1)
    }
}
