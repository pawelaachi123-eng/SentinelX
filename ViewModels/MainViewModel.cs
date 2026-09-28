using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
using SentinelX.Views.Controls;
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
    private readonly WebAccessService web = WebAccessService.Shared;
    /// <summary>0.99 · TOASTY: ważne zmiany stanu (WiFi, stop awaryjny, wznowienie)
/// wpadają w rogu ekranu jako zwinne powiadomienie — nie tylko w sidebare.</summary>
private static void Toast(string title, string message, ToastKind kind) => UiToast.Show(title, message, kind);

public bool WifiEnabled => web.Enabled;
    public string WifiLabel => web.Enabled ? "WiFi: wł. — szukanie w sieci działa" : "WiFi: wył. — wszystko lokalnie";
    public System.Windows.Media.Brush WifiBrush => (System.Windows.Media.Brush?)System.Windows.Application.Current?.TryFindResource(web.Enabled ? "SxAccentCyan" : "SxTextSecondary") ?? System.Windows.Media.Brushes.Gray;
    [RelayCommand] private void ToggleWifi()
    {
        web.Toggle(!web.Enabled);
        Toast("Połączenie z siecią", web.Enabled
            ? "WiFi WŁĄCZONE — szukanie w sieci i modele zdalne działają."
            : "WiFi WYŁĄCZONE — wszystko zostaje na tym komputerze.", ToastKind.Info);
    }
    private void OnWebChanged() { OnPropertyChanged(nameof(WifiEnabled)); OnPropertyChanged(nameof(WifiLabel)); OnPropertyChanged(nameof(WifiBrush)); }

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
        DiagnosticViewModel diagnostics, StudioViewModel studio)
    {
        this.engine = engine; this.desktop = desktop; this.dispatcher = dispatcher; Voice = voice; Palette = palette; Readiness = readiness; commandCenter = command;
        Palette.Chosen += PaletteChosen; Readiness.OpenSectionRequested += Navigate; commandCenter.NavigationRequested += Navigate; web.StateChanged += OnWebChanged;
        NavItems =
        [
            new NavItem("command", "⌘", "Centrum", command),
            new NavItem("memory", "▤", "Pamięć", memory),
            new NavItem("projects", "▣", "Projekty", projects),
            new NavItem("studio", "◈", "Studio 0.97", studio),
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
        // 0.97 · STUDIO: paleta może wskazać kategorię („studio:analiza”), a nie tylko stronę.
        if (key.StartsWith("studio", StringComparison.Ordinal))
        {
            SelectedItem = NavItems.FirstOrDefault(x => x.Key == "studio") ?? SelectedItem;
            if (key.Length > "studio:".Length) (SelectedItem?.ViewModel as StudioViewModel)?.ShowCategory(key["studio:".Length..]);
            return;
        }
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
    [RelayCommand] private void EmergencyStop()
    {
        engine.EmergencyStop();
        Toast("STOP AWARYJNY", "Nowe akcje zablokowane, głos wyciszony. Ctrl+Shift+X lub „Wznów”, by przywrócić.", ToastKind.Error);
    }
    [RelayCommand] private void Resume()
    {
        engine.Resume();
        Toast("Sentinel wznowiony", "Akcje i głos znów działają. Przygotowany do poleceń.", ToastKind.Success);
    }
    [RelayCommand] private void Exit() => desktop.Exit();
    public void Dispose() { engine.Changed -= Sync; desktop.StatusChanged -= DesktopChanged; Palette.Chosen -= PaletteChosen; Readiness.OpenSectionRequested -= Navigate; commandCenter.NavigationRequested -= Navigate; Readiness.RefreshCommand.Cancel(); web.StateChanged -= OnWebChanged; }
}
