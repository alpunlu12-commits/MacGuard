using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MacGuard.Alarm;
using MacGuard.Notify;
using MacGuard.Platform;
using MacGuard.Security;
using MacGuard.Sensors;
using MacGuard.UI;
using NAudio.CoreAudioApi;

namespace MacGuard.Core;

public enum GuardStateKind { Disarmed, Arming, Armed, Warning, Alarming }

/// <summary>Durum makinesinin anlık hâli (macOS'taki State enum'ının karşılığı).</summary>
public readonly record struct GuardState(GuardStateKind Kind, int Remaining = 0, TriggerKind Reason = TriggerKind.Motion)
{
    public static GuardState Disarmed => new(GuardStateKind.Disarmed);
    public bool IsProtecting => Kind != GuardStateKind.Disarmed;
    public bool IsAlarming => Kind == GuardStateKind.Alarming;
    public bool IsWarning => Kind == GuardStateKind.Warning;
    public bool IsArmed => Kind == GuardStateKind.Armed;
    /// <summary>Alarm perdesinin görünmesi gereken durumlar.</summary>
    public bool ShowsOverlay => IsAlarming || IsWarning;
}

/// <summary>
/// MacGuard'ın durum makinesi: sensörleri kurar, tetikleri alarma çevirir,
/// PIN doğrulanınca her şeyi eski haline döndürür.
/// Yalnızca arayüz iş parçacığından kullanılır (macOS'taki @MainActor).
/// </summary>
public sealed class GuardEngine : ObservableObject
{
    private static GuardEngine? _shared;
    public static GuardEngine Shared => _shared ??= new GuardEngine();

    // MARK: Yayınlanan durum

    private GuardState _state = GuardState.Disarmed;
    public GuardState State { get => _state; private set => Set(ref _state, value); }

    private TriggerEvent? _lastTrigger;
    public TriggerEvent? LastTrigger { get => _lastTrigger; private set => Set(ref _lastTrigger, value); }

    private DateTime? _armedSince;
    public DateTime? ArmedSince { get => _armedSince; private set => Set(ref _armedSince, value); }

    /// <summary>Arayüzdeki canlı hassasiyet göstergeleri.</summary>
    private double _liveMotionScore, _liveMotionCoverage, _liveFaceHeight;
    public double LiveMotionScore { get => _liveMotionScore; private set => Set(ref _liveMotionScore, value); }
    public double LiveMotionCoverage { get => _liveMotionCoverage; private set => Set(ref _liveMotionCoverage, value); }
    public double LiveFaceHeight { get => _liveFaceHeight; private set => Set(ref _liveFaceHeight, value); }

    /// <summary>Alt sensörler nöbete geçti mi? (sahne sakinleşmesi tamamlandı mı)</summary>
    private bool _motionWatchReady, _proximityWatchReady, _inputWatchReady;
    public bool MotionWatchReady { get => _motionWatchReady; private set => Set(ref _motionWatchReady, value); }
    public bool ProximityWatchReady { get => _proximityWatchReady; private set => Set(ref _proximityWatchReady, value); }
    public bool InputWatchReady { get => _inputWatchReady; private set => Set(ref _inputWatchReady, value); }

    /// <summary>Önceki çalıştırmadan kalan kapak ayarı var ve elle düzeltilmeli.</summary>
    private bool _sleepLeftoverNeedsAttention;
    public bool SleepLeftoverNeedsAttention { get => _sleepLeftoverNeedsAttention; set => Set(ref _sleepLeftoverNeedsAttention, value); }

    /// <summary>Yanlış PIN denemelerinden sonraki bekleme.</summary>
    private DateTime? _lockoutUntil;
    public DateTime? LockoutUntil { get => _lockoutUntil; private set => Set(ref _lockoutUntil, value); }
    private int _failedAttempts;
    public int FailedAttempts { get => _failedAttempts; private set => Set(ref _failedAttempts, value); }

    private string _lastMessage = "";
    public string LastMessage { get => _lastMessage; set => Set(ref _lastMessage, value); }

    /// <summary>Korumayı açmadan kamerayı çalıştırıp eşikleri ayarlama modu.</summary>
    private bool _isCalibrating;
    public bool IsCalibrating { get => _isCalibrating; private set => Set(ref _isCalibrating, value); }
    /// <summary>Korumayı açmadan sirenin sesini dinleme.</summary>
    private bool _isPreviewingSiren;
    public bool IsPreviewingSiren { get => _isPreviewingSiren; private set => Set(ref _isPreviewingSiren, value); }
    /// <summary>Korumayı açmadan koruma ekranının nasıl göründüğünü görme.</summary>
    private bool _isPreviewingLockScreen;
    public bool IsPreviewingLockScreen { get => _isPreviewingLockScreen; private set => Set(ref _isPreviewingLockScreen, value); }

    // MARK: Bileşenler

    private readonly Settings _settings = Settings.Shared;
    private readonly EventLog _log = EventLog.Shared;

    private readonly AlarmSiren _siren = new();
    private readonly SpeechAlert _speech = new();
    private readonly SleepBlocker _sleepBlocker = new();
    private ISensor[] _sensors = Array.Empty<ISensor>();

    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _warningTimer;
    /// <summary>Alarm sırasında sesi kısmaya çalışanı geri alan nöbetçi.</summary>
    private Timer? _volumeWatchdog;

    // Ses iş parçacığına ait: alarmdan önceki ses seviyesi.
    private string? _audioDeviceId;
    private float? _previousVolume;
    private bool _previousMuted;
    private MMDeviceEnumerator? _audioEnumerator;

    private GuardEngine()
    {
        // Kapak kapanıp sistem uyuduysa alarm susar. Uyanır uyanmaz sürdür.
        MessageWindow.Shared.Resumed += ResumeAfterWake;
        // Koruma açıkken oturum kapatma / yeniden başlatma reddedilir.
        MessageWindow.Shared.BlockEndSession = () =>
        {
            if (!BlocksTermination) return false;
            _log.Log("Kapatma denemesi engellendi", "Koruma açıkken Windows oturumu kapatılamaz",
                Glyphs.Shield, Severity.Warn);
            return true;
        };

        CameraSensor.Shared.OnReadings = r =>
        {
            LiveMotionScore = r.MotionScore;
            LiveMotionCoverage = r.MotionCoverage;
            LiveFaceHeight = r.FaceHeight;
            MotionWatchReady = r.MotionLive;
            ProximityWatchReady = r.ProximityLive;
        };
    }

    // MARK: - Koruma açma / kapama

    /// <summary>Koruma modunu geri sayımla başlatır.</summary>
    private bool _armPending;

    public async void Arm()
    {
        if (State.Kind != GuardStateKind.Disarmed || _armPending) return;
        if (IsCalibrating) StopCalibration();
        if (!PinStore.IsConfigured)
        {
            LastMessage = "Önce bir PIN belirlemelisin.";
            return;
        }

        // Kamera gerekiyorsa erişimi şimdi dene; geri sayım sırasında sürpriz olmasın.
        var needsCamera = _settings.IsEnabled(TriggerKind.Motion) || _settings.IsEnabled(TriggerKind.Proximity)
                          || _settings.CaptureIntruderPhoto;
        if (needsCamera && CameraSensor.Authorization != CameraAccess.Granted)
        {
            _armPending = true;
            bool granted;
            try { granted = await CameraSensor.RequestAccessAsync(); }
            finally { _armPending = false; }
            if (State.Kind != GuardStateKind.Disarmed) return;
            if (!granted)
                LastMessage = CameraSensor.Authorization == CameraAccess.NoDevice
                    ? "Kamera bulunamadı. Hareket ve yakınlık algılama çalışmayacak."
                    : "Kamera erişimi kapalı. Hareket ve yakınlık algılama çalışmayacak.";
        }
        BeginCountdown();
    }

    private void BeginCountdown()
    {
        var delay = Math.Max(0, _settings.ArmDelay);
        _log.Log("Koruma başlatılıyor", $"{delay} saniye sonra devrede", Glyphs.Shield, Severity.Info);

        // Kamerayı geri sayım sırasında ısıt; devreye girdiğinde hazır olsun.
        PrepareCamera();

        if (delay <= 0)
        {
            FinishArming();
            return;
        }

        State = new GuardState(GuardStateKind.Arming, delay);
        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            if (State.Kind != GuardStateKind.Arming) { _countdownTimer?.Stop(); return; }
            if (State.Remaining <= 1)
            {
                _countdownTimer?.Stop();
                FinishArming();
            }
            else
            {
                State = new GuardState(GuardStateKind.Arming, State.Remaining - 1);
            }
        };
        _countdownTimer.Start();
    }

    public void CancelArming()
    {
        if (State.Kind != GuardStateKind.Arming) return;
        _countdownTimer?.Stop();
        _countdownTimer = null;
        CameraSensor.Shared.Stop();
        State = GuardState.Disarmed;
        _log.Log("Koruma iptal edildi", "", Glyphs.Cancel, Severity.Info);
    }

    private void FinishArming()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        StartSensors();
        _sleepBlocker.BeginSystemAwake();
        if (_settings.BlockClamshellSleep && !_sleepBlocker.DisableLidSleep())
        {
            _log.Log("Kapak ayarı değiştirilemedi", "Kapak kapanınca bilgisayar uyuyabilir",
                Glyphs.Warning, Severity.Warn);
        }
        ArmedSince = DateTime.Now;
        State = new GuardState(GuardStateKind.Armed);
        Native.ShutdownBlockReasonCreate(MessageWindow.Shared.Handle,
            "MacGuard koruması açık. Kapatmak için önce PIN ile korumayı kaldır.");
        if (_settings.ShowLockScreen)
        {
            // Ekran uyursa caydırıcı yazı kimseye görünmez.
            _sleepBlocker.BeginDisplayAwake();
            OverlayManager.LockScreen.Show();
        }
        // Perde bir kez kurulup unutulmaz: ekran düzeni değişirse yeniden kurar,
        // kullanıcı bilgisayara dokunduğunda öne alıp klavyeyi ona verir.
        OverlayManager.Guardian.Start();
        LastMessage = "Koruma aktif. Bilgisayarına göz kulak oluyorum.";
        _log.Log("Koruma aktif", ActiveSensorSummary(), Glyphs.Shield, Severity.Info);
        TrayIcon.ShowBalloon("MacGuard koruma modunda", ActiveSensorSummary());
    }

    /// <summary>PIN ile korumayı kapatır. Yanlış PIN artan bekleme süresiyle cezalandırılır.</summary>
    public bool Disarm(string pin)
    {
        if (LockoutUntil is { } until && until > DateTime.Now) return false;

        if (!PinStore.Verify(pin))
        {
            FailedAttempts++;
            if (FailedAttempts >= 3)
            {
                var wait = Math.Min(60, Math.Pow(2.0, FailedAttempts - 2) * 5);
                LockoutUntil = DateTime.Now.AddSeconds(wait);
            }
            _log.Log("Hatalı PIN denemesi", $"{FailedAttempts}. deneme", Glyphs.Lock, Severity.Warn);
            return false;
        }

        FailedAttempts = 0;
        LockoutUntil = null;
        Teardown("PIN doğrulandı");
        return true;
    }

    /// <summary>Alarmı ve korumayı tamamen kapatır, sistemi bulduğu gibi bırakır.</summary>
    private void Teardown(string reason)
    {
        var wasNoisy = State.IsAlarming || State.IsWarning;

        _warningTimer?.Stop();
        _warningTimer = null;
        StopAlarmOutputs();
        StopSensors();
        _sleepBlocker.EndAll();
        OverlayManager.Guardian.Stop();
        OverlayManager.Alarm.Hide();
        OverlayManager.LockScreen.Hide();
        KeyboardLockdown.Disable();
        Native.ShutdownBlockReasonDestroy(MessageWindow.Shared.Handle);

        _countdownTimer?.Stop();
        _countdownTimer = null;
        ArmedSince = null;
        State = GuardState.Disarmed;
        LastMessage = "Koruma kapatıldı.";
        _log.Log(wasNoisy ? "Alarm durduruldu" : "Koruma kapatıldı", reason, Glyphs.Unlock, Severity.Info);
    }

    // MARK: - Sensörler

    private void PrepareCamera()
    {
        var cam = CameraSensor.Shared;
        var wantsMotion = _settings.IsEnabled(TriggerKind.Motion);
        var wantsProximity = _settings.IsEnabled(TriggerKind.Proximity);
        var wantsSnapshot = _settings.CaptureIntruderPhoto;
        cam.SetAnalysis(wantsMotion, wantsProximity, triggers: true);
        cam.Configure(_settings.MotionSensitivity, _settings.MotionCoverage, _settings.ProximityThreshold);
        if (wantsMotion || wantsProximity || wantsSnapshot)
        {
            cam.OnTrigger = Handle;
            cam.Start();
        }
    }

    private void StartSensors()
    {
        StopSensors(keepCamera: true);

        var list = new System.Collections.Generic.List<ISensor>();
        if (_settings.IsEnabled(TriggerKind.Power)) list.Add(new PowerSensor());
        if (_settings.IsEnabled(TriggerKind.Clamshell)) list.Add(new LidSensor());
        if (_settings.IsEnabled(TriggerKind.Usb)) list.Add(new UsbSensor());
        if (_settings.IsEnabled(TriggerKind.Input))
        {
            // Nöbete geçişi arayüze taşı; "neden çalmıyor" görünür olsun.
            list.Add(new InputSensor { OnLiveChange = live => InputWatchReady = live });
        }
        if (_settings.IsEnabled(TriggerKind.Display)) list.Add(new DisplaySensor());

        foreach (var sensor in list)
        {
            sensor.OnTrigger = Handle;
            sensor.Start();
        }
        _sensors = list.ToArray();

        // Kamera geri sayımda ısıtılmıştı; analizi şimdi hedef eşiklerle açıyoruz.
        // Sahne sakinleşmesini sıfırla: sen hâlâ bilgisayarın başında olabilirsin.
        var cam = CameraSensor.Shared;
        cam.Configure(_settings.MotionSensitivity, _settings.MotionCoverage, _settings.ProximityThreshold);
        cam.ResetSettleState();
    }

    private void StopSensors(bool keepCamera = false)
    {
        foreach (var s in _sensors) s.Stop();
        _sensors = Array.Empty<ISensor>();
        if (!keepCamera)
        {
            CameraSensor.Shared.Stop();
            LiveMotionScore = 0;
            LiveMotionCoverage = 0;
            LiveFaceHeight = 0;
            MotionWatchReady = false;
            ProximityWatchReady = false;
        }
        InputWatchReady = false;
    }

    public string ActiveSensorSummary()
    {
        var names = TriggerKinds.All.Where(_settings.IsEnabled).Select(k => k.Title()).ToArray();
        return names.Length == 0 ? "Hiçbir sensör seçili değil!" : string.Join(" · ", names);
    }

    // MARK: - Tetik -> Alarm

    private void Handle(TriggerEvent e)
    {
        // Sadece koruma tam devredeyken tetik kabul edilir.
        if (State.Kind != GuardStateKind.Armed) return;
        if (!_settings.IsEnabled(e.Kind)) return;

        LastTrigger = e;
        _log.Log(e.Kind.Title(), e.Message, e.Kind.Glyph(), Severity.Alarm);

        if (_settings.GraceSeconds > 0) BeginWarning(e);
        else RaiseAlarm(e);
    }

    /// <summary>
    /// Tam alarmdan önce PIN girmen için süre tanır: kesik bip çalar, perdeyi
    /// açar ve geri sayar. Süre dolarsa tam alarma geçer.
    /// </summary>
    private void BeginWarning(TriggerEvent e)
    {
        State = new GuardState(GuardStateKind.Warning, _settings.GraceSeconds, e.Kind);

        OverlayManager.LockScreen.Hide();
        ApplyAlarmAudio();
        _siren.Start(SirenMode.Warning, _settings.ForceBuiltInSpeakers);
        _sleepBlocker.BeginDisplayAwake();
        OverlayManager.Alarm.Show(e, warning: true);
        OverlayManager.Guardian.Start();

        _warningTimer?.Stop();
        _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _warningTimer.Tick += (_, _) =>
        {
            if (State.Kind != GuardStateKind.Warning) { _warningTimer?.Stop(); return; }
            if (State.Remaining <= 1)
            {
                _warningTimer?.Stop();
                _warningTimer = null;
                RaiseAlarm(LastTrigger ?? new TriggerEvent(State.Reason, State.Reason.Title()));
            }
            else
            {
                State = State with { Remaining = State.Remaining - 1 };
            }
        };
        _warningTimer.Start();
    }

    private void RaiseAlarm(TriggerEvent e)
    {
        _warningTimer?.Stop();
        _warningTimer = null;
        State = new GuardState(GuardStateKind.Alarming, 0, e.Kind);

        // 1) Alarmın duyulacağından emin ol.
        ApplyAlarmAudio();

        // 2) Sesi başlat (uyarı bipi çalıyorsa tam sirene yükselir).
        _siren.Start(SirenMode.Full, _settings.ForceBuiltInSpeakers);
        if (_settings.SpeakWarning) _speech.StartRepeating(_settings.WarningText, _settings.ForceBuiltInSpeakers);

        // 3) Ekranı uyandır, perdeyi bas, kaçış yollarını kapat.
        _sleepBlocker.BeginDisplayAwake();
        KeyboardLockdown.Enable();
        OverlayManager.LockScreen.Hide();
        OverlayManager.Alarm.Show(e, warning: false);
        OverlayManager.Guardian.Start();

        // 4) Kanıtı topla ve telefona haber ver.
        _ = DispatchRemoteAlertAsync(e);
    }

    /// <summary>
    /// Sesi alarm seviyesine getirir ve nöbetçiyi başlatır.
    /// Uyarı aşamasında da çağrılır: bip duyulmazsa anlamı kalmaz.
    /// </summary>
    private void ApplyAlarmAudio()
    {
        var forceVolume = _settings.ForceMaxVolume;
        var preferSpeakers = _settings.ForceBuiltInSpeakers;
        var target = (float)_settings.AlarmVolume;
        if (forceVolume)
        {
            AudioThread.Post(() =>
            {
                _audioEnumerator ??= new MMDeviceEnumerator();
                var device = AudioOutputControl.AlarmDevice(_audioEnumerator, preferSpeakers);
                if (device == null) return;
                if (_previousVolume == null)
                {
                    _audioDeviceId = device.ID;
                    _previousVolume = AudioOutputControl.Volume(device);
                    _previousMuted = AudioOutputControl.IsMuted(device);
                }
                AudioOutputControl.SetMuted(device, false);
                AudioOutputControl.SetVolume(device, target);
                AudioOutputControl.EnsureOwnSessionAudible(device);
            });
        }
        StartVolumeWatchdog();
    }

    /// <summary>
    /// Hırsız ses tuşuna basarsa ya da sessize alırsa saniyede bir geri alır.
    /// Siren çıkışı ölmüşse (aygıt kayboldu, sürücü yeniden başladı) onu da yeniden kurar.
    /// </summary>
    private void StartVolumeWatchdog()
    {
        StopVolumeWatchdog();
        var forceVolume = _settings.ForceMaxVolume;
        var preferSpeakers = _settings.ForceBuiltInSpeakers;
        var target = (float)_settings.AlarmVolume;

        _volumeWatchdog = new Timer(_ =>
        {
            AudioThread.Post(() =>
            {
                _audioEnumerator ??= new MMDeviceEnumerator();
                var device = AudioOutputControl.Find(_audioEnumerator, _siren.DeviceId)
                             ?? AudioOutputControl.AlarmDevice(_audioEnumerator, preferSpeakers);
                if (device != null)
                {
                    AudioOutputControl.EnsureOwnSessionAudible(device);
                    if (forceVolume)
                    {
                        if (AudioOutputControl.IsMuted(device)) AudioOutputControl.SetMuted(device, false);
                        // Sesi yükseltene karışma, yalnızca kısılmışsa geri al.
                        if (AudioOutputControl.Volume(device) is { } current && current < target - 0.02f)
                            AudioOutputControl.SetVolume(device, target);
                    }
                }
            });
            _siren.EnsurePlaying();
        }, null, 1000, 1000);
    }

    private void StopVolumeWatchdog()
    {
        _volumeWatchdog?.Dispose();
        _volumeWatchdog = null;
    }

    private void StopAlarmOutputs()
    {
        StopVolumeWatchdog();
        _siren.Stop();
        _speech.Stop();
        AudioThread.Post(() =>
        {
            if (_previousVolume is { } v && _audioDeviceId != null)
            {
                _audioEnumerator ??= new MMDeviceEnumerator();
                var device = AudioOutputControl.Find(_audioEnumerator, _audioDeviceId);
                if (device != null)
                {
                    AudioOutputControl.SetVolume(device, v);
                    AudioOutputControl.SetMuted(device, _previousMuted);
                }
            }
            _previousVolume = null;
            _audioDeviceId = null;
        });
    }

    /// <summary>
    /// Sistem uykudan uyandığında alarmı kaldığı yerden sürdürür.
    /// Kapak kapandığında Windows uyur ve ses durur. Hırsız kapağı açtığı ya da
    /// bilgisayar herhangi bir sebeple uyandığı anda siren yeniden devreye girer.
    /// </summary>
    private void ResumeAfterWake()
    {
        if (State.Kind == GuardStateKind.Armed)
        {
            if (_settings.ShowLockScreen && !OverlayManager.LockScreen.IsVisible) OverlayManager.LockScreen.Show();
            return;
        }
        if (!State.IsAlarming && !State.IsWarning) return;

        _log.Log("Sistem uyandı — alarm sürdürülüyor", "", Glyphs.Bell, Severity.Alarm);

        ApplyAlarmAudio();

        // Uykudan sonra çıkış ölü olabilir; zorla yeniden kur.
        _siren.Stop();
        _siren.Start(State.IsAlarming ? SirenMode.Full : SirenMode.Warning, _settings.ForceBuiltInSpeakers);
        if (State.IsAlarming && _settings.SpeakWarning)
            _speech.StartRepeating(_settings.WarningText, _settings.ForceBuiltInSpeakers);

        _sleepBlocker.BeginDisplayAwake();
        if (LastTrigger is { } trigger) OverlayManager.Alarm.Show(trigger, warning: State.IsWarning);
        OverlayManager.Guardian.Reassert();
    }

    // MARK: - Telefona bildirim

    private async Task DispatchRemoteAlertAsync(TriggerEvent e)
    {
        var photo = _settings.CaptureIntruderPhoto ? CameraSensor.Shared.SnapshotJpeg() : null;

        // Kareyi önce diske yaz: telefon bildirimi kapalı olsa da kanıt kalsın.
        if (photo != null)
        {
            var saved = SnapshotStore.Save(photo, e.Kind, e.Date);
            _log.Log("Davetsiz misafir fotoğrafı kaydedildi",
                saved != null ? System.IO.Path.GetFileName(saved) : "kaydedilemedi", Glyphs.Camera, Severity.Warn);
        }
        else if (_settings.CaptureIntruderPhoto)
        {
            _log.Log("Fotoğraf çekilemedi", "Kamera kapalı ya da erişim yok", Glyphs.Camera, Severity.Warn);
        }

        if (!_settings.PushEnabled) return;
        var config = new NtfyClient.Config(_settings.PushServer, _settings.PushTopic);
        if (config.Url == null) return;

        var time = e.Date.ToString("HH:mm:ss");
        var text = await NtfyClient.SendAsync(config, "MacGuard alarmı!", $"{e.Message}\nSaat: {time}");
        NtfyClient.SendResult? photoResult = null;
        if (photo != null) photoResult = await NtfyClient.SendPhotoAsync(config, photo);

        // Başarısızlığı yutma: bildirim gitmediyse kullanıcı bunu bilmeli,
        // yoksa telefonunun haber vereceğini sanarak güvenir.
        if (text.IsSuccess && (photoResult?.IsSuccess ?? true))
        {
            _log.Log("Telefona bildirim gönderildi", "ntfy · " + _settings.PushTopic, Glyphs.Phone, Severity.Info);
        }
        else
        {
            var reason = text.IsSuccess ? (photoResult?.Message ?? "") : text.Message;
            _log.Log("Telefona bildirim GÖNDERİLEMEDİ", reason, Glyphs.Phone, Severity.Warn);
        }
    }

    // MARK: - Kapatma

    /// <summary>Koruma açıkken uygulamanın kapatılmasını engeller.</summary>
    public bool BlocksTermination => State.IsProtecting;

    /// <summary>Uygulama kapanırken sistemde iz bırakmamak için her şeyi geri al.</summary>
    public void PrepareForShutdown()
    {
        _warningTimer?.Stop();
        _warningTimer = null;
        StopAlarmOutputs();
        StopSensors();
        _sleepBlocker.EndAll();
        OverlayManager.Guardian.Stop();
        OverlayManager.Alarm.Hide();
        OverlayManager.LockScreen.Hide();
        KeyboardLockdown.Disable();
        Native.ShutdownBlockReasonDestroy(MessageWindow.Shared.Handle);
    }

    // MARK: - Kalibrasyon ve önizleme (koruma kapalıyken)

    /// <summary>
    /// Kamerayı açar, ölçümleri arayüze akıtır ama alarm çaldırmaz.
    /// Eşikleri kafedeki gerçek ortamda ayarlamak için.
    /// </summary>
    public async void StartCalibration()
    {
        if (State.Kind != GuardStateKind.Disarmed || IsCalibrating) return;
        if (!await CameraSensor.RequestAccessAsync())
        {
            LastMessage = "Kalibrasyon için kamera erişimi gerekiyor.";
            return;
        }
        if (State.Kind != GuardStateKind.Disarmed) return;
        var cam = CameraSensor.Shared;
        cam.SetAnalysis(motion: true, proximity: true, triggers: false);
        cam.OnTrigger = null;
        cam.Configure(_settings.MotionSensitivity, _settings.MotionCoverage, _settings.ProximityThreshold);
        cam.Start();
        IsCalibrating = true;
        LastMessage = "Kalibrasyon açık. Bilgisayarı oynat, çubuk sarı çizgiyi geçiyor mu bak.";
    }

    public void StopCalibration()
    {
        if (!IsCalibrating) return;
        IsCalibrating = false;
        CameraSensor.Shared.SetAnalysis(false, false, true);
        CameraSensor.Shared.Stop();
        LiveMotionScore = 0;
        LiveMotionCoverage = 0;
        LiveFaceHeight = 0;
        MotionWatchReady = false;
        ProximityWatchReady = false;
        LastMessage = "Kalibrasyon kapatıldı.";
    }

    /// <summary>Koruma ekranını korumayı başlatmadan gösterir.</summary>
    public async void PreviewLockScreen(double seconds = 10)
    {
        if (State.Kind != GuardStateKind.Disarmed || IsPreviewingLockScreen) return;
        IsPreviewingLockScreen = true;
        OverlayManager.LockScreen.Show(preview: true, onPreviewDismiss: EndLockScreenPreview);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        EndLockScreenPreview();
    }

    public void EndLockScreenPreview()
    {
        if (!IsPreviewingLockScreen) return;
        IsPreviewingLockScreen = false;
        OverlayManager.LockScreen.Hide();
    }

    /// <summary>
    /// Sireni birkaç saniye çalar. Sistem sesini değiştirmez —
    /// gerçek alarmın aksine burada sesi sen kontrol edersin.
    /// </summary>
    public async void PreviewSiren(double seconds = 3)
    {
        if (State.IsAlarming || IsPreviewingSiren) return;
        IsPreviewingSiren = true;
        _siren.Start();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        if (!State.ShowsOverlay) _siren.Stop();
        IsPreviewingSiren = false;
    }

    /// <summary>Açılışta çağrılır: kalan kapak ayarını arka planda kontrol eder.</summary>
    public async void CheckSleepLeftover()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var result = await Task.Run(SleepBlocker.RecoverFromCrashIfNeeded);
        await dispatcher.InvokeAsync(() =>
        {
            switch (result)
            {
                case SleepBlocker.LeftoverState.Recovered:
                    _log.Log("Kalan kapak ayarı geri alındı", "Önceki oturum düzgün kapanmamış",
                        Glyphs.Power, Severity.Warn);
                    break;
                case SleepBlocker.LeftoverState.NeedsAttention:
                    SleepLeftoverNeedsAttention = true;
                    _log.Log("Kapak ayarı açık kalmış", "Düzeltilmezse kapak kapanınca bilgisayar uyumaz",
                        Glyphs.Warning, Severity.Warn);
                    break;
            }
        });
    }

    /// <summary>Kullanıcı "Düzelt" dediğinde: gerekirse UAC sorulur.</summary>
    public void FixSleepLeftover()
    {
        if (!SleepBlocker.ClearLeftoverNow()) return;
        SleepLeftoverNeedsAttention = false;
        _log.Log("Kapak ayarı geri alındı", "", Glyphs.Check, Severity.Info);
    }

    /// <summary>Sadece test amaçlı: alarmı elle çaldırır.</summary>
    public void TestAlarm()
    {
        if (State.Kind != GuardStateKind.Armed)
        {
            LastMessage = "Test için önce korumayı başlat.";
            return;
        }
        var e = new TriggerEvent(TriggerKind.Motion, "Test alarmı");
        LastTrigger = e;
        _log.Log("Test alarmı", "", Glyphs.Bell, Severity.Warn);
        RaiseAlarm(e);
    }
}
