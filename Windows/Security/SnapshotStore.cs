using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using MacGuard.Core;

namespace MacGuard.Security;

/// <summary>
/// Alarm anında çekilen kareleri diske yazar: %APPDATA%\MacGuard\Snapshots.
/// En son 100 kare saklanır, daha eskiler otomatik silinir.
/// </summary>
public static class SnapshotStore
{
    /// <summary>Diskte tutulan en fazla kare sayısı. Fazlası en eskiden silinir.</summary>
    private const int KeepLimit = 100;

    public static string Directory
    {
        get
        {
            var dir = Path.Combine(AppPaths.Root, "Snapshots");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Kareyi kaydeder ve dosya yolunu döndürür.</summary>
    public static string? Save(byte[] jpeg, TriggerKind kind, DateTime date)
    {
        var name = $"{date.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}_{kind.RawValue()}.jpg";
        var path = Path.Combine(Directory, name);
        try
        {
            AppPaths.WriteAtomic(path, jpeg);
        }
        catch (Exception e)
        {
            Debug.WriteLine("MacGuard: fotoğraf kaydedilemedi — " + e.Message);
            return null;
        }
        PruneOldest();
        return path;
    }

    /// <summary>Diskteki kareler, en yenisi başta.</summary>
    public static string[] All()
    {
        try
        {
            return new DirectoryInfo(Directory).GetFiles("*.jpg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static int Count => All().Length;

    /// <summary>Klasörü Dosya Gezgini'nde açar; son kare varsa onu seçili gösterir.</summary>
    public static void RevealInExplorer()
    {
        var newest = All().FirstOrDefault();
        var args = newest != null ? $"/select,\"{newest}\"" : $"\"{Directory}\"";
        try { Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true }); } catch { }
    }

    public static void Clear()
    {
        foreach (var f in All()) { try { File.Delete(f); } catch { } }
    }

    private static void PruneOldest()
    {
        foreach (var f in All().Skip(KeepLimit)) { try { File.Delete(f); } catch { } }
    }
}
