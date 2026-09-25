// Sireni WAV dosyalarına aktarır.
//
// Uygulamanın çaldığı sesin birebir aynısını üretir: dalga formu
// Sources/MacGuard/Alarm/AlarmSiren.swift içindeki SirenVoice'tan geliyor,
// burada ikinci bir kopyası yok.
//
// Kullanım:
//   swiftc -O -o /tmp/export_siren \
//     Scripts/export_siren.swift Sources/MacGuard/Alarm/AlarmSiren.swift
//   /tmp/export_siren <çıktı-klasörü>

import AVFoundation
import Foundation

@main
struct SirenExporter {
    static let sampleRate: Double = 48_000

    static func main() {
        let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

        // 1) Tam alarm — uygulamadaki gibi yumuşak girişli
        write(mode: .full, seconds: 10, fadeIn: 0.12, edgeFade: 0,
              name: "MacGuard-alarm-sireni.wav", dir: outDir)

        // 2) Tam alarm, döngüye uygun — tam sayıda süpürme döngüsü.
        //    Girişte yumuşatma yok; birleşme noktasında tık olmasın diye
        //    yalnızca 5 ms'lik kenar yumuşatması var.
        let cycles = 7.0
        write(mode: .full, seconds: AlarmSiren.Mode.full.cycleSeconds * cycles,
              fadeIn: 0, edgeFade: 0.005,
              name: "MacGuard-alarm-sireni-DONGU.wav", dir: outDir)

        // 3) Uyarı bipi
        write(mode: .warning, seconds: 10, fadeIn: 0.12, edgeFade: 0,
              name: "MacGuard-uyari-bipi.wav", dir: outDir)
    }

    static func write(mode: AlarmSiren.Mode, seconds: Double, fadeIn: Double,
                      edgeFade: Double, name: String, dir: String) {
        let frames = Int(seconds * sampleRate)
        let voice = SirenVoice(mode: mode, sampleRate: sampleRate, fadeInSeconds: fadeIn)

        guard let procFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                             sampleRate: sampleRate,
                                             channels: 2, interleaved: false),
              let buffer = AVAudioPCMBuffer(pcmFormat: procFormat,
                                            frameCapacity: AVAudioFrameCount(frames)) else {
            print("  ✗ \(name): arabellek oluşturulamadı"); return
        }
        buffer.frameLength = AVAudioFrameCount(frames)
        guard let channels = buffer.floatChannelData else {
            print("  ✗ \(name): kanal verisi yok"); return
        }

        let edgeFrames = Int(edgeFade * sampleRate)
        var peak: Float = 0
        var sumSquares: Double = 0

        for i in 0..<frames {
            var v = voice.next()
            // Döngü dosyasında baş ve son birkaç milisaniyeyi yumuşat.
            if edgeFrames > 0 {
                if i < edgeFrames { v *= Float(Double(i) / Double(edgeFrames)) }
                let tail = frames - 1 - i
                if tail < edgeFrames { v *= Float(Double(tail) / Double(edgeFrames)) }
            }
            channels[0][i] = v
            channels[1][i] = v
            peak = max(peak, abs(v))
            sumSquares += Double(v) * Double(v)
        }

        let url = URL(fileURLWithPath: dir).appendingPathComponent(name)
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: sampleRate,
            AVNumberOfChannelsKey: 2,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsNonInterleaved: false
        ]
        do {
            let file = try AVAudioFile(forWriting: url, settings: settings)
            try file.write(from: buffer)
            let rms = (sumSquares / Double(frames)).squareRoot()
            let kb = ((try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int) ?? 0) ?? 0
            print(String(format: "  ✓ %@  %.1f sn, %d KB, tepe %.2f, RMS %.3f",
                         name, seconds, kb / 1024, peak, rms))
        } catch {
            print("  ✗ \(name): \(error.localizedDescription)")
        }
    }
}
