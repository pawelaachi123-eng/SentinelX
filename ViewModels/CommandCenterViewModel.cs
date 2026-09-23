using System.Collections.ObjectModel;
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
    public CommandCenterViewModel(IActionEngine engine, IVoiceService voice, IUiDispatcher dispatcher,
        SystemViewModel system, VoiceViewModel voiceViewModel, IHistoryService history, ConversationMemoryService memory, TaskService tasks)
    {
        this.engine = engine; this.voice = voice; this.dispatcher = dispatcher; this.history = history; this.memory = memory; this.tasks = tasks; System = system; Voice = voiceViewModel;
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        ConversationTitle = memory.ActiveConversationTitle; IsPrivateMode = memory.PrivateMode;
        if (!memory.PrivateMode && memory.GetDraft().Length > 0) UserInput = memory.GetDraft();
        engine.Changed += Sync; voice.CommandRecognized += Recognized;
        memory.Changed += MemorySync; memory.SessionChanged += SessionSync;
        tasks.ReminderFired += ReminderFired;
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
        UserInput = ""; await SubmitAsync(input);
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
    private async Task SubmitAsync(string input, bool fromVoice = false)
    {
        Messages.Add(new("user", input, DateTime.Now));
        Status = "Przetwarzanie polecenia…";
        var result = await engine.ExecuteAsync(input, fromVoice: fromVoice);
        Messages.Add(new("sentinel", result.Text, DateTime.Now, result.Action));
        while (Messages.Count > 300) Messages.RemoveAt(0);
        Status = result.Action?.StorageWarning is { Length: > 0 } warning ? warning : history.StorageError ?? (engine.IsStopped ? "STOP awaryjny · nowe akcje zablokowane" : "Gotowe · wyniki akcji znajdziesz w Historii");
        if (fromVoice) voice.Speak(result.Text);
    }
    public void Dispose()
    {
        engine.Changed -= Sync; voice.CommandRecognized -= Recognized;
        memory.Changed -= MemorySync; memory.SessionChanged -= SessionSync;
        tasks.ReminderFired -= ReminderFired;
        memory.FlushDraft();
    }
}
