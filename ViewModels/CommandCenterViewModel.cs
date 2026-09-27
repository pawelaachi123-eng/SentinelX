using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.History;
using SentinelX.Services.Voice;
namespace SentinelX.ViewModels;

/// <summary>One tab of the Centrum hub: an emoji icon (no text label, per the 0.91 design),
/// a tooltip and the page view-model shown when the icon is selected.</summary>
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
    /// <summary>Raised when a slash entry needs a top-level page (e.g. Ustawienia); MainViewModel routes it.</summary>
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
    [ObservableProperty] private bool isStreaming;
    [ObservableProperty] private string streamingText = "";
    [ObservableProperty] private bool slashOpen;
    [ObservableProperty] private int slashIndex;
    [ObservableProperty] private IReadOnlyList<SlashItem> slashItems = [];
    [ObservableProperty] private bool voiceActive;
    private readonly StringBuilder pendingChunks = new();
    private readonly object streamGate = new();
    private bool streamPending;
    private string lastUserInput = "";
    /// <summary>0.97 · jeden tik dla harmonogramu, watchdoga i bezpiecznego schowka (co 15 s sprawdzam,
    /// czy nadeszła pora). Działa tylko przy uruchomionej aplikacji — nic nie rejestruję w systemie.</summary>
    private readonly AutomationPoller? poller;
    private readonly DispatcherTimer? pollerTimer;

    /// <summary>Centrum tabs: chat plus every panel that used to be a top-level page. Icons only.</summary>
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
        MemoryArchiveService? archives = null, SchedulerService? scheduler = null, WatchdogService? watchdog = null)
    {
        this.engine = engine; this.voice = voice; this.dispatcher = dispatcher; this.history = history; this.memory = memory; this.tasks = tasks; System = system; Voice = voiceViewModel;
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
        Voice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Voice.State)) VoiceActive = Voice.State != VoiceState.Off; };
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode;
        if (!memory.PrivateMode && memory.GetDraft().Length > 0) UserInput = memory.GetDraft();
        engine.Changed += Sync; voice.CommandRecognized += Recognized;
        memory.Changed += MemorySync; memory.SessionChanged += SessionSync;
        tasks.ReminderFired += ReminderFired;
        foreach (var archived in archives?.ArchiveDue() ?? [])
            Messages.Add(new("assistant", $"📦 Rozmowy z {archived.Month} przeniesione do archiwum ({archived.Conversations} rozmów, {archived.Turns} wypowiedzi) i usunięte z aktywnego magazynu. Folder: {archived.JsonPath}. Wspomnienia są nietknięte. Polecenie „archiwa” pokazuje listę.", DateTime.Now));
        // 0.97: automation tick — schedules, folder watchdog and the secure clipboard.
        poller = new AutomationPoller(scheduler, watchdog, RunScheduledCommand, Report, () => engine.IsBusy);
        pollerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        pollerTimer.Tick += (_, _) => poller.Tick();
        pollerTimer.Start();
        var missed = tasks.CheckDue(atStartup: true);
        if (missed.Count > 0)
            Messages.Add(new("assistant", missed.Count == 1
                ? $"⏰ Przegapione przypomnienie (aplikacja była zamknięta): {missed[0].Text} (termin {missed[0].RemindAt:dd.MM.yyyy HH:mm})."
                : $"⏰ Przegapione przypomnienia (aplikacja była zamknięta), najnowsze: {missed[^1].Text}. Pełna lista: panel Zadania. Terminy: {string.Join(", ", missed.Take(3).Select(x => x.RemindAt.ToString("dd.MM HH:mm")))}.", DateTime.Now));
    }
    private void ReminderFired(string line) => dispatcher.Post(() => Messages.Add(new("assistant", line + " (przypomnienie działa tylko, gdy aplikacja jest uruchomiona)", DateTime.Now)));
    private void Sync() => dispatcher.Post(() =>
    {
        CurrentAction = engine.CurrentAction; IsBusy = engine.IsBusy; IsStopped = engine.IsStopped;
        HasPermission = engine.HasPendingPermission; PermissionSummary = engine.PermissionSummary;
    });
    private void MemorySync() => dispatcher.Post(() => { ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode; });
    private void SessionSync() => dispatcher.Post(() =>
    {
        Messages.Clear();
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle;
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

    /// <summary>Executes the highlighted // entry. Commands run through the normal engine path,
    /// tabs switch inside Centrum, pages raise a navigation event — nothing runs on its own.</summary>
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
        Status = memory.PrivateMode
            ? "Tryb prywatny WŁĄCZONY. Treść rozmowy nie jest zapisywana — po restarcie nie będzie czego przywrócić."
            : "Tryb prywatny WYŁĄCZONY. Zapis rozmów zgodny z ustawieniami prywatności.";
    }
    private async Task SubmitAsync(string input, bool fromVoice = false, bool isRetry = false, string? displayText = null)
    {
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
    /// <summary>Wpis z harmonogramu uruchamia się przez ten sam silnik, co wpisane polecenie:
    /// z historią akcji, oknem zgody i audytem. Bez skrótów i bez uprawnień ponad człowieka.</summary>
    private async Task RunScheduledCommand(string command, CancellationToken token)
    {
        try
        {
            var result = await engine.ExecuteAsync(command, token, fromVoice: false);
            string first = result.Text.Replace("\r", " ").Split('\n').FirstOrDefault(x => x.Trim().Length > 0) ?? "";
            if (first.Length > 160) first = first[..159] + "…";
            if (first.Length > 0) Report("↳ " + first);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Report("Zaplanowane polecenie nie powiodło się: " + ex.Message);
            AppLog.Write("Harmonogram", "Błąd zaplanowanego polecenia: " + command + " · " + ex.Message);
        }
    }

    /// <summary>Komunikat z tika automatyzacji (harmonogram, watchdog, schowek) trafia do czatu,
    /// żebyś widział, że Sentinel coś zrobił sam — nigdy po cichu.</summary>
    private void Report(string line) => dispatcher.Post(() =>
    {
        Messages.Add(new("sentinel", line, DateTime.Now));
        while (Messages.Count > 300) Messages.RemoveAt(0);
    });

    public void Dispose()
    {
        pollerTimer?.Stop();
        engine.Changed -= Sync; voice.CommandRecognized -= Recognized;
        memory.Changed -= MemorySync; memory.SessionChanged -= SessionSync;
        tasks.ReminderFired -= ReminderFired;
        lock (streamGate) { pendingChunks.Clear(); streamPending = false; }
        memory.FlushDraft();
    }
}
