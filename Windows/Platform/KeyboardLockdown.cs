using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacGuard.Platform;

/// <summary>
/// Alarm sırasında kaçış kısayollarını kapatır: Win tuşu, Alt+Tab, Alt+Esc,
/// Ctrl+Esc, Ctrl+Shift+Esc (Görev Yöneticisi) ve Alt+F4.
///
/// macOS'taki presentationOptions (Dock, ⌘-Tab, Zorla Çık kapalı) karşılığı.
/// Düşük seviyeli klavye kancası yönetici yetkisi istemez. Ctrl+Alt+Del
/// Windows'un güvenli dikkat dizisidir ve hiçbir uygulama tarafından
/// engellenemez — bu sınır README'de açıkça yazıyor.
/// </summary>
internal static class KeyboardLockdown
{
    private static IntPtr _hook = IntPtr.Zero;
    // Temsilci çöpe gitmesin diye alanda tutuluyor; yoksa kanca rastgele çöker.
    private static readonly Native.LowLevelKeyboardProc Proc = HookCallback;

    public static bool IsActive => _hook != IntPtr.Zero;

    public static void Enable()
    {
        if (_hook != IntPtr.Zero) return;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, Proc,
            Native.GetModuleHandle(module?.ModuleName), 0);
    }

    public static void Disable()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>
    /// Hızlı olmalı: Windows yavaş kancaları sessizce söker. Yalnızca birkaç
    /// karşılaştırma yapıyor, bellek ayırmıyor.
    /// </summary>
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            var vk = (int)k.vkCode;
            var alt = (k.flags & Native.LLKHF_ALTDOWN) != 0;
            var ctrl = Native.IsKeyDown(Native.VK_CONTROL);

            var block =
                vk is Native.VK_LWIN or Native.VK_RWIN or Native.VK_APPS
                || (alt && vk is Native.VK_TAB or Native.VK_ESCAPE or Native.VK_F4)
                || (ctrl && vk == Native.VK_ESCAPE);   // Ctrl+Esc ve Ctrl+Shift+Esc

            if (block) return new IntPtr(1);
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
