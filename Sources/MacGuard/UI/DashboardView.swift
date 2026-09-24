import SwiftUI

struct DashboardView: View {
    @ObservedObject private var engine = GuardEngine.shared
    @ObservedObject private var settings = Settings.shared
    @ObservedObject private var log = EventLog.shared

    @State private var showSettings = false
    @State private var showPinSetup = false
    @State private var showDisarmSheet = false
    @State private var snapshotCount = 0

    var body: some View {
        ZStack {
            Theme.background.ignoresSafeArea()

            ScrollView {
                VStack(spacing: 22) {
                    StatusHeader()
                    if usesCamera, CameraSensor.authorization != .authorized {
                        CameraPermissionBanner()
                    }
                    actionRow
                    if engine.sleepLeftoverNeedsAttention { sleepLeftoverBar }
                    if settings.forceMaxVolume, settings.alarmVolume < 0.99 { testVolumeBar }
                    if !engine.lastMessage.isEmpty { messageBar }
                    sensorSection
                    if usesCamera && (engine.isCalibrating || engine.state.isProtecting) {
                        LiveMetersView()
                    }
                    logSection
                }
                .padding(26)
            }
        }
        .frame(minWidth: 860, minHeight: 700)
        .sheet(isPresented: $showSettings) { SettingsView() }
        .sheet(isPresented: $showPinSetup) { PinSetupView() }
        .sheet(isPresented: $showDisarmSheet) {
            VStack(spacing: 18) {
                PinPadView(title: "Korumayı kapatmak için PIN gir") { pin in
                    let ok = engine.disarm(pin: pin)
                    if ok { showDisarmSheet = false }
                    return ok
                }
                Button("Vazgeç") { showDisarmSheet = false }
                    .buttonStyle(.plain)
                    .foregroundStyle(Theme.textSecondary)
            }
            .padding(24)
            .frame(width: 380)
            .background(Theme.background)
        }
        .onAppear {
            if !PinStore.isConfigured { showPinSetup = true }
            NotificationBanner.requestPermission()
            refreshSnapshotCount()
        }
        .onChange(of: log.entries.count) { _, _ in refreshSnapshotCount() }
    }

    private var usesCamera: Bool {
        settings.isEnabled(.motion) || settings.isEnabled(.proximity)
    }

    /// Dosya sistemine bakar; ana iş parçacığını meşgul etmeyelim.
    private func refreshSnapshotCount() {
        DispatchQueue.global(qos: .utility).async {
            let n = SnapshotStore.count
            DispatchQueue.main.async { snapshotCount = n }
        }
    }

    // MARK: Eylem düğmeleri

    private var actionRow: some View {
        HStack(spacing: 12) {
            switch engine.state {
            case .disarmed:
                Button {
                    engine.arm()
                    if !PinStore.isConfigured { showPinSetup = true }
                } label: {
                    Label("KORUMAYI BAŞLAT", systemImage: "shield.fill")
                        .font(.system(size: 16, weight: .bold, design: .rounded))
                        .frame(maxWidth: .infinity, minHeight: 54)
                }
                .buttonStyle(PrimaryButtonStyle(tint: Theme.safe))

                if usesCamera {
                    Button {
                        engine.isCalibrating ? engine.stopCalibration() : engine.startCalibration()
                    } label: {
                        Label(engine.isCalibrating ? "Kalibrasyonu Bitir" : "Kamerayı Ayarla",
                              systemImage: engine.isCalibrating ? "stop.circle.fill" : "camera.viewfinder")
                            .font(.system(size: 14, weight: .semibold))
                            .frame(minHeight: 54)
                            .padding(.horizontal, 18)
                    }
                    .buttonStyle(PrimaryButtonStyle(tint: engine.isCalibrating ? Theme.arming : Theme.surfaceHi))
                }

            case .arming:
                Button {
                    engine.cancelArming()
                } label: {
                    Label("GERİ SAYIMI İPTAL ET", systemImage: "xmark")
                        .font(.system(size: 16, weight: .bold, design: .rounded))
                        .frame(maxWidth: .infinity, minHeight: 54)
                }
                .buttonStyle(PrimaryButtonStyle(tint: Theme.arming))

            case .armed:
                Button { showDisarmSheet = true } label: {
                    Label("KORUMAYI KAPAT", systemImage: "lock.open.fill")
                        .font(.system(size: 16, weight: .bold, design: .rounded))
                        .frame(maxWidth: .infinity, minHeight: 54)
                }
                .buttonStyle(PrimaryButtonStyle(tint: Theme.idle))

                Button { engine.testAlarm() } label: {
                    Label("Alarmı Test Et", systemImage: "bell.and.waves.left.and.right.fill")
                        .font(.system(size: 14, weight: .semibold))
                        .frame(minHeight: 54)
                        .padding(.horizontal, 18)
                }
                .buttonStyle(PrimaryButtonStyle(tint: Theme.danger.opacity(0.8)))

            case .warning:
                Text("Uyarı aşaması — ekrandaki PIN'i gir, alarm çalmasın")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Theme.arming)
                    .frame(maxWidth: .infinity, minHeight: 54)

            case .alarming:
                Text("Alarm çalıyor — durdurmak için ekrandaki PIN'i gir")
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Theme.danger)
                    .frame(maxWidth: .infinity, minHeight: 54)
            }

            Button { showSettings = true } label: {
                Image(systemName: "slider.horizontal.3")
                    .font(.system(size: 17, weight: .semibold))
                    .frame(width: 54, height: 54)
            }
            .buttonStyle(PrimaryButtonStyle(tint: Theme.surfaceHi))
            .disabled(engine.state.isAlarming)
        }
    }

    /// Önceki oturumdan kalan uyku engeli: sessiz bırakılırsa Mac hiç uyumaz.
    private var sleepLeftoverBar: some View {
        HStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(Theme.danger)
            VStack(alignment: .leading, spacing: 2) {
                Text("Bilgisayarın uyku engeli açık kalmış")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Theme.textPrimary)
                Text("Önceki oturum düzgün kapanmamış. Düzeltilmezse Mac uyumaz ve pil biter.")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Theme.textSecondary)
            }
            Spacer()
            Button("Düzelt") { engine.fixSleepLeftover() }
                .font(.system(size: 12, weight: .medium))
        }
        .padding(12)
        .card(highlighted: true)
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(Theme.danger.opacity(0.6), lineWidth: 1)
        )
    }

    /// Test için ses kısılmışsa çekim öncesi fark edilsin diye kalıcı uyarı.
    private var testVolumeBar: some View {
        HStack(spacing: 10) {
            Image(systemName: "speaker.wave.1.fill")
                .foregroundStyle(Theme.arming)
            Text("Alarm sesi test seviyesinde: %\(Int((settings.alarmVolume * 100).rounded()))")
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Theme.textPrimary)
            Spacer()
            Button("%100 yap") { settings.alarmVolume = 1.0 }
                .font(.system(size: 12, weight: .medium))
        }
        .padding(12)
        .card(highlighted: true)
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(Theme.arming.opacity(0.5), lineWidth: 1)
        )
    }

    private var messageBar: some View {
        Label(engine.lastMessage, systemImage: "info.circle.fill")
            .font(.system(size: 13))
            .foregroundStyle(Theme.textSecondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(12)
            .card()
    }

    // MARK: Sensörler

    private var sensorSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitle("Tetikleyiciler", subtitle: "Hangi durumda alarm çalsın?")
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 250), spacing: 12)], spacing: 12) {
                ForEach(TriggerKind.allCases) { kind in
                    SensorCard(kind: kind)
                }
            }
        }
    }

    // MARK: Kayıtlar

    private var logSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 14) {
                SectionTitle("Olay kaydı", subtitle: "Son hareketler")
                Spacer()
                if snapshotCount > 0 {
                    Button {
                        SnapshotStore.revealInFinder()
                    } label: {
                        Label("\(snapshotCount) fotoğraf", systemImage: "photo.stack.fill")
                    }
                    .buttonStyle(.plain)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(Theme.safe)
                }
                if !log.entries.isEmpty {
                    Button("Temizle") { log.clear() }
                        .buttonStyle(.plain)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Theme.textSecondary)
                }
            }

            if log.entries.isEmpty {
                Text("Henüz kayıt yok.")
                    .font(.system(size: 13))
                    .foregroundStyle(Theme.textSecondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(16)
                    .card()
            } else {
                VStack(spacing: 0) {
                    ForEach(Array(log.entries.prefix(12).enumerated()), id: \.element.id) { index, entry in
                        LogRow(entry: entry)
                        if index < min(11, log.entries.count - 1) {
                            Divider().overlay(Theme.stroke)
                        }
                    }
                }
                .card()
            }
        }
    }
}

// MARK: - Parçalar

struct StatusHeader: View {
    @ObservedObject private var engine = GuardEngine.shared
    @State private var pulse = false

    private var tint: Color {
        switch engine.state {
        case .disarmed: return Theme.idle
        case .arming:   return Theme.arming
        case .armed:    return Theme.safe
        case .warning:  return Theme.arming
        case .alarming: return Theme.danger
        }
    }

    private var symbol: String {
        switch engine.state {
        case .disarmed: return "shield.slash.fill"
        case .arming:   return "hourglass"
        case .armed:    return "checkmark.shield.fill"
        case .warning:  return "exclamationmark.circle.fill"
        case .alarming: return "exclamationmark.triangle.fill"
        }
    }

    private var headline: String {
        switch engine.state {
        case .disarmed:              return "Koruma kapalı"
        case .arming(let remaining): return "\(remaining) saniye…"
        case .armed:                 return "Koruma aktif"
        case .warning(_, let left):  return "Uyarı — \(left) sn"
        case .alarming:              return "ALARM!"
        }
    }

    private var subline: String {
        switch engine.state {
        case .disarmed:
            return "Bilgisayarın şu anda korunmuyor."
        case .arming:
            return "Çantanı al, kalk — sensörler geri sayım bitince devreye girecek."
        case .armed:
            if let since = engine.armedSince {
                let mins = Int(Date().timeIntervalSince(since)) / 60
                return mins > 0 ? "\(mins) dakikadır nöbetteyim." : "Nöbet başladı."
            }
            return "Nöbetteyim."
        case .warning:
            return "PIN girersen alarm çalmayacak."
        case .alarming(let reason):
            return engine.lastTrigger?.message ?? reason.title
        }
    }

    var body: some View {
        HStack(spacing: 20) {
            ZStack {
                Circle()
                    .fill(tint.opacity(0.14))
                    .frame(width: 92, height: 92)
                Circle()
                    .stroke(tint.opacity(pulse ? 0.7 : 0.25), lineWidth: 2)
                    .frame(width: 92, height: 92)
                    .scaleEffect(pulse ? 1.08 : 1)
                Image(systemName: symbol)
                    .font(.system(size: 38, weight: .semibold))
                    .foregroundStyle(tint)
            }
            .animation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true), value: pulse)

            VStack(alignment: .leading, spacing: 6) {
                Text("MacGuard")
                    .font(.system(size: 12, weight: .bold, design: .rounded))
                    .tracking(3)
                    .foregroundStyle(Theme.textSecondary)
                Text(headline)
                    .font(.system(size: 34, weight: .bold, design: .rounded))
                    .foregroundStyle(Theme.textPrimary)
                    .contentTransition(.numericText())
                Text(subline)
                    .font(.system(size: 14))
                    .foregroundStyle(Theme.textSecondary)
            }
            Spacer()
        }
        .padding(20)
        .card(highlighted: true)
        .onAppear { pulse = engine.state.isProtecting }
        .onChange(of: engine.state) { _, newValue in pulse = newValue.isProtecting }
    }
}

struct SensorCard: View {
    let kind: TriggerKind
    @ObservedObject private var settings = Settings.shared
    @ObservedObject private var engine = GuardEngine.shared

    init(kind: TriggerKind) { self.kind = kind }

    private var isOn: Bool { settings.isEnabled(kind) }
    private var isUnsupported: Bool {
        kind == .clamshell && !ClamshellSensor.isSupported
    }

    /// Sakinleşme bekleyen sensörler için nöbet durumu.
    /// Diğer sensörler devreye girer girmez hazırdır, onlarda rozet göstermeyiz.
    private var watchState: Bool? {
        guard isOn, engine.state.isProtecting else { return nil }
        switch kind {
        case .motion:    return engine.motionWatchReady
        case .proximity: return engine.proximityWatchReady
        case .input:     return engine.inputWatchReady
        default:         return nil
        }
    }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: kind.symbol)
                .font(.system(size: 18, weight: .medium))
                .foregroundStyle(isOn ? Theme.safe : Theme.idle)
                .frame(width: 34, height: 34)
                .background(Circle().fill((isOn ? Theme.safe : Theme.idle).opacity(0.12)))

            VStack(alignment: .leading, spacing: 4) {
                Text(kind.title)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(Theme.textPrimary)
                Text(isUnsupported ? "Bu Mac kapak durumunu bildirmiyor." : kind.detail)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
                if kind.needsCamera, CameraSensor.authorization != .authorized {
                    Label("Kamera izni gerekli", systemImage: "camera.badge.ellipsis")
                        .font(.system(size: 10.5, weight: .medium))
                        .foregroundStyle(Theme.arming)
                }
                if let ready = watchState {
                    Label(ready ? "Nöbette" : "Sakinleşme bekleniyor",
                          systemImage: ready ? "eye.fill" : "hourglass")
                        .font(.system(size: 10.5, weight: .semibold))
                        .foregroundStyle(ready ? Theme.safe : Theme.arming)
                }
            }

            Spacer(minLength: 4)

            Toggle("", isOn: Binding(
                get: { isOn },
                set: { settings.setEnabled(kind, $0) }
            ))
            .labelsHidden()
            .toggleStyle(.switch)
            .disabled(engine.state.isProtecting || isUnsupported)
        }
        .padding(14)
        .card(highlighted: isOn)
        .opacity(isUnsupported ? 0.5 : 1)
    }
}

/// Kamera sensörünün anlık okumalarını gösterir — hassasiyet ayarlarken çok işe yarar.
struct LiveMetersView: View {
    @ObservedObject private var engine = GuardEngine.shared
    @ObservedObject private var settings = Settings.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionTitle("Canlı kamera okuması",
                         subtitle: "Hareket alarmı için iki çubuk da eşiği geçmeli")
            VStack(spacing: 14) {
                if settings.isEnabled(.motion) {
                    WatchStateBadge(ready: engine.motionWatchReady,
                                    readyText: "Hareket nöbette",
                                    waitingText: "Hareket: sahne sakinleşmesi bekleniyor")
                    MeterRow(label: "Değişimin şiddeti",
                             value: engine.liveMotionScore,
                             threshold: settings.motionSensitivity,
                             maxValue: max(settings.motionSensitivity * 3, 0.12),
                             format: { String(format: "%.3f", $0) })
                    MeterRow(label: "Kareye yayılma",
                             value: engine.liveMotionCoverage,
                             threshold: settings.motionCoverage,
                             maxValue: 1.0,
                             format: { String(format: "%.0f%%", $0 * 100) })
                }
                if settings.isEnabled(.proximity) {
                    if settings.isEnabled(.motion) {
                        Divider().overlay(Theme.stroke).padding(.vertical, 2)
                    }
                    WatchStateBadge(ready: engine.proximityWatchReady,
                                    readyText: "Yakınlık nöbette",
                                    waitingText: "Yakınlık: kadrajın boşalması bekleniyor")
                    MeterRow(label: "Yüz büyüklüğü",
                             value: engine.liveFaceHeight,
                             threshold: settings.proximityThreshold,
                             maxValue: 1.0,
                             format: { String(format: "%.2f", $0) })
                }
                if !CameraSensor.shared.isRunning {
                    Text("Kamera yalnızca koruma veya kalibrasyon açıkken çalışır.")
                        .font(.system(size: 11.5))
                        .foregroundStyle(Theme.textSecondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            .padding(16)
            .card()
        }
    }
}

/// Bir alt sensörün nöbete geçip geçmediğini gösterir.
/// Koruma açıldığı anda sen hâlâ oradasın; sensörler ortam sakinleşene kadar
/// tetik üretmez ve bu rozet bunu görünür kılar.
struct WatchStateBadge: View {
    let ready: Bool
    let readyText: String
    let waitingText: String

    var body: some View {
        Label(ready ? readyText : waitingText,
              systemImage: ready ? "eye.fill" : "hourglass")
            .font(.system(size: 11.5, weight: .medium))
            .foregroundStyle(ready ? Theme.safe : Theme.arming)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct MeterRow: View {
    let label: String
    let value: Double
    let threshold: Double
    let maxValue: Double
    let format: (Double) -> String

    private var fraction: Double { min(1, max(0, value / maxValue)) }
    private var thresholdFraction: Double { min(1, max(0, threshold / maxValue)) }
    private var over: Bool { value >= threshold }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(label)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(Theme.textSecondary)
                Spacer()
                Text(format(value))
                    .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    .foregroundStyle(over ? Theme.danger : Theme.textPrimary)
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(.white.opacity(0.08))
                    Capsule()
                        .fill(over ? Theme.danger : Theme.safe)
                        .frame(width: geo.size.width * fraction)
                        .animation(.easeOut(duration: 0.12), value: fraction)
                    Rectangle()
                        .fill(Theme.arming)
                        .frame(width: 2)
                        .offset(x: geo.size.width * thresholdFraction)
                }
            }
            .frame(height: 8)
        }
    }
}

struct LogRow: View {
    let entry: EventLog.Entry

    private var tint: Color {
        switch entry.severity {
        case .info:  return Theme.idle
        case .warn:  return Theme.arming
        case .alarm: return Theme.danger
        }
    }

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: entry.icon)
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(tint)
                .frame(width: 22)
            VStack(alignment: .leading, spacing: 2) {
                Text(entry.title)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(Theme.textPrimary)
                if !entry.detail.isEmpty {
                    Text(entry.detail)
                        .font(.system(size: 11.5))
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            Spacer()
            Text(entry.date, style: .time)
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(Theme.textSecondary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

struct SectionTitle: View {
    let title: String
    let subtitle: String?
    init(_ title: String, subtitle: String? = nil) {
        self.title = title
        self.subtitle = subtitle
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(Theme.textPrimary)
            if let subtitle {
                Text(subtitle)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Theme.textSecondary)
            }
        }
    }
}

struct PrimaryButtonStyle: ButtonStyle {
    var tint: Color
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(.white)
            .background(
                RoundedRectangle(cornerRadius: 13, style: .continuous)
                    .fill(tint.opacity(configuration.isPressed ? 0.75 : 1))
            )
            .contentShape(RoundedRectangle(cornerRadius: 13, style: .continuous))
            .scaleEffect(configuration.isPressed ? 0.985 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}


/// Kamera izni verilmemişken ne yapılacağını anlatan uyarı.
struct CameraPermissionBanner: View {
    @State private var status = CameraSensor.authorizationDescription
    @State private var asking = false

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "camera.badge.ellipsis")
                .font(.system(size: 20))
                .foregroundStyle(Theme.arming)

            VStack(alignment: .leading, spacing: 3) {
                Text("Kamera izni gerekiyor — durum: \(status)")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(Theme.textPrimary)
                Text("Hareket ve yakınlık algılama kamerayı kullanır. Görüntü bilgisayarından çıkmaz.")
                    .font(.system(size: 11.5))
                    .foregroundStyle(Theme.textSecondary)
            }

            Spacer()

            Button(asking ? "Soruluyor…" : "İzin iste") {
                asking = true
                Task {
                    _ = await CameraSensor.requestAccess()
                    status = CameraSensor.authorizationDescription
                    asking = false
                }
            }
            .disabled(asking)

            Button("Sistem Ayarları") { CameraSensor.openSystemSettings() }
        }
        .font(.system(size: 12))
        .padding(14)
        .card(highlighted: true)
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(Theme.arming.opacity(0.5), lineWidth: 1)
        )
    }
}
