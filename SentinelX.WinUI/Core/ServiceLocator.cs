using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Desktop;
using SentinelX.Services.History;
using SentinelX.Services.Intent;
using SentinelX.Services.Link;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.ViewModels;
using SentinelX.WinUI.Core;
using SentinelX.WinUI.Services.Link;
using SentinelX.WinUI.Services.Toasts;
using SentinelX.WinUI.UI;
using SentinelX.WinUI.ViewModels;

namespace SentinelX.Core;

/// <summary>
/// Dependency injection for the WinUI shell. Same backend registrations as the WPF
/// build (shared sources, one logic) plus the shell-only services and ViewModels.
/// </summary>
public static class ServiceLocator
{
    public static ServiceProvider Build(DispatcherQueue queue)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher>(new WinUiDispatcher(queue));
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
        services.AddSingleton<UnderstandingJournal>();
        services.AddSingleton<Services.Memory.MemoryActionService>(sp => new(sp.GetRequiredService<ConversationMemoryService>(),
            sp.GetRequiredService<Services.Permissions.IPermissionService>(), sp.GetRequiredService<ActionHistoryService>()));
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<HistoryExportService>();
        services.AddSingleton<Services.Memory.IConversationMemory>(sp => sp.GetRequiredService<ConversationMemoryService>());
        services.AddSingleton<Services.Engine.EngineService>(sp => new(isGaming: () => !string.IsNullOrEmpty(sp.GetRequiredService<GamingModeService>().GetRunningGame()),
            totalRamGb: () => { double total = sp.GetRequiredService<SystemMonitor>().GetTotalRamGB(); return double.IsFinite(total) && total > 0 ? total : 8; },
            autoInstall: () => sp.GetRequiredService<ISettingsService>().Current.Ai.AutoInstallEngine));
        services.AddSingleton<Services.Engine.IEngineService>(sp => sp.GetRequiredService<Services.Engine.EngineService>());
        services.AddSingleton<LocalAiService>(sp =>
        {
            var engine = sp.GetRequiredService<Services.Engine.EngineService>();
            return new(sp.GetRequiredService<GamingModeService>(), handler: engine.CreateHandler(),
                systemMonitor: sp.GetRequiredService<SystemMonitor>(), aiSettingsProvider: () => sp.GetRequiredService<ISettingsService>().Current.Ai)
            { EngineDescribe = engine.Describe, RepairHandler = engine.RepairAsync };
        });
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
        services.AddSingleton<SpeechOutputService>(); // WinUI build: Windows.Media speech synthesis.
        services.AddSingleton<IVoiceService, VoiceService>();
        services.AddSingleton<AlertFeed>();
        services.AddSingleton<XamlRootProvider>();
        services.AddSingleton<WinUiLinkUi>(sp => new(sp.GetRequiredService<IUiDispatcher>(),
            () => sp.GetRequiredService<LinkService>(),
            () => sp.GetRequiredService<XamlRootProvider>().Root,
            sp.GetRequiredService<IClipboardService>()));
        services.AddSingleton<ILinkApprovalUi>(sp => sp.GetRequiredService<WinUiLinkUi>());
        services.AddSingleton<LinkService>(sp => new(new LinkApi(sp.GetRequiredService<IActionEngine>(), sp.GetRequiredService<ISystemMonitorService>(),
            sp.GetRequiredService<TaskService>(), sp.GetRequiredService<ConversationMemoryService>(), sp.GetRequiredService<AlertFeed>(),
            () => sp.GetRequiredService<Services.Care.CareService>().BuildLinkInfo(), () => sp.GetRequiredService<LinkService>().Urls),
            sp.GetRequiredService<ILinkApprovalUi>(), () => sp.GetRequiredService<ISettingsService>().Current.Link));
        services.AddSingleton<Services.Care.CareService>();
        // WinUI shell services.
        services.AddSingleton<IClipboardService, WinUiClipboardService>();
        services.AddSingleton<GamingPolicyService>();
        services.AddSingleton<ICoreStateService, CoreStateService>();
        services.AddSingleton<CommandExecutor>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<IDesktopService, WinUiDesktopService>();
        // Shared backend ViewModels reused by the shell.
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<VoiceViewModel>();
        services.AddSingleton<AiViewModel>();
        services.AddSingleton<GamingViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<OverlayViewModel>();
        // Shell-only ViewModels.
        services.AddSingleton<TelemetryViewModel>();
        services.AddSingleton<CommandBarViewModel>();
        services.AddSingleton<DetailPanelViewModel>();
        services.AddSingleton<SettingsPanelViewModel>();
        services.AddSingleton<HistoryPanelViewModel>();
        services.AddSingleton<MiniModeViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<ShellWindow>();
        services.AddTransient<MiniModeWindow>();
        services.AddTransient<GamingOverlayWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    
    /// <summary>
    /// Independent headless PC Agent.
    /// No window, no automatic privileged approvals.
    /// </summary>
    public static ServiceProvider BuildAgent()
    {
        var services = new ServiceCollection();

        services.AddSingleton<SystemMonitor>();
        services.AddSingleton<GamingModeService>();

        services.AddSingleton<
            SentinelX.Services.Base.OllamaSupervisor>();

        services.AddSingleton<
            SentinelX.Services.Link.AlertFeed>();

        services.AddSingleton<
            SentinelX.Services.Desktop.IDesktopService,
            SentinelX.Services.Agent.HeadlessDesktopService>();

        services.AddSingleton<
            SentinelX.Services.Base.WindowsBaseService>();

        services.AddSingleton<
            SentinelX.Services.Base.IBaseControl>(
            sp => sp.GetRequiredService<
                SentinelX.Services.Base.WindowsBaseService>());

        services.AddSingleton<
            SentinelX.Services.Maintenance.MaintenanceService>();

        return services.BuildServiceProvider();
    }
    private static MemoryPrivacy MapPrivacy(MemorySettings s) =>
        new(s.SaveConversations, s.UseHistoryForAi, s.SaveMemories, s.UseMemoriesForAi, s.RetentionDays, s.ContextPreviewEnabled);
}
