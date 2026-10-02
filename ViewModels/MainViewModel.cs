using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
using SentinelX.Services.Settings;
using SentinelX.Services.Performance;
namespace SentinelX.ViewModels;
public sealed record NavItem(string Key, string Icon, string Label, object ViewModel, string Category = "Main");

/// <summary>0.95: one tile of the live theme switcher in the sidebar (value = what settings store).</summary>
public sealed record ThemeOption(string Value, string Icon, string Label);
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IDesktopService desktop;
    private readonly IUiDispatcher dispatcher;
    private readonly ISettingsService settings;
    public string Version => AppConstants.Version;
    public IReadOnlyList<NavItem> NavItems { get; }
    public DashboardViewModel Dashboard { get; }
    public VoiceViewModel Voice { get; }
    /// <summary>0.95: live CPU/RAM/GPU tiles in the sidebar, the same source as the System tab.</summary>
    public SystemViewModel System { get; }
    /// <summary>0.95: the Tools catalogue page (sidebar „Narzędzia” and the 🧰 tab in Centrum).</summary>
    public ToolsViewModel Tools { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("Dark", "🌙", "Ciemny"),
        new("Light", "☀", "Jasny"),
        new("System", "🖥", "Jak Windows")
    ];
    [ObservableProperty] private ThemeOption? currentTheme;
    public CommandPaletteViewModel Palette { get; }
    public ReadinessViewModel Readiness { get; }
    private readonly CommandCenterViewModel commandCenter;
    [ObservableProperty] private NavItem? selectedItem;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private string desktopStatus = "";
    [ObservableProperty] private bool sidebarCollapsed;

    /// <summary>0.91 · CENTRUM: every former top-level page key now lives as an icon tab inside Centrum.</summary>
    private static readonly Dictionary<string, string> CenterTabByLegacyKey = new(StringComparer.Ordinal)
    {
        ["command"] = "rozmowa", ["tasks"] = "zadania", ["history"] = "historia", ["voice"] = "glos",
        ["system"] = "system", ["gaming"] = "gry", ["ai"] = "ai", ["actions"] = "akcje", ["diagnostics"] = "diagnostyka",
        ["chat"] = "rozmowa", ["ai"] = "ai", ["home"] = "home"
    };

    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, SystemViewModel system, GamingViewModel gaming,
        VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions, HistoryViewModel history, SettingsViewModel settings,
        CommandPaletteViewModel palette, ReadinessViewModel readiness, MemoryViewModel memory, ProjectViewModel projects, TaskViewModel tasks,
        DiagnosticViewModel diagnostics, ToolsViewModel tools, ISettingsService settingsService,
        DashboardViewModel dashboard, NotificationService notifications, SxThemeService theme)
    {
        this.engine = engine; this.desktop = desktop; this.dispatcher = dispatcher; this.settings = settingsService;
        Voice = voice; Palette = palette; Readiness = readiness; commandCenter = command; System = system; Tools = tools; Dashboard = dashboard;
        Palette.Chosen += PaletteChosen; Readiness.OpenSectionRequested += Navigate; commandCenter.NavigationRequested += Navigate;
        dashboard.NavigateRequested += Navigate;
        NavItems =
        [
            new NavItem("home",      "⌂",  "Pulpit",        dashboard, "Main"),
            new NavItem("command",   "💬", "Sentinel AI",   command,   "Main"),
            new NavItem("voice",     "🎙", "Głos",          voice,     "Main"),
            new NavItem("tasks",     "📋", "Zadania",       tasks,     "Automation"),
            new NavItem("actions",   "⚡", "Automatyzacje", actions,   "Automation"),
            new NavItem("memory",    "🧠", "Pamięć",        memory,    "AI"),
            new NavItem("projects",  "▣", "Projekty",      projects,  "AI"),
            new NavItem("tools",     "🧰", "Narzędzia",     tools,     "System"),
            new NavItem("system",    "📊", "Wydajność",     system,    "System"),
            new NavItem("gaming",    "🎮", "Tryb Gry",      gaming,    "System"),
            new NavItem("diagnostics","🩺","Diagnostyka",   diagnostics, "System"),
            new NavItem("history",   "🕒", "Historia",      history,   "System"),
            new NavItem("ai",        "🤖", "AI Lokalne",    ai,        "AI"),
            new NavItem("settings",  "⚙", "Ustawienia",    settings,  "System")
        ];
        // Apply theme
        theme.Apply();
        CurrentTheme = ThemeOptions.FirstOrDefault(option => option.Value == settingsService.Current.Ui.Theme) ?? ThemeOptions[0];
        SidebarCollapsed = settingsService.Current.Ui.SidebarMode == SidebarCollapseMode.IconsOnly;
        SelectedItem = NavItems.FirstOrDefault(n => n.Key == settingsService.Current.Ui.SelectedPage) ?? NavItems[0];
        engine.Changed += Sync; desktop.StatusChanged += DesktopChanged;
        // Ping welcome notification
        notifications.Success("Sentinel X 2.0", "Silnik wielordzeniowy aktywny · Glass UI · Nowy pulpit.");
        // Keep references so DI holds VMs alive
        _ = diagnostics;
    }
    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value == null) return;
        CurrentPage = value.ViewModel;
        settings.Current.Ui.SelectedPage = value.Key;
        if (value.ViewModel is HistoryViewModel history && !history.RefreshCommand.IsRunning) history.RefreshCommand.Execute(null);
    }
    partial void OnSidebarCollapsedChanged(bool value)
    {
        settings.Current.Ui.SidebarMode = value ? SidebarCollapseMode.IconsOnly : SidebarCollapseMode.Expanded;
    }
    private void Navigate(string key)
    {
        if (key == "home") { SelectedItem = NavItems[0]; return; }
        if (CenterTabByLegacyKey.TryGetValue(key, out string? tab) && key == "command")
        {
            SelectedItem = NavItems.First(n => n.Key == "command");
            commandCenter.SelectTabCommand.Execute(tab);
            return;
        }
        var match = NavItems.FirstOrDefault(x => x.Key == key);
        if (match != null) { SelectedItem = match; return; }
        if (CenterTabByLegacyKey.TryGetValue(key, out _))
        {
            SelectedItem = NavItems.First(n => n.Key == "command");
        }
    }
    private void PaletteChosen(Models.PaletteEntry entry)
    {
        if (entry.PageKey != null) Navigate(entry.PageKey);
        else if (entry.CommandText != null) { Navigate("command"); commandCenter.StageCommand(entry.CommandText); }
    }
    /// <summary>0.95: switching the tile changes the palette, the Fluent mode and persists the choice.</summary>
    partial void OnCurrentThemeChanged(ThemeOption? value)
    {
        if (value == null || settings.Current.Ui.Theme == value.Value) return;
        settings.Current.Ui.Theme = value.Value;
        settings.Save();
        var theme = App.Services.GetService<SxThemeService>();
        theme?.Apply();
        DesktopStatus = "Motyw: " + value.Label + " — zapisany w ustawieniach.";
    }

    [RelayCommand] private void ToggleSidebar() => SidebarCollapsed = !SidebarCollapsed;
    [RelayCommand] private void OpenTools() { SelectedItem = NavItems.FirstOrDefault(item => item.Key == "tools") ?? SelectedItem; }
    [RelayCommand] private void OpenPalette() { Readiness.IsOpen = false; Palette.Open(); }
    [RelayCommand] private void OpenReadiness() { Palette.CloseCommand.Execute(null); Readiness.IsOpen = true; }
    [RelayCommand] private Task InitializeAsync() => Readiness.RefreshCommand.ExecuteAsync(null);
    private void Sync() => dispatcher.Post(() => IsStopped = engine.IsStopped);
    private void DesktopChanged() => dispatcher.Post(() => DesktopStatus = desktop.Status);
    [RelayCommand] private void EmergencyStop() => engine.EmergencyStop();
    [RelayCommand] private void Resume() => engine.Resume();
    [RelayCommand] private void Exit() => desktop.Exit();
    public void Dispose() { engine.Changed -= Sync; desktop.StatusChanged -= DesktopChanged; Palette.Chosen -= PaletteChosen; Readiness.OpenSectionRequested -= Navigate; commandCenter.NavigationRequested -= Navigate; Dashboard.NavigateRequested -= Navigate; Readiness.RefreshCommand.Cancel(); }
}
