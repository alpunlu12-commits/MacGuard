using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MacGuard.Core;
using MacGuard.Security;

namespace MacGuard.UI;

/// <summary>Küçük kalıcı pencereler: PIN belirleme ve korumayı kapatma.</summary>
public static class Dialogs
{
    private static Window MakeWindow(Window owner, string title, double width)
    {
        return new Window
        {
            Title = title,
            Owner = owner,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("Background"),
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
            FontSize = 13,
        };
    }

    /// <summary>İlk açılışta veya PIN değiştirirken kullanılan kurulum ekranı.</summary>
    public static void ShowPinSetup(Window owner)
    {
        var w = MakeWindow(owner, "PIN belirle", 440);
        var panel = new StackPanel { Margin = new Thickness(24) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = Glyphs.Lock,
            Style = (Style)Application.Current.FindResource("Icon"),
            FontSize = 22,
            Foreground = (Brush)Application.Current.FindResource("Safe"),
            Margin = new Thickness(0, 0, 10, 0),
        });
        header.Children.Add(new TextBlock { Text = "PIN belirle", FontSize = 20, FontWeight = FontWeights.Bold });
        panel.Children.Add(header);

        panel.Children.Add(new TextBlock
        {
            Text = "Alarmı yalnızca bu PIN durdurabilir. En az 4 rakam gir ve unutma — PIN düz metin olarak hiçbir yere yazılmaz, yalnızca geri çevrilemez bir özeti saklanır.",
            Style = (Style)Application.Current.FindResource("Secondary"),
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var first = new PasswordBox { Margin = new Thickness(0, 4, 0, 10) };
        var second = new PasswordBox { Margin = new Thickness(0, 4, 0, 10) };
        panel.Children.Add(new TextBlock { Text = "Yeni PIN", Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock { Text = "PIN tekrar", Foreground = (Brush)Application.Current.FindResource("TextSecondary") });
        panel.Children.Add(second);

        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("Danger"),
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Vazgeç", Style = (Style)Application.Current.FindResource("SmallButton"), IsCancel = true };
        var save = new Button
        {
            Content = "Kaydet",
            Style = (Style)Application.Current.FindResource("SmallButton"),
            Background = (Brush)Application.Current.FindResource("Safe"),
            Margin = new Thickness(0),
            IsDefault = true,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        save.Click += (_, _) =>
        {
            var digits = first.Password.Trim();
            if (digits.Length < 4 || !digits.All(char.IsAsciiDigit))
            {
                error.Text = "PIN en az 4 rakam olmalı.";
                error.Visibility = Visibility.Visible;
                return;
            }
            if (digits != second.Password.Trim())
            {
                error.Text = "İki PIN aynı değil.";
                error.Visibility = Visibility.Visible;
                return;
            }
            PinStore.Set(digits);
            EventLog.Shared.Log("PIN güncellendi", "", Glyphs.Lock, Severity.Info);
            w.DialogResult = true;
        };

        w.Content = panel;
        w.Loaded += (_, _) => first.Focus();
        w.ShowDialog();
    }

    /// <summary>Korumayı kapatmak için PIN soran pencere.</summary>
    public static void ShowDisarm(Window owner)
    {
        var w = MakeWindow(owner, "Korumayı kapat", 380);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var pad = new PinPad { Title = "Korumayı kapatmak için PIN gir" };
        pad.OnSubmit = pin =>
        {
            var ok = GuardEngine.Shared.Disarm(pin);
            if (ok) w.Close();
            return ok;
        };
        panel.Children.Add(pad);

        var cancel = new Button
        {
            Content = "Vazgeç",
            Style = (Style)Application.Current.FindResource("LinkButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
            IsCancel = true,
        };
        cancel.Click += (_, _) => w.Close();
        panel.Children.Add(cancel);

        w.Content = panel;
        w.ShowDialog();
    }
}
