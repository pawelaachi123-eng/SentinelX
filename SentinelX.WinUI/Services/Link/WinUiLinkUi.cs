using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using SentinelX.Core;
using SentinelX.Services.Link;
using SentinelX.WinUI.Core;
using Windows.Storage.Streams;

namespace SentinelX.WinUI.Services.Link;

/// <summary>
/// Phone-link UI for the WinUI shell: pairing approvals and the phone panel (QR).
/// Same role as the WPF LinkUi, implemented with ContentDialogs.
/// </summary>
public sealed class WinUiLinkUi(
    IUiDispatcher dispatcher,
    Func<LinkService> link,
    Func<XamlRoot?> xamlRoot,
    IClipboardService clipboard) : ILinkApprovalUi
{
    private readonly Dictionary<string, ContentDialog> open = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Set by the desktop service on attach (same pattern as PhoneHint.Open).</summary>
    public Action ShowWindowAction { get; set; } = () => { };

    public void ShowRequest(PairingRequestInfo request, Action<bool> decide)
    {
        dispatcher.Post(() =>
        {
            XamlRoot? root = xamlRoot();
            if (root == null)
            {
                AppLog.Write(new InvalidOperationException("Brak XamlRoot — odrzucono parowanie."));
                try
                {
                    decide(false);
                }
                catch (Exception ex)
                {
                    AppLog.Write(ex);
                }

                return;
            }

            try
            {
                ShowWindowAction();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            bool settled = false;
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "Nowe urządzenie chce się połączyć",
                PrimaryButtonText = "Zezwól",
                CloseButtonText = "Odrzuć",
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = request.DeviceName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        new TextBlock { Text = "Adres: " + request.RemoteAddress, Opacity = 0.75 },
                        new TextBlock
                        {
                            Text = request.Sas,
                            FontSize = 34,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 8, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = "Porównaj kod z kodem na telefonie. Wygasa: " + request.ExpiresAt.LocalDateTime.ToString("HH:mm:ss"),
                            Opacity = 0.75,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            dialog.PrimaryButtonClick += (_, _) =>
            {
                settled = true;
                decide(true);
            };
            dialog.Closed += (_, _) =>
            {
                lock (gate) open.Remove(request.Id);
                if (!settled)
                {
                    try
                    {
                        decide(false);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write(ex);
                    }
                }
            };
            lock (gate) open[request.Id] = dialog;
            _ = dialog.ShowAsync();
        });
    }

    public void CloseRequest(string requestId)
    {
        dispatcher.Post(() =>
        {
            ContentDialog? dialog;
            lock (gate) open.TryGetValue(requestId, out dialog);
            dialog?.Hide();
        });
    }

    public void ShowPhoneWindow()
    {
        dispatcher.Post(() =>
        {
            XamlRoot? root = xamlRoot();
            if (root == null) return;
            try
            {
                ShowWindowAction();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            LinkService service;
            try
            {
                service = link();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                return;
            }

            string url = service.Urls.FirstOrDefault() ?? "";
            var qr = new Image { Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Center };
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(service.IsRunning
                ? new TextBlock { Text = "Zeskanuj kod aparatem telefonu (ta sama sieć Wi-Fi):", TextWrapping = TextWrapping.Wrap }
                : new TextBlock { Text = "Łącze z telefonem jest wyłączone. Włącz je w Ustawienia → Telefon.", TextWrapping = TextWrapping.Wrap });
            if (url.Length > 0) body.Children.Add(qr);
            var address = new TextBox { Text = url, IsReadOnly = true, FontSize = 12 };
            if (url.Length > 0) body.Children.Add(address);

            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "Sentinel w telefonie",
                Content = body,
                PrimaryButtonText = url.Length > 0 ? "Kopiuj adres" : null,
                CloseButtonText = "Zamknij"
            };
            if (url.Length > 0)
                dialog.PrimaryButtonClick += (_, _) => clipboard.CopyText(url);

            _ = dialog.ShowAsync();
            if (url.Length > 0) _ = LoadQrAsync(qr, url, body);
        });
    }

    private static async Task LoadQrAsync(Image target, string url, StackPanel fallbackParent)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            byte[] png = new PngByteQRCode(data).GetGraphic(20);
            var image = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            target.Source = image;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            fallbackParent.Children.Add(new TextBlock
            {
                Text = "Nie udało się wygenerować kodu QR — przepisz adres ręcznie.",
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}
