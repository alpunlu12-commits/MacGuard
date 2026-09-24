import Foundation
import IOKit.ps

/// Şarj adaptörünün çıkarılmasını IOKit güç kaynağı bildirimiyle anında yakalar.
final class PowerSensor: Sensor {
    let kinds: [TriggerKind] = [.power]
    var onTrigger: ((TriggerEvent) -> Void)?

    private var runLoopSource: CFRunLoopSource?
    private var lastWasAC = true
    private var running = false

    /// Şu an prize takılı mı?
    static func isOnACPower() -> Bool {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue() else { return true }
        let type = IOPSGetProvidingPowerSourceType(blob)?.takeRetainedValue() as String?
        // Masaüstü Mac'lerde batarya yoktur; "AC Power" döner.
        return type != kIOPSBatteryPowerValue
    }

    func start() {
        guard !running else { return }
        running = true
        lastWasAC = Self.isOnACPower()

        let ctx = Unmanaged.passUnretained(self).toOpaque()
        guard let src = IOPSNotificationCreateRunLoopSource({ context in
            guard let context else { return }
            let me = Unmanaged<PowerSensor>.fromOpaque(context).takeUnretainedValue()
            me.evaluate()
        }, ctx)?.takeRetainedValue() else { return }

        runLoopSource = src
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .defaultMode)
    }

    func stop() {
        running = false
        if let src = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), src, .defaultMode)
            runLoopSource = nil
        }
    }

    private func evaluate() {
        guard running else { return }
        let isAC = Self.isOnACPower()
        defer { lastWasAC = isAC }
        // Sadece "takılıyken çıkarıldı" geçişi tetikler.
        guard lastWasAC, !isAC else { return }
        emit(TriggerEvent(kind: .power, message: "Şarj kablosu çıkarıldı"))
    }
}
