using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;

namespace SentinelX.ViewModels;

public sealed class TaskItemViewModel
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Priority { get; init; } = "";
    public bool HighPriority => Priority == TaskRecord.PriorityHigh;
    public string StatusLabel { get; init; } = "";
    public string MetaText { get; init; } = "";
    public string StatusCycleLabel { get; init; } = "";
}

public sealed class ReminderItemViewModel
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public string MetaText { get; init; } = "";
    public bool Awaiting => NotifiedAt == null;
    public DateTime? NotifiedAt { get; init; }
    public bool Missed { get; init; }
}

public partial class TaskViewModel : ObservableObject, IDisposable
{
    public const string FilterToday = "Dzisiaj";
    public const string FilterAll = "Wszystkie";
    public const string FilterOverdue = "Przeterminowane";
    public const string FilterDone = "Zrobione";

    private readonly TaskService tasks;
    private readonly ProjectService projects;
    private readonly IUiDispatcher dispatcher;
    private readonly DispatcherTimer dueTimer;

    public ObservableCollection<TaskItemViewModel> Items { get; } = [];
    public ObservableCollection<ReminderItemViewModel> Reminders { get; } = [];
    public ObservableCollection<string> ProjectOptions { get; } = [];
    private readonly List<string> projectIds = [];
    public IReadOnlyList<string> FilterOptions { get; } = [FilterToday, FilterAll, FilterOverdue, FilterDone];
    public IReadOnlyList<string> PriorityOptions { get; } = [TaskRecord.PriorityNormal, TaskRecord.PriorityLow, TaskRecord.PriorityHigh];
    public bool HasItems => Items.Count > 0;
    public bool HasReminders => Reminders.Count > 0;
    public event Action? DashboardChanged;

    [ObservableProperty] private string filter = FilterToday;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string banner = "";
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private string newDueText = "";
    [ObservableProperty] private string newDuePreview = "";
    [ObservableProperty] private string newPriority = TaskRecord.PriorityNormal;
    [ObservableProperty] private int newProjectIndex;
    [ObservableProperty] private string newReminderText = "";
    [ObservableProperty] private string newReminderWhen = "";
    [ObservableProperty] private string newReminderPreview = "";
    [ObservableProperty] private TaskItemViewModel? selectedTask;
    [ObservableProperty] private TaskItemViewModel? pendingDeleteTask;
    [ObservableProperty] private ReminderItemViewModel? pendingDeleteReminder;
    [ObservableProperty] private string deleteConfirmationText = "";
    [ObservableProperty] private bool isDeleteConfirmationOpen;
    [ObservableProperty] private string editorTitle = "";
    [ObservableProperty] private string editorDueText = "";
    [ObservableProperty] private string editorDuePreview = "";
    public TaskViewModel(TaskService tasks, ProjectService projects, IUiDispatcher dispatcher)
    {
        this.tasks = tasks; this.projects = projects; this.dispatcher = dispatcher;
        tasks.Changed += Sync; tasks.ReminderFired += FiredSync; projects.Changed += SyncProjects;
        RebuildProjects();
        Refresh();
        dueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        dueTimer.Tick += (_, _) => tasks.CheckDue();
        dueTimer.Start();
    }

    partial void OnFilterChanged(string value) => RefreshItems();
    partial void OnNewDueTextChanged(string value) => NewDuePreview = Preview(value, out _);
    partial void OnNewReminderWhenChanged(string value) => NewReminderPreview = Preview(value, out _);
    partial void OnEditorDueTextChanged(string value)
        => EditorDuePreview = value.Trim().Length == 0 ? "(bez terminu)" : PolishTimeParser.TryParse(value, DateTime.Now, out _, out string d) ? "→ " + d : "⚠ " + d;
    partial void OnSelectedTaskChanged(TaskItemViewModel? value)
    {
        if (value == null) return;
        EditorTitle = value.Title;
        EditorDueText = "";
        EditorDuePreview = "(bez zmian terminu)";
    }

    private static string Preview(string raw, out DateTime? when)
    {
        when = null;
        if (raw.Trim().Length == 0) return "(opcjonalnie: „jutro o 18”, „za 2 godziny”, „24.12 o 12”)";
        if (PolishTimeParser.TryParse(raw, DateTime.Now, out DateTime parsed, out string description)) { when = parsed; return "→ " + description; }
        return "⚠ " + description;
    }

    private void Sync() => dispatcher.Post(Refresh);
    private void FiredSync(string line) => dispatcher.Post(() =>
    {
        Banner = line + " — przypomnienie zadziałało (aplikacja przypomina tylko, gdy jest uruchomiona).";
        Refresh();
    });

    private void SyncProjects() => dispatcher.Post(() => { RebuildProjects(); });

    private void RebuildProjects()
    {
        int selectedProjectIndex = NewProjectIndex;
        string previous = selectedProjectIndex >= 0 && selectedProjectIndex < projectIds.Count ? projectIds[selectedProjectIndex] : "";
        projectIds.Clear(); projectIds.Add("");
        ProjectOptions.Clear(); ProjectOptions.Add("(bez projektu)");
        foreach (var project in projects.GetProjects()) { projectIds.Add(project.Id); ProjectOptions.Add(project.Name); }
        NewProjectIndex = Math.Max(0, projectIds.IndexOf(previous.Length > 0 ? previous : projects.ActiveProjectId ?? ""));
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshItems();
        RefreshReminders();
        Status = tasks.LastStorageError ?? projects.LastStorageError ??
            (Items.Count == 0 && Reminders.Count == 0
                ? "Brak pozycji dla tego filtra. Dodaj zadanie lub przypomnienie poniżej."
                : $"Zadania: {Items.Count} na liście · Przypomnienia: {Reminders.Count} · wszystkie czasy to czas lokalny tego komputera.");
        DashboardChanged?.Invoke();
    }

    private void RefreshItems()
    {
        bool includeDone = Filter == FilterDone;
        var today = DateTime.Today;
        var source = tasks.GetTasks(includeDone).AsEnumerable();
        source = Filter switch
        {
            FilterToday => source.Where(x => x.DueAt == null || x.DueAt.Value.Date <= today),
            FilterOverdue => source.Where(x => x.DueAt != null && x.DueAt.Value.Date < today),
            FilterDone => source.Where(x => x.Status == TaskRecord.StatusDone),
            _ => source,
        };
        var projectNames = projects.GetProjects(includeArchived: true).ToDictionary(x => x.Id, x => x.Name);
        var items = source.Select(x => ToViewModel(x, projectNames, today)).ToArray();
        Items.Clear(); foreach (var item in items) Items.Add(item);
        OnPropertyChanged(nameof(HasItems));
        if (SelectedTask != null && items.All(x => x.Id != SelectedTask.Id)) SelectedTask = null;
    }

    /// <summary>Dashboard projection independent of the interactive task-page filter.</summary>
    public IReadOnlyList<TaskItemViewModel> GetDashboardItems(int limit = 4)
    {
        if (limit <= 0) return Array.Empty<TaskItemViewModel>();
        DateTime today = DateTime.Today;
        var projectNames = projects.GetProjects(includeArchived: true).ToDictionary(x => x.Id, x => x.Name);
        return tasks.GetTasks().Take(limit).Select(item => ToViewModel(item, projectNames, today)).ToArray();
    }

    private static TaskItemViewModel ToViewModel(TaskRecord task, IReadOnlyDictionary<string, string> projectNames, DateTime today) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Priority = task.Priority,
        StatusLabel = task.Status,
        MetaText = $"{(task.Priority == TaskRecord.PriorityHigh ? "‼ " : "")}{task.Status}{(task.DueAt != null ? $" · termin: {task.DueAt:dd.MM.yyyy HH:mm}{(task.DueAt.Value.Date < today && task.Status != TaskRecord.StatusDone ? " (po terminie)" : "")}" : "")}{(task.ProjectId.Length > 0 && projectNames.TryGetValue(task.ProjectId, out string? name) ? $" · projekt: {name}" : "")} · utworzone {task.CreatedAt:dd.MM.yyyy}",
        StatusCycleLabel = task.Status == TaskRecord.StatusOpen ? "W toku" : task.Status == TaskRecord.StatusDoing ? "Zrobione ✓" : "Przywróć",
    };

    private void RefreshReminders()
    {
        var now = DateTime.Now;
        var reminders = tasks.GetReminders().Select(x =>
        {
            string meta;
            if (x.NotifiedAt == null)
            {
                var span = x.RemindAt - now;
                meta = $"za {(span.Days > 0 ? span.Days + " d " : "")}{Math.Max(0, (int)span.TotalHours)} h — {x.RemindAt:dd.MM.yyyy HH:mm}";
            }
            else meta = $"{(x.Missed ? "⚠ przegapione (aplikacja była zamknięta)" : "dostarczone")} · termin {x.RemindAt:dd.MM.yyyy HH:mm}";
            return new ReminderItemViewModel { Id = x.Id, Text = x.Text, NotifiedAt = x.NotifiedAt, Missed = x.Missed, MetaText = meta };
        }).ToArray();
        Reminders.Clear(); foreach (var reminder in reminders) Reminders.Add(reminder);
        OnPropertyChanged(nameof(HasReminders));
    }

    [RelayCommand]
    private void AddTask()
    {
        string title = NewTitle;
        string dueText = NewDueText;
        int selectedProjectIndex = NewProjectIndex;
        var projectId = selectedProjectIndex > 0 && selectedProjectIndex < projectIds.Count ? projectIds[selectedProjectIndex] : "";
        DateTime? due = null;
        if (dueText.Trim().Length > 0)
        {
            if (!PolishTimeParser.TryParse(dueText, DateTime.Now, out DateTime parsed, out string description)) { Status = "Termin nieczytelny: " + description + " Nic nie zapisano."; return; }
            due = parsed;
        }
        var created = tasks.AddTask(title, NewPriority, due, projectId);
        if (created == null) { Status = tasks.LastStorageError ?? "Nie dodano zadania."; return; }
        NewTitle = "";
        string result = due != null ? $"Dodano zadanie z terminem {due:dd.MM.yyyy HH:mm} (czas lokalny)." : "Dodano zadanie bez terminu.";
        Refresh();
        Status = result;
    }

    [RelayCommand]
    private void AddReminder()
    {
        string text = NewReminderText;
        string whenText = NewReminderWhen;
        if (!PolishTimeParser.TryParse(whenText, DateTime.Now, out DateTime parsed, out string description))
        { Status = "Termin nieczytelny: " + description + " Nic nie zapisano."; return; }
        if (parsed <= DateTime.Now) { Status = "Termin już minął — nic nie zapisano. Wolisz „za N minut” albo „jutro o …”."; return; }
        var created = tasks.AddReminder(text, parsed, "");
        if (created == null) { Status = tasks.LastStorageError ?? "Nie ustawiono przypomnienia."; return; }
        NewReminderText = ""; NewReminderWhen = "";
        string result = $"Ustawiono przypomnienie na {description}. Zadziała tylko, gdy Sentinel jest uruchomiony; spóźnione pokażą się jako przegapione.";
        Refresh();
        Status = result;
    }

    [RelayCommand]
    private void CycleStatus(TaskItemViewModel? task)
    {
        if (task == null) return;
        string next = task.StatusLabel == TaskRecord.StatusOpen ? TaskRecord.StatusDoing : task.StatusLabel == TaskRecord.StatusDoing ? TaskRecord.StatusDone : TaskRecord.StatusOpen;
        bool changed = tasks.SetTaskStatus(task.Id, next);
        Refresh();
        Status = changed ? $"Status: {next}." : (tasks.LastStorageError ?? "Nie zmieniono statusu.");
    }

    [RelayCommand]
    private void DeleteTask(TaskItemViewModel? task)
    {
        if (task == null) return;
        PendingDeleteTask = task;
        PendingDeleteReminder = null;
        DeleteConfirmationText = $"Usunąć zadanie „{task.Title}”? Tej zmiany nie można cofnąć. Usunięty zostanie tylko ten wpis.";
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void DeleteReminder(ReminderItemViewModel? reminder)
    {
        if (reminder == null) return;
        PendingDeleteTask = null;
        PendingDeleteReminder = reminder;
        DeleteConfirmationText = $"Usunąć przypomnienie „{reminder.Text}”? Tej zmiany nie można cofnąć.";
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmationOpen = false;
        PendingDeleteTask = null;
        PendingDeleteReminder = null;
        DeleteConfirmationText = "";
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        string result;
        if (PendingDeleteTask is { } task)
        {
            bool removed = tasks.DeleteTask(task.Id);
            result = removed
                ? "Usunięto zadanie (nieodwracalnie; tylko ten wpis — eksporty i audyt pozostały)."
                : (tasks.LastStorageError ?? "Nie usunięto — zadanie nie istnieje.");
        }
        else if (PendingDeleteReminder is { } reminder)
        {
            bool removed = tasks.DeleteReminder(reminder.Id);
            result = removed ? "Usunięto przypomnienie (nieodwracalnie, tylko ten wpis)." : (tasks.LastStorageError ?? "Nie usunięto — przypomnienie nie istnieje.");
        }
        else
        {
            Status = "Nie ma wybranego wpisu do usunięcia.";
            CancelDelete();
            return;
        }
        CancelDelete();
        Refresh();
        Status = result;
    }

    [RelayCommand] private void SelectTask(TaskItemViewModel? task) { if (task != null) SelectedTask = task; }

    [RelayCommand]
    private void SaveSelected()
    {
        if (SelectedTask == null) { Status = "Wybierz zadanie z listy."; return; }
        bool renamed = tasks.RenameTask(SelectedTask.Id, EditorTitle);
        DateTime? newDue = null; bool dueChanged = false;
        if (EditorDueText.Trim().Length > 0)
        {
            if (!PolishTimeParser.TryParse(EditorDueText, DateTime.Now, out DateTime parsed, out string dueError))
            {
                Refresh();
                Status = $"Termin nieczytelny ({dueError}) — zmiana terminu pominięta. Tytuł: {(renamed ? "zapisany" : "bez zmian")}.";
                return;
            }
            newDue = parsed; dueChanged = true;
        }
        bool dueSet = dueChanged && tasks.SetTaskDue(SelectedTask.Id, newDue);
        string result = renamed || dueSet ? "Zapisano zmiany zadania." : (tasks.LastStorageError ?? "Brak zmian do zapisania.");
        Refresh();
        Status = result;
    }

    public void Dispose()
    {
        dueTimer.Stop();
        tasks.Changed -= Sync; tasks.ReminderFired -= FiredSync; projects.Changed -= SyncProjects;
    }
}
