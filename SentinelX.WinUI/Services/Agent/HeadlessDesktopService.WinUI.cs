using Microsoft.UI.Xaml;
using SentinelX.Services.Desktop;

namespace SentinelX.Services.Agent;

public sealed class HeadlessDesktopService : IDesktopService
{
    public string Status => "";

    public event Action? StatusChanged
    {
        add { }
        remove { }
    }

    public void Attach(Window window) { }

    public void ShowWindow() { }

    public void ToggleOverlay() { }

    public void ToggleMiniMode() { }

    public void Exit() { }
}