import Foundation
import AVFoundation

/// Sirenin dalga formunu üreten çekirdek.
///
/// Hem gerçek zamanlı ses düğümü hem de dosyaya aktarma (`Scripts/export_siren.swift`)
/// bunu kullanır. Matematiğin iki ayrı kopyası olsaydı zamanla birbirinden
/// sapar, dışa aktarılan ses uygulamanın çaldığından farklı olurdu.
///
/// Ses iş parçacığında çalışır: bellek ayırmaz, kilit almaz, yalnızca aritmetik.
final class SirenVoice {
    private let sampleRate: Double
    private let peak: Double
    private let sweepSeconds: Double
    private let baseFrequency: Double
    private let frequencySpan: Double
    private let pulseSeconds: Double
    private let fadeInSeconds: Double

    private var phase: Double = 0
    private var sweep: Double = 0
    private var pulse: Double = 0
    private var envelope: Double = 0   // yumuşak giriş için 0'dan 1'e tırmanır

    init(mode: AlarmSiren.Mode, sampleRate: Double, fadeInSeconds: Double = 0.12) {
        self.sampleRate = sampleRate > 0 ? sampleRate : 48_000
        self.peak = mode.amplitude
        self.sweepSeconds = mode.sweepSeconds
        self.baseFrequency = mode.baseFrequency
        self.frequencySpan = mode.frequencySpan
        self.pulseSeconds = mode.pulseSeconds
        self.fadeInSeconds = fadeInSeconds
        // Giriş yumuşatması istenmiyorsa zarf baştan açık başlasın
        // (döngüye uygun dosya üretirken gerekiyor).
        self.envelope = fadeInSeconds <= 0 ? 1 : 0
    }

    /// Sıradaki örnek.
    func next() -> Float {
        let dt = 1.0 / sampleRate

        // Üçgen süpürme: yukarı-aşağı inleyen klasik siren.
        sweep += dt / sweepSeconds
        if sweep >= 2 { sweep -= 2 }
        let ramp = sweep <= 1 ? sweep : (2 - sweep)
        let freq = baseFrequency + ramp * frequencySpan

        phase += 2 * Double.pi * freq * dt
        if phase > 2 * Double.pi { phase -= 2 * Double.pi }

        // tanh ile kırpılmış sinüs: saf sinüsten çok daha delici.
        let core = tanh(sin(phase) * 3.0)
        let harmonic = sin(phase * 1.5) * 0.30

        // Uyarı modunda bip-bip: periyodun bir bölümü sesli, kalanı sessiz.
        var gate = 1.0
        if pulseSeconds > 0 {
            pulse += dt / pulseSeconds
            if pulse >= 1 { pulse -= 1 }
            gate = pulse < 0.55 ? 1.0 : 0.0
        }

        if fadeInSeconds > 0 {
            envelope += (1 - envelope) * (dt / fadeInSeconds)
        }

        return Float((core + harmonic) * peak * envelope * gate)
    }
}

/// Siren sesini çalışma anında üretir — pakette ses dosyası taşımaya gerek yok.
final class AlarmSiren {

    enum Mode {
        /// Tam alarm: sürekli, sert, 600–1500 Hz arası inleyen siren.
        case full
        /// Uyarı: kesik kesik biplenen, daha alçak ve daha sakin ton.
        /// "PIN gir yoksa alarm çalacak" aşamasında kullanılır.
        case warning

        var amplitude: Double { self == .full ? 0.55 : 0.26 }
        var sweepSeconds: Double { self == .full ? 0.7 : 1.4 }
        var baseFrequency: Double { self == .full ? 600 : 460 }
        var frequencySpan: Double { self == .full ? 900 : 280 }
        /// 0 = kesintisiz. Uyarıda 1 saniyelik aç/kapa döngüsü.
        var pulseSeconds: Double { self == .full ? 0 : 1.0 }

        /// Bir tam süpürme döngüsünün süresi — döngüye uygun dosya üretirken gerekiyor.
        var cycleSeconds: Double { sweepSeconds * 2 }
    }

    private let engine = AVAudioEngine()
    private var sourceNode: AVAudioSourceNode?
    private(set) var isPlaying = false
    private(set) var mode: Mode = .full

    func start(mode: Mode = .full) {
        // Zaten aynı modda çalıyorsa dokunma; mod değişiyorsa yeniden kur.
        if isPlaying {
            guard mode != self.mode else { return }
            stop()
        }
        self.mode = mode

        let outputRate = engine.outputNode.outputFormat(forBus: 0).sampleRate
        let sampleRate = outputRate > 0 ? outputRate : 48_000
        guard let format = AVAudioFormat(standardFormatWithSampleRate: sampleRate, channels: 2) else { return }

        let voice = SirenVoice(mode: mode, sampleRate: sampleRate)

        let node = AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList -> OSStatus in
            let ablPointer = UnsafeMutableAudioBufferListPointer(audioBufferList)
            for frame in 0..<Int(frameCount) {
                let value = voice.next()
                for buffer in ablPointer {
                    guard let data = buffer.mData?.assumingMemoryBound(to: Float.self) else { continue }
                    data[frame] = value
                }
            }
            return noErr
        }

        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        engine.mainMixerNode.outputVolume = 1.0
        sourceNode = node

        do {
            engine.prepare()
            try engine.start()
            isPlaying = true
        } catch {
            engine.detach(node)
            sourceNode = nil
            NSLog("MacGuard: siren başlatılamadı — %@", error.localizedDescription)
        }
    }

    func stop() {
        guard isPlaying else { return }
        isPlaying = false
        engine.stop()
        if let node = sourceNode {
            engine.detach(node)
            sourceNode = nil
        }
    }
}
