using System.IO;
using System.Text.Json;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;

namespace SentinelX.Tests;

/// <summary>Durable memory v3: persistence, identity-based edits, privacy gates, migration, retention, import.</summary>
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
        Check(reloaded.AddNote("wolę krótkie odpowiedzi") == NoteAddResult.Added, "A second note must be stored.");
        Check(reloaded.GetNotes().Any(x => x.Category == "preferencja"), "Preference wording must be categorized.");
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
        Check(!reloaded.GetStableContext().Contains("Preferuję"), "Stale note must stay out of the AI context.");
        Check(!reloaded.DeleteNote(id + "x"), "Unknown ID must not delete anything.");
        Check(reloaded.DeleteNote(id), "Delete by ID must work.");
        Check(reloaded.GetNotes().Single().Text.EndsWith("polski!"), "Similar text must survive the deletion.");
        Check(new ConversationMemoryService(first).GetNotes().Single().Text.EndsWith("polski!"), "Deletion must persist after reload.");

        // --- search is diacritic-insensitive ---
        reloaded.AddNote("Zażółć gęślą jaźń to pangram");
        Check(reloaded.SearchNotes("zazolc gesla").Count == 1, "Search must ignore Polish diacritics.");
        Check(reloaded.SearchNotes("GEŚLĄ").Count == 1, "Search must ignore case and diacritics.");

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

        // --- private mode blocks all durable writes, scrubs a saved draft, and rolls back attempted mutations ---
        string third = Path.Combine(directory, "private");
        var privacy = new ConversationMemoryService(third);
        privacy.AddNote("zapisane wspomnienie sprzed trybu prywatnego");
        privacy.AddUserMessage("zwykła rozmowa przed trybem prywatnym", "test");
        privacy.SaveDraft("szkic sprzed trybu prywatnego");
        privacy.FlushDraft();
        string persistedSessionId = privacy.ActiveSessionId;
        privacy.SetPrivateMode(true);
        string privatePath = Path.Combine(third, "conversation-memory.json");
        string privateFile = File.ReadAllText(privatePath);
        Check(!privateFile.Contains("szkic sprzed trybu prywatnego"), "Entering private mode must scrub a previously persisted draft.");
        privacy.StartNewSession("prywatna sesja tymczasowa");
        Check(privacy.ActiveSessionId != persistedSessionId, "Starting a temporary session should work while private.");
        Check(privacy.ResumeSession(persistedSessionId), "Selecting an existing conversation must remain available as a read-only operation in private mode.");
        privacy.StartNewSession("druga prywatna sesja");
        privacy.AddUserMessage("sekretna rozmowa prywatna", "test");
        privacy.AddAssistantMessage("prywatna odpowiedź");
        privacy.SaveDraft("prywatny szkic");
        Check(privacy.GetContextForQuestion("zwykła rozmowa przed trybem", includeRecentHistory: true).Length == 0,
            "Private mode must not send even pre-existing memory or history as model context.");
        Check(!privacy.ExportConversationMarkdown().Success, "Private mode must reject conversation exports too.");
        Check(privacy.AddNote("wspomnienie wpisane prywatnie") == NoteAddResult.Disabled, "Private mode must reject durable memory writes.");
        Check(privacy.ImportMemories(Path.Combine(third, "missing-import.json")).Contains("Tryb prywatny"), "Private mode must reject memory imports before reading or writing.");
        bool exportBlocked = false;
        try { privacy.Export(); } catch (InvalidOperationException) { exportBlocked = true; }
        Check(exportBlocked, "Private mode must reject explicit exports.");
        privacy.FlushDraft();
        privateFile = File.ReadAllText(privatePath);
        Check(!privateFile.Contains("sekretna") && !privateFile.Contains("prywatny szkic") && !privateFile.Contains("wpisane prywatnie"), "Private content must never reach the persisted memory file.");
        var privateReloaded = new ConversationMemoryService(third);
        Check(privateReloaded.GetAllEntries().Any(x => x.Text.Contains("zwykła rozmowa przed trybem")) &&
              privateReloaded.GetAllEntries().All(x => !x.Text.Contains("sekretna") && !x.Text.Contains("prywatna odpowiedź")),
            "Existing history must be preserved while private-session content must not resurrect after restart.");
        privacy.SetPrivateMode(false);
        Check(privacy.ActiveSessionId == persistedSessionId, "Exiting private mode must restore the pre-session conversation identity.");
        Check(privacy.GetNotes().Single().Text.Contains("sprzed trybu prywatnego"), "Exiting private mode must preserve the pre-session baseline.");
        Check(privacy.GetNotes().All(x => !x.Text.Contains("wpisane prywatnie")), "Ephemeral mutations must be discarded when private mode ends.");
        Check(privacy.VerifyPersistedState(out _), "Leaving private mode must restore a verifiable persisted baseline.");
        Check(new ConversationMemoryService(third).GetNotes().Count == 1, "Only the pre-private memory may survive a reload.");

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

        // Turning private mode off inside the command must not retroactively persist that private turn.
        string toggleDir = Path.Combine(directory, "private-toggle-race");
        var toggleHistory = new ActionHistoryService(toggleDir);
        var toggleMemory = new ConversationMemoryService(Path.Combine(toggleDir, "mem"));
        toggleMemory.SetPrivateMode(true);
        var toggleToolbox = new SentinelToolboxService(history: toggleHistory, memory: toggleMemory);
        var toggleEngine = new ActionEngine(new TurnOffPrivateRouter(toggleMemory), toggleToolbox, toggleHistory, toggleMemory, new NoAi());
        const string privateTurnCanary = "PRIVATE_TURN_LATCH_CANARY_4B2D";
        await toggleEngine.ExecuteAsync(privateTurnCanary);
        Check(!toggleMemory.PrivateMode, "The fake router must turn private mode off within the request.");
        toggleMemory.AddUserMessage("normal post-private message", "test");
        string toggleAuditText = File.ReadAllText(toggleHistory.HistoryPath);
        string toggleMemoryText = File.ReadAllText(toggleMemory.StoragePath);
        Check(toggleAuditText.Contains("treść niezapisana") &&
              !toggleAuditText.Contains(privateTurnCanary) && !toggleAuditText.Contains("Odpowiedź prywatna:") &&
              !toggleMemoryText.Contains(privateTurnCanary) && !toggleMemoryText.Contains("Odpowiedź prywatna:"),
            "The request's start-of-turn privacy latch must redact both audit and conversation output after privacy is toggled off.");

        string intervalDir = Path.Combine(directory, "private-interval-race");
        var intervalHistory = new ActionHistoryService(intervalDir);
        var intervalMemory = new ConversationMemoryService(Path.Combine(intervalDir, "mem"));
        var intervalToolbox = new SentinelToolboxService(history: intervalHistory, memory: intervalMemory);
        var intervalEngine = new ActionEngine(new PrivateIntervalRouter(intervalMemory), intervalToolbox, intervalHistory, intervalMemory, new NoAi());
        const string intervalReplyCanary = "PRIVATE_INTERVAL_REPLY_CANARY_6E13";
        await intervalEngine.ExecuteAsync("harmless privacy-toggle test");
        intervalMemory.AddUserMessage("normal message after privacy interval", "test");
        string intervalAuditText = File.ReadAllText(intervalHistory.HistoryPath);
        string intervalMemoryText = File.ReadAllText(intervalMemory.StoragePath);
        Check(intervalAuditText.Contains("treść niezapisana") && !intervalAuditText.Contains(intervalReplyCanary) && !intervalMemoryText.Contains(intervalReplyCanary),
            "Entering and leaving private mode during one request must latch redaction until its result is persisted.");

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
        Check(resumed.GetRecentEntries().All(x => !x.Text.Contains("ogrodzie")), "Conversations must not leak across sessions.");

        // --- explicit recall obeys both the active session and the active project boundary ---
        string recallDir = Path.Combine(directory, "project-recall");
        string activeProject = "alpha";
        var scopedRecall = new ConversationMemoryService(recallDir) { ActiveProjectIdProvider = () => activeProject };
        scopedRecall.AddUserMessage("alpha marker: alpha-only conversation secret", "test");
        scopedRecall.AddNote("alpha-note-marker: alpha-only memory");
        scopedRecall.AddNote("Shared setting: blue");
        Check(scopedRecall.TryFindTextAfter("alpha-note-marker:", null, out string alphaMemory) && alphaMemory == "alpha-only memory",
            "Recall must still find a note belonging to the active project.");
        activeProject = "beta";
        scopedRecall.StartNewSession();
        scopedRecall.AddUserMessage("beta latest request", "test");
        scopedRecall.AddNote("Shared setting: red");
        Check(scopedRecall.FindSimilarNotes("Shared setting: red").Count == 0, "Similar-note hints must not reveal another project's notes.");
        Check(!scopedRecall.TryFindTextAfter("alpha marker:", "beta latest request", out _), "Text recall must not search a different project's old conversation.");
        Check(!scopedRecall.TryFindTextAfter("alpha-note-marker:", "beta latest request", out _), "Text recall must not search another project's notes.");
        Check(!scopedRecall.TryGetUserMessageFromAgo(TimeSpan.Zero, "beta latest request", out _), "Time-based recall must stay inside the active session instead of falling back to another project.");

        // --- query-aware long-term notes and short-term retrieval within only the active conversation ---
        string retrievalDir = Path.Combine(directory, "retrieval");
        var retrieval = new ConversationMemoryService(retrievalDir);
        retrieval.AddNote("Project Phoenix release date: April 12.");
        string staleId = retrieval.GetNotes().Single().Id;
        Check(retrieval.SetStale(staleId, true), "The old Phoenix note must be markable as stale.");
        retrieval.AddNote("Project Phoenix current launch date: April 18.");
        for (int i = 0; i < 35; i++) retrieval.AddNote($"Unrelated note {i}: office supplies and gardening details.");
        string longTermContext = retrieval.GetContextForQuestion("What is the current Phoenix launch date?", includeRecentHistory: false);
        Check(longTermContext.Contains("April 18") && !longTermContext.Contains("April 12"), "Relevant older long-term notes must be retrieved while stale notes stay excluded.");
        Check(retrieval.LastContextTrace.Any(x => x.Kind == "wspomnienie" && x.Reason.Contains("dopasowanie leksykalne")), "The explainable context trace must say why a retrieved note was included.");

        var shortTerm = new ConversationMemoryService(Path.Combine(directory, "short-term"));
        shortTerm.AddUserMessage("Which icon did you prefer?", "test");
        shortTerm.AddAssistantMessage("For project Nebula, we chose the blue launch icon.");
        for (int i = 0; i < 20; i++)
        {
            shortTerm.AddUserMessage($"Unrelated topic number {i}: weather in a different city.", "test");
            shortTerm.AddAssistantMessage($"The forecast for city {i} is mild.");
        }
        shortTerm.AddUserMessage("What did we decide about the Nebula project?", "test");
        string shortTermContext = shortTerm.GetContextForQuestion("What did we decide about the Nebula project?", includeRecentHistory: true, maxEntries: 2);
        Check(shortTermContext.Contains("blue launch icon") && shortTermContext.Contains("starszy trafiony fragment"), "A follow-up must retrieve the matching older user/assistant exchange.");
        shortTerm.StartNewSession();
        Check(!shortTerm.GetContextForQuestion("Nebula project", includeRecentHistory: true).Contains("blue launch icon"), "Retrieved short-term history must remain isolated to its active session.");
        string oldSession = shortTerm.GetConversations().Single(x => x.Title.Contains("Which icon")).Id;
        shortTerm.ResumeSession(oldSession);
        shortTerm.PrivacyProvider = () => Privacy(useHistory: false);
        Check(!shortTerm.GetContextForQuestion("Nebula project", includeRecentHistory: true).Contains("blue launch icon"), "The history privacy toggle must block query-aware short-term retrieval.");

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
        Check(new ConversationMemoryService(fifth).GetNotes().Count == 1, "Migrated file must load cleanly as v3.");

        // --- profile keys remain case-insensitive across JSON reloads and do not split into aliases ---
        string profileDirectory = Path.Combine(directory, "profile-casing");
        Directory.CreateDirectory(profileDirectory);
        var casedProfile = new ConversationMemoryState { Version = 3, Profile = new Dictionary<string, string> { ["Name"] = "Ada" } };
        File.WriteAllText(Path.Combine(profileDirectory, "conversation-memory.json"), JsonSerializer.Serialize(casedProfile));
        var profileMemory = new ConversationMemoryService(profileDirectory);
        Check(profileMemory.UserName == "Ada", "A cased profile key must still be readable as the canonical user name.");
        profileMemory.AddUserMessage("mam na imię Ewa", "test");
        using (var profileDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(profileDirectory, "conversation-memory.json"))))
        {
            int nameKeys = profileDocument.RootElement.GetProperty("Profile").EnumerateObject()
                .Count(x => x.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
            Check(nameKeys == 1 && new ConversationMemoryService(profileDirectory).UserName == "Ewa",
                "Updating a cased profile key must replace it instead of creating duplicate name aliases.");
        }

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
        target.ActiveProjectIdProvider = () => "project-import";
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
        string disabledImportDirectory = Path.Combine(directory, "import-disabled");
        var importDisabled = new ConversationMemoryService(disabledImportDirectory) { PrivacyProvider = () => Privacy(saveMemories: false) };
        Check(importDisabled.ImportMemories(exportPath).Contains("wyłączone") && !File.Exists(Path.Combine(disabledImportDirectory, "conversation-memory.json")),
            "SaveMemories off must block imports before any storage file is created.");
        string before = File.ReadAllText(Path.Combine(eighth, "conversation-memory.json"));
        var preview = target.PreviewImport(exportPath);
        Check(preview.Valid && preview.NotesToAdd == 1 && preview.Duplicates == 1 && preview.Invalid == 1 && preview.ProfileKeys == 1, "Preview must classify rows without writing.");
        Check(File.ReadAllText(Path.Combine(eighth, "conversation-memory.json")) == before, "Preview must not modify the memory file.");
        string summary = target.ImportMemories(exportPath);
        Check(summary.StartsWith("Zaimportowano"), "Import must report the merge.");
        Check(target.GetNotes().Count == 2 && target.GetNotes().Any(x => x.Text == "nowe z importu"), "Import must add only fresh notes.");
        Check(target.GetNotes().Single(x => x.Text == "nowe z importu").ProjectId == "project-import", "Imported notes must inherit the active project instead of becoming global by accident.");
        target.ActiveProjectIdProvider = () => "another-project";
        string isolatedImportContext = target.GetStableContext();
        Check(isolatedImportContext.Contains("istniejące wspomnienie") && !isolatedImportContext.Contains("nowe z importu"), "An import into one project must not leak into another project's context.");
        Check(target.GetAllEntries().All(x => !x.Text.Contains("NIE wolno")), "Import must never pull conversation history.");
        Check(target.PreviewImport(exportPath).Duplicates == 2, "Re-import must see everything as duplicates.");
        Check(new ConversationMemoryService(eighth).ImportMemories(exportPath).Contains("0 nowych"), "Reloaded import must be idempotent.");
        var broken = Path.Combine(exportDir, "broken.json");
        File.WriteAllText(broken, "[\"nie\", \"obiekt\"]");
        Check(!target.PreviewImport(broken).Valid, "Non-export JSON must be rejected with a message.");

        // Capacity-aware previews must match the actual merge instead of promising more notes than can fit.
        string cappedDirectory = Path.Combine(directory, "import-capacity");
        Directory.CreateDirectory(cappedDirectory);
        var almostFullState = new ConversationMemoryState
        {
            Notes = Enumerable.Range(0, 499).Select(i => new ConversationMemoryEntry
            {
                Timestamp = DateTime.Now, Role = "note", Text = $"existing capped note {i}", Id = i.ToString("D12"), Category = "notatka"
            }).ToList()
        };
        File.WriteAllText(Path.Combine(cappedDirectory, "conversation-memory.json"), JsonSerializer.Serialize(almostFullState));
        var cappedMemory = new ConversationMemoryService(cappedDirectory);
        string capacityImportPath = Path.Combine(exportDir, "capacity.json");
        File.WriteAllText(capacityImportPath, JsonSerializer.Serialize(new
        {
            Version = 3,
            Notes = new[] { new { Text = "capacity import A" }, new { Text = "capacity import B" }, new { Text = "capacity import C" } }
        }));
        var capacityPreview = cappedMemory.PreviewImport(capacityImportPath);
        Check(capacityPreview.NotesToAdd == 1 && capacityPreview.Message.Contains("pominięte unikalne wpisy: 2"), "Import preview must disclose capacity-limited unique notes.");
        string capacitySummary = cappedMemory.ImportMemories(capacityImportPath);
        Check(cappedMemory.NoteCount == 500 && capacitySummary.Contains("Zaimportowano 1"), "Import must add exactly the amount previewed when the memory limit is reached.");
    }

    private sealed class EchoRouter : IIntentRouter
    {
        public Task<string> ProcessAsync(string input, CancellationToken token) => Task.FromResult("echo: " + input);
    }
    private sealed class TurnOffPrivateRouter(ConversationMemoryService memory) : IIntentRouter
    {
        public Task<string> ProcessAsync(string input, CancellationToken token)
        {
            memory.SetPrivateMode(false);
            return Task.FromResult("Odpowiedź prywatna: " + input);
        }
    }
    private sealed class PrivateIntervalRouter(ConversationMemoryService memory) : IIntentRouter
    {
        public Task<string> ProcessAsync(string input, CancellationToken token)
        {
            memory.SetPrivateMode(true);
            memory.SetPrivateMode(false);
            return Task.FromResult("PRIVATE_INTERVAL_REPLY_CANARY_6E13");
        }
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
