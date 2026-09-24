import Foundation
import UserNotifications

/// macOS bildirim merkezine kısa bilgi düşürür. İzin yoksa sessizce atlar.
enum NotificationBanner {
    private static var authorized = false

    static func requestPermission() {
        guard Bundle.main.bundleIdentifier != nil else { return }
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { granted, _ in
            authorized = granted
        }
    }

    static func show(title: String, body: String) {
        guard authorized, Bundle.main.bundleIdentifier != nil else { return }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }
}
