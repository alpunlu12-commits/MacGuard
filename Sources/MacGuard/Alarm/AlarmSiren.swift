import Foundation
import AVFoundation

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
    }

    /// Ses iş parçacığında okunan/yazılan durum. Sadece basit sayılar tutar.
    private final class State {
        var phase: Double = 0
        var sweep: Double = 0
        var pulse: Double = 0
        var sampleRate: Double = 48_000
        var amplitude: Double = 0     // yumuşak giriş için 0'dan hedefe tırmanır
        var target: Double = 0
        var peak: Double = 0.55
        var sweepSeconds: Double = 0.7
        var baseFrequency: Double = 600
        var frequencySpan: Double = 900
        var pulseSeconds: Double = 0
    }

    private let engine = AVAudioEngine()
    private let state = State()
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

        let sampleRate = engine.outputNode.outputFormat(forBus: 0).sampleRate
        state.sampleRate = sampleRate > 0 ? sampleRate : 48_000
        state.phase = 0
        state.sweep = 0
        state.pulse = 0
        state.amplitude = 0
        state.target = 1
        state.peak = mode.amplitude
        state.sweepSeconds = mode.sweepSeconds
        state.baseFrequency = mode.baseFrequency
        state.frequencySpan = mode.frequencySpan
        state.pulseSeconds = mode.pulseSeconds

        guard let format = AVAudioFormat(standardFormatWithSampleRate: state.sampleRate, channels: 2) else { return }
        let st = state

        let node = AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList -> OSStatus in
            let ablPointer = UnsafeMutableAudioBufferListPointer(audioBufferList)
            let dt = 1.0 / st.sampleRate

            for frame in 0..<Int(frameCount) {
                // Üçgen süpürme: yukarı-aşağı inleyen klasik siren.
                st.sweep += dt / st.sweepSeconds
                if st.sweep >= 2 { st.sweep -= 2 }
                let ramp = st.sweep <= 1 ? st.sweep : (2 - st.sweep)
                let freq = st.baseFrequency + ramp * st.frequencySpan

                st.phase += 2 * Double.pi * freq * dt
                if st.phase > 2 * Double.pi { st.phase -= 2 * Double.pi }

                // tanh ile kırpılmış sinüs: saf sinüsten çok daha delici.
                let core = tanh(sin(st.phase) * 3.0)
                let harmonic = sin(st.phase * 1.5) * 0.30

                // Uyarı modunda bip-bip: yarım periyot sesli, yarım periyot sessiz.
                var gate = 1.0
                if st.pulseSeconds > 0 {
                    st.pulse += dt / st.pulseSeconds
                    if st.pulse >= 1 { st.pulse -= 1 }
                    gate = st.pulse < 0.55 ? 1.0 : 0.0
                }

                // 120 ms'lik yumuşak giriş; hoparlörde "pat" sesi olmasın.
                st.amplitude += (st.target - st.amplitude) * (dt / 0.12)
                let value = Float((core + harmonic) * st.peak * st.amplitude * gate)

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
