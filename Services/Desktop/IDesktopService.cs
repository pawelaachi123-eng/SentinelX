using System.Windows;
namespace SentinelX.Services.Desktop;
public interface IDesktopService
{
    string Status { get; }
    event Action? StatusChanged;
    void Attach(Window window);
    void ShowWindow();
    void ToggleOverlay();
    void Exit();
}
