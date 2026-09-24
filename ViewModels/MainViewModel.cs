using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
namespace SentinelX.ViewModels;
public sealed record NavItem(string Key, string Icon, string Label, object ViewModel);
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IDesktopService desktop;
    private readonly IUiDispatcher dispatcher;
    public string Version => AppConstants.Version;
    public IReadOnlyList<NavItem> NavItems { get; }
    public VoiceViewModel Voice { get; }
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
        ["system"] = "system", ["gaming"] = "gry", ["ai"] = "ai", ["actions"] = "akcje", ["diagnostics"] = "diagnostyka"
    };

    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, SystemViewModel system, GamingViewModel gaming,
        VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions, HistoryViewModel history, SettingsViewModel settings,
        CommandPaletteViewModel palette, ReadinessViewModel readiness, MemoryViewModel memory, ProjectViewModel projects, TaskViewModel tasks,
        DiagnosticViewModel diagnostics)
    {
        this.engine = engine; this.desktop = desktop; this.dispatcher = dispatcher; Voice = voice; Palette = palette; Readiness = readiness; commandCenter = command;
        Palette.Chosen += PaletteChosen; Readiness.OpenSectionRequested += Navigate; commandCenter.NavigationRequested += Navigate;
        NavItems =
        [
            new NavItem("command", "⌘", "Centrum", command),
            new NavItem("memory", "▤", "Pamięć", memory),
            new NavItem("projects", "▣", "Projekty", projects),
            new NavItem("settings", "⚙", "Ustawienia", settings)
        ];
        SelectedItem = NavItems[0]; Readiness.IsOpen = command.Messages.Count == 0; engine.Changed += Sync; desktop.StatusChanged += DesktopChanged;
        // Referenced so DI keeps constructing the cached page VMs (they live inside Centrum's tabs now).
        _ = system; _ = gaming; _ = ai; _ = actions; _ = history; _ = tasks; _ = diagnostics;
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
