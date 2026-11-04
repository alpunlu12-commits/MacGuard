using System;
using System.Linq;
using System.Threading;
using System.Windows;
using MacGuard.Core;
using MacGuard.Platform;
using MacGuard.UI;

namespace MacGuard;

public partial class App : Application
{
    private const string MutexName = @"Local\MacGuard.SingleInstance";
    private const string ShowEventName = @"Local\MacGuard.ShowWindow";

    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private bool _shutdownPrepared;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tek örnek: ikinci kez açılırsa var olan pencereyi öne getir ve çık.
        _single = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(ShowMainWindow);
        }) { IsBackground = true, Name = "MacGuard.ShowListener" };
        listener.Start();

        // Alarm çalarken beklenmedik bir hata uygulamayı düşürmesin: siren susar,
        // perde kaybolur. Hatayı kaydet ve devam et.
        DispatcherUnhandledException += (_, args) =>
        {
            try { EventLog.Shared.Log("Beklenmeyen hata", args.Exception.Message, Glyphs.Error, Severity.Warn); } catch { }
            args.Handled = true;
        };

        // Koruma açıkken oturum kapatma reddedilir (Windows "yine de kapat" seçeneği sunar).
        SessionEnding += (_, args) =>
        {
            if (GuardEngine.Shared.BlocksTermination) args.Cancel = true;
        };

        var engine = GuardEngine.Shared;
        TrayIcon.Install();

        var window = new DashboardWindow();
        MainWindow = window;
        if (!e.Args.Contains("--background")) window.Show();

        // Önceki çalıştırma çökerek kapak ayarını değiştirilmiş bıraktıysa geri al.
        engine.CheckSleepLeftover();
        EventLog.Shared.Log("MacGuard başlatıldı", "", Glyphs.Power, Severity.Info);
    }

    public static void ShowMainWindow()
    {
        if (Current.MainWindow is not { } w) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        // Perde açıksa ana pencere onun önüne geçmesin.
        if (GuardEngine.Shared.State.ShowsOverlay) OverlayManager.Guardian.Reassert();
    }

    /// <summary>
    /// Koruma açıkken uygulama kapatılamaz; alarmı susturmanın tek yolu PIN.
    /// Uyarı kutusu açılmaz: kutu perdenin arkasında kalıp klavyeyi yutabilir.
    /// Sessizce reddedip perdeyi öne alıyoruz.
    /// </summary>
    public static void RequestQuit()
    {
        var engine = GuardEngine.Shared;
        if (engine.BlocksTermination)
        {
            EventLog.Shared.Log("Kapatma denemesi engellendi", "Koruma açıkken MacGuard kapatılamaz",
                Glyphs.Shield, Severity.Warn);
            OverlayManager.Guardian.Reassert();
            return;
        }
        ((App)Current).PrepareShutdown();
        Current.Shutdown();
    }

    private void PrepareShutdown()
    {
        if (_shutdownPrepared) return;
        _shutdownPrepared = true;
        // Sistem ayarlarını (kapak eylemi, ses) arkamızda bırakmayalım.
        GuardEngine.Shared.PrepareForShutdown();
        TrayIcon.Remove();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_single != null && _showSignal != null) PrepareShutdown();
        try { _single?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
