using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
namespace SentinelX.ViewModels;

public sealed record NavItem(string Key, string Icon, string Label, object ViewModel);

/// <summary>The single-window shell. Feature pages are opened only as transient panels over the Core.</summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IDesktopService desktop;
    private readonly IUiDispatcher dispatcher;
    private readonly CommandCenterViewModel commandCenter;
    private readonly IReadOnlyDictionary<string, (string Title, object Page)> panels;

    public string Version => AppConstants.Version;
    public IReadOnlyList<NavItem> NavItems { get; }
    public VoiceViewModel Voice { get; }
    public CommandCenterViewModel Center => commandCenter;
    public CommandPaletteViewModel Palette { get; }
    public ReadinessViewModel Readiness { get; }

    [ObservableProperty] private NavItem? selectedItem;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private object? overlayPage;
    [ObservableProperty] private string overlayTitle = "";
    [ObservableProperty] private bool isOverlayOpen;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private string desktopStatus = "";

    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, SystemViewModel system, GamingViewModel gaming,
        VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions, HistoryViewModel history, SettingsViewModel settings,
        CommandPaletteViewModel palette, ReadinessViewModel readiness, MemoryViewModel memory, ProjectViewModel projects, TaskViewModel tasks,
        DiagnosticViewModel diagnostics)
    {
        this.engine = engine;
        this.desktop = desktop;
        this.dispatcher = dispatcher;
        commandCenter = command;
        Voice = voice;
        Palette = palette;
        Readiness = readiness;

        // There is one permanent destination. Tools and settings are contextual panels, never top-level tabs.
        NavItems = [new NavItem("command", "◉", "Sentinel Core", command)];
        CurrentPage = command;
        SelectedItem = NavItems[0];
        Readiness.IsOpen = false;

        panels = new Dictionary<string, (string, object)>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = ("Ustawienia", settings),
            ["history"] = ("Historia", history),
            ["memory"] = ("Pamięć lokalna", memory),
            ["projects"] = ("Projekty", projects),
            ["tasks"] = ("Przypomnienia", tasks),
            ["system"] = ("System", system),
            ["gaming"] = ("Gry", gaming),
            ["voice"] = ("Głos i mikrofon", voice),
            ["ai"] = ("Model AI", ai),
            ["actions"] = ("Aktywność", actions),
            ["diagnostics"] = ("Diagnostyka", diagnostics)
        };

        Palette.Chosen += PaletteChosen;
        Readiness.OpenSectionRequested += Navigate;
        commandCenter.NavigationRequested += Navigate;
        engine.Changed += Sync;
        desktop.StatusChanged += DesktopChanged;
    }

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value != null) CurrentPage = value.ViewModel;
    }

    private void Navigate(string key)
    {
        if (string.Equals(key, "command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "home", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "rozmowa", StringComparison.OrdinalIgnoreCase))
        {
            CloseOverlay();
            return;
        }

        // Accept the short keys used by command/slash routing and map them to the same transient surface.
        string normalized = key.ToLowerInvariant() switch
        {
            "glos" => "voice",
            "gry" => "gaming",
            "zadania" => "tasks",
            "akcje" => "actions",
            "diagnostyka" => "diagnostics",
            "historia rozmow" => "history",
            _ => key
        };
        if (!panels.TryGetValue(normalized, out var panel)) return;

        OverlayTitle = panel.Title;
        OverlayPage = panel.Page;
        IsOverlayOpen = true;
    }

    private void PaletteChosen(Models.PaletteEntry entry)
    {
        if (entry.PageKey != null) Navigate(entry.PageKey);
        else if (entry.CommandText != null) { CloseOverlay(); commandCenter.StageCommand(entry.CommandText); }
    }

    [RelayCommand]
    private void OpenSettings() => Navigate("settings");

    [RelayCommand]
    private void OpenHistory() => Navigate("history");

    [RelayCommand]
    private void CloseOverlay()
    {
        IsOverlayOpen = false;
        OverlayPage = null;
        OverlayTitle = "";
    }

    [RelayCommand]
    private void OpenPalette()
    {
        CloseOverlay();
        Readiness.IsOpen = false;
        Palette.Open();
    }

    [RelayCommand]
    private void OpenReadiness()
    {
        CloseOverlay();
        Palette.CloseCommand.Execute(null);
        Readiness.IsOpen = true;
    }

    [RelayCommand] private Task InitializeAsync() => Readiness.RefreshCommand.ExecuteAsync(null);
    private void Sync() => dispatcher.Post(() => IsStopped = engine.IsStopped);
    private void DesktopChanged() => dispatcher.Post(() => DesktopStatus = desktop.Status);
    [RelayCommand] private void EmergencyStop() => engine.EmergencyStop();
    [RelayCommand] private void Resume() => engine.Resume();
    [RelayCommand] private void Exit() => desktop.Exit();

    public void Dispose()
    {
        engine.Changed -= Sync;
        desktop.StatusChanged -= DesktopChanged;
        Palette.Chosen -= PaletteChosen;
        Readiness.OpenSectionRequested -= Navigate;
        commandCenter.NavigationRequested -= Navigate;
        Readiness.RefreshCommand.Cancel();
    }
}
