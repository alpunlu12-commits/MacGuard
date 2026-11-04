using System;
using System.Linq;
using System.Reflection;

namespace MacGuard.Core;

/// <summary>
/// Uygulamanın künyesi. Tek yerden düzenlenir; arayüz, kilit ekranı ve
/// "Hakkında" bölümü buradan besleniyor.
/// </summary>
public static class AppInfo
{
    public const string Name = "MacGuard";

    /// <summary>Yazarın görünen adı — lisans ve künyede kullanılır.</summary>
    public const string Author = "Alp Ünlü";

    /// <summary>Sosyal hesaplar, başında @ olmadan. Boş bırakılan alan arayüzde hiç gösterilmez.</summary>
    public const string InstagramHandle = "alppunlu";
    public const string YoutubeHandle = "alpunlu";

    /// <summary>Projenin kaynak adresi. Boşsa gösterilmez.</summary>
    public const string RepositoryUrl = "";

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    /// <summary>Künye satırı: "MacGuard 1.1 · Alp Ünlü"</summary>
    public static string CreditLine => $"{Name} {Version} · {Author}";

    /// <summary>
    /// Kullanıcı adını temizler ve yalnızca güvenli karakterlerden oluşuyorsa döner.
    /// Sabit yanlışlıkla bozulursa hiçbir bağlantı üretilmez.
    /// </summary>
    private static string? Sanitize(string handle)
    {
        var t = handle.Trim().Trim('@');
        if (t.Length == 0 || t.Length > 40) return null;
        return t.All(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')) ? t : null;
    }

    public static Uri? InstagramUrl => Sanitize(InstagramHandle) is { } h ? new Uri($"https://instagram.com/{h}") : null;
    public static string? InstagramDisplay => Sanitize(InstagramHandle) is { } h ? "@" + h : null;
    public static Uri? YoutubeUrl => Sanitize(YoutubeHandle) is { } h ? new Uri($"https://youtube.com/@{h}") : null;
    public static string? YoutubeDisplay => Sanitize(YoutubeHandle) is { } h ? "@" + h : null;

    public static Uri? RepositoryLink
    {
        get
        {
            var raw = RepositoryUrl.Trim();
            return raw.StartsWith("https://", StringComparison.Ordinal) && Uri.TryCreate(raw, UriKind.Absolute, out var u) ? u : null;
        }
    }

    /// <summary>Kilit ekranındaki tek satırlık imza: "MacGuard 1.1 · Alp Ünlü · @alppunlu"</summary>
    public static string SignatureLine => InstagramDisplay is { } ig ? $"{CreditLine} · {ig}" : CreditLine;
}
