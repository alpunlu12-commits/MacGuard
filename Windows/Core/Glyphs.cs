namespace MacGuard.Core;

/// <summary>
/// Segoe Fluent Icons / Segoe MDL2 Assets simge kodları (SF Symbols karşılığı).
/// Windows 11'de Fluent, Windows 10'da MDL2 yazı tipi aynı kodları taşır.
/// </summary>
public static class Glyphs
{
    public static string Of(int code) => ((char)code).ToString();

    public static readonly string Shield = Of(0xEA18);
    public static readonly string Lock = Of(0xE72E);
    public static readonly string Unlock = Of(0xE785);
    public static readonly string Warning = Of(0xE7BA);
    public static readonly string Error = Of(0xE783);
    public static readonly string Info = Of(0xE946);
    public static readonly string Clock = Of(0xE823);
    public static readonly string Hourglass = Of(0xE916);  // Stopwatch
    public static readonly string Eye = Of(0xE890);
    public static readonly string Settings = Of(0xE713);
    public static readonly string Camera = Of(0xE722);
    public static readonly string Photo = Of(0xE91B);
    public static readonly string Backspace = Of(0xE750);
    public static readonly string Check = Of(0xE73E);
    public static readonly string Cancel = Of(0xE711);
    public static readonly string Phone = Of(0xE8EA);
    public static readonly string Speaker = Of(0xE767);
    public static readonly string Power = Of(0xE7E8);
    public static readonly string Send = Of(0xE724);
    public static readonly string Bell = Of(0xEA8F);
}
