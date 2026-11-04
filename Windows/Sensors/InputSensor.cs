using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using MacGuard.Core;
using MacGuard.Platform;

namespace MacGuard.Sensors;

/// <summary>
/// Klavye / touchpad dokunuşunu yakalar.
/// Olay dinlemek yerine sistemin "son girdiden bu yana geçen süre" sayacını
/// (GetLastInputInfo) okur; bu yöntem hiçbir izin ya da kanca gerektirmez.
/// </summary>
public sealed class InputSensor : ISensor
{
    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Input };
    public Action<TriggerEvent>? OnTrigger { get; set; }

    private Timer? _timer;
    private DateTime _lastFired = DateTime.MinValue;
    /// <summary>
    /// Koruma açıldığı anda elin hâlâ klavyededir. Bir süre dokunulmadan
    /// geçmeden nöbete geçmeyiz; yoksa kendi dokunuşun alarm çaldırır.
    /// </summary>
    private volatile bool _isLive;
    private const double SettleSeconds = 2.0;

    /// <summary>Nöbete geçti mi? Arayüz bunu gösterir, "neden çalmıyor" görünür olsun.</summary>
    public Action<bool>? OnLiveChange { get; set; }

    /// <summary>Kullanıcının en son girdi yaptığı andan bu yana geçen saniye.</summary>
    public static double SecondsSinceLastInput()
    {
        var info = new Native.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref info)) return double.MaxValue;
        // İkisi de 32 bitlik milisaniye sayacı; taşmayı işaretsiz çıkarma çözer.
        var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return elapsed / 1000.0;
    }

    public void Start()
    {
        _isLive = false;
        PostLive(false);
        _timer = new Timer(_ => Poll(), null, 250, 250);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _isLive = false;
        PostLive(false);
    }

    private void PostLive(bool live)
    {
        var cb = OnLiveChange;
        if (cb == null) return;
        Application.Current?.Dispatcher.BeginInvoke(() => cb(live));
    }

    private void Poll()
    {
        var idle = SecondsSinceLastInput();

        if (!_isLive)
        {
            // Belirli bir süre hiç dokunulmadıysa nöbete geç.
            if (idle >= SettleSeconds)
            {
                _isLive = true;
                PostLive(true);
            }
            return;
        }

        if (idle >= 0.5) return;
        // Aynı dokunuş serisinden saniyede bir kereden fazla olay üretme.
        if ((DateTime.Now - _lastFired).TotalSeconds <= 1.5) return;
        _lastFired = DateTime.Now;
        this.Emit(new TriggerEvent(TriggerKind.Input, "Klavyeye veya touchpad'e dokunuldu"));
    }
}
