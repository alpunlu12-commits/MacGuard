using System;
using System.Collections.Generic;
using Microsoft.Win32;
using MacGuard.Core;

namespace MacGuard.Sensors;

/// <summary>Harici ekran / dock bağlantısındaki değişimi izler.</summary>
public sealed class DisplaySensor : ISensor
{
    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Display };
    public Action<TriggerEvent>? OnTrigger { get; set; }

    private int _lastCount;
    private bool _running;

    public static int ScreenCount => System.Windows.Forms.Screen.AllScreens.Length;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastCount = ScreenCount;
        SystemEvents.DisplaySettingsChanged += OnChanged;
    }

    public void Stop()
    {
        _running = false;
        SystemEvents.DisplaySettingsChanged -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_running) return;
        var now = ScreenCount;
        var last = _lastCount;
        _lastCount = now;
        if (now == last) return;
        var msg = now < last ? "Harici ekran bağlantısı koparıldı" : "Yeni bir ekran bağlandı";
        this.Emit(new TriggerEvent(TriggerKind.Display, msg));
    }
}
