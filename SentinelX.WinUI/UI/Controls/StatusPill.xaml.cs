using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SentinelX.WinUI.UI.Controls;

public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(""));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(StatusPill), new PropertyMetadata(null));

    public static readonly DependencyProperty PillCommandProperty =
        DependencyProperty.Register(nameof(PillCommand), typeof(ICommand), typeof(StatusPill), new PropertyMetadata(null));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public ICommand? PillCommand
    {
        get => (ICommand?)GetValue(PillCommandProperty);
        set => SetValue(PillCommandProperty, value);
    }

    public StatusPill() => InitializeComponent();
}
