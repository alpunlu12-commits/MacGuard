import SwiftUI

enum Theme {
    static let background = Color(red: 0.043, green: 0.059, blue: 0.078)
    static let surface    = Color(red: 0.078, green: 0.102, blue: 0.129)
    static let surfaceHi  = Color(red: 0.110, green: 0.141, blue: 0.176)
    static let stroke     = Color.white.opacity(0.08)

    static let safe   = Color(red: 0.204, green: 0.827, blue: 0.600)
    static let arming = Color(red: 0.984, green: 0.749, blue: 0.141)
    static let danger = Color(red: 0.937, green: 0.267, blue: 0.267)
    static let idle   = Color(red: 0.42, green: 0.47, blue: 0.53)

    static let textPrimary   = Color.white.opacity(0.94)
    static let textSecondary = Color.white.opacity(0.55)
}

/// Koyu, ince kenarlıklı kart yüzeyi.
struct CardBackground: ViewModifier {
    var highlighted = false
    func body(content: Content) -> some View {
        content
            .background(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(highlighted ? Theme.surfaceHi : Theme.surface)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .stroke(Theme.stroke, lineWidth: 1)
            )
    }
}

extension View {
    func card(highlighted: Bool = false) -> some View {
        modifier(CardBackground(highlighted: highlighted))
    }
}
