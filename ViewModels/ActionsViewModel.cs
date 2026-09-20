using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
namespace SentinelX.ViewModels;
public partial class ActionsViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    public ObservableCollection<ActionRecord> Tasks { get; } = [];
    [ObservableProperty] private string permissionSummary = "Brak oczekujących zgód.";
    [ObservableProperty] private bool hasPermission;
    [ObservableProperty] private string status = "Brak zadań w tej sesji. Zleć polecenie w Command Center.";
    public ActionsViewModel(IActionEngine engine, IUiDispatcher dispatcher)
    { this.engine = engine; this.dispatcher = dispatcher; engine.ActionStarted += Started; engine.Changed += Sync; }
    private void Started(ActionRecord action) => dispatcher.Post(() =>
    { Tasks.Insert(0, action); while (Tasks.Count > 100) Tasks.RemoveAt(Tasks.Count - 1); Status = "Zadania bieżącej sesji · trwałe dowody w Historii"; });
    private void Sync() => dispatcher.Post(() => { HasPermission = engine.HasPendingPermission; PermissionSummary = engine.PermissionSummary; });
    [RelayCommand] private void Cancel() => engine.Cancel();
    [RelayCommand] private async Task ApproveAsync() => Status = (await engine.ExecuteAsync("potwierdz")).Text;
    public void Dispose() { engine.ActionStarted -= Started; engine.Changed -= Sync; }
}
