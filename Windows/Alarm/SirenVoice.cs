using System;

namespace MacGuard.Alarm;

public enum SirenMode
{
    /// <summary>Tam alarm: sürekli, sert, 600–1500 Hz arası inleyen siren.</summary>
    Full,
    /// <summary>Uyarı: kesik kesik biplenen, daha alçak ve daha sakin ton.</summary>
    Warning,
}

public static class SirenModeExtensions
{
    public static double Amplitude(this SirenMode m) => m == SirenMode.Full ? 0.55 : 0.26;
    public static double SweepSeconds(this SirenMode m) => m == SirenMode.Full ? 0.7 : 1.4;
    public static double BaseFrequency(this SirenMode m) => m == SirenMode.Full ? 600 : 460;
    public static double FrequencySpan(this SirenMode m) => m == SirenMode.Full ? 900 : 280;
    /// <summary>0 = kesintisiz. Uyarıda 1 saniyelik aç/kapa döngüsü.</summary>
    public static double PulseSeconds(this SirenMode m) => m == SirenMode.Full ? 0 : 1.0;
}

/// <summary>
/// Sirenin dalga formunu üreten çekirdek. macOS sürümündeki SirenVoice ile
/// birebir aynı matematik: üçgen süpürme, tanh ile kırpılmış sinüs + 1,5 kat
/// harmonik, uyarı modunda kapı, yumuşak giriş zarfı.
///
/// Ses iş parçacığında çalışır: bellek ayırmaz, kilit almaz, yalnızca aritmetik.
/// </summary>
public sealed class SirenVoice
{
    private readonly double _sampleRate;
    private readonly double _peak;
    private readonly double _sweepSeconds;
    private readonly double _baseFrequency;
    private readonly double _frequencySpan;
    private readonly double _pulseSeconds;
    private readonly double _fadeInSeconds;

    private double _phase;
    private double _sweep;
    private double _pulse;
    private double _envelope;   // yumuşak giriş için 0'dan 1'e tırmanır

    public SirenVoice(SirenMode mode, double sampleRate, double fadeInSeconds = 0.12)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 48_000;
        _peak = mode.Amplitude();
        _sweepSeconds = mode.SweepSeconds();
        _baseFrequency = mode.BaseFrequency();
        _frequencySpan = mode.FrequencySpan();
        _pulseSeconds = mode.PulseSeconds();
        _fadeInSeconds = fadeInSeconds;
        _envelope = fadeInSeconds <= 0 ? 1 : 0;
    }

    /// <summary>Sıradaki örnek.</summary>
    public float Next()
    {
        var dt = 1.0 / _sampleRate;

        // Üçgen süpürme: yukarı-aşağı inleyen klasik siren.
        _sweep += dt / _sweepSeconds;
        if (_sweep >= 2) _sweep -= 2;
        var ramp = _sweep <= 1 ? _sweep : (2 - _sweep);
        var freq = _baseFrequency + ramp * _frequencySpan;

        _phase += 2 * Math.PI * freq * dt;
        if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;

        // tanh ile kırpılmış sinüs: saf sinüsten çok daha delici.
        var core = Math.Tanh(Math.Sin(_phase) * 3.0);
        var harmonic = Math.Sin(_phase * 1.5) * 0.30;

        // Uyarı modunda bip-bip: periyodun bir bölümü sesli, kalanı sessiz.
        var gate = 1.0;
        if (_pulseSeconds > 0)
        {
            _pulse += dt / _pulseSeconds;
            if (_pulse >= 1) _pulse -= 1;
            gate = _pulse < 0.55 ? 1.0 : 0.0;
        }

        if (_fadeInSeconds > 0) _envelope += (1 - _envelope) * (dt / _fadeInSeconds);

        return (float)((core + harmonic) * _peak * _envelope * gate);
    }
}
