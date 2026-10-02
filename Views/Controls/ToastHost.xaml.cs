using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SentinelX.Services.System;

namespace SentinelX.Views.Controls;

public partial class ToastHost : UserControl
{
    private NotificationService? service;
    private readonly DispatcherTimer dismissTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly ObservableCollection<SentinelNotification> visible = [];

    public ToastHost()
    {
        InitializeComponent();
        ToastItems.ItemsSource = visible;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (service != null) return;
        service = App.Services.GetService<NotificationService>();
        if (service == null) return;
        service.Toast += n =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                visible.Insert(0, n);
                if (n.AutoDismiss)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    timer.Tick += (_, _) => { visible.Remove(n); timer.Stop(); };
                    timer.Start();
                }
            });
        };
    }
}
