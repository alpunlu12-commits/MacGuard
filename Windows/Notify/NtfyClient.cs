using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MacGuard.Notify;

/// <summary>
/// Telefona anlık bildirim gönderir (ntfy.sh).
/// Telefonda ntfy uygulamasından aynı konu adına abone olmak yeterlidir; hesap veya anahtar istemez.
/// </summary>
public static class NtfyClient
{
    private static readonly HttpClient Http = new();

    /// <summary>
    /// ntfy konu adları yalnızca ASCII harf, rakam, `_` ve `-` kabul eder.
    ///
    /// Doğrulama sadece incelik değil, güvenlik meselesi: `abc#d` gibi bir konu
    /// adı URL'de parça ayırıcı üretir ve mesaj aslında `abc` konusuna gider —
    /// yani davetsiz misafir fotoğrafı, kullanıcının haberi olmadan başkasının
    /// konusuna düşebilir. Türkçe karakter ve boşluk ise sunucu tarafından
    /// reddedilir ve bildirim sessizce hiç gitmez.
    /// </summary>
    public static bool IsValidTopic(string topic)
    {
        var t = topic.Trim();
        if (t.Length < 1 || t.Length > 64) return false;
        return t.All(ch => ch < 128 && (char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-'));
    }

    public readonly record struct Config(string Server, string Topic)
    {
        /// <summary>
        /// Sunucu alanından yalnızca kök adresi çıkarır (şema + host + port).
        ///
        /// Kullanıcılar sunucu alanına çoğu zaman konuyu da içeren tam adresi
        /// yapıştırıyor ("https://ntfy.sh/konum"). Eskiden bu değer olduğu gibi
        /// alınıp sonuna konu bir kez daha eklendiği için mesaj
        /// "ntfy.sh/konum/konum" adresine gidiyordu.
        /// </summary>
        public string? NormalizedServer
        {
            get
            {
                var raw = (Server ?? "").Trim();
                if (raw.Length == 0) return null;
                if (!raw.Contains("://")) raw = "https://" + raw;
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return null;
                if (u.Scheme != "http" && u.Scheme != "https") return null;
                if (string.IsNullOrEmpty(u.Host)) return null;
                var outStr = $"{u.Scheme}://{u.Host}";
                if (!u.IsDefaultPort) outStr += $":{u.Port}";
                return outStr;
            }
        }

        /// <summary>Konu adı. Tam adres yapıştırılmışsa son parçayı alır.</summary>
        public string NormalizedTopic
        {
            get
            {
                var t = (Topic ?? "").Trim();
                var slash = t.LastIndexOf('/');
                if (slash >= 0) t = t[(slash + 1)..];
                return t.Trim();
            }
        }

        public Uri? Url
        {
            get
            {
                var b = NormalizedServer;
                if (b == null) return null;
                var t = NormalizedTopic;
                if (!IsValidTopic(t)) return null;
                return Uri.TryCreate($"{b}/{t}", UriKind.Absolute, out var u) ? u : null;
            }
        }
    }

    /// <summary>
    /// Bildirim önceliği. ntfy'nin JSON API'si bunu **sayı** olarak bekler;
    /// "urgent" gibi metinler yalnızca HTTP başlığında geçerlidir.
    /// </summary>
    public enum Priority { Low = 2, Normal = 3, High = 4, Urgent = 5 }

    /// <summary>Gönderim sonucu. Sessizce yutulmaz: arayüz kullanıcıya söyler.</summary>
    public readonly record struct SendResult(bool IsSuccess, string Message)
    {
        public static SendResult Success => new(true, "Gönderildi");
        public static SendResult Failure(string reason) => new(false, reason);
    }

    /// <summary>Metin bildirimi. Başlık ve gövde JSON ile gider; Türkçe karakterler bozulmaz.</summary>
    public static async Task<SendResult> SendAsync(Config config, string title, string message,
        Priority priority = Priority.Urgent, string[]? tags = null)
    {
        var url = config.Url;
        var root = config.NormalizedServer;
        if (url == null || root == null) return SendResult.Failure("Sunucu adresi ya da konu adı geçersiz");

        var payload = new
        {
            topic = config.NormalizedTopic,
            title,
            message,
            priority = (int)priority,
            tags = tags ?? new[] { "rotating_light" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, root + "/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        return await PerformAsync(request, "bildirim", TimeSpan.FromSeconds(12));
    }

    /// <summary>Davetsiz misafirin fotoğrafını ek olarak gönderir.</summary>
    public static async Task<SendResult> SendPhotoAsync(Config config, byte[] jpeg, string filename = "macguard.jpg")
    {
        var url = config.Url;
        if (url == null) return SendResult.Failure("Adres geçersiz");

        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(jpeg) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        request.Headers.Add("Filename", filename);
        // HTTP başlıkları ASCII olmak zorunda; Türkçe metin gövdedeki JSON mesajda gidiyor.
        request.Headers.Add("Title", "MacGuard");
        request.Headers.Add("Priority", "high");
        request.Headers.Add("Tags", "camera_flash");
        return await PerformAsync(request, "fotoğraf", TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// İsteği yollar ve **durum kodunu kontrol eder**. 400/500 yanıtları başarı
    /// sayılırsa kullanıcı hiçbir şey görmeden bildirimsiz kalır.
    /// </summary>
    private static async Task<SendResult> PerformAsync(HttpRequestMessage request, string what, TimeSpan timeout)
    {
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(timeout);
            using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return SendResult.Success;
            var detail = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            System.Diagnostics.Debug.WriteLine($"MacGuard: {what} reddedildi (HTTP {(int)response.StatusCode}) — {detail}");
            if (detail.Length > 120) detail = detail[..120];
            return SendResult.Failure($"Sunucu reddetti (HTTP {(int)response.StatusCode})" + (detail.Length == 0 ? "" : ": " + detail));
        }
        catch (Exception e)
        {
            var reason = e is TaskCanceledException ? "zaman aşımı" : e.Message;
            System.Diagnostics.Debug.WriteLine($"MacGuard: {what} gönderilemedi — {reason}");
            return SendResult.Failure("Bağlantı kurulamadı: " + reason);
        }
    }
}
