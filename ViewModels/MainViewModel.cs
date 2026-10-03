using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
using SentinelX.Services.Settings;

namespace SentinelX.ViewModels;

public sealed record NavItem(string Key, string Icon, string Label, string Group, object ViewModel);

/// <summary>A theme selector item; Value is the persisted setting key.</summary>
public sealed record ThemeOption(string Value, string Icon, string Label);

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IDesktopService desktop;
    private readonly IUiDispatcher dispatcher;
    private readonly ISettingsService settings;
    private readonly CommandCenterViewModel commandCenter;
    private readonly HomeViewModel home;
    private readonly ProjectViewModel projectPage;

    public string Version => AppConstants.Version;
    public IReadOnlyList<NavItem> NavItems { get; }
    public ICollectionView NavigationView { get; }
    public VoiceViewModel Voice { get; }
    public SystemViewModel System { get; }
    public ToolsViewModel Tools { get; }
    public HomeViewModel Home => home;
    public DevicesViewModel Devices { get; }
    public NotificationsViewModel Notifications { get; }
    public CommandPaletteViewModel Palette { get; }
    public ReadinessViewModel Readiness { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("Dark", "🌙", "Ciemny"),
        new("Deep Dark", "◐", "Głęboka czerń"),
        new("Light", "☀", "Jasny"),
        new("System", "▣", "Jak Windows")
    ];

    [ObservableProperty] private ThemeOption? currentTheme;
    [ObservableProperty] private NavItem? selectedItem;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private bool isSidebarCollapsed;
    [ObservableProperty] private string desktopStatus = "";

    public GridLength SidebarWidth => IsSidebarCollapsed ? new GridLength(76) : new GridLength(248);

    private static readonly Dictionary<string, string> NavigationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chat"] = "command", ["centrum"] = "command", ["rozmowa"] = "command",
        ["automations"] = "automation", ["narzedzia"] = "tools", ["glos"] = "voice",
        ["gry"] = "gaming", ["diagnostyka"] = "diagnostics", ["historia"] = "history",
        ["ustawienia"] = "settings", ["gotowosc"] = "readiness"
    };

    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, HomeViewModel home, SystemViewModel system,
        GamingViewModel gaming, VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions,
        HistoryViewModel history, SettingsViewModel settingsPage, CommandPaletteViewModel palette,
        ReadinessViewModel readiness, MemoryViewModel memory, ProjectViewModel projects,
        TaskViewModel tasks, DiagnosticViewModel diagnostics, ToolsViewModel tools,
        AutomationViewModel automation, DevicesViewModel devices, NotificationsViewModel notifications,
        ISettingsService settingsService)
    {
        this.engine = engine;
        this.desktop = desktop;
        this.dispatcher = dispatcher;
        settings = settingsService;
        commandCenter = command;
        this.home = home;
        projectPage = projects;
        Voice = voice;
        System = system;
        Tools = tools;
        Devices = devices;
        Notifications = notifications;
        Palette = palette;
        Readiness = readiness;

        NavItems =
        [
            new("home", "⌂", "Start", "PULPIT", home),
            new("command", "✦", "Asystent", "PULPIT", command),

            new("system", "▦", "Komputer", "KOMPUTER", system),
            new("gaming", "ϟ", "Wydajność", "KOMPUTER", gaming),
            new("voice", "◉", "Głos", "KOMPUTER", voice),
            new("devices", "⌁", "Urządzenia", "KOMPUTER", devices),

            new("automation", "⏱", "Automatyzacje", "PRACA", automation),
            new("tasks", "☷", "Zadania", "PRACA", tasks),
            new("actions", "↗", "Akcje", "PRACA", actions),
            new("history", "◷", "Aktywność", "PRACA", history),

            new("ai", "✧", "AI lokalne", "NARZĘDZIA I DANE", ai),
            new("diagnostics", "⌕", "Diagnostyka", "NARZĘDZIA I DANE", diagnostics),
            new("tools", "⌘", "Narzędzia", "NARZĘDZIA I DANE", tools),
            new("projects", "▣", "Projekty", "NARZĘDZIA I DANE", projects),
            new("memory", "▤", "Pamięć", "NARZĘDZIA I DANE", memory),

            new("notifications", "◌", "Powiadomienia", "SYSTEM", notifications),
            new("settings", "⚙", "Ustawienia", "SYSTEM", settingsPage)
        ];

        NavigationView = CollectionViewSource.GetDefaultView(NavItems);
        NavigationView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
        CurrentTheme = ThemeOptions.FirstOrDefault(option => option.Value == settingsService.Current.Ui.Theme) ?? ThemeOptions[0];
        SelectedItem = NavItems[0];
        // Readiness refreshes at startup, but stays non-modal so the Home dashboard remains the first visible surface.
        Readiness.IsOpen = false;

        engine.Changed += Sync;
        settings.Changed += SettingsChanged;
        desktop.StatusChanged += DesktopChanged;
        Palette.Chosen += PaletteChosen;
        Readiness.OpenSectionRequested += Navigate;
        commandCenter.NavigationRequested += Navigate;
        home.NavigationRequested += Navigate;
        projectPage.NavigationRequested += Navigate;
    }

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value == null) return;
        CurrentPage = value.ViewModel;
        if (value.ViewModel is HistoryViewModel history && !history.RefreshCommand.IsRunning)
            history.RefreshCommand.Execute(null);
    }

    partial void OnIsSidebarCollapsedChanged(bool value) => OnPropertyChanged(nameof(SidebarWidth));

    private void Navigate(string key)
    {
        string resolved = NavigationAliases.GetValueOrDefault(key, key);
        if (resolved == "readiness")
        {
            OpenReadiness();
            return;
        }
        SelectedItem = NavItems.FirstOrDefault(item => string.Equals(item.Key, resolved, StringComparison.OrdinalIgnoreCase)) ?? SelectedItem;
    }

    private void PaletteChosen(Models.PaletteEntry entry)
    {
        if (entry.PageKey != null) Navigate(entry.PageKey);
        else if (entry.CommandText != null)
        {
            Navigate("command");
            commandCenter.StageCommand(entry.CommandText);
        }
    }

    partial void OnCurrentThemeChanged(ThemeOption? value)
    {
        if (value == null || settings.Current.Ui.Theme == value.Value) return;
        string previous = settings.Current.Ui.Theme;
        settings.Current.Ui.Theme = value.Value;
        settings.Save();
        if (settings.LastError is { } failure)
        {
            settings.Current.Ui.Theme = previous;
            CurrentTheme = ThemeOptions.FirstOrDefault(option => option.Value == previous);
            DesktopStatus = failure;
            return;
        }
        DesktopStatus = "Motyw „" + value.Label + "” zapisany.";
    }

    [RelayCommand] private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;
    [RelayCommand] private void OpenTools() => Navigate("tools");
    [RelayCommand] private void OpenNotifications() => Navigate("notifications");
    [RelayCommand] private void ToggleVoice()
    {
        if (Voice.State == Models.VoiceState.Off) Voice.StartCommand.Execute(null);
        else Voice.StopCommand.Execute(null);
    }
    [RelayCommand] private void OpenPalette() { Readiness.IsOpen = false; Palette.Open(); }
    [RelayCommand] private void OpenReadiness() { Palette.CloseCommand.Execute(null); Readiness.IsOpen = true; }
    [RelayCommand] private Task InitializeAsync() => Readiness.RefreshCommand.ExecuteAsync(null);

    private void Sync() => dispatcher.Post(() => IsStopped = engine.IsStopped);
    private void SettingsChanged() => dispatcher.Post(() =>
    {
        ThemeOption? matching = ThemeOptions.FirstOrDefault(option => string.Equals(option.Value, settings.Current.Ui.Theme, StringComparison.OrdinalIgnoreCase));
        if (matching != null && CurrentTheme?.Value != matching.Value) CurrentTheme = matching;
    });
    private void DesktopChanged() => dispatcher.Post(() => DesktopStatus = desktop.Status);
    [RelayCommand] private void EmergencyStop() => engine.EmergencyStop();
    [RelayCommand] private void Resume() => engine.Resume();
    [RelayCommand] private void Exit() => desktop.Exit();

    public void Dispose()
    {
        engine.Changed -= Sync;
        settings.Changed -= SettingsChanged;
        desktop.StatusChanged -= DesktopChanged;
        Palette.Chosen -= PaletteChosen;
        Readiness.OpenSectionRequested -= Navigate;
        commandCenter.NavigationRequested -= Navigate;
        home.NavigationRequested -= Navigate;
        projectPage.NavigationRequested -= Navigate;
        Readiness.RefreshCommand.Cancel();
    }
}
