using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacGuard.Core;

namespace MacGuard.Security;

/// <summary>
/// PIN'i tuzlanmış, çok turlu SHA-256 özeti olarak saklar.
/// PIN'in kendisi hiçbir yerde düz metin tutulmaz.
///
/// Algoritma ve dosya biçimi macOS sürümüyle birebir aynı:
/// 32 bayt tuz, H(tuz + pin), ardından 50.000 kez H(tuz + önceki).
/// Dosya %APPDATA%\MacGuard\pin.json içinde; bu klasör yalnızca senin
/// kullanıcına (ve SYSTEM/Yöneticilere) açıktır.
/// </summary>
public static class PinStore
{
    private sealed class Record
    {
        public byte[] salt { get; set; } = Array.Empty<byte>();
        public byte[] hash { get; set; } = Array.Empty<byte>();
        public int rounds { get; set; }
    }

    private const int Rounds = 50_000;

    private static string FilePath => AppPaths.File("pin.json");

    public static bool IsConfigured => File.Exists(FilePath);

    public static void Set(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var record = new Record { salt = salt, hash = Digest(pin, salt, Rounds), rounds = Rounds };
        AppPaths.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(record));
    }

    public static bool Verify(string pin)
    {
        var record = Load();
        if (record == null) return false;
        var candidate = Digest(pin, record.salt, record.rounds);
        // Sabit süreli karşılaştırma.
        return CryptographicOperations.FixedTimeEquals(candidate, record.hash);
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }

    /// <summary>PIN uzayı küçük olduğu için özeti tekrarlayarak deneme maliyetini artırıyoruz.</summary>
    internal static byte[] Digest(string pin, byte[] salt, int rounds)
    {
        var pinBytes = Encoding.UTF8.GetBytes(pin);
        var input = new byte[salt.Length + pinBytes.Length];
        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(pinBytes, 0, input, salt.Length, pinBytes.Length);
        var output = SHA256.HashData(input);

        var next = new byte[salt.Length + output.Length];
        Buffer.BlockCopy(salt, 0, next, 0, salt.Length);
        for (var i = 0; i < Math.Max(1, rounds); i++)
        {
            Buffer.BlockCopy(output, 0, next, salt.Length, output.Length);
            output = SHA256.HashData(next);
        }
        return output;
    }

    private static Record? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Record>(File.ReadAllBytes(FilePath));
        }
        catch
        {
            return null;
        }
    }
}
