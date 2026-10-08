using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SentinelX.WinUI.ViewModels;
using Windows.System;

namespace SentinelX.WinUI.UI;

public sealed partial class ShellWindow : Window
{
    public ShellViewModel ViewModel { get; }
    private bool minimized;

    public ShellWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragZone);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Title = "Sentinel X";

        ViewModel.Commands.PropertyChanged += OnCommandsChanged;
        ViewModel.GamingPolicy.PropertyChanged += OnPolicyChanged;
        AppWindow.Changed += OnAppWindowChanged;
        VisibilityChanged += OnVisibilityChanged;
        Closed += OnClosed;
        UpdateCoreAnimations();
    }

    private void PaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.OpenPaletteCommand.Execute(null);
        args.Handled = true;
    }

    private void CommandBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string text = args.ChosenSuggestion is CommandSuggestion chosen ? chosen.Text : args.QueryText;
        ViewModel.Commands.SubmitCommand.Execute(text);
    }

    private void CommandBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (CommandBox.Text.Length == 0 && e.Key == VirtualKey.Up)
        {
            ViewModel.Commands.PreviousHistory();
            e.Handled = true;
        }
        else if (CommandBox.Text.Length == 0 && e.Key == VirtualKey.Down)
        {
            ViewModel.Commands.NextHistory();
            e.Handled = true;
        }
    }

    private void OnCommandsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandBarViewModel.FocusVersion))
            CommandBox.Focus(FocusState.Programmatic);
    }

    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Core.GamingPolicyService.AnimationsAllowed)) UpdateCoreAnimations();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange)
        {
            minimized = sender.Presenter is OverlappedPresenter presenter &&
                        presenter.State == OverlappedPresenterState.Minimized;
            UpdateCoreAnimations();
        }
    }

    private void OnVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => UpdateCoreAnimations();

    private void UpdateCoreAnimations()
    {
        try
        {
            Core.AnimationsEnabled = ViewModel.GamingPolicy.AnimationsAllowed && Visible && !minimized;
        }
        catch
        {
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Commands.PropertyChanged -= OnCommandsChanged;
        ViewModel.GamingPolicy.PropertyChanged -= OnPolicyChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        VisibilityChanged -= OnVisibilityChanged;
        Closed -= OnClosed;
    }
}
