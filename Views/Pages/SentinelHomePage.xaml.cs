using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentinelX.Models;
using SentinelX.ViewModels;

namespace SentinelX.Views.Pages;

public partial class SentinelHomePage : UserControl
{
    private readonly DispatcherTimer toastTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(9)
    };
    private CommandCenterViewModel? viewModel;

    public SentinelHomePage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        toastTimer.Tick += ToastTimer_Tick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsLoaded) AttachMessages(args.NewValue as CommandCenterViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachMessages(DataContext as CommandCenterViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        AttachMessages(null);
        toastTimer.Stop();
        ResponseToast.Visibility = Visibility.Collapsed;
    }

    private void AttachMessages(CommandCenterViewModel? next)
    {
        if (ReferenceEquals(viewModel, next)) return;
        if (viewModel != null) viewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
        viewModel = next;
        if (viewModel != null) viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var message = args.NewItems?.OfType<ConversationMessage>()
            .LastOrDefault(x => x.Role.Equals("sentinel", StringComparison.OrdinalIgnoreCase)
                             || x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (message == null) return;
        Dispatcher.BeginInvoke(new Action(() => ShowToast(message.Content)), DispatcherPriority.Background);
    }

    private void ShowToast(string text)
    {
        string clean = text.Trim();
        if (clean.Length > 420) clean = clean[..420].TrimEnd() + "…";
        if (clean.Length == 0) return;
        ToastText.Text = clean;
        ResponseToast.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }

    private void DismissToast_Click(object sender, RoutedEventArgs args)
    {
        toastTimer.Stop();
        ResponseToast.Visibility = Visibility.Collapsed;
    }

    private void ToastTimer_Tick(object? sender, EventArgs args)
    {
        toastTimer.Stop();
        ResponseToast.Visibility = Visibility.Collapsed;
    }
}
