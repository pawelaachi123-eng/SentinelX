using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentinelX.Models;
using SentinelX.ViewModels;

namespace SentinelX.Views.Behaviors;

/// <summary>Owns the short-lived response toast behavior independently of the XAML page code-behind.</summary>
public sealed class ResponseToastBehavior
{
    private readonly FrameworkElement page;
    private readonly FrameworkElement toast;
    private readonly TextBlock textBlock;
    private readonly Button dismissButton;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(9) };
    private CommandCenterViewModel? viewModel;

    private ResponseToastBehavior(FrameworkElement page, FrameworkElement toast, TextBlock textBlock, Button dismissButton)
    {
        this.page = page;
        this.toast = toast;
        this.textBlock = textBlock;
        this.dismissButton = dismissButton;
        page.DataContextChanged += OnDataContextChanged;
        page.Loaded += OnLoaded;
        page.Unloaded += OnUnloaded;
        dismissButton.Click += OnDismiss;
        timer.Tick += OnTimerTick;
    }

    public static void Attach(FrameworkElement page, FrameworkElement toast, TextBlock textBlock, Button dismissButton) =>
        _ = new ResponseToastBehavior(page, toast, textBlock, dismissButton);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (page.IsLoaded) AttachMessages(args.NewValue as CommandCenterViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => AttachMessages(page.DataContext as CommandCenterViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        AttachMessages(null);
        timer.Stop();
        toast.Visibility = Visibility.Collapsed;
    }

    private void AttachMessages(CommandCenterViewModel? next)
    {
        if (ReferenceEquals(viewModel, next)) return;
        if (viewModel != null) viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        viewModel = next;
        if (viewModel != null) viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var message = args.NewItems?.OfType<ConversationMessage>()
            .LastOrDefault(x => x.Role.Equals("sentinel", StringComparison.OrdinalIgnoreCase)
                             || x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (message == null) return;
        _ = page.Dispatcher.BeginInvoke(new Action(() => ShowToast(message.Content)), DispatcherPriority.Background);
    }

    private void ShowToast(string value)
    {
        string clean = value.Trim();
        if (clean.Length > 420) clean = clean[..420].TrimEnd() + "…";
        if (clean.Length == 0) return;
        textBlock.Text = clean;
        toast.Visibility = Visibility.Visible;
        timer.Stop();
        timer.Start();
    }

    private void OnDismiss(object sender, RoutedEventArgs args) => Dismiss();

    private void OnTimerTick(object? sender, EventArgs args) => Dismiss();

    private void Dismiss()
    {
        timer.Stop();
        toast.Visibility = Visibility.Collapsed;
    }
}
