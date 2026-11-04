using System;
using System.Collections.Generic;
using MacGuard.Core;
using MacGuard.Platform;

namespace MacGuard.Sensors;

/// <summary>
/// USB aygıt takma/çıkarma bildirimleri (GUID_DEVINTERFACE_USB_DEVICE).
/// macOS'taki IOKit eşleşme bildirimlerinin karşılığı. Bellek, kablo, fare —
/// USB veri yolundaki her aygıt.
/// </summary>
public sealed class UsbSensor : ISensor
{
    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Usb };
    public Action<TriggerEvent>? OnTrigger { get; set; }

    private bool _running;
    private DateTime _lastFired = DateTime.MinValue;

    public void Start()
    {
        if (_running) return;
        _running = true;
        MessageWindow.Shared.DeviceInterfaceChanged += OnDeviceChanged;
        MessageWindow.Shared.SubscribeDeviceInterface(Native.GUID_DEVINTERFACE_USB_DEVICE);
    }

    public void Stop()
    {
        _running = false;
        MessageWindow.Shared.DeviceInterfaceChanged -= OnDeviceChanged;
        MessageWindow.Shared.UnsubscribeDeviceInterface(Native.GUID_DEVINTERFACE_USB_DEVICE);
    }

    private void OnDeviceChanged(Guid cls, bool arrived)
    {
        if (!_running || cls != Native.GUID_DEVINTERFACE_USB_DEVICE) return;
        // Tek bir takma işlemi birden çok arayüz bildirimi üretebiliyor (hub + aygıt).
        if ((DateTime.Now - _lastFired).TotalSeconds < 1.0) return;
        _lastFired = DateTime.Now;
        this.Emit(new TriggerEvent(TriggerKind.Usb, arrived ? "USB aygıt takıldı" : "USB aygıt çıkarıldı"));
    }
}
