using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using MacGuard.Core;
using MacGuard.Platform;

namespace MacGuard.Alarm;

/// <summary>
/// Koruma açıkken sistemin uyuyup sensörleri susturmasını engeller.
///
/// İki kademe vardır:
///  • SetThreadExecutionState — boşta uykuyu (ve istenirse ekran kapanmasını)
///    engeller, hiçbir izin gerektirmez. macOS'taki IOPMAssertion karşılığı.
///  • Güç planının kapak kapatma eylemi — koruma süresince prizde ve pilde
///    "Hiçbir şey yapma"ya çekilir, koruma kapanınca eski değerine döner.
///    macOS'taki `pmset disablesleep` karşılığı. Windows'ta bu ayar genellikle
///    yönetici yetkisi istemez; grup ilkesi engellerse bir kez UAC sorulur.
///
/// Uygulama çökerse ayar açık kalmasın diye eski değerler önce diske yazılır;
/// bir sonraki açılışta geri yüklenir.
/// SetThreadExecutionState iş parçacığına bağlıdır: yalnızca arayüz
/// iş parçacığından çağrılır.
/// </summary>
public sealed class SleepBlocker
{
    private bool _systemAwake;
    private bool _displayAwake;
    public bool LidSleepDisabled { get; private set; }

    private sealed class Leftover
    {
        public Guid scheme { get; set; }
        public uint ac { get; set; }
        public uint dc { get; set; }
    }

    private static string LeftoverPath => AppPaths.File("lid-leftover.json");

    // MARK: - Uyku engelleri (izinsiz)

    /// <summary>Sistem uyumasın (ekran uyuyabilir) — koruma modu için.</summary>
    public void BeginSystemAwake()
    {
        _systemAwake = true;
        Apply();
    }

    /// <summary>Ekran da açık kalsın — koruma ekranı ve alarm görünsün diye.</summary>
    public void BeginDisplayAwake()
    {
        _displayAwake = true;
        Apply();
    }

    public void EndDisplayAwake()
    {
        _displayAwake = false;
        Apply();
    }

    public void EndAll()
    {
        _systemAwake = false;
        _displayAwake = false;
        Apply();
        RestoreLidSleep();
    }

    private void Apply()
    {
        var flags = Native.EXECUTION_STATE.ES_CONTINUOUS;
        if (_systemAwake || _displayAwake) flags |= Native.EXECUTION_STATE.ES_SYSTEM_REQUIRED;
        if (_displayAwake) flags |= Native.EXECUTION_STATE.ES_DISPLAY_REQUIRED;
        Native.SetThreadExecutionState(flags);
    }

    // MARK: - Kapak kapalıyken uyanık kalma

    /// <summary>Windows'a kapak kapansa bile uyuma der. Başarılıysa true.</summary>
    public bool DisableLidSleep()
    {
        if (LidSleepDisabled) return true;
        if (!TryReadLidAction(out var scheme, out var ac, out var dc)) return false;

        // Eski değerleri ayar değişmeden ÖNCE kaydet: arada çökersek geri dönebilelim.
        // Önceki bir kalıntı varsa onun değerleri asıl özgün değerlerdir; ezme.
        if (!File.Exists(LeftoverPath))
            WriteLeftover(new Leftover { scheme = scheme, ac = ac, dc = dc });

        if (WriteLidAction(scheme, 0, 0, allowElevation: true))
        {
            LidSleepDisabled = true;
            return true;
        }
        if (ReadLeftover() is { } l && l.scheme == scheme && l.ac == ac && l.dc == dc) DeleteLeftover();
        return false;
    }

    public void RestoreLidSleep()
    {
        if (!LidSleepDisabled) return;
        LidSleepDisabled = false;
        if (ReadLeftover() is { } l && WriteLidAction(l.scheme, l.ac, l.dc, allowElevation: true))
            DeleteLeftover();
    }

    /// <summary>Önceki çalıştırmadan kalan kapak ayarının durumu.</summary>
    public enum LeftoverState
    {
        /// <summary>Kalıntı yok.</summary>
        None,
        /// <summary>Kalıntı vardı, sessizce geri alındı.</summary>
        Recovered,
        /// <summary>Kalıntı var ama yetki olmadan geri alınamıyor. Kullanıcıya söylenmeli.</summary>
        NeedsAttention,
    }

    /// <summary>
    /// Uygulama açılışında çağrılır. Bir önceki çalıştırma çökerek ayarı açık
    /// bıraktıysa geri alır — yoksa kapak kapandığında bilgisayar bir daha uyumaz.
    /// </summary>
    public static LeftoverState RecoverFromCrashIfNeeded()
    {
        var l = ReadLeftover();
        if (l == null) return LeftoverState.None;
        if (WriteLidAction(l.scheme, l.ac, l.dc, allowElevation: false))
        {
            DeleteLeftover();
            return LeftoverState.Recovered;
        }
        return LeftoverState.NeedsAttention;
    }

    /// <summary>Kalan ayarı gerekirse UAC sorarak geri alır.</summary>
    public static bool ClearLeftoverNow()
    {
        var l = ReadLeftover();
        if (l == null) return true;
        if (!WriteLidAction(l.scheme, l.ac, l.dc, allowElevation: true)) return false;
        DeleteLeftover();
        return true;
    }

    // MARK: - Güç planı

    private static bool TryReadLidAction(out Guid scheme, out uint ac, out uint dc)
    {
        scheme = Guid.Empty;
        ac = dc = 1;
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return false;
        try
        {
            scheme = Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            Native.LocalFree(ptr);
        }
        var sub = Native.GUID_SYSTEM_BUTTON_SUBGROUP;
        var setting = Native.GUID_LIDCLOSE_ACTION;
        var s = scheme;
        if (Native.PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, out ac) != 0) return false;
        if (Native.PowerReadDCValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, out dc) != 0) return false;
        return true;
    }

    /// <summary>
    /// Kapak eylemini yazar ve planı yeniden etkinleştirir (değişiklik ancak
    /// böyle uygulanır). Önce yetkisiz API; olmazsa ve izin verildiyse UAC ile
    /// powercfg.
    /// </summary>
    private static bool WriteLidAction(Guid scheme, uint ac, uint dc, bool allowElevation)
    {
        var s = scheme;
        var sub = Native.GUID_SYSTEM_BUTTON_SUBGROUP;
        var setting = Native.GUID_LIDCLOSE_ACTION;
        var r1 = Native.PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, ac);
        var r2 = Native.PowerWriteDCValueIndex(IntPtr.Zero, ref s, ref sub, ref setting, dc);
        if (r1 == 0 && r2 == 0 && Native.PowerSetActiveScheme(IntPtr.Zero, ref s) == 0) return true;
        if (!allowElevation) return false;
        return RunElevatedPowercfg(scheme, ac, dc);
    }

    private static bool RunElevatedPowercfg(Guid scheme, uint ac, uint dc)
    {
        // Değerler sayı ve GUID; kullanıcı girdisi komuta karışmaz.
        var sch = scheme.ToString("D");
        var sub = Native.GUID_SYSTEM_BUTTON_SUBGROUP.ToString("D");
        var set = Native.GUID_LIDCLOSE_ACTION.ToString("D");
        var cmd = $"/c powercfg /setacvalueindex {sch} {sub} {set} {ac} && "
                + $"powercfg /setdcvalueindex {sch} {sub} {set} {dc} && "
                + $"powercfg /setactive {sch}";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd)
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p == null) return false;
            if (!p.WaitForExit(30_000)) return false;
            return p.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // Kullanıcı UAC penceresini iptal etti.
            return false;
        }
    }

    // MARK: - Kalıntı dosyası

    private static Leftover? ReadLeftover()
    {
        try
        {
            return File.Exists(LeftoverPath) ? JsonSerializer.Deserialize<Leftover>(File.ReadAllBytes(LeftoverPath)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteLeftover(Leftover l)
    {
        try { AppPaths.WriteAtomic(LeftoverPath, JsonSerializer.SerializeToUtf8Bytes(l)); } catch { }
    }

    private static void DeleteLeftover()
    {
        try { File.Delete(LeftoverPath); } catch { }
    }
}
