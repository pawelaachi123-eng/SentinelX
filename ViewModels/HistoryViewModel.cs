using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.History;
using SentinelX.Services.Actions;
namespace SentinelX.ViewModels;
public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService history;
    private readonly IActionEngine engine;
    public IReadOnlyList<string> StatusOptions { get; } = ["Wszystkie", "VERIFIED", "UNVERIFIED", "FAILED", "CANCELLED", "PENDING", "RUNNING", "INTERRUPTED"];
    [ObservableProperty] private string statusFilter = "Wszystkie";
    [ObservableProperty] private string exportSummary = "Eksport zawiera do 200 ostatnich stanów akcji, niezależnie od filtra; prywatne komendy i ścieżki. Zapis lokalny, bez wysyłania.";
    public ObservableCollection<ActionHistoryEntry> Entries { get; } = [];
    public ObservableCollection<ConversationMemoryEntry> Conversation { get; } = [];
    public ICollectionView FilteredEntries { get; }
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string status = "Odśwież, aby wczytać historię lokalną.";
    [ObservableProperty] private ActionHistoryEntry? selectedEntry;
    public HistoryViewModel(IHistoryService history, IActionEngine engine)
    {
        this.history = history; this.engine = engine; FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = row => row is ActionHistoryEntry entry &&
            (StatusFilter == "Wszystkie" || entry.Status == StatusFilter) &&
            ConversationMemoryService.Normalize($"{entry.ActionId} {entry.RequestId} {entry.Command} {entry.Status} {entry.Evidence}")
                .Contains(ConversationMemoryService.Normalize(Search), StringComparison.Ordinal);
    }
    partial void OnStatusFilterChanged(string value) => FilteredEntries.Refresh();
    [RelayCommand] private async Task ExportAsync(string format)
    {
        if (format is not ("json" or "csv")) return;
        try
        {
            var result = await engine.ExecuteAsync("eksportuj historie " + format);
            ExportSummary = result.Text + (result.Action?.StorageWarning is { Length: > 0 } warning ? "\n" + warning : "");
        }
        catch (Exception ex) { ExportSummary = "Błąd eksportu: " + ex.Message; }
    }
    partial void OnSearchChanged(string value) => FilteredEntries.Refresh();
    [RelayCommand] private async Task RefreshAsync()
    {
        try
        {
            var actions = await Task.Run(history.ReadActions);
            var conversation = await Task.Run(history.ReadConversation);
            Entries.Clear(); foreach (var entry in actions) Entries.Add(entry);
            Conversation.Clear(); foreach (var entry in conversation) Conversation.Add(entry);
            Status = history.StorageError ?? $"{Entries.Count} ostatnich akcji · {Conversation.Count} wiadomości · tylko dane lokalne";
        }
        catch (Exception ex) { Status = "Błąd odczytu historii: " + ex.Message; }
    }
}
