namespace SentinelX.Services.Desktop;

/// <summary>
/// Desktop lifetime contract. Same fully-qualified name as the WPF build, but bound
/// to a WinUI window — shared ViewModels (e.g. GamingViewModel) compile unchanged.
/// </summary>
public interface IDesktopService
{
    string Status { get; }
    event Action? StatusChanged;
    void Attach(Microsoft.UI.Xaml.Window window);
    void ShowWindow();
    void ToggleOverlay();
    void ToggleMiniMode();
    void Exit();
}
