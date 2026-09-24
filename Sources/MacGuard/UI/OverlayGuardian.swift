import AppKit
import Foundation

/// Koruma/alarm perdesinin ekranda KALMASINI garanti eden nöbetçi.
///
/// Perdeler bir kez kuruluyordu ve kurulduğu andaki ekran listesine göre
/// şekilleniyordu. Gerçek hayatta bu yetmiyor:
///
///  - kapak kapanıp açılınca, ikinci ekran takılıp çıkarılınca ekran listesi
///    değişiyor; yeni ekranda perde hiç olmuyor, eskisinin çerçevesi kayıyor,
///  - Cmd+Q / Alt+Tab / başka bir uygulamanın penceresi odağı alınca perde
///    hâlâ görünüyor ama tuşlar ona gitmiyor, yani PIN yazılamıyor.
///
/// Nöbetçi iki şeye bakar: ekran düzeni değişti mi, ve kullanıcı az önce
/// bilgisayara dokundu mu. Dokunmayı `InputSensor`'ın izinsiz okuduğu
/// "son girdiden bu yana geçen süre" sayacından anlıyoruz — bunun için
/// Erişilebilirlik izni gerekmiyor.
@MainActor
final class OverlayGuardian {
    static let shared = OverlayGuardian()

    private var timer: Timer?
    private var screenObserver: NSObjectProtocol?
    private var lastSignature = ""
    private var lastReassert = Date.distantPast

    /// Dokunuştan sonra perdeyi ne kadar hızlı öne alalım.
    private static let pollInterval: TimeInterval = 0.35
    /// Bu kadar saniye içinde girdi olduysa "kullanıcı ekrana bastı" sayılır.
    private static let recentInputWindow: TimeInterval = 1.2
    /// Aynı dokunuş serisinde pencereyi saniyede bir kereden fazla zorlamayalım.
    private static let reassertCooldown: TimeInterval = 0.8

    private init() {}

    var isRunning: Bool { timer != nil }

    func start() {
        guard timer == nil else { return }
        lastSignature = Self.screenSignature()

        // Ekran takılıp çıkarılması: bildirim anında gelir, beklemeye gerek yok.
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main
        ) { _ in
            Task { @MainActor in OverlayGuardian.shared.screensChanged() }
        }

        let t = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { _ in
            Task { @MainActor in OverlayGuardian.shared.tick() }
        }
        t.tolerance = 0.1
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        if let screenObserver {
            NotificationCenter.default.removeObserver(screenObserver)
            self.screenObserver = nil
        }
        lastSignature = ""
    }

    // MARK: - Nöbet

    private func tick() {
        guard hasOverlay else { return }

        let signature = Self.screenSignature()
        if signature != lastSignature {
            screensChanged()
            return
        }

        // Kullanıcı az önce klavyeye/trackpad'e dokunduysa perdeyi öne al.
        // Perde zaten önde ve anahtar pencereyse hiçbir şey yapmayız, yoksa
        // her dokunuşta odak sıfırlanır ve yazılan PIN uçardı.
        guard InputSensor.secondsSinceLastInput() < Self.recentInputWindow else { return }
        guard Date().timeIntervalSince(lastReassert) > Self.reassertCooldown else { return }
        reassert()
    }

    private func screensChanged() {
        lastSignature = Self.screenSignature()
        // Perdeyi yeni ekran düzenine göre baştan kur: eksik ekrana pencere
        // koyar, kaybolan ekranınkini atar, kalanların çerçevesini düzeltir.
        AlarmOverlayController.shared.rebuildForCurrentScreens()
        LockScreenController.shared.rebuildForCurrentScreens()
        lastReassert = Date()
    }

    /// Perdeyi öne getirir ve klavyeyi ona verir. Zaten öndeyse dokunmaz.
    func reassert() {
        guard hasOverlay else { return }
        lastReassert = Date()
        if !NSApp.isActive { NSApp.activate(ignoringOtherApps: true) }
        // Alarm perdesi koruma perdesinin üstünde; varsa klavyeyi o almalı.
        if AlarmOverlayController.shared.isVisible {
            AlarmOverlayController.shared.bringToFront()
        } else {
            LockScreenController.shared.bringToFront()
        }
    }

    private var hasOverlay: Bool {
        AlarmOverlayController.shared.isVisible || LockScreenController.shared.isVisible
    }

    /// Ekran kimlikleri + çerçeveleri: düzen değiştiğinde değişen bir parmak izi.
    private static func screenSignature() -> String {
        NSScreen.screens
            .map { "\(Int($0.frame.origin.x)),\(Int($0.frame.origin.y)),\(Int($0.frame.width)),\(Int($0.frame.height))" }
            .joined(separator: "|")
    }
}

/// İki perde denetleyicisinin ortak pencere davranışı.
@MainActor
protocol OverlayPresenting: AnyObject {
    var windows: [OverlayWindow] { get set }
    /// Perde içeriğini o anki ekran listesine göre yeniden kurar.
    func rebuildForCurrentScreens()
}

extension OverlayPresenting {
    /// Tüm perde pencerelerini öne alır ve klavyeyi farenin bulunduğu ekrandaki
    /// tuş takımına verir — PIN, kullanıcının baktığı ekranda yazılabilsin diye.
    ///
    /// Perdelerden biri zaten anahtar pencereyse dokunmaz: kullanıcı bir ekranda
    /// PIN yazarken fare öbür ekranda duruyor olabilir, odağı oraya taşırsak
    /// yazdığı haneler iki tuş takımına bölünürdü.
    func bringToFront() {
        guard !windows.isEmpty else { return }
        for window in windows { window.orderFrontRegardless() }
        guard !windows.contains(where: { $0.isKeyWindow }) else { return }
        let mouse = NSEvent.mouseLocation
        let target = windows.first { $0.frame.contains(mouse) } ?? windows.first
        target?.makeKeyAndOrderFront(nil)
    }
}
