using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Desktop;
using SentinelX.Services.History;
using SentinelX.Services.Intent;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.ViewModels;
namespace SentinelX.Core;
public static class ServiceLocator
{
    public static ServiceProvider Build(Dispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcher));
        services.AddSingleton<AppSettingsService>(_ => new());
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<SystemMonitor>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<GamingModeService>();
        services.AddSingleton<NetworkDiagnosticService>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<ActionHistoryService>(_ => new());
        services.AddSingleton<ConversationMemoryService>(_ => new());
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<LocalAiService>(sp => new(sp.GetRequiredService<GamingModeService>(),
            systemMonitor: sp.GetRequiredService<SystemMonitor>(), aiSettingsProvider: () => sp.GetRequiredService<ISettingsService>().Current.Ai));
        services.AddSingleton<IAiService, AiService>();
        services.AddSingleton<CommandRouter>();
        services.AddSingleton<SentinelToolboxService>(sp => new(() => sp.GetRequiredService<ISettingsService>().Current.Ui.DefaultBrowserPreference, sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<FileWorkspaceService>(sp => new(history: sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<IIntentRouter, IntentRouter>();
        services.AddSingleton<IActionEngine, ActionEngine>();
        services.AddSingleton<VoiceRecognitionService>(sp => new(() => sp.GetRequiredService<ISettingsService>().Current.Voice));
        services.AddSingleton<SpeechOutputService>();
        services.AddSingleton<IVoiceService, VoiceService>();
        services.AddSingleton<IDesktopService, DesktopService>();
        // Cached page VMs preserve drafts, selections and subscriptions across navigation.
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<VoiceViewModel>();
        services.AddSingleton<CommandCenterViewModel>();
        services.AddSingleton<GamingViewModel>();
        services.AddSingleton<AiViewModel>();
        services.AddSingleton<ActionsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Views.MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
