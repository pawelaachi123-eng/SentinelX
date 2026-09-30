using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.History;
using SentinelX.Services.Voice;
using SentinelX.Services.Settings;
namespace SentinelX.ViewModels;

/// <summary>Legacy feature-page metadata retained for command routing and compatibility; the active shell opens these as transient panels.</summary>
public sealed record CenterTab(string Key, string Icon, string Label, object ViewModel);

/// <summary>One row of the „//” palette above the chat input.</summary>
public sealed record SlashItem(SlashEntry Entry) { public string Display => "//" + Entry.Trigger; }

public partial class CommandCenterViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IVoiceService voice;
    private readonly IUiDispatcher dispatcher;
    private readonly IHistoryService history;
    private readonly ConversationMemoryService memory;
    private readonly TaskService tasks;
    public SystemViewModel System { get; }
    public VoiceViewModel Voice { get; }
    /// <summary>Raised when a selected command needs a transient context panel; MainViewModel routes it.</summary>
    public event Action<string>? NavigationRequested;
    public ObservableCollection<ConversationMessage> Messages { get; } = [];
    [ObservableProperty] private int inputFocusVersion;
    [ObservableProperty] private ActionRecord? currentAction;
    [ObservableProperty] private string userInput = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private bool hasPermission;
    [ObservableProperty] private string permissionSummary = "";
    [ObservableProperty] private string status = "Lokalny asystent · gotowy na polecenie";
    [ObservableProperty] private string conversationTitle = "";
    [ObservableProperty] private bool isPrivateMode;
    [ObservableProperty] private bool externalNetworkBlocked = true;
    [ObservableProperty] private bool externalNetworkAllowed;
    public bool IsPrivateOnline => IsPrivateMode && ExternalNetworkAllowed;
    partial void OnIsPrivateModeChanged(bool value) => OnPropertyChanged(nameof(IsPrivateOnline));
    partial void OnExternalNetworkAllowedChanged(bool value) => OnPropertyChanged(nameof(IsPrivateOnline));
    [ObservableProperty] private bool isStreaming;
    [ObservableProperty] private string streamingText = "";
    [ObservableProperty] private bool slashOpen;
    [ObservableProperty] private int slashIndex;
    [ObservableProperty] private IReadOnlyList<SlashItem> slashItems = [];
    [ObservableProperty] private bool voiceActive;
    [ObservableProperty] private string coreState = "IDLE";
    [ObservableProperty] private string coreActivity = "";
    [ObservableProperty] private string coreDetail = "";
    [ObservableProperty] private double coreAudioLevel;
    [ObservableProperty] private bool hasCoreCard;
    private readonly StringBuilder pendingChunks = new();
    private readonly object streamGate = new();
    private bool streamPending;
    private string lastUserInput = "";
    private string? lastErrorActionId;
    private DateTime errorPulseUntil = DateTime.MinValue;
    private readonly AutopilotService? autopilot;
    private readonly ISettingsService? settings;
    private string watcherNotice = "";
    private DateTimeOffset watcherNoticeUntil;
    private long watcherNoticeSequence;
    private string autopilotProgress = "";

    /// <summary>Cached feature surfaces; the active Sentinel Core shell does not render this collection as tabs.</summary>
    public IReadOnlyList<CenterTab> Sections { get; }
    [ObservableProperty] private CenterTab? selectedTab;
    public bool IsChatTab => SelectedTab?.Key == "rozmowa";
    /// <summary>Null while chat is visible — binding this to the sub-page ContentControl keeps the
    /// template engine from instantiating Centrum inside itself (the chat tab maps to this VM).</summary>
    public object? SubTabContent => IsChatTab ? null : SelectedTab?.ViewModel;
    partial void OnSelectedTabChanged(CenterTab? value) { OnPropertyChanged(nameof(IsChatTab)); OnPropertyChanged(nameof(SubTabContent)); }
    [RelayCommand]
    private void SelectTab(string key)
    {
        var tab = Sections.FirstOrDefault(x => x.Key == key);
        if (tab != null) SelectedTab = tab;
    }
    [RelayCommand]
    private void ShowChat() => SelectedTab = Sections.First(x => x.Key == "rozmowa");

    public CommandCenterViewModel(IActionEngine engine, IVoiceService voice, IUiDispatcher dispatcher,
        SystemViewModel system, VoiceViewModel voiceViewModel, IHistoryService history, ConversationMemoryService memory, TaskService tasks,
        TaskViewModel taskPage, HistoryViewModel historyPage, GamingViewModel gamingPage, AiViewModel aiPage,
        ActionsViewModel actionsPage, DiagnosticViewModel diagnosticsPage,
        MemoryArchiveService? archives = null, AutopilotService? autopilot = null, ISettingsService? settings = null)
    {
        this.engine = engine; this.voice = voice; this.dispatcher = dispatcher; this.history = history; this.memory = memory; this.tasks = tasks; this.autopilot = autopilot; this.settings = settings; System = system; Voice = voiceViewModel;
        Sections =
        [
            new CenterTab("rozmowa", "💬", "Rozmowa", this),
            new CenterTab("zadania", "📓", "Zadania", taskPage),
            new CenterTab("historia", "🕘", "Historia", historyPage),
            new CenterTab("glos", "🎤", "Głos", voiceViewModel),
            new CenterTab("system", "🖥", "System", system),
            new CenterTab("gry", "🎮", "Gaming", gamingPage),
            new CenterTab("ai", "✨", "AI", aiPage),
            new CenterTab("akcje", "⚡", "Akcje", actionsPage),
            new CenterTab("diagnostyka", "🩺", "Diagnostyka", diagnosticsPage)
        ];
        SelectedTab = Sections[0];
        VoiceActive = Voice.State != VoiceState.Off;
        Voice.PropertyChanged += VoiceChanged;
        RefreshCorePresentation();
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode;
        RefreshExternalNetworkStatus();
        if (!memory.PrivateMode && memory.GetDraft().Length > 0) UserInput = memory.GetDraft();
        engine.Changed += Sync; voice.CommandRecognized += Recognized;
        memory.Changed += MemorySync; memory.SessionChanged += SessionSync;
        if (settings != null) settings.Changed += SettingsChanged;
        tasks.ReminderFired += ReminderFired;
        if (autopilot != null)
        {
            autopilot.NoticeRaised += WatcherNoticeReceived;
            autopilot.ProgressRaised += AutopilotProgressReceived;
        }
        foreach (var archived in archives?.ArchiveDue() ?? [])
            Messages.Add(new("assistant", $"📦 Rozmowy z {archived.Month} przeniesione do archiwum ({archived.Conversations} rozmów, {archived.Turns} wypowiedzi) i usunięte z aktywnego magazynu. Folder: {archived.JsonPath}. Wspomnienia są nietknięte. Polecenie „archiwa” pokazuje listę.", DateTime.Now));
        var missed = tasks.CheckDue(atStartup: true);
        if (missed.Count > 0)
            Messages.Add(new("assistant", missed.Count == 1
                ? $"⏰ Przegapione przypomnienie (aplikacja była zamknięta): {missed[0].Text} (termin {missed[0].RemindAt:dd.MM.yyyy HH:mm})."
                : $"⏰ Przegapione przypomnienia (aplikacja była zamknięta), najnowsze: {missed[^1].Text}. Pełna lista: panel Zadania. Terminy: {string.Join(", ", missed.Take(3).Select(x => x.RemindAt.ToString("dd.MM HH:mm")))}.", DateTime.Now));
    }
    private void ReminderFired(string line) => dispatcher.Post(() => Messages.Add(new("assistant", line + " (przypomnienie działa tylko, gdy aplikacja jest uruchomiona)", DateTime.Now)));

    private void AutopilotProgressReceived(string step) => dispatcher.Post(() =>
    {
        autopilotProgress = step;
        RefreshCorePresentation();
    });

    private void WatcherNoticeReceived(string notice) => dispatcher.Post(() =>
    {
        watcherNotice = notice;
        watcherNoticeUntil = DateTimeOffset.Now.AddSeconds(20);
        long sequence = ++watcherNoticeSequence;
        RefreshCorePresentation();
        _ = ClearWatcherNoticeAsync(sequence);
    });

    private async Task ClearWatcherNoticeAsync(long sequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        dispatcher.Post(() =>
        {
            if (sequence != watcherNoticeSequence) return;
            watcherNotice = "";
            watcherNoticeUntil = DateTimeOffset.MinValue;
            RefreshCorePresentation();
        });
    }

    private void VoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => dispatcher.Post(() =>
    {
        VoiceActive = Voice.State != VoiceState.Off;
        RefreshCorePresentation();
    });

    partial void OnIsBusyChanged(bool value) => RefreshCorePresentation();
    partial void OnIsStoppedChanged(bool value) => RefreshCorePresentation();
    partial void OnHasPermissionChanged(bool value) => RefreshCorePresentation();
    partial void OnIsStreamingChanged(bool value) => RefreshCorePresentation();
    partial void OnCurrentActionChanged(ActionRecord? value) => RefreshCorePresentation();
    partial void OnPermissionSummaryChanged(string value) => RefreshCorePresentation();

    private void RefreshCorePresentation()
    {
        // The loopback RMS is sampled from the actual output device while SAPI is speaking;
        // otherwise the enhanced microphone RMS drives the listening visualization.
        double liveAudio = Voice.IsSpeaking ? Voice.Metrics.Playback : Voice.Metrics.Enhanced;
        CoreAudioLevel = Math.Clamp(liveAudio / 100d, 0d, 1d);
        bool recentFailure = CurrentAction?.Status == ActionStatus.Failed && DateTime.UtcNow < errorPulseUntil;
        bool voiceStarting = Voice.State == VoiceState.Off &&
            (Voice.Status.StartsWith("Ładowanie", StringComparison.OrdinalIgnoreCase)
             || Voice.Status.StartsWith("Pobieranie", StringComparison.OrdinalIgnoreCase));
        bool voiceUnavailable = Voice.State == VoiceState.Off && !voiceStarting
            && !string.Equals(Voice.Status, "Mikrofon wyłączony", StringComparison.Ordinal);
        if (IsStopped)
        {
            CoreState = "ERROR";
            CoreActivity = "Sentinel zatrzymany";
            CoreDetail = "Użyj Wznów, aby ponownie zezwolić na akcje.";
        }
        else if (Voice.IsSpeaking)
        {
            CoreState = "SPEAKING";
            CoreActivity = "Sentinel odpowiada głosem";
            CoreDetail = "";
        }
        else if (recentFailure)
        {
            CoreState = "ERROR";
            CoreActivity = "Ostatnie zadanie nie powiodło się";
            CoreDetail = CurrentAction?.Error ?? CurrentAction?.Evidence ?? "Szczegóły są dostępne w historii.";
        }
        else if (voiceUnavailable)
        {
            CoreState = "ERROR";
            CoreActivity = "Mikrofon niedostępny";
            CoreDetail = Voice.Status;
        }
        else if (HasPermission)
        {
            CoreState = "WORKING";
            CoreActivity = "Wymaga Twojej zgody";
            CoreDetail = PermissionSummary;
        }
        else if (IsBusy)
        {
            bool readingScreen = IsScreenAwarenessRequest(CurrentAction?.UserRequest ?? "");
            if (autopilotProgress.Length > 0)
            {
                CoreState = "WORKING";
                CoreActivity = "Autopilot · " + autopilotProgress;
                CoreDetail = "Zbieram lokalne dowody; bez zmian w systemie.";
            }
            else
            {
                CoreState = IsStreaming ? "THINKING" : "WORKING";
                CoreActivity = readingScreen
                    ? (IsStreaming ? "Wyjaśniam treść aktywnego okna" : "Odczytuję tekst aktywnego okna")
                    : (IsStreaming ? "Przygotowuję odpowiedź" : "Wykonuję zadanie");
                CoreDetail = readingScreen
                    ? "Odczyt ograniczony do tekstu udostępnionego przez UI Automation."
                    : CurrentAction?.UserRequest ?? CurrentAction?.Phase ?? "";
            }
        }
        else if (watcherNotice.Length > 0 && DateTimeOffset.Now < watcherNoticeUntil)
        {
            CoreState = "WORKING";
            CoreActivity = "Watcher — powiadomienie";
            CoreDetail = watcherNotice;
        }
        else if (voiceStarting)
        {
            CoreState = "WORKING";
            CoreActivity = Voice.Status;
            CoreDetail = "";
        }
        else if (Voice.State != VoiceState.Off)
        {
            CoreState = "LISTENING";
            CoreActivity = "Nasłuchuje";
            CoreDetail = Voice.Status;
        }
        else
        {
            CoreState = "IDLE";
            CoreActivity = "";
            CoreDetail = "";
        }
        HasCoreCard = IsBusy || HasPermission || IsStopped || recentFailure || voiceStarting || voiceUnavailable ||
            (watcherNotice.Length > 0 && DateTimeOffset.Now < watcherNoticeUntil);
    }

    private void Sync() => dispatcher.Post(() =>
    {
        CurrentAction = engine.CurrentAction;
        if (CurrentAction is { Status: ActionStatus.Failed } failed && failed.ActionId != lastErrorActionId)
        {
            lastErrorActionId = failed.ActionId;
            errorPulseUntil = DateTime.UtcNow.AddSeconds(4);
            _ = ClearErrorPulseAsync(failed.ActionId);
        }
        IsBusy = engine.IsBusy; IsStopped = engine.IsStopped;
        HasPermission = engine.HasPendingPermission; PermissionSummary = engine.PermissionSummary;
        RefreshCorePresentation();
    });

    private async Task ClearErrorPulseAsync(string actionId)
    {
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        dispatcher.Post(() =>
        {
            if (lastErrorActionId == actionId) RefreshCorePresentation();
        });
    }
    private void MemorySync() => dispatcher.Post(() => { ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode; RefreshExternalNetworkStatus(); });
    private void SettingsChanged() => dispatcher.Post(RefreshExternalNetworkStatus);
    private void RefreshExternalNetworkStatus()
    {
        ExternalNetworkAllowed = memory.ExternalNetworkAllowed;
        ExternalNetworkBlocked = !ExternalNetworkAllowed;
    }
    private void SessionSync() => dispatcher.Post(() =>
    {
        Messages.Clear();
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle;
        RefreshExternalNetworkStatus();
        Status = "Przełączono rozmowę — kontekst poniżej dotyczy wyłącznie wybranej rozmowy.";
    });
    partial void OnUserInputChanged(string value)
    {
        memory.SaveDraft(value);
        RefreshSlash(value);
    }

    private void RefreshSlash(string value)
    {
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            SlashItems = SlashCatalog.Filter(value[2..]).Select(x => new SlashItem(x)).ToList();
            SlashOpen = true;
            if (SlashItems.Count == 0) SlashIndex = -1;
            else if (SlashIndex < 0 || SlashIndex >= SlashItems.Count) SlashIndex = 0;
        }
        else if (SlashOpen) { SlashOpen = false; SlashIndex = -1; }
    }

    [RelayCommand]
    private void SlashNext()
    {
        if (!SlashOpen || SlashItems.Count == 0) return;
        SlashIndex = (SlashIndex + 1) % SlashItems.Count;
    }

    [RelayCommand]
    private void SlashPrev()
    {
        if (!SlashOpen || SlashItems.Count == 0) return;
        SlashIndex = (SlashIndex - 1 + SlashItems.Count) % SlashItems.Count;
    }

    [RelayCommand]
    private void SlashClose()
    {
        SlashOpen = false; SlashIndex = -1;
        if (UserInput.StartsWith("//", StringComparison.Ordinal)) UserInput = "";
    }

    /// <summary>Executes the highlighted // entry. Commands use the normal engine path; panels open only after explicit selection.</summary>
    [RelayCommand]
    private async Task SlashChooseAsync()
    {
        if (!SlashOpen || SlashIndex < 0 || SlashIndex >= SlashItems.Count) return;
        var entry = SlashItems[SlashIndex].Entry;
        UserInput = ""; SlashOpen = false; SlashIndex = -1;
        switch (entry.Kind)
        {
            case SlashKind.Command: await SubmitAsync(entry.Target, displayText: "//" + entry.Trigger); break;
            case SlashKind.Tab: SelectTab(entry.Target); Status = "Centrum → " + entry.Label + "."; break;
            case SlashKind.Page: NavigationRequested?.Invoke(entry.Target); break;
        }
    }

    /// <summary>One click starts or stops the always-listening microphone — the 0.91 quick-off for the default-on voice.</summary>
    [RelayCommand]
    private void ToggleVoice()
    {
        if (Voice.State == VoiceState.Off) Voice.StartCommand.Execute(null);
        else Voice.StopCommand.Execute(null);
    }
    public void StageCommand(string text)
    {
        UserInput = text; InputFocusVersion++;
        Status = "Polecenie przygotowane — sprawdź treść i dopiero wtedy wyślij.";
    }
    private void Recognized(string input) => dispatcher.Post(() => _ = SubmitAsync(input, true));
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var input = UserInput.Trim(); if (input.Length == 0) return;
        if (engine.IsBusy || engine.IsStopped) { Status = engine.IsStopped ? "STOP jest aktywny. Wznów Sentinel przed wysłaniem." : "Trwa zadanie. Szkic pozostaje w polu wpisywania."; return; }
        string normalized = CommandText.Normalize(input);
        UserInput = ""; SlashOpen = false; SlashIndex = -1;
        // A typed „//trigger” sent with Enter resolves through the slash catalogue without touching the engine.
        if (input.StartsWith("//", StringComparison.Ordinal))
        {
            var entry = SlashCatalog.TryResolve(input[2..].Trim());
            if (entry is { Kind: SlashKind.Command }) { await SubmitAsync(entry.Target, displayText: input); return; }
            if (entry is { Kind: SlashKind.Tab }) { SelectTab(entry.Target); Status = "Centrum → " + entry.Label + "."; return; }
            if (entry is { Kind: SlashKind.Page }) { NavigationRequested?.Invoke(entry.Target); return; }
            await SubmitAsync(input); // unknown shortcut: the engine answers honestly with the closest matches
            return;
        }
        if (normalized is "ponow" or "ponow odpowiedz" or "ponow to" or "sproboj jeszcze raz") { await RetryAsync(); return; }
        await SubmitAsync(input);
    }
    /// <summary>Repeats the last command verbatim and marks the answer as a retry — nothing is invented.</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (lastUserInput.Length == 0) { Status = "Nie mam czego ponowić — w tej sesji nie wysłano jeszcze polecenia."; return; }
        if (engine.IsBusy || engine.IsStopped) { Status = engine.IsStopped ? "STOP jest aktywny. Wznów Sentinel przed ponowieniem." : "Trwa zadanie — najpierw je zatrzymaj."; return; }
        Status = "Ponawiam ostatnie polecenie. Odpowiedź modelu może się różnić od poprzedniej.";
        await SubmitAsync(lastUserInput, isRetry: true);
    }
    /// <summary>Always reachable: stops the running generation and keeps whatever the model already produced.</summary>
    [RelayCommand]
    private void StopGeneration()
    {
        if (!engine.IsBusy) { Status = "Nic teraz nie jest generowane."; return; }
        engine.Cancel();
        Status = "Wysłałem sygnał zatrzymania. Częściowa odpowiedź zostanie pokazana i oznaczona jako urwana.";
    }
    private void StreamChunk(string chunk)
    {
        lock (streamGate) { pendingChunks.Append(chunk); if (streamPending) return; streamPending = true; }
        dispatcher.Post(FlushStream);
    }
    private void FlushStream()
    {
        string chunk;
        lock (streamGate) { chunk = pendingChunks.ToString(); pendingChunks.Clear(); streamPending = false; }
        if (chunk.Length == 0) return;
        StreamingText += chunk;
        IsStreaming = true;
    }
    [RelayCommand] private Task QuickCommandAsync(string input) => SubmitAsync(input);
    [RelayCommand] private Task ApproveAsync() => SubmitAsync("potwierdz");
    [RelayCommand] private void Cancel() => engine.Cancel();
    [RelayCommand]
    private void TogglePrivateMode()
    {
        memory.SetPrivateMode(!memory.PrivateMode);
        RefreshExternalNetworkStatus();
        Status = memory.PrivateMode
            ? (memory.ExternalNetworkAllowed
                ? "Tryb prywatny WŁĄCZONY: tekst rozmowy nie jest zapisywany w lokalnej historii/audycie. Funkcje online mogą przekazać temat usługom, które mogą go logować; jawnie zlecone pliki są osobnym lokalnym zapisem."
                : "Tryb prywatny WŁĄCZONY: tekst rozmowy nie jest zapisywany w lokalnej historii/audycie. Jawnie zlecone pliki są osobnym lokalnym zapisem; Tryb tylko lokalnie blokuje internet Sentinela.")
            : "Tryb prywatny WYŁĄCZONY. Zapis rozmów zgodny z ustawieniami prywatności.";
    }
    private static bool IsScreenAwarenessRequest(string input)
    {
        string normalized = CommandText.Normalize(input).TrimEnd('.', '!', '?', ',');
        return normalized is "co jest na ekranie" or "co widzisz na ekranie" or "odczytaj ekran"
            or "przeczytaj ekran" or "pokaz tekst z ekranu" or "co jest w tym oknie" or "opisz to okno"
            or "pomoz z tym oknem" or "co to za blad" or "wyjasnij ten blad" or "co oznacza ten komunikat"
            or "wyjasnij ten komunikat" or "jaki komunikat widzisz" or "co jest napisane na ekranie"
            or "przeczytaj komunikat na ekranie" or "what is on screen" or "what do you see on screen"
            or "read the screen" or "what is in this window" or "what is this error" or "explain this error"
            || normalized.EndsWith("na ekranie", StringComparison.Ordinal)
            || normalized.EndsWith("w oknie", StringComparison.Ordinal);
    }

    private static bool IsHistoryPanelRequest(string input)
    {
        string normalized = CommandText.Normalize(input.Trim().TrimStart('/').Trim());
        return normalized is "pokaz historie" or "historia" or "historia rozmow" or "rozmowy historia"
            or "pokaz rozmowy" or "pokaz ostatnia rozmowe" or "ostatnia rozmowa";
    }

    private async Task SubmitAsync(string input, bool fromVoice = false, bool isRetry = false, string? displayText = null)
    {
        if (IsHistoryPanelRequest(input))
        {
            Messages.Add(new("user", displayText ?? input, DateTime.Now));
            Status = "Otwieram lokalną historię.";
            NavigationRequested?.Invoke("history");
            if (fromVoice) voice.Speak("Otwieram lokalną historię.");
            return;
        }

        lastUserInput = input;
        Messages.Add(new("user", (displayText ?? input) + (isRetry ? "  (ponowione)" : ""), DateTime.Now));
        Status = "Przetwarzanie polecenia…";
        IntentResult result;
        try { result = await engine.ExecuteAsync(input, fromVoice: fromVoice, onDelta: StreamChunk); }
        finally { lock (streamGate) { pendingChunks.Clear(); streamPending = false; } IsStreaming = false; StreamingText = ""; }
        Messages.Add(new("sentinel", isRetry ? result.Text + "\n\n[Ponowiona odpowiedź na to samo polecenie — model mógł odpowiedzieć inaczej.]" : result.Text, DateTime.Now, result.Action));
        while (Messages.Count > 300) Messages.RemoveAt(0);
        Status = result.Action?.StorageWarning is { Length: > 0 } warning ? warning : history.StorageError ?? (engine.IsStopped ? "STOP awaryjny · nowe akcje zablokowane" : "Gotowe · wyniki akcji znajdziesz w Historii");
        if (fromVoice) voice.Speak(result.Text);
    }
    public void Dispose()
    {
        engine.Changed -= Sync; voice.CommandRecognized -= Recognized; Voice.PropertyChanged -= VoiceChanged;
        memory.Changed -= MemorySync; memory.SessionChanged -= SessionSync;
        if (settings != null) settings.Changed -= SettingsChanged;
        tasks.ReminderFired -= ReminderFired;
        if (autopilot != null)
        {
            autopilot.NoticeRaised -= WatcherNoticeReceived;
            autopilot.ProgressRaised -= AutopilotProgressReceived;
        }
        lock (streamGate) { pendingChunks.Clear(); streamPending = false; }
        memory.FlushDraft();
    }
}
