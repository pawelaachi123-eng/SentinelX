using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SentinelX.ViewModels;
using Windows.System;

namespace SentinelX.WinUI.UI.Dialogs;

public sealed partial class CommandPalette : UserControl
{
    public CommandPalette()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private CommandPaletteViewModel? tracked;

    private void Subscribe()
    {
        if (tracked != null) tracked.PropertyChanged -= OnVmChanged;
        tracked = DataContext as CommandPaletteViewModel;
        if (tracked != null) tracked.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && tracked?.IsOpen == true)
        {
            QueryBox.Text = "";
            QueryBox.Focus(FocusState.Programmatic);
        }
    }

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (tracked == null) return;
        switch (e.Key)
        {
            case VirtualKey.Up:
                tracked.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                tracked.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                tracked.ChooseCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                tracked.CloseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e) => tracked?.ChooseCommand.Execute(null);

    private void Card_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void Dim_Tapped(object sender, TappedRoutedEventArgs e) => tracked?.CloseCommand.Execute(null);
}
