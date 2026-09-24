import Foundation
import IOKit.pwr_mgt

/// Koruma açıkken sistemin uyuyup sensörleri susturmasını engeller.
///
/// Üç kademe vardır:
///  • IOPMAssertion — boşta uykuyu engeller, hiçbir izin gerektirmez.
///  • `pmset -a disablesleep 1` — kapak kapansa bile uyumayı engeller. Bu komut
///    root ister; macOS'ta kapak uykusunu root olmadan engellemenin bir yolu yok.
///  • İsteğe bağlı, tek seferlik yetki kuralı — kurulursa `pmset` her seferinde
///    şifre sormadan çalışır.
final class SleepBlocker {

    /// Yetki kuralının kurulu olup olmadığı ve ne yapılabileceği.
    enum PrivilegeState {
        case passwordless      // kural kurulu, şifre sorulmayacak
        case asksEachTime      // kural yok, her seferinde şifre sorulacak
    }

    private static let sudoersPath = "/etc/sudoers.d/macguard"
    private static let pmset = "/usr/bin/pmset"
    private static let sudo = "/usr/bin/sudo"

    /// Uygulama çökerse ayarın açık kaldığını anlamak için.
    private static let leftoverKey = "clamshellSleepDisabledByUs"

    private var idleAssertion: IOPMAssertionID = 0
    private var displayAssertion: IOPMAssertionID = 0
    private(set) var clamshellSleepDisabled = false

    // MARK: - Uyku engelleri (izinsiz)

    /// Sistem uyumasın (ekran uyuyabilir) — koruma modu için.
    func beginSystemAwake(reason: String = "MacGuard koruma modu açık") {
        guard idleAssertion == 0 else { return }
        IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleSystemSleep as CFString,
                                    IOPMAssertionLevel(kIOPMAssertionLevelOn),
                                    reason as CFString,
                                    &idleAssertion)
    }

    /// Ekran da açık kalsın — alarm ekranı görünsün diye.
    func beginDisplayAwake(reason: String = "MacGuard alarmı çalıyor") {
        guard displayAssertion == 0 else { return }
        IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleDisplaySleep as CFString,
                                    IOPMAssertionLevel(kIOPMAssertionLevelOn),
                                    reason as CFString,
                                    &displayAssertion)
    }

    func endDisplayAwake() {
        guard displayAssertion != 0 else { return }
        IOPMAssertionRelease(displayAssertion)
        displayAssertion = 0
    }

    func endAll() {
        endDisplayAwake()
        if idleAssertion != 0 {
            IOPMAssertionRelease(idleAssertion)
            idleAssertion = 0
        }
        restoreClamshellSleep()
    }

    // MARK: - Kapak kapalıyken uyanık kalma

    /// Şifresiz çalıştırma hakkımız var mı? Hiçbir şey çalıştırmaz, sadece sorar.
    static var privilegeState: PrivilegeState {
        // `sudo -n -l <komut>` komutu çalıştırmaz, yalnızca izinli mi diye bakar.
        let r = Self.run(sudo, ["-n", "-l", pmset, "-a", "disablesleep", "1"])
        return r.status == 0 ? .passwordless : .asksEachTime
    }

    /// macOS'a kapak kapansa bile uyuma der.
    /// Yetki kuralı kuruluysa sessizce, değilse bir kez şifre sorarak.
    @discardableResult
    func disableClamshellSleep() -> Bool {
        guard !clamshellSleepDisabled else { return true }
        guard setDisableSleep(true) else { return false }
        clamshellSleepDisabled = true
        UserDefaults.standard.set(true, forKey: Self.leftoverKey)
        return true
    }

    func restoreClamshellSleep() {
        guard clamshellSleepDisabled else { return }
        _ = setDisableSleep(false)
        clamshellSleepDisabled = false
        UserDefaults.standard.set(false, forKey: Self.leftoverKey)
    }

    /// Önceki çalıştırmadan kalan uyku engelinin durumu.
    enum LeftoverState {
        /// Kalıntı yok.
        case none
        /// Kalıntı vardı, sessizce geri alındı.
        case recovered
        /// Kalıntı var ama yönetici şifresi olmadan geri alınamıyor.
        /// Kullanıcıya söylenmeli: Mac'i bir daha uyumaz.
        case needsAttention
    }

    /// Uygulama açılışında çağrılır. Bir önceki çalıştırma çökerek ayarı açık
    /// bıraktıysa geri alır — yoksa Mac bir daha hiç uyumaz.
    ///
    /// Alt süreç çalıştırır; ana iş parçacığından çağrılmamalı.
    static func recoverFromCrashIfNeeded() -> LeftoverState {
        guard UserDefaults.standard.bool(forKey: leftoverKey) else { return .none }

        if privilegeState == .passwordless,
           run(sudo, ["-n", pmset, "-a", "disablesleep", "0"]).status == 0 {
            UserDefaults.standard.set(false, forKey: leftoverKey)
            NSLog("MacGuard: önceki oturumdan kalan uyku engeli geri alındı")
            return .recovered
        }

        // Sessizce vazgeçmek en kötüsü: kullanıcının Mac'i bir daha uyumaz ve
        // sebebini bilmez. Durumu bildiriyoruz, düzeltmeyi kullanıcı onaylıyor.
        NSLog("MacGuard: uyku engeli açık kalmış, yönetici şifresi olmadan geri alınamıyor")
        return .needsAttention
    }

    /// Kalan uyku engelini yönetici şifresi sorarak geri alır.
    @discardableResult
    static func clearLeftoverNow() -> Bool {
        if run(sudo, ["-n", pmset, "-a", "disablesleep", "0"]).status == 0 {
            UserDefaults.standard.set(false, forKey: leftoverKey)
            return true
        }
        guard runPrivileged("\(pmset) -a disablesleep 0") else { return false }
        UserDefaults.standard.set(false, forKey: leftoverKey)
        return true
    }

    private func setDisableSleep(_ on: Bool) -> Bool {
        let value = on ? "1" : "0"

        // Önce şifresiz yol.
        if Self.run(Self.sudo, ["-n", Self.pmset, "-a", "disablesleep", value]).status == 0 {
            return true
        }
        // Olmadıysa macOS'un kendi yönetici penceresiyle.
        return Self.runPrivileged("\(Self.pmset) -a disablesleep \(value)")
    }

    // MARK: - Tek seferlik yetki kuralı

    /// `/etc/sudoers.d/macguard` kuralını kurar. Bir kez yönetici şifresi ister,
    /// sonrasında MacGuard bu iki komutu şifresiz çalıştırabilir.
    ///
    /// Kural bilinçli olarak çok dardır: yalnızca bu kullanıcı, yalnızca
    /// `pmset -a disablesleep 0/1`. Başka hiçbir komut kapsam dışındadır.
    @discardableResult
    static func installPasswordlessRule() -> (ok: Bool, message: String) {
        let user = NSUserName()
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-")
        guard !user.isEmpty, user.unicodeScalars.allSatisfy({ allowed.contains($0) }) else {
            return (false, "Kullanıcı adında beklenmeyen karakterler var; güvenlik için kural kurulmadı.")
        }

        let rule = """
        # MacGuard — koruma modunda kapak kapansa bile uyumayı engellemek için.
        # Kapsam bilerek dardır: yalnızca bu kullanıcı, yalnızca bu iki komut.
        \(user) ALL=(root) NOPASSWD: \(pmset) -a disablesleep 1, \(pmset) -a disablesleep 0
        """

        // Paylaşılan geçici dizin yerine yalnızca bu kullanıcının erişebildiği
        // kendi dizinimiz: root'un okuyacağı dosya ile yazdığımız dosya
        // arasında başka bir sürecin araya girme ihtimalini daraltır.
        let fm = FileManager.default
        let scratch = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("MacGuard", isDirectory: true)
        try? fm.createDirectory(at: scratch, withIntermediateDirectories: true,
                                attributes: [.posixPermissions: 0o700])
        try? fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: scratch.path)
        let tmp = scratch.appendingPathComponent("sudoers-\(UUID().uuidString)")
        let tmpPath = tmp.path
        guard !tmpPath.contains("'") else {
            return (false, "Geçici dosya yolu beklenmedik biçimde; kural kurulmadı.")
        }
        defer { try? FileManager.default.removeItem(at: tmp) }

        do {
            try rule.write(to: tmp, atomically: true, encoding: .utf8)
            try fm.setAttributes([.posixPermissions: 0o600], ofItemAtPath: tmp.path)
        } catch {
            return (false, "Geçici dosya yazılamadı: \(error.localizedDescription)")
        }

        // visudo -cf önce sözdizimini doğrular. Bozuk bir dosya asla
        // /etc/sudoers.d içine kopyalanmaz — yoksa sudo tamamen kırılabilir.
        let command = "/usr/sbin/visudo -cf '\(tmpPath)' && "
                    + "/usr/bin/install -m 0440 -o root -g wheel '\(tmpPath)' '\(sudoersPath)'"

        guard runPrivileged(command) else {
            return (false, "Kural kurulamadı. Şifre penceresi iptal edilmiş olabilir.")
        }

        guard privilegeState == .passwordless else {
            return (false, "Kural yazıldı ama etkin olmadı. /etc/sudoers dosyasında "
                         + "sudoers.d dizininin dahil edilmemiş olması mümkün.")
        }
        return (true, "Kuruldu. Bundan sonra şifre sorulmayacak.")
    }

    /// Kuralı kaldırır. Bir kez yönetici şifresi ister.
    @discardableResult
    static func removePasswordlessRule() -> (ok: Bool, message: String) {
        guard runPrivileged("/bin/rm -f '\(sudoersPath)'") else {
            return (false, "Kaldırılamadı. Şifre penceresi iptal edilmiş olabilir.")
        }
        return (true, "Kaldırıldı. Artık her seferinde şifre sorulacak.")
    }

    // MARK: - Süreç çalıştırma yardımcıları

    private static func run(_ path: String, _ args: [String]) -> (status: Int32, output: String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = args
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        // sudo'nun hiçbir koşulda terminalde şifre beklememesi için.
        var env = ProcessInfo.processInfo.environment
        env["SUDO_ASKPASS"] = ""
        process.environment = env

        do { try process.run() } catch { return (-1, "") }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return (process.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }

    private static func runPrivileged(_ command: String) -> Bool {
        let source = "do shell script \"\(command)\" with administrator privileges"
        var error: NSDictionary?
        guard let script = NSAppleScript(source: source) else { return false }
        script.executeAndReturnError(&error)
        if let error {
            NSLog("MacGuard: yetkili komut başarısız — %@", String(describing: error))
            return false
        }
        return true
    }

    private func runPrivileged(_ command: String) -> Bool {
        Self.runPrivileged(command)
    }
}
