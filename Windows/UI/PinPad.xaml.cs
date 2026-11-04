using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacGuard.Core;

namespace MacGuard.UI;

/// <summary>Tuş takımı. Hem ekrandaki düğmelerden hem fiziksel klavyeden giriş kabul eder.</summary>
public partial class PinPad : UserControl
{
    /// <summary>PIN dolunca çağrılır; doğruysa true döndürmeli.</summary>
    public Func<string, bool>? OnSubmit { get; set; }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    private string _pin = "";
    private string? _error;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly GuardEngine _engine = GuardEngine.Shared;

    public PinPad()
    {
        InitializeComponent();
        BuildKeys();
        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _tick.Start();
            _engine.PropertyChanged += OnEngineChanged;
            Refresh();
            Focus();
        };
        Unloaded += (_, _) =>
        {
            _tick.Stop();
            _engine.PropertyChanged -= OnEngineChanged;
        };
        PreviewMouseDown += (_, _) => Focus();
        Refresh();
    }

    private void OnEngineChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuardEngine.LockoutUntil)) Refresh();
    }

    private int LockoutRemaining =>
        _engine.LockoutUntil is { } until ? Math.Max(0, (int)Math.Ceiling((until - DateTime.Now).TotalSeconds)) : 0;

    private bool IsLockedOut => LockoutRemaining > 0;

    private void BuildKeys()
    {
        for (var n = 1; n <= 9; n++) Keys.Children.Add(DigitKey(n.ToString()));
        Keys.Children.Add(IconKey(Glyphs.Backspace, Backspace));
        Keys.Children.Add(DigitKey("0"));
        Keys.Children.Add(IconKey(Glyphs.Check, Submit));
    }

    private Button DigitKey(string label)
    {
        var b = new Button { Content = label, Style = (Style)FindResource("KeypadButton") };
        b.Click += (_, _) => Append(label);
        return b;
    }

    private Button IconKey(string glyph, Action action)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 19 },
            Style = (Style)FindResource("KeypadButton"),
        };
        b.Click += (_, _) => action();
        return b;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsLockedOut) { e.Handled = true; return; }
        if (e.Key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            Append(((int)(e.Key - Key.D0)).ToString());
            e.Handled = true;
        }
        else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            Append(((int)(e.Key - Key.NumPad0)).ToString());
            e.Handled = true;
        }
        else if (e.Key is Key.Back or Key.Delete)
        {
            Backspace();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void Append(string digit)
    {
        if (IsLockedOut || _pin.Length >= 12) return;
        _error = null;
        _pin += digit;
        Refresh();
        // 4 hanede otomatik dene; daha uzun PIN'ler için onay tuşu var.
        if (_pin.Length == 4) Submit();
    }

    private void Backspace()
    {
        _error = null;
        if (_pin.Length > 0) _pin = _pin[..^1];
        Refresh();
    }

    private void Submit()
    {
        if (_pin.Length == 0 || IsLockedOut) return;
        var ok = OnSubmit?.Invoke(_pin) ?? false;
        _pin = "";
        _error = ok ? null : "Hatalı PIN";
        Refresh();
        if (!ok) ShakeDots();
    }

    private void ShakeDots()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        double[] xs = { -12, 10, -8, 6, -3, 0 };
        for (var i = 0; i < xs.Length; i++)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60 * (i + 1)))));
        Shake.BeginAnimation(TranslateTransform.XProperty, a);
    }

    private void Refresh()
    {
        Dots.Items.Clear();
        var count = Math.Max(4, _pin.Length);
        for (var i = 0; i < count; i++)
        {
            Dots.Items.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(7, 0, 7, 0),
                Fill = i < _pin.Length ? Brushes.White : new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)),
            });
        }

        var locked = LockoutRemaining;
        if (locked > 0)
        {
            StatusRow.Visibility = Visibility.Visible;
            StatusIcon.Text = Glyphs.Hourglass;
            StatusText.Text = $"Çok fazla hatalı deneme — {locked} sn bekle";
            StatusIcon.Foreground = StatusText.Foreground = (Brush)FindResource("Arming");
        }
        else if (_error != null)
        {
            StatusRow.Visibility = Visibility.Visible;
            StatusIcon.Text = Glyphs.Error;
            StatusText.Text = _error;
            StatusIcon.Foreground = StatusText.Foreground = (Brush)FindResource("Danger");
        }
        else
        {
            StatusRow.Visibility = Visibility.Collapsed;
        }

        Keys.IsEnabled = locked == 0;
        Keys.Opacity = locked > 0 ? 0.45 : 1;
    }
}
