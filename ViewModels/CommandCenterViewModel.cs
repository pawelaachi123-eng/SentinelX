using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.History;
using SentinelX.Services.Voice;
namespace SentinelX.ViewModels;
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
    private readonly StringBuilder pendingChunks = new();
    private readonly object streamGate = new();
    private bool streamPending;
    private string lastUserInput = "";
    public CommandCenterViewModel(IActionEngine engine, IVoiceService voice, IUiDispatcher dispatcher,
        SystemViewModel system, VoiceViewModel voiceViewModel, IHistoryService history, ConversationMemoryService memory, TaskService tasks,
        MemoryArchiveService? archives = null)
    {
        this.engine = engine; this.voice = voice; this.dispatcher = dispatcher; this.history = history; this.memory = memory; this.tasks = tasks; System = system; Voice = voiceViewModel;
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode;
        if (!memory.PrivateMode && memory.GetDraft().Length > 0) UserInput = memory.GetDraft();
        engine.Changed += Sync; voice.CommandRecognized += Recognized;
        memory.Changed += MemorySync; memory.SessionChanged += SessionSync;
        tasks.ReminderFired += ReminderFired;
        foreach (var archived in archives?.ArchiveDue() ?? [])
            Messages.Add(new("assistant", $"📦 Rozmowy z {archived.Month} przeniesione do archiwum ({archived.Conversations} rozmów, {archived.Turns} wypowiedzi) i usunięte z aktywnego magazynu. Folder: {archived.JsonPath}. Wspomnienia są nietknięte. Polecenie „archiwa” pokazuje listę.", DateTime.Now));
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
    partial void OnUserInputChanged(string value) => memory.SaveDraft(value);
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
        UserInput = "";
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
    private async Task SubmitAsync(string input, bool fromVoice = false, bool isRetry = false)
    {
        lastUserInput = input;
        Messages.Add(new("user", input + (isRetry ? "  (ponowione)" : ""), DateTime.Now));
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
        engine.Changed -= Sync; voice.CommandRecognized -= Recognized;
        memory.Changed -= MemorySync; memory.SessionChanged -= SessionSync;
        tasks.ReminderFired -= ReminderFired;
        lock (streamGate) { pendingChunks.Clear(); streamPending = false; }
        memory.FlushDraft();
    }
}
