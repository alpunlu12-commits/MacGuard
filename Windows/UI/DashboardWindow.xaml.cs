using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MacGuard.Core;
using MacGuard.Security;
using MacGuard.Sensors;

namespace MacGuard.UI;

public partial class DashboardWindow : Window
{
    private readonly GuardEngine _engine = GuardEngine.Shared;
    private readonly Settings _settings = Settings.Shared;
    private readonly EventLog _log = EventLog.Shared;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<TriggerKind, SensorCard> _cards = new();
    private MeterRow? _motionMeter, _coverageMeter, _faceMeter;
    private TextBlock? _motionBadge, _proximityBadge, _cameraIdleNote;
    private Border? _meterDivider;
    private int _snapshotCount;
    private bool _pulsing;
    private bool _toldAboutTray;

    public DashboardWindow()
    {
        InitializeComponent();
        BuildSensorCards();
        BuildMeters();

        MainAction.Click += (_, _) => OnMainAction();
        SideAction.Click += (_, _) => OnSideAction();
        SettingsButton.Click += (_, _) => new SettingsWindow { Owner = this }.ShowDialog();
        FixLeftover.Click += (_, _) => _engine.FixSleepLeftover();
        FullVolume.Click += (_, _) => _settings.AlarmVolume = 1.0;
        CameraSettings.Click += (_, _) => CameraSensor.OpenSystemSettings();
        CameraRetry.Click += async (_, _) =>
        {
            CameraRetry.IsEnabled = false;
            await CameraSensor.RequestAccessAsync();
            CameraRetry.IsEnabled = true;
            Refresh();
        };
        PhotosButton.Click += (_, _) => SnapshotStore.RevealInExplorer();
        ClearLog.Click += (_, _) => _log.Clear();

        _engine.PropertyChanged += OnModelChanged;
        _settings.PropertyChanged += OnModelChanged;
        _log.Entries.CollectionChanged += (_, _) =>
        {
            RefreshLog();
            RefreshSnapshotCount();
        };
        _clock.Tick += (_, _) => RefreshHeader();
        SizeChanged += (_, _) => SensorGrid.Columns = Math.Max(1, (int)((ActualWidth - 60) / 270));

        Loaded += (_, _) =>
        {
            _clock.Start();
            Refresh();
            RefreshLog();
            RefreshSnapshotCount();
            if (!PinStore.IsConfigured) Dialogs.ShowPinSetup(this);
        };

        // Pencereyi kapatmak uygulamayı kapatmaz: MacGuard bildirim alanında çalışmaya devam eder.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            if (!_toldAboutTray)
            {
                _toldAboutTray = true;
                Platform.TrayIcon.ShowBalloon("MacGuard çalışmaya devam ediyor",
                    "Bildirim alanındaki kalkan simgesinden açabilir ya da çıkabilirsin.");
            }
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private bool UsesCamera => _settings.IsEnabled(TriggerKind.Motion) || _settings.IsEnabled(TriggerKind.Proximity);

    // MARK: - Yenileme

    private void Refresh()
    {
        RefreshHeader();
        RefreshActions();

        var cameraBlocked = CameraSensor.Authorization is CameraAccess.Denied or CameraAccess.NoDevice;
        CameraBanner.Visibility = UsesCamera && cameraBlocked ? Visibility.Visible : Visibility.Collapsed;
        CameraStatusText.Text = "Kamera kullanılamıyor — durum: " + CameraSensor.AuthorizationDescription;

        LeftoverBar.Visibility = _engine.SleepLeftoverNeedsAttention ? Visibility.Visible : Visibility.Collapsed;

        var testLevel = _settings.ForceMaxVolume && _settings.AlarmVolume < 0.99;
        TestVolumeBar.Visibility = testLevel ? Visibility.Visible : Visibility.Collapsed;
        TestVolumeText.Text = $"Alarm sesi test seviyesinde: %{(int)Math.Round(_settings.AlarmVolume * 100)}";

        MessageBar.Visibility = string.IsNullOrEmpty(_engine.LastMessage) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = _engine.LastMessage;

        foreach (var card in _cards.Values) card.Refresh(cameraBlocked);
        RefreshMeters();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void RefreshHeader()
    {
        var s = _engine.State;
        var tintKey = s.Kind switch
        {
            GuardStateKind.Disarmed => "IdleColor",
            GuardStateKind.Arming => "ArmingColor",
            GuardStateKind.Armed => "SafeColor",
            GuardStateKind.Warning => "ArmingColor",
            _ => "DangerColor",
        };
        var tint = (Color)FindResource(tintKey);
        HaloFill.Fill = new SolidColorBrush(Color.FromArgb(36, tint.R, tint.G, tint.B));
        Ring.Stroke = new SolidColorBrush(tint);
        StatusIcon.Foreground = new SolidColorBrush(tint);
        StatusIcon.Text = s.Kind switch
        {
            GuardStateKind.Arming => Glyphs.Hourglass,
            GuardStateKind.Warning => Glyphs.Error,
            GuardStateKind.Alarming => Glyphs.Warning,
            _ => Glyphs.Shield,
        };
        Headline.Text = s.Kind switch
        {
            GuardStateKind.Disarmed => "Koruma kapalı",
            GuardStateKind.Arming => $"{s.Remaining} saniye…",
            GuardStateKind.Armed => "Koruma aktif",
            GuardStateKind.Warning => $"Uyarı — {s.Remaining} sn",
            _ => "ALARM!",
        };
        Subline.Text = s.Kind switch
        {
            GuardStateKind.Disarmed => "Bilgisayarın şu anda korunmuyor.",
            GuardStateKind.Arming => "Çantanı al, kalk — sensörler geri sayım bitince devreye girecek.",
            GuardStateKind.Armed => _engine.ArmedSince is { } since && (int)(DateTime.Now - since).TotalMinutes is var m && m > 0
                ? $"{m} dakikadır nöbetteyim."
                : "Nöbet başladı.",
            GuardStateKind.Warning => "PIN girersen alarm çalmayacak.",
            _ => _engine.LastTrigger?.Message ?? s.Reason.Title(),
        };

        SetPulse(s.IsProtecting);
    }

    private void SetPulse(bool on)
    {
        if (on == _pulsing) return;
        _pulsing = on;
        if (on)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var dur = TimeSpan.FromSeconds(0.9);
            Ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 0.7, dur)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            var scale = new DoubleAnimation(1, 1.08, dur)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            RingScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            RingScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }
        else
        {
            Ring.BeginAnimation(OpacityProperty, null);
            RingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Ring.Opacity = 0.25;
        }
    }

    // MARK: - Eylem düğmeleri

    private void RefreshActions()
    {
        var s = _engine.State;
        MainAction.Visibility = Visibility.Visible;
        ActionNote.Visibility = Visibility.Collapsed;
        SideAction.Visibility = Visibility.Collapsed;
        SettingsButton.IsEnabled = !s.IsAlarming;

        switch (s.Kind)
        {
            case GuardStateKind.Disarmed:
                MainAction.Content = Label(Glyphs.Shield, "KORUMAYI BAŞLAT");
                MainAction.Background = Res("Safe");
                if (UsesCamera)
                {
                    SideAction.Visibility = Visibility.Visible;
                    SideAction.Content = _engine.IsCalibrating
                        ? Label(Glyphs.Cancel, "Kalibrasyonu Bitir")
                        : Label(Glyphs.Camera, "Kamerayı Ayarla");
                    SideAction.Background = _engine.IsCalibrating ? Res("Arming") : Res("SurfaceHi");
                }
                break;
            case GuardStateKind.Arming:
                MainAction.Content = Label(Glyphs.Cancel, "GERİ SAYIMI İPTAL ET");
                MainAction.Background = Res("Arming");
                break;
            case GuardStateKind.Armed:
                MainAction.Content = Label(Glyphs.Unlock, "KORUMAYI KAPAT");
                MainAction.Background = Res("Idle");
                SideAction.Visibility = Visibility.Visible;
                SideAction.Content = Label(Glyphs.Bell, "Alarmı Test Et");
                SideAction.Background = new SolidColorBrush(Color.FromArgb(204, 0xEF, 0x44, 0x44));
                break;
            case GuardStateKind.Warning:
                MainAction.Visibility = Visibility.Collapsed;
                ActionNote.Visibility = Visibility.Visible;
                ActionNote.Text = "Uyarı aşaması — ekrandaki PIN'i gir, alarm çalmasın";
                ActionNote.Foreground = Res("Arming");
                break;
            case GuardStateKind.Alarming:
                MainAction.Visibility = Visibility.Collapsed;
                ActionNote.Visibility = Visibility.Visible;
                ActionNote.Text = "Alarm çalıyor — durdurmak için ekrandaki PIN'i gir";
                ActionNote.Foreground = Res("Danger");
                break;
        }
    }

    private static StackPanel Label(string glyph, string text)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        p.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    private void OnMainAction()
    {
        switch (_engine.State.Kind)
        {
            case GuardStateKind.Disarmed:
                if (!PinStore.IsConfigured)
                {
                    Dialogs.ShowPinSetup(this);
                    if (!PinStore.IsConfigured) return;
                }
                _engine.Arm();
                break;
            case GuardStateKind.Arming:
                _engine.CancelArming();
                break;
            case GuardStateKind.Armed:
                Dialogs.ShowDisarm(this);
                break;
        }
    }

    private void OnSideAction()
    {
        switch (_engine.State.Kind)
        {
            case GuardStateKind.Disarmed:
                if (_engine.IsCalibrating) _engine.StopCalibration();
                else _engine.StartCalibration();
                break;
            case GuardStateKind.Armed:
                _engine.TestAlarm();
                break;
        }
    }

    // MARK: - Sensör kartları

    private void BuildSensorCards()
    {
        foreach (var kind in TriggerKinds.All)
        {
            var card = new SensorCard(kind, this);
            _cards[kind] = card;
            SensorGrid.Children.Add(card.Root);
        }
    }

    /// <summary>Bir tetikleyicinin kartı: simge, açıklama, nöbet rozeti ve anahtar.</summary>
    private sealed class SensorCard
    {
        public Border Root { get; }
        private readonly TriggerKind _kind;
        private readonly DashboardWindow _w;
        private readonly TextBlock _icon;
        private readonly Border _iconBg;
        private readonly TextBlock _detail;
        private readonly TextBlock _cameraNote;
        private readonly TextBlock _watch;
        private readonly CheckBox _switch;
        private bool _updating;

        public SensorCard(TriggerKind kind, DashboardWindow w)
        {
            _kind = kind;
            _w = w;
            _icon = new TextBlock
            {
                Text = kind.Glyph(),
                Style = (Style)w.FindResource("Icon"),
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _iconBg = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = _icon };

            var title = new TextBlock { Text = kind.Title(), FontSize = 14, FontWeight = FontWeights.SemiBold };
            _detail = new TextBlock { Style = (Style)w.FindResource("Secondary"), Margin = new Thickness(0, 4, 0, 0) };
            _cameraNote = new TextBlock
            {
                Text = "Kamera erişimi gerekli",
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)w.FindResource("Arming"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            _watch = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };

            var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
            texts.Children.Add(title);
            texts.Children.Add(_detail);
            texts.Children.Add(_cameraNote);
            texts.Children.Add(_watch);

            _switch = new CheckBox { Style = (Style)w.FindResource("Switch"), VerticalAlignment = VerticalAlignment.Top };
            _switch.Checked += (_, _) => Toggle(true);
            _switch.Unchecked += (_, _) => Toggle(false);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(_iconBg);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(_switch, 2);
            grid.Children.Add(_switch);
            _iconBg.VerticalAlignment = VerticalAlignment.Top;

            Root = new Border { Padding = new Thickness(14), Margin = new Thickness(6), Child = grid };
        }

        private void Toggle(bool on)
        {
            if (_updating) return;
            Settings.Shared.SetEnabled(_kind, on);
        }

        private bool IsUnsupported => _kind == TriggerKind.Clamshell && !LidSensor.IsSupported;

        public void Refresh(bool cameraBlocked)
        {
            var engine = GuardEngine.Shared;
            var isOn = Settings.Shared.IsEnabled(_kind);
            Root.Style = (Style)_w.FindResource(isOn ? "CardHi" : "Card");
            Root.Opacity = IsUnsupported ? 0.5 : 1;

            var tint = (Color)_w.FindResource(isOn ? "SafeColor" : "IdleColor");
            _icon.Foreground = new SolidColorBrush(tint);
            _iconBg.Background = new SolidColorBrush(Color.FromArgb(31, tint.R, tint.G, tint.B));
            _detail.Text = IsUnsupported ? "Bu bilgisayarda kapak yok (pil bulunamadı)." : _kind.Detail();
            _cameraNote.Visibility = _kind.NeedsCamera() && cameraBlocked ? Visibility.Visible : Visibility.Collapsed;

            // Sakinleşme bekleyen sensörler için nöbet durumu.
            bool? ready = null;
            if (isOn && engine.State.IsProtecting)
            {
                ready = _kind switch
                {
                    TriggerKind.Motion => engine.MotionWatchReady,
                    TriggerKind.Proximity => engine.ProximityWatchReady,
                    TriggerKind.Input => engine.InputWatchReady,
                    _ => null,
                };
            }
            _watch.Visibility = ready.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (ready.HasValue)
            {
                _watch.Text = ready.Value ? "Nöbette" : "Sakinleşme bekleniyor";
                _watch.Foreground = (Brush)_w.FindResource(ready.Value ? "Safe" : "Arming");
            }

            _updating = true;
            _switch.IsChecked = isOn;
            _updating = false;
            _switch.IsEnabled = !engine.State.IsProtecting && !IsUnsupported;
        }
    }

    // MARK: - Canlı ölçüm

    private void BuildMeters()
    {
        _motionBadge = Badge();
        _motionMeter = new MeterRow("Değişimin şiddeti", v => v.ToString("0.000"), this);
        _coverageMeter = new MeterRow("Kareye yayılma", v => $"%{v * 100:0}", this);
        _meterDivider = new Border { Height = 1, Background = Res("Stroke"), Margin = new Thickness(0, 4, 0, 8) };
        _proximityBadge = Badge();
        _faceMeter = new MeterRow("Yüz büyüklüğü", v => v.ToString("0.00"), this);
        _cameraIdleNote = new TextBlock
        {
            Text = "Kamera yalnızca koruma veya kalibrasyon açıkken çalışır.",
            Style = (Style)FindResource("Secondary"),
        };

        MetersPanel.Children.Add(_motionBadge);
        MetersPanel.Children.Add(_motionMeter.Root);
        MetersPanel.Children.Add(_coverageMeter.Root);
        MetersPanel.Children.Add(_meterDivider);
        MetersPanel.Children.Add(_proximityBadge);
        MetersPanel.Children.Add(_faceMeter.Root);
        MetersPanel.Children.Add(_cameraIdleNote);
    }

    private static TextBlock Badge() => new() { FontSize = 11.5, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 10) };

    private void RefreshMeters()
    {
        var show = UsesCamera && (_engine.IsCalibrating || _engine.State.IsProtecting);
        MetersSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var motion = _settings.IsEnabled(TriggerKind.Motion);
        var prox = _settings.IsEnabled(TriggerKind.Proximity);
        var vis = (bool b) => b ? Visibility.Visible : Visibility.Collapsed;

        _motionBadge!.Visibility = vis(motion);
        _motionMeter!.Root.Visibility = vis(motion);
        _coverageMeter!.Root.Visibility = vis(motion);
        _meterDivider!.Visibility = vis(motion && prox);
        _proximityBadge!.Visibility = vis(prox);
        _faceMeter!.Root.Visibility = vis(prox);
        _cameraIdleNote!.Visibility = vis(!CameraSensor.Shared.IsRunning);

        SetBadge(_motionBadge, _engine.MotionWatchReady, "Hareket nöbette", "Hareket: sahne sakinleşmesi bekleniyor");
        SetBadge(_proximityBadge, _engine.ProximityWatchReady, "Yakınlık nöbette", "Yakınlık: kadrajın boşalması bekleniyor");

        _motionMeter.Update(_engine.LiveMotionScore, _settings.MotionSensitivity, Math.Max(_settings.MotionSensitivity * 3, 0.12));
        _coverageMeter.Update(_engine.LiveMotionCoverage, _settings.MotionCoverage, 1.0);
        _faceMeter.Update(_engine.LiveFaceHeight, _settings.ProximityThreshold, 1.0);
    }

    private void SetBadge(TextBlock t, bool ready, string readyText, string waitingText)
    {
        t.Text = (ready ? Glyphs.Eye : Glyphs.Hourglass) + "  " + (ready ? readyText : waitingText);
        t.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets");
        t.Foreground = Res(ready ? "Safe" : "Arming");
    }

    /// <summary>Değer çubuğu + sarı eşik çizgisi.</summary>
    private sealed class MeterRow
    {
        public StackPanel Root { get; } = new() { Margin = new Thickness(0, 0, 0, 14) };
        private readonly TextBlock _value = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly Func<double, string> _format;
        private readonly ColumnDefinition _fill = new(), _rest = new();
        private readonly ColumnDefinition _thr = new(), _thrRest = new();
        private readonly Border _bar = new() { CornerRadius = new CornerRadius(4) };
        private readonly DashboardWindow _w;

        public MeterRow(string label, Func<double, string> format, DashboardWindow w)
        {
            _format = format;
            _w = w;
            _value.FontFamily = (FontFamily)w.FindResource("MonoFont");

            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = (Brush)w.FindResource("TextSecondary") });
            head.Children.Add(_value);

            var bars = new Grid { Height = 8 };
            bars.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) });

            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(_fill);
            fillGrid.ColumnDefinitions.Add(_rest);
            fillGrid.Children.Add(_bar);
            bars.Children.Add(fillGrid);

            var thrGrid = new Grid();
            thrGrid.ColumnDefinitions.Add(_thr);
            thrGrid.ColumnDefinitions.Add(_thrRest);
            var line = new Border { Width = 2, Background = (Brush)w.FindResource("Arming"), HorizontalAlignment = HorizontalAlignment.Right };
            thrGrid.Children.Add(line);
            bars.Children.Add(thrGrid);

            Root.Children.Add(head);
            Root.Children.Add(bars);
        }

        public void Update(double value, double threshold, double max)
        {
            var f = Math.Clamp(value / max, 0, 1);
            var t = Math.Clamp(threshold / max, 0, 1);
            var over = value >= threshold;
            _fill.Width = new GridLength(Math.Max(f, 0.0001), GridUnitType.Star);
            _rest.Width = new GridLength(Math.Max(1 - f, 0.0001), GridUnitType.Star);
            _thr.Width = new GridLength(Math.Max(t, 0.0001), GridUnitType.Star);
            _thrRest.Width = new GridLength(Math.Max(1 - t, 0.0001), GridUnitType.Star);
            _bar.Background = (Brush)_w.FindResource(over ? "Danger" : "Safe");
            _value.Text = _format(value);
            _value.Foreground = (Brush)_w.FindResource(over ? "Danger" : "TextPrimary");
        }
    }

    // MARK: - Olay kaydı

    private void RefreshLog()
    {
        var entries = _log.Entries.Take(12).ToList();
        EmptyLog.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogCard.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearLog.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        LogList.Children.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            LogList.Children.Add(LogRow(entries[i]));
            if (i < entries.Count - 1)
                LogList.Children.Add(new Border { Height = 1, Background = Res("Stroke") });
        }
    }

    private Grid LogRow(LogEntry e)
    {
        var tint = Res(e.Severity switch { Severity.Warn => "Arming", Severity.Alarm => "Danger", _ => "Idle" });
        var g = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        g.Children.Add(new TextBlock { Text = e.Icon, Style = (Style)FindResource("Icon"), FontSize = 13, Foreground = tint });

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = e.Title, FontSize = 13, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
        if (e.HasDetail)
            texts.Children.Add(new TextBlock { Text = e.Detail, Style = (Style)FindResource("Secondary"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);

        var time = new TextBlock
        {
            Text = e.Time,
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            Foreground = Res("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(time, 2);
        g.Children.Add(time);
        return g;
    }

    /// <summary>Dosya sistemine bakar; arayüz iş parçacığını meşgul etmeyelim.</summary>
    private async void RefreshSnapshotCount()
    {
        _snapshotCount = await Task.Run(() => SnapshotStore.Count);
        PhotosButton.Visibility = _snapshotCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PhotosButton.Content = Label(Glyphs.Photo, $"{_snapshotCount} fotoğraf");
    }
}
