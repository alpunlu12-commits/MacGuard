import SwiftUI
import AppKit

/// Alarm perdesi klavyeyi alabilmek için anahtar pencere olabilmeli.
final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Alarm anında tüm ekranları kaplayan kırmızı perdeyi yönetir.
@MainActor
final class AlarmOverlayController: OverlayPresenting {
    static let shared = AlarmOverlayController()
    var windows: [OverlayWindow] = []
    /// Ekran düzeni değiştiğinde perdeyi aynı içerikle yeniden kurabilmek için.
    private var current: (reason: TriggerEvent, warning: Bool)?
    private init() {}

    var isVisible: Bool { !windows.isEmpty }

    func show(reason: TriggerEvent, warning: Bool = false) {
        // Uyarıdan tam alarma geçerken pencereleri yeniden kurmak yerine güncelle.
        hide()
        current = (reason, warning)
        for screen in NSScreen.screens {
            let window = OverlayWindow(contentRect: screen.frame,
                                       styleMask: [.borderless],
                                       backing: .buffered,
                                       defer: false)
            // Koruma ekranı da .screenSaver seviyesinde; alarm onun da üstünde kalmalı.
            window.level = NSWindow.Level(rawValue: NSWindow.Level.screenSaver.rawValue + 1)
            window.isOpaque = true
            window.backgroundColor = .black
            window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
            window.ignoresMouseEvents = false
            window.hasShadow = false

            // PIN tuş takımı HER ekranda. Eskiden yalnız ilk ekrandaydı; ikinci
            // ekran bağlıyken ya da kapak kapalıyken tuş takımı bakılmayan
            // ekranda kalıyor ve alarm durdurulamıyordu.
            let view = AlarmOverlayView(reason: reason, warning: warning)
            // NSHostingView'ın ideal boyutu pencereyi ekrandan taşırmasın.
            let hosting = NSHostingView(rootView: view)
            hosting.frame = CGRect(origin: .zero, size: screen.frame.size)
            hosting.autoresizingMask = [.width, .height]
            hosting.sizingOptions = []
            window.contentView = hosting
            window.setFrame(screen.frame, display: true)
            window.orderFrontRegardless()
            windows.append(window)
        }
        windows.first?.makeKeyAndOrderFront(nil)
    }

    func hide() {
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
        current = nil
    }

    /// Ekran takıldı/çıkarıldı: aynı içerikle yeni ekran listesine göre kur.
    func rebuildForCurrentScreens() {
        guard let current else { return }
        show(reason: current.reason, warning: current.warning)
    }
}

struct AlarmOverlayView: View {
    let reason: TriggerEvent
    /// true iken tam alarm değil, PIN için süre tanıyan uyarı aşaması.
    let warning: Bool

    @ObservedObject private var engine = GuardEngine.shared
    @State private var pulse = false
    @State private var elapsed = 0

    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    private var tint: Color { warning ? Theme.arming : Theme.danger }

    /// Uyarı aşamasında kalan saniye.
    private var countdown: Int? {
        if case .warning(_, let remaining) = engine.state { return remaining }
        return nil
    }

    var body: some View {
        ZStack {
            RadialGradient(colors: [tint.opacity(pulse ? 0.55 : 0.18), .black],
                           center: .center, startRadius: 60, endRadius: 900)
                .ignoresSafeArea()
                .animation(.easeInOut(duration: 0.55).repeatForever(autoreverses: true), value: pulse)

            VStack(spacing: 28) {
                Spacer()

                Image(systemName: warning ? "exclamationmark.circle.fill" : "exclamationmark.triangle.fill")
                    .font(.system(size: 88, weight: .bold))
                    .foregroundStyle(.white)
                    .shadow(color: tint, radius: pulse ? 40 : 12)

                VStack(spacing: 10) {
                    Text(warning ? "UYARI" : "ALARM")
                        .font(.system(size: 82, weight: .black, design: .rounded))
                        .tracking(14)
                        .foregroundStyle(.white)

                    Label(reason.message, systemImage: reason.kind.symbol)
                        .font(.system(size: 24, weight: .semibold, design: .rounded))
                        .foregroundStyle(.white.opacity(0.9))

                    if let countdown {
                        Text("\(countdown) saniye içinde PIN girilmezse alarm çalacak")
                            .font(.system(size: 17, weight: .semibold, design: .rounded))
                            .foregroundStyle(Theme.arming)
                    } else {
                        Text("Bu bilgisayar korumalıdır · \(elapsed) sn")
                            .font(.system(size: 15, weight: .medium, design: .monospaced))
                            .foregroundStyle(.white.opacity(0.6))
                    }
                }

                PinPadView(title: warning ? "Alarmı önlemek için PIN gir"
                                          : "Durdurmak için PIN gir") { pin in
                    engine.disarm(pin: pin)
                }
                .frame(width: 320)
                .padding(.top, 12)

                Spacer()
            }
            .padding(40)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear { pulse = true }
        .onReceive(tick) { _ in elapsed += 1 }
    }
}
