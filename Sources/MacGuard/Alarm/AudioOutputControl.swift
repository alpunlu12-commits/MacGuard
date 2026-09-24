import Foundation
import CoreAudio
import AppKit

/// Sistem ses seviyesini ve çıkış aygıtını yönetir.
/// Alarm anında ses kısıksa ya da kulaklık takılıysa alarm duyulmaz — bunu engeller.
enum AudioOutputControl {

    // MARK: Ses seviyesi (AppleScript; sistem genelinde en güvenilir yol)

    /// 0...100 arası mevcut çıkış seviyesi.
    static func currentVolume() -> Int? {
        runAppleScript("output volume of (get volume settings)").flatMap(Int.init)
    }

    static func isMuted() -> Bool {
        runAppleScript("output muted of (get volume settings)") == "true"
    }

    static func setVolume(_ percent: Int, muted: Bool = false) {
        let clamped = max(0, min(100, percent))
        _ = runAppleScript("set volume output volume \(clamped) \(muted ? "with" : "without") output muted")
    }

    @discardableResult
    private static func runAppleScript(_ source: String) -> String? {
        var error: NSDictionary?
        guard let script = NSAppleScript(source: source) else { return nil }
        let result = script.executeAndReturnError(&error)
        if error != nil { return nil }
        return result.stringValue
    }

    // MARK: Çıkış aygıtı

    private static func defaultOutputDeviceID() -> AudioDeviceID? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var deviceID = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                                &address, 0, nil, &size, &deviceID)
        return status == noErr ? deviceID : nil
    }

    private static func setDefaultOutputDevice(_ id: AudioDeviceID) {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var value = id
        AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                   &address, 0, nil,
                                   UInt32(MemoryLayout<AudioDeviceID>.size), &value)
    }

    private static func allDeviceIDs() -> [AudioDeviceID] {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject),
                                             &address, 0, nil, &size) == noErr else { return [] }
        let count = Int(size) / MemoryLayout<AudioDeviceID>.size
        guard count > 0 else { return [] }
        var ids = [AudioDeviceID](repeating: 0, count: count)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject),
                                         &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids
    }

    private static func transportType(_ id: AudioDeviceID) -> UInt32 {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyTransportType,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        AudioObjectGetPropertyData(id, &address, 0, nil, &size, &value)
        return value
    }

    private static func hasOutputStreams(_ id: AudioDeviceID) -> Bool {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreams,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &address, 0, nil, &size) == noErr else { return false }
        return size > 0
    }

    /// Dahili hoparlörü varsayılan çıkışa alır; eskisini geri yüklemek için döndürür.
    @discardableResult
    static func switchToBuiltInSpeakers() -> AudioDeviceID? {
        let previous = defaultOutputDeviceID()
        guard let builtIn = allDeviceIDs().first(where: {
            transportType($0) == kAudioDeviceTransportTypeBuiltIn && hasOutputStreams($0)
        }) else { return previous }

        if builtIn != previous { setDefaultOutputDevice(builtIn) }
        return previous
    }


    // MARK: Ses seviyesi (CoreAudio)
    //
    // Alarm sırasında saniyede bir yoklanacağı için AppleScript kullanılamaz:
    // her çağrı ana iş parçacığında onlarca milisaniye yerdi. CoreAudio hem
    // hızlı hem de iş parçacığı güvenli, böylece nöbetçi arka planda dönebilir.

    /// Bir aygıtın verilen kapsam/elemandaki ses seviyesini okur.
    private static func volumeScalar(_ id: AudioDeviceID, element: AudioObjectPropertyElement) -> Float? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyVolumeScalar,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: element)
        guard AudioObjectHasProperty(id, &address) else { return nil }
        var value: Float32 = 0
        var size = UInt32(MemoryLayout<Float32>.size)
        guard AudioObjectGetPropertyData(id, &address, 0, nil, &size, &value) == noErr else { return nil }
        return value
    }

    private static func setVolumeScalar(_ id: AudioDeviceID, element: AudioObjectPropertyElement, _ value: Float) -> Bool {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyVolumeScalar,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: element)
        guard AudioObjectHasProperty(id, &address) else { return false }
        var settable: DarwinBoolean = false
        guard AudioObjectIsPropertySettable(id, &address, &settable) == noErr, settable.boolValue else { return false }
        var v = Float32(max(0, min(1, value)))
        return AudioObjectSetPropertyData(id, &address, 0, nil,
                                          UInt32(MemoryLayout<Float32>.size), &v) == noErr
    }

    /// Stereo çiftin kanal numaraları; ana eleman ses seviyesini desteklemeyen
    /// aygıtlarda kanal kanal ayarlamak gerekiyor.
    private static func stereoChannels(_ id: AudioDeviceID) -> [AudioObjectPropertyElement] {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyPreferredChannelsForStereo,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain)
        var channels: (UInt32, UInt32) = (1, 2)
        var size = UInt32(MemoryLayout<(UInt32, UInt32)>.size)
        if AudioObjectGetPropertyData(id, &address, 0, nil, &size, &channels) != noErr {
            return [1, 2]
        }
        return [channels.0, channels.1]
    }

    /// Varsayılan çıkışın ses seviyesi (0...1). Okunamazsa nil.
    static func outputVolume() -> Float? {
        guard let device = defaultOutputDeviceID() else { return nil }
        if let main = volumeScalar(device, element: kAudioObjectPropertyElementMain) { return main }
        let values = stereoChannels(device).compactMap { volumeScalar(device, element: $0) }
        guard !values.isEmpty else { return nil }
        return values.reduce(0, +) / Float(values.count)
    }

    /// Varsayılan çıkışın ses seviyesini ayarlar ve sessizi kaldırır.
    @discardableResult
    static func setOutputVolume(_ value: Float) -> Bool {
        guard let device = defaultOutputDeviceID() else { return false }
        setMuted(false)
        if setVolumeScalar(device, element: kAudioObjectPropertyElementMain, value) { return true }
        var anyOK = false
        for channel in stereoChannels(device) {
            if setVolumeScalar(device, element: channel, value) { anyOK = true }
        }
        if anyOK { return true }
        // Son çare: AppleScript. Ana iş parçacığında çalışmak zorunda.
        let percent = Int((value * 100).rounded())
        if Thread.isMainThread {
            setVolume(percent, muted: false)
        } else {
            DispatchQueue.main.async { setVolume(percent, muted: false) }
        }
        return true
    }

    static func isOutputMuted() -> Bool {
        guard let device = defaultOutputDeviceID() else { return false }
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyMute,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain)
        guard AudioObjectHasProperty(device, &address) else { return isMuted() }
        var value: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        guard AudioObjectGetPropertyData(device, &address, 0, nil, &size, &value) == noErr else { return false }
        return value != 0
    }

    static func setMuted(_ muted: Bool) {
        guard let device = defaultOutputDeviceID() else { return }
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyMute,
            mScope: kAudioObjectPropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain)
        guard AudioObjectHasProperty(device, &address) else { return }
        var settable: DarwinBoolean = false
        guard AudioObjectIsPropertySettable(device, &address, &settable) == noErr, settable.boolValue else { return }
        var value: UInt32 = muted ? 1 : 0
        AudioObjectSetPropertyData(device, &address, 0, nil,
                                   UInt32(MemoryLayout<UInt32>.size), &value)
    }

    /// Çıkışı dahili hoparlöre sabitler ama "önceki aygıt" kaydını bozmaz.
    /// Alarm sırasında saniyede bir çağrıldığı için ayrı bir sürüm gerekiyor:
    /// `switchToBuiltInSpeakers` her çağrıldığında eski aygıtı döndürdüğü için
    /// tekrar tekrar çağrılırsa gerçek kullanıcı aygıtı kaybolurdu.
    static func ensureBuiltInSpeakers() {
        guard let builtIn = allDeviceIDs().first(where: {
            transportType($0) == kAudioDeviceTransportTypeBuiltIn && hasOutputStreams($0)
        }) else { return }
        guard defaultOutputDeviceID() != builtIn else { return }
        setDefaultOutputDevice(builtIn)
    }

    static func restoreOutputDevice(_ id: AudioDeviceID?) {
        guard let id, id != 0 else { return }
        setDefaultOutputDevice(id)
    }
}
