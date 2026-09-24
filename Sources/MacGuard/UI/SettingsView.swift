import SwiftUI

struct SettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @ObservedObject private var settings = Settings.shared
    @ObservedObject private var engine = GuardEngine.shared

    @State private var showPinSetup = false
    @State private var pushTestResult: String?
    @State private var launchAtLogin = LoginItem.isEnabled
    @State private var loginItemFailed = false
    @State private var passwordless = false
    @State private var privilegeBusy = false
    @State private var privilegeMessage: String?
    /// Acil durum PIN alanı: perdeye ulaşılamadığında alarmı buradan durdurma.
    @State private var emergencyPin = ""
    @State private var emergencyError: String?
    @State private var emergencyDone = false
    @State private var now = Date()

    private let clock = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Label("Ayarlar", systemImage: "slider.horizontal.3")
                    .font(.title3.bold())
                    .foregroundStyle(Theme.textPrimary)
                Spacer()
                Button("Bitti") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding(20)

            Divider().overlay(Theme.stroke)

            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    sensitivitySection
                    timingSection
                    alarmSection
                    lockScreenSection
                    pushSection
                    securitySection
                    systemSection
                    aboutSection
                    emergencyPinSection
                }
                .padding(20)
            }
        }
        .frame(width: 560, height: 680)
        .background(Theme.background)
        .sheet(isPresented: $showPinSetup) { PinSetupView() }
        .onAppear { refreshPrivilegeState() }
        .onReceive(clock) { now = $0 }
    }

    // MARK: Hassasiyet

    private var sensitivitySection: some View {
        SettingsSection("Hassasiyet", icon: "dial.medium") {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("Hareket eşiği")
                    Spacer()
                    Text(String(format: "%.3f", settings.motionSensitivity))
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundStyle(Theme.textSecondary)
                }
                Slider(value: $settings.motionSensitivity, in: 0.005...0.120)
                Text("Düşük değer = daha hassas. 0.030 civarı kafe masası için iyi bir başlangıç; küçük titreşimlerde bile çalsın istiyorsan 0.012'ye indir.")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("Kare kaplama eşiği")
                    Spacer()
                    Text(String(format: "%.0f%%", settings.motionCoverage * 100))
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundStyle(Theme.textSecondary)
                }
                Slider(value: $settings.motionCoverage, in: 0.20...0.90)
                Text("Asıl ayrım bu. Bilgisayar yerinden oynadığında karenin tamamı birden değişir; önünde biri kıpırdadığında yalnızca bir bölgesi. Yüksek değer = sadece gerçek taşınmaya tepki verir, masanın önünden geçen insanlara aldırmaz.")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("Yakınlık eşiği")
                    Spacer()
                    Text(String(format: "%.2f", settings.proximityThreshold))
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundStyle(Theme.textSecondary)
                }
                Slider(value: $settings.proximityThreshold, in: 0.20...0.90)
                Text("Yüzün kadrajda kapladığı yükseklik oranı. 0.55 ≈ kol mesafesinden yakın. Değeri düşürdükçe daha uzaktaki kişiler de tetikler.")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .onChange(of: settings.motionSensitivity) { _, _ in pushThresholds() }
        .onChange(of: settings.motionCoverage) { _, _ in pushThresholds() }
        .onChange(of: settings.proximityThreshold) { _, _ in pushThresholds() }
    }

    private func pushThresholds() {
        CameraSensor.shared.configure(motionThreshold: settings.motionSensitivity,
                                      motionCoverage: settings.motionCoverage,
                                      proximityThreshold: settings.proximityThreshold)
    }

    // MARK: Zamanlama

    private var timingSection: some View {
        SettingsSection("Zamanlama", icon: "timer") {
            Stepper(value: $settings.armDelay, in: 0...60) {
                HStack {
                    Text("Devreye girme gecikmesi")
                    Spacer()
                    Text("\(settings.armDelay) sn")
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            Text("Butona bastıktan sonra kalkıp uzaklaşman için tanınan süre. Kafede 8–10 saniye yeterli.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider().overlay(Theme.stroke).padding(.vertical, 2)

            Stepper(value: $settings.graceSeconds, in: 0...30) {
                HStack {
                    Text("Uyarı süresi")
                    Spacer()
                    Text(settings.graceSeconds == 0 ? "kapalı" : "\(settings.graceSeconds) sn")
                        .foregroundStyle(Theme.textSecondary)
                }
            }
            Text("0 = tetik gelir gelmez tam alarm. Sıfırdan büyükse önce kesik bir uyarı bipi çalar ve PIN girmen için bu kadar saniye tanır; girmezsen tam siren devreye girer. Ev alarmlarındaki mantık.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Alarm

    private var alarmSection: some View {
        SettingsSection("Alarm", icon: "speaker.wave.3.fill") {
            Toggle("Alarm anında sistem sesini zorla ayarla", isOn: $settings.forceMaxVolume)

            if settings.forceMaxVolume {
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text("Alarm ses seviyesi")
                        Spacer()
                        Text("%\(Int((settings.alarmVolume * 100).rounded()))")
                            .font(.system(size: 12, weight: .semibold, design: .monospaced))
                            .foregroundStyle(settings.alarmVolume < 0.99 ? Theme.arming : Theme.textSecondary)
                    }
                    Slider(value: $settings.alarmVolume, in: 0.05...1.0)
                    if settings.alarmVolume < 0.99 {
                        Label("Test seviyesi açık — çekimden önce %100'e almayı unutma.",
                              systemImage: "exclamationmark.triangle.fill")
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(Theme.arming)
                    }
                    Text("Evde test ederken düşür. Alarm çalarken MacGuard sesi saniyede bir kontrol eder: biri ses tuşuyla kısarsa ya da sessize alırsa anında bu seviyeye geri getirir.")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Toggle("Dahili hoparlöre geç (kulaklık takılıysa bile)", isOn: $settings.forceBuiltInSpeakers)
            Toggle("Sesli uyarı oku", isOn: $settings.speakWarning)
            if settings.speakWarning {
                TextField("Uyarı metni", text: $settings.warningText, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .lineLimit(1...3)
            }
            Toggle("Alarm anında davetsiz misafirin fotoğrafını çek", isOn: $settings.captureIntruderPhoto)
            if settings.captureIntruderPhoto {
                HStack(spacing: 10) {
                    Text("Kayıt yeri: Application Support › MacGuard › Snapshots")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textSecondary)
                    Button("Klasörü aç") { SnapshotStore.revealInFinder() }
                        .font(.system(size: 11))
                    Spacer()
                }
                Text("Telefon bildirimi kapalı olsa da fotoğraf diske yazılır. En son 100 kare saklanır, eskiler silinir. Masaüstü/Belgeler gibi korunan klasörlere bilerek yazılmıyor — oraya yazmak macOS izni gerektirir ve bu uygulama ad-hoc imzalı olduğu için izin her derlemede yeniden sorulur.")
                    .font(.system(size: 10.5))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack {
                Button {
                    engine.previewSiren(seconds: 3)
                } label: {
                    Label(engine.isPreviewingSiren ? "Çalıyor…" : "Sireni 3 saniye dinle",
                          systemImage: "speaker.wave.2.bubble.left.fill")
                }
                .disabled(engine.isPreviewingSiren)
                Spacer()
            }
            .font(.system(size: 12))
            Text("Önizleme sistem sesini değiştirmez; sesi kendin ayarla. Gerçek alarmda MacGuard sesi zaten %100'e çıkarır.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider().overlay(Theme.stroke).padding(.vertical, 2)

            Toggle("Kapak kapansa bile uyuma", isOn: $settings.blockClamshellSleep)
            Text("Normalde kapak kapanınca macOS uyur ve alarm susar. Bu seçenek koruma süresince `pmset disablesleep` ayarını açar, koruma kapanınca otomatik geri alır. Bu komut root yetkisi ister — macOS'ta kapak uykusunu başka türlü engellemenin yolu yok.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            if settings.blockClamshellSleep {
                privilegeRow
            }
        }
        .disabled(engine.state.isAlarming)
    }

    // MARK: Telefon bildirimi

    private var pushSection: some View {
        SettingsSection("Telefona bildirim", icon: "iphone.radiowaves.left.and.right") {
            Toggle("Alarm anında telefonuma bildirim gönder", isOn: $settings.pushEnabled)

            if settings.pushEnabled {
                VStack(alignment: .leading, spacing: 8) {
                    TextField("Sunucu", text: $settings.pushServer)
                        .textFieldStyle(.roundedBorder)
                    TextField("Konu adı (örn. macguard-alp-7fj3)", text: $settings.pushTopic)
                        .textFieldStyle(.roundedBorder)

                    if !settings.pushTopic.trimmingCharacters(in: .whitespaces).isEmpty,
                       !NtfyClient.isValidTopic(settings.pushTopic) {
                        Label("Geçersiz konu adı. Yalnızca İngiliz alfabesi harfleri, rakam, `_` ve `-` kullanılabilir — boşluk, Türkçe karakter ve `#` olmaz.",
                              systemImage: "exclamationmark.triangle.fill")
                            .font(.system(size: 11, weight: .medium))
                            .foregroundStyle(Theme.danger)
                            .fixedSize(horizontal: false, vertical: true)
                    }

                    Text("Telefonuna ntfy uygulamasını kur ve aynı konu adına abone ol. Hesap, anahtar, kurulum yok. Konu adı herkese açıktır — tahmin edilemeyecek bir isim seç.")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)

                    HStack {
                        Button("Rastgele konu üret") {
                            // Konu adı herkese açık bir adres: fotoğraflarını
                            // koruyan tek şey tahmin edilemezliği. 20 karakter
                            // ~103 bit; 8 haneli UUID öneki (32 bit) zayıftı.
                            let alphabet = Array("abcdefghijkmnpqrstuvwxyz23456789")
                            settings.pushTopic = "macguard-" + String((0..<20).map { _ in
                                alphabet[Int.random(in: 0..<alphabet.count)]
                            })
                        }
                        Button("Test bildirimi gönder") {
                            let config = NtfyClient.Config(server: settings.pushServer, topic: settings.pushTopic)
                            guard config.url != nil else {
                                pushTestResult = "Konu adı geçersiz ya da sunucu adresi hatalı."
                                return
                            }
                            pushTestResult = "Gönderiliyor…"
                            Task {
                                await NtfyClient.send(config: config,
                                                      title: "MacGuard testi",
                                                      message: "Bildirimler çalışıyor. 👍",
                                                      priority: "default",
                                                      tags: ["white_check_mark"])
                                pushTestResult = "Gönderildi — telefonunu kontrol et."
                            }
                        }
                        .disabled(!NtfyClient.isValidTopic(settings.pushTopic))
                    }
                    .font(.system(size: 12))

                    if let pushTestResult {
                        Text(pushTestResult)
                            .font(.system(size: 11))
                            .foregroundStyle(Theme.safe)
                    }
                }
            }
        }
    }

    // MARK: Güvenlik

    private var securitySection: some View {
        SettingsSection("Güvenlik", icon: "lock.shield") {
            HStack {
                Text(PinStore.isConfigured ? "PIN tanımlı" : "PIN tanımlı değil")
                    .foregroundStyle(PinStore.isConfigured ? Theme.safe : Theme.danger)
                Spacer()
                Button(PinStore.isConfigured ? "PIN'i değiştir" : "PIN belirle") {
                    showPinSetup = true
                }
                .disabled(engine.state.isProtecting)
            }
            Text("PIN yalnızca tuzlanmış, 50.000 turlu SHA-256 özeti olarak tutulur; dosya sadece senin kullanıcınla okunabilir. Unutursan sıfırlamak için korumayı kapatıp yeniden belirlemen gerekir.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

extension SettingsView {

    /// Künye: sürüm, yazar ve bağlantılar.
    /// Bağlantılar yalnızca `AppInfo` içinde doldurulmuşsa görünür —
    /// boş bir sabit yüzünden kimse yanlış bir hesaba yönlendirilmesin.
    var aboutSection: some View {
        SettingsSection("Hakkında", icon: "info.circle") {
            HStack(spacing: 12) {
                Image(systemName: "lock.shield.fill")
                    .font(.system(size: 30))
                    .foregroundStyle(Theme.safe)
                VStack(alignment: .leading, spacing: 3) {
                    Text("\(AppInfo.name) \(AppInfo.version)")
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(Theme.textPrimary)
                    Text("Yapan: \(AppInfo.author)")
                        .font(.system(size: 12))
                        .foregroundStyle(Theme.textSecondary)
                    Text("MIT Lisansı — özgürce kullan, değiştir, dağıt")
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.textSecondary)
                }
                Spacer()
            }

            if AppInfo.instagramURL != nil || AppInfo.youtubeURL != nil || AppInfo.repositoryLink != nil {
                HStack(spacing: 10) {
                    if let url = AppInfo.instagramURL, let label = AppInfo.instagramDisplay {
                        Button {
                            NSWorkspace.shared.open(url)
                        } label: {
                            Label(label, systemImage: "camera.circle.fill")
                        }
                    }
                    if let url = AppInfo.youtubeURL, let label = AppInfo.youtubeDisplay {
                        Button {
                            NSWorkspace.shared.open(url)
                        } label: {
                            Label(label, systemImage: "play.rectangle.fill")
                        }
                    }
                    if let repo = AppInfo.repositoryLink {
                        Button {
                            NSWorkspace.shared.open(repo)
                        } label: {
                            Label("Kaynak kodu", systemImage: "chevron.left.forwardslash.chevron.right")
                        }
                    }
                    Spacer()
                }
                .font(.system(size: 12))
            }
        }
    }

    /// Koruma aktifken ekranı kaplayan caydırıcı bilgi ekranı.
    var lockScreenSection: some View {
        SettingsSection("Koruma ekranı", icon: "lock.display") {
            Toggle("Koruma aktifken ekranı kaplayan uyarı göster", isOn: $settings.showLockScreen)
            Text("Alarm çalmadan önce iş gören kısım: masaya yaklaşan kişi bilgisayara dokunmadan durumu okur. Kapatırsan koruma sessizce arka planda çalışır.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            if settings.showLockScreen {
                Divider().overlay(Theme.stroke).padding(.vertical, 2)

                Text("Ekranda yazacak metin")
                    .font(.system(size: 12, weight: .medium))
                TextEditor(text: $settings.lockScreenText)
                    .font(.system(size: 12))
                    .frame(height: 120)
                    .scrollContentBackground(.hidden)
                    .padding(6)
                    .background(RoundedRectangle(cornerRadius: 8).fill(.black.opacity(0.3)))
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Theme.stroke, lineWidth: 1))

                HStack {
                    Button("Önizle (10 sn)") { engine.previewLockScreen() }
                        .disabled(engine.state.isProtecting || engine.isPreviewingLockScreen)
                    Button("Varsayılana dön") {
                        settings.lockScreenText = Settings.defaultLockScreenText
                    }
                    Spacer()
                }
                .font(.system(size: 12))

                Text("Varsayılan metin bilerek yalnızca uygulamanın gerçekten yaptığı şeyleri sayar. İstersen değiştir — ama ekranda yazan bir şeyin arkasını uygulamanın dolduramadığını bil (örneğin konum bilgisi gönderilmiyor).")
                    .font(.system(size: 10.5))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)

                Text("Ekran uyumaz, yoksa yazı kimseye görünmez — bu yüzden koruma açıkken pil tüketimi biraz artar.")
                    .font(.system(size: 10.5))
                    .foregroundStyle(Theme.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// Yetki durumu sorgusu bir alt süreç çalıştırır; asla ana iş parçacığında olmaz.
    func refreshPrivilegeState() {
        DispatchQueue.global(qos: .userInitiated).async {
            let isPasswordless = SleepBlocker.privilegeState == .passwordless
            DispatchQueue.main.async { passwordless = isPasswordless }
        }
    }

    /// Şifreyi her seferinde sormak yerine tek seferlik dar kapsamlı bir
    /// yetki kuralı kurmayı önerir.
    var privilegeRow: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Image(systemName: passwordless ? "checkmark.seal.fill" : "key.fill")
                    .foregroundStyle(passwordless ? Theme.safe : Theme.arming)
                Text(passwordless
                     ? "Şifre sorulmayacak — yetki kuralı kurulu"
                     : "Her koruma açılışında yönetici şifresi sorulacak")
                    .font(.system(size: 12, weight: .medium))
                Spacer()
            }

            HStack {
                if passwordless {
                    Button(privilegeBusy ? "Kaldırılıyor…" : "Kuralı kaldır") {
                        privilegeBusy = true
                        privilegeMessage = nil
                        DispatchQueue.main.async {
                            let r = SleepBlocker.removePasswordlessRule()
                            privilegeMessage = r.message
                            privilegeBusy = false
                            refreshPrivilegeState()
                        }
                    }
                } else {
                    Button(privilegeBusy ? "Kuruluyor…" : "Şifreyi bir kez gir, bir daha sorma") {
                        privilegeBusy = true
                        privilegeMessage = nil
                        DispatchQueue.main.async {
                            let r = SleepBlocker.installPasswordlessRule()
                            privilegeMessage = r.message
                            privilegeBusy = false
                            refreshPrivilegeState()
                        }
                    }
                }
                Spacer()
            }
            .disabled(privilegeBusy)
            .font(.system(size: 12))

            if let privilegeMessage {
                Text(privilegeMessage)
                    .font(.system(size: 11))
                    .foregroundStyle(passwordless ? Theme.safe : Theme.danger)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Text("Kural `/etc/sudoers.d/macguard` dosyasına yazılır ve bilerek çok dardır: yalnızca senin kullanıcın, yalnızca `pmset -a disablesleep 0` ve `1` komutları. Başka hiçbir komut kapsamda değildir. Yazmadan önce `visudo` ile sözdizimi doğrulanır, böylece bozuk bir dosya sudo'yu kıramaz. İstediğin an buradan kaldırabilirsin.")
                .font(.system(size: 10.5))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous)
            .fill(Color.white.opacity(0.04)))
        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
            .stroke(Theme.stroke, lineWidth: 1))
    }

    var systemSection: some View {
        SettingsSection("Sistem", icon: "gearshape.2") {
            Toggle("Oturum açılınca MacGuard'ı başlat", isOn: Binding(
                get: { launchAtLogin },
                set: { newValue in
                    launchAtLogin = newValue
                    if !LoginItem.setEnabled(newValue) {
                        loginItemFailed = true
                        launchAtLogin = LoginItem.isEnabled
                    } else {
                        loginItemFailed = false
                    }
                }
            ))
            if loginItemFailed {
                HStack(spacing: 8) {
                    Label("macOS izin vermedi", systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(Theme.arming)
                    Button("Giriş Öğeleri'ni aç") { LoginItem.openSystemSettings() }
                }
                .font(.system(size: 11))
            }
            Text("Açık olduğunda MacGuard her açılışta sessizce hazır bekler. Koruma yine de senin başlatmanı bekler; kendiliğinden devreye girmez.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Güvenlik PIN'i (acil çıkış)

    /// Perdedeki tuş takımına ulaşılamadığı durumlar için ikinci PIN kapısı.
    ///
    /// Alarm çalarken perde ikinci ekranda kalmış, kapak kapanıp açılmış ya da
    /// uygulama arkaya düşmüş olabilir. O anda alarmı susturmanın tek yolu yine
    /// PIN olmalı — ama girilebilecek bir yer bulunmalı. Burası o yer.
    private var emergencyLockoutRemaining: Int {
        guard let until = engine.lockoutUntil else { return 0 }
        return max(0, Int(until.timeIntervalSince(now).rounded(.up)))
    }

    private var emergencyPinSection: some View {
        SettingsSection("Güvenlik PIN'i", icon: "lock.open.trianglebadge.exclamationmark") {
            Text("Alarm çalıyor ama PIN ekranı görünmüyorsa (ikinci ekranda kaldıysa, kapak kapanıp açıldıysa ya da uygulama arkaya düştüyse) alarmı buradan PIN'ini girerek durdurabilirsin.")
                .font(.system(size: 11))
                .foregroundStyle(Theme.textSecondary)
                .fixedSize(horizontal: false, vertical: true)

            HStack(spacing: 10) {
                SecureField("PIN", text: $emergencyPin)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 150)
                    .disabled(!engine.state.isProtecting || emergencyLockoutRemaining > 0)
                    .onSubmit { submitEmergencyPin() }

                Button("Alarmı durdur") { submitEmergencyPin() }
                    .disabled(!engine.state.isProtecting
                              || emergencyPin.isEmpty
                              || emergencyLockoutRemaining > 0)

                Spacer()
            }

            if !engine.state.isProtecting {
                Label(emergencyDone ? "Koruma kapatıldı." : "Koruma kapalı — burada yapılacak bir şey yok.",
                      systemImage: emergencyDone ? "checkmark.circle.fill" : "info.circle")
                    .font(.system(size: 11))
                    .foregroundStyle(emergencyDone ? Theme.safe : Theme.textSecondary)
            } else if emergencyLockoutRemaining > 0 {
                Label("Çok fazla hatalı deneme — \(emergencyLockoutRemaining) sn bekle",
                      systemImage: "hourglass")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.arming)
            } else if let emergencyError {
                Label(emergencyError, systemImage: "xmark.circle.fill")
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.danger)
            }
        }
    }

    private func submitEmergencyPin() {
        guard engine.state.isProtecting else { return }
        let entered = emergencyPin
        emergencyPin = ""
        if engine.disarm(pin: entered) {
            emergencyError = nil
            emergencyDone = true
        } else {
            // Yanlış PIN ve bekleme cezası motorda sayılıyor; burada yalnız gösteriyoruz.
            emergencyError = "PIN doğru değil."
        }
    }
}

struct SettingsSection<Content: View>: View {
    let title: String
    let icon: String
    @ViewBuilder var content: Content

    init(_ title: String, icon: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.icon = icon
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label(title, systemImage: icon)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(Theme.textPrimary)
            VStack(alignment: .leading, spacing: 12) { content }
                .font(.system(size: 13))
                .foregroundStyle(Theme.textPrimary)
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card()
        }
    }
}
