import Foundation
import Combine

/// Kalıcı ayarlar. UserDefaults'a yazar, SwiftUI'ya yayınlar.
@MainActor
final class Settings: ObservableObject {
    static let shared = Settings()

    private let d = UserDefaults.standard

    // MARK: Hangi sensörler açık
    @Published var enabledTriggers: Set<TriggerKind> {
        didSet { d.set(enabledTriggers.map(\.rawValue), forKey: "enabledTriggers") }
    }

    // MARK: Hassasiyet
    /// Kamera hareket eşiği. Düşük = hassas. 0.005 ... 0.12
    @Published var motionSensitivity: Double {
        didSet { d.set(motionSensitivity, forKey: "motionSensitivity") }
    }
    /// Değişimin kareye yayılma oranı eşiği. Bilgisayar oynadığında karenin
    /// tamamı değişir; önünde biri kıpırdadığında yalnızca bir bölgesi.
    /// Yüksek değer = sadece gerçek taşınmaya tepki. 0.20 ... 0.90
    @Published var motionCoverage: Double {
        didSet { d.set(motionCoverage, forKey: "motionCoverage") }
    }
    /// Yüzün kadrajda kapladığı yükseklik oranı eşiği. 0.25 ... 0.9
    @Published var proximityThreshold: Double {
        didSet { d.set(proximityThreshold, forKey: "proximityThreshold") }
    }

    // MARK: Zamanlama
    /// Koruma başlatıldıktan sonra sensörlerin devreye girmesi için beklenen saniye.
    @Published var armDelay: Int {
        didSet { d.set(armDelay, forKey: "armDelay") }
    }
    /// Tetikten alarma kadar tanınan sessiz süre (saniye). PIN girilirse alarm çalmaz.
    @Published var graceSeconds: Int {
        didSet { d.set(graceSeconds, forKey: "graceSeconds") }
    }

    // MARK: Alarm davranışı
    @Published var forceMaxVolume: Bool { didSet { d.set(forceMaxVolume, forKey: "forceMaxVolume") } }
    /// Alarm sırasında sistem sesinin ayarlanacağı seviye (0...1).
    /// Test ederken düşürülür; gerçek kullanımda 1.0 olmalı.
    @Published var alarmVolume: Double { didSet { d.set(alarmVolume, forKey: "alarmVolume") } }
    @Published var forceBuiltInSpeakers: Bool { didSet { d.set(forceBuiltInSpeakers, forKey: "forceBuiltInSpeakers") } }
    @Published var speakWarning: Bool { didSet { d.set(speakWarning, forKey: "speakWarning") } }
    @Published var warningText: String { didSet { d.set(warningText, forKey: "warningText") } }
    @Published var captureIntruderPhoto: Bool { didSet { d.set(captureIntruderPhoto, forKey: "captureIntruderPhoto") } }
    /// Kapak kapansa bile uyumayı engelle (yönetici şifresi ister).
    @Published var blockClamshellSleep: Bool { didSet { d.set(blockClamshellSleep, forKey: "blockClamshellSleep") } }

    // MARK: Koruma ekranı
    /// Koruma aktifken ekranı kaplayan caydırıcı bilgi ekranı.
    @Published var showLockScreen: Bool { didSet { d.set(showLockScreen, forKey: "showLockScreen") } }
    @Published var lockScreenText: String { didSet { d.set(lockScreenText, forKey: "lockScreenText") } }

    /// Varsayılan metin bilerek yalnızca uygulamanın gerçekten yaptığı şeyleri sayar.
    static let defaultLockScreenText =
        "Bu bilgisayar MacGuard ile korunuyor.\n\n"
        + "\u{2022} Yerinden oynatmak, kapağı kapatmak ya da şarjı çıkarmak alarmı tetikler\n"
        + "\u{2022} Alarm yalnızca sahibinin PIN'i ile susturulabilir\n"
        + "\u{2022} Tetiklendiği anda kamera fotoğraf çeker ve sahibine bildirim gider\n\n"
        + "Lütfen dokunmayın."

    // MARK: Telefona bildirim (ntfy.sh)
    @Published var pushEnabled: Bool { didSet { d.set(pushEnabled, forKey: "pushEnabled") } }
    @Published var pushTopic: String { didSet { d.set(pushTopic, forKey: "pushTopic") } }
    @Published var pushServer: String { didSet { d.set(pushServer, forKey: "pushServer") } }

    private init() {
        if let raw = d.array(forKey: "enabledTriggers") as? [String] {
            enabledTriggers = Set(raw.compactMap(TriggerKind.init(rawValue:)))
        } else {
            enabledTriggers = [.motion, .proximity, .power, .clamshell, .usb]
        }
        motionSensitivity    = d.object(forKey: "motionSensitivity") as? Double ?? 0.030
        motionCoverage       = d.object(forKey: "motionCoverage") as? Double ?? 0.50
        proximityThreshold   = d.object(forKey: "proximityThreshold") as? Double ?? 0.55
        armDelay             = d.object(forKey: "armDelay") as? Int ?? 8
        graceSeconds         = d.object(forKey: "graceSeconds") as? Int ?? 0
        forceMaxVolume       = d.object(forKey: "forceMaxVolume") as? Bool ?? true
        alarmVolume          = d.object(forKey: "alarmVolume") as? Double ?? 1.0
        showLockScreen       = d.object(forKey: "showLockScreen") as? Bool ?? true
        lockScreenText       = d.string(forKey: "lockScreenText") ?? Settings.defaultLockScreenText
        forceBuiltInSpeakers = d.object(forKey: "forceBuiltInSpeakers") as? Bool ?? true
        speakWarning         = d.object(forKey: "speakWarning") as? Bool ?? true
        warningText          = d.string(forKey: "warningText") ?? "Dikkat! Bu bilgisayar korumalıdır. Lütfen uzaklaşın."
        captureIntruderPhoto = d.object(forKey: "captureIntruderPhoto") as? Bool ?? true
        blockClamshellSleep  = d.object(forKey: "blockClamshellSleep") as? Bool ?? false
        pushEnabled          = d.object(forKey: "pushEnabled") as? Bool ?? false
        pushTopic            = d.string(forKey: "pushTopic") ?? ""
        pushServer           = d.string(forKey: "pushServer") ?? "https://ntfy.sh"
    }

    func isEnabled(_ kind: TriggerKind) -> Bool { enabledTriggers.contains(kind) }

    func setEnabled(_ kind: TriggerKind, _ on: Bool) {
        if on { enabledTriggers.insert(kind) } else { enabledTriggers.remove(kind) }
    }
}
