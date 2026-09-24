import Foundation
import IOKit
import IOKit.usb

/// USB aygıtların takılmasını / çıkarılmasını IOKit eşleşme bildirimleriyle izler.
final class USBSensor: Sensor {
    let kinds: [TriggerKind] = [.usb]
    var onTrigger: ((TriggerEvent) -> Void)?

    private var notifyPort: IONotificationPortRef?
    private var addedIter: io_iterator_t = 0
    private var removedIter: io_iterator_t = 0
    /// İlk tarama mevcut aygıtları da bildirir; onları yok sayarız.
    private var primedAdd = false
    private var primedRemove = false
    private let queue = DispatchQueue(label: "macguard.usb")

    func start() {
        // Tüm durum değişiklikleri tek bir seri kuyrukta. IOKit geri çağrıları
        // da bu kuyruğa bağlandığı için ilk tarama ile gelen bildirimler
        // sıraya girer; daha önce ikisi farklı iş parçacıklarında çalışıp
        // `primed` bayrağını yarışa sokuyor ve korumayı açarken hâlihazırda
        // takılı aygıtların yanlış alarm üretmesine yol açabiliyordu.
        queue.async { self.startOnQueue() }
    }

    private func startOnQueue() {
        guard notifyPort == nil else { return }
        guard let port = IONotificationPortCreate(kIOMainPortDefault) else { return }
        notifyPort = port
        IONotificationPortSetDispatchQueue(port, queue)

        let ctx = Unmanaged.passUnretained(self).toOpaque()

        let addCallback: IOServiceMatchingCallback = { context, iterator in
            guard let context else { return }
            let me = Unmanaged<USBSensor>.fromOpaque(context).takeUnretainedValue()
            me.drain(iterator, attached: true)
        }
        let removeCallback: IOServiceMatchingCallback = { context, iterator in
            guard let context else { return }
            let me = Unmanaged<USBSensor>.fromOpaque(context).takeUnretainedValue()
            me.drain(iterator, attached: false)
        }

        // IOUSBHostDevice modern USB yığınının aygıt sınıfıdır.
        IOServiceAddMatchingNotification(port, kIOFirstMatchNotification,
                                         IOServiceMatching("IOUSBHostDevice"),
                                         addCallback, ctx, &addedIter)
        IOServiceAddMatchingNotification(port, kIOTerminatedNotification,
                                         IOServiceMatching("IOUSBHostDevice"),
                                         removeCallback, ctx, &removedIter)

        // Bildirimleri silahlandırmak için ilk listeyi boşaltmak zorunludur.
        // Aynı kuyruktayız, geri çağrılarla sıralı çalışır.
        drain(addedIter, attached: true)
        drain(removedIter, attached: false)
    }

    func stop() {
        queue.async { self.stopOnQueue() }
    }

    private func stopOnQueue() {
        if addedIter != 0 { IOObjectRelease(addedIter); addedIter = 0 }
        if removedIter != 0 { IOObjectRelease(removedIter); removedIter = 0 }
        if let port = notifyPort {
            IONotificationPortDestroy(port)
            notifyPort = nil
        }
        primedAdd = false
        primedRemove = false
    }

    private func drain(_ iterator: io_iterator_t, attached: Bool) {
        var names: [String] = []
        while case let service = IOIteratorNext(iterator), service != 0 {
            names.append(Self.deviceName(service))
            IOObjectRelease(service)
        }

        // İlk boşaltma = hâlihazırda takılı aygıtlar; tetik sayılmaz.
        if attached {
            if !primedAdd { primedAdd = true; return }
        } else {
            if !primedRemove { primedRemove = true; return }
        }

        guard !names.isEmpty else { return }
        let label = names.first ?? "USB aygıt"
        let verb = attached ? "takıldı" : "çıkarıldı"
        emit(TriggerEvent(kind: .usb, message: "\(label) \(verb)"))
    }

    private static func deviceName(_ service: io_service_t) -> String {
        for key in ["USB Product Name", "kUSBProductString", "IORegistryEntryName"] {
            if let v = IORegistryEntryCreateCFProperty(service, key as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() as? String, !v.isEmpty {
                return v
            }
        }
        var name = [CChar](repeating: 0, count: 128)
        if IORegistryEntryGetName(service, &name) == KERN_SUCCESS {
            let s = String(cString: name)
            if !s.isEmpty { return s }
        }
        return "USB aygıt"
    }
}
