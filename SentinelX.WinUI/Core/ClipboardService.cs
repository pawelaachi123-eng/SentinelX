using Windows.ApplicationModel.DataTransfer;

namespace SentinelX.WinUI.Core;

public interface IClipboardService
{
    void CopyText(string text);
}

public sealed class WinUiClipboardService : IClipboardService
{
    public void CopyText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            SentinelX.AppLog.Write(ex);
        }
    }
}
