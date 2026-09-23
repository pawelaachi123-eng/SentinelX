using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SentinelX;

public sealed class ProjectRecord
{
    public const string StatusActive = "aktywny";
    public const string StatusPaused = "wstrzymany";
    public const string StatusDone = "zakończony";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = StatusActive;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public sealed class ProjectState
{
    public int Version { get; set; } = 1;
    public string ActiveProjectId { get; set; } = "";
    public List<ProjectRecord> Projects { get; set; } = [];
}

/// <summary>Project workspaces: own memory namespace, assigned conversations, resumable "where did we stop" view.</summary>
public sealed class ProjectService
{
    private const int MaxProjects = 200;
    private readonly object syncRoot = new();
    private readonly string storePath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private ProjectState state = new();
    public string? LastStorageError { get; private set; }
    public string StoragePath => storePath;
    public event Action? Changed;

    public ProjectService(string? projectsDirectory = null)
    {
        storePath = Path.Combine(projectsDirectory ?? AppPaths.MemoryDirectory, "projects.json");
        Load();
    }

    public string? ActiveProjectId { get { lock (syncRoot) return state.ActiveProjectId.Length == 0 ? null : state.ActiveProjectId; } }
    public ProjectRecord? ActiveProject { get { lock (syncRoot) { var project = state.Projects.FirstOrDefault(x => x.Id == state.ActiveProjectId); return project == null ? null : Clone(project); } } }

    public IReadOnlyList<ProjectRecord> GetProjects(bool includeArchived = false)
    {
        lock (syncRoot)
            return state.Projects.Where(x => includeArchived || x.ArchivedAt == null).OrderBy(x => x.ArchivedAt != null).ThenByDescending(x => x.UpdatedAt).Select(Clone).ToArray();
    }

    public ProjectRecord? Find(string id) { lock (syncRoot) return state.Projects.Where(x => x.Id == id).Select(Clone).FirstOrDefault(); }

    public ProjectRecord? Create(string name, string description, bool activate = true)
    {
        bool fire = false;
        ProjectRecord created;
        lock (syncRoot)
        {
            name = (name ?? "").Trim();
            if (state.Projects.Count(x => x.ArchivedAt == null) >= MaxProjects) { LastStorageError = "Osiągnięto limit 200 projektów."; return null; }
            if (name.Length is < 1 or > 80) { LastStorageError = "Nazwa projektu musi mieć 1–80 znaków."; return null; }
            if (state.Projects.Any(x => x.ArchivedAt == null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { LastStorageError = "Projekt o takiej nazwie już istnieje."; return null; }
            created = new() { Id = Guid.NewGuid().ToString("N")[..12], Name = name, Description = (description ?? "").Trim()[..Math.Min((description ?? "").Trim().Length, 4000)], CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
            state.Projects.Add(created);
            if (activate) state.ActiveProjectId = created.Id;
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return Clone(created);
    }

    public bool Rename(string id, string name)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            name = (name ?? "").Trim();
            if (project == null || name.Length is < 1 or > 80) return false;
            if (state.Projects.Any(x => x.Id != id && x.ArchivedAt == null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { LastStorageError = "Inny projekt ma już taką nazwę."; return false; }
            project.Name = name; project.UpdatedAt = DateTime.Now;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public bool SetDescription(string id, string description)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            description = (description ?? "").Trim();
            if (project == null || description.Length > 4000) return false;
            project.Description = description; project.UpdatedAt = DateTime.Now;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool SetStatus(string id, string status)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            if (project == null || status is not (ProjectRecord.StatusActive or ProjectRecord.StatusPaused or ProjectRecord.StatusDone)) return false;
            project.Status = status; project.UpdatedAt = DateTime.Now;
            if (status == ProjectRecord.StatusDone && state.ActiveProjectId == id) state.ActiveProjectId = "";
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    /// <summary>Archiving keeps every note and conversation; it only hides the project from active lists.</summary>
    public bool Archive(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            if (project == null || project.ArchivedAt != null) return false;
            project.ArchivedAt = DateTime.Now; project.UpdatedAt = DateTime.Now; project.Status = ProjectRecord.StatusDone;
            if (state.ActiveProjectId == id) state.ActiveProjectId = "";
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool Restore(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            if (project == null || project.ArchivedAt == null) return false;
            project.ArchivedAt = null; project.Status = ProjectRecord.StatusPaused; project.UpdatedAt = DateTime.Now;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool Activate(string id) => SetActive(id);
    public void Deactivate() => SetActive("");

    private bool SetActive(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            if (id.Length > 0 && !state.Projects.Any(x => x.Id == id && x.ArchivedAt == null)) { LastStorageError = "Projekt nie istnieje lub jest zarchiwizowany."; return false; }
            if (state.ActiveProjectId == id) return true;
            state.ActiveProjectId = id;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    /// <summary>Exports the project with its memories and conversation metadata. Conversation bodies stay in the main memory store.</summary>
    public string Export(string id, IReadOnlyList<ConversationMemoryEntry> projectNotes, IReadOnlyList<ConversationInfo> projectConversations)
    {
        lock (syncRoot)
        {
            var project = state.Projects.FirstOrDefault(x => x.Id == id);
            if (project == null) return "Projekt nie istnieje — eksport przerwany, nic nie zapisano.";
            string directory = Path.Combine(Path.GetDirectoryName(storePath)!, "Exports");
            Directory.CreateDirectory(directory);
            string safe = new string(project.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            string path = Path.Combine(directory, $"projekt-{(safe.Length == 0 ? id : safe)}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
            var payload = new { FormatVersion = 1, ExportedAt = DateTime.Now, Project = Clone(project), Notes = projectNotes, Conversations = projectConversations };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8);
            LastStorageError = null;
            return "Eksport projektu: " + path;
        }
    }

    internal bool VerifyPersistedState(out string evidence)
    {
        lock (syncRoot)
        {
            try
            {
                if (LastStorageError != null) { evidence = LastStorageError; return false; }
                string expected = JsonSerializer.Serialize(state, jsonOptions);
                if (File.ReadAllText(storePath, Encoding.UTF8) != expected) { evidence = "Zapis projektów nie odpowiada bieżącemu stanowi."; return false; }
                evidence = $"Odczyt zwrotny: {storePath}; SHA-256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(storePath)))}; projekty: {state.Projects.Count}.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { evidence = "Nie można sprawdzić zapisu projektów: " + ex.Message; return false; }
        }
    }

    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(storePath)) return;
                if (new FileInfo(storePath).Length > 5 * 1024 * 1024) throw new IOException("Plik projektów przekracza 5 MB.");
                var loaded = JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
                if (loaded == null) throw new JsonException("Pusty plik projektów.");
                state = loaded;
                state.Projects ??= [];
                foreach (var project in state.Projects)
                {
                    if (project.Id.Length == 0) project.Id = Guid.NewGuid().ToString("N")[..12];
                    if (project.Status.Length == 0) project.Status = ProjectRecord.StatusActive;
                }
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                state = new();
                LastStorageError = "Nie udało się wczytać projektów: " + ex.Message;
                try { if (File.Exists(storePath)) File.Copy(storePath, storePath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", false); }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { LastStorageError += " Nie udało się utworzyć kopii."; }
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            string temporary = storePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, jsonOptions), Encoding.UTF8);
            File.Move(temporary, storePath, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastStorageError = "Zmiany projektów działają tylko do zamknięcia aplikacji. Błąd zapisu: " + ex.Message; }
    }

    private static ProjectRecord Clone(ProjectRecord x) => new() { Id = x.Id, Name = x.Name, Description = x.Description, Status = x.Status, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt, ArchivedAt = x.ArchivedAt };
}

