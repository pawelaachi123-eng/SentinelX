using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SentinelX.ViewModels;

namespace SentinelX.Utilities;

/// <summary>Keyboard control for the „//” palette inside the chat TextBox. Tab must be intercepted in
/// PreviewKeyDown — WPF's focus navigation would otherwise eat it before any InputBinding can run.
/// Tab cycles down, Shift+Tab up, Enter picks the highlighted entry, Escape closes the palette.</summary>
public static class SlashKeys
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SlashKeys), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.PreviewKeyDown -= HandleKeyDown;
        if (e.NewValue is true) box.PreviewKeyDown += HandleKeyDown;
    }

    private static void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not CommandCenterViewModel vm || !vm.SlashOpen) return;
        switch (e.Key)
        {
            case Key.Tab:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) vm.SlashPrevCommand.Execute(null);
                else vm.SlashNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                if (vm.SlashIndex >= 0 && vm.SlashIndex < vm.SlashItems.Count)
                {
                    vm.SlashChooseCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                vm.SlashCloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                vm.SlashPrevCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                vm.SlashNextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
