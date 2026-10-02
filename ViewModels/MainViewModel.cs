using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
using SentinelX.Services.Settings;
namespace SentinelX.ViewModels;
public sealed record NavItem(string Key, string Icon, string Label, object ViewModel);

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
    public VoiceViewModel Voice { get; }
    /// <summary>0.95: live CPU/RAM/GPU tiles in the sidebar, the same source as the System tab.</summary>
    public SystemViewModel System { get; }
    /// <summary>0.95: the Tools catalogue page (sidebar „Narzędzia” and the 🧰 tab in Centrum).</summary>
    public ToolsViewModel Tools { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("Dark", "🌙", "Ciemny"),
        new("Deep Dark", "⚫", "Czarny"),
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

    /// <summary>0.91 · CENTRUM: every former top-level page key now lives as an icon tab inside Centrum.
    /// Palette entries, readiness cards and legacy navigation keep working through this map.</summary>
    private static readonly Dictionary<string, string> CenterTabByLegacyKey = new(StringComparer.Ordinal)
    {
        ["command"] = "rozmowa", ["tasks"] = "zadania", ["history"] = "historia", ["voice"] = "glos",
        ["system"] = "system", ["gaming"] = "gry", ["ai"] = "ai", ["actions"] = "akcje", ["diagnostics"] = "diagnostyka",
        ["automations"] = "automations"
    };

    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, SystemViewModel system, GamingViewModel gaming,
        VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions, HistoryViewModel history, SettingsViewModel settings,
        CommandPaletteViewModel palette, ReadinessViewModel readiness, MemoryViewModel memory, ProjectViewModel projects, TaskViewModel tasks,
        DiagnosticViewModel diagnostics, ToolsViewModel tools, ISettingsService settingsService)
    {
        this.engine = engine; this.desktop = desktop; this.dispatcher = dispatcher; this.settings = settingsService;
        Voice = voice; Palette = palette; Readiness = readiness; commandCenter = command; System = system; Tools = tools;
        Palette.Chosen += PaletteChosen; Readiness.OpenSectionRequested += Navigate; commandCenter.NavigationRequested += Navigate;
        NavItems =
        [
            new NavItem("command", "⌘", "Centrum", command),
            new NavItem("tools", "🧰", "Narzędzia", tools),
            new NavItem("memory", "▤", "Pamięć", memory),
            new NavItem("projects", "▣", "Projekty", projects),
            new NavItem("settings", "⚙", "Ustawienia", settings)
        ];
        CurrentTheme = ThemeOptions.FirstOrDefault(option => option.Value == settingsService.Current.Ui.Theme) ?? ThemeOptions[0];
        SelectedItem = NavItems[0]; Readiness.IsOpen = command.Messages.Count == 0; engine.Changed += Sync; desktop.StatusChanged += DesktopChanged;
        // Referenced so DI keeps constructing the cached page VMs (they live inside Centrum's tabs now).
        _ = gaming; _ = ai; _ = actions; _ = history; _ = tasks; _ = diagnostics;
    }
    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value == null) return;
        CurrentPage = value.ViewModel;
        if (value.ViewModel is HistoryViewModel history && !history.RefreshCommand.IsRunning) history.RefreshCommand.Execute(null);
    }
    private void Navigate(string key)
    {
        if (CenterTabByLegacyKey.TryGetValue(key, out string? tab))
        {
            SelectedItem = NavItems[0]; // Centrum
            commandCenter.SelectTabCommand.Execute(tab);
            return;
        }
        SelectedItem = NavItems.FirstOrDefault(x => x.Key == key) ?? SelectedItem;
    }
    private void PaletteChosen(Models.PaletteEntry entry)
    {
        if (entry.PageKey != null) Navigate(entry.PageKey);
        else if (entry.CommandText != null) { Navigate("command"); commandCenter.StageCommand(entry.CommandText); }
    }
    /// <summary>0.95: switching the tile changes the palette, the Fluent mode and persists the choice;
    /// the change is visible immediately because the shell listens for settings changes.</summary>
    partial void OnCurrentThemeChanged(ThemeOption? value)
    {
        if (value == null || settings.Current.Ui.Theme == value.Value) return;
        settings.Current.Ui.Theme = value.Value;
        settings.Save();
        DesktopStatus = "Motyw: " + value.Label + " — zapisany w ustawieniach.";
    }

    [RelayCommand] private void OpenTools() { SelectedItem = NavItems.FirstOrDefault(item => item.Key == "tools") ?? SelectedItem; }
    [RelayCommand] private void OpenPalette() { Readiness.IsOpen = false; Palette.Open(); }
    [RelayCommand] private void OpenReadiness() { Palette.CloseCommand.Execute(null); Readiness.IsOpen = true; }
    [RelayCommand] private Task InitializeAsync() => Readiness.RefreshCommand.ExecuteAsync(null);
    private void Sync() => dispatcher.Post(() => IsStopped = engine.IsStopped);
    private void DesktopChanged() => dispatcher.Post(() => DesktopStatus = desktop.Status);
    [RelayCommand] private void EmergencyStop() => engine.EmergencyStop();
    [RelayCommand] private void Resume() => engine.Resume();
    [RelayCommand] private void Exit() => desktop.Exit();
    public void Dispose() { engine.Changed -= Sync; desktop.StatusChanged -= DesktopChanged; Palette.Chosen -= PaletteChosen; Readiness.OpenSectionRequested -= Navigate; commandCenter.NavigationRequested -= Navigate; Readiness.RefreshCommand.Cancel(); }
}
