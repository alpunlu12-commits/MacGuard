import Foundation

/// Tüm sensörlerin ortak arayüzü.
/// Sensörler ana aktöre bağlı değildir; olayları `emit` ile ana kuyruğa taşırlar.
protocol Sensor: AnyObject {
    /// Bu sensörün ürettiği tetik türleri (kamera ikisini birden üretir).
    var kinds: [TriggerKind] { get }
    func start()
    func stop()
    var onTrigger: ((TriggerEvent) -> Void)? { get set }
}

extension Sensor {
    /// Olayı ana kuyruğa taşıyarak yayınlar.
    func emit(_ event: TriggerEvent) {
        let cb = onTrigger
        DispatchQueue.main.async { cb?(event) }
    }
}
