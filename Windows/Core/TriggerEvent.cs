using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MacGuard.Core;

/// <summary>Bir tetikleyicinin türü. Her sensör bunlardan birini üretir.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerKind>))]
public enum TriggerKind
{
    Motion,      // Kamera görüntüsü topluca kaydı -> bilgisayar hareket etti
    Proximity,   // Yüz kadraja fazla yaklaştı
    Power,       // Şarj kablosu çıkarıldı
    Clamshell,   // Kapak kapatıldı
    Usb,         // USB aygıt takıldı / çıkarıldı
    Input,       // Klavye / touchpad'e dokunuldu
    Display,     // Harici ekran bağlantısı değişti
}

public static class TriggerKinds
{
    public static readonly IReadOnlyList<TriggerKind> All = (TriggerKind[])Enum.GetValues(typeof(TriggerKind));

    /// <summary>Ayar dosyasında macOS sürümüyle aynı anahtarlar kullanılır.</summary>
    public static string RawValue(this TriggerKind k) => k switch
    {
        TriggerKind.Motion => "motion",
        TriggerKind.Proximity => "proximity",
        TriggerKind.Power => "power",
        TriggerKind.Clamshell => "clamshell",
        TriggerKind.Usb => "usb",
        TriggerKind.Input => "input",
        TriggerKind.Display => "display",
        _ => k.ToString().ToLowerInvariant(),
    };

    public static TriggerKind? FromRaw(string raw)
    {
        foreach (var k in All)
            if (k.RawValue() == raw) return k;
        return null;
    }

    public static string Title(this TriggerKind k) => k switch
    {
        TriggerKind.Motion => "Hareket",
        TriggerKind.Proximity => "Yakınlık",
        TriggerKind.Power => "Şarj Kablosu",
        TriggerKind.Clamshell => "Ekran Kapağı",
        TriggerKind.Usb => "USB Aygıt",
        TriggerKind.Input => "Klavye / Touchpad",
        TriggerKind.Display => "Harici Ekran",
        _ => k.ToString(),
    };

    public static string Detail(this TriggerKind k) => k switch
    {
        TriggerKind.Motion => "Kamera görüntüsü topluca kayarsa bilgisayar yerinden oynatılmış demektir.",
        TriggerKind.Proximity => "Biri kameraya haddinden fazla yaklaşırsa uyarır.",
        TriggerKind.Power => "Şarj adaptörü prizden ya da bilgisayardan çıkarılırsa.",
        TriggerKind.Clamshell => "Ekran kapağı kapatılırsa.",
        TriggerKind.Usb => "Bir USB bellek, kablo ya da aygıt takılır/çıkarılırsa.",
        TriggerKind.Input => "Elini çektikten 2 sn sonra nöbete geçer; sonraki her dokunuş alarmı çaldırır.",
        TriggerKind.Display => "Harici monitör / dock bağlantısı koparılırsa.",
        _ => "",
    };

    /// <summary>Segoe MDL2 / Fluent simge kodu (SF Symbols karşılığı).</summary>
    public static string Glyph(this TriggerKind k) => Glyphs.Of(k switch
    {
        TriggerKind.Motion => 0xE7C2,     // Move
        TriggerKind.Proximity => 0xE77B,  // Contact
        TriggerKind.Power => 0xE945,      // LightningBolt
        TriggerKind.Clamshell => 0xE7F8,  // DeviceLaptopNoPic
        TriggerKind.Usb => 0xE88E,        // USB
        TriggerKind.Input => 0xE765,      // KeyboardClassic
        TriggerKind.Display => 0xE7F4,    // TVMonitor
        _ => 0xE946,                      // Info
    });

    /// <summary>Kamera izni gerektirenler.</summary>
    public static bool NeedsCamera(this TriggerKind k) => k is TriggerKind.Motion or TriggerKind.Proximity;
}

/// <summary>Sensörden çıkan somut olay.</summary>
public sealed class TriggerEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public TriggerKind Kind { get; }
    public DateTime Date { get; }
    /// <summary>Kullanıcıya gösterilecek serbest metin, örn. "Şarj kablosu çıkarıldı".</summary>
    public string Message { get; }
    /// <summary>0...1 arası şiddet; kamera sensörleri doldurur, diğerleri 1.0 verir.</summary>
    public double Intensity { get; }

    public TriggerEvent(TriggerKind kind, string message, double intensity = 1.0, DateTime? date = null)
    {
        Kind = kind;
        Message = message;
        Intensity = intensity;
        Date = date ?? DateTime.Now;
    }
}
