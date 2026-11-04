using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MacGuard.Core;

namespace MacGuard.UI;

/// <summary>Alarm anında tüm ekranları kaplayan kırmızı perde. Tuş takımı HER ekranda.</summary>
public partial class AlarmWindow : Window, IOverlayWindow
{
    private int _elapsed;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly GuardEngine _engine = GuardEngine.Shared;

    public bool AllowClose { get; set; }
    public PinPad PinPad => Pad;

    /// <param name="warning">true iken tam alarm değil, PIN için süre tanıyan uyarı aşaması.</param>
    public AlarmWindow(TriggerEvent reason, bool warning)
    {
        InitializeComponent();

        var tint = (Color)FindResource(warning ? "ArmingColor" : "DangerColor");
        TintStop.Color = Color.FromArgb(140, tint.R, tint.G, tint.B);
        BigIcon.Text = warning ? Glyphs.Error : Glyphs.Warning;
        BigIcon.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = tint, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.9,
        };
        Headline.Text = warning ? "UYARI" : "ALARM";
        ReasonIcon.Text = reason.Kind.Glyph();
        ReasonText.Text = reason.Message;

        Pad.Title = warning ? "Alarmı önlemek için PIN gir" : "Durdurmak için PIN gir";
        Pad.OnSubmit = pin => _engine.Disarm(pin);

        _tick.Tick += (_, _) => { _elapsed++; UpdateTexts(); };
        Loaded += (_, _) =>
        {
            _tick.Start();
            _engine.PropertyChanged += OnEngineChanged;
            UpdateTexts();
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _engine.PropertyChanged -= OnEngineChanged;
        };
        Closing += (_, e) => e.Cancel = !AllowClose;
        // Pencere etkinleşince klavye doğrudan tuş takımına gitsin.
        Activated += (_, _) => System.Windows.Input.Keyboard.Focus(Pad);
    }

    private void OnEngineChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuardEngine.State)) UpdateTexts();
    }

    private void UpdateTexts()
    {
        var state = _engine.State;
        if (state.IsWarning)
        {
            Countdown.Text = $"{state.Remaining} saniye içinde PIN girilmezse alarm çalacak";
            Countdown.Visibility = Visibility.Visible;
            Elapsed.Visibility = Visibility.Collapsed;
        }
        else
        {
            Countdown.Visibility = Visibility.Collapsed;
            Elapsed.Text = $"Bu bilgisayar korumalıdır · {_elapsed} sn";
            Elapsed.Visibility = Visibility.Visible;
        }
    }
}
