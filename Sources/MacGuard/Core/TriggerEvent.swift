import Foundation

/// Bir tetikleyicinin türü. Her sensör bunlardan birini üretir.
enum TriggerKind: String, Codable, CaseIterable, Identifiable {
    case motion          // Kamera görüntüsü topluca kaydı -> bilgisayar hareket etti
    case proximity       // Yüz kadraja fazla yaklaştı
    case power           // Şarj kablosu çıkarıldı
    case clamshell       // Kapak kapatıldı
    case usb             // USB aygıt takıldı / çıkarıldı
    case input           // Klavye / trackpad'e dokunuldu
    case display         // Harici ekran bağlantısı değişti

    var id: String { rawValue }

    var title: String {
        switch self {
        case .motion:     return "Hareket"
        case .proximity:  return "Yakınlık"
        case .power:      return "Şarj Kablosu"
        case .clamshell:  return "Ekran Kapağı"
        case .usb:        return "USB Aygıt"
        case .input:      return "Klavye / Trackpad"
        case .display:    return "Harici Ekran"
        }
    }

    var detail: String {
        switch self {
        case .motion:     return "Kamera görüntüsü topluca kayarsa bilgisayar yerinden oynatılmış demektir."
        case .proximity:  return "Biri kameraya haddinden fazla yaklaşırsa uyarır."
        case .power:      return "Şarj adaptörü prizden ya da bilgisayardan çıkarılırsa."
        case .clamshell:  return "Ekran kapağı kapatılırsa."
        case .usb:        return "Bir USB bellek, kablo ya da aygıt takılır/çıkarılırsa."
        case .input:      return "Elini çektikten 2 sn sonra nöbete geçer; sonraki her dokunuş alarmı çaldırır."
        case .display:    return "Harici monitör / dock bağlantısı koparılırsa."
        }
    }

    var symbol: String {
        switch self {
        case .motion:     return "move.3d"
        case .proximity:  return "person.fill.viewfinder"
        case .power:      return "powerplug.fill"
        case .clamshell:  return "laptopcomputer"
        case .usb:        return "cable.connector"
        case .input:      return "keyboard.fill"
        case .display:    return "display.2"
        }
    }

    /// Kamera izni gerektirenler.
    var needsCamera: Bool { self == .motion || self == .proximity }
}

/// Sensörden çıkan somut olay.
struct TriggerEvent: Identifiable, Codable {
    let id: UUID
    let kind: TriggerKind
    let date: Date
    /// Kullanıcıya gösterilecek serbest metin, örn. "Şarj kablosu çıkarıldı".
    let message: String
    /// 0...1 arası şiddet; kamera sensörleri doldurur, diğerleri 1.0 verir.
    let intensity: Double

    init(kind: TriggerKind, message: String, intensity: Double = 1.0, date: Date = Date()) {
        self.id = UUID()
        self.kind = kind
        self.date = date
        self.message = message
        self.intensity = intensity
    }
}
