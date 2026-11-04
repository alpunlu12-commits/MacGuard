using System;
using System.IO;

namespace MacGuard.Core;

/// <summary>
/// Uygulamanın diske yazdığı her şey tek klasörde: %APPDATA%\MacGuard.
///
/// Bu klasör varsayılan olarak yalnızca bu kullanıcıya (ve SYSTEM/Yöneticilere)
/// açıktır; macOS sürümündeki 0700 izinli Application Support klasörünün karşılığı.
/// </summary>
public static class AppPaths
{
    public static string Root
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacGuard");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string File(string name) => Path.Combine(Root, name);

    /// <summary>Önce geçici dosyaya yaz, sonra yer değiştir: yarım yazılmış dosya kalmasın.</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        System.IO.File.WriteAllBytes(tmp, data);
        System.IO.File.Move(tmp, path, overwrite: true);
    }
}
