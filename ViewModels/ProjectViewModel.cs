using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;

namespace SentinelX.ViewModels;

public sealed class ProjectCardViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsActive { get; init; }
    public bool IsArchived { get; init; }
    public bool IsDone => Status == ProjectRecord.StatusDone;
    public bool IsPaused => Status == ProjectRecord.StatusPaused;
    public string MetaText { get; init; } = "";
    public string ActivateLabel => IsActive ? "Wyłącz kontekst" : "Aktywuj";
    public string ArchiveLabel => IsArchived ? "Przywróć" : "Archiwizuj";
    public string StatusCycleLabel => Status switch
    {
        ProjectRecord.StatusActive => "Wstrzymaj",
        ProjectRecord.StatusPaused => "Wznów",
        _ => "Aktywuj ponownie",
    };
}

public partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly ProjectService projects;
    private readonly ConversationMemoryService memory;
    private readonly IUiDispatcher dispatcher;
    private string? statusOverride;

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = [];

    [ObservableProperty] private string newName = "";
    [ObservableProperty] private string newDescription = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string activeLine = "";
    [ObservableProperty] private ProjectCardViewModel? selectedCard;
    [ObservableProperty] private string editorName = "";
    [ObservableProperty] private string editorDescription = "";
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private bool showArchived;
    public bool HasSummary => SummaryText.Length > 0;
    public event Action<string>? NavigationRequested;

    public ProjectViewModel(ProjectService projects, ConversationMemoryService memory, IUiDispatcher dispatcher)
    {
        this.projects = projects; this.memory = memory; this.dispatcher = dispatcher;
        projects.Changed += Sync; memory.Changed += Sync; memory.SessionChanged += Sync;
        Refresh();
    }

    partial void OnSelectedCardChanged(ProjectCardViewModel? value)
    {
        if (value == null) return;
        var record = projects.Find(value.Id);
        EditorName = record?.Name ?? "";
        EditorDescription = record?.Description ?? "";
    }

    partial void OnSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasSummary));

    [RelayCommand] private void SelectCard(ProjectCardViewModel? card) { if (card != null) SelectedCard = card; }

    private void Sync() => dispatcher.Post(Refresh);

    [RelayCommand]
    private void Refresh()
    {
        var active = projects.ActiveProjectId;
        var notes = memory.GetNotes();
        var cards = projects.GetProjects(ShowArchived).Select(project =>
        {
            int noteCount = notes.Count(x => x.ProjectId == project.Id && x.SupersededAt == null);
            int conversationCount = memory.GetConversationsForProject(project.Id).Count;
            return new ProjectCardViewModel
            {
                Id = project.Id, Name = project.Name, Description = project.Description, Status = project.Status,
                IsActive = project.Id == active, IsArchived = project.ArchivedAt != null,
                MetaText = $"{(project.Id == active ? "▶ aktywny · " : "")}{project.Status}{(project.ArchivedAt != null ? " · zarchiwizowany" : "")} · utworzony {project.CreatedAt:dd.MM.yyyy} · notatki: {noteCount} · rozmowy: {conversationCount}"
            };
        }).ToArray();
        Cards.Clear(); foreach (var card in cards) Cards.Add(card);
        var activeProject = projects.ActiveProject;
        ActiveLine = activeProject == null
            ? "Brak aktywnego projektu — wspomnienia i rozmowy są globalne."
            : $"Aktywny projekt: {activeProject.Name}. Kontekst AI obejmuje tylko globalne wpisy i wpisy tego projektu; rozmowy z innych projektów nie trafiają do modelu.";
        Status = statusOverride ?? projects.LastStorageError ?? memory.LastStorageError ??
            (Cards.Count == 0 ? "Nie ma jeszcze żadnego projektu. Utwórz pierwszy poniżej." : $"Projekty: {Cards.Count} · projekty nie usuwają danych — archiwizacja tylko je ukrywa.");
    }

    private void SetStatus(string message)
    {
        statusOverride = message;
        Status = message;
    }

    [RelayCommand] private void Create()
    {
        string name = NewName;
        var created = projects.Create(name, NewDescription, activate: true);
        if (created == null) { SetStatus(projects.LastStorageError ?? "Nie utworzono projektu."); return; }
        NewName = ""; NewDescription = "";
        SetStatus($"Utworzono i aktywowano projekt „{created.Name}”. Nowa rozmowa trafi do niego automatycznie.");
        Refresh();
    }

    [RelayCommand] private void SaveSelected()
    {
        if (SelectedCard == null) { SetStatus("Wybierz projekt z listy."); return; }
        bool renamed = projects.Rename(SelectedCard.Id, EditorName);
        bool described = projects.SetDescription(SelectedCard.Id, EditorDescription);
        SetStatus(renamed || described ? "Zapisano zmiany projektu." : (projects.LastStorageError ?? "Brak zmian do zapisania."));
        Refresh();
    }

    [RelayCommand] private void ToggleStatus(ProjectCardViewModel? card)
    {
        if (card == null) return;
        string next = card.Status switch
        {
            ProjectRecord.StatusActive => ProjectRecord.StatusPaused,
            ProjectRecord.StatusPaused => ProjectRecord.StatusActive,
            _ => ProjectRecord.StatusActive,
        };
        SetStatus(projects.SetStatus(card.Id, next) ? $"Status projektu: {next}." : "Nie zmieniono statusu.");
        Refresh();
    }

    [RelayCommand] private void ToggleActive(ProjectCardViewModel? card)
    {
        if (card == null) return;
        if (card.IsActive) { projects.Deactivate(); SetStatus("Kontekst projektu wyłączony — wracasz do globalnej pamięci."); }
        else SetStatus(projects.Activate(card.Id) ? $"Aktywowano projekt „{card.Name}”." : projects.LastStorageError ?? "Nie udało się aktywować projektu.");
        Refresh();
    }

    [RelayCommand] private void ToggleArchive(ProjectCardViewModel? card)
    {
        if (card == null) return;
        if (card.IsArchived) SetStatus(projects.Restore(card.Id) ? "Projekt przywrócony ze stanu ukrycia." : "Nie udało się przywrócić projektu.");
        else SetStatus(projects.Archive(card.Id) ? "Projekt zarchiwizowany. Wszystkie notatki i rozmowy są zachowane." : "Nie udało się zarchiwizować projektu.");
        Refresh();
    }

    [RelayCommand] private void NewConversation(ProjectCardViewModel? card)
    {
        if (card == null) return;
        memory.StartNewSession();
        // StartNewSession already stamps the active project; override only when creating for a different one.
        memory.AssignConversationToProject(memory.ActiveSessionId, card.Id);
        SetStatus($"Nowa rozmowa w projekcie „{card.Name}” — wpisz wiadomość w Asystencie, aby ją napełnić.");
        Refresh();
    }

    [RelayCommand] private void AssignActiveConversation(ProjectCardViewModel? card)
    {
        if (card == null) return;
        SetStatus(memory.AssignConversationToProject(memory.ActiveSessionId, card.Id)
            ? $"Aktywna rozmowa przypisana do projektu „{card.Name}”."
            : "Rozmowa już należy do tego projektu.");
        Refresh();
    }

    [RelayCommand] private void Export(ProjectCardViewModel? card)
    {
        if (card == null) return;
        var notes = memory.GetNotes().Where(x => x.ProjectId == card.Id).ToArray();
        var conversations = memory.GetConversationsForProject(card.Id);
        SetStatus(projects.Export(card.Id, notes, conversations));
        Refresh();
    }

    /// <summary>"Gdzie skończyliśmy?" — from stored data only, never fabricated.</summary>
    [RelayCommand] private void WhereDidWeStop(ProjectCardViewModel? card)
    {
        if (card == null) return;
        var lines = new List<string> { $"Projekt „{card.Name}” — ostatni punkt pracy (z zapisanych danych):" };
        var conversations = memory.GetConversationsForProject(card.Id);
        bool resumeConversation = false;
        if (conversations.Count == 0) lines.Add("• Brak rozmów przypisanych do tego projektu.");
        else
        {
            var last = conversations[0];
            lines.Add($"• Ostatnia rozmowa: „{last.Title}” (ostatnio {last.LastActiveAt:dd.MM.yyyy HH:mm}).");
            string reason = "";
            resumeConversation = memory.ActiveSessionId == last.Id;
            if (!resumeConversation) resumeConversation = memory.ResumeSession(last.Id, out reason);
            if (resumeConversation)
                lines.Add("• Rozmowa jest aktywna lub została wznowiona. Otwieram asystenta z jej zapisanym kontekstem.");
            else lines.Add($"• Nie wznowiono rozmowy: {reason}");
        }
        var recentNotes = memory.GetNotes().Where(x => x.ProjectId == card.Id && x.SupersededAt == null).OrderByDescending(x => x.UpdatedAt ?? x.Timestamp).Take(3).ToArray();
        if (recentNotes.Length == 0) lines.Add("• Brak notatek w projekcie.");
        else lines.AddRange(recentNotes.Select(x => $"• Notatka ({(x.UpdatedAt ?? x.Timestamp):dd.MM HH:mm}): {x.Text}"));
        SummaryText = string.Join("\n", lines);
        Refresh();
        if (resumeConversation) NavigationRequested?.Invoke("command");
    }

    partial void OnShowArchivedChanged(bool value) => Refresh();

    public void Dispose() { projects.Changed -= Sync; memory.Changed -= Sync; memory.SessionChanged -= Sync; }
}
