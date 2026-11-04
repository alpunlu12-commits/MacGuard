using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MacGuard.Core;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace MacGuard.Sensors;

/// <summary>Kameraya erişim durumu.</summary>
public enum CameraAccess { Unknown, Granted, Denied, NoDevice }

/// <summary>
/// Kameranın tek sahibi.
///
/// Çoğu dizüstü bilgisayarda ivmeölçer ya yok ya da masaüstü uygulamalarına
/// açık değil; "bilgisayar hareket etti" bilgisi görüntüden çıkarılır. Aynı
/// akış Windows'un yerleşik yüz algılayıcısıyla (FaceDetector) "biri fazla
/// yaklaştı" durumunu da verir ve alarm anında fotoğraf sağlar.
///
/// İki ayrım bu sınıfın kalbidir (macOS sürümüyle birebir aynı):
///  • Kaplama (coverage) — bilgisayar oynadığında karenin *tamamı* değişir.
///    Önünde biri kıpırdadığında yalnızca bir bölgesi değişir. Tetik için hem
///    değişimin şiddeti hem de kareye yayılma oranı eşiği geçmelidir.
///  • Sahne sakinleşmesi (settle) — koruma açıldığı anda sen hâlâ bilgisayarın
///    başındasın. Her alt sensör, ortam bir süre sakin/boş kalana kadar tetik
///    üretmez; ancak ondan sonra nöbete geçer.
/// </summary>
public sealed class CameraSensor : ISensor
{
    public static CameraSensor Shared { get; } = new();

    /// <summary>Arayüzün gösterdiği anlık okumalar.</summary>
    public readonly record struct Readings(double MotionScore, double MotionCoverage, double FaceHeight,
        bool MotionLive, bool ProximityLive);

    public IReadOnlyList<TriggerKind> Kinds { get; } = new[] { TriggerKind.Motion, TriggerKind.Proximity };
    public Action<TriggerEvent>? OnTrigger { get; set; }
    public Action<Readings>? OnReadings { get; set; }

    // MARK: Ayarlanabilir sabitler

    private const int GridW = 48;
    private const int GridH = 36;
    /// <summary>Bir ızgara hücresinin "değişti" sayılması için gereken parlaklık farkı (0...1).</summary>
    private const float CellChangeThreshold = 0.055f;
    /// <summary>Hareket nöbete geçmeden önce kaç kare sakin geçmeli (~12 fps).</summary>
    private const int MotionSettleFrames = 14;
    /// <summary>Yakınlık nöbete geçmeden önce kaç ölçüm boş geçmeli (~3 fps).</summary>
    private const int ProximitySettleChecks = 7;

    // MARK: Durum — hepsi _gate kilidi altında okunur/yazılır

    private readonly object _gate = new();

    private bool _motionEnabled;
    private bool _proximityEnabled;
    /// <summary>false iken analiz sürer ve ölçümler arayüze akar ama alarm tetiklenmez (kalibrasyon).</summary>
    private bool _triggersEnabled = true;

    private double _motionThreshold = 0.030;
    private double _motionCoverageThreshold = 0.50;
    private double _proximityThreshold = 0.55;

    private float[]? _prevGrid;
    private DateTime _warmupUntil = DateTime.MinValue;
    private int _motionStreak;
    private int _proximityStreak;
    private DateTime _lastMotionFire = DateTime.MinValue;
    private DateTime _lastProximityFire = DateTime.MinValue;
    private int _frameCounter;
    private DateTime _lastProcessed = DateTime.MinValue;
    private DateTime _lastReadingReport = DateTime.MinValue;
    private double _lastFaceHeight;

    // Sahne sakinleşmesi
    private bool _motionLive;
    private int _motionCalmStreak;
    private bool _proximityLive;
    private int _proximityClearStreak;

    // Fotoğraf önbelleği
    private byte[]? _cachedJpeg;
    private DateTime _nextSnapshotAt = DateTime.MinValue;

    // Yakalama
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private FaceDetector? _faceDetector;
    private byte[] _pixels = Array.Empty<byte>();
    private int _busy;
    private volatile bool _wantRunning;

    public bool IsRunning => _wantRunning;
    public string? LastError { get; private set; }

    private CameraSensor() { }

    // MARK: - Erişim

    public static CameraAccess Authorization { get; private set; } = CameraAccess.Unknown;

    /// <summary>
    /// Masaüstü uygulamalarında macOS'taki gibi bir izin penceresi yok:
    /// Windows Ayarları > Gizlilik > Kamera altındaki "Masaüstü uygulamalarının
    /// kameraya erişmesine izin ver" anahtarı belirleyici. Durumu öğrenmenin tek
    /// güvenilir yolu kamerayı bir an açmayı denemek.
    /// </summary>
    public static async Task<bool> RequestAccessAsync()
    {
        if (Authorization == CameraAccess.Granted) return true;
        if (Shared.IsRunning && Shared._capture != null) { Authorization = CameraAccess.Granted; return true; }

        try
        {
            var device = await PreferredDeviceAsync();
            if (device == null)
            {
                Authorization = CameraAccess.NoDevice;
                return false;
            }
            using var probe = new MediaCapture();
            await probe.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            Authorization = CameraAccess.Granted;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Authorization = CameraAccess.Denied;
            return false;
        }
        catch (Exception e)
        {
            // Kamera başka bir uygulamada açık olabilir; izin sorunu değil.
            Debug.WriteLine("MacGuard: kamera denemesi başarısız — " + e.Message);
            Authorization = CameraAccess.Granted;
            return true;
        }
    }

    /// <summary>İzin durumunun okunabilir hâli — arayüzde göstermek için.</summary>
    public static string AuthorizationDescription => Authorization switch
    {
        CameraAccess.Granted => "erişilebilir",
        CameraAccess.Denied => "Windows ayarlarında kapalı",
        CameraAccess.NoDevice => "kamera bulunamadı",
        _ => "henüz denenmedi",
    };

    /// <summary>Windows Ayarları'nda Gizlilik > Kamera bölümünü açar.</summary>
    public static void OpenSystemSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-webcam") { UseShellExecute = true }); } catch { }
    }

    /// <summary>Sanal kameraları (OBS vb.) atlayıp dahili ön kamerayı tercih eder.</summary>
    private static async Task<DeviceInformation?> PreferredDeviceAsync()
    {
        var all = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        var list = all.Where(d => d.IsEnabled).ToList();
        return list.FirstOrDefault(d => d.EnclosureLocation?.Panel == Panel.Front)
            ?? list.FirstOrDefault(d => d.EnclosureLocation != null)
            ?? list.FirstOrDefault();
    }

    // MARK: - Yapılandırma

    /// <summary>Eşikleri analiz tarafına taşır.</summary>
    public void Configure(double motionThreshold, double motionCoverage, double proximityThreshold)
    {
        lock (_gate)
        {
            _motionThreshold = motionThreshold;
            _motionCoverageThreshold = motionCoverage;
            _proximityThreshold = proximityThreshold;
        }
    }

    /// <summary>Hangi analizlerin çalışacağını ve tetik yayınının açık olup olmadığını belirler.</summary>
    public void SetAnalysis(bool motion, bool proximity, bool triggers)
    {
        lock (_gate)
        {
            _motionEnabled = motion;
            _proximityEnabled = proximity;
            _triggersEnabled = triggers;
        }
    }

    /// <summary>Sahne sakinleşmesini sıfırlar: her koruma başlangıcında çağrılır.</summary>
    public void ResetSettleState()
    {
        lock (_gate)
        {
            _motionLive = false;
            _motionCalmStreak = 0;
            _proximityLive = false;
            _proximityClearStreak = 0;
            _motionStreak = 0;
            _proximityStreak = 0;
        }
    }

    // MARK: - Yaşam döngüsü

    public void Start()
    {
        if (_wantRunning) return;
        _wantRunning = true;
        LastError = null;

        // Otomatik pozlama oturana kadar analiz yapma, yoksa sahte hareket üretir.
        lock (_gate)
        {
            _prevGrid = null;
            _motionStreak = 0;
            _proximityStreak = 0;
            _motionLive = false;
            _motionCalmStreak = 0;
            _proximityLive = false;
            _proximityClearStreak = 0;
            _warmupUntil = DateTime.UtcNow.AddSeconds(2.0);
        }

        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (_wantRunning && _capture == null) await StartCoreAsync();
            }
            finally
            {
                _lifecycle.Release();
            }
        });
    }

    public void Stop()
    {
        if (!_wantRunning) return;
        _wantRunning = false;
        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!_wantRunning) await StopCoreAsync();
            }
            finally
            {
                _lifecycle.Release();
            }
        });
        lock (_gate) _prevGrid = null;
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var device = await PreferredDeviceAsync();
            if (device == null)
            {
                Authorization = CameraAccess.NoDevice;
                Fail("Kullanılabilir kamera bulunamadı");
                return;
            }

            var capture = new MediaCapture();
            var exclusive = true;
            try
            {
                await capture.InitializeAsync(CaptureSettings(device.Id, MediaCaptureSharingMode.ExclusiveControl));
            }
            catch (UnauthorizedAccessException)
            {
                capture.Dispose();
                throw;
            }
            catch
            {
                // Kamera başka bir uygulamada açıksa yalnızca okuyarak paylaş.
                capture.Dispose();
                capture = new MediaCapture();
                exclusive = false;
                await capture.InitializeAsync(CaptureSettings(device.Id, MediaCaptureSharingMode.SharedReadOnly));
            }
            Authorization = CameraAccess.Granted;

            var source = capture.FrameSources.Values
                .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(s => s.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoPreview ? 0 : 1)
                .FirstOrDefault();
            if (source == null)
            {
                capture.Dispose();
                Fail("Video çıkışı eklenemedi");
                return;
            }

            // ~640 piksel genişlik analiz için fazlasıyla yeterli; CPU'yu boşuna yakmayalım.
            if (exclusive)
            {
                var format = source.SupportedFormats
                    .Where(f => f.VideoFormat.Width >= 320)
                    .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 640))
                    .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
                    .FirstOrDefault();
                if (format != null)
                {
                    try { await source.SetFormatAsync(format); } catch { /* varsayılan biçimle devam */ }
                }
            }

            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrameArrived;

            if (FaceDetector.IsSupported && _faceDetector == null)
            {
                try { _faceDetector = await FaceDetector.CreateAsync(); } catch { _faceDetector = null; }
            }

            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                reader.FrameArrived -= OnFrameArrived;
                reader.Dispose();
                capture.Dispose();
                Fail("Kamera açılamadı: " + status);
                return;
            }

            _capture = capture;
            _reader = reader;

            // Başlatma sürerken durdurma istendiyse hemen kapat.
            if (!_wantRunning) await StopCoreAsync();
        }
        catch (UnauthorizedAccessException)
        {
            Authorization = CameraAccess.Denied;
            Fail("Kamera erişimi Windows ayarlarında kapalı");
        }
        catch (Exception e)
        {
            Fail("Kamera açılamadı: " + e.Message);
        }
    }

    private static MediaCaptureInitializationSettings CaptureSettings(string id, MediaCaptureSharingMode mode) => new()
    {
        VideoDeviceId = id,
        StreamingCaptureMode = StreamingCaptureMode.Video,
        SharingMode = mode,
        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
    };

    private void Fail(string message)
    {
        LastError = message;
        _wantRunning = false;
        Debug.WriteLine("MacGuard: " + message);
    }

    private async Task StopCoreAsync()
    {
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        if (reader != null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try { await reader.StopAsync(); } catch { }
            reader.Dispose();
        }
        capture?.Dispose();
        lock (_gate) _cachedJpeg = null;
    }

    /// <summary>Alarm anında yakalanmış son kare (JPEG). Kamera kapalıysa null.</summary>
    public byte[]? SnapshotJpeg()
    {
        lock (_gate) return _cachedJpeg;
    }

    // MARK: - Kare işleme

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Önceki kare hâlâ işleniyorsa bunu atla (gerçek zamanlı mod zaten eskileri düşürür).
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = ProcessAsync(sender);
    }

    private async Task ProcessAsync(MediaFrameReader sender)
    {
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var now = DateTime.UtcNow;
            // ~12 fps yeterli.
            if ((now - _lastProcessed).TotalSeconds < 1.0 / 12.0) return;
            var source = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (source == null) return;
            _lastProcessed = now;

            using var converted = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? null
                : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            var bmp = converted ?? source;
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            if (_pixels.Length != w * h * 4) _pixels = new byte[w * h * 4];
            bmp.CopyToBuffer(_pixels.AsBuffer());

            if (now >= _nextSnapshotAt)
            {
                _nextSnapshotAt = now.AddSeconds(1.0);
                await CacheSnapshotAsync(bmp);
            }

            DateTime warmup;
            bool proximityEnabled;
            lock (_gate) { warmup = _warmupUntil; proximityEnabled = _proximityEnabled; }
            if (now < warmup) return;

            _frameCounter = unchecked(_frameCounter + 1);
            var grid = SampleLumaGrid(_pixels, w, h);
            var motion = grid == null ? default : AnalyseMotion(grid);

            if (proximityEnabled && _frameCounter % 4 == 0)
                _lastFaceHeight = await AnalyseProximityAsync(bmp);
            else if (!proximityEnabled)
                _lastFaceHeight = 0;

            if ((now - _lastReadingReport).TotalSeconds >= 0.12)
            {
                _lastReadingReport = now;
                Readings r;
                lock (_gate)
                    r = new Readings(motion.Score, motion.Coverage, _lastFaceHeight, _motionLive, _proximityLive);
                var cb = OnReadings;
                if (cb != null) Application.Current?.Dispatcher.BeginInvoke(() => cb(r));
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("MacGuard: kare işlenemedi — " + e.Message);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private readonly record struct MotionReading(double Score, double Coverage);

    /// <summary>
    /// Ardışık kareler arasındaki yapısal farkı ölçer.
    ///
    /// Ortalama fark çıkarılır (odanın ışığı topluca değişince tetiklenmesin) ve
    /// ayrıca değişimin kareye yayılma oranı hesaplanır. Bilgisayar kaldırıldığında
    /// hücrelerin neredeyse tamamı değişir; önünde biri kıpırdadığında yalnızca
    /// küçük bir bölge değişir. Tetik için ikisi birden gerekir.
    /// </summary>
    private MotionReading AnalyseMotion(float[] grid)
    {
        lock (_gate)
        {
            var prev = _prevGrid;
            _prevGrid = grid;
            if (prev == null || prev.Length != grid.Length) return default;

            var n = grid.Length;
            float sum = 0;
            for (var i = 0; i < n; i++) sum += grid[i] - prev[i];
            var mean = sum / n;

            float acc = 0;
            var changed = 0;
            for (var i = 0; i < n; i++)
            {
                var d = Math.Abs((grid[i] - prev[i]) - mean);
                acc += d;
                if (d > CellChangeThreshold) changed++;
            }

            var reading = new MotionReading(acc / n, (double)changed / n);

            if (!_motionEnabled || !_triggersEnabled) return reading;

            // Nöbete geçmeden önce sahnenin sakinleşmesini bekle.
            // Koruma açılırken sen hâlâ bilgisayarın başındaysan alarm çalmasın.
            if (!_motionLive)
            {
                if (reading.Score < _motionThreshold * 0.6)
                {
                    _motionCalmStreak++;
                    if (_motionCalmStreak >= MotionSettleFrames) _motionLive = true;
                }
                else
                {
                    _motionCalmStreak = 0;
                }
                return reading;
            }

            var looksLikeTheLaptopMoved = reading.Score >= _motionThreshold
                && reading.Coverage >= _motionCoverageThreshold;

            // İki ardışık kare: tek karelik gürültü alarm çaldırmasın.
            _motionStreak = looksLikeTheLaptopMoved ? _motionStreak + 1 : 0;

            if (_motionStreak >= 2 && (DateTime.UtcNow - _lastMotionFire).TotalSeconds > 3)
            {
                _lastMotionFire = DateTime.UtcNow;
                _motionStreak = 0;
                this.Emit(new TriggerEvent(TriggerKind.Motion, "Bilgisayar yerinden oynatıldı",
                    Math.Min(1, reading.Coverage)));
            }
            return reading;
        }
    }

    /// <summary>Kadrajdaki en büyük yüzün yüksekliğini (0...1) ölçer.</summary>
    private async Task<double> AnalyseProximityAsync(SoftwareBitmap bmp)
    {
        var detector = _faceDetector;
        if (detector == null) return 0;

        double tallest;
        try
        {
            using var gray = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Gray8);
            var faces = await detector.DetectFacesAsync(gray);
            tallest = faces.Count == 0 ? 0 : faces.Max(f => (double)f.FaceBox.Height) / Math.Max(1, gray.PixelHeight);
        }
        catch
        {
            return 0;
        }

        lock (_gate)
        {
            if (!_triggersEnabled) return tallest;

            // Kadraj bir süre boşalmadan yakınlık nöbete geçmez.
            // Yoksa korumayı açan kişinin kendi yüzü anında alarm çaldırır.
            if (!_proximityLive)
            {
                if (tallest < _proximityThreshold * 0.85)
                {
                    _proximityClearStreak++;
                    if (_proximityClearStreak >= ProximitySettleChecks) _proximityLive = true;
                }
                else
                {
                    _proximityClearStreak = 0;
                }
                return tallest;
            }

            _proximityStreak = tallest >= _proximityThreshold ? _proximityStreak + 1 : 0;

            if (_proximityStreak >= 2 && (DateTime.UtcNow - _lastProximityFire).TotalSeconds > 5)
            {
                _lastProximityFire = DateTime.UtcNow;
                _proximityStreak = 0;
                this.Emit(new TriggerEvent(TriggerKind.Proximity, "Biri bilgisayara fazla yaklaştı",
                    Math.Min(1, tallest)));
            }
            return tallest;
        }
    }

    /// <summary>BGRA karesinden seyrek bir parlaklık ızgarası örneği alır — çözünürlükten bağımsız ucuz.</summary>
    private static float[]? SampleLumaGrid(byte[] px, int w, int h)
    {
        if (w < GridW || h < GridH) return null;
        var grid = new float[GridW * GridH];
        int cellH = h / GridH, cellW = w / GridW;
        var stride = w * 4;
        for (var gy = 0; gy < GridH; gy++)
        {
            var y = gy * cellH + cellH / 2;
            var row = y * stride;
            for (var gx = 0; gx < GridW; gx++)
            {
                var x = gx * cellW + cellW / 2;
                var i = row + x * 4;
                // BT.601 parlaklık (macOS sürümü kameranın Y düzlemini doğrudan okuyor).
                var luma = 0.114f * px[i] + 0.587f * px[i + 1] + 0.299f * px[i + 2];
                grid[gy * GridW + gx] = luma / 255f;
            }
        }
        return grid;
    }

    private async Task CacheSnapshotAsync(SoftwareBitmap bmp)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var props = new BitmapPropertySet
            {
                { "ImageQuality", new BitmapTypedValue(0.75f, Windows.Foundation.PropertyType.Single) },
            };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, props);
            using var copy = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            encoder.SetSoftwareBitmap(copy);
            await encoder.FlushAsync();

            var bytes = new byte[stream.Size];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }
            lock (_gate) _cachedJpeg = bytes;
        }
        catch (Exception e)
        {
            Debug.WriteLine("MacGuard: fotoğraf önbelleği yazılamadı — " + e.Message);
        }
    }
}
