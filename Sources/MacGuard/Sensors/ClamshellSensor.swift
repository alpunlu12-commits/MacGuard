import Foundation
import AppKit
import IOKit

/// Ekran kapağının kapatılmasını IOPMrootDomain üzerinden izler.
/// Yedek olarak sistemin uyku bildirimini de dinler.
final class ClamshellSensor: Sensor {
    let kinds: [TriggerKind] = [.clamshell]
    var onTrigger: ((TriggerEvent) -> Void)?

    private var timer: DispatchSourceTimer?
    private var lastClosed = false
    private var sleepObserver: NSObjectProtocol?
    private let queue = DispatchQueue(label: "macguard.clamshell")

    /// Kapak kapalı mı? Donanım desteklemiyorsa nil.
    static func isClosed() -> Bool? {
        let entry = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPMrootDomain"))
        guard entry != 0 else { return nil }
        defer { IOObjectRelease(entry) }
        guard let prop = IORegistryEntryCreateCFProperty(entry, "AppleClamshellState" as CFString,
                                                         kCFAllocatorDefault, 0)?.takeRetainedValue()
        else { return nil }
        return (prop as? Bool) ?? ((prop as? NSNumber)?.boolValue ?? nil)
    }

    /// Bu donanımda kapak durumu okunabiliyor mu?
    static var isSupported: Bool { isClosed() != nil }

    func start() {
        lastClosed = Self.isClosed() ?? false

        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + 0.3, repeating: 0.3)
        t.setEventHandler { [weak self] in self?.poll() }
        t.resume()
        timer = t

        sleepObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.willSleepNotification, object: nil, queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            self.emit(TriggerEvent(kind: .clamshell, message: "Bilgisayar uykuya alınıyor"))
        }
    }

    func stop() {
        timer?.cancel()
        timer = nil
        if let o = sleepObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(o)
            sleepObserver = nil
        }
    }

    private func poll() {
        guard let closed = Self.isClosed() else { return }
        defer { lastClosed = closed }
        guard closed, !lastClosed else { return }
        emit(TriggerEvent(kind: .clamshell, message: "Ekran kapağı kapatıldı"))
    }
}
