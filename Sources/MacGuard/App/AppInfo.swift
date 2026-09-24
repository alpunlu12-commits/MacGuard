import Foundation

/// Uygulamanın künyesi. Tek yerden düzenlenir; arayüz, kilit ekranı ve
/// "Hakkında" bölümü buradan besleniyor.
enum AppInfo {
    static let name = "MacGuard"

    /// Yazarın görünen adı — lisans ve künyede kullanılır.
    static let author = "Alp Ünlü"

    /// Sosyal hesaplar, başında @ olmadan.
    /// Boş bırakılan alan arayüzde hiç gösterilmez.
    static let instagramHandle = "alppunlu"
    static let youtubeHandle = "alpunlu"

    /// Projenin kaynak adresi. Boşsa gösterilmez.
    static let repositoryURL = ""

    // MARK: Sürüm

    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0"
    }

    static var build: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1"
    }

    /// Künye satırı: "MacGuard 1.0 · Alp Ünlü"
    static var creditLine: String {
        "\(name) \(version) · \(author)"
    }

    // MARK: Bağlantılar

    /// Kullanıcı adını temizler ve yalnızca güvenli karakterlerden oluşuyorsa döner.
    /// Sabit yanlışlıkla bozulursa hiçbir bağlantı üretilmez — yanlış bir hesaba
    /// yönlendirmektense hiç göstermemek daha iyi.
    private static func sanitize(_ handle: String) -> String? {
        let trimmed = handle.trimmingCharacters(in: .whitespaces)
            .trimmingCharacters(in: CharacterSet(charactersIn: "@"))
        guard !trimmed.isEmpty, trimmed.count <= 40 else { return nil }
        let allowed = trimmed.allSatisfy { ch in
            ch.isASCII && (ch.isLetter || ch.isNumber || ch == "." || ch == "_" || ch == "-")
        }
        return allowed ? trimmed : nil
    }

    static var instagramURL: URL? {
        sanitize(instagramHandle).flatMap { URL(string: "https://instagram.com/\($0)") }
    }

    /// Gösterim metni yalnızca geçerli bir adres üretilebiliyorsa döner;
    /// tek doğruluk kaynağı bağlantının kendisi.
    static var instagramDisplay: String? {
        guard instagramURL != nil, let h = sanitize(instagramHandle) else { return nil }
        return "@\(h)"
    }

    static var youtubeURL: URL? {
        sanitize(youtubeHandle).flatMap { URL(string: "https://youtube.com/@\($0)") }
    }

    static var youtubeDisplay: String? {
        guard youtubeURL != nil, let h = sanitize(youtubeHandle) else { return nil }
        return "@\(h)"
    }

    static var repositoryLink: URL? {
        let raw = repositoryURL.trimmingCharacters(in: .whitespaces)
        guard raw.hasPrefix("https://"), let url = URL(string: raw) else { return nil }
        return url
    }

    /// Kilit ekranındaki tek satırlık imza için: "MacGuard 1.0 · Alp Ünlü · @alppunlu"
    static var signatureLine: String {
        var parts = [creditLine]
        if let ig = instagramDisplay { parts.append(ig) }
        return parts.joined(separator: " · ")
    }
}
