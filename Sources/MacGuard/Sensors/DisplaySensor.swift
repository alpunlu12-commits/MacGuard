import Foundation
import AppKit

/// Harici ekran / dock bağlantısındaki değişimi izler.
final class DisplaySensor: Sensor {
    let kinds: [TriggerKind] = [.display]
    var onTrigger: ((TriggerEvent) -> Void)?

    private var observer: NSObjectProtocol?
    private var lastCount = 0

    func start() {
        lastCount = NSScreen.screens.count
        observer = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            let now = NSScreen.screens.count
            defer { self.lastCount = now }
            guard now != self.lastCount else { return }
            let msg = now < self.lastCount ? "Harici ekran bağlantısı koparıldı"
                                           : "Yeni bir ekran bağlandı"
            self.emit(TriggerEvent(kind: .display, message: msg))
        }
    }

    func stop() {
        if let o = observer {
            NotificationCenter.default.removeObserver(o)
            observer = nil
        }
    }
}
