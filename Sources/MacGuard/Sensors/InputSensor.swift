import Foundation
import CoreGraphics

/// Klavye / trackpad dokunuşunu yakalar.
/// Olay dinlemek yerine sistemin "son girdiden bu yana geçen süre" sayacını okur;
/// bu yöntem hiçbir Erişilebilirlik izni gerektirmez.
final class InputSensor: Sensor {
    let kinds: [TriggerKind] = [.input]
    var onTrigger: ((TriggerEvent) -> Void)?

    private var timer: DispatchSourceTimer?
    private let queue = DispatchQueue(label: "macguard.input")
    private var lastFired = Date.distantPast
    /// Koruma açıldığı anda elin hâlâ klavyededir. Bir süre dokunulmadan
    /// geçmeden nöbete geçmeyiz; yoksa kendi dokunuşun alarm çaldırır.
    private var isLive = false
    private static let settleSeconds: Double = 2.0

    /// Nöbete geçti mi? Arayüz bunu gösterir, yoksa "neden çalmıyor" sorusu
    /// kullanıcı için tamamen görünmez bir bilmeceye dönüşüyor.
    var onLiveChange: ((Bool) -> Void)?

    private static let watchedTypes: [CGEventType] = [
        .keyDown, .keyUp, .flagsChanged,
        .leftMouseDown, .rightMouseDown, .otherMouseDown,
        .leftMouseDragged, .mouseMoved, .scrollWheel
    ]

    /// Kullanıcının en son girdi yaptığı andan bu yana geçen saniye.
    static func secondsSinceLastInput() -> Double {
        watchedTypes
            .map { CGEventSource.secondsSinceLastEventType(.hidSystemState, eventType: $0) }
            .min() ?? .greatestFiniteMagnitude
    }

    func start() {
        isLive = false
        let cb = onLiveChange
        DispatchQueue.main.async { cb?(false) }
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + 0.25, repeating: 0.25)
        t.setEventHandler { [weak self] in self?.poll() }
        t.resume()
        timer = t
    }

    func stop() {
        timer?.cancel()
        timer = nil
        isLive = false
        let cb = onLiveChange
        DispatchQueue.main.async { cb?(false) }
    }

    private func poll() {
        let idle = Self.secondsSinceLastInput()

        guard isLive else {
            // Belirli bir süre hiç dokunulmadıysa nöbete geç.
            if idle >= Self.settleSeconds {
                isLive = true
                let cb = onLiveChange
                DispatchQueue.main.async { cb?(true) }
            }
            return
        }

        guard idle < 0.5 else { return }
        // Aynı dokunuş serisinden saniyede bir kereden fazla olay üretme.
        guard Date().timeIntervalSince(lastFired) > 1.5 else { return }
        lastFired = Date()
        emit(TriggerEvent(kind: .input, message: "Klavyeye veya trackpad'e dokunuldu"))
    }
}
