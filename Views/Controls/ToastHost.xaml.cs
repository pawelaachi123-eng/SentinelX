using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Services.System;

namespace SentinelX.Views.Controls;

public partial class ToastHost : UserControl
{
    private readonly ObservableCollection<SentinelNotification> visible = [];
    public ToastHost()
    {
        InitializeComponent(); ToastItems.ItemsSource = visible;
        Loaded += (_, _) =>
        {
            var svc = App.Services.GetService<NotificationService>();
            if (svc == null) return;
            svc.Toast += n => Dispatcher.BeginInvoke(() =>
            {
                visible.Insert(0, n);
                if (!n.AutoDismiss) return;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                t.Tick += (_, _) => { visible.Remove(n); t.Stop(); }; t.Start();
            });
        };
    }
}
