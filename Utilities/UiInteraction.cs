using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
namespace SentinelX.Utilities;

/// <summary>View-only keyboard behaviors: no service or ViewModel dependencies.</summary>
public static class UiInteraction
{
    public static readonly DependencyProperty LoadedCommandProperty = DependencyProperty.RegisterAttached(
        "LoadedCommand", typeof(ICommand), typeof(UiInteraction), new PropertyMetadata(null, LoadedCommandChanged));
    public static ICommand? GetLoadedCommand(DependencyObject d) => (ICommand?)d.GetValue(LoadedCommandProperty);
    public static void SetLoadedCommand(DependencyObject d, ICommand? value) => d.SetValue(LoadedCommandProperty, value);
    private static void LoadedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= Loaded; if (e.NewValue != null) element.Loaded += Loaded;
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        var element = (FrameworkElement)sender; element.Loaded -= Loaded;
        var command = GetLoadedCommand(element); if (command?.CanExecute(null) == true) command.Execute(null);
    }
    public static readonly DependencyProperty FocusOnVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusOnVisible", typeof(bool), typeof(UiInteraction), new PropertyMetadata(false, FocusChanged));
    public static bool GetFocusOnVisible(DependencyObject d) => (bool)d.GetValue(FocusOnVisibleProperty);
    public static void SetFocusOnVisible(DependencyObject d, bool value) => d.SetValue(FocusOnVisibleProperty, value);
    private static void FocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.IsVisibleChanged -= VisibleChanged;
        if ((bool)e.NewValue) { element.IsVisibleChanged += VisibleChanged; Focus(element); }
    }
    private static void VisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    { if (args.NewValue is true) Focus((FrameworkElement)sender); }
    public static readonly DependencyProperty FocusVersionProperty = DependencyProperty.RegisterAttached(
        "FocusVersion", typeof(int), typeof(UiInteraction), new PropertyMetadata(0, (d, _) => { if (d is FrameworkElement e) Focus(e); }));
    public static int GetFocusVersion(DependencyObject d) => (int)d.GetValue(FocusVersionProperty);
    public static void SetFocusVersion(DependencyObject d, int value) => d.SetValue(FocusVersionProperty, value);
    private static void Focus(FrameworkElement element) => element.Dispatcher.BeginInvoke(() =>
    {
        if (!element.IsVisible || !element.IsEnabled) return;
        Keyboard.Focus(element);
        if (element is TextBox input) input.CaretIndex = input.Text.Length;
    }, DispatcherPriority.Input);
    public static readonly DependencyProperty FollowSelectionProperty = DependencyProperty.RegisterAttached(
        "FollowSelection", typeof(bool), typeof(UiInteraction), new PropertyMetadata(false, FollowChanged));
    public static bool GetFollowSelection(DependencyObject d) => (bool)d.GetValue(FollowSelectionProperty);
    public static void SetFollowSelection(DependencyObject d, bool value) => d.SetValue(FollowSelectionProperty, value);
    private static void FollowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;
        list.SelectionChanged -= SelectionChanged;
        if ((bool)e.NewValue) list.SelectionChanged += SelectionChanged;
    }
    private static void SelectionChanged(object sender, SelectionChangedEventArgs args)
    { var list = (ListBox)sender; if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); }
}
