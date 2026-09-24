import SwiftUI
import AppKit

@main
struct MacGuardApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @ObservedObject private var engine = GuardEngine.shared

    var body: some Scene {
        WindowGroup("MacGuard") {
            DashboardView()
        }
        .windowResizability(.contentMinSize)
        .commands {
            // "Yeni pencere" bu uygulamada anlamsız.
            CommandGroup(replacing: .newItem) {}
        }

        MenuBarExtra {
            MenuBarContent()
        } label: {
            Image(systemName: menuBarSymbol)
        }
    }

    private var menuBarSymbol: String {
        switch engine.state {
        case .disarmed: return "shield.slash"
        case .arming:   return "hourglass"
        case .armed:    return "checkmark.shield.fill"
        case .warning:  return "exclamationmark.circle.fill"
        case .alarming: return "exclamationmark.triangle.fill"
        }
    }
}

struct MenuBarContent: View {
    @ObservedObject private var engine = GuardEngine.shared

    var body: some View {
        switch engine.state {
        case .disarmed:
            Button("Korumayı başlat") { GuardEngine.shared.arm() }
        case .arming(let remaining):
            Text("Devreye giriyor — \(remaining) sn")
            Button("İptal et") { GuardEngine.shared.cancelArming() }
        case .armed:
            Text("Koruma aktif")
            Button("Alarmı test et") { GuardEngine.shared.testAlarm() }
        case .warning(_, let remaining):
            Text("Uyarı — \(remaining) sn içinde PIN gir")
        case .alarming:
            Text("ALARM ÇALIYOR")
        }

        Divider()

        Button("MacGuard penceresini aç") {
            NSApp.activate(ignoringOtherApps: true)
            NSApp.windows.first { $0 is OverlayWindow == false }?.makeKeyAndOrderFront(nil)
        }

        if !engine.state.isProtecting {
            Button("Çık") { NSApp.terminate(nil) }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        // Önceki çalıştırma çökerek uyku engelini açık bıraktıysa geri al,
        // yoksa Mac bir daha hiç uyumaz. Alt süreç çalıştırdığı için arka planda.
        GuardEngine.shared.checkSleepLeftover()
        EventLog.shared.log("MacGuard başlatıldı", icon: "power", severity: .info)
    }

    /// Koruma açıkken uygulama kapatılamaz; alarmı susturmanın tek yolu PIN.
    ///
    /// Burada bir NSAlert AÇILMIYOR. Alarm perdesi `.screenSaver` seviyesinde,
    /// uyarı kutusu ise normal seviyede: kutu perdenin ARKASINDA açılıyor,
    /// uygulama modal döngüye giriyor ve klavye görünmeyen kutuya gidiyordu.
    /// Cmd+Q'ya arka arkaya basınca PIN'in girilememesinin sebebi buydu.
    /// Bunun yerine sessizce reddedip perdeyi öne alıyoruz.
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard GuardEngine.shared.blocksTermination else { return .terminateNow }

        EventLog.shared.log("Kapatma denemesi engellendi",
                            detail: "Koruma açıkken MacGuard kapatılamaz",
                            icon: "xmark.shield.fill", severity: .warn)
        OverlayGuardian.shared.reassert()
        return .terminateCancel
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationWillTerminate(_ notification: Notification) {
        // Sistem ayarlarını (uyku engeli, ses aygıtı) arkamızda bırakmayalım.
        GuardEngine.shared.prepareForShutdown()
    }
}
