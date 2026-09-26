using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SentinelX;
public sealed record ChatLine(string Speaker, string Text, string Time);

public partial class MainWindow : Window
{
    private readonly AppSettingsService settings = new();
    private readonly SystemMonitor monitor = new();
    private readonly SystemInfoService systemInfo = new();
    private readonly GamingModeService gaming = new();
    private readonly ConversationMemoryService memory = new();
    private readonly SessionFactBook factBook = new();
    private readonly LocalAiService ai;
    private readonly CommandRouter router;
    private readonly SentinelToolboxService toolbox;
    private readonly VoiceRecognitionService voice;
    private readonly ResourceGuardService resourceGuard = new();
    private readonly FileWorkspaceService files;
    private readonly ProgramBuilderService programBuilder = new();
    private SettingsEditorView? settingsEditor;
    private bool applyingSettings;
    private readonly SpeechOutputService speech = new();
    private readonly StartupService startup = new("Sentinel X");
    private readonly ObservableCollection<ChatLine> messages = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentTask;
    private TrayService? tray;
    private GlobalHotkeyService? hotkeys;
    private MetricOverlay? overlay;
    private bool ready, busy, installing, exiting, emergency, speaking;
    private string currentPage = "Chat", overlayMetric = "ram", lastVoiceStatus = "Mikrofon wyłączony";
    private DateTime activeUntil, lastTick, highUsageSince, lastWatchAlert;
    private readonly bool smokeMode;

    public MainWindow() : this(false) { }
    public MainWindow(bool smokeMode)
    {
        this.smokeMode = smokeMode;
        // The compatibility UI honors the same memory privacy toggles as the MVVM shell.
        memory.PrivacyProvider = () => new(settings.Settings.Memory.SaveConversations, settings.Settings.Memory.UseHistoryForAi,
            settings.Settings.Memory.SaveMemories, settings.Settings.Memory.UseMemoriesForAi,
            settings.Settings.Memory.RetentionDays, settings.Settings.Memory.ContextPreviewEnabled);
        files = new FileWorkspaceService(desktop: smokeMode ? System.IO.Path.Combine(AppPaths.Root, "TestDesktop") : null);
        toolbox = new SentinelToolboxService(() => settings.Settings.Ui.DefaultBrowserPreference, memory: memory);
        InitializeComponent();
        ai = new LocalAiService(gaming, systemMonitor: monitor, aiSettingsProvider: () => settings.Settings.Ai);
        voice = new VoiceRecognitionService(() => settings.Settings.Voice);
        router = new CommandRouter(monitor, systemInfo, ai, memory, facts: factBook);
        ChatItems.ItemsSource = messages;
        LoadChat(); ApplySettings();
        settingsEditor = new SettingsEditorView(settings); SettingsEditorHost.Content = settingsEditor;
        settings.Changed += ApplyLiveSettings;
        if (!smokeMode) { Width = Math.Min(settings.Settings.Ui.WindowWidth, SystemParameters.WorkArea.Width); Height = Math.Min(settings.Settings.Ui.WindowHeight, SystemParameters.WorkArea.Height); }
        voice.StatusChanged += s => OnUi(() => { lastVoiceStatus = s; UpdateVoiceUi(); });
        voice.ErrorOccurred += s => OnUi(() => { lastVoiceStatus = s; ModelStatusText.Text = s; StatusText.Text = s; UpdateVoiceUi(); });
        voice.SpeechRecognized += s => OnUi(() => HandleSpeech(s));
        speech.Completed += () => OnUi(SpeechCompleted);
        speech.Failed += s => OnUi(() => { SpeechStatusText.Text = s; SpeechCompleted(); });
        timer.Tick += (_, _) => Tick();
        Loaded += LoadedAsync; Closing += ClosingWindow; PreviewKeyDown += WindowKeyDown;
        StateChanged += (_, _) => { if (ready && WindowState == WindowState.Minimized && settings.Settings.MinimizeToTray) HideToTray(); };
        ShowPage(smokeMode ? "Chat" : settings.Settings.Ui.SelectedPage);
    }
    private void OnUi(Action action)
    {
        if (exiting || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (!exiting) action(); }));
    }
    private async void LoadedAsync(object sender, RoutedEventArgs e)
    {
        if (ready) return;
        ready = true; timer.Start(); RefreshDevices();
        SpeechVoiceCombo.ItemsSource = speech.Voices;
        SpeechVoiceCombo.SelectedItem = speech.Voices.Contains(settings.Settings.SpeechVoice) ? settings.Settings.SpeechVoice : speech.PreferredVoice;
        SpeechStatusText.Text = speech.Status;
        try { HardwareText.Text = await Task.Run(() => $"{systemInfo.GetCpuName()}\n{systemInfo.GetWindowsVersion()}\n\n{systemInfo.GetDiskInfo()}"); }
        catch (Exception ex) { HardwareText.Text = "Odczyt niedostępny: " + ex.Message; }
        if (exiting) return;
        if (!smokeMode)
        {
            try
            {
                tray = new TrayService();
                tray.OpenRequested += () => OnUi(ShowFromTray);
                tray.VoiceOnRequested += () => OnUi(async () => await StartVoiceAsync());
                tray.VoiceOffRequested += () => OnUi(StopVoice);
                tray.EmergencyStopRequested += () => OnUi(TriggerEmergency);
                tray.ExitRequested += () => OnUi(ExitApplication);
                hotkeys = new GlobalHotkeyService(this, () => { ShowFromTray(); if (!voice.IsListening) _ = StartVoiceAsync(); }, TriggerEmergency);
                if (!hotkeys.Registered || !hotkeys.EmergencyRegistered) SettingsStatusText.Text = "Jeden z globalnych skrótów jest zajęty. Użyj przycisków w oknie lub zasobnika.";
            }
            catch (Exception ex) { settings.Settings.CloseToTray = false; SettingsStatusText.Text = "Zasobnik niedostępny: " + ex.Message; }
            await RefreshAiAsync();
            if (exiting) return;
            ModelStatusText.Text = "Wybierz „Sprawdź modele lokalne” lub pobierz model głosu.";
            if (settings.Settings.StartVoiceOnLaunch) await StartVoiceAsync();
            if (settings.Settings.StartMinimized || Environment.GetCommandLineArgs().Contains("--tray")) HideToTray();
        }
        else AiStatusText.Text = "Tryb testowy • bez AI i mikrofonu";
        StatusText.Text = settings.LastError ?? memory.LastStorageError ?? "Gotowy. Wpisz polecenie lub włącz mikrofon.";
        CommandInput.Focus();
    }
    private void ApplySettings()
    {
        applyingSettings = true;
        try
        {
        var s = settings.Settings;
        TrayCheck.IsChecked = s.CloseToTray; MinimizeCheck.IsChecked = s.MinimizeToTray;
        StartMinimizedCheck.IsChecked = s.StartMinimized; StartVoiceCheck.IsChecked = s.StartVoiceOnLaunch;
        SpeakCheck.IsChecked = s.SpeakResponses; WatchCheck.IsChecked = s.WatchEnabled;
        StartupCheck.IsChecked = startup.IsEnabled();
        ThemeService.Apply(this, s.Ui);
        }
        finally { applyingSettings = false; }
    }
    private void ApplyLiveSettings()
    {
        if (exiting) return;
        ApplySettings(); voice.ApplySettings(); overlay?.ApplySettings(settings.Settings.Ui);
        if (voice.IsListening && !voice.IsWakeOnlyMode) activeUntil = DateTime.Now.AddMinutes(settings.Settings.ConversationMinutes);
    }
    private void LoadChat()
    {
        messages.Clear();
        foreach (var entry in memory.GetRecentEntries(100)) messages.Add(new(entry.Role == "user" ? "TY" : "SENTINEL", entry.Text, entry.Timestamp.ToString("HH:mm")));
        if (messages.Count == 0) AddMessage("SENTINEL", "Cześć. Mogę pokazać zużycie komputera, sprawdzić zabezpieczenia, uruchomić aplikację lub porozmawiać z pomocą lokalnego AI.\n\nZacznij od polecenia albo skonfiguruj mikrofon w zakładce „Głos i mikrofon”.", false);
    }
    private void AddMessage(string speaker, string text, bool persist = true)
    {
        messages.Add(new(speaker, text, DateTime.Now.ToString("HH:mm")));
        while (messages.Count > 120) messages.RemoveAt(0);
        if (persist && speaker == "SENTINEL") memory.AddAssistantMessage(text);
        _ = Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()), DispatcherPriority.Background);
    }
    private void Tick()
    {
        if (exiting) return;
        VoiceMeter.Value = voice.CurrentAudioLevel * 100;
        EnhancedVoiceMeter.Value = AudioEnhancementService.RmsToMeter(voice.CurrentEnhancedRms) * 100;
        LastTranscriptText.Text = "Ostatnia wypowiedź: " + (voice.LastTranscript.Length > 0 ? voice.LastTranscript : "—") + "\n" + voice.LastDecision;
        VoiceDetailText.Text = voice.IsListening ? $"{voice.CurrentRawDbfs:0.0} dBFS • AGC {voice.CurrentGain:0.0}× • {(voice.IsCalibrating ? "kalibracja" : voice.SpeechDetected ? "mowa" : "cisza")} • kolejka {voice.QueueDepth}/5 • ASR {voice.LastRecognitionMilliseconds} ms" : "Mikrofon wyłączony";
        if (voice.IsClipping) VoiceDetailText.Text += " • przesterowanie";
        double interval = gaming.IsGaming() ? settings.Settings.Resources.GamingMonitorIntervalSeconds : settings.Settings.Resources.MonitorIntervalSeconds;
        if (DateTime.Now - lastTick < TimeSpan.FromSeconds(interval)) return;
        lastTick = DateTime.Now;
        float cpu = monitor.GetCpuUsage(); double ram = monitor.GetRamUsagePercent();
        float gpu = monitor.GetGpuUsagePercent();
        MiniCpuText.Text = "CPU " + Format(cpu, "%"); MiniRamText.Text = "RAM " + Format(ram, "%"); MiniGpuText.Text = "GPU " + Format(gpu, "%");
        var own = resourceGuard.Sample();
        ResourceUsageText.Text = $"Sentinel: {own.WorkingSetMb:0} MB • CPU {Format(own.CpuPercent, "%", 1)}";
        AiRuntimeText.Text = $"Ostatni model: {(ai.LastModel.Length > 0 ? ai.LastModel : "—")}\nCzas: {ai.LastResponseTime.TotalSeconds:0.0} s\nWybór: {ai.LastRoutingReason}\n{ai.LastFallbackReason}";
        if (currentPage == "Actions") TasksOutput.Text = toolbox.GetTaskSummary();
        CpuText.Text = Format(cpu, "%"); RamText.Text = Format(monitor.GetUsedRamGB(), " GB", 1);
        CpuBar.Value = float.IsFinite(cpu) ? Math.Clamp(cpu, 0, 100) : 0;
        RamBar.Value = double.IsFinite(ram) ? Math.Clamp(ram, 0, 100) : 0;
        RamBar.ToolTip = Format(ram, "%") + " z " + Format(monitor.GetTotalRamGB(), " GB", 1);
        UptimeText.Text = systemInfo.GetUptime();
        string game = gaming.GetRunningGame();
        GamingText.Text = string.IsNullOrEmpty(game)
            ? $"Nie wykryto gry. GPU: {Format(gpu, "%")}. Lokalne komendy działają bez modelu AI; proste pytania idą kodem."
            : $"Wykryta gra: {game}. GPU: {Format(gpu, "%")}. AI auto preferuje lżejszy model przy obciążeniu.";
        overlay?.Update(overlayMetric.ToUpperInvariant(), overlayMetric == "ram" ? Format(monitor.GetUsedRamGB(), " GB", 1) : Format(cpu, "%"));
        if (voice.IsListening && !voice.IsWakeOnlyMode && !busy && !speaking && DateTime.Now > activeUntil) SetStandby();
        tray?.UpdateState(voice.IsListening, emergency, game.Length > 0); UpdatePermission();
        if (settings.Settings.WatchEnabled &&
            ((float.IsFinite(cpu) && cpu >= settings.Settings.Watch.CpuAlertPercent) ||
             (double.IsFinite(ram) && ram >= settings.Settings.Watch.RamAlertPercent)))
        {
            if (highUsageSince == default) highUsageSince = DateTime.Now;
            if (DateTime.Now - highUsageSince > TimeSpan.FromSeconds(settings.Settings.Watch.MinSecondsBeforeAlert) &&
                DateTime.Now - lastWatchAlert > TimeSpan.FromMinutes(settings.Settings.Watch.CooldownMinutes))
            {
                lastWatchAlert = DateTime.Now;
                string alert = $"{DateTime.Now:HH:mm:ss} Wysokie obciążenie przez co najmniej {settings.Settings.Watch.MinSecondsBeforeAlert} s. CPU {Format(cpu, "%")}, RAM {Format(ram, "%")}.";
                if (WatchOutput.Text == "Brak alertów w tej sesji.") WatchOutput.Clear();
                WatchOutput.AppendText(alert + "\n"); if (WatchOutput.Text.Length > 12000) WatchOutput.Text = WatchOutput.Text[^10000..];
                StatusText.Text = alert; tray?.ShowInfo("Sentinel Watch", alert);
            }
        }
        else highUsageSince = default;
    }
    private static string Format(double value, string suffix, int decimals = 0) => double.IsFinite(value) && value >= 0 ? value.ToString("F" + decimals, CultureInfo.GetCultureInfo("pl-PL")) + suffix : "niedostępne";
    private void ShowPage(string page)
    {
        if (page is not ("Chat" or "System" or "Tools" or "Voice" or "Gaming" or "Memory" or "Settings" or "AI" or "Actions" or "Programs")) page = "Chat";
        currentPage = page;
        FrameworkElement[] pages = [ChatPage, SystemPage, ToolsPage, VoicePage, GamingPage, MemoryPage, SettingsPage, AiPage, ActionsPage, ProgramsPage];
        string[] keys = ["Chat", "System", "Tools", "Voice", "Gaming", "Memory", "Settings", "AI", "Actions", "Programs"];
        string[] names = ["Command Center", "Twój komputer", "Narzędzia diagnostyczne", "Głos i mikrofon", "Gry i Sentinel Watch", "Pamięć i historia", "Ustawienia", "Lokalne AI", "Akcje i zadania", "Programy i pliki"];
        for (int i = 0; i < pages.Length; i++) { pages[i].Visibility = keys[i] == page ? Visibility.Visible : Visibility.Collapsed; if (keys[i] == page) PageTitle.Text = names[i]; }
        foreach (Button button in Navigation.Children)
        {
            bool selected = (string)button.Tag == page;
            button.SetResourceReference(BackgroundProperty, selected ? "Panel2" : "SidebarBrush");
            button.SetResourceReference(BorderBrushProperty, selected ? "Accent" : "SidebarBrush");
            button.SetResourceReference(ForegroundProperty, "TextBrush");
        }
        if (page == "Settings") settingsEditor?.Rebuild();
    }
    private void ShowOverlay(string metric)
    {
        overlayMetric = metric;
        if (overlay == null) { overlay = new MetricOverlay(); overlay.Closed += (_, _) => overlay = null; }
        overlay.ApplySettings(settings.Settings.Ui);
        overlay.Update(metric.ToUpperInvariant(), metric == "ram" ? Format(monitor.GetUsedRamGB(), " GB", 1) : Format(monitor.GetCpuUsage(), "%")); overlay.Show();
    }
    private void HideToTray() { if (tray == null) { WindowState = WindowState.Minimized; return; } Hide(); }
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); CommandInput.Focus(); }
    internal void ActivateExistingWindow() => OnUi(ShowFromTray);
    private void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (!exiting && !smokeMode && settings.Settings.CloseToTray && tray != null) { e.Cancel = true; HideToTray(); }
        else Cleanup();
    }
    private void ExitApplication() { Cleanup(); Close(); }
    private void Cleanup()
    {
        if (exiting) return;
        if (!smokeMode) { settings.Settings.Ui.WindowWidth = RestoreBounds.Width; settings.Settings.Ui.WindowHeight = RestoreBounds.Height; settings.Settings.Ui.SelectedPage = currentPage; settings.Save(); }
        exiting = true; timer.Stop(); lifetime.Cancel(); currentTask?.Cancel(); ai.CancelCurrentRequest();
        toolbox.CancelAllTasks(); settings.Changed -= ApplyLiveSettings; resourceGuard.Dispose();
        speech.Dispose(); voice.Dispose(); overlay?.Close(); hotkeys?.Dispose(); tray?.Dispose(); monitor.Dispose(); ai.Dispose();
    }
    internal void SelectTestPage(string page) => ShowPage(page);
    internal void CloseTestWindow() => ExitApplication();
}
