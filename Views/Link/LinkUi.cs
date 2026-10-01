using SentinelX.Core;
using SentinelX.Services.Link;

namespace SentinelX.Views.Link;

/// <summary>The WPF side of the phone link: shows the pairing request and the phone panel on the UI thread.</summary>
public sealed class LinkUi : ILinkApprovalUi
{
    private readonly IUiDispatcher dispatcher;
    private readonly Func<LinkService> link;
    private PairingWindow? pairing;
    private PhoneLinkWindow? phone;

    public LinkUi(IUiDispatcher dispatcher, Func<LinkService> link)
    {
        this.dispatcher = dispatcher;
        this.link = link;
    }

    public void ShowRequest(PairingRequestInfo request, Action<bool> decide) => dispatcher.Post(() =>
    {
        pairing?.Close();
        var window = new PairingWindow(request, decide);
        pairing = window;
        window.Closed += (_, _) => { if (ReferenceEquals(pairing, window)) pairing = null; };
        window.Show();
        window.Activate();
    });

    public void CloseRequest(string requestId) => dispatcher.Post(() =>
    {
        if (pairing != null && pairing.RequestId == requestId) pairing.Close();
    });

    public void ShowPhoneWindow() => dispatcher.Post(() =>
    {
        if (phone == null)
        {
            var window = new PhoneLinkWindow(link());
            phone = window;
            window.Closed += (_, _) => { if (ReferenceEquals(phone, window)) phone = null; };
            window.Show();
        }
        phone.WindowState = System.Windows.WindowState.Normal;
        phone.Activate();
    });
}
