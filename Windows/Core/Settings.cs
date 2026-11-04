using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MacGuard.Core;

/// <summary>
/// Kalıcı ayarlar. %APPDATA%\MacGuard\settings.json dosyasına yazar, arayüze yayınlar.
/// Anahtar adları ve varsayılanlar macOS sürümüyle aynıdır.
/// </summary>
public sealed class Settings : ObservableObject
{
    public static Settings Shared { get; } = new();

    private readonly string _path = AppPaths.File("settings.json");
    private readonly JsonObject _d;

    public const string DefaultLockScreenText =
        "Bu bilgisayar MacGuard ile korunuyor.\n\n"
        + "• Yerinden oynatmak, kapağı kapatmak ya da şarjı çıkarmak alarmı tetikler\n"
        + "• Alarm yalnızca sahibinin PIN'i ile susturulabilir\n"
        + "• Tetiklendiği anda kamera fotoğraf çeker ve sahibine bildirim gider\n\n"
        + "Lütfen dokunmayın.";

    public const string DefaultWarningText = "Dikkat! Bu bilgisayar korumalıdır. Lütfen uzaklaşın.";

    private Settings()
    {
        try
        {
            _d = File.Exists(_path) ? (JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new()) : new();
        }
        catch
        {
            _d = new();
        }

        if (_d["enabledTriggers"] is JsonArray arr)
        {
            _enabled = new HashSet<TriggerKind>(arr
                .Select(n => n?.GetValue<string>() is { } s ? TriggerKinds.FromRaw(s) : null)
                .Where(k => k.HasValue).Select(k => k!.Value));
        }
        else
        {
            _enabled = new HashSet<TriggerKind>
            {
                TriggerKind.Motion, TriggerKind.Proximity, TriggerKind.Power, TriggerKind.Clamshell, TriggerKind.Usb,
            };
        }
    }

    // MARK: Hangi sensörler açık

    private HashSet<TriggerKind> _enabled;

    public bool IsEnabled(TriggerKind kind) => _enabled.Contains(kind);

    public void SetEnabled(TriggerKind kind, bool on)
    {
        var changed = on ? _enabled.Add(kind) : _enabled.Remove(kind);
        if (!changed) return;
        _d["enabledTriggers"] = new JsonArray(_enabled.Select(k => (JsonNode)JsonValue.Create(k.RawValue())!).ToArray());
        Persist();
        OnPropertyChanged(nameof(EnabledTriggers));
    }

    /// <summary>Değişiklik bildirimi için; içerik <see cref="IsEnabled"/> ile okunur.</summary>
    public IReadOnlyCollection<TriggerKind> EnabledTriggers => _enabled;

    // MARK: Hassasiyet

    /// <summary>Kamera hareket eşiği. Düşük = hassas. 0.005 ... 0.12</summary>
    public double MotionSensitivity { get => Get(0.030); set => Put(value); }

    /// <summary>
    /// Değişimin kareye yayılma oranı eşiği. Bilgisayar oynadığında karenin
    /// tamamı değişir; önünde biri kıpırdadığında yalnızca bir bölgesi.
    /// Yüksek değer = sadece gerçek taşınmaya tepki. 0.20 ... 0.90
    /// </summary>
    public double MotionCoverage { get => Get(0.50); set => Put(value); }

    /// <summary>Yüzün kadrajda kapladığı yükseklik oranı eşiği. 0.20 ... 0.90</summary>
    public double ProximityThreshold { get => Get(0.55); set => Put(value); }

    // MARK: Zamanlama

    /// <summary>Koruma başlatıldıktan sonra sensörlerin devreye girmesi için beklenen saniye.</summary>
    public int ArmDelay { get => Get(8); set => Put(value); }

    /// <summary>Tetikten alarma kadar tanınan sessiz süre (saniye). PIN girilirse alarm çalmaz.</summary>
    public int GraceSeconds { get => Get(0); set => Put(value); }

    // MARK: Alarm davranışı

    public bool ForceMaxVolume { get => Get(true); set => Put(value); }

    /// <summary>Alarm sırasında sistem sesinin ayarlanacağı seviye (0...1). Test ederken düşürülür.</summary>
    public double AlarmVolume { get => Get(1.0); set => Put(value); }

    public bool ForceBuiltInSpeakers { get => Get(true); set => Put(value); }
    public bool SpeakWarning { get => Get(true); set => Put(value); }
    public string WarningText { get => Get(DefaultWarningText); set => Put(value); }
    public bool CaptureIntruderPhoto { get => Get(true); set => Put(value); }

    /// <summary>Kapak kapansa bile uyumayı engelle (güç planının kapak eylemini değiştirir).</summary>
    public bool BlockClamshellSleep { get => Get(false); set => Put(value); }

    // MARK: Koruma ekranı

    public bool ShowLockScreen { get => Get(true); set => Put(value); }
    public string LockScreenText { get => Get(DefaultLockScreenText); set => Put(value); }

    // MARK: Telefona bildirim (ntfy.sh)

    public bool PushEnabled { get => Get(false); set => Put(value); }
    public string PushTopic { get => Get(""); set => Put(value); }
    public string PushServer { get => Get("https://ntfy.sh"); set => Put(value); }

    // MARK: - Depo

    private static string Key(string member) => char.ToLowerInvariant(member[0]) + member[1..];

    private T Get<T>(T fallback, [CallerMemberName] string member = "")
    {
        try
        {
            if (_d[Key(member)] is JsonValue v && v.TryGetValue<T>(out var value)) return value;
            // Sayılar JSON'da double/int karışık gelebilir.
            if (_d[Key(member)] is JsonValue n)
            {
                if (typeof(T) == typeof(int) && n.TryGetValue<double>(out var dd)) return (T)(object)(int)Math.Round(dd);
                if (typeof(T) == typeof(double) && n.TryGetValue<int>(out var ii)) return (T)(object)(double)ii;
            }
        }
        catch { }
        return fallback;
    }

    private void Put<T>(T value, [CallerMemberName] string member = "")
    {
        var key = Key(member);
        var current = _d[key]?.ToJsonString();
        var next = JsonValue.Create(value);
        if (current == next?.ToJsonString()) return;
        _d[key] = next;
        Persist();
        OnPropertyChanged(member);
    }

    private void Persist()
    {
        try
        {
            var json = _d.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            AppPaths.WriteAtomic(_path, System.Text.Encoding.UTF8.GetBytes(json));
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("MacGuard: ayarlar yazılamadı — " + e.Message);
        }
    }
}
