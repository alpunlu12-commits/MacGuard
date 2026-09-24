import SwiftUI
import AppKit

/// Koruma aktifken tüm ekranları kaplayan caydırıcı bilgi ekranı.
///
/// Amaç alarm çalmadan **önce** iş görmek: masaya yaklaşan kişi bilgisayara
/// dokunmadan durumu okusun ve vazgeçsin. Bu yüzden kırmızı/panik değil,
/// sakin ve okunur bir tasarım.
@MainActor
final class LockScreenController: OverlayPresenting {
    static let shared = LockScreenController()
    var windows: [OverlayWindow] = []
    /// Ekran düzeni değişince perdeyi aynı içerikle yeniden kurabilmek için.
    private var current: (preview: Bool, onPreviewDismiss: (() -> Void)?)?
    private init() {}

    var isVisible: Bool { !windows.isEmpty }

    func show(preview: Bool = false, onPreviewDismiss: (() -> Void)? = nil) {
        guard windows.isEmpty else { return }
        current = (preview, onPreviewDismiss)
        for screen in NSScreen.screens {
            let window = OverlayWindow(contentRect: screen.frame,
                                       styleMask: [.borderless],
                                       backing: .buffered,
                                       defer: false)
            window.level = .screenSaver
            window.isOpaque = true
            window.backgroundColor = .black
            window.collectionBehavior = [.canJoinAllSpaces, .stationary,
                                         .fullScreenAuxiliary, .ignoresCycle]
            window.hasShadow = false
            // NSHostingView kendi ideal boyutunu pencereye dayatabiliyor; bu da
            // ekrandan kat kat yüksek bir pencere üretiyordu. Boyutu ekrana sabitle.
            // Tuş takımı HER ekranda: ikinci ekran bağlıyken ya da kapak
            // kapanıp açıldığında PIN alanının bakılmayan ekranda kalmaması için.
            let hosting = NSHostingView(rootView: LockScreenView(
                isPreview: preview,
                onPreviewDismiss: onPreviewDismiss))
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
        let snapshot = current
        hide()
        show(preview: snapshot.preview, onPreviewDismiss: snapshot.onPreviewDismiss)
    }
}

struct LockScreenView: View {
    /// Önizleme modunda gerçek koruma yok; PIN yalnızca ekranı kapatır.
    var isPreview: Bool = false
    var onPreviewDismiss: (() -> Void)? = nil

    @ObservedObject private var engine = GuardEngine.shared
    @ObservedObject private var settings = Settings.shared
    @State private var breathe = false
    @State private var now = Date()

    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    private var activeSensors: [TriggerKind] {
        TriggerKind.allCases.filter { settings.isEnabled($0) }
    }

    private var guardedFor: String {
        guard let since = engine.armedSince else { return "" }
        let seconds = Int(now.timeIntervalSince(since))
        let h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, s)
                     : String(format: "%02d:%02d", m, s)
    }

    var body: some View {
        ZStack {
            LinearGradient(colors: [Color(red: 0.04, green: 0.07, blue: 0.10),
                                    Color.black],
                           startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()

            // Nefes alan yeşil hale: "izleniyor" hissi, panik değil.
            RadialGradient(colors: [Theme.safe.opacity(breathe ? 0.13 : 0.05), .clear],
                           center: .center, startRadius: 40, endRadius: 760)
                .ignoresSafeArea()
                .animation(.easeInOut(duration: 2.6).repeatForever(autoreverses: true), value: breathe)

            HStack(spacing: 64) {
                Spacer(minLength: 0)

                VStack(alignment: .leading, spacing: 26) {
                    HStack(spacing: 14) {
                        Image(systemName: "lock.shield.fill")
                            .font(.system(size: 46, weight: .semibold))
                            .foregroundStyle(Theme.safe)
                        VStack(alignment: .leading, spacing: 2) {
                            Text("MACGUARD")
                                .font(.system(size: 12, weight: .bold, design: .rounded))
                                .tracking(4)
                                .foregroundStyle(Theme.safe.opacity(0.8))
                            Text("KİLİT ALTINDA")
                                .font(.system(size: 44, weight: .black, design: .rounded))
                                .tracking(2)
                                .foregroundStyle(.white)
                        }
                    }

                    Text(settings.lockScreenText)
                        .font(.system(size: 19, weight: .regular, design: .rounded))
                        .foregroundStyle(.white.opacity(0.88))
                        .lineSpacing(7)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: 560, alignment: .leading)

                    // Hangi sensörlerin nöbette olduğunu açıkça göster:
                    // caydırıcılık bilginin somut olmasından geliyor.
                    if !activeSensors.isEmpty {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("AKTİF SENSÖRLER")
                                .font(.system(size: 10.5, weight: .bold, design: .rounded))
                                .tracking(2.5)
                                .foregroundStyle(.white.opacity(0.4))
                            HStack(spacing: 10) {
                                ForEach(activeSensors) { kind in
                                    Label(kind.title, systemImage: kind.symbol)
                                        .font(.system(size: 11.5, weight: .medium))
                                        .foregroundStyle(.white.opacity(0.75))
                                        .padding(.horizontal, 10)
                                        .padding(.vertical, 6)
                                        .background(Capsule().fill(.white.opacity(0.07)))
                                        .overlay(Capsule().stroke(.white.opacity(0.12), lineWidth: 1))
                                }
                            }
                        }
                    }

                    // İnce künye: caydırıcı tabelanın altındaki "korunuyor" imzası.
                    Text(AppInfo.signatureLine)
                        .font(.system(size: 11, weight: .medium, design: .rounded))
                        .foregroundStyle(.white.opacity(0.32))

                    if isPreview {
                        Label("ÖNİZLEME — koruma aktif değil",
                              systemImage: "eye.trianglebadge.exclamationmark.fill")
                            .font(.system(size: 12, weight: .bold, design: .rounded))
                            .tracking(1.5)
                            .foregroundStyle(Theme.arming)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 7)
                            .background(Capsule().fill(Theme.arming.opacity(0.14)))
                    }

                    if !isPreview, !guardedFor.isEmpty {
                        Label("\(guardedFor) süredir nöbette", systemImage: "clock.fill")
                            .font(.system(size: 13, weight: .medium, design: .monospaced))
                            .foregroundStyle(Theme.safe.opacity(0.85))
                    }
                }

                VStack(spacing: 14) {
                    PinPadView(title: isPreview ? "PIN alanı (önizleme)"
                                                : "Korumayı kaldırmak için PIN gir") { pin in
                        guard !isPreview else {
                            onPreviewDismiss?()
                            return true
                        }
                        return engine.disarm(pin: pin)
                    }
                    .frame(width: 310)

                    if isPreview {
                        Button {
                            onPreviewDismiss?()
                        } label: {
                            Label("Önizlemeyi kapat", systemImage: "xmark.circle.fill")
                                .font(.system(size: 13, weight: .semibold))
                                .frame(width: 310, height: 40)
                        }
                        .buttonStyle(PrimaryButtonStyle(tint: Theme.idle))
                    }
                }

                Spacer(minLength: 0)
            }
            .padding(60)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear { breathe = true }
        .onReceive(tick) { now = $0 }
    }
}
