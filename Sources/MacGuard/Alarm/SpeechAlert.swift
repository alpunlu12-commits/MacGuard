import Foundation
import AVFoundation

/// Alarm sırasında sesli uyarı okur. Türkçe ses varsa onu kullanır.
final class SpeechAlert {
    private let synthesizer = AVSpeechSynthesizer()
    private var timer: Timer?

    func startRepeating(_ text: String, every seconds: TimeInterval = 6) {
        stop()
        speak(text)
        let t = Timer.scheduledTimer(withTimeInterval: seconds, repeats: true) { [weak self] _ in
            self?.speak(text)
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        synthesizer.stopSpeaking(at: .immediate)
    }

    private func speak(_ text: String) {
        guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { return }
        let utterance = AVSpeechUtterance(string: text)
        utterance.voice = AVSpeechSynthesisVoice(language: "tr-TR")
            ?? AVSpeechSynthesisVoice(language: "en-US")
        utterance.rate = 0.52
        utterance.volume = 1.0
        synthesizer.speak(utterance)
    }
}
