import Foundation
import ServiceManagement

/// "Oturum açılınca başlat" ayarı. macOS'un kendi Giriş Öğeleri listesine kaydolur.
enum LoginItem {
    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled
    }

    /// true döndürürse istenen duruma geçildi.
    @discardableResult
    static func setEnabled(_ on: Bool) -> Bool {
        do {
            if on {
                guard SMAppService.mainApp.status != .enabled else { return true }
                try SMAppService.mainApp.register()
            } else {
                guard SMAppService.mainApp.status == .enabled else { return true }
                try SMAppService.mainApp.unregister()
            }
            return true
        } catch {
            NSLog("MacGuard: giriş öğesi ayarlanamadı — %@", error.localizedDescription)
            return false
        }
    }

    /// Kullanıcı ayarı reddettiyse macOS'un Giriş Öğeleri panelini açar.
    static func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
