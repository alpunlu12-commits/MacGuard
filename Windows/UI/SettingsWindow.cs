using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MacGuard.Core;
using MacGuard.Notify;
using MacGuard.Platform;
using MacGuard.Security;
using MacGuard.Sensors;

namespace MacGuard.UI;

/// <summary>
/// Ayarlar penceresi. macOS sürümündeki SettingsView'ın bölümleri birebir:
/// hassasiyet, zamanlama, alarm, koruma ekranı, telefona bildirim, güvenlik,
/// sistem, hakkında ve güvenlik PIN'i (acil çıkış).
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings _settings = Settings.Shared;
    private readonly GuardEngine _engine = GuardEngine.Shared;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly BooleanToVisibilityConverter _vis = new();

    // Durumla değişen parçalar
    private TextBlock _volumeValue = null!;
    private TextBlock _testLevelWarning = null!;
    private TextBlock _effectiveUrl = null!;
    private TextBlock _effectiveIcon = null!;
    private StackPanel _effectiveRow = null!;
    private TextBlock _invalidTopic = null!;
    private Button _testPush = null!;
    private TextBlock _pushResult = null!;
    private TextBlock _pinStatus = null!;
    private Button _pinButton = null!;
    private Button _sirenPreview = null!;
    private Button _lockPreview = null!;
    private StackPanel _alarmSection = null!;
    private PasswordBox _emergencyPin = null!;
    private Button _emergencyButton = null!;
    private TextBlock _emergencyStatus = null!;
    private string? _emergencyError;
    private bool _emergencyDone;

    public SettingsWindow()
    {
        Title = "MacGuard — Ayarlar";
        Width = 580;
        Height = 720;
        MinWidth = 520;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Res("Background");
        Foreground = Res("TextPrimary");
        FontFamily = (FontFamily)FindResource("UiFont");
        FontSize = 13;

        var root = new DockPanel();

        var header = new Grid { Margin = new Thickness(20) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(GlyphText(Glyphs.Settings, 17, Res("TextPrimary"), new Thickness(0, 0, 10, 0)));
        title.Children.Add(new TextBlock { Text = "Ayarlar", FontSize = 18, FontWeight = FontWeights.Bold });
        header.Children.Add(title);
        var done = new Button
        {
            Content = "Bitti",
            Style = (Style)FindResource("SmallButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0),
            IsDefault = true,
            IsCancel = true,
        };
        done.Click += (_, _) => Close();
        header.Children.Add(done);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var divider = new Border { Height = 1, Background = Res("Stroke") };
        DockPanel.SetDock(divider, Dock.Top);
        root.Children.Add(divider);

        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(SensitivitySection());
        body.Children.Add(TimingSection());
        body.Children.Add(_alarmSection = AlarmSection());
        body.Children.Add(LockScreenSection());
        body.Children.Add(PushSection());
        body.Children.Add(SecuritySection());
        body.Children.Add(SystemSection());
        body.Children.Add(AboutSection());
        body.Children.Add(EmergencyPinSection());
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Content = root;

        _settings.PropertyChanged += OnChanged;
        _engine.PropertyChanged += OnChanged;
        _clock.Tick += (_, _) => RefreshEmergency();
        Loaded += (_, _) => { _clock.Start(); Refresh(); };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _settings.PropertyChanged -= OnChanged;
            _engine.PropertyChanged -= OnChanged;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _settings && e.PropertyName is nameof(Settings.MotionSensitivity)
                or nameof(Settings.MotionCoverage) or nameof(Settings.ProximityThreshold))
        {
            CameraSensor.Shared.Configure(_settings.MotionSensitivity, _settings.MotionCoverage,
                _settings.ProximityThreshold);
        }
        Refresh();
    }

    private void Refresh()
    {
        var protecting = _engine.State.IsProtecting;

        _volumeValue.Text = $"%{(int)Math.Round(_settings.AlarmVolume * 100)}";
        _volumeValue.Foreground = Res(_settings.AlarmVolume < 0.99 ? "Arming" : "TextSecondary");
        _testLevelWarning.Visibility = _settings.AlarmVolume < 0.99 ? Visibility.Visible : Visibility.Collapsed;

        var cfg = new NtfyClient.Config(_settings.PushServer, _settings.PushTopic);
        var url = cfg.Url;
        _effectiveUrl.Text = url?.ToString() ?? "Adres kurulamadı — sunucu ya da konu adı hatalı";
        _effectiveIcon.Text = url != null ? Glyphs.Send : Glyphs.Error;
        _effectiveUrl.Foreground = _effectiveIcon.Foreground = Res(url != null ? "Safe" : "Danger");
        _invalidTopic.Visibility = _settings.PushTopic.Trim().Length > 0 && !NtfyClient.IsValidTopic(cfg.NormalizedTopic)
            ? Visibility.Visible : Visibility.Collapsed;
        _testPush.IsEnabled = url != null;

        _pinStatus.Text = PinStore.IsConfigured ? "PIN tanımlı" : "PIN tanımlı değil";
        _pinStatus.Foreground = Res(PinStore.IsConfigured ? "Safe" : "Danger");
        _pinButton.Content = PinStore.IsConfigured ? "PIN'i değiştir" : "PIN belirle";
        _pinButton.IsEnabled = !protecting;

        _sirenPreview.Content = _engine.IsPreviewingSiren ? "Çalıyor…" : "Sireni 3 saniye dinle";
        _sirenPreview.IsEnabled = !_engine.IsPreviewingSiren;
        _lockPreview.IsEnabled = !protecting && !_engine.IsPreviewingLockScreen;
        _alarmSection.IsEnabled = !_engine.State.IsAlarming;

        RefreshEmergency();
    }

    // MARK: - Yapı taşları

    private Brush Res(string key) => (Brush)FindResource(key);

    private TextBlock GlyphText(string glyph, double size, Brush brush, Thickness margin) => new()
    {
        Text = glyph,
        Style = (Style)FindResource("Icon"),
        FontSize = size,
        Foreground = brush,
        Margin = margin,
    };

    private StackPanel Section(string title, string glyph, params UIElement[] content)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(GlyphText(glyph, 13, Res("TextPrimary"), new Thickness(0, 0, 8, 0)));
        head.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        outer.Children.Add(head);

        var inner = new StackPanel();
        foreach (var c in content)
        {
            if (c is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 0, 12);
            inner.Children.Add(c);
        }
        if (inner.Children.Count > 0 && inner.Children[^1] is FrameworkElement last) last.Margin = new Thickness(0);
        outer.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14), Child = inner });
        return outer;
    }

    private TextBlock Note(string text, double size = 11) => new()
    {
        Text = text,
        Style = (Style)FindResource("Secondary"),
        FontSize = size,
    };

    private Border Divider() => new() { Height = 1, Background = Res("Stroke"), Margin = new Thickness(0, 2, 0, 14) };

    private Binding Bind(string path) => new(path) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

    private void ShowWhen(FrameworkElement e, string boolPath) =>
        e.SetBinding(VisibilityProperty, new Binding(boolPath) { Source = _settings, Converter = _vis });

    private CheckBox Switch(string label, string path)
    {
        var c = new CheckBox { Style = (Style)FindResource("Switch"), Content = label };
        c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(path));
        return c;
    }

    private StackPanel SliderRow(string label, string path, double min, double max, string format, out TextBlock valueText)
    {
        var p = new StackPanel();
        var head = new Grid();
        head.Children.Add(new TextBlock { Text = label });
        valueText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            Foreground = Res("TextSecondary"),
        };
        if (format.Length > 0)
            valueText.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = _settings, StringFormat = format, ConverterCulture = CultureInfo.InvariantCulture });
        head.Children.Add(valueText);
        p.Children.Add(head);
        var slider = new Slider { Minimum = min, Maximum = max, Margin = new Thickness(0, 6, 0, 0) };
        slider.SetBinding(Slider.ValueProperty, Bind(path));
        p.Children.Add(slider);
        return p;
    }

    /// <summary>macOS'taki Stepper karşılığı: etiket, değer ve − / + düğmeleri.</summary>
    private Grid Stepper(string label, Func<int> get, Action<int> set, int min, int max, Func<int, string> format)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        var value = new TextBlock
        {
            Foreground = Res("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(value, 1);
        g.Children.Add(value);

        var minus = new Button { Content = "−", Style = (Style)FindResource("SmallButton"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 4, 0) };
        var plus = new Button { Content = "+", Style = (Style)FindResource("SmallButton"), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0) };
        Grid.SetColumn(minus, 2);
        Grid.SetColumn(plus, 3);
        g.Children.Add(minus);
        g.Children.Add(plus);

        void Update()
        {
            value.Text = format(get());
            minus.IsEnabled = get() > min;
            plus.IsEnabled = get() < max;
        }
        minus.Click += (_, _) => { set(Math.Max(min, get() - 1)); Update(); };
        plus.Click += (_, _) => { set(Math.Min(max, get() + 1)); Update(); };
        Update();
        return g;
    }

    private Button Small(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("SmallButton") };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    // MARK: - Bölümler

    private StackPanel SensitivitySection() => Section("Hassasiyet", Glyphs.Of(0xE9E9),
        SliderRow("Hareket eşiği", nameof(Settings.MotionSensitivity), 0.005, 0.120, "{0:0.000}", out _),
        Note("Düşük değer = daha hassas. 0.030 civarı kafe masası için iyi bir başlangıç; küçük titreşimlerde bile çalsın istiyorsan 0.012'ye indir."),
        SliderRow("Kare kaplama eşiği", nameof(Settings.MotionCoverage), 0.20, 0.90, "{0:0%}", out _),
        Note("Asıl ayrım bu. Bilgisayar yerinden oynadığında karenin tamamı birden değişir; önünde biri kıpırdadığında yalnızca bir bölgesi. Yüksek değer = sadece gerçek taşınmaya tepki verir, masanın önünden geçen insanlara aldırmaz."),
        SliderRow("Yakınlık eşiği", nameof(Settings.ProximityThreshold), 0.20, 0.90, "{0:0.00}", out _),
        Note("Yüzün kadrajda kapladığı yükseklik oranı. 0.55 ≈ kol mesafesinden yakın. Değeri düşürdükçe daha uzaktaki kişiler de tetikler."));

    private StackPanel TimingSection() => Section("Zamanlama", Glyphs.Clock,
        Stepper("Devreye girme gecikmesi", () => _settings.ArmDelay, v => _settings.ArmDelay = v, 0, 60, v => $"{v} sn"),
        Note("Butona bastıktan sonra kalkıp uzaklaşman için tanınan süre. Kafede 8–10 saniye yeterli."),
        Divider(),
        Stepper("Uyarı süresi", () => _settings.GraceSeconds, v => _settings.GraceSeconds = v, 0, 30, v => v == 0 ? "kapalı" : $"{v} sn"),
        Note("0 = tetik gelir gelmez tam alarm. Sıfırdan büyükse önce kesik bir uyarı bipi çalar ve PIN girmen için bu kadar saniye tanır; girmezsen tam siren devreye girer. Ev alarmlarındaki mantık."));

    private StackPanel AlarmSection()
    {
        var volume = SliderRow("Alarm ses seviyesi", nameof(Settings.AlarmVolume), 0.05, 1.0, "", out _volumeValue);
        _volumeValue.FontWeight = FontWeights.SemiBold;
        _testLevelWarning = new TextBlock
        {
            Text = Glyphs.Warning + "  Test seviyesi açık — çekimden önce %100'e almayı unutma.",
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = Res("Arming"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        volume.Children.Add(_testLevelWarning);
        volume.Children.Add(Note("Evde test ederken düşür. Alarm çalarken MacGuard sesi saniyede bir kontrol eder: biri ses tuşuyla kısarsa, sessize alırsa ya da Ses karıştırıcısında yalnızca MacGuard'ı kısarsa anında bu seviyeye geri getirir."));
        volume.Children[^1].SetValue(MarginProperty, new Thickness(0, 6, 0, 0));
        ShowWhen(volume, nameof(Settings.ForceMaxVolume));

        var warningText = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
        warningText.SetBinding(TextBox.TextProperty, Bind(nameof(Settings.WarningText)));
        ShowWhen(warningText, nameof(Settings.SpeakWarning));

        var photoPanel = new StackPanel();
        var where = Row(
            new TextBlock { Text = @"Kayıt yeri: %APPDATA%\MacGuard\Snapshots", FontSize = 11, Foreground = Res("TextSecondary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) },
            Small("Klasörü aç", SnapshotStore.RevealInExplorer));
        photoPanel.Children.Add(where);
        var photoNote = Note("Telefon bildirimi kapalı olsa da fotoğraf diske yazılır. En son 100 kare saklanır, eskiler silinir. Klasör yalnızca senin Windows kullanıcına açıktır.", 10.5);
        photoNote.Margin = new Thickness(0, 8, 0, 0);
        photoPanel.Children.Add(photoNote);
        ShowWhen(photoPanel, nameof(Settings.CaptureIntruderPhoto));

        _sirenPreview = Small("Sireni 3 saniye dinle", () => _engine.PreviewSiren(3));

        return Section("Alarm", Glyphs.Speaker,
            Switch("Alarm anında sistem sesini zorla ayarla", nameof(Settings.ForceMaxVolume)),
            volume,
            Switch("Dahili hoparlörden çal (kulaklık takılıysa bile)", nameof(Settings.ForceBuiltInSpeakers)),
            Note("Windows'ta varsayılan çıkışı değiştiren genel bir yol yok; MacGuard sireni doğrudan bilgisayarın hoparlörüne gönderir. Bazı ses kartlarında kulaklık takılınca hoparlör tamamen kapanır — o durumda ses kulaklıktan çıkar."),
            Switch("Sesli uyarı oku", nameof(Settings.SpeakWarning)),
            warningText,
            Switch("Alarm anında davetsiz misafirin fotoğrafını çek", nameof(Settings.CaptureIntruderPhoto)),
            photoPanel,
            Row(_sirenPreview),
            Note("Önizleme sistem sesini değiştirmez; sesi kendin ayarla. Gerçek alarmda MacGuard sesi zaten ayarladığın seviyeye çıkarır."),
            Divider(),
            Switch("Kapak kapansa bile uyuma", nameof(Settings.BlockClamshellSleep)),
            Note("Normalde kapak kapanınca Windows uyur ve alarm susar. Bu seçenek koruma süresince güç planındaki \"Kapağı kapattığımda\" ayarını \"Hiçbir şey yapma\"ya çeker, koruma kapanınca eski değerine döndürür. Çoğu bilgisayarda yönetici şifresi istemez; grup ilkesi engelliyorsa bir kez Windows'un onay penceresi çıkar."));
    }

    private StackPanel LockScreenSection()
    {
        var details = new StackPanel();
        details.Children.Add(Divider());
        details.Children.Add(new TextBlock { Text = "Ekranda yazacak metin", FontSize = 12, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 6) });
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12,
        };
        editor.SetBinding(TextBox.TextProperty, Bind(nameof(Settings.LockScreenText)));
        details.Children.Add(editor);

        _lockPreview = Small("Önizle (10 sn)", () => _engine.PreviewLockScreen());
        var buttons = Row(_lockPreview, Small("Varsayılana dön", () => _settings.LockScreenText = Settings.DefaultLockScreenText));
        buttons.Margin = new Thickness(0, 12, 0, 12);
        details.Children.Add(buttons);
        details.Children.Add(Note("Varsayılan metin bilerek yalnızca uygulamanın gerçekten yaptığı şeyleri sayar. İstersen değiştir — ama ekranda yazan bir şeyin arkasını uygulamanın dolduramadığını bil (örneğin konum bilgisi gönderilmiyor).", 10.5));
        var battery = Note("Ekran kapanmaz, yoksa yazı kimseye görünmez — bu yüzden koruma açıkken pil tüketimi biraz artar.", 10.5);
        battery.Margin = new Thickness(0, 8, 0, 0);
        details.Children.Add(battery);
        ShowWhen(details, nameof(Settings.ShowLockScreen));

        return Section("Koruma ekranı", Glyphs.Lock,
            Switch("Koruma aktifken ekranı kaplayan uyarı göster", nameof(Settings.ShowLockScreen)),
            Note("Alarm çalmadan önce iş gören kısım: masaya yaklaşan kişi bilgisayara dokunmadan durumu okur. Kapatırsan koruma sessizce arka planda çalışır."),
            details);
    }

    private StackPanel PushSection()
    {
        var details = new StackPanel();

        details.Children.Add(new TextBlock { Text = "Sunucu (yalnızca adres, konu adı olmadan)", FontSize = 11, Foreground = Res("TextSecondary") });
        var server = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        server.SetBinding(TextBox.TextProperty, Bind(nameof(Settings.PushServer)));
        details.Children.Add(server);

        details.Children.Add(new TextBlock { Text = "Konu adı (örn. macguard-alp-7fj3)", FontSize = 11, Foreground = Res("TextSecondary") });
        var topic = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        topic.SetBinding(TextBox.TextProperty, Bind(nameof(Settings.PushTopic)));
        details.Children.Add(topic);

        // Gidecek adresi göster: sunucu alanına tam adres yapıştırılınca mesajlar
        // sessizce yanlış konuya gidiyordu ve kimse fark etmiyordu.
        _effectiveIcon = GlyphText(Glyphs.Send, 11, Res("Safe"), new Thickness(0, 0, 6, 0));
        _effectiveUrl = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Kopyalamak için tıkla",
        };
        _effectiveUrl.MouseLeftButtonUp += (_, _) =>
        {
            if (new NtfyClient.Config(_settings.PushServer, _settings.PushTopic).Url is { } u)
            {
                try { Clipboard.SetText(u.ToString()); } catch { }
            }
        };
        _effectiveRow = Row(_effectiveIcon, _effectiveUrl);
        _effectiveRow.Margin = new Thickness(0, 0, 0, 6);
        details.Children.Add(_effectiveRow);
        details.Children.Add(Note("Telefonundaki ntfy uygulamasında tam olarak bu adrese abone olmalısın.", 10.5));

        _invalidTopic = new TextBlock
        {
            Text = "Geçersiz konu adı. Yalnızca İngiliz alfabesi harfleri, rakam, _ ve - kullanılabilir — boşluk, Türkçe karakter ve # olmaz.",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = Res("Danger"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        details.Children.Add(_invalidTopic);

        var desc = Note("Telefonuna ntfy uygulamasını kur ve aynı konu adına abone ol. Hesap, anahtar, kurulum yok. Konu adı herkese açıktır — tahmin edilemeyecek bir isim seç.");
        desc.Margin = new Thickness(0, 8, 0, 12);
        details.Children.Add(desc);

        _testPush = Small("Test bildirimi gönder", SendTestPush);
        details.Children.Add(Row(Small("Rastgele konu üret", GenerateTopic), _testPush));

        _pushResult = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        details.Children.Add(_pushResult);
        ShowWhen(details, nameof(Settings.PushEnabled));

        return Section("Telefona bildirim", Glyphs.Phone,
            Switch("Alarm anında telefonuma bildirim gönder", nameof(Settings.PushEnabled)),
            details);
    }

    /// <summary>
    /// Konu adı herkese açık bir adres: fotoğraflarını koruyan tek şey tahmin
    /// edilemezliği. 20 karakter ~100 bit.
    /// </summary>
    private void GenerateTopic()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        var chars = new char[20];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        _settings.PushTopic = "macguard-" + new string(chars);
    }

    private async void SendTestPush()
    {
        var config = new NtfyClient.Config(_settings.PushServer, _settings.PushTopic);
        _pushResult.Visibility = Visibility.Visible;
        if (config.Url == null)
        {
            ShowPushResult("Konu adı geçersiz ya da sunucu adresi hatalı.", false);
            return;
        }
        ShowPushResult("Gönderiliyor…", null);
        var result = await NtfyClient.SendAsync(config, "MacGuard testi", "Bildirimler çalışıyor. 👍",
            NtfyClient.Priority.Normal, new[] { "white_check_mark" });
        ShowPushResult(result.IsSuccess ? "Gönderildi — telefonunu kontrol et." : "Gönderilemedi. " + result.Message,
            result.IsSuccess);
    }

    private void ShowPushResult(string text, bool? ok)
    {
        var glyph = ok == null ? Glyphs.Send : ok.Value ? Glyphs.Check : Glyphs.Warning;
        _pushResult.Text = glyph + "  " + text;
        _pushResult.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe Fluent Icons, Segoe MDL2 Assets");
        _pushResult.Foreground = Res(ok == null ? "TextSecondary" : ok.Value ? "Safe" : "Danger");
    }

    private StackPanel SecuritySection()
    {
        var g = new Grid();
        _pinStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(_pinStatus);
        _pinButton = Small("PIN belirle", () => { Dialogs.ShowPinSetup(this); Refresh(); });
        _pinButton.HorizontalAlignment = HorizontalAlignment.Right;
        _pinButton.Margin = new Thickness(0);
        g.Children.Add(_pinButton);

        return Section("Güvenlik", Glyphs.Shield,
            g,
            Note("PIN yalnızca tuzlanmış, 50.000 turlu SHA-256 özeti olarak tutulur; dosya (%APPDATA%\\MacGuard\\pin.json) yalnızca senin Windows kullanıcına açıktır. Unutursan sıfırlamak için korumayı kapatıp yeniden belirlemen gerekir."));
    }

    private StackPanel SystemSection()
    {
        var login = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Oturum açılınca MacGuard'ı başlat", IsChecked = LoginItem.IsEnabled };
        var failed = Row(
            new TextBlock { Text = "Windows izin vermedi", FontSize = 11, Foreground = Res("Arming"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
            Small("Başlangıç uygulamalarını aç", LoginItem.OpenSystemSettings));
        failed.Visibility = Visibility.Collapsed;

        void Apply(bool on)
        {
            if (!LoginItem.SetEnabled(on))
            {
                failed.Visibility = Visibility.Visible;
                login.IsChecked = LoginItem.IsEnabled;
            }
            else
            {
                failed.Visibility = Visibility.Collapsed;
            }
        }
        login.Checked += (_, _) => Apply(true);
        login.Unchecked += (_, _) => Apply(false);

        return Section("Sistem", Glyphs.Settings,
            login,
            failed,
            Note("Açık olduğunda MacGuard her açılışta bildirim alanında sessizce hazır bekler. Koruma yine de senin başlatmanı bekler; kendiliğinden devreye girmez."));
    }

    private StackPanel AboutSection()
    {
        var g = new StackPanel { Orientation = Orientation.Horizontal };
        g.Children.Add(GlyphText(Glyphs.Shield, 30, Res("Safe"), new Thickness(0, 0, 12, 0)));
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = $"{AppInfo.Name} {AppInfo.Version} (Windows)", FontSize = 14, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock { Text = "Yapan: " + AppInfo.Author, FontSize = 12, Foreground = Res("TextSecondary") });
        texts.Children.Add(new TextBlock { Text = "MIT Lisansı — özgürce kullan, değiştir, dağıt", FontSize = 11, Foreground = Res("TextSecondary") });
        g.Children.Add(texts);

        var links = new StackPanel { Orientation = Orientation.Horizontal };
        void Link(string label, Uri? url)
        {
            if (url == null) return;
            links.Children.Add(Small(label, () =>
            {
                try { Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true }); } catch { }
            }));
        }
        if (AppInfo.InstagramDisplay is { } ig) Link("Instagram " + ig, AppInfo.InstagramUrl);
        if (AppInfo.YoutubeDisplay is { } yt) Link("YouTube " + yt, AppInfo.YoutubeUrl);
        Link("Kaynak kodu", AppInfo.RepositoryLink);

        return links.Children.Count > 0
            ? Section("Hakkında", Glyphs.Info, g, links)
            : Section("Hakkında", Glyphs.Info, g);
    }

    // MARK: Güvenlik PIN'i (acil çıkış)

    /// <summary>
    /// Perdedeki tuş takımına ulaşılamadığı durumlar için ikinci PIN kapısı.
    /// Yanlış PIN ve bekleme cezası perdedekiyle aynı sayaçtan işler —
    /// ikinci bir kapı, gevşek bir kapı değil.
    /// </summary>
    private StackPanel EmergencyPinSection()
    {
        _emergencyPin = new PasswordBox { Width = 150, Margin = new Thickness(0, 0, 10, 0) };
        _emergencyPin.PasswordChanged += (_, _) => RefreshEmergency();
        _emergencyPin.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) SubmitEmergencyPin();
        };
        _emergencyButton = Small("Alarmı durdur", SubmitEmergencyPin);
        _emergencyStatus = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };

        return Section("Güvenlik PIN'i", Glyphs.Unlock,
            Note("Alarm çalıyor ama PIN ekranı görünmüyorsa (ikinci ekranda kaldıysa, kapak kapanıp açıldıysa ya da uygulama arkaya düştüyse) alarmı buradan PIN'ini girerek durdurabilirsin."),
            Row(_emergencyPin, _emergencyButton),
            _emergencyStatus);
    }

    private int EmergencyLockoutRemaining =>
        _engine.LockoutUntil is { } until ? Math.Max(0, (int)Math.Ceiling((until - DateTime.Now).TotalSeconds)) : 0;

    private void RefreshEmergency()
    {
        if (_emergencyPin == null) return;
        var protecting = _engine.State.IsProtecting;
        var locked = EmergencyLockoutRemaining;
        _emergencyPin.IsEnabled = protecting && locked == 0;
        _emergencyButton.IsEnabled = protecting && _emergencyPin.Password.Length > 0 && locked == 0;

        if (!protecting)
        {
            _emergencyStatus.Text = _emergencyDone ? "Koruma kapatıldı." : "Koruma kapalı — burada yapılacak bir şey yok.";
            _emergencyStatus.Foreground = Res(_emergencyDone ? "Safe" : "TextSecondary");
        }
        else if (locked > 0)
        {
            _emergencyStatus.Text = $"Çok fazla hatalı deneme — {locked} sn bekle";
            _emergencyStatus.Foreground = Res("Arming");
        }
        else if (_emergencyError != null)
        {
            _emergencyStatus.Text = _emergencyError;
            _emergencyStatus.Foreground = Res("Danger");
        }
        else
        {
            _emergencyStatus.Text = "";
        }
    }

    private void SubmitEmergencyPin()
    {
        if (!_engine.State.IsProtecting) return;
        var entered = _emergencyPin.Password;
        _emergencyPin.Clear();
        if (_engine.Disarm(entered))
        {
            _emergencyError = null;
            _emergencyDone = true;
        }
        else
        {
            // Yanlış PIN ve bekleme cezası motorda sayılıyor; burada yalnız gösteriyoruz.
            _emergencyError = "PIN doğru değil.";
        }
        RefreshEmergency();
    }
}
