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
    public SystemViewModel System { get; }
    public VoiceViewModel Voice { get; }
    public ObservableCollection<ConversationMessage> Messages { get; } = [];
    [ObservableProperty] private string userInput = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isStopped;
    [ObservableProperty] private bool hasPermission;
    [ObservableProperty] private string permissionSummary = "";
    [ObservableProperty] private string status = "Lokalny asystent · gotowy na polecenie";
    public CommandCenterViewModel(IActionEngine engine, IVoiceService voice, IUiDispatcher dispatcher,
        SystemViewModel system, VoiceViewModel voiceViewModel, IHistoryService history)
    {
        this.engine = engine; this.voice = voice; this.dispatcher = dispatcher; this.history = history; System = system; Voice = voiceViewModel;
        foreach (var entry in history.ReadConversation().TakeLast(100)) Messages.Add(new(entry.Role, entry.Text, entry.Timestamp));
        engine.Changed += Sync; voice.CommandRecognized += Recognized;
    }
    private void Sync() => dispatcher.Post(() =>
    {
        IsBusy = engine.IsBusy; IsStopped = engine.IsStopped;
        HasPermission = engine.HasPendingPermission; PermissionSummary = engine.PermissionSummary;
    });
    private void Recognized(string input) => dispatcher.Post(() => _ = SubmitAsync(input, true));
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var input = UserInput.Trim(); if (input.Length == 0) return;
        UserInput = ""; await SubmitAsync(input);
    }
    [RelayCommand] private Task QuickCommandAsync(string input) => SubmitAsync(input);
    [RelayCommand] private Task ApproveAsync() => SubmitAsync("potwierdz");
    [RelayCommand] private void Cancel() => engine.Cancel();
    private async Task SubmitAsync(string input, bool fromVoice = false)
    {
        Messages.Add(new("user", input, DateTime.Now));
        Status = "Przetwarzanie polecenia…";
        var result = await engine.ExecuteAsync(input, fromVoice: fromVoice);
        Messages.Add(new("sentinel", result.Text, DateTime.Now, result.Action));
        while (Messages.Count > 300) Messages.RemoveAt(0);
        Status = history.StorageError ?? (engine.IsStopped ? "STOP awaryjny · nowe akcje zablokowane" : "Gotowe · wyniki akcji znajdziesz w Historii");
        if (fromVoice) voice.Speak(result.Text);
    }
    public void Dispose() { engine.Changed -= Sync; voice.CommandRecognized -= Recognized; }
}
