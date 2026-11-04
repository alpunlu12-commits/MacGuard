using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MacGuard.Core;

namespace MacGuard.Platform;

/// <summary>
/// Bildirim alanı simgesi — macOS'taki menü çubuğu öğesinin karşılığı.
/// Rengi koruma durumunu gösterir, sağ tık menüsü duruma göre değişir.
/// </summary>
internal static class TrayIcon
{
    private static NotifyIcon? _icon;
    private static GuardStateKind? _drawnFor;

    public static void Install()
    {
        if (_icon != null) return;
        _icon = new NotifyIcon
        {
            Visible = true,
            Text = "MacGuard",
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) App.ShowMainWindow();
        };
        BuildMenu();
        Refresh();

        GuardEngine.Shared.PropertyChanged += OnEngineChanged;
    }

    public static void Remove()
    {
        if (_icon == null) return;
        GuardEngine.Shared.PropertyChanged -= OnEngineChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }

    public static void ShowBalloon(string title, string body)
    {
        _icon?.ShowBalloonTip(4000, title, body, ToolTipIcon.Info);
    }

    private static void OnEngineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuardEngine.State)) Refresh();
    }

    private static void Refresh()
    {
        if (_icon == null) return;
        var state = GuardEngine.Shared.State;
        _icon.Text = state.Kind switch
        {
            GuardStateKind.Disarmed => "MacGuard — koruma kapalı",
            GuardStateKind.Arming => $"MacGuard — {state.Remaining} sn içinde devrede",
            GuardStateKind.Armed => "MacGuard — koruma aktif",
            GuardStateKind.Warning => "MacGuard — UYARI",
            _ => "MacGuard — ALARM",
        };
        if (_drawnFor == state.Kind) return;
        _drawnFor = state.Kind;
        var old = _icon.Icon;
        _icon.Icon = Draw(state.Kind);
        old?.Dispose();
    }

    private static void BuildMenu()
    {
        var menu = _icon?.ContextMenuStrip;
        if (menu == null) return;
        menu.Items.Clear();
        var engine = GuardEngine.Shared;
        var state = engine.State;

        switch (state.Kind)
        {
            case GuardStateKind.Disarmed:
                menu.Items.Add("Korumayı başlat", null, (_, _) => engine.Arm());
                break;
            case GuardStateKind.Arming:
                menu.Items.Add(new ToolStripMenuItem($"Devreye giriyor — {state.Remaining} sn") { Enabled = false });
                menu.Items.Add("İptal et", null, (_, _) => engine.CancelArming());
                break;
            case GuardStateKind.Armed:
                menu.Items.Add(new ToolStripMenuItem("Koruma aktif") { Enabled = false });
                menu.Items.Add("Alarmı test et", null, (_, _) => engine.TestAlarm());
                break;
            case GuardStateKind.Warning:
                menu.Items.Add(new ToolStripMenuItem($"Uyarı — {state.Remaining} sn içinde PIN gir") { Enabled = false });
                break;
            case GuardStateKind.Alarming:
                menu.Items.Add(new ToolStripMenuItem("ALARM ÇALIYOR") { Enabled = false });
                break;
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("MacGuard penceresini aç", null, (_, _) => App.ShowMainWindow());
        if (!state.IsProtecting) menu.Items.Add("Çık", null, (_, _) => App.RequestQuit());
    }

    /// <summary>Durum rengiyle kalkan simgesi çizer (SF Symbols olmadığı için elle).</summary>
    private static Icon Draw(GuardStateKind kind)
    {
        var color = kind switch
        {
            GuardStateKind.Disarmed => Color.FromArgb(0x9A, 0xA6, 0xB2),
            GuardStateKind.Arming => Color.FromArgb(0xFB, 0xBF, 0x24),
            GuardStateKind.Armed => Color.FromArgb(0x34, 0xD3, 0x99),
            GuardStateKind.Warning => Color.FromArgb(0xFB, 0xBF, 0x24),
            _ => Color.FromArgb(0xEF, 0x44, 0x44),
        };
        const int s = 32;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var shield = new[]
            {
                new PointF(16, 1.5f), new PointF(29, 6), new PointF(29, 16),
                new PointF(16, 30.5f), new PointF(3, 16), new PointF(3, 6),
            };
            using var fill = new SolidBrush(color);
            g.FillPolygon(fill, shield);
            using var dark = new SolidBrush(Color.FromArgb(11, 15, 20));
            if (kind is GuardStateKind.Warning or GuardStateKind.Alarming)
            {
                // Ünlem
                g.FillRectangle(dark, 14.5f, 8, 3, 11);
                g.FillEllipse(dark, 14.3f, 21, 3.4f, 3.4f);
            }
            else
            {
                // Kilit
                using var pen = new Pen(Color.FromArgb(11, 15, 20), 2.6f);
                g.DrawArc(pen, 11.5f, 8.5f, 9, 9, 180, 180);
                if (kind == GuardStateKind.Disarmed) g.DrawLine(pen, 20.5f, 13, 20.5f, 11);
                else g.DrawLine(pen, 20.5f, 13, 20.5f, 15);
                g.DrawLine(pen, 11.5f, 13, 11.5f, 15);
                g.FillRectangle(dark, 10, 14.5f, 12, 9);
            }
        }
        var h = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(h).Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
