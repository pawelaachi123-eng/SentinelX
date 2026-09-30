using System.IO;
using System.Text.Json;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;

namespace SentinelX.Tests;

/// <summary>Durable memory v4: confidence metadata, persistence, identity-based edits, privacy gates, migration, retention, import.</summary>
internal static class MemoryRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static MemoryPrivacy Privacy(bool saveConversations = true, bool useHistory = true, bool saveMemories = true, bool useMemories = true, int retentionDays = 0, bool preview = true)
        => new(saveConversations, useHistory, saveMemories, useMemories, retentionDays, preview);

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- explicit memories survive a "restart" (fresh service instance on the same directory) ---
        string first = Path.Combine(directory, "basic");
        var memory = new ConversationMemoryService(first);
        Check(memory.AddNote("Ulubiony język odpowiedzi to polski") == NoteAddResult.Added, "First note must be stored.");
        memory.AddUserMessage("mam na imię Ada", "test");
        var reloaded = new ConversationMemoryService(first);
        Check(reloaded.GetNotes().Single().Text.Contains("polski"), "Note must survive reload.");
        Check(reloaded.UserName == "Ada", "Profile must survive reload.");
        Check(reloaded.GetNotes().Single().Id.Length == 12, "Notes must carry stable identities.");
        Check(reloaded.GetNotes().Single().Category == "notatka", "Plain notes must default to the note category.");
        Check(reloaded.GetNotes().Single().Confidence >= 0.9 && reloaded.GetNotes().Single().LastConfirmedAt != null,
            "Explicit user memories must retain source confidence and last-confirmed metadata.");
        Check(reloaded.AddNote("wolę krótkie odpowiedzi") == NoteAddResult.Added, "A second note must be stored.");
        Check(reloaded.GetNotes().Any(x => x.Category == "preferencja"), "Preference wording must be categorized.");
        reloaded.AddUserMessage("Wolę krótkie odpowiedzi", "test");
        string profileContext = reloaded.GetRelevantContext("Odpowiedz zgodnie z moim stylem", includeRecent: false);
        Check(profileContext.Contains("explicit user preference", StringComparison.Ordinal) && profileContext.Contains("confidence: 95%", StringComparison.Ordinal),
            "Explicit response preferences must keep their source, confirmation and confidence metadata in profile context.");
        reloaded.DeleteNote(reloaded.GetNotes().Single(x => x.Category == "preferencja").Id);

        // --- identity-based edit/pin/stale/delete ---
        string id = reloaded.GetNotes().Single().Id;
        reloaded.AddNote("Ulubiony język odpowiedzi to polski!"); // similar, not identical
        Check(reloaded.AddNote("Ulubiony język odpowiedzi to polski") == NoteAddResult.Duplicate, "Exact duplicate must be rejected.");
        Check(reloaded.UpdateNote(id, "Preferuję polski język odpowiedzi"), "Edit by ID must work.");
        Check(reloaded.FindNote(id)!.UpdatedAt != null, "Edit must stamp UpdatedAt.");
        Check(reloaded.GetChanges().Any(x => x.Kind == "edytowane" && x.NoteId == id), "Edit must be recorded in the change log.");
        Check(reloaded.SetPinned(id, true) && reloaded.GetNotes().First().Pinned, "Pinned note must sort first.");
        Check(reloaded.SetStale(id, true), "Marking stale must work.");
        Check(reloaded.FindNote(id)!.Confidence <= 0.25, "Marking a memory stale must lower its effective source confidence.");
        Check(!reloaded.GetStableContext().Contains("Preferuję"), "Stale note must stay out of the AI context.");
        Check(!reloaded.DeleteNote(id + "x"), "Unknown ID must not delete anything.");
        Check(reloaded.DeleteNote(id), "Delete by ID must work.");
        Check(reloaded.GetNotes().Single().Text.EndsWith("polski!"), "Similar text must survive the deletion.");
        Check(new ConversationMemoryService(first).GetNotes().Single().Text.EndsWith("polski!"), "Deletion must persist after reload.");

        // --- search is diacritic-insensitive ---
        reloaded.AddNote("Zażółć gęślą jaźń to pangram");
        Check(reloaded.SearchNotes("zazolc gesla").Count == 1, "Search must ignore Polish diacritics.");
        Check(reloaded.SearchNotes("GEŚLĄ").Count == 1, "Search must ignore case and diacritics.");

        // --- bounded, query-relevant retrieval and high-confidence durable fact upserts ---
        string autoDir = Path.Combine(directory, "automatic-facts");
        var automatic = new ConversationMemoryService(autoDir);
        automatic.AddUserMessage("Mam 32 GB RAM", "test");
        Check(automatic.GetNotes().Single().Text == "RAM: 32 GB" && automatic.GetNotes().Single().FactKey == "hardware.ram",
            "An affirmative, unambiguous RAM declaration must become one structured local fact.");
        Check(automatic.GetNotes().Single().Confidence >= 0.8 && automatic.GetNotes().Single().LastConfirmedAt != null,
            "Automatically extracted user facts must carry conservative confidence and confirmation timestamps.");
        Check(automatic.TakeAutomaticMemoryNotice().Contains("Zapisałem lokalnie", StringComparison.Ordinal),
            "Automatic durable capture must be disclosed to the user.");
        automatic.AddNote("Ulubiony kolor to niebieski");
        string computerContext = automatic.GetRelevantContext("Co pamiętasz o moim komputerze?", includeRecent: false);
        Check(computerContext.Contains("RAM: 32 GB", StringComparison.Ordinal) && !computerContext.Contains("Ulubiony kolor", StringComparison.Ordinal),
            "A computer query must retrieve matching memory only, not the whole note database.");
        Check(computerContext.Contains("confidence:", StringComparison.Ordinal) && computerContext.Contains("trafność zapytania:", StringComparison.Ordinal) &&
              computerContext.Contains("ostatnie potwierdzenie:", StringComparison.Ordinal),
            "Retrieved memory context must disclose source confidence, query relevance and last confirmation rather than presenting stale facts as certain.");
        automatic.AddUserMessage("Mój komputer ma 64 GB RAM", "test");
        Check(automatic.GetNotes().Count(x => x.FactKey == "hardware.ram" && x.SupersededAt == null) == 1 &&
              automatic.GetNotes().Single(x => x.FactKey == "hardware.ram").Text == "RAM: 64 GB",
            "A changed stable fact must replace the current value rather than leave conflicting active values.");
        Check(automatic.GetChanges().Any(x => x.Kind == "zaktualizowane" && x.OldText == "RAM: 32 GB" && x.NewText == "RAM: 64 GB"),
            "Superseded RAM values must remain inspectable in the change log.");
        automatic.AddUserMessage("Czy mam 16 GB RAM?", "test");
        Check(automatic.GetNotes().Single(x => x.FactKey == "hardware.ram").Text == "RAM: 64 GB",
            "A question must not be treated as a durable factual assertion.");
        var reloadedAutomatic = new ConversationMemoryService(autoDir);
        Check(reloadedAutomatic.GetNotes().Single(x => x.FactKey == "hardware.ram").Text == "RAM: 64 GB",
            "Structured durable facts must survive a service restart.");
        var privateAutomatic = new ConversationMemoryService(Path.Combine(directory, "private-automatic"));
        privateAutomatic.SetPrivateMode(true);
        privateAutomatic.AddUserMessage("Mam 32 GB RAM", "test");
        Check(privateAutomatic.GetNotes().Count == 0, "Private mode must block automatic durable capture.");
        var secretAutomatic = new ConversationMemoryService(Path.Combine(directory, "secret-automatic"));
        secretAutomatic.AddUserMessage("Mam 128 GB RAM, hasło: SecretValue123", "test");
        Check(secretAutomatic.GetNotes().Count == 0, "An utterance containing a redacted secret must not trigger automatic durable capture.");

        // --- contextual "forget this information" resolves only a unique recent memory and stays approval-gated ---
        string contextualDir = Path.Combine(directory, "contextual-forget");
        var contextualMemory = new ConversationMemoryService(contextualDir);
        contextualMemory.AddNote("RAM: 32 GB", "fakt", "test");
        contextualMemory.AddUserMessage("Mam 32 GB RAM", "test");
        contextualMemory.AddUserMessage("Zapomnij tę informację", "test");
        var contextualPermissions = new PermissionCenterService();
        var contextualAction = new SentinelX.Services.Memory.MemoryActionService(contextualMemory, contextualPermissions, new ActionHistoryService(Path.Combine(contextualDir, "audit")));
        string contextualResponse = contextualAction.TryRequest("Zapomnij tę informację") ?? "";
        Check(contextualPermissions.HasPendingAction && contextualResponse.Contains("RAM: 32 GB", StringComparison.Ordinal),
            "A contextual memory deletion must resolve the unique recent fact but still wait for explicit approval.");

        // --- conflict detection is a hint, never a merge ---
        reloaded.AddNote("Ulubiony kolor: niebieski");
        reloaded.AddNote("Ulubiony kolor: zielony");
        Check(reloaded.FindConflicts().Count == 1, "Conflicting key notes must be reported.");
        Check(reloaded.GetNotes().Count(x => x.Text.StartsWith("Ulubiony kolor")) == 2, "Both versions stay until the user decides.");

        // --- privacy gates work independently ---
        string second = Path.Combine(directory, "privacy");
        var gated = new ConversationMemoryService(second) { PrivacyProvider = () => Privacy(saveConversations: false) };
        gated.AddUserMessage("tajne polecenie", "test");
        gated.AddAssistantMessage("tajna odpowiedź");
        Check(gated.GetAllEntries().Count == 0, "SaveConversations off: no conversation entries in memory.");
        gated.AddNote("ważne ustalenie");
        Check(gated.GetNotes().Count == 1, "SaveConversations off must not block explicit memories.");

        var noAiMemory = new ConversationMemoryService(second) { PrivacyProvider = () => Privacy(useMemories: false) };
        Check(!noAiMemory.GetStableContext().Contains("ważne"), "UseMemoriesForAi off: notes must not enter the AI context.");
        var noSaveMemories = new ConversationMemoryService(second) { PrivacyProvider = () => Privacy(saveMemories: false) };
        Check(noSaveMemories.AddNote("nie powinno się zapisać") == NoteAddResult.Disabled, "SaveMemories off must refuse new notes.");
        Check(!File.ReadAllText(Path.Combine(second, "conversation-memory.json")).Contains("nie powinno"), "Refused note must never reach disk.");
        var noHistory = new ConversationMemoryService(second) { PrivacyProvider = () => Privacy(useHistory: false) };
        noHistory.AddUserMessage("widoczna w pliku", "test"); // saved (saving is a separate toggle)
        Check(!noHistory.GetRecentContext(12).Contains("widoczna w pliku"), "UseHistoryForAi off: recent turns must not enter the AI context.");

        // --- private mode: nothing persists, draft is wiped ---
        string third = Path.Combine(directory, "private");
        var privacy = new ConversationMemoryService(third);
        privacy.SetPrivateMode(true);
        privacy.AddUserMessage("sekretna rozmowa prywatna", "test");
        privacy.AddAssistantMessage("prywatna odpowiedź");
        privacy.SaveDraft("prywatny szkic");
        string privatePath = Path.Combine(third, "conversation-memory.json");
        // Private mode means nothing was ever written: the file may legitimately not exist at all.
        string privateFile = File.Exists(privatePath) ? File.ReadAllText(privatePath) : "";
        Check(!privateFile.Contains("sekretna") && !privateFile.Contains("prywatny szkic"), "Private mode must leave no content on disk.");
        Check(new ConversationMemoryService(third).GetAllEntries().Count == 0, "Private session must not resurrect after restart.");

        // --- the audit trail is redacted in private mode too ---
        string auditDir = Path.Combine(directory, "audit");
        var auditHistory = new ActionHistoryService(auditDir);
        var privateMemory = new ConversationMemoryService(Path.Combine(auditDir, "mem")) { };
        privateMemory.SetPrivateMode(true);
        var toolbox = new SentinelToolboxService(history: auditHistory, memory: privateMemory);
        var engine = new ActionEngine(new EchoRouter(), toolbox, auditHistory, privateMemory, new NoAi());
        await engine.ExecuteAsync("remove all client secrets ZXCVBNM");
        string auditText = string.Join("\n", Directory.GetFiles(Path.Combine(auditDir, "History")).Select(File.ReadAllText));
        Check(!auditText.Contains("ZXCVBNM"), "Private session must redact the audit file.");
        // The serialized audit escapes Polish characters, so assert the marker on parsed entries.
        Check(auditHistory.GetRecentEntries().Any(x => x.Command.Contains("treść niezapisana")), "Audit must record that a redacted action ran.");

        // --- named conversations: auto-title, resume, isolation ---
        string fourth = Path.Combine(directory, "conversations");
        var conversations = new ConversationMemoryService(fourth);
        conversations.AddUserMessage("planuję remont łazienki w marcu", "test");
        string firstSession = conversations.ActiveSessionId;
        Check(conversations.GetConversations().Single(x => x.Id == firstSession).Title.Contains("remont"), "First message must title the conversation.");
        conversations.StartNewSession();
        conversations.AddUserMessage("zupełnie inny temat o ogrodzie", "test");
        Check(conversations.GetConversations().Count == 2, "Each session must register as a conversation.");
        Check(conversations.GetRecentEntries().All(x => x.Text.Contains("ogrodzie")), "New session must not see the old session's turns.");
        Check(conversations.ResumeSession(firstSession), "Resume must work.");
        var resumed = new ConversationMemoryService(fourth);
        Check(resumed.ActiveSessionId == firstSession, "Active conversation must persist across restart.");
        Check(resumed.GetRecentEntries().Any(x => x.Text.Contains("remont")), "Resumed conversation must show its own turns.");
        Check(resumed.GetRecentEntries().All(x => !x.Text.Contains("ogrodzie")), "Projects must not leak between conversations.");

        // --- migration from the v2 format: backup is written, data and identity appear ---
        string fifth = Path.Combine(directory, "migrate");
        Directory.CreateDirectory(fifth);
        string legacyJson = JsonSerializer.Serialize(new
        {
            Version = 2,
            ActiveSessionId = "legacy-session",
            Entries = new[] { new { Timestamp = DateTime.Now.AddHours(-2), Role = "user", Text = "stara wiadomość", Source = "keyboard", SessionId = "legacy-session" } },
            Notes = new[] { new { Timestamp = DateTime.Now.AddDays(-1), Role = "note", Text = "stare wspomnienie z wersji 2", Source = "MEMORY", SessionId = "" } },
            Profile = new Dictionary<string, string> { ["name"] = "Bartek" }
        });
        File.WriteAllText(Path.Combine(fifth, "conversation-memory.json"), legacyJson);
        var migrated = new ConversationMemoryService(fifth);
        Check(migrated.GetNotes().Single().Text.Contains("wersji 2") && migrated.UserName == "Bartek", "Migration must preserve content.");
        Check(migrated.GetNotes().Single().Id.Length == 12 && migrated.GetNotes().Single().Category == "notatka", "Migration must stamp identity and defaults.");
        Check(migrated.GetConversations().Any(x => x.Id == "legacy-session"), "Migration must rebuild the conversation index.");
        Check(Directory.GetFiles(fifth, "*.v2-backup-*").Length == 1, "Migration must keep a backup of the original file.");
        Check(new ConversationMemoryService(fifth).GetNotes().Count == 1 && new ConversationMemoryService(fifth).GetNotes().Single().Confidence > 0,
            "Migrated file must load cleanly with conservative v4 confidence metadata.");

        // A v4 reload must preserve intentional confidence and confirmation metadata exactly.
        string v4Dir = Path.Combine(directory, "v4-preservation");
        Directory.CreateDirectory(v4Dir);
        DateTime confirmedAt = DateTime.Now.AddDays(-4);
        var v4State = new ConversationMemoryState
        {
            Version = 4,
            Notes = [new ConversationMemoryEntry { Id = "v4-note", Role = "note", Text = "imported fact", Source = "import: archive.json", Timestamp = confirmedAt.AddDays(-2), LastConfirmedAt = confirmedAt, Confidence = 0.37 }],
            Profile = new Dictionary<string, string> { ["responseStyle"] = "short" },
            ProfileMetadata = new Dictionary<string, MemoryProfileMetadata>
            {
                ["responseStyle"] = new("import: archive.json", confirmedAt.AddDays(-2), null, 0.50)
            }
        };
        File.WriteAllText(Path.Combine(v4Dir, "conversation-memory.json"), JsonSerializer.Serialize(v4State));
        var preservedV4 = new ConversationMemoryService(v4Dir);
        var preservedNote = preservedV4.GetNotes().Single();
        Check(Math.Abs(preservedNote.Confidence - 0.37) < 0.0001 && preservedNote.LastConfirmedAt == confirmedAt,
            "Loading v4 data must not rewrite valid confidence or confirmation metadata.");
        string preservedProfile = preservedV4.GetRelevantContext("Use my preferred response style", includeRecent: false);
        Check(preservedProfile.Contains("import: archive.json", StringComparison.Ordinal) && preservedProfile.Contains("confidence: 50%", StringComparison.Ordinal),
            "Profile provenance and confidence must survive v4 persistence too.");

        // --- retention prunes conversation history but never explicit memories ---
        string sixth = Path.Combine(directory, "retention");
        var retention = new ConversationMemoryService(sixth) { PrivacyProvider = () => Privacy(retentionDays: 7) };
        retention.AddNote("stare ale ważne wspomnienie");
        retention.AddUserMessage("dzisiejsza wiadomość", "test");
        retention.AddUserMessage("kolejna wiadomość wymuszająca zapis", "test");
        // Simulate an old turn reaching past the retention window.
        string raw = File.ReadAllText(Path.Combine(sixth, "conversation-memory.json"));
        raw = raw.Replace(DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd"));
        File.WriteAllText(Path.Combine(sixth, "conversation-memory.json"), raw);
        var pruned = new ConversationMemoryService(sixth) { PrivacyProvider = () => Privacy(retentionDays: 7) };
        pruned.AddUserMessage("nowy zapis po retencji", "test");
        Check(pruned.GetAllEntries().Count == 1, "Retention must prune old conversation turns.");
        Check(pruned.GetNotes().Single().Text.Contains("stare"), "Retention must keep explicit memories.");

        // --- draft survives restart outside private mode ---
        string seventh = Path.Combine(directory, "draft");
        var draft = new ConversationMemoryService(seventh);
        draft.SaveDraft("niedokończone zdanie na później");
        draft.FlushDraft();
        Check(new ConversationMemoryService(seventh).GetDraft().Contains("niedokończone"), "Draft must survive restart.");

        // --- import: preview first, dedupe, no history import, file untouched on failure ---
        string eighth = Path.Combine(directory, "import");
        var target = new ConversationMemoryService(eighth);
        target.AddNote("istniejące wspomnienie");
        string exportDir = Path.Combine(directory, "import-source");
        Directory.CreateDirectory(exportDir);
        string exportPath = Path.Combine(exportDir, "pamiec.json");
        File.WriteAllText(exportPath, JsonSerializer.Serialize(new
        {
            Version = 3,
            Notes = new[] { new { Text = "istniejące wspomnienie" }, new { Text = "nowe z importu" }, new { Text = "" } },
            Profile = new Dictionary<string, string> { ["styl"] = "rzeczowy" },
            Entries = new[] { new { Role = "user", Text = "NIE wolno importować historii", Timestamp = DateTime.Now, SessionId = "x", Source = "y" } }
        }));
        string before = File.ReadAllText(Path.Combine(eighth, "conversation-memory.json"));
        var preview = target.PreviewImport(exportPath);
        Check(preview.Valid && preview.NotesToAdd == 1 && preview.Duplicates == 1 && preview.Invalid == 1 && preview.ProfileKeys == 1, "Preview must classify rows without writing.");
        Check(File.ReadAllText(Path.Combine(eighth, "conversation-memory.json")) == before, "Preview must not modify the memory file.");
        string summary = target.ImportMemories(exportPath);
        Check(summary.StartsWith("Zaimportowano"), "Import must report the merge.");
        Check(target.GetNotes().Count == 2 && target.GetNotes().Any(x => x.Text == "nowe z importu"), "Import must add only fresh notes.");
        Check(target.GetAllEntries().All(x => !x.Text.Contains("NIE wolno")), "Import must never pull conversation history.");
        Check(target.PreviewImport(exportPath).Duplicates == 2, "Re-import must see everything as duplicates.");
        Check(new ConversationMemoryService(eighth).ImportMemories(exportPath).Contains("0 nowych"), "Reloaded import must be idempotent.");
        var broken = Path.Combine(exportDir, "broken.json");
        File.WriteAllText(broken, "[\"nie\", \"obiekt\"]");
        Check(!target.PreviewImport(broken).Valid, "Non-export JSON must be rejected with a message.");
    }

    private sealed class EchoRouter : IIntentRouter
    {
        public Task<string> ProcessAsync(string input, CancellationToken token) => Task.FromResult("echo: " + input);
    }
    private sealed class NoAi : IAiService
    {
        public string RoutingReason => "test";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => Task.FromResult(input);
    }
}
