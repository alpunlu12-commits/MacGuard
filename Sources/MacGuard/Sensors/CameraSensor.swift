import Foundation
import AppKit
import AVFoundation
import CoreImage
import Vision

/// Kameranın tek sahibi.
///
/// Apple Silicon Mac'lerde ivmeölçer bulunmadığı için "bilgisayar hareket etti"
/// bilgisi görüntüden çıkarılır. Aynı akış Vision ile yüz kutusunu ölçerek
/// "biri fazla yaklaştı" durumunu da verir ve alarm anında fotoğraf sağlar.
///
/// İki ayrım bu sınıfın kalbidir:
///  • **Kaplama (coverage)** — bilgisayar oynadığında karenin *tamamı* değişir.
///    Önünde biri kıpırdadığında yalnızca bir bölgesi değişir. Tetik için hem
///    değişimin şiddeti hem de kareye yayılma oranı eşiği geçmelidir.
///  • **Sahne sakinleşmesi (settle)** — koruma açıldığı anda sen hâlâ bilgisayarın
///    başındasın. Her alt sensör, ortam bir süre sakin/boş kalana kadar tetik
///    üretmez; ancak ondan sonra nöbete geçer.
final class CameraSensor: NSObject, Sensor, AVCaptureVideoDataOutputSampleBufferDelegate {
    static let shared = CameraSensor()

    /// Arayüzün gösterdiği anlık okumalar.
    struct Readings {
        var motionScore: Double = 0
        var motionCoverage: Double = 0
        var faceHeight: Double = 0
        /// Hareket tetiği nöbete geçti mi? (sahne sakinleşti mi)
        var motionLive: Bool = false
        /// Yakınlık tetiği nöbete geçti mi? (kadraj boşaldı mı)
        var proximityLive: Bool = false
    }

    let kinds: [TriggerKind] = [.motion, .proximity]
    var onTrigger: ((TriggerEvent) -> Void)?
    var onReadings: ((Readings) -> Void)?

    // Hangi analizler çalışsın.
    // Bu bayraklar YALNIZCA `queue` üzerinde okunup yazılır. Daha önce ana
    // aktörden doğrudan atanıyorlardı ve analiz kuyruğunda okunuyorlardı —
    // düzeltilmiş bir veri yarışıydı.
    private var motionEnabled = false
    private var proximityEnabled = false
    /// false iken analiz sürer ve ölçümler arayüze akar ama alarm tetiklenmez.
    /// Kalibrasyon modu bunu kullanır.
    private var triggersEnabled = true

    private let session = AVCaptureSession()
    private let output = AVCaptureVideoDataOutput()
    private let queue = DispatchQueue(label: "macguard.camera.analysis")
    private let ciContext = CIContext(options: [.useSoftwareRenderer: false])

    // MARK: Ayarlanabilir sabitler

    private static let gridW = 48
    private static let gridH = 36
    /// Bir ızgara hücresinin "değişti" sayılması için gereken parlaklık farkı (0...1).
    private static let cellChangeThreshold: Float = 0.055
    /// Hareket nöbete geçmeden önce kaç kare sakin geçmeli (~12 fps).
    private static let motionSettleFrames = 14
    /// Yakınlık nöbete geçmeden önce kaç ölçüm boş geçmeli (~3 fps).
    private static let proximitySettleChecks = 7

    // MARK: Eşikler (ana aktörden kopyalanır, analiz kuyruğunda okunur)

    private var motionThreshold: Double = 0.030
    private var motionCoverageThreshold: Double = 0.50
    private var proximityThreshold: Double = 0.55

    // MARK: Durum

    private var prevGrid: [Float]?
    private var warmupUntil = Date.distantPast
    private var motionStreak = 0
    private var proximityStreak = 0
    private var lastMotionFire = Date.distantPast
    private var lastProximityFire = Date.distantPast
    private var frameCounter = 0
    private var lastProcessed = Date.distantPast
    private var lastReadingReport = Date.distantPast

    // Sahne sakinleşmesi
    private var motionLive = false
    private var motionCalmStreak = 0
    private var proximityLive = false
    private var proximityClearStreak = 0

    // Fotoğraf önbelleği
    private let snapshotLock = NSLock()
    private var cachedJPEG: Data?
    private var nextSnapshotAt = Date.distantPast

    private(set) var isRunning = false
    private(set) var lastError: String?

    private override init() { super.init() }

    // MARK: - İzin

    static var authorization: AVAuthorizationStatus {
        AVCaptureDevice.authorizationStatus(for: .video)
    }

    static func requestAccess() async -> Bool {
        let status = authorization
        NSLog("MacGuard: kamera izni durumu = %d", status.rawValue)
        if status == .authorized { return true }
        let granted = await AVCaptureDevice.requestAccess(for: .video)
        NSLog("MacGuard: kamera izni sonucu = %@", granted ? "verildi" : "reddedildi")
        return granted
    }

    /// İzin durumunun okunabilir hâli — arayüzde göstermek için.
    static var authorizationDescription: String {
        switch authorization {
        case .authorized:    return "verildi"
        case .denied:        return "reddedildi"
        case .restricted:    return "kısıtlı"
        case .notDetermined: return "henüz sorulmadı"
        @unknown default:    return "bilinmiyor"
        }
    }

    /// Sistem Ayarları'nda Gizlilik > Kamera bölümünü açar.
    static func openSystemSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Camera")
        else { return }
        NSWorkspace.shared.open(url)
    }

    /// Sanal kameraları (OBS vb.) atlayıp dahili FaceTime kamerayı tercih eder.
    static func preferredDevice() -> AVCaptureDevice? {
        let types: [AVCaptureDevice.DeviceType] = [.builtInWideAngleCamera, .external, .continuityCamera]
        let found = AVCaptureDevice.DiscoverySession(deviceTypes: types,
                                                     mediaType: .video,
                                                     position: .unspecified).devices
        return found.first { $0.deviceType == .builtInWideAngleCamera } ?? found.first
    }

    // MARK: - Yaşam döngüsü

    /// Eşikleri ana aktörden analiz kuyruğuna taşır.
    func configure(motionThreshold: Double, motionCoverage: Double, proximityThreshold: Double) {
        queue.async {
            self.motionThreshold = motionThreshold
            self.motionCoverageThreshold = motionCoverage
            self.proximityThreshold = proximityThreshold
        }
    }

    /// Hangi analizlerin çalışacağını ve tetik yayınının açık olup olmadığını belirler.
    /// Bayraklar kuyruğa taşınarak atanır; ana iş parçacığından doğrudan yazılmaz.
    func setAnalysis(motion: Bool, proximity: Bool, triggers: Bool) {
        queue.async {
            self.motionEnabled = motion
            self.proximityEnabled = proximity
            self.triggersEnabled = triggers
        }
    }

    /// Sahne sakinleşmesini sıfırlar: her koruma başlangıcında çağrılır.
    /// Böylece kalibrasyondan ya da önceki nöbetten durum sızmaz.
    func resetSettleState() {
        queue.async {
            self.motionLive = false
            self.motionCalmStreak = 0
            self.proximityLive = false
            self.proximityClearStreak = 0
            self.motionStreak = 0
            self.proximityStreak = 0
        }
    }

    func start() {
        guard !isRunning else { return }
        guard Self.authorization == .authorized else {
            lastError = "Kamera izni verilmemiş"
            return
        }
        guard let device = Self.preferredDevice() else {
            lastError = "Kullanılabilir kamera bulunamadı"
            return
        }

        session.beginConfiguration()
        session.sessionPreset = session.canSetSessionPreset(.hd1280x720) ? .hd1280x720 : .medium

        session.inputs.forEach { session.removeInput($0) }
        session.outputs.forEach { session.removeOutput($0) }

        do {
            let input = try AVCaptureDeviceInput(device: device)
            guard session.canAddInput(input) else {
                session.commitConfiguration()
                lastError = "Kamera girişi eklenemedi"
                return
            }
            session.addInput(input)
        } catch {
            session.commitConfiguration()
            lastError = "Kamera açılamadı: \(error.localizedDescription)"
            return
        }

        output.alwaysDiscardsLateVideoFrames = true
        output.videoSettings = [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarFullRange
        ]
        output.setSampleBufferDelegate(self, queue: queue)
        guard session.canAddOutput(output) else {
            session.commitConfiguration()
            lastError = "Video çıkışı eklenemedi"
            return
        }
        session.addOutput(output)
        session.commitConfiguration()

        // Otomatik pozlama oturana kadar analiz yapma, yoksa sahte hareket üretir.
        queue.async {
            self.prevGrid = nil
            self.motionStreak = 0
            self.proximityStreak = 0
            self.motionLive = false
            self.motionCalmStreak = 0
            self.proximityLive = false
            self.proximityClearStreak = 0
            self.warmupUntil = Date().addingTimeInterval(2.0)
        }

        isRunning = true
        lastError = nil
        DispatchQueue.global(qos: .userInitiated).async { self.session.startRunning() }
    }

    func stop() {
        guard isRunning else { return }
        isRunning = false
        let s = session
        DispatchQueue.global(qos: .userInitiated).async { s.stopRunning() }
        queue.async { self.prevGrid = nil }
    }

    /// Alarm anında yakalanmış son kare (JPEG). Kamera kapalıysa nil.
    func snapshotJPEG() -> Data? {
        snapshotLock.lock()
        defer { snapshotLock.unlock() }
        return cachedJPEG
    }

    // MARK: - Kare işleme

    func captureOutput(_ o: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        let now = Date()
        // ~12 fps yeterli; CPU'yu boşuna yakmayalım.
        guard now.timeIntervalSince(lastProcessed) >= 1.0 / 12.0 else { return }
        lastProcessed = now

        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }

        if now >= nextSnapshotAt {
            nextSnapshotAt = now.addingTimeInterval(1.0)
            cacheSnapshot(pixelBuffer)
        }

        guard now >= warmupUntil else { return }

        frameCounter &+= 1
        let motion = analyseMotion(pixelBuffer)
        var faceHeight = 0.0
        if proximityEnabled, frameCounter % 4 == 0 {
            faceHeight = analyseProximity(pixelBuffer)
        }

        if now.timeIntervalSince(lastReadingReport) >= 0.12 {
            lastReadingReport = now
            let readings = Readings(motionScore: motion.score,
                                    motionCoverage: motion.coverage,
                                    faceHeight: faceHeight,
                                    motionLive: motionLive,
                                    proximityLive: proximityLive)
            let cb = onReadings
            DispatchQueue.main.async { cb?(readings) }
        }
    }

    private struct MotionReading {
        var score: Double = 0
        var coverage: Double = 0
    }

    /// Ardışık kareler arasındaki yapısal farkı ölçer.
    ///
    /// Ortalama fark çıkarılır (odanın ışığı topluca değişince tetiklenmesin) ve
    /// ayrıca değişimin kareye yayılma oranı hesaplanır. Bilgisayar kaldırıldığında
    /// hücrelerin neredeyse tamamı değişir; önünde biri kıpırdadığında yalnızca
    /// küçük bir bölge değişir. Tetik için ikisi birden gerekir.
    private func analyseMotion(_ pb: CVPixelBuffer) -> MotionReading {
        guard let grid = sampleLumaGrid(pb) else { return MotionReading() }
        defer { prevGrid = grid }
        guard let prev = prevGrid, prev.count == grid.count else { return MotionReading() }

        let n = grid.count
        var sum: Float = 0
        for i in 0..<n { sum += grid[i] - prev[i] }
        let mean = sum / Float(n)

        var acc: Float = 0
        var changed = 0
        for i in 0..<n {
            let d = abs((grid[i] - prev[i]) - mean)
            acc += d
            if d > Self.cellChangeThreshold { changed += 1 }
        }

        let reading = MotionReading(score: Double(acc / Float(n)),
                                    coverage: Double(changed) / Double(n))

        guard motionEnabled, triggersEnabled else { return reading }

        // Nöbete geçmeden önce sahnenin sakinleşmesini bekle.
        // Koruma açılırken sen hâlâ bilgisayarın başındaysan alarm çalmasın.
        if !motionLive {
            if reading.score < motionThreshold * 0.6 {
                motionCalmStreak += 1
                if motionCalmStreak >= Self.motionSettleFrames { motionLive = true }
            } else {
                motionCalmStreak = 0
            }
            return reading
        }

        let looksLikeTheMacMoved = reading.score >= motionThreshold
            && reading.coverage >= motionCoverageThreshold

        // İki ardışık kare: tek karelik gürültü alarm çaldırmasın.
        if looksLikeTheMacMoved { motionStreak += 1 } else { motionStreak = 0 }

        if motionStreak >= 2, Date().timeIntervalSince(lastMotionFire) > 3 {
            lastMotionFire = Date()
            motionStreak = 0
            emit(TriggerEvent(kind: .motion,
                              message: "Bilgisayar yerinden oynatıldı",
                              intensity: min(1, reading.coverage)))
        }
        return reading
    }

    /// Kadrajdaki en büyük yüzün yüksekliğini (0...1) ölçer.
    private func analyseProximity(_ pb: CVPixelBuffer) -> Double {
        let request = VNDetectFaceRectanglesRequest()
        let handler = VNImageRequestHandler(cvPixelBuffer: pb, orientation: .up, options: [:])
        do { try handler.perform([request]) } catch { return 0 }

        let faces = (request.results ?? [])
        let tallest = faces.map { Double($0.boundingBox.height) }.max() ?? 0

        guard triggersEnabled else { return tallest }

        // Kadraj bir süre boşalmadan yakınlık nöbete geçmez.
        // Yoksa korumayı açan kişinin kendi yüzü anında alarm çaldırır.
        if !proximityLive {
            if tallest < proximityThreshold * 0.85 {
                proximityClearStreak += 1
                if proximityClearStreak >= Self.proximitySettleChecks { proximityLive = true }
            } else {
                proximityClearStreak = 0
            }
            return tallest
        }

        if tallest >= proximityThreshold {
            proximityStreak += 1
        } else {
            proximityStreak = 0
        }

        if proximityStreak >= 2, Date().timeIntervalSince(lastProximityFire) > 5 {
            lastProximityFire = Date()
            proximityStreak = 0
            emit(TriggerEvent(kind: .proximity,
                              message: "Biri bilgisayara fazla yaklaştı",
                              intensity: min(1, tallest)))
        }
        return tallest
    }

    /// Y (parlaklık) düzleminden seyrek bir ızgara örneği alır — çözünürlükten bağımsız ucuz.
    private func sampleLumaGrid(_ pb: CVPixelBuffer) -> [Float]? {
        CVPixelBufferLockBaseAddress(pb, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pb, .readOnly) }

        let w = CVPixelBufferGetWidthOfPlane(pb, 0)
        let h = CVPixelBufferGetHeightOfPlane(pb, 0)
        guard w >= Self.gridW, h >= Self.gridH,
              let base = CVPixelBufferGetBaseAddressOfPlane(pb, 0) else { return nil }

        let bpr = CVPixelBufferGetBytesPerRowOfPlane(pb, 0)
        let ptr = base.assumingMemoryBound(to: UInt8.self)

        var grid = [Float](repeating: 0, count: Self.gridW * Self.gridH)
        let cellH = h / Self.gridH
        let cellW = w / Self.gridW
        for gy in 0..<Self.gridH {
            let y = gy * cellH + cellH / 2
            let row = ptr + y * bpr
            for gx in 0..<Self.gridW {
                let x = gx * cellW + cellW / 2
                grid[gy * Self.gridW + gx] = Float(row[x]) / 255.0
            }
        }
        return grid
    }

    private func cacheSnapshot(_ pb: CVPixelBuffer) {
        let image = CIImage(cvPixelBuffer: pb)
        guard let cs = CGColorSpace(name: CGColorSpace.sRGB) else { return }
        guard let data = ciContext.jpegRepresentation(of: image, colorSpace: cs,
                                                      options: [kCGImageDestinationLossyCompressionQuality as CIImageRepresentationOption: 0.75])
        else { return }
        snapshotLock.lock()
        cachedJPEG = data
        snapshotLock.unlock()
    }
}
