using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MacGuard.Alarm;

/// <summary>
/// Siren sesini çalışma anında üretir — pakette ses dosyası taşımaya gerek yok.
/// WASAPI ile doğrudan alarm uç noktasına (tercihen dahili hoparlöre) çalar.
/// Bütün ses nesneleri <see cref="AudioThread"/> üzerinde yaşar.
/// </summary>
public sealed class AlarmSiren
{
    private MMDeviceEnumerator? _enumerator;
    private WasapiOut? _output;
    private string? _deviceId;
    private SirenMode _mode = SirenMode.Full;
    private bool _preferSpeakers;
    private volatile bool _wantPlaying;

    public bool IsPlaying => _wantPlaying;

    /// <summary>Sirenin çaldığı uç nokta; ses nöbetçisi aynı aygıtın sesini korur.</summary>
    public string? DeviceId => _deviceId;

    public void Start(SirenMode mode = SirenMode.Full, bool preferSpeakers = false)
    {
        _wantPlaying = true;
        AudioThread.Post(() => StartCore(mode, preferSpeakers, force: false));
    }

    public void Stop()
    {
        _wantPlaying = false;
        AudioThread.Post(StopCore);
    }

    /// <summary>
    /// Uykudan dönüşte ya da aygıt kaybolunca (kulaklık takıldı, sürücü yeniden
    /// başladı) çıkış ölmüş olabilir. Çalması gerekiyorsa baştan kurar.
    /// </summary>
    public void Restart()
    {
        if (!_wantPlaying) return;
        AudioThread.Post(() => StartCore(_mode, _preferSpeakers, force: true));
    }

    /// <summary>Ses nöbetçisi her saniye çağırır: çalması gerekirken durmuşsa yeniden kur.</summary>
    public void EnsurePlaying()
    {
        if (!_wantPlaying) return;
        AudioThread.Post(() =>
        {
            if (_wantPlaying && (_output == null || _output.PlaybackState != PlaybackState.Playing))
                StartCore(_mode, _preferSpeakers, force: true);
        });
    }

    private void StartCore(SirenMode mode, bool preferSpeakers, bool force)
    {
        if (!_wantPlaying) return;
        // Zaten aynı modda çalıyorsa dokunma; mod değişiyorsa yeniden kur.
        if (!force && _output != null && _output.PlaybackState == PlaybackState.Playing
            && mode == _mode && preferSpeakers == _preferSpeakers) return;

        StopCore();
        _mode = mode;
        _preferSpeakers = preferSpeakers;

        _enumerator ??= new MMDeviceEnumerator();
        var device = AudioOutputControl.AlarmDevice(_enumerator, preferSpeakers);
        if (device == null)
        {
            System.Diagnostics.Debug.WriteLine("MacGuard: siren için çıkış aygıtı yok");
            return;
        }

        try
        {
            WaveFormat mix;
            using (var client = device.AudioClient) mix = client.MixFormat;
            var provider = new SirenProvider(new SirenVoice(mode, mix.SampleRate), mix.SampleRate, mix.Channels);
            var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
            output.Init(provider);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();
            _output = output;
            _deviceId = device.ID;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("MacGuard: siren başlatılamadı — " + e.Message);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Aygıt kayboldu ya da sürücü hata verdi; çalması gerekiyorsa yeniden kur.
        if (_wantPlaying && sender == _output) Restart();
    }

    private void StopCore()
    {
        var o = _output;
        _output = null;
        if (o == null) return;
        o.PlaybackStopped -= OnPlaybackStopped;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
    }

    /// <summary>SirenVoice'u WASAPI'nin karıştırıcı biçimine (float, N kanal) döker.</summary>
    private sealed class SirenProvider : ISampleProvider
    {
        private readonly SirenVoice _voice;
        public WaveFormat WaveFormat { get; }

        public SirenProvider(SirenVoice voice, int sampleRate, int channels)
        {
            _voice = voice;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var ch = WaveFormat.Channels;
            var frames = count / ch;
            for (var f = 0; f < frames; f++)
            {
                var v = _voice.Next();
                var i = offset + f * ch;
                for (var c = 0; c < ch; c++) buffer[i + c] = v;
            }
            return frames * ch;
        }
    }
}
