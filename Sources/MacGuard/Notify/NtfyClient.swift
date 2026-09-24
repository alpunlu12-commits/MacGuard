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

        /// Kırpılmış konu adı.
        var normalizedTopic: String {
            topic.trimmingCharacters(in: .whitespaces)
        }

        var url: URL? {
            let base = server.trimmingCharacters(in: .whitespaces)
                .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            let t = normalizedTopic
            guard !base.isEmpty, NtfyClient.isValidTopic(t) else { return nil }
            guard base.hasPrefix("http://") || base.hasPrefix("https://") else { return nil }
            return URL(string: "\(base)/\(t)")
        }
    }

    /// Metin bildirimi. Başlık ve gövde JSON ile gider; Türkçe karakterler bozulmaz.
    static func send(config: Config,
                     title: String,
                     message: String,
                     priority: String = "urgent",
                     tags: [String] = ["rotating_light"]) async {
        guard let base = config.url,
              let root = URL(string: base.deletingLastPathComponent().absoluteString)
        else { return }

        let payload: [String: Any] = [
            "topic": config.normalizedTopic,
            "title": title,
            "message": message,
            "priority": priority,
            "tags": tags
        ]
        guard let body = try? JSONSerialization.data(withJSONObject: payload) else { return }

        var request = URLRequest(url: root)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        request.timeoutInterval = 12

        do { _ = try await URLSession.shared.data(for: request) }
        catch { NSLog("MacGuard: bildirim gönderilemedi — %@", error.localizedDescription) }
    }

    /// Davetsiz misafirin fotoğrafını ek olarak gönderir.
    static func sendPhoto(config: Config, jpeg: Data, filename: String = "macguard.jpg") async {
        guard let url = config.url else { return }

        var request = URLRequest(url: url)
        request.httpMethod = "PUT"
        request.setValue(filename, forHTTPHeaderField: "Filename")
        // HTTP başlıkları ASCII olmak zorunda; Türkçe metin gövdedeki JSON mesajda gidiyor.
        request.setValue("MacGuard", forHTTPHeaderField: "Title")
        request.setValue("high", forHTTPHeaderField: "Priority")
        request.setValue("camera_flash", forHTTPHeaderField: "Tags")
        request.httpBody = jpeg
        request.timeoutInterval = 20

        do { _ = try await URLSession.shared.data(for: request) }
        catch { NSLog("MacGuard: fotoğraf gönderilemedi — %@", error.localizedDescription) }
    }
}
