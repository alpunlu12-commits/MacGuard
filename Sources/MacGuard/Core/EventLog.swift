import Foundation

/// Basit, kalıcı olay defteri. Son 200 kayıt tutulur.
@MainActor
final class EventLog: ObservableObject {
    static let shared = EventLog()

    struct Entry: Identifiable, Codable {
        let id: UUID
        let date: Date
        let icon: String
        let title: String
        let detail: String
        let severity: Severity

        enum Severity: String, Codable { case info, warn, alarm }
    }

    @Published private(set) var entries: [Entry] = []

    private let url: URL = {
        let fm = FileManager.default
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MacGuard", isDirectory: true)
        try? fm.createDirectory(at: base, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        try? fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: base.path)
        return base.appendingPathComponent("events.json")
    }()

    private init() {
        if let data = try? Data(contentsOf: url),
           let decoded = try? JSONDecoder().decode([Entry].self, from: data) {
            entries = decoded
        }
    }

    func log(_ title: String, detail: String = "", icon: String = "info.circle", severity: Entry.Severity = .info) {
        let e = Entry(id: UUID(), date: Date(), icon: icon, title: title, detail: detail, severity: severity)
        entries.insert(e, at: 0)
        if entries.count > 200 { entries.removeLast(entries.count - 200) }
        persist()
    }

    func clear() {
        entries.removeAll()
        persist()
    }

    private func persist() {
        guard let data = try? JSONEncoder().encode(entries) else { return }
        try? data.write(to: url, options: .atomic)
        // Nerede olduğun ve ne zaman uzaklaştığın bilgisi başkasına açık olmasın.
        try? FileManager.default.setAttributes([.posixPermissions: 0o600],
                                               ofItemAtPath: url.path)
    }
}
