using System;
using System.Collections.Generic;
using MacGuard.Core;
using MacGuard.Platform;

namespace MacGuard.Sensors;

/// <summary>
/// Şarj adaptörünün çıkarılmasını güç kaynağı bildirimiyle (GUID_ACDC_POWER_SOURCE)
/// anında yakalar. macOS'taki IOKit güç kaynağı bildiriminin karşılığı.
/// </summary>
public sealed class PowerSensor : ISensor
{
    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Power };
    public Action<TriggerEvent>? OnTrigger { get; set; }

    private bool _lastWasAC = true;
    private bool _running;

    /// <summary>Şu an prize takılı mı? Pili olmayan masaüstlerinde her zaman true.</summary>
    public static bool IsOnACPower()
    {
        if (!Native.GetSystemPowerStatus(out var s)) return true;
        return s.ACLineStatus != 0;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastWasAC = IsOnACPower();
        MessageWindow.Shared.PowerSettingChanged += OnPowerSetting;
        MessageWindow.Shared.SubscribePowerSetting(Native.GUID_ACDC_POWER_SOURCE);
    }

    public void Stop()
    {
        _running = false;
        MessageWindow.Shared.PowerSettingChanged -= OnPowerSetting;
        MessageWindow.Shared.UnsubscribePowerSetting(Native.GUID_ACDC_POWER_SOURCE);
    }

    private void OnPowerSetting(Guid setting, int data)
    {
        if (!_running || setting != Native.GUID_ACDC_POWER_SOURCE) return;
        var isAC = data == 0;
        var wasAC = _lastWasAC;
        _lastWasAC = isAC;
        // Sadece "takılıyken çıkarıldı" geçişi tetikler.
        if (wasAC && !isAC) this.Emit(new TriggerEvent(TriggerKind.Power, "Şarj kablosu çıkarıldı"));
    }
}
