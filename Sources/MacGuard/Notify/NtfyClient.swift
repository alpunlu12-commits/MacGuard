import Foundation

/// Telefona anlık bildirim gönderir (ntfy.sh).
/// Telefonda ntfy uygulamasından aynı konu adına abone olmak yeterlidir; hesap veya anahtar istemez.
enum NtfyClient {

    /// ntfy konu adları yalnızca ASCII harf, rakam, `_` ve `-` kabul eder.
    ///
    /// Doğrulama sadece incelik değil, güvenlik meselesi: `abc#d` gibi bir konu
    /// adı URL'de parça ayırıcı üretir ve mesaj aslında `abc` konusuna gider —
    /// yani davetsiz misafir fotoğrafı, kullanıcının haberi olmadan başkasının
    /// konusuna düşebilir. Türkçe karakter ve boşluk ise sunucu tarafından
    /// reddedilir ve bildirim sessizce hiç gitmez.
    static func isValidTopic(_ topic: String) -> Bool {
        let t = topic.trimmingCharacters(in: .whitespaces)
        guard (1...64).contains(t.count) else { return false }
        return t.allSatisfy { ch in
            ch.isASCII && (ch.isLetter || ch.isNumber || ch == "_" || ch == "-")
        }
    }

    struct Config {
        let server: String
        let topic: String

        /// Sunucu alanından yalnızca kök adresi çıkarır (şema + host + port).
        ///
        /// Kullanıcılar sunucu alanına çoğu zaman konuyu da içeren tam adresi
        /// yapıştırıyor ("https://ntfy.sh/konum"). Eskiden bu değer olduğu gibi
        /// alınıp sonuna konu bir kez daha eklendiği için mesaj
        /// "ntfy.sh/konum/konum" adresine gidiyordu: telefon hiçbir şey
        /// almıyordu ve hata da hiçbir yerde görünmüyordu.
        var normalizedServer: String? {
            var raw = server.trimmingCharacters(in: .whitespaces)
            guard !raw.isEmpty else { return nil }
            if !raw.contains("://") { raw = "https://" + raw }
            guard let u = URL(string: raw),
                  let scheme = u.scheme, scheme == "http" || scheme == "https",
                  let host = u.host, !host.isEmpty else { return nil }
            var out = "\(scheme)://\(host)"
            if let port = u.port { out += ":\(port)" }
            return out
        }

        /// Konu adı. Tam adres yapıştırılmışsa son parçayı alır.
        var normalizedTopic: String {
            var t = topic.trimmingCharacters(in: .whitespaces)
            if let slash = t.lastIndex(of: "/") {
                t = String(t[t.index(after: slash)...])
            }
            return t.trimmingCharacters(in: .whitespaces)
        }

        var url: URL? {
            guard let base = normalizedServer else { return nil }
            let t = normalizedTopic
            guard NtfyClient.isValidTopic(t) else { return nil }
            return URL(string: "\(base)/\(t)")
        }
    }

    /// Bildirim önceliği. ntfy'nin JSON API'si bunu **sayı** olarak bekler;
    /// "urgent" gibi metinler yalnızca HTTP başlığında geçerlidir. Eskiden JSON
    /// gövdesine metin konduğu için sunucu her isteği 400 ile reddediyordu.
    enum Priority: Int {
        case low = 2, normal = 3, high = 4, urgent = 5
    }

    /// Gönderim sonucu. Sessizce yutulmaz: arayüz kullanıcıya söyler.
    enum SendResult {
        case success
        case failure(String)

        var isSuccess: Bool { if case .success = self { return true }; return false }
        var message: String {
            switch self {
            case .success: return "Gönderildi"
            case .failure(let reason): return reason
            }
        }
    }

    /// Metin bildirimi. Başlık ve gövde JSON ile gider; Türkçe karakterler bozulmaz.
    @discardableResult
    static func send(config: Config,
                     title: String,
                     message: String,
                     priority: Priority = .urgent,
                     tags: [String] = ["rotating_light"]) async -> SendResult {
        guard let base = config.url,
              let root = URL(string: base.deletingLastPathComponent().absoluteString)
        else { return .failure("Sunucu adresi ya da konu adı geçersiz") }

        let payload: [String: Any] = [
            "topic": config.normalizedTopic,
            "title": title,
            "message": message,
            "priority": priority.rawValue,
            "tags": tags
        ]
        guard let body = try? JSONSerialization.data(withJSONObject: payload) else {
            return .failure("İstek gövdesi hazırlanamadı")
        }

        var request = URLRequest(url: root)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        request.timeoutInterval = 12

        return await perform(request, what: "bildirim")
    }

    /// İsteği yollar ve **durum kodunu kontrol eder**.
    ///
    /// URLSession 400/500 yanıtlarını hata saymaz; eskiden yalnızca `try`
    /// bloğuna bakıldığı için sunucunun reddettiği istekler başarı sanılıyor,
    /// kullanıcı hiçbir şey görmeden bildirimsiz kalıyordu.
    private static func perform(_ request: URLRequest, what: String) async -> SendResult {
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse else { return .success }
            guard (200..<300).contains(http.statusCode) else {
                let detail = String(data: data, encoding: .utf8)?
                    .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                NSLog("MacGuard: %@ reddedildi (HTTP %d) — %@", what, http.statusCode, detail)
                return .failure("Sunucu reddetti (HTTP \(http.statusCode))"
                                + (detail.isEmpty ? "" : ": \(detail.prefix(120))"))
            }
            return .success
        } catch {
            NSLog("MacGuard: %@ gönderilemedi — %@", what, error.localizedDescription)
            return .failure("Bağlantı kurulamadı: \(error.localizedDescription)")
        }
    }

    /// Davetsiz misafirin fotoğrafını ek olarak gönderir.
    @discardableResult
    static func sendPhoto(config: Config, jpeg: Data, filename: String = "macguard.jpg") async -> SendResult {
        guard let url = config.url else { return .failure("Adres geçersiz") }

        var request = URLRequest(url: url)
        request.httpMethod = "PUT"
        request.setValue(filename, forHTTPHeaderField: "Filename")
        // HTTP başlıkları ASCII olmak zorunda; Türkçe metin gövdedeki JSON mesajda gidiyor.
        request.setValue("MacGuard", forHTTPHeaderField: "Title")
        request.setValue("high", forHTTPHeaderField: "Priority")
        request.setValue("camera_flash", forHTTPHeaderField: "Tags")
        request.httpBody = jpeg
        request.timeoutInterval = 20

        return await perform(request, what: "fotoğraf")
    }
}
