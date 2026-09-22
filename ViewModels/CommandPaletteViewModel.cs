using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
namespace SentinelX.ViewModels;
public partial class CommandPaletteViewModel : ObservableObject
{
    public ObservableCollection<PaletteEntry> Results { get; } = [];
    public event Action<PaletteEntry>? Chosen;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private PaletteEntry? selectedEntry;
    [ObservableProperty] private bool hasNoResults;
    public CommandPaletteViewModel() => Filter();
    partial void OnQueryChanged(string value) => Filter();
    private void Filter()
    {
        Results.Clear(); foreach (var entry in CommandCatalog.Search(Query)) Results.Add(entry);
        SelectedEntry = Results.FirstOrDefault(); HasNoResults = Results.Count == 0;
    }
    public void Open() { Query = ""; Filter(); IsOpen = true; }
    [RelayCommand] private void Close() => IsOpen = false;
    [RelayCommand] private void Choose()
    {
        var entry = SelectedEntry;
        if (entry == null && !string.IsNullOrWhiteSpace(Query))
            entry = new(Query.Trim(), "Własne polecenie", "", CommandText: Query.Trim());
        if (entry == null) return;
        IsOpen = false; Chosen?.Invoke(entry);
    }
    [RelayCommand] private void Next() => Move(1);
    [RelayCommand] private void Previous() => Move(-1);
    private void Move(int offset)
    {
        if (Results.Count == 0) return;
        int index = SelectedEntry == null ? 0 : Results.IndexOf(SelectedEntry);
        SelectedEntry = Results[(index + offset + Results.Count) % Results.Count];
    }
}
