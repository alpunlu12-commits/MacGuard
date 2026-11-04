using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MacGuard.Platform;

/// <summary>
/// "Oturum açılınca başlat" ayarı. Kullanıcının kendi Run anahtarına yazar;
/// yönetici yetkisi istemez. Görev Yöneticisi > Başlangıç uygulamaları
/// sekmesinde de görünür ve oradan kapatılabilir.
/// </summary>
internal static class LoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacGuard";

    private static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
    }

    /// <summary>true döndürürse istenen duruma geçildi.</summary>
    public static bool SetEnabled(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (on) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine("MacGuard: başlangıç öğesi ayarlanamadı — " + e.Message);
            return false;
        }
    }

    /// <summary>Windows Ayarları > Uygulamalar > Başlangıç sayfasını açar.</summary>
    public static void OpenSystemSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); } catch { }
    }
}
