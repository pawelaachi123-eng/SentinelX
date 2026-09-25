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
        services.AddSingleton<Services.Network.INetworkService>(sp => sp.GetRequiredService<NetworkDiagnosticService>());
        services.AddSingleton<Services.Gaming.IGamingService>(sp => sp.GetRequiredService<GamingModeService>());
        services.AddSingleton<Services.Permissions.IPermissionService, PermissionCenterService>();
        services.AddSingleton<Services.Apps.IAppLauncherService>(sp => new AppLauncherService(() => sp.GetRequiredService<ISettingsService>().Current.Ui.DefaultBrowserPreference));
        services.AddSingleton<ProcessToolService>();
        services.AddSingleton<PcDiagnosticService>();
        services.AddSingleton<DiagnosticSnapshotService>(sp => new DiagnosticSnapshotService(sp.GetRequiredService<PcDiagnosticService>()));
        services.AddSingleton<ActionTaskRegistry>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<ActionHistoryService>(_ => new());
        services.AddSingleton<ProjectService>(_ => new ProjectService());
        services.AddSingleton<TaskService>(_ => new TaskService());
        services.AddSingleton<ConversationMemoryService>(sp => new ConversationMemoryService
        {
            PrivacyProvider = () => MapPrivacy(sp.GetRequiredService<ISettingsService>().Current.Memory),
            ActiveProjectIdProvider = () => sp.GetRequiredService<ProjectService>().ActiveProjectId,
        });
        services.AddSingleton<MemoryArchiveService>(sp => new MemoryArchiveService(sp.GetRequiredService<ConversationMemoryService>())
        {
            KeepMonthsProvider = () => sp.GetRequiredService<ISettingsService>().Current.Memory.ArchiveMonths
        });
        services.AddSingleton<WorkspaceInsightsService>(sp => new(sp.GetRequiredService<ConversationMemoryService>(), sp.GetRequiredService<TaskService>(),
            sp.GetRequiredService<ProjectService>(), sp.GetRequiredService<DiagnosticSnapshotService>(), sp.GetRequiredService<ActionHistoryService>(),
            sp.GetRequiredService<MemoryArchiveService>()));
        // 0.91: the local lessons journal („lekcje”) records typo repairs — deterministic learning.
        services.AddSingleton<UnderstandingJournal>();
        services.AddSingleton<Services.Memory.MemoryActionService>(sp => new(sp.GetRequiredService<ConversationMemoryService>(),
            sp.GetRequiredService<Services.Permissions.IPermissionService>(), sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<HistoryExportService>();
        services.AddSingleton<Services.Memory.IConversationMemory>(sp => sp.GetRequiredService<ConversationMemoryService>());
        services.AddSingleton<LocalAiService>(sp => new(sp.GetRequiredService<GamingModeService>(),
            systemMonitor: sp.GetRequiredService<SystemMonitor>(), aiSettingsProvider: () => sp.GetRequiredService<ISettingsService>().Current.Ai));
        services.AddSingleton<IAiService, AiService>();
        services.AddSingleton<CommandRouter>();
        services.AddSingleton<SentinelToolboxService>(sp => new(() => sp.GetRequiredService<ISettingsService>().Current.Ui.DefaultBrowserPreference, sp.GetRequiredService<ActionHistoryService>(),
            sp.GetRequiredService<Services.Permissions.IPermissionService>(), sp.GetRequiredService<Services.Apps.IAppLauncherService>(),
            sp.GetRequiredService<ProcessToolService>(), sp.GetRequiredService<Services.Network.INetworkService>(),
            sp.GetRequiredService<PcDiagnosticService>(), sp.GetRequiredService<ActionTaskRegistry>(),
            sp.GetRequiredService<ConversationMemoryService>(), sp.GetRequiredService<HistoryExportService>(),
            sp.GetRequiredService<Services.Memory.MemoryActionService>()));
        services.AddSingleton<FileWorkspaceService>(sp => new(history: sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<Services.Files.IFileService>(sp => sp.GetRequiredService<FileWorkspaceService>());
        services.AddSingleton<Services.Files.FileCleanupService>(sp => new(history: sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<ReadOnlyCommandService>();
        services.AddSingleton<IIntentRouter, IntentRouter>();
        services.AddSingleton<IActionEngine, ActionEngine>();
        services.AddSingleton<VoiceRecognitionService>(sp => new(() => sp.GetRequiredService<ISettingsService>().Current.Voice));
        services.AddSingleton<SpeechOutputService>();
        services.AddSingleton<IVoiceService, VoiceService>();
        services.AddSingleton<IDesktopService, DesktopService>();
        services.AddSingleton<Services.Readiness.IReadinessService, Services.Readiness.ReadinessService>();
        services.AddSingleton<ReadinessViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        // Cached page VMs preserve drafts, selections and subscriptions across navigation.
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<VoiceViewModel>();
        services.AddSingleton<CommandCenterViewModel>();
        services.AddSingleton<GamingViewModel>();
        services.AddSingleton<AiViewModel>();
        services.AddSingleton<ActionsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MemoryViewModel>();
        services.AddSingleton<ProjectViewModel>();
        services.AddSingleton<TaskViewModel>();
        services.AddSingleton<DiagnosticViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Views.MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    private static MemoryPrivacy MapPrivacy(MemorySettings s) =>
        new(s.SaveConversations, s.UseHistoryForAi, s.SaveMemories, s.UseMemoriesForAi, s.RetentionDays, s.ContextPreviewEnabled);
}
