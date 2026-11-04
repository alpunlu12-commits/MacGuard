using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.SpeechSynthesis;

namespace MacGuard.Alarm;

/// <summary>
/// Alarm sırasında sesli uyarı okur. Türkçe ses (Microsoft Tolga) varsa onu,
/// yoksa İngilizce bir ses kullanır. Konuşma sirenle aynı uç noktaya çalınır.
/// </summary>
public sealed class SpeechAlert
{
    private Timer? _timer;
    private int _generation;
    private string? _cachedText;
    private byte[]? _cachedWav;

    private MMDeviceEnumerator? _enumerator;
    private WasapiOut? _output;

    public void StartRepeating(string text, bool preferSpeakers, double everySeconds = 6)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(text)) return;
        var gen = Interlocked.Increment(ref _generation);
        _timer = new Timer(_ => _ = SpeakAsync(text, preferSpeakers, gen), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(everySeconds));
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _timer?.Dispose();
        _timer = null;
        AudioThread.Post(StopOutput);
    }

    private async Task SpeakAsync(string text, bool preferSpeakers, int gen)
    {
        try
        {
            var wav = await SynthesizeAsync(text);
            if (wav == null || gen != Volatile.Read(ref _generation)) return;
            AudioThread.Post(() =>
            {
                if (gen != Volatile.Read(ref _generation)) return;
                Play(wav, preferSpeakers);
            });
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("MacGuard: sesli uyarı okunamadı — " + e.Message);
        }
    }

    private async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (_cachedText == text && _cachedWav != null) return _cachedWav;

        using var synth = new SpeechSynthesizer();
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
                    ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (voice != null) synth.Voice = voice;
        synth.Options.AudioVolume = 1.0;
        synth.Options.SpeakingRate = 1.0;

        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        using var managed = stream.AsStreamForRead();
        using var ms = new MemoryStream();
        await managed.CopyToAsync(ms);
        _cachedText = text;
        _cachedWav = ms.ToArray();
        return _cachedWav;
    }

    /// <summary>Ses iş parçacığında çalışır.</summary>
    private void Play(byte[] wav, bool preferSpeakers)
    {
        StopOutput();
        _enumerator ??= new MMDeviceEnumerator();
        var device = AudioOutputControl.AlarmDevice(_enumerator, preferSpeakers);
        if (device == null) return;

        WaveFormat mix;
        using (var client = device.AudioClient) mix = client.MixFormat;
        ISampleProvider source = new WaveFileReader(new MemoryStream(wav)).ToSampleProvider();
        if (source.WaveFormat.Channels > 1) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.SampleRate != mix.SampleRate)
            source = new WdlResamplingSampleProvider(source, mix.SampleRate);
        var provider = new MonoToChannels(source, mix.Channels);

        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 80);
        output.Init(provider);
        output.Play();
        _output = output;
    }

    private void StopOutput()
    {
        var o = _output;
        _output = null;
        if (o == null) return;
        try { o.Stop(); } catch { }
        try { o.Dispose(); } catch { }
    }

    /// <summary>Tek kanallı sesi aygıtın kanal sayısına çoğaltır.</summary>
    private sealed class MonoToChannels : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float[] _buffer = Array.Empty<float>();
        public WaveFormat WaveFormat { get; }

        public MonoToChannels(ISampleProvider source, int channels)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, Math.Max(1, channels));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var ch = WaveFormat.Channels;
            var frames = count / ch;
            if (_buffer.Length < frames) _buffer = new float[frames];
            var read = _source.Read(_buffer, 0, frames);
            for (var f = 0; f < read; f++)
                for (var c = 0; c < ch; c++) buffer[offset + f * ch + c] = _buffer[f];
            return read * ch;
        }
    }
}
