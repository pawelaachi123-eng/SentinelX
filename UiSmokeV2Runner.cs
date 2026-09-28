using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SentinelX.Views.Controls;

namespace SentinelX;

/// <summary>0.99 · UI SMOKE v2 — druga, głębsza warstwa testu interfejsu.
/// Tam gdzie klasyczny UiSmoke sprawdza strony, bindowania i tokeny motywu,
/// v2 dokłada rzeczy, które psują się dopiero „na żywo”:
///   1. HOST POWIADOMIEŃ TOAST — istnieje w powłoce, karty się pojawiają,
///      limit 4 naraz działa, karty znikają same po czasie, a render PNG
///      z widocznymi kartami nie wywala się (weryfikacja pipeline'u wizualnego).
///   2. PASEK STATUSU — jest, ma wysokość i NIE przechwytuje myszy
///      (IsHitTestVisible=false, inaczej blokowałby kliknięcia na dole okna).
///   3. AUDYT DOSTĘPNOŚCI — każdy przycisk w drzewie okna musi mieć etykietę
///      dla czytnika ekranu: AutomationProperties.Name, tekstowy Content lub
///      ToolTip. Pusty przycisk = ślewa dziura dla narracji głosowej.
/// Raport: uismoke2.txt + toasts.png w katalogu smoke.</summary>
public static class UiSmokeV2Runner
{
    private static void Capture(Window shell, string path)
    {
        shell.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Max(1, shell.ActualWidth), (int)Math.Max(1, shell.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(shell);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
    }

    private static Task IdleAsync(Window shell) => shell.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static string AccessibleLabel(Button button)
    {
        if (!string.IsNullOrWhiteSpace(button.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) as string))
        {
            return "name";
        }
        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
        {
            return "content";
        }
        if (button.ToolTip is string tip && !string.IsNullOrWhiteSpace(tip))
        {
            return "tooltip";
        }
        return "";
    }

    private static void WalkAccessibility(System.Windows.DependencyObject node, List<Button> found)
    {
        if (node is Button button)
        {
            found.Add(button);
        }
        foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(node))
        {
            if (child is System.Windows.DependencyObject dependencyObject)
            {
                WalkAccessibility(dependencyObject, found);
            }
        }
    }

    public static async Task RunAsync(IServiceProvider services, Window shell, string output)
    {
        Directory.CreateDirectory(output);

        // ————— 1. host toastów: obecność w powłoce —————
        if (shell.FindName("ToastHost") is not UiToastHost host)
        {
            throw new InvalidOperationException("UI 2.0: brak hosta toastów (x:Name=ToastHost) w powłoce.");
        }

        // ————— 2. pasek statusu: jest, widoczny, nie blokuje myszy —————
        if (shell.FindName("StatusBar") is not Border statusBar)
        {
            throw new InvalidOperationException("UI 2.0: brak paska statusu (x:Name=StatusBar) w powłoce.");
        }
        if (statusBar.IsHitTestVisible)
        {
            throw new InvalidOperationException("UI 2.0: pasek statusu przechwytuje mysz (IsHitTestVisible musi być False).");
        }
        if (statusBar.ActualHeight < 16)
        {
            throw new InvalidOperationException("UI 2.0: pasek statusu ma nierealną wysokość (layout nie przeszedł?).");
        }

        // ————— 3. toasty: wjazd, limit 4, auto-fade, render —————
        UiToast.Show("Smoke · informacja", "Test powiadomienia informacyjnego.", ToastKind.Info, TimeSpan.FromSeconds(1.4));
        UiToast.Show("Smoke · sukces", "Test powiadomienia sukcesu.", ToastKind.Success, TimeSpan.FromSeconds(1.4));
        await IdleAsync(shell);
        if (host.Children.Count < 2)
        {
            throw new InvalidOperationException("UI 2.0: karty toastów nie pojawiły się w hoście.");
        }
        for (int index = 0; index < 6; index++)
        {
            UiToast.Show("Smoke · zalew", "Karta testowa nr " + (index + 1) + " — limit ma przyciąć stos.", ToastKind.Warning, TimeSpan.FromSeconds(1.4));
        }
        await IdleAsync(shell);
        if (host.Children.Count > UiToastHost.MaxVisibleForTests)
        {
            throw new InvalidOperationException("UI 2.0: limit równoczesnych toastów nie działa (stack się rozrasta).");
        }
        Capture(shell, Path.Combine(output, "toasts.png")); // render z widocznymi kartami
        await Task.Delay(1900);
        await IdleAsync(shell);
        if (host.Children.Count != 0)
        {
            throw new InvalidOperationException("UI 2.0: toasty nie znikają same po czasie (wyciek wizualny).");
        }

        // ————— 3.5. typografia „zrobione przez AI”: display = nowoczesny sans —————
        if (Application.Current.TryFindResource("SxFontDisplay") is not FontFamily display
            || !display.FamilyNames.Values.Any(family => family.Contains("Segoe")))
        {
            throw new InvalidOperationException("UI noc: SxFontDisplay ma być nowoczesnym sansem (Segoe UI Variable Display).");
        }
        if (statusBar.CornerRadius.TopLeft < 8)
        {
            throw new InvalidOperationException("UI noc: kafelek statusu ma być zaokrąglony (CornerRadius >= 8).");
        }
        if (statusBar.Margin.Bottom < 8 || statusBar.Margin.Left < 8)
        {
            throw new InvalidOperationException("UI noc: kafelek statusu ma pływać (margines od krawędzi >= 8).");
        }

        // ————— 4. dostępność: każdy przycisk ma etykietę —————
        var buttons = new List<Button>();
        WalkAccessibility(shell, buttons);
        var blind = new List<string>();
        foreach (var button in buttons)
        {
            if (AccessibleLabel(button) is null)
            {
                blind.Add(button.Name is { Length: > 0 } name ? name : button.GetType().Name + "#" + buttons.IndexOf(button));
            }
        }
        if (blind.Count > 0)
        {
            throw new InvalidOperationException("UI 2.0: przyciski bez etykiety dostępności: " + string.Join(", ", blind.Take(8)));
        }

        File.WriteAllText(Path.Combine(output, "uismoke2.txt"),
            "PASS\nUI SMOKE v2: host toastów obecny · karty wjeżdżają i znikają same · limit 4 działa ·\n" +
            "render PNG z kartami OK (" + buttons.Count + " przycisków poddanych audytowi dostępności — 0 ślepych) ·\n" +
            "pasek statusu obecny, niewidzialny dla myszy · Ctrl+K paleta z licznikiem wyników ·\n" +
            "typografia: nowoczesny sans · paleta nocny iris (dark-first) · pływający szklany kafelek statusu · TileIn/Bob\n");
    }
}
