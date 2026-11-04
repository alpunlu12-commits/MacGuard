using System;
using System.Collections.Generic;
using MacGuard.Core;
using MacGuard.Platform;

namespace MacGuard.Sensors;

/// <summary>
/// Ekran kapağının kapatılmasını GUID_LIDSWITCH_STATE_CHANGE bildirimiyle izler.
/// Yedek olarak sistemin uykuya girme bildirimini de dinler.
/// </summary>
public sealed class LidSensor : ISensor
{
    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Clamshell };
    public Action<TriggerEvent>? OnTrigger { get; set; }

    private bool _running;
    /// <summary>Windows abone olur olmaz o anki durumu bildiriyor; ilkini tetik sayma.</summary>
    private bool _sawInitial;
    private bool _lastClosed;

    /// <summary>
    /// Kapak bildirimi yalnızca kapağı olan cihazlarda gelir. Pili olmayan
    /// bir masaüstü bilgisayarda bu sensör anlamsız.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            if (!Native.GetSystemPowerStatus(out var s)) return true;
            return (s.BatteryFlag & 128) == 0;   // 128 = sistem pili yok
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _sawInitial = false;
        _lastClosed = false;
        MessageWindow.Shared.PowerSettingChanged += OnPowerSetting;
        MessageWindow.Shared.Suspending += OnSuspending;
        MessageWindow.Shared.SubscribePowerSetting(Native.GUID_LIDSWITCH_STATE_CHANGE);
    }

    public void Stop()
    {
        _running = false;
        MessageWindow.Shared.PowerSettingChanged -= OnPowerSetting;
        MessageWindow.Shared.Suspending -= OnSuspending;
        MessageWindow.Shared.UnsubscribePowerSetting(Native.GUID_LIDSWITCH_STATE_CHANGE);
    }

    private void OnPowerSetting(Guid setting, int data)
    {
        if (!_running || setting != Native.GUID_LIDSWITCH_STATE_CHANGE) return;
        var closed = data == 0;
        var wasClosed = _lastClosed;
        _lastClosed = closed;
        if (!_sawInitial) { _sawInitial = true; return; }
        if (closed && !wasClosed) this.Emit(new TriggerEvent(TriggerKind.Clamshell, "Ekran kapağı kapatıldı"));
    }

    private void OnSuspending()
    {
        if (!_running) return;
        this.Emit(new TriggerEvent(TriggerKind.Clamshell, "Bilgisayar uykuya alınıyor"));
    }
}
