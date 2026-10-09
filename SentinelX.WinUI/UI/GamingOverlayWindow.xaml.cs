using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SentinelX.ViewModels;
using Windows.Graphics;

namespace SentinelX.WinUI.UI;

/// <summary>
/// Tiny always-on-top metrics strip for games. Read-only: Sentinel never touches
/// process priorities or game settings.
/// </summary>
public sealed partial class GamingOverlayWindow : Window
{
    public OverlayViewModel ViewModel { get; }

    public GamingOverlayWindow(OverlayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        AppWindow.Title = "Sentinel X";
        AppWindow.Resize(new SizeInt32(380, 64));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
    }
}
