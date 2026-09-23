using System.IO;
using System.Text.Json;

namespace SentinelX.Tests;

/// <summary>Projects: CRUD, activation, context isolation, conversation assignment, persistence and export.</summary>
internal static class ProjectRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static MemoryPrivacy FullPrivacy => new(true, true, true, true, 0, true);

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- create, activate, duplicate guard ---
        string dir = Path.Combine(directory, "projects");
        var projects = new ProjectService(dir);
        Check(projects.StoragePath == Path.Combine(dir, "projects.json"), "Projects must live in their own store file.");
        var alfa = projects.Create("Projekt Alfa", "Pierwszy canary ZXCVBNM");
        var beta = projects.Create("Projekt Beta", "Drugi");
        Check(alfa != null && beta != null, "Two projects must be created.");
        Check(projects.ActiveProjectId == beta!.Id, "Creating a project activates it.");
        Check(projects.Create("projekt alfa", "") == null, "Duplicate names must be refused, case-insensitively.");
        Check(projects.Create("", "") == null, "Empty name must be refused.");
        Check(projects.GetProjects().Count == 2, "Both projects must be listed.");

        // --- notes and conversations are stamped with the active project ---
        var memory = new ConversationMemoryService(dir)
        {
            PrivacyProvider = () => FullPrivacy,
            ActiveProjectIdProvider = () => projects.ActiveProjectId,
        };
        projects.Activate(alfa!.Id);
        Check(memory.AddNote("ustalenie projektu alfa ZXCVBNM") == NoteAddResult.Added, "Note under an active project must be stored.");
        string alfaNoteId = memory.GetNotes().Single().Id;
        Check(memory.GetNotes().Single().ProjectId == alfa.Id, "Note must be stamped with the active project.");
        memory.StartNewSession();
        string alfaConversation = memory.ActiveSessionId;
        memory.AddUserMessage("wiadomość w projekcie alfa", "test");
        Check(memory.GetConversationsForProject(alfa.Id).Single().Id == alfaConversation, "New conversation must belong to the active project.");

        projects.Activate(beta.Id);
        memory.AddNote("ustalenie projektu beta");
        memory.StartNewSession();
        string betaConversation = memory.ActiveSessionId;
        projects.Deactivate();
        memory.AddNote("ustalenie globalne");
        memory.StartNewSession();
        string globalConversation = memory.ActiveSessionId;
        Check(memory.GetNotes().Count == 3, "All three notes must be stored.");

        // --- context isolation: a project sees its own and global memories only ---
        projects.Activate(alfa.Id);
        string context = memory.GetStableContext();
        Check(context.Contains("ZXCVBNM"), "Active project notes must reach the AI context.");
        Check(context.Contains("globalne"), "Global notes must reach the project context.");
        Check(!context.Contains("beta"), "Other projects' notes must stay out of the AI context.");
        projects.Deactivate();
        context = memory.GetStableContext();
        Check(context.Contains("beta"), "Without an active project every note is visible again.");

        // --- conversation isolation and resume guards ---
        projects.Activate(beta.Id);
        Check(!memory.ResumeSession(alfaConversation, out string reason) && reason.Contains("projekt"), "Resuming a conversation from another project must be refused with an explanation.");
        Check(!memory.ResumeSession(globalConversation, out _), "Resuming a global conversation under an active project must be refused too.");
        Check(memory.ResumeSession(betaConversation, out _), "Resuming the active project's conversation must work.");
        projects.Deactivate();
        Check(memory.ResumeSession(alfaConversation, out _), "Without an active project any conversation may be resumed.");

        // --- explicit assignment moves conversations between contexts ---
        Check(memory.AssignConversationToProject(globalConversation, beta.Id), "Assignment to a project must persist.");
        Check(memory.GetConversationsForProject(beta.Id).Count == 2, "Both conversations must be listed under Beta.");
        Check(memory.AssignConversationToProject(globalConversation, ""), "Detaching a conversation back to global must work.");

        // --- UI search stays global (transparency); isolation protects the AI context only ---
        projects.Activate(alfa.Id);
        Check(memory.SearchNotes("ustalenie").Count == 3, "UI search must show every note, regardless of the active project.");
        projects.Deactivate();

        // --- restart persistence: fresh instances on the same directories ---
        var projectsReloaded = new ProjectService(dir);
        var memoryReloaded = new ConversationMemoryService(dir) { PrivacyProvider = () => FullPrivacy, ActiveProjectIdProvider = () => projectsReloaded.ActiveProjectId };
        Check(projectsReloaded.GetProjects().Count == 2, "Projects must survive a reload.");
        Check(memoryReloaded.GetNotes().Single(x => x.Id == alfaNoteId).ProjectId == alfa.Id, "Note-to-project stamps must survive a reload.");
        Check(memoryReloaded.GetConversationsForProject(alfa.Id).Single().ProjectId == alfa.Id, "Conversation-to-project stamps must survive a reload.");
        Check(projectsReloaded.VerifyPersistedState(out string evidence), "Project store must verify with read-back evidence: " + evidence);
        Check(StoreParsesBack(projectsReloaded), "Persisted JSON must parse back.");

        // --- export writes a navigable file with the whole project slice ---
        projectsReloaded.Activate(alfa.Id); // activation also persists, so the file is definitely fresh
        projectsReloaded.Deactivate();
        string result = projectsReloaded.Export(alfa.Id, memoryReloaded.GetNotes().Where(x => x.ProjectId == alfa.Id).ToArray(), memoryReloaded.GetConversationsForProject(alfa.Id));
        string exportPath = result["Eksport projektu: ".Length..].Trim();
        Check(File.Exists(exportPath), "Export must create the announced file.");
        using var doc = JsonDocument.Parse(File.ReadAllText(exportPath));
        Check(doc.RootElement.GetProperty("Project").GetProperty("Name").GetString() == "Projekt Alfa", "Export must contain the project card.");
        Check(doc.RootElement.GetProperty("Notes").GetArrayLength() == 1, "Export must contain exactly the project's notes.");
        Check(doc.RootElement.GetProperty("Notes")[0].GetProperty("Text").GetString()!.Contains("ZXCVBNM"), "Export must carry the note text.");

        // --- status cycle and archive keep every byte ---
        Check(projectsReloaded.SetStatus(alfa.Id, ProjectRecord.StatusPaused), "Pausing must work.");
        Check(projectsReloaded.SetStatus(alfa.Id, ProjectRecord.StatusActive), "Resuming must work.");
        projectsReloaded.Activate(beta.Id);
        Check(projectsReloaded.Archive(beta.Id), "Archiving the active project must work.");
        Check(projectsReloaded.ActiveProjectId == null, "Archiving must deactivate the project.");
        Check(projectsReloaded.GetProjects().Count == 1, "Archived projects leave the active list.");
        Check(projectsReloaded.GetProjects(includeArchived: true).Count == 2, "Archive list remains available.");
        Check(memoryReloaded.GetNotes().Any(x => x.ProjectId == beta.Id), "Archiving must never delete project memories.");
        Check(projectsReloaded.Restore(beta.Id), "Restore must bring the project back.");
        Check(projectsReloaded.Find(beta.Id)!.Status == ProjectRecord.StatusPaused, "Restored project comes back paused, not magically active.");
        await Task.CompletedTask;
    }

    private static bool StoreParsesBack(ProjectService service)
    {
        try { using var parsed = JsonDocument.Parse(File.ReadAllText(service.StoragePath)); return parsed.RootElement.GetProperty("Projects").GetArrayLength() == service.GetProjects(includeArchived: true).Count; }
        catch { return false; }
    }
}
