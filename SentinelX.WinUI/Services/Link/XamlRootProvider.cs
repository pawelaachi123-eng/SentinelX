using Microsoft.UI.Xaml;

namespace SentinelX.WinUI.Services.Link;

/// <summary>Carries the shell XamlRoot to dialogs (pairing, phone panel). Set once the shell activates.</summary>
public sealed class XamlRootProvider
{
    public XamlRoot? Root { get; set; }
}
