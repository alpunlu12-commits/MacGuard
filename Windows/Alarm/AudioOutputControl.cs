using System;
using System.Linq;
using NAudio.CoreAudioApi;

namespace MacGuard.Alarm;

/// <summary>
/// Çıkış aygıtının ses seviyesi ve sessiz kipi (Core Audio, NAudio üzerinden).
///
/// macOS sürümü alarm anında sistemin varsayılan çıkışını dahili hoparlöre
/// çeviriyor. Windows'ta varsayılan aygıtı değiştiren genel bir API yok;
/// bunun yerine siren doğrudan hoparlör uç noktasına çalınır. Kulaklık
/// varsayılan aygıt olsa bile ses hoparlörden çıkar.
///
/// Bütün çağrılar <see cref="AudioThread"/> üzerinde çalışmalı.
/// </summary>
internal static class AudioOutputControl
{
    /// <summary>
    /// Alarmın çalınacağı uç nokta. "Dahili hoparlör" isteniyorsa form faktörü
    /// hoparlör olan etkin bir uç nokta aranır; yoksa varsayılan çıkış.
    /// </summary>
    public static MMDevice? AlarmDevice(MMDeviceEnumerator e, bool preferSpeakers)
    {
        if (preferSpeakers)
        {
            try
            {
                var speakers = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .FirstOrDefault(IsSpeaker);
                if (speakers != null) return speakers;
            }
            catch { }
        }
        try { return e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }   // hiç çıkış aygıtı yok
    }

    private static bool IsSpeaker(MMDevice d)
    {
        try
        {
            // PKEY_AudioEndpoint_FormFactor: 1 = Speakers (3 = Headphones, 5 = Headset, 9 = HDMI ekran)
            var v = d.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor]?.Value;
            return v != null && Convert.ToInt32(v) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static MMDevice? Find(MMDeviceEnumerator e, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try { return e.GetDevice(id); } catch { return null; }
    }

    public static float? Volume(MMDevice d)
    {
        try { return d.AudioEndpointVolume.MasterVolumeLevelScalar; } catch { return null; }
    }

    public static bool IsMuted(MMDevice d)
    {
        try { return d.AudioEndpointVolume.Mute; } catch { return false; }
    }

    public static void SetVolume(MMDevice d, float value)
    {
        try { d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch { }
    }

    public static void SetMuted(MMDevice d, bool muted)
    {
        try { d.AudioEndpointVolume.Mute = muted; } catch { }
    }

    /// <summary>
    /// Windows'un "Ses karıştırıcısı"nda uygulama başına ses ayarı var. Biri
    /// yalnızca MacGuard'ı kısarsa ya da sessize alırsa onu da geri al.
    /// </summary>
    public static void EnsureOwnSessionAudible(MMDevice d)
    {
        try
        {
            var s = d.AudioSessionManager.SimpleAudioVolume;
            if (s.Mute) s.Mute = false;
            if (s.Volume < 0.99f) s.Volume = 1f;
        }
        catch { }
    }
}
