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
    [ObservableProperty] private NavItem? selectedItem;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private string desktopStatus = "";
    public MainViewModel(IActionEngine engine, IDesktopService desktop, IUiDispatcher dispatcher,
        CommandCenterViewModel command, SystemViewModel system, GamingViewModel gaming,
        VoiceViewModel voice, AiViewModel ai, ActionsViewModel actions, HistoryViewModel history, SettingsViewModel settings)
    {
        this.engine = engine; this.desktop = desktop; this.dispatcher = dispatcher; Voice = voice;
        NavItems = [new("command", "⌘", "Command Center", command), new("system", "▥", "System", system),
            new("gaming", "◇", "Gaming", gaming), new("voice", "◉", "Voice", voice), new("ai", "✧", "AI", ai),
            new("actions", "ϟ", "Actions", actions), new("history", "≡", "History", history), new("settings", "⚙", "Settings", settings)];
        SelectedItem = NavItems[0]; engine.Changed += Sync; desktop.StatusChanged += DesktopChanged;
    }
    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value == null) return;
        CurrentPage = value.ViewModel;
        if (value.ViewModel is HistoryViewModel history && !history.RefreshCommand.IsRunning) history.RefreshCommand.Execute(null);
    }
    private void Sync() => dispatcher.Post(() => IsStopped = engine.IsStopped);
    private void DesktopChanged() => dispatcher.Post(() => DesktopStatus = desktop.Status);
    [RelayCommand] private void EmergencyStop() => engine.EmergencyStop();
    [RelayCommand] private void Resume() => engine.Resume();
    [RelayCommand] private void Exit() => desktop.Exit();
    public void Dispose() { engine.Changed -= Sync; desktop.StatusChanged -= DesktopChanged; }
}
