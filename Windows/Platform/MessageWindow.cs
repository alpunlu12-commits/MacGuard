using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MacGuard.Platform;

/// <summary>
/// Sistem bildirimlerini alan görünmez pencere.
///
/// Kapak, güç kaynağı ve USB olayları Windows'ta pencere mesajı olarak gelir
/// (WM_POWERBROADCAST, WM_DEVICECHANGE). macOS'taki IOKit bildirimlerinin
/// karşılığı bu. Mesaj-yalnız (HWND_MESSAGE) pencere yerine gizli bir üst
/// düzey pencere kullanılıyor: oturum kapatma sorgusu (WM_QUERYENDSESSION)
/// yalnızca üst düzey pencerelere gönderiliyor.
/// Yalnızca arayüz iş parçacığından kullanılır.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private static MessageWindow? _shared;
    public static MessageWindow Shared => _shared ??= new MessageWindow();

    private readonly HwndSource _source;

    /// <summary>Güç ayarı değişti: (ayar, ilk veri baytı).</summary>
    public event Action<Guid, int>? PowerSettingChanged;
    /// <summary>Sistem uykuya giriyor.</summary>
    public event Action? Suspending;
    /// <summary>Sistem uykudan uyandı.</summary>
    public event Action? Resumed;
    /// <summary>Bir aygıt arayüzü takıldı (true) ya da çıkarıldı (false).</summary>
    public event Action<Guid, bool>? DeviceInterfaceChanged;
    /// <summary>
    /// Oturum kapatma / yeniden başlatma sorulduğunda true dönerse reddedilir.
    /// Windows kullanıcıya "Bu uygulama kapatmayı engelliyor" der; kullanıcı yine
    /// de zorlayabilir — bu bir caydırıcı, kilit değil.
    /// </summary>
    public Func<bool>? BlockEndSession { get; set; }

    public IntPtr Handle => _source.Handle;

    private readonly Dictionary<Guid, IntPtr> _powerRegistrations = new();
    private readonly Dictionary<Guid, IntPtr> _deviceRegistrations = new();

    private MessageWindow()
    {
        var p = new HwndSourceParameters("MacGuardMessages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,       // WS_OVERLAPPED, hiç gösterilmez
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW: görev çubuğunda görünmesin
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Güç ayarı bildirimine abone olur. Windows abone olur olmaz o anki
    /// değeri bir kez gönderir; sensörler başlangıç durumunu buradan öğrenir.
    /// </summary>
    public void SubscribePowerSetting(Guid setting)
    {
        if (_powerRegistrations.ContainsKey(setting)) return;
        var g = setting;
        var h = Native.RegisterPowerSettingNotification(Handle, ref g, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (h != IntPtr.Zero) _powerRegistrations[setting] = h;
    }

    public void UnsubscribePowerSetting(Guid setting)
    {
        if (_powerRegistrations.Remove(setting, out var h)) Native.UnregisterPowerSettingNotification(h);
    }

    public void SubscribeDeviceInterface(Guid classGuid)
    {
        if (_deviceRegistrations.ContainsKey(classGuid)) return;
        var filter = new Native.DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<Native.DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = classGuid,
        };
        var buffer = Marshal.AllocHGlobal(filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            var h = Native.RegisterDeviceNotification(Handle, buffer, Native.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (h != IntPtr.Zero) _deviceRegistrations[classGuid] = h;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void UnsubscribeDeviceInterface(Guid classGuid)
    {
        if (_deviceRegistrations.Remove(classGuid, out var h)) Native.UnregisterDeviceNotification(h);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_QUERYENDSESSION:
                if (BlockEndSession?.Invoke() == true)
                {
                    handled = true;
                    return IntPtr.Zero;   // FALSE: kapatmayı reddet
                }
                break;

            case Native.WM_POWERBROADCAST:
                switch (wParam.ToInt32())
                {
                    case Native.PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                        var s = Marshal.PtrToStructure<Native.POWERBROADCAST_SETTING>(lParam);
                        PowerSettingChanged?.Invoke(s.PowerSetting, s.Data);
                        break;
                    case Native.PBT_APMSUSPEND:
                        Suspending?.Invoke();
                        break;
                    case Native.PBT_APMRESUMEAUTOMATIC:
                    case Native.PBT_APMRESUMESUSPEND:
                        Resumed?.Invoke();
                        break;
                }
                break;

            case Native.WM_DEVICECHANGE:
                var ev = wParam.ToInt32();
                if ((ev == Native.DBT_DEVICEARRIVAL || ev == Native.DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero)
                {
                    var hdr = Marshal.PtrToStructure<Native.DEV_BROADCAST_HDR>(lParam);
                    if (hdr.dbch_devicetype == Native.DBT_DEVTYP_DEVICEINTERFACE)
                    {
                        var di = Marshal.PtrToStructure<Native.DEV_BROADCAST_DEVICEINTERFACE>(lParam);
                        DeviceInterfaceChanged?.Invoke(di.dbcc_classguid, ev == Native.DBT_DEVICEARRIVAL);
                    }
                }
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var h in _powerRegistrations.Values) Native.UnregisterPowerSettingNotification(h);
        foreach (var h in _deviceRegistrations.Values) Native.UnregisterDeviceNotification(h);
        _powerRegistrations.Clear();
        _deviceRegistrations.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
