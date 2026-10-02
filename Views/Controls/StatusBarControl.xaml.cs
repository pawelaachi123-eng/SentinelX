using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Core;
using SentinelX.Services.Monitoring;

namespace SentinelX.Views.Controls;
public partial class StatusBarControl : UserControl
{
    public StatusBarControl()
    {
        InitializeComponent(); Version.Text = AppConstants.Version;
        Loaded += (_, _) =>
        {
            var m = App.Services.GetService<ISystemMonitorService>();
            if (m == null) return;
            m.Updated += s => Dispatcher.BeginInvoke(() => PerfStatus.Text = $"CPU {(double.IsFinite(s.Cpu) ? s.Cpu.ToString("F0") + "%" : "—")} · RAM {(double.IsFinite(s.RamUsed) ? s.RamUsed.ToString("F0") + " GB" : "—")}");
        };
    }
}
