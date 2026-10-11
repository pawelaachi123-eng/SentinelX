using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.UI.Nexus;

/// <summary>
/// Native shell for the Nexus UI: Mica window + WebView2 rendering the GPU core and panels.
/// Source: SENTINEL_NEXUS_URL (dev server / hosted) or the bundled "nexus" folder (yarn build output).
/// </summary>
public sealed class NexusWindow : Window
{
    private const string VirtualHost = "nexus.sentinel";
    private readonly WebView2 _view = new() { DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 4, 22, 42) };
    private readonly NexusBridge _bridge;

    public NexusWindow(ICoreStateService core, CommandExecutor executor)
    {
        Title = "SENTINEL X";
        SystemBackdrop = new MicaBackdrop();
        Content = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 4, 22, 42)), Children = { _view } };

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "sentinel.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = bar.ButtonBackgroundColor = bar.InactiveBackgroundColor = bar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 4, 22, 42);
        bar.ForegroundColor = bar.ButtonForegroundColor = Colors.Gainsboro;

        _bridge = new NexusBridge(_view, core, executor, DispatcherQueue);
        VisibilityChanged += (_, e) => _bridge.NotifyVisibility(e.Visible);
        Closed += (_, _) => _bridge.Dispose();
        _ = InitAsync();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        await _bridge.AttachAsync();
        var url = Environment.GetEnvironmentVariable("SENTINEL_NEXUS_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "nexus");
            _view.CoreWebView2.SetVirtualHostNameToFolderMapping(VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            url = $"https://{VirtualHost}/index.html";
        }
        _view.CoreWebView2.ProcessFailed += (_, _) => _view.Reload();
        _view.Source = new Uri(url);
    }
}
