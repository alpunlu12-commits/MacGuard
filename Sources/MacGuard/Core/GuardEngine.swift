import Foundation
import AppKit
import CoreAudio
import Combine

/// MacGuard'ın durum makinesi: sensörleri kurar, tetikleri alarma çevirir,
/// PIN doğrulanınca her şeyi eski haline döndürür.
@MainActor
final class GuardEngine: ObservableObject {
    static let shared = GuardEngine()

    enum State: Equatable {
        case disarmed
        case arming(remaining: Int)
        case armed
        /// Tetik geldi ama tam alarmdan önce PIN için süre tanınıyor.
        case warning(reason: TriggerKind, remaining: Int)
        case alarming(reason: TriggerKind)

        var isProtecting: Bool {
            switch self {
            case .disarmed: return false
            default: return true
            }
        }
        var isAlarming: Bool {
            if case .alarming = self { return true }
            return false
        }
        var isWarning: Bool {
            if case .warning = self { return true }
            return false
        }
        /// Alarm perdesinin görünmesi gereken durumlar.
        var showsOverlay: Bool { isAlarming || isWarning }
    }

    @Published private(set) var state: State = .disarmed
    @Published private(set) var lastTrigger: TriggerEvent?
    @Published private(set) var armedSince: Date?
    /// Arayüzdeki canlı hassasiyet göstergeleri.
    @Published private(set) var liveMotionScore: Double = 0
    @Published private(set) var liveMotionCoverage: Double = 0
    @Published private(set) var liveFaceHeight: Double = 0
    /// Alt sensörler nöbete geçti mi? (sahne sakinleşmesi tamamlandı mı)
    @Published private(set) var motionWatchReady = false
    @Published private(set) var proximityWatchReady = false
    @Published private(set) var inputWatchReady = false
    /// Önceki çalıştırmadan kalan uyku engeli var ve elle düzeltilmeli.
    @Published var sleepLeftoverNeedsAttention = false
    /// Yanlış PIN denemelerinden sonraki bekleme.
    @Published private(set) var lockoutUntil: Date?
    @Published private(set) var failedAttempts = 0
    @Published var lastMessage: String = ""
    /// Korumayı açmadan kamerayı çalıştırıp eşikleri ayarlama modu.
    @Published private(set) var isCalibrating = false
    /// Korumayı açmadan sirenin sesini dinleme.
    @Published private(set) var isPreviewingSiren = false
    /// Korumayı açmadan koruma ekranının nasıl göründüğünü görme.
    @Published private(set) var isPreviewingLockScreen = false

    private let settings = Settings.shared
    private let log = EventLog.shared

    private let siren = AlarmSiren()
    private let speech = SpeechAlert()
    private let sleepBlocker = SleepBlocker()
    private var sensors: [Sensor] = []

    private var countdownTimer: Timer?
    private var previousVolumeScalar: Float?
    private var previousMuted = false
    private var previousOutputDevice: AudioDeviceID?
    /// Alarm sırasında sesi kısmaya çalışanı geri alan nöbetçi.
    private var volumeWatchdog: DispatchSourceTimer?

    private var warningTimer: Timer?
    private var wakeObserver: NSObjectProtocol?

    private init() {
        // Kapak kapanıp sistem uyuduysa alarm susar. Uyanır uyanmaz sürdür.
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.resumeAfterWake() }
        }

        CameraSensor.shared.onReadings = { [weak self] r in
            guard let self else { return }
            self.liveMotionScore = r.motionScore
            self.liveMotionCoverage = r.motionCoverage
            self.liveFaceHeight = r.faceHeight
            self.motionWatchReady = r.motionLive
            self.proximityWatchReady = r.proximityLive
        }
    }

    // MARK: - Koruma açma / kapama

    /// Koruma modunu geri sayımla başlatır.
    func arm() {
        guard case .disarmed = state else { return }
        if isCalibrating { stopCalibration() }
        guard PinStore.isConfigured else {
            lastMessage = "Önce bir PIN belirlemelisin."
            return
        }

        // Kamera gerekiyorsa izni şimdi iste; geri sayım sırasında sorulmasın.
        let needsCamera = settings.isEnabled(.motion) || settings.isEnabled(.proximity) || settings.captureIntruderPhoto
        if needsCamera, CameraSensor.authorization != .authorized {
            Task { @MainActor in
                let granted = await CameraSensor.requestAccess()
                if granted {
                    self.beginCountdown()
                } else {
                    self.lastMessage = "Kamera izni verilmedi. Hareket ve yakınlık algılama çalışmayacak."
                    self.beginCountdown()
                }
            }
            return
        }
        beginCountdown()
    }

    private func beginCountdown() {
        let delay = max(0, settings.armDelay)
        log.log("Koruma başlatılıyor", detail: "\(delay) saniye sonra devrede",
                icon: "shield.lefthalf.filled", severity: .info)

        // Kamerayı geri sayım sırasında ısıt; devreye girdiğinde hazır olsun.
        prepareCamera()

        guard delay > 0 else { return finishArming() }

        state = .arming(remaining: delay)
        countdownTimer?.invalidate()
        let timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] t in
            Task { @MainActor in
                guard let self else { return }
                guard case .arming(let remaining) = self.state else { t.invalidate(); return }
                if remaining <= 1 {
                    t.invalidate()
                    self.finishArming()
                } else {
                    self.state = .arming(remaining: remaining - 1)
                }
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        countdownTimer = timer
    }

    func cancelArming() {
        guard case .arming = state else { return }
        countdownTimer?.invalidate()
        countdownTimer = nil
        CameraSensor.shared.stop()
        state = .disarmed
        log.log("Koruma iptal edildi", icon: "xmark.shield", severity: .info)
    }

    private func finishArming() {
        countdownTimer?.invalidate()
        countdownTimer = nil
        startSensors()
        sleepBlocker.beginSystemAwake()
        if settings.blockClamshellSleep {
            // Kapak kapansa bile sistem uyanık kalsın; kullanıcı şifre girmezse sessizce atlanır.
            _ = sleepBlocker.disableClamshellSleep()
        }
        armedSince = Date()
        state = .armed
        if settings.showLockScreen {
            // Ekran uyursa caydırıcı yazı kimseye görünmez.
            sleepBlocker.beginDisplayAwake(reason: "MacGuard koruma ekranı açık")
            LockScreenController.shared.show()
        }
        // Perde bir kez kurulup unutulmaz: ekran düzeni değişirse yeniden kurar,
        // kullanıcı bilgisayara dokunduğunda öne alıp klavyeyi ona verir.
        OverlayGuardian.shared.start()
        lastMessage = "Koruma aktif. Bilgisayarına göz kulak oluyorum."
        log.log("Koruma aktif", detail: activeSensorSummary(),
                icon: "checkmark.shield.fill", severity: .info)
        NotificationBanner.show(title: "MacGuard koruma modunda", body: activeSensorSummary())
    }

    /// PIN ile korumayı kapatır. Yanlış PIN artan bekleme süresiyle cezalandırılır.
    @discardableResult
    func disarm(pin: String) -> Bool {
        if let until = lockoutUntil, until > Date() { return false }

        guard PinStore.verify(pin) else {
            failedAttempts += 1
            if failedAttempts >= 3 {
                let wait = min(60, pow(2.0, Double(failedAttempts - 2)) * 5)
                lockoutUntil = Date().addingTimeInterval(wait)
            }
            log.log("Hatalı PIN denemesi", detail: "\(failedAttempts). deneme",
                    icon: "exclamationmark.lock.fill", severity: .warn)
            return false
        }

        failedAttempts = 0
        lockoutUntil = nil
        teardown(reason: "PIN doğrulandı")
        return true
    }

    /// Alarmı ve korumayı tamamen kapatır, sistemi bulduğu gibi bırakır.
    private func teardown(reason: String) {
        let wasNoisy = state.isAlarming || state.isWarning

        warningTimer?.invalidate()
        warningTimer = nil
        stopAlarmOutputs()
        stopSensors()
        sleepBlocker.endAll()
        OverlayGuardian.shared.stop()
        AlarmOverlayController.shared.hide()
        LockScreenController.shared.hide()
        restorePresentationOptions()

        countdownTimer?.invalidate()
        countdownTimer = nil
        armedSince = nil
        state = .disarmed
        lastMessage = "Koruma kapatıldı."
        log.log(wasNoisy ? "Alarm durduruldu" : "Koruma kapatıldı",
                detail: reason, icon: "shield.slash", severity: .info)
    }

    // MARK: - Sensörler

    private func prepareCamera() {
        let cam = CameraSensor.shared
        let wantsMotion = settings.isEnabled(.motion)
        let wantsProximity = settings.isEnabled(.proximity)
        let wantsSnapshot = settings.captureIntruderPhoto
        cam.setAnalysis(motion: wantsMotion, proximity: wantsProximity, triggers: true)
        cam.configure(motionThreshold: settings.motionSensitivity,
                      motionCoverage: settings.motionCoverage,
                      proximityThreshold: settings.proximityThreshold)
        if wantsMotion || wantsProximity || wantsSnapshot {
            cam.onTrigger = { [weak self] event in self?.handle(event) }
            cam.start()
        }
    }

    private func startSensors() {
        stopSensors(keepCamera: true)

        var list: [Sensor] = []
        if settings.isEnabled(.power)     { list.append(PowerSensor()) }
        if settings.isEnabled(.clamshell) { list.append(ClamshellSensor()) }
        if settings.isEnabled(.usb)       { list.append(USBSensor()) }
        if settings.isEnabled(.input) {
            let input = InputSensor()
            // Nöbete geçişi arayüze taşı; "neden çalmıyor" görünür olsun.
            input.onLiveChange = { [weak self] live in self?.inputWatchReady = live }
            list.append(input)
        }
        if settings.isEnabled(.display)   { list.append(DisplaySensor()) }

        for sensor in list {
            sensor.onTrigger = { [weak self] event in self?.handle(event) }
            sensor.start()
        }
        sensors = list

        // Kamera geri sayımda ısıtılmıştı; analizi şimdi hedef eşiklerle açıyoruz.
        // Sahne sakinleşmesini sıfırla: sen hâlâ bilgisayarın başında olabilirsin,
        // ortam sakinleşene kadar hiçbir kamera tetiği alarm çaldırmasın.
        let cam = CameraSensor.shared
        cam.configure(motionThreshold: settings.motionSensitivity,
                      motionCoverage: settings.motionCoverage,
                      proximityThreshold: settings.proximityThreshold)
        cam.resetSettleState()
    }

    private func stopSensors(keepCamera: Bool = false) {
        sensors.forEach { $0.stop() }
        sensors.removeAll()
        if !keepCamera {
            CameraSensor.shared.stop()
            liveMotionScore = 0
            liveMotionCoverage = 0
            liveFaceHeight = 0
            motionWatchReady = false
            proximityWatchReady = false
        }
        inputWatchReady = false
    }

    private func activeSensorSummary() -> String {
        let names = TriggerKind.allCases.filter { settings.isEnabled($0) }.map(\.title)
        return names.isEmpty ? "Hiçbir sensör seçili değil!" : names.joined(separator: " · ")
    }

    // MARK: - Tetik -> Alarm

    private func handle(_ event: TriggerEvent) {
        // Sadece koruma tam devredeyken tetik kabul edilir.
        guard case .armed = state else { return }
        guard settings.isEnabled(event.kind) else { return }

        lastTrigger = event
        log.log(event.kind.title, detail: event.message, icon: event.kind.symbol, severity: .alarm)

        if settings.graceSeconds > 0 {
            beginWarning(event)
        } else {
            raiseAlarm(event)
        }
    }

    /// Tam alarmdan önce PIN girmen için süre tanır: kesik bip çalar, perdeyi
    /// açar ve geri sayar. Süre dolarsa tam alarma geçer.
    private func beginWarning(_ event: TriggerEvent) {
        state = .warning(reason: event.kind, remaining: settings.graceSeconds)

        LockScreenController.shared.hide()
        applyAlarmAudio()
        siren.start(mode: .warning)
        sleepBlocker.beginDisplayAwake()
        AlarmOverlayController.shared.show(reason: event, warning: true)
        NSApp.activate(ignoringOtherApps: true)
        OverlayGuardian.shared.start()

        warningTimer?.invalidate()
        let timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] t in
            Task { @MainActor in
                guard let self else { return }
                guard case .warning(let reason, let remaining) = self.state else {
                    t.invalidate(); return
                }
                if remaining <= 1 {
                    t.invalidate()
                    self.warningTimer = nil
                    self.raiseAlarm(self.lastTrigger
                        ?? TriggerEvent(kind: reason, message: reason.title))
                } else {
                    self.state = .warning(reason: reason, remaining: remaining - 1)
                }
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        warningTimer = timer
    }

    private func raiseAlarm(_ event: TriggerEvent) {
        warningTimer?.invalidate()
        warningTimer = nil
        state = .alarming(reason: event.kind)

        // 1) Alarmın duyulacağından emin ol.
        applyAlarmAudio()

        // 2) Sesi başlat (uyarı bipi çalıyorsa tam sirene yükselir).
        siren.start(mode: .full)
        if settings.speakWarning {
            speech.startRepeating(settings.warningText)
        }

        // 3) Ekranı uyandır, kilit ekranını bas, kaçış yollarını kapat.
        sleepBlocker.beginDisplayAwake()
        applyLockdownPresentationOptions()
        LockScreenController.shared.hide()
        AlarmOverlayController.shared.show(reason: event, warning: false)
        NSApp.activate(ignoringOtherApps: true)
        OverlayGuardian.shared.start()

        // 4) Kanıtı topla ve telefona haber ver.
        Task { await self.dispatchRemoteAlert(for: event) }
    }

    /// Sesi alarm seviyesine getirir ve nöbetçiyi başlatır.
    /// Uyarı aşamasında da çağrılır: bip duyulmazsa anlamı kalmaz.
    private func applyAlarmAudio() {
        if settings.forceBuiltInSpeakers, previousOutputDevice == nil {
            previousOutputDevice = AudioOutputControl.switchToBuiltInSpeakers()
        }
        if settings.forceMaxVolume {
            if previousVolumeScalar == nil {
                previousVolumeScalar = AudioOutputControl.outputVolume()
                previousMuted = AudioOutputControl.isOutputMuted()
            }
            AudioOutputControl.setOutputVolume(Float(settings.alarmVolume))
        }
        startVolumeWatchdog()
    }

    /// Hırsız ses tuşuna basarsa ya da sessize alırsa saniyede bir geri alır.
    /// CoreAudio kullandığı için arka planda dönebiliyor; ana iş parçacığına dokunmaz.
    private func startVolumeWatchdog() {
        stopVolumeWatchdog()
        let forceVolume = settings.forceMaxVolume
        let forceDevice = settings.forceBuiltInSpeakers
        guard forceVolume || forceDevice else { return }
        let target = Float(settings.alarmVolume)

        let timer = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .userInitiated))
        timer.schedule(deadline: .now() + 1.0, repeating: 1.0)
        timer.setEventHandler {
            if forceDevice { AudioOutputControl.ensureBuiltInSpeakers() }
            guard forceVolume else { return }
            if AudioOutputControl.isOutputMuted() { AudioOutputControl.setMuted(false) }
            // Sesi yükseltene karışma, yalnızca kısılmışsa geri al.
            if let current = AudioOutputControl.outputVolume(), current < target - 0.02 {
                AudioOutputControl.setOutputVolume(target)
            }
        }
        timer.resume()
        volumeWatchdog = timer
    }

    private func stopVolumeWatchdog() {
        volumeWatchdog?.cancel()
        volumeWatchdog = nil
    }

    private func stopAlarmOutputs() {
        stopVolumeWatchdog()
        siren.stop()
        speech.stop()
        if let v = previousVolumeScalar {
            AudioOutputControl.setOutputVolume(v)
            AudioOutputControl.setMuted(previousMuted)
            previousVolumeScalar = nil
        }
        if let d = previousOutputDevice {
            AudioOutputControl.restoreOutputDevice(d)
            previousOutputDevice = nil
        }
    }

    /// Sistem uykudan uyandığında alarmı kaldığı yerden sürdürür.
    ///
    /// Kapak kapandığında macOS uyur ve ses motoru ölür. Hırsız kapağı açtığı
    /// ya da Mac herhangi bir sebeple uyandığı anda siren yeniden devreye girer.
    private func resumeAfterWake() {
        if case .armed = state {
            if settings.showLockScreen, !LockScreenController.shared.isVisible {
                LockScreenController.shared.show()
            }
            return
        }
        guard state.isAlarming || state.isWarning else { return }

        log.log("Sistem uyandı — alarm sürdürülüyor",
                icon: "alarm.waves.left.and.right.fill", severity: .alarm)

        applyAlarmAudio()

        // Uykudan sonra motor ölü olsa da isPlaying true kalabilir; zorla yeniden kur.
        siren.stop()
        siren.start(mode: state.isAlarming ? .full : .warning)
        if state.isAlarming, settings.speakWarning {
            speech.startRepeating(settings.warningText)
        }

        sleepBlocker.beginDisplayAwake()
        if let trigger = lastTrigger {
            AlarmOverlayController.shared.show(reason: trigger, warning: state.isWarning)
        }
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: - Telefona bildirim

    private func dispatchRemoteAlert(for event: TriggerEvent) async {
        let photo: Data? = settings.captureIntruderPhoto ? CameraSensor.shared.snapshotJPEG() : nil

        // Kareyi önce diske yaz: telefon bildirimi kapalı olsa da kanıt kalsın.
        if let photo {
            let saved = SnapshotStore.save(photo, kind: event.kind, date: event.date)
            log.log("Davetsiz misafir fotoğrafı kaydedildi",
                    detail: saved?.lastPathComponent ?? "kaydedilemedi",
                    icon: "camera.fill", severity: .warn)
        } else if settings.captureIntruderPhoto {
            log.log("Fotoğraf çekilemedi",
                    detail: "Kamera kapalı ya da izin verilmemiş",
                    icon: "camera.badge.ellipsis", severity: .warn)
        }

        guard settings.pushEnabled else { return }
        let config = NtfyClient.Config(server: settings.pushServer, topic: settings.pushTopic)
        guard config.url != nil else { return }

        let time = DateFormatter.localizedString(from: event.date, dateStyle: .none, timeStyle: .medium)
        let textResult = await NtfyClient.send(config: config,
                                               title: "MacGuard alarmı!",
                                               message: "\(event.message)\nSaat: \(time)")
        var photoResult: NtfyClient.SendResult?
        if let photo {
            photoResult = await NtfyClient.sendPhoto(config: config, jpeg: photo)
        }

        await MainActor.run {
            // Başarısızlığı yutma: bildirim gitmediyse kullanıcı bunu bilmeli,
            // yoksa telefonunun haber vereceğini sanarak güvenir.
            if textResult.isSuccess, photoResult?.isSuccess ?? true {
                self.log.log("Telefona bildirim gönderildi",
                             detail: "ntfy · \(self.settings.pushTopic)",
                             icon: "iphone.radiowaves.left.and.right", severity: .info)
            } else {
                let reason = textResult.isSuccess ? (photoResult?.message ?? "") : textResult.message
                self.log.log("Telefona bildirim GÖNDERİLEMEDİ", detail: reason,
                             icon: "exclamationmark.iphone", severity: .warn)
            }
        }
    }

    // MARK: - Kaçışı zorlaştırma

    private func applyLockdownPresentationOptions() {
        NSApp.presentationOptions = [
            .hideDock, .hideMenuBar,
            .disableProcessSwitching,
            .disableForceQuit,
            .disableSessionTermination,
            .disableHideApplication
        ]
    }

    private func restorePresentationOptions() {
        NSApp.presentationOptions = []
    }

    /// Koruma açıkken uygulamanın kapatılmasını engeller.
    var blocksTermination: Bool { state.isProtecting }

    /// Uygulama kapanırken sistemde iz bırakmamak için her şeyi geri al.
    func prepareForShutdown() {
        warningTimer?.invalidate()
        warningTimer = nil
        stopAlarmOutputs()
        stopSensors()
        sleepBlocker.endAll()
        OverlayGuardian.shared.stop()
        AlarmOverlayController.shared.hide()
        LockScreenController.shared.hide()
        restorePresentationOptions()
    }

    // MARK: - Kalibrasyon ve önizleme (koruma kapalıyken)

    /// Kamerayı açar, ölçümleri arayüze akıtır ama alarm çaldırmaz.
    /// Eşikleri kafedeki gerçek ortamda ayarlamak için.
    func startCalibration() {
        guard case .disarmed = state, !isCalibrating else { return }
        Task { @MainActor in
            guard await CameraSensor.requestAccess() else {
                self.lastMessage = "Kalibrasyon için kamera izni gerekiyor."
                return
            }
            let cam = CameraSensor.shared
            cam.setAnalysis(motion: true, proximity: true, triggers: false)
            cam.onTrigger = nil
            cam.configure(motionThreshold: self.settings.motionSensitivity,
                          motionCoverage: self.settings.motionCoverage,
                          proximityThreshold: self.settings.proximityThreshold)
            cam.start()
            self.isCalibrating = true
            self.lastMessage = "Kalibrasyon açık. Bilgisayarı oynat, çubuk sarı çizgiyi geçiyor mu bak."
        }
    }

    func stopCalibration() {
        guard isCalibrating else { return }
        isCalibrating = false
        CameraSensor.shared.setAnalysis(motion: false, proximity: false, triggers: true)
        CameraSensor.shared.stop()
        liveMotionScore = 0
        liveMotionCoverage = 0
        liveFaceHeight = 0
        motionWatchReady = false
        proximityWatchReady = false
        lastMessage = "Kalibrasyon kapatıldı."
    }

    /// Koruma ekranını korumayı başlatmadan gösterir.
    /// Çekimden önce metnin nasıl durduğunu görmek için.
    func previewLockScreen(seconds: Double = 10) {
        guard case .disarmed = state, !isPreviewingLockScreen else { return }
        isPreviewingLockScreen = true
        LockScreenController.shared.show(preview: true) { [weak self] in
            self?.endLockScreenPreview()
        }
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            self.endLockScreenPreview()
        }
    }

    func endLockScreenPreview() {
        guard isPreviewingLockScreen else { return }
        isPreviewingLockScreen = false
        LockScreenController.shared.hide()
    }

    /// Sireni birkaç saniye çalar. Sistem sesini değiştirmez —
    /// gerçek alarmın aksine burada sesi sen kontrol edersin.
    func previewSiren(seconds: Double = 3) {
        guard !state.isAlarming, !isPreviewingSiren else { return }
        isPreviewingSiren = true
        siren.start()
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
            self.siren.stop()
            self.isPreviewingSiren = false
        }
    }

    /// Açılışta çağrılır: kalan uyku engelini arka planda kontrol eder.
    func checkSleepLeftover() {
        DispatchQueue.global(qos: .utility).async {
            let state = SleepBlocker.recoverFromCrashIfNeeded()
            DispatchQueue.main.async {
                switch state {
                case .none:
                    break
                case .recovered:
                    self.log.log("Kalan uyku engeli geri alındı",
                                 detail: "Önceki oturum düzgün kapanmamış",
                                 icon: "bolt.badge.clock", severity: .warn)
                case .needsAttention:
                    self.sleepLeftoverNeedsAttention = true
                    self.log.log("Mac uyku engeli açık kalmış",
                                 detail: "Düzeltilmezse bilgisayar uyumaz",
                                 icon: "exclamationmark.triangle.fill", severity: .warn)
                }
            }
        }
    }

    /// Kullanıcı "Düzelt" dediğinde: yönetici şifresi sorulur.
    func fixSleepLeftover() {
        guard SleepBlocker.clearLeftoverNow() else { return }
        sleepLeftoverNeedsAttention = false
        log.log("Uyku engeli geri alındı", icon: "checkmark.circle.fill", severity: .info)
    }

    /// Sadece test amaçlı: alarmı elle çaldırır.
    func testAlarm() {
        guard case .armed = state else {
            lastMessage = "Test için önce korumayı başlat."
            return
        }
        let event = TriggerEvent(kind: .motion, message: "Test alarmı")
        lastTrigger = event
        log.log("Test alarmı", icon: "bell.badge.fill", severity: .warn)
        raiseAlarm(event)
    }
}
