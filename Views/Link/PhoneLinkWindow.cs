using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using SentinelX.Services.Link;

namespace SentinelX.Views.Link;

/// <summary>Compatibility phone-link window with the same live state and safe confirmation flow as the Devices page.</summary>
public sealed class PhoneLinkWindow : Window
{
    private readonly LinkService link;
    private readonly TextBlock status = new();
    private readonly TextBlock addresses = new();
    private readonly Image qr = new() { Width = 176, Height = 176, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel devices = new();
    private readonly Border confirmation = new();
    private readonly TextBlock confirmationText = new();
    private Button confirmationCancel = new();
    private Button? disconnectAllButton;
    private IInputElement? confirmationReturnFocus;
    private string? pendingDeviceId;
    private string? statusNotice;
    private bool disconnectAll;

    public PhoneLinkWindow(LinkService link)
    {
        this.link = link;
        Title = "Sentinel X — urządzenia";
        Width = 760;
        Height = 760;
        MinWidth = 560;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "SxBackground");
        FontFamily = TryFindResource("SxFontFamily") as FontFamily ?? new FontFamily("Segoe UI");
        AutomationProperties.SetName(this, "Połączenie Sentinel X z telefonem");

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = CreateButton("Zamknij", "SxGhostButton");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var heading = new StackPanel();
        heading.Children.Add(Text("Urządzenia", 24, true, "SxTextPrimary"));
        heading.Children.Add(Text("Połączenie z telefonem w sieci lokalnej", 12, false, "SxTextSecondary", new Thickness(0, 3, 0, 0)));
        header.Children.Add(heading);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new StackPanel();
        content.Children.Add(BuildStatusCard());
        content.Children.Add(BuildPairingCard());
        content.Children.Add(BuildDevicesCard());
        content.Children.Add(Card(Text("Telefon działa wyłącznie w sieci prywatnej. Zaufanie jest nadawane dopiero po ręcznej zgodzie na tym komputerze; zapisany token nie jest przechowywany.", 12, false, "SxTextSecondary"), new Thickness(0, 0, 0, 12)));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = Text("Android może znaleźć komputer automatycznie. iPhone lub inna przeglądarka może otworzyć lokalny adres; ostrzeżenie o własnym certyfikacie jest oczekiwane.", 11, false, "SxTextSecondary", new Thickness(0, 12, 0, 0));
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        link.Changed += OnChanged;
        Closed += (_, _) => link.Changed -= OnChanged;
        Loaded += (_, _) => Refresh();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && confirmation.Visibility == Visibility.Visible)
            {
                CloseConfirmation();
                e.Handled = true;
            }
        };
    }

    private Border BuildStatusCard()
    {
        status.TextWrapping = TextWrapping.Wrap;
        status.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetName(status, "Stan połączenia z telefonem");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        status.SetResourceReference(TextBlock.ForegroundProperty, "SxTextPrimary");
        var content = new StackPanel();
        content.Children.Add(Text("Stan połączenia", 15, true, "SxAccentCyan", new Thickness(0, 0, 0, 8)));
        content.Children.Add(status);
        return Card(content, new Thickness(0, 0, 0, 14));
    }

    private Border BuildPairingCard()
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var qrCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(7), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        qrCard.Child = qr;
        Grid.SetColumn(qrCard, 0);
        content.Children.Add(qrCard);

        var instructions = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        instructions.Children.Add(Text("Połącz telefon", 16, true, "SxTextPrimary"));
        instructions.Children.Add(Text("Zeskanuj kod QR albo wpisz adres poniżej w przeglądarce telefonu. Przy pierwszym połączeniu porównaj kod bezpieczeństwa na obu ekranach i dopiero wtedy zezwól.", 12, false, "SxTextSecondary", new Thickness(0, 6, 0, 12)));
        addresses.TextWrapping = TextWrapping.Wrap;
        addresses.IsReadOnly = true;
        addresses.AcceptsReturn = true;
        addresses.MinHeight = 48;
        addresses.SetResourceReference(TextBox.BackgroundProperty, "SxBackground");
        addresses.SetResourceReference(TextBox.ForegroundProperty, "SxTextPrimary");
        instructions.Children.Add(addresses);
        var copy = CreateButton("Kopiuj adres", "SxPrimaryButton");
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(0, 8, 0, 0);
        copy.Click += (_, _) => CopyAddress();
        instructions.Children.Add(copy);
        Grid.SetColumn(instructions, 1);
        content.Children.Add(instructions);
        return Card(content, new Thickness(0, 0, 0, 14));
    }

    private Border BuildDevicesCard()
    {
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var disconnect = CreateButton("Odłącz wszystkie", "SxDangerButton");
        disconnectAllButton = disconnect;
        DockPanel.SetDock(disconnect, Dock.Right);
        disconnect.Click += (_, _) => RequestDisconnectAll();
        header.Children.Add(disconnect);
        header.Children.Add(Text("Sparowane urządzenia", 16, true, "SxTextPrimary"));
        panel.Children.Add(header);
        panel.Children.Add(devices);

        confirmation.Padding = new Thickness(14);
        confirmation.Margin = new Thickness(0, 12, 0, 0);
        confirmation.CornerRadius = new CornerRadius(12);
        confirmation.BorderThickness = new Thickness(1);
        confirmation.SetResourceReference(Border.BackgroundProperty, "SxSurfaceActive");
        confirmation.SetResourceReference(Border.BorderBrushProperty, "SxWarning");
        confirmation.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(confirmation, "Potwierdzenie odłączenia telefonu");
        var confirmPanel = new StackPanel();
        confirmPanel.Children.Add(Text("Potwierdź odłączenie", 15, true, "SxTextPrimary"));
        confirmationText.TextWrapping = TextWrapping.Wrap;
        confirmationText.Margin = new Thickness(0, 6, 0, 12);
        confirmationText.SetResourceReference(TextBlock.ForegroundProperty, "SxTextSecondary");
        confirmPanel.Children.Add(confirmationText);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = CreateButton("Anuluj", "SxGhostButton");
        confirmationCancel = cancel;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => CloseConfirmation();
        var approve = CreateButton("Odłącz", "SxDangerButton");
        AutomationProperties.SetName(approve, "Potwierdź odłączenie telefonu");
        approve.Click += (_, _) => ConfirmDisconnect();
        buttons.Children.Add(cancel);
        buttons.Children.Add(approve);
        confirmPanel.Children.Add(buttons);
        confirmation.Child = confirmPanel;
        panel.Children.Add(confirmation);
        return Card(panel, new Thickness(0, 0, 0, 14));
    }

    private static Border Card(UIElement child, Thickness margin)
    {
        var card = new Border { Margin = margin };
        card.Style = Application.Current.TryFindResource("SxCard") as Style;
        card.Child = child;
        return card;
    }

    private static TextBlock Text(string text, double size, bool bold, string brushKey, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
        if (bold) block.FontWeight = FontWeights.SemiBold;
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private Button CreateButton(string text, string styleKey)
    {
        var button = new Button { Content = text, MinHeight = 40, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(button, text);
        button.Style = TryFindResource(styleKey) as Style;
        return button;
    }

    private void OnChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        IReadOnlyList<string> urls = link.Urls;
        string connectionStatus = link.IsRunning
            ? "Łącze działa i czeka w sieci lokalnej · port " + link.Port + "."
            : "Łącze jest wyłączone albo jeszcze się uruchamia (Ustawienia → Telefon).";
        status.Text = (string.IsNullOrWhiteSpace(link.DeviceStoreError)
            ? connectionStatus
            : connectionStatus + "\n\nOstrzeżenie: " + link.DeviceStoreError)
            + (string.IsNullOrWhiteSpace(statusNotice) ? "" : "\n\n" + statusNotice);
        addresses.Text = urls.Count == 0 ? "Brak adresu w sieci — połącz komputer z Wi‑Fi lub Ethernetem." : string.Join(Environment.NewLine, urls);
        qr.Source = urls.Count == 0 ? null : PhoneQrCode.Create(urls[0]);
        qr.Visibility = qr.Source == null ? Visibility.Collapsed : Visibility.Visible;

        devices.Children.Clear();
        IReadOnlyList<LinkDeviceInfo> paired = link.Devices;
        if (paired.Count == 0)
            devices.Children.Add(Text("Nie ma jeszcze sparowanych telefonów. Nowe urządzenie musi zostać ręcznie zaakceptowane.", 12, false, "SxTextSecondary"));
        foreach (LinkDeviceInfo device in paired)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(Text(device.Name, 14, true, "SxTextPrimary"));
            details.Children.Add(Text($"Połączono {device.AddedAt.LocalDateTime:dd.MM.yyyy HH:mm} · ostatnio widziano {device.LastSeen.LocalDateTime:dd.MM.yyyy HH:mm}", 11, false, "SxTextSecondary", new Thickness(0, 3, 0, 0)));
            details.Children.Add(Text("Zgłoszone funkcje: " + device.CapabilitiesText, 11, false, "SxTextSecondary", new Thickness(0, 3, 0, 0)));
            row.Children.Add(details);

            var remove = CreateButton("Odłącz", "SxGhostButton");
            remove.MinWidth = 76;
            remove.Foreground = TryFindResource("SxError") as Brush;
            remove.VerticalAlignment = VerticalAlignment.Center;
            remove.Margin = new Thickness(12, 0, 0, 0);
            AutomationProperties.SetName(remove, $"Odłącz urządzenie {device.Name}");
            remove.Click += (_, _) => RequestDisconnect(device.Id, device.Name, remove);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            devices.Children.Add(row);
        }
    }

    private void RequestDisconnectAll()
    {
        if (link.Devices.Count == 0)
        {
            SetStatusNotice("Nie ma sparowanych telefonów do odłączenia.");
            return;
        }
        pendingDeviceId = null;
        disconnectAll = true;
        confirmationReturnFocus = disconnectAllButton;
        confirmationText.Text = "Odłączyć wszystkie telefony? Każdy będzie musiał ponownie uzyskać zgodę.";
        ShowConfirmation();
    }

    private void RequestDisconnect(string id, string name, IInputElement returnFocus)
    {
        pendingDeviceId = id;
        disconnectAll = false;
        confirmationReturnFocus = returnFocus;
        confirmationText.Text = $"Odłączyć „{name}”? Zapisane zaufanie zostanie unieważnione.";
        ShowConfirmation();
    }

    private void ShowConfirmation()
    {
        confirmation.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => confirmationCancel.Focus()));
    }

    private void ConfirmDisconnect()
    {
        string target = disconnectAll ? "wszystkie telefony" : "telefon";
        bool removed = disconnectAll ? link.RemoveAllDevices() : pendingDeviceId != null && link.RemoveDevice(pendingDeviceId);
        CloseConfirmation(restoreFocus: false);
        Refresh();
        SetStatusNotice(!removed
            ? link.DeviceStoreError ?? $"Nie udało się odłączyć {target}. Spróbuj ponownie."
            : $"Odłączono {target}. Przy następnym połączeniu telefon poprosi o zgodę.");
    }

    private void CloseConfirmation(bool restoreFocus = true)
    {
        confirmation.Visibility = Visibility.Collapsed;
        pendingDeviceId = null;
        disconnectAll = false;
        if (restoreFocus && confirmationReturnFocus is FrameworkElement target && target.IsVisible && target.IsEnabled)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Keyboard.Focus(target)));
        confirmationReturnFocus = null;
    }

    private void SetStatusNotice(string message)
    {
        statusNotice = message;
        Refresh();
    }

    private void CopyAddress()
    {
        string? address = link.Urls.FirstOrDefault();
        if (address == null) { SetStatusNotice("Brak adresu do skopiowania."); return; }
        try { Clipboard.SetText(address); SetStatusNotice("Adres skopiowany do schowka."); }
        catch (Exception ex) { SetStatusNotice("Nie udało się skopiować adresu (" + ex.GetType().Name + "). Zaznacz go ręcznie."); }
    }
}
