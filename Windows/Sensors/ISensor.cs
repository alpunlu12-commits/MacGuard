using System;
using System.Collections.Generic;
using System.Windows;
using MacGuard.Core;

namespace MacGuard.Sensors;

/// <summary>
/// Tüm sensörlerin ortak arayüzü. Olayları <see cref="SensorExtensions.Emit"/>
/// ile arayüz iş parçacığına taşırlar.
/// </summary>
public interface ISensor
{
    /// <summary>Bu sensörün ürettiği tetik türleri (kamera ikisini birden üretir).</summary>
    IReadOnlyList<TriggerKind> Kinds { get; }
    void Start();
    void Stop();
    Action<TriggerEvent>? OnTrigger { get; set; }
}

public static class SensorExtensions
{
    /// <summary>Olayı arayüz iş parçacığına taşıyarak yayınlar.</summary>
    public static void Emit(this ISensor sensor, TriggerEvent e)
    {
        var cb = sensor.OnTrigger;
        if (cb == null) return;
        var d = Application.Current?.Dispatcher;
        if (d == null) return;
        d.BeginInvoke(() => cb(e));
    }
}
