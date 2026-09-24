import Foundation
import CryptoKit

/// PIN'i tuzlanmış, çok turlu SHA-256 özeti olarak saklar.
/// PIN'in kendisi hiçbir yerde düz metin tutulmaz.
///
/// Neden Anahtar Zinciri değil: MacGuard ad-hoc imzalı ve sık sık yeniden
/// derleniyor. Her derlemede imza özeti değiştiği için macOS, kaydı oluşturan
/// uygulamayla yenisini aynı saymıyor ve her açılışta "anahtar zincirine
/// erişmek istiyor" onayı çıkarıyor. O onay penceresi gelmediğinde
/// `SecItemCopyMatching` securityd'yi beklerken **süresiz kilitleniyor** ve
/// uygulama hiç açılmıyordu. Dosya tabanlı depo aynı işi yapıyor: özet yine
/// geri çevrilemez, dosya yalnızca bu kullanıcı tarafından okunabilir (0600).
enum PinStore {

    private struct Record: Codable {
        let salt: Data
        let hash: Data
        let rounds: Int
    }

    private static let rounds = 50_000

    private static var directory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MacGuard", isDirectory: true)
    }

    private static var fileURL: URL {
        directory.appendingPathComponent("pin.json")
    }

    static var isConfigured: Bool {
        FileManager.default.fileExists(atPath: fileURL.path)
    }

    static func set(_ pin: String) {
        var salt = Data(count: 32)
        salt.withUnsafeMutableBytes { buffer in
            guard let base = buffer.baseAddress else { return }
            // SecRandom yerine sistemin rastgele üretecini kullanıyoruz;
            // Security çerçevesine hiç dokunmuyoruz.
            base.assumingMemoryBound(to: UInt8.self)
                .update(from: (0..<32).map { _ in UInt8.random(in: 0...255) }, count: 32)
        }

        let record = Record(salt: salt, hash: digest(pin, salt: salt, rounds: rounds), rounds: rounds)
        guard let data = try? JSONEncoder().encode(record) else { return }

        let fm = FileManager.default
        try? fm.createDirectory(at: directory, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        // Dizin daha önce başka bir bileşen tarafından geniş izinle açılmış olabilir.
        try? fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        // Önce yaz, sonra izinleri daralt: dosya asla geniş izinle durmasın.
        try? data.write(to: fileURL, options: [.atomic])
        try? fm.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
    }

    static func verify(_ pin: String) -> Bool {
        guard let record = load() else { return false }
        let candidate = digest(pin, salt: record.salt, rounds: record.rounds)
        // Sabit süreli karşılaştırma.
        return candidate.count == record.hash.count
            && zip(candidate, record.hash).reduce(0) { $0 | ($1.0 ^ $1.1) } == 0
    }

    static func clear() {
        try? FileManager.default.removeItem(at: fileURL)
    }

    /// PIN uzayı küçük olduğu için özeti tekrarlayarak deneme maliyetini artırıyoruz.
    private static func digest(_ pin: String, salt: Data, rounds: Int) -> Data {
        var input = salt
        input.append(Data(pin.utf8))
        var out = Data(SHA256.hash(data: input))
        for _ in 0..<max(1, rounds) {
            var next = salt
            next.append(out)
            out = Data(SHA256.hash(data: next))
        }
        return out
    }

    private static func load() -> Record? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(Record.self, from: data)
    }
}
