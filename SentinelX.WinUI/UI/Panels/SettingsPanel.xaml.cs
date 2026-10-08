using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SentinelX.WinUI.ViewModels;
using Windows.System;

namespace SentinelX.WinUI.UI.Panels;

public sealed partial class SettingsPanel : UserControl
{
    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(SettingsPanel), new PropertyMetadata(null));

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public SettingsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => PanelBorder.Translation = new System.Numerics.Vector3(0, 0, 24);
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible)
            {
                try
                {
                    SlideIn.Begin();
                }
                catch
                {
                }
            }
        });
    }

    private void Field_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is FrameworkElement element && element.DataContext is SettingRow row)
        {
            row.ApplyCommand.Execute(null);
            e.Handled = true;
        }
    }
}
