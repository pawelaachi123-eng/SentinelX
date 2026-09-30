using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Memory;
using SentinelX.Services.Settings;
namespace SentinelX.ViewModels;

public sealed class MemoryItemViewModel
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public bool Pinned { get; init; }
    public bool Stale { get; init; }
    public string MetaText { get; init; } = "";
    public string PinLabel => Pinned ? "Odepnij" : "Przypnij";
    public string StaleLabel => Stale ? "Przywróć" : "Nieaktualne";
}

public sealed class ConversationListItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string MetaText { get; init; } = "";
    public bool IsActive { get; init; }
}

public partial class MemoryViewModel : ObservableObject, IDisposable
{
    private const string AllCategories = "(wszystkie)";
    private readonly ConversationMemoryService memory;
    private readonly MemoryActionService memoryActions;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    public ObservableCollection<MemoryItemViewModel> Items { get; } = [];
    public ObservableCollection<ConversationListItem> Conversations { get; } = [];
    public ObservableCollection<string> Trace { get; } = [];
    public IReadOnlyList<string> FilterOptions { get; }
    public IReadOnlyList<string> CategoryOptions => ConversationMemoryService.Categories;

    [ObservableProperty] private string search = "";
    [ObservableProperty] private string categoryFilter = AllCategories;
    [ObservableProperty] private bool pinnedOnly;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string privacyLine = "";
    [ObservableProperty] private string newText = "";
    [ObservableProperty] private string newCategory = "notatka";
    [ObservableProperty] private MemoryItemViewModel? selectedItem;
    [ObservableProperty] private string editorText = "";
    [ObservableProperty] private ConversationListItem? selectedConversation;
    [ObservableProperty] private string renameTitle = "";
    [ObservableProperty] private string importPath = "";
    [ObservableProperty] private string importSummary = "";
    [ObservableProperty] private bool isPrivateMode;
    [ObservableProperty] private string conflictHint = "";

    public MemoryViewModel(ConversationMemoryService memory, MemoryActionService memoryActions, ISettingsService settings, IUiDispatcher dispatcher)
    {
        this.memory = memory; this.memoryActions = memoryActions; this.settings = settings; this.dispatcher = dispatcher;
        FilterOptions = new[] { AllCategories }.Concat(ConversationMemoryService.Categories).ToArray();
        memory.Changed += Sync; memory.SessionChanged += Sync; settings.Changed += Sync;
        Refresh();
    }

    partial void OnSearchChanged(string value) => RefreshItems();
    partial void OnCategoryFilterChanged(string value) => RefreshItems();
    partial void OnPinnedOnlyChanged(bool value) => RefreshItems();
    partial void OnSelectedItemChanged(MemoryItemViewModel? value) { EditorText = value?.Text ?? ""; }
    partial void OnSelectedConversationChanged(ConversationListItem? value) { RenameTitle = value?.Title ?? ""; }

    private void Sync() => dispatcher.Post(Refresh);

    private void Refresh()
    {
        RefreshItems();
        string active = memory.ActiveSessionId;
        var conversations = memory.GetConversations().Select(x => new ConversationListItem
        {
            Id = x.Id, Title = x.Title, IsActive = x.Id == active,
            MetaText = $"{(x.Id == active ? "▶ aktywna · " : "")}utworzona {x.CreatedAt:dd.MM.yyyy} · ostatnio {x.LastActiveAt:dd.MM HH:mm}"
        }).ToArray();
        Conversations.Clear(); foreach (var item in conversations) Conversations.Add(item);
        Trace.Clear();
        if (memory.PrivacyContextVisible)
        {
            foreach (var slice in memory.LastContextTrace) Trace.Add($"{slice.Kind} · {slice.Label} — {slice.Reason}");
            if (Trace.Count == 0) Trace.Add("Brak — od uruchomienia nie wysłano jeszcze zapytania z kontekstem.");
        }
        else Trace.Add("Podgląd kontekstu jest wyłączony lub aktywny jest tryb prywatny. Źródła kontekstu nie są rejestrowane.");
        IsPrivateMode = memory.PrivateMode;
        var s = settings.Current.Memory;
        PrivacyLine = $"Ruch zewnętrzny Sentinel: {(memory.ExternalNetworkAllowed ? "DOZWOLONY" : "ZABLOKOWANY · tylko lokalnie") } · Zapis rozmów: {On(s.SaveConversations)} · AI czyta historię: {On(s.UseHistoryForAi)} · Zapis wspomnień: {On(s.SaveMemories)} · AI czyta wspomnienia: {On(s.UseMemoriesForAi)} · Retencja: {(s.RetentionDays > 0 ? s.RetentionDays + " dni" : "bezterminowa")}";
        UpdateConflicts();
        Status = memory.LastStorageError ?? $"Wspomnienia: {Items.Count} · Rozmowy: {conversations.Length} · Zmiany konfiguracji działają od razu, bez restartu.";
    }

    private static string On(bool value) => value ? "wł" : "wył";

    private void RefreshItems()
    {
        IEnumerable<ConversationMemoryEntry> source = string.IsNullOrWhiteSpace(Search) ? memory.GetNotes() : memory.SearchNotes(Search);
        if (CategoryFilter is not null && CategoryFilter != AllCategories) source = source.Where(x => x.Category == CategoryFilter);
        if (PinnedOnly) source = source.Where(x => x.Pinned);
        var items = source.Select(x => new MemoryItemViewModel
        {
            Id = x.Id, Category = string.IsNullOrEmpty(x.Category) ? "notatka" : x.Category, Text = x.Text, Source = x.Source, Pinned = x.Pinned, Stale = x.SupersededAt != null,
            MetaText = $"{(x.Pinned ? "📌 " : "")}{(x.SupersededAt != null ? "nieaktualne · " : "")}{x.Category} · utworzone {x.Timestamp:dd.MM.yyyy HH:mm}{(x.UpdatedAt != null ? $" · zmienione {x.UpdatedAt:dd.MM.yyyy HH:mm}" : "")} · ostatnio potwierdzone: {(x.LastConfirmedAt?.ToString("dd.MM.yyyy HH:mm") ?? "brak")} · pewność źródła {Math.Clamp(x.Confidence, 0, 1):P0} · źródło: {x.Source}"
        }).ToArray();
        Items.Clear(); foreach (var item in items) Items.Add(item);
        if (SelectedItem != null && items.All(x => x.Id != SelectedItem.Id)) SelectedItem = null;
    }

    private void UpdateConflicts()
    {
        var conflicts = memory.FindConflicts();
        ConflictHint = conflicts.Count == 0 ? "" :
            "Możliwe sprzeczne wspomnienia (zdecyduj, która wersja obowiązuje):\n" + string.Join("\n", conflicts.Take(3).Select(x => $"• „{x.A.Text}”  vs  „{x.B.Text}”"));
    }

    [RelayCommand] private void RefreshData() => Refresh();

    [RelayCommand]
    private void AddNote()
    {
        string text = NewText.Trim();
        if (text.Length == 0) { Status = "Wpisz treść wspomnienia przed zapisaniem."; return; }
        var similar = memory.FindSimilarNotes(text);
        Status = memoryActions.AddNoteVerified(text, NewCategory, "panel pamięci");
        NewText = "";
        Refresh();
        if (similar.Count > 0)
            ConflictHint = "Podobne istniejące wspomnienia:\n" + string.Join("\n", similar.Take(3).Select(x => "• " + x.Text)) + "\nJeśli wpisy się wykluczają, oznacz stare jako nieaktualne.";
    }

    [RelayCommand]
    private void SaveEdit()
    {
        if (SelectedItem == null) return;
        Status = memoryActions.UpdateNoteVerified(SelectedItem.Id, EditorText.Trim());
        Refresh();
    }

    [RelayCommand] private void BeginEdit(MemoryItemViewModel? item) { if (item != null) SelectedItem = item; }

    [RelayCommand] private void CancelEdit() => SelectedItem = null;

    [RelayCommand]
    private void TogglePin(MemoryItemViewModel? item)
    {
        if (item == null) return;
        Status = memoryActions.SetPinnedVerified(item.Id, !item.Pinned);
        Refresh();
    }

    [RelayCommand]
    private void ToggleStale(MemoryItemViewModel? item)
    {
        if (item == null) return;
        Status = memoryActions.SetStaleVerified(item.Id, !item.Stale);
        Refresh();
    }

    [RelayCommand]
    private void RequestDelete(MemoryItemViewModel? item)
    {
        if (item == null) return;
        Status = memoryActions.RequestDeleteNote(item.Id, item.Text)
            + " Usuwanie wymaga zgody — zatwierdź ją klawiaturą w Centrum.";
        Refresh();
    }

    [RelayCommand]
    private void NewConversation()
    {
        memory.StartNewSession();
        Status = "Rozpoczęto nową rozmowę. Wpisanie pierwszego polecenia nada jej tytuł.";
        Refresh();
    }

    [RelayCommand]
    private void ResumeConversation(ConversationListItem? item)
    {
        if (item == null) return;
        Status = memory.ResumeSession(item.Id)
            ? $"Wznowiono rozmowę „{item.Title}”. Centrum pokazuje jej treść."
            : "Ta rozmowa jest już aktywna.";
        Refresh();
    }

    [RelayCommand]
    private void RenameConversation()
    {
        if (SelectedConversation == null || RenameTitle.Trim().Length == 0) { Status = "Wybierz rozmowę i wpisz nowy tytuł."; return; }
        Status = memory.RenameConversation(SelectedConversation.Id, RenameTitle.Trim())
            ? "Zmieniono tytuł rozmowy."
            : "Nie zmieniono tytułu (1–120 znaków, rozmowa musi istnieć).";
        Refresh();
    }

    [RelayCommand]
    private void TogglePrivateMode()
    {
        memory.SetPrivateMode(!memory.PrivateMode);
        Status = memory.PrivateMode
            ? "Tryb prywatny WŁĄCZONY — treść rozmowy nie jest nigdzie zapisywana i nie wróci po restarcie."
            : "Tryb prywatny WYŁĄCZONY — zapis zgodny z ustawieniami prywatności.";
        Refresh();
    }

    [RelayCommand]
    private void PreviewImport()
    {
        var preview = memory.PreviewImport(ImportPath.Trim());
        ImportSummary = preview.Message;
    }

    [RelayCommand]
    private void RunImport()
    {
        ImportSummary = memoryActions.ImportVerified(ImportPath.Trim());
        Refresh();
    }

    [RelayCommand]
    private void ExportMemory()
    {
        try { Status = "Eksport lokalny zapisany: " + memory.Export(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Nie udało się wyeksportować pamięci: " + ex.Message; }
    }

    public void Dispose() { memory.Changed -= Sync; memory.SessionChanged -= Sync; settings.Changed -= Sync; }
}
