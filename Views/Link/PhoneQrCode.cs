using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace SentinelX.Views.Link;

/// <summary>Small frozen QR bitmap shared by the Devices page and the compatibility phone window.</summary>
internal static class PhoneQrCode
{
    public static BitmapImage? Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            byte[] png;
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M))
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
