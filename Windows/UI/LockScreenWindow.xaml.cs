using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MacGuard.Core;

namespace MacGuard.UI;

/// <summary>
/// Koruma aktifken tüm ekranları kaplayan caydırıcı bilgi ekranı.
///
/// Amaç alarm çalmadan **önce** iş görmek: masaya yaklaşan kişi bilgisayara
/// dokunmadan durumu okusun ve vazgeçsin. Bu yüzden kırmızı/panik değil,
/// sakin ve okunur bir tasarım. Tuş takımı her ekranda var.
/// </summary>
public partial class LockScreenWindow : Window, IOverlayWindow
{
    private readonly bool _isPreview;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public bool AllowClose { get; set; }
    public PinPad PinPad => Pad;

    public LockScreenWindow(bool isPreview, Action? onPreviewDismiss)
    {
        InitializeComponent();
        _isPreview = isPreview;

        var settings = Settings.Shared;
        LockText.Text = settings.LockScreenText;
        Signature.Text = AppInfo.SignatureLine;

        var active = TriggerKinds.All.Where(settings.IsEnabled).ToList();
        SensorsPanel.Visibility = active.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var kind in active) SensorChips.Children.Add(Chip(kind));

        Pad.Title = isPreview ? "PIN alanı (önizleme)" : "Korumayı kaldırmak için PIN gir";
        Pad.OnSubmit = pin =>
        {
            if (isPreview)
            {
                onPreviewDismiss?.Invoke();
                return true;
            }
            return GuardEngine.Shared.Disarm(pin);
        };

        PreviewBadge.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        ClosePreview.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        ClosePreview.Click += (_, _) => onPreviewDismiss?.Invoke();

        _tick.Tick += (_, _) => UpdateGuarded();
        Loaded += (_, _) => { _tick.Start(); UpdateGuarded(); };
        Closed += (_, _) => _tick.Stop();
        Closing += (_, e) => e.Cancel = !AllowClose;
        // Pencere etkinleşince klavye doğrudan tuş takımına gitsin.
        Activated += (_, _) => System.Windows.Input.Keyboard.Focus(Pad);
    }

    private static Border Chip(TriggerKind kind)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = kind.Glyph(),
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = kind.Title(),
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromArgb(191, 255, 255, 255)),
        });
        return new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 10, 10),
        };
    }

    private void UpdateGuarded()
    {
        if (_isPreview || GuardEngine.Shared.ArmedSince is not { } since)
        {
            GuardedRow.Visibility = Visibility.Collapsed;
            return;
        }
        var seconds = (int)(DateTime.Now - since).TotalSeconds;
        int h = seconds / 3600, m = seconds % 3600 / 60, s = seconds % 60;
        var text = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        GuardedText.Text = $"{text} süredir nöbette";
        GuardedRow.Visibility = Visibility.Visible;
    }
}
