using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using SentinelX.Services.Link;

namespace SentinelX.Views.Link;

/// <summary>"Telefon prosi o połączenie" — the single click that pairs a phone. Built in code (no XAML) and themed through resource references.
/// Closing the window without an answer counts as "no". Allow stays disabled for two seconds so a window that pops up while the user is
/// typing or playing cannot be confirmed by accident.</summary>
public sealed class PairingWindow : Window
{
    private readonly Action<bool> decide;
    private readonly DateTimeOffset expires;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock countdown = new();
    private readonly Button allow = new();
    private int elapsed;
    private bool answered;

    public string RequestId { get; }

    public PairingWindow(PairingRequestInfo info, Action<bool> decide)
    {
        RequestId = info.Id;
        this.decide = decide;
        expires = info.ExpiresAt;

        Title = "Sentinel X — telefon prosi o połączenie";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        SetResourceReference(BackgroundProperty, "SxBackground");
        FontFamily = TryFindResource("SxFontFamily") as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily("Segoe UI");
        AutomationProperties.SetName(this, "Telefon prosi o połączenie z Sentinel X");

        string code = info.Sas.Length == 6 ? info.Sas[..3] + " " + info.Sas[3..] : info.Sas;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(Text("Telefon prosi o połączenie", 22, true, "SxTextPrimary"));
        panel.Children.Add(Text($"Urządzenie: {info.DeviceName}", 14, false, "SxTextSecondary", new Thickness(0, 10, 0, 0)));
        panel.Children.Add(Text($"Adres w sieci: {info.RemoteAddress}", 13, false, "SxTextSecondary", new Thickness(0, 2, 0, 0)));
        panel.Children.Add(Text($"Zgłoszone funkcje (informacyjnie): {info.Capabilities.Describe()}", 12, false, "SxTextSecondary", new Thickness(0, 2, 0, 0)));

        var codeBox = new Border { Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        codeBox.SetResourceReference(Border.BackgroundProperty, "SxSurface");
        codeBox.SetResourceReference(Border.BorderBrushProperty, "SxBorder");
        var codeStack = new StackPanel();
        codeStack.Children.Add(Text("KOD POTWIERDZENIA", 11, true, "SxTextSecondary"));
        TextBlock big = Text(code, 44, true, "SxAccentCyan", new Thickness(0, 6, 0, 6));
        big.HorizontalAlignment = HorizontalAlignment.Center;
        codeStack.Children.Add(big);
        codeStack.Children.Add(Text("Na telefonie powinien być taki sam kod. Jeśli się różni, kliknij Odrzuć — ktoś w sieci może podszywać się pod telefon.", 12.5, false, "SxTextSecondary"));
        codeBox.Child = codeStack;
        panel.Children.Add(codeBox);

        countdown.Margin = new Thickness(0, 14, 0, 0);
        countdown.FontSize = 12.5;
        countdown.SetResourceReference(TextBlock.ForegroundProperty, "SxTextSecondary");
        panel.Children.Add(countdown);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var deny = new Button { Content = "Odrzuć", Margin = new Thickness(0, 0, 10, 0), MinWidth = 110, IsCancel = true };
        AutomationProperties.SetName(deny, "Odrzuć prośbę o połączenie");
        deny.Style = TryFindResource("SxSecondaryButton") as Style;
        deny.Click += (_, _) => Answer(false);
        allow.Content = "Zezwól";
        allow.MinWidth = 130;
        allow.IsDefault = true;
        allow.IsEnabled = false;
        AutomationProperties.SetName(allow, "Zezwól temu telefonowi na połączenie");
        allow.Style = TryFindResource("SxPrimaryButton") as Style;
        allow.Click += (_, _) => Answer(true);
        buttons.Children.Add(deny);
        buttons.Children.Add(allow);
        panel.Children.Add(buttons);
        Content = panel;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Answer(false); };
        Closing += (_, _) =>
        {
            if (answered) return;
            answered = true;
            timer.Stop();
            Report(false);
        };
        timer.Tick += (_, _) => OnTick();
        Loaded += (_, _) => { OnTick(); timer.Start(); deny.Focus(); };
    }

    private static TextBlock Text(string text, double size, bool bold, string brushKey, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
        if (bold) block.FontWeight = FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private void OnTick()
    {
        elapsed++;
        if (elapsed >= 3) allow.IsEnabled = true;
        int left = (int)Math.Max(0, (expires - DateTimeOffset.Now).TotalSeconds);
        countdown.Text = $"Prośba wygaśnie za {left} s.";
        if (left == 0 && !answered) Close();
    }

    private void Answer(bool approved)
    {
        if (answered) return;
        answered = true;
        timer.Stop();
        Report(approved);
        Close();
    }

    private void Report(bool approved)
    {
        try { decide(approved); }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
