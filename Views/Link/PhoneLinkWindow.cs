using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using SentinelX.Services.Link;

namespace SentinelX.Views.Link;

/// <summary>Everything about the phone on one screen: the address (as a QR code for iPhone or any browser), how to connect an Android phone,
/// the list of paired phones and a button to disconnect them all. Built in code so it follows the theme without new XAML.</summary>
public sealed class PhoneLinkWindow : Window
{
    private readonly LinkService link;
    private readonly TextBlock status = new();
    private readonly TextBlock addresses = new();
    private readonly Image qr = new() { Width = 190, Height = 190, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel devices = new();

    public PhoneLinkWindow(LinkService link)
    {
        this.link = link;
        Title = "Sentinel X — telefon";
        Width = 560;
        Height = 700;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SxBackground");

        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(Text("Telefon", 24, true, "SxTextPrimary"));
        panel.Children.Add(status);
        status.Margin = new Thickness(0, 4, 0, 14);
        status.TextWrapping = TextWrapping.Wrap;
        status.SetResourceReference(TextBlock.ForegroundProperty, "SxTextSecondary");

        panel.Children.Add(Text("Android", 15, true, "SxAccentCyan"));
        panel.Children.Add(Text("Zainstaluj aplikację „Sentinel X Telefon” (plik SentinelX-Phone.apk z wydania) i ją otwórz. Sama znajdzie ten komputer — tu wystarczy jedno kliknięcie „Zezwól”.", 13, false, "SxTextSecondary", new Thickness(0, 4, 0, 14)));

        panel.Children.Add(Text("iPhone albo przeglądarka", 15, true, "SxAccentCyan"));
        panel.Children.Add(Text("Zeskanuj kod aparatem telefonu albo wpisz adres. Przeglądarka ostrzeże o certyfikacie — to normalne, bo jest własny i działa tylko w Twojej sieci (Zaawansowane → Przejdź dalej).", 13, false, "SxTextSecondary", new Thickness(0, 4, 0, 0)));
        panel.Children.Add(qr);
        addresses.Margin = new Thickness(0, 8, 0, 14);
        addresses.TextWrapping = TextWrapping.Wrap;
        addresses.SetResourceReference(TextBlock.ForegroundProperty, "SxTextPrimary");
        panel.Children.Add(addresses);

        panel.Children.Add(Text("Sparowane telefony", 15, true, "SxAccentCyan"));
        panel.Children.Add(devices);

        var disconnect = new Button { Content = "Odłącz wszystkie telefony", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        disconnect.Style = TryFindResource("SxDangerButton") as Style;
        disconnect.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Odłączyć wszystkie telefony? Każdy będzie musiał ponownie uzyskać zgodę na komputerze.", "Sentinel X", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                && !link.RemoveAllDevices())
                MessageBox.Show(this, link.DeviceStoreError ?? "Nie udało się trwale odłączyć telefonów. Spróbuj ponownie.", "Sentinel X", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        panel.Children.Add(disconnect);
        panel.Children.Add(Text("Jeśli Windows zapyta o zaporę, kliknij „Zezwól” dla sieci prywatnych. Sentinel nigdy nie łączy się z telefonem przez internet.", 12, false, "SxTextSecondary", new Thickness(0, 16, 0, 0)));

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        link.Changed += OnChanged;
        Closed += (_, _) => link.Changed -= OnChanged;
        Loaded += (_, _) => Refresh();
    }

    private static TextBlock Text(string text, double size, bool bold, string brushKey, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
        if (bold) block.FontWeight = FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private void OnChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        IReadOnlyList<string> urls = link.Urls;
        string connectionStatus = link.IsRunning
            ? "Łącze z telefonem działa i czeka w Twojej sieci domowej."
            : "Łącze z telefonem jest wyłączone (Ustawienia → Telefon) albo jeszcze się uruchamia.";
        status.Text = string.IsNullOrWhiteSpace(link.DeviceStoreError)
            ? connectionStatus
            : connectionStatus + "\n\nOstrzeżenie: " + link.DeviceStoreError;
        addresses.Text = urls.Count == 0 ? "Brak adresu w sieci — połącz komputer z Wi‑Fi lub kablem." : string.Join("\n", urls);
        qr.Source = urls.Count == 0 ? null : RenderQr(urls[0]);
        qr.Visibility = qr.Source == null ? Visibility.Collapsed : Visibility.Visible;

        devices.Children.Clear();
        IReadOnlyList<LinkDeviceInfo> list = link.Devices;
        if (list.Count == 0) devices.Children.Add(Text("Jeszcze żaden telefon — otwórz aplikację na telefonie.", 13, false, "SxTextSecondary", new Thickness(0, 6, 0, 0)));
        foreach (LinkDeviceInfo device in list)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(Text(device.Name, 14, true, "SxTextPrimary"));
            details.Children.Add(Text($"połączony {device.AddedAt.LocalDateTime:dd.MM.yyyy HH:mm} · ostatnio widziany {device.LastSeen.LocalDateTime:dd.MM HH:mm}", 12, false, "SxTextSecondary"));
            row.Children.Add(details);
            var disconnectOne = new Button { Content = "Odłącz", MinWidth = 76, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            disconnectOne.Style = TryFindResource("SxSecondaryButton") as Style;
            disconnectOne.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Odłączyć „{device.Name}”? Telefon będzie musiał ponownie uzyskać zgodę.", "Sentinel X", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                    && !link.RemoveDevice(device.Id))
                    MessageBox.Show(this, link.DeviceStoreError ?? "Nie udało się trwale odłączyć telefonu. Spróbuj ponownie.", "Sentinel X", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            Grid.SetColumn(disconnectOne, 1);
            row.Children.Add(disconnectOne);
            devices.Children.Add(row);
        }
    }

    private static BitmapImage? RenderQr(string text)
    {
        try
        {
            byte[] png;
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M))
                png = new PngByteQRCode(data).GetGraphic(8);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return null;
        }
    }
}
