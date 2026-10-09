using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
using SentinelX.Services.Engine;
using SentinelX.Services.Voice;
using SentinelX.ViewModels;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.ViewModels;

/// <summary>
/// Root ViewModel of the WinUI shell. Aggregates the shared backend ViewModels
/// (system, voice, AI, gaming, palette) and the shell-only panels. No tabs —
/// details open as overlays next to the central Sentinel Core.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IEngineService aiEngine;
    private readonly IDesktopService desktop;
    private readonly GamingPolicyService gaming;
    private readonly IVoiceService voiceService;
    private bool disposed;

    public string ProductName => "Sentinel X";
    public string VersionLine => AppConstants.Version;

    public ICoreStateService Core { get; }
    public CommandBarViewModel Commands { get; }
    public TelemetryViewModel Telemetry { get; }
    public DetailPanelViewModel Details { get; }
    public SettingsPanelViewModel SettingsPanel { get; }
    public HistoryPanelViewModel History { get; }
    public MiniModeViewModel Mini { get; }
    public CommandPaletteViewModel Palette { get; }
    public VoiceViewModel Voice { get; }
    public GamingViewModel Gaming { get; }

    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private string micText = "MIC …";
    [ObservableProperty] private bool micActive;
    [ObservableProperty] private string aiText = "AI …";
    [ObservableProperty] private bool aiOk;
    [ObservableProperty] private string watchText = "WATCH …";
    [ObservableProperty] private bool watchOk = true;
    [ObservableProperty] private string ollamaText = "OLLAMA …";
    [ObservableProperty] private bool ollamaOk;
    [ObservableProperty] private string pcText = "PC …";
    [ObservableProperty] private bool pcOk = true;
    [ObservableProperty] private string gamingText = "";
    [ObservableProperty] private string desktopStatus = "";
    [ObservableProperty] private bool voiceModelsMissing;

    public GamingPolicyService GamingPolicy => gaming;

    public ShellViewModel(ICoreStateService core, CommandBarViewModel commands, TelemetryViewModel telemetry,
        DetailPanelViewModel details, SettingsPanelViewModel settingsPanel, HistoryPanelViewModel history,
        MiniModeViewModel mini, CommandPaletteViewModel palette, VoiceViewModel voice, GamingViewModel gamingVm,
        IActionEngine engine, IEngineService aiEngine, IDesktopService desktop, GamingPolicyService gaming,
        IVoiceService voiceService)
    {
        Core = core;
        Commands = commands;
        Telemetry = telemetry;
        Details = details;
        SettingsPanel = settingsPanel;
        History = history;
        Mini = mini;
        Palette = palette;
        Voice = voice;
        Gaming = gamingVm;
        this.engine = engine;
        this.aiEngine = aiEngine;
        this.desktop = desktop;
        this.gaming = gaming;
        this.voiceService = voiceService;

        Telemetry.PropertyChanged += OnTelemetryChanged;
        Details.Ai.PropertyChanged += OnAiChanged;
        Details.PropertyChanged += OnDetailsChanged;
        Voice.PropertyChanged += OnVoiceChanged;
        voiceService.Changed += OnVoiceServiceChanged;
        gaming.PropertyChanged += OnGamingChanged;
        desktop.StatusChanged += OnDesktopStatus;
        Palette.Chosen += OnPaletteChosen;
        Commands.Executed += OnCommandExecuted;
        Commands.SlashNavigationRequested += OnSlashNavigation;

        SyncMic();
        OnVoiceServiceChanged();
        SyncAi();
        SyncWatch();
        SyncPc();
        SyncGaming();
        DesktopStatus = desktop.Status;
    }

    // ------------------------------------------------------------------ pills

    private void OnTelemetryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TelemetryViewModel.Snapshot)) SyncPc();
    }

    private void OnAiChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiViewModel.EngineLabel) or nameof(AiViewModel.EngineProgressPercent))
            SyncAi();
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailPanelViewModel.WatchEnabled)) SyncWatch();
    }

    private void OnVoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VoiceViewModel.State)) SyncMic();
    }

    private void OnVoiceServiceChanged() => VoiceModelsMissing = !voiceService.HasLocalModels;

    private void OnGamingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamingPolicyService.IsGamingActive) or nameof(GamingPolicyService.RunningGame))
            SyncGaming();
    }

    private void OnDesktopStatus() => DesktopStatus = desktop.Status;

    private void SyncMic()
    {
        (MicText, MicActive) = Voice.State switch
        {
            VoiceState.Active => ("MIC · SŁUCHA", true),
            VoiceState.Standby => ("MIC · CZUWA", true),
            _ => ("MIC · WYŁ", false)
        };
    }

    private void SyncAi()
    {
        EngineStatus status = aiEngine.Status;
        AiText = status.State switch
        {
            "ready" => "AI · GOTOWE",
            "installing" => $"AI · {status.Progress * 100:F0}%",
            "paused" => "AI · ŚPI (GRA)",
            "error" => "AI · CZEKA",
            _ => "AI · START"
        };
        OllamaText = status.State switch
        {
            "ready" => "OLLAMA API · OK",
            "installing" => "OLLAMA API · …",
            _ => "OLLAMA API · —"
        };
        AiOk = status.State == "ready";
        OllamaOk = status.State == "ready";
    }

    private void SyncWatch()
    {
        WatchText = Details.WatchEnabled ? "WATCH · ON" : "WATCH · OFF";
        WatchOk = Details.WatchEnabled;
    }

    private void SyncPc()
    {
        var snapshot = Telemetry.Snapshot;
        if (!double.IsFinite(snapshot.Cpu) && !double.IsFinite(snapshot.RamUsed))
        {
            PcText = "PC · …";
            PcOk = true;
            return;
        }

        bool diskFull = snapshot.Disks.Any(d => d.Percent >= 95);
        PcText = diskFull ? "PC · DYSK" : "PC · OK";
        PcOk = !diskFull;
    }

    private void SyncGaming() => GamingText = gaming.IsGamingActive ? "GRA · " + gaming.RunningGame : "";

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void OpenSettingsSection(string key)
    {
        SettingsPanel.OpenSection(key);
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void OpenPalette() => Palette.Open();

    [RelayCommand]
    private void ToggleMiniMode() => desktop.ToggleMiniMode();

    [RelayCommand]
    private void ToggleHistory() => History.IsOpen = !History.IsOpen;

    [RelayCommand]
    private void OpenDetails(string kind)
    {
        if (Enum.TryParse<DetailKind>(kind, out var parsed)) Details.Open(parsed);
    }

    [RelayCommand]
    private void MicClick()
    {
        if (Voice.State == VoiceState.Off) Voice.StartCommand.Execute(null);
        else Voice.StopCommand.Execute(null);
    }

    [RelayCommand]
    private void AiClick() => Details.Open(DetailKind.Ai);

    [RelayCommand]
    private void WatchClick() => Details.Open(DetailKind.Watch);

    [RelayCommand]
    private void OllamaClick() => Details.Open(DetailKind.Ai);

    [RelayCommand]
    private void PcClick() => Details.Open(DetailKind.Cpu);

    [RelayCommand]
    private void QuickCommand(string text) => Commands.StageCommand(text);

    [RelayCommand]
    private void DownloadVoiceModels() => Voice.DownloadModelsCommand.Execute(null);

    [RelayCommand]
    private void EmergencyStop() => engine.EmergencyStop();

    [RelayCommand]
    private void Resume() => engine.Resume();

    [RelayCommand]
    private void Exit() => desktop.Exit();

    // ------------------------------------------------------------------ palette + //

    private void OnPaletteChosen(PaletteEntry entry)
    {
        if (entry.CommandText != null)
        {
            Commands.StageCommand(entry.CommandText);
            return;
        }

        switch (entry.PageKey)
        {
            case "command": Commands.StageCommand(""); break;
            case "system": Details.Open(DetailKind.Cpu); break;
            case "ai": Details.Open(DetailKind.Ai); break;
            case "gaming": desktop.ToggleOverlay(); break;
            case "voice": SettingsPanel.OpenSection("voice"); IsSettingsOpen = true; break;
            case "history":
            case "actions": History.IsOpen = true; History.Refresh(); break;
            case "memory": Commands.StageCommand("co pamiętasz"); break;
            case "projects": Commands.StageCommand("projekty"); break;
            case "tasks": Commands.StageCommand("zadania"); break;
            case "diagnostics": Commands.StageCommand("diagnostyka komputera"); break;
            case "tools": Commands.StageCommand("pomoc"); break;
            default: Commands.StageCommand(entry.Title); break;
        }
    }

    private void OnSlashNavigation(SlashEntry entry)
    {
        switch (entry.Target)
        {
            case "rozmowa": Details.CloseCommand.Execute(null); IsSettingsOpen = false; break;
            case "historia":
            case "akcje": History.IsOpen = true; History.Refresh(); break;
            case "glos": SettingsPanel.OpenSection("voice"); IsSettingsOpen = true; break;
            case "system": Details.Open(DetailKind.Cpu); break;
            case "gry": desktop.ToggleOverlay(); break;
            case "ai": Details.Open(DetailKind.Ai); break;
            case "narzedzia": Commands.StageCommand("pomoc"); break;
            case "settings": SettingsPanel.OpenSection("general"); IsSettingsOpen = true; break;
            default: Commands.StageCommand(entry.Target); break;
        }
    }

    private void OnCommandExecuted() => History.Refresh();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Telemetry.PropertyChanged -= OnTelemetryChanged;
        Details.Ai.PropertyChanged -= OnAiChanged;
        Details.PropertyChanged -= OnDetailsChanged;
        Voice.PropertyChanged -= OnVoiceChanged;
        voiceService.Changed -= OnVoiceServiceChanged;
        gaming.PropertyChanged -= OnGamingChanged;
        desktop.StatusChanged -= OnDesktopStatus;
        Palette.Chosen -= OnPaletteChosen;
        Commands.Executed -= OnCommandExecuted;
        Commands.SlashNavigationRequested -= OnSlashNavigation;
    }
}
