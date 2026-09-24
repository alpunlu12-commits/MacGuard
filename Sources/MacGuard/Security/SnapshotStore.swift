import Foundation
import AppKit

/// Alarm anında çekilen kareleri diske yazar.
///
/// Korunan klasörlere (Masaüstü, Belgeler, İndirilenler, Resimler) bilerek
/// dokunulmuyor: oralara yazmak macOS'tan izin istemeyi gerektirir ve bu
/// uygulama ad-hoc imzalı olduğu için her derlemede yeniden sorulur.
/// Application Support hiçbir izin istemez.
enum SnapshotStore {

    /// Diskte tutulan en fazla kare sayısı. Fazlası en eskiden silinir.
    private static let keepLimit = 100

    static var directory: URL {
        let fm = FileManager.default
        let url = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MacGuard", isDirectory: true)
            .appendingPathComponent("Snapshots", isDirectory: true)
        try? fm.createDirectory(at: url, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        try? fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: url.path)
        return url
    }

    /// Kareyi kaydeder ve dosya adını döndürür.
    @discardableResult
    static func save(_ jpeg: Data, kind: TriggerKind, date: Date = Date()) -> URL? {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        let name = "\(formatter.string(from: date))_\(kind.rawValue).jpg"
        let url = directory.appendingPathComponent(name)

        do {
            try jpeg.write(to: url, options: .atomic)
            try? FileManager.default.setAttributes([.posixPermissions: 0o600],
                                                   ofItemAtPath: url.path)
        } catch {
            NSLog("MacGuard: fotoğraf kaydedilemedi — %@", error.localizedDescription)
            return nil
        }

        pruneOldest()
        return url
    }

    /// Diskteki kareler, en yenisi başta.
    static func all() -> [URL] {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else { return [] }

        return items
            .filter { $0.pathExtension.lowercased() == "jpg" }
            .sorted { a, b in
                let da = (try? a.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
                let db = (try? b.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate ?? .distantPast
                return da > db
            }
    }

    static var count: Int { all().count }

    /// Klasörü Finder'da açar; son kare varsa onu seçili gösterir.
    static func revealInFinder() {
        let files = all()
        if let newest = files.first {
            NSWorkspace.shared.activateFileViewerSelecting([newest])
        } else {
            NSWorkspace.shared.open(directory)
        }
    }

    static func clear() {
        for url in all() { try? FileManager.default.removeItem(at: url) }
    }

    private static func pruneOldest() {
        let files = all()
        guard files.count > keepLimit else { return }
        for url in files[keepLimit...] {
            try? FileManager.default.removeItem(at: url)
        }
    }
}
