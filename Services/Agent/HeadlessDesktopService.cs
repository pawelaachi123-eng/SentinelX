using System.Windows;
using SentinelX.Services.Desktop;

namespace SentinelX.Services.Agent;

/// <summary>No-op desktop for the headless Agent: there is no window to show.</summary>
public sealed class HeadlessDesktopService : IDesktopService
{
    public string Status => "";
    public event Action? StatusChanged { add { } remove { } }
    public void Attach(Window window) { }
    public void ShowWindow() { }
    public void ToggleOverlay() { }
    public void Exit() { }
}
