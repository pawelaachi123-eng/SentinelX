using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.History;
namespace SentinelX.ViewModels;
public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService history;
    public ObservableCollection<ActionHistoryEntry> Entries { get; } = [];
    public ObservableCollection<ConversationMemoryEntry> Conversation { get; } = [];
    public ICollectionView FilteredEntries { get; }
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string status = "Odśwież, aby wczytać historię lokalną.";
    [ObservableProperty] private ActionHistoryEntry? selectedEntry;
    public HistoryViewModel(IHistoryService history)
    {
        this.history = history; FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = row => row is ActionHistoryEntry entry &&
            $"{entry.ActionId} {entry.RequestId} {entry.Command} {entry.Status} {entry.Evidence}".Contains(Search, StringComparison.OrdinalIgnoreCase);
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
