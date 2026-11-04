using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using MacGuard.Core;
using MacGuard.Platform;
using MacGuard.Sensors;
using Screen = System.Windows.Forms.Screen;

namespace MacGuard.UI;

/// <summary>Perde pencerelerinin ortak yüzü.</summary>
public interface IOverlayWindow
{
    bool AllowClose { get; set; }
    PinPad PinPad { get; }
}

/// <summary>İki perde ve onları ekranda tutan nöbetçi.</summary>
public static class OverlayManager
{
    public static LockScreenController LockScreen { get; } = new();
    public static AlarmOverlayController Alarm { get; } = new();
    public static OverlayGuardian Guardian { get; } = new();
}

/// <summary>
/// Ekran başına bir perde penceresi kurar. Pencere, ekranın fiziksel piksel
/// sınırlarına SetWindowPos ile oturtulur: farklı ölçekleme oranlı ekranlarda
/// da kenar boşluğu kalmasın.
/// </summary>
public abstract class OverlayController
{
    protected readonly List<Window> Windows = new();

    public bool IsVisible => Windows.Count > 0;

    protected abstract Window Create();

    /// <summary>Ekran takıldı/çıkarıldı: aynı içerikle yeni ekran listesine göre kur.</summary>
    public abstract void RebuildForCurrentScreens();

    protected void ShowOnAllScreens()
    {
        foreach (var screen in Screen.AllScreens)
        {
            var w = Create();
            Place(w, screen);
            w.Show();
            Windows.Add(w);
        }
        BringToFront(force: true);
    }

    public void Hide()
    {
        foreach (var w in Windows)
        {
            if (w is IOverlayWindow o) o.AllowClose = true;
            w.Close();
        }
        Windows.Clear();
    }

    private static void Place(Window w, Screen screen)
    {
        var b = screen.Bounds;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        // Yaklaşık konum (DIP); asıl yerleşim SourceInitialized'da fiziksel pikselle yapılır.
        w.Left = b.X;
        w.Top = b.Y;

        void Fit()
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        w.SourceInitialized += (_, _) => Fit();
        w.Loaded += (_, _) => Fit();
        // Farklı ölçekli ekrana geçerken WPF pencereyi yeniden boyutlandırabiliyor.
        w.DpiChanged += (_, _) => w.Dispatcher.BeginInvoke(Fit, DispatcherPriority.Background);
    }

    /// <summary>
    /// Tüm perde pencerelerini öne alır ve klavyeyi farenin bulunduğu ekrandaki
    /// tuş takımına verir — PIN, kullanıcının baktığı ekranda yazılabilsin diye.
    ///
    /// Perdelerden biri zaten etkinse dokunmaz: kullanıcı bir ekranda PIN
    /// yazarken fare öbür ekranda duruyor olabilir; odağı taşırsak yazdığı
    /// haneler iki tuş takımına bölünürdü.
    /// </summary>
    public void BringToFront(bool force = false)
    {
        if (Windows.Count == 0) return;
        foreach (var w in Windows)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero)
                Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
        if (!force && Windows.Any(w => w.IsActive)) return;

        Native.GetCursorPos(out var p);
        var target = Windows.FirstOrDefault(w =>
        {
            var s = Screen.FromHandle(new WindowInteropHelper(w).Handle).Bounds;
            return s.Contains(p.X, p.Y);
        }) ?? Windows[0];

        Native.ForceForeground(new WindowInteropHelper(target).Handle);
        target.Activate();
        if (target is IOverlayWindow o) o.PinPad.Focus();
    }
}

/// <summary>Koruma aktifken tüm ekranları kaplayan caydırıcı bilgi ekranı.</summary>
public sealed class LockScreenController : OverlayController
{
    private (bool preview, Action? onDismiss)? _current;

    public void Show(bool preview = false, Action? onPreviewDismiss = null)
    {
        if (Windows.Count > 0) return;
        _current = (preview, onPreviewDismiss);
        ShowOnAllScreens();
    }

    public new void Hide()
    {
        base.Hide();
        _current = null;
    }

    protected override Window Create() =>
        new LockScreenWindow(_current?.preview ?? false, _current?.onDismiss);

    public override void RebuildForCurrentScreens()
    {
        if (_current is not { } c) return;
        Hide();
        Show(c.preview, c.onDismiss);
    }
}

/// <summary>Alarm anında tüm ekranları kaplayan kırmızı perde.</summary>
public sealed class AlarmOverlayController : OverlayController
{
    private (TriggerEvent reason, bool warning)? _current;

    public void Show(TriggerEvent reason, bool warning)
    {
        // Uyarıdan tam alarma geçerken pencereleri baştan kur.
        Hide();
        _current = (reason, warning);
        ShowOnAllScreens();
    }

    public new void Hide()
    {
        base.Hide();
        _current = null;
    }

    protected override Window Create() =>
        new AlarmWindow(_current!.Value.reason, _current.Value.warning);

    public override void RebuildForCurrentScreens()
    {
        if (_current is not { } c) return;
        Show(c.reason, c.warning);
    }
}

/// <summary>
/// Koruma/alarm perdesinin ekranda KALMASINI garanti eden nöbetçi.
///
/// Perdeler bir kez kurulup unutulursa gerçek hayatta yetmiyor:
///  - kapak kapanıp açılınca, ikinci ekran takılıp çıkarılınca ekran listesi
///    değişiyor; yeni ekranda perde hiç olmuyor, eskisinin çerçevesi kayıyor,
///  - Alt+Tab / başka bir pencere odağı alınca perde görünse de tuşlar ona
///    gitmiyor, yani PIN yazılamıyor.
///
/// Nöbetçi iki şeye bakar: ekran düzeni değişti mi, ve kullanıcı az önce
/// bilgisayara dokundu mu ("son girdiden bu yana geçen süre" — izin gerekmez).
/// </summary>
public sealed class OverlayGuardian
{
    private DispatcherTimer? _timer;
    private string _lastSignature = "";
    private DateTime _lastReassert = DateTime.MinValue;

    /// <summary>Bu kadar saniye içinde girdi olduysa "kullanıcı ekrana dokundu" sayılır.</summary>
    private const double RecentInputWindow = 1.2;
    /// <summary>Aynı dokunuş serisinde pencereyi saniyede bir kereden fazla zorlamayalım.</summary>
    private const double ReassertCooldown = 0.8;

    public bool IsRunning => _timer != null;

    public void Start()
    {
        if (_timer != null) return;
        _lastSignature = ScreenSignature();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _lastSignature = "";
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(ScreensChanged);
    }

    private static bool HasOverlay => OverlayManager.Alarm.IsVisible || OverlayManager.LockScreen.IsVisible;

    private void Tick()
    {
        if (!HasOverlay) return;

        if (ScreenSignature() != _lastSignature)
        {
            ScreensChanged();
            return;
        }

        // Kullanıcı az önce klavyeye/touchpad'e dokunduysa perdeyi öne al.
        // Perde zaten önde ve etkinse BringToFront hiçbir şey yapmaz; yoksa
        // her dokunuşta odak sıfırlanır ve yazılan PIN uçardı.
        if (InputSensor.SecondsSinceLastInput() >= RecentInputWindow) return;
        if ((DateTime.Now - _lastReassert).TotalSeconds <= ReassertCooldown) return;
        Reassert();
    }

    private void ScreensChanged()
    {
        if (!HasOverlay) return;
        _lastSignature = ScreenSignature();
        // Perdeyi yeni ekran düzenine göre baştan kur.
        OverlayManager.Alarm.RebuildForCurrentScreens();
        OverlayManager.LockScreen.RebuildForCurrentScreens();
        _lastReassert = DateTime.Now;
    }

    /// <summary>Perdeyi öne getirir ve klavyeyi ona verir. Zaten öndeyse dokunmaz.</summary>
    public void Reassert()
    {
        if (!HasOverlay) return;
        _lastReassert = DateTime.Now;
        // Alarm perdesi koruma perdesinin üstünde; varsa klavyeyi o almalı.
        if (OverlayManager.Alarm.IsVisible) OverlayManager.Alarm.BringToFront();
        else OverlayManager.LockScreen.BringToFront();
    }

    /// <summary>Ekran konumları + boyutları: düzen değiştiğinde değişen bir parmak izi.</summary>
    private static string ScreenSignature() =>
        string.Join("|", Screen.AllScreens.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));
}
