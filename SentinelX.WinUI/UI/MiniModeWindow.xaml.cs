using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SentinelX.WinUI.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace SentinelX.WinUI.UI;

public sealed partial class MiniModeWindow : Window
{
    public MiniModeViewModel ViewModel { get; }

    public MiniModeWindow(MiniModeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        AppWindow.Title = "Sentinel X — Mini";
        AppWindow.Resize(new SizeInt32(420, 340));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }
    }

    private void CommandBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string text = args.ChosenSuggestion as string ?? args.QueryText;
        ViewModel.SubmitCommand.Execute(text);
    }

    private void CommandBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.ExpandCommand.Execute(null);
            e.Handled = true;
        }
    }
}
