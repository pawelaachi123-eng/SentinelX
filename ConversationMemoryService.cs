using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public sealed class ConversationMemoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string SessionId { get; set; } = "";
    // Format v3: stable identity and lifecycle metadata for explicit memories.
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Pinned { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? SupersededAt { get; set; }
    /// <summary>Most recent explicit reaffirmation or user edit; null means no confirmation was recorded.</summary>
    public DateTime? LastConfirmedAt { get; set; }
    /// <summary>Source confidence, not a probability that the text is objectively true.</summary>
    public double Confidence { get; set; } = 0.55;
    /// <summary>Empty = global memory. Project memories are included in context only while that project is active.</summary>
    public string ProjectId { get; set; } = "";
    /// <summary>Optional stable key for safely upserting an automatically recognized durable fact.</summary>
    public string FactKey { get; set; } = "";
}

public sealed class ConversationInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    /// <summary>Empty = global conversation. Active project filtering isolates contexts between projects.</summary>
    public string ProjectId { get; set; } = "";
}

public sealed class MemoryChange
{
    public string NoteId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string OldText { get; set; } = "";
    public string NewText { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public sealed record MemoryProfileMetadata(string Source, DateTime CreatedAt, DateTime? LastConfirmedAt, double Confidence);

public sealed class ConversationMemoryState
{
    public int Version { get; set; } = 4;
    public string ActiveSessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ConversationMemoryEntry> Entries { get; set; } = [];
    public List<ConversationMemoryEntry> Notes { get; set; } = [];
    public Dictionary<string, string> Profile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MemoryProfileMetadata> ProfileMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ConversationInfo> Conversations { get; set; } = [];
    public List<MemoryChange> Changes { get; set; } = [];
    public string Draft { get; set; } = "";
}

/// <summary>Memory privacy decisions taken per call, so a settings change applies immediately without a restart.</summary>
public sealed record MemoryPrivacy(bool SaveConversations, bool UseHistoryForAi, bool SaveMemories, bool UseMemoriesForAi, int RetentionDays, bool ContextPreview)
{
    public static MemoryPrivacy Default { get; } = new(true, true, true, true, 0, true);
}

public enum NoteAddResult { Added, Duplicate, Limit, Invalid, Disabled, StaleDuplicate, Updated }

/// <summary>One line of the explainable context trace: what reached the model and why.</summary>
public sealed record ContextSlice(string Kind, string Id, string Label, string Reason);

/// <summary>Result of a conversation export: path + read-back hash, or the honest reason nothing was written.</summary>
public sealed record ConversationExport(string? Path, string Sha256, int Turns, string Error)
{
    public bool Success => Path != null && Error.Length == 0;
}

public sealed record ImportPreview(bool Valid, string Message, int NotesToAdd, int Duplicates, int Invalid, int ProfileKeys);

/// <summary>Bounded conversation history and separate durable explicit memories, stored only locally.</summary>
public sealed class ConversationMemoryService : Services.Memory.IConversationMemory
{
    private const int MaxEntries = 720;
    private const int MaxNotes = 500;
    private const int MaxChanges = 200;
    private const int MaxConversations = 100;
    private const int ContextBudget = 12000;
    private const int StableMemoryBudget = 4000;
    public static readonly string[] Categories = ["notatka", "preferencja", "fakt", "decyzja", "zadanie", "narzędzie", "umiejętność"];
    private readonly object syncRoot = new();
    private readonly string memoryPath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private ConversationMemoryState state = new();
    private DateTime lastDraftSaveUtc = DateTime.MinValue;
    public string? LastStorageError { get; private set; }
    public string StoragePath => memoryPath;
    /// <summary>Set by the host (per settings). Null = everything allowed, matching historical behaviour.</summary>
    public Func<MemoryPrivacy>? PrivacyProvider { get; set; }
    public Func<bool>? ExternalNetworkAllowedProvider { get; set; }
    /// <summary>Session-only learned material; never serialized and only read when memory use is enabled.</summary>
    public Func<string, string>? EphemeralSkillContextProvider { get; set; }
    /// <summary>Current project id supplied by the host (ProjectService). Null/empty = no project filtering.</summary>
    public Func<string?>? ActiveProjectIdProvider { get; set; }
    private string? ActiveProject => ActiveProjectIdProvider?.Invoke();
    /// <summary>Runtime-only switch that prevents chat text and action audit content from being persisted. Explicitly requested file outputs are separate. Never persisted.</summary>
    public bool PrivateMode { get; private set; }
    public string ActiveSessionId { get { lock (syncRoot) return state.ActiveSessionId; } }
    public int Count { get { lock (syncRoot) return state.Entries.Count + state.Notes.Count; } }
    public int NoteCount { get { lock (syncRoot) return state.Notes.Count; } }
    public string UserName { get { lock (syncRoot) return state.Profile.GetValueOrDefault("name", ""); } }
    public IReadOnlyList<ContextSlice> LastContextTrace { get; private set; } = [];
    public DateTime? LastContextBuiltAt { get; private set; }
    /// <summary>A user-visible notice for a high-confidence local auto-capture in the current turn.</summary>
    public string LastAutomaticMemoryNotice { get; private set; } = "";
    /// <summary>True when the context trace is being recorded and may be shown in the UI.</summary>
    public bool PrivacyContextVisible => !PrivateMode && Privacy.ContextPreview;
    public event Action? Changed;
    public event Action? SessionChanged;

    public ConversationMemoryService(string? memoryDirectory = null)
    {
        memoryPath = Path.Combine(memoryDirectory ?? Path.Combine(AppPaths.Root, "Memory"), "conversation-memory.json");
        Load();
    }

    private MemoryPrivacy Privacy => PrivacyProvider?.Invoke() ?? MemoryPrivacy.Default;
    /// <summary>True when the current turn must leave no durable trace (private session or conversation saving off).</summary>
    public bool IsEphemeral => PrivateMode || !Privacy.SaveConversations;
    /// <summary>Autopilot checkpoints are durable local memory and follow both conversation and memory privacy toggles.</summary>
    public bool CanPersistAutopilotGoals => !PrivateMode && Privacy.SaveConversations && Privacy.SaveMemories;
    public bool CanPersistExplicitMemories => !PrivateMode && Privacy.SaveMemories;
    /// <summary>External network is controlled by the explicit local-only lock; private mode only controls local persistence.</summary>
    public bool ExternalNetworkAllowed => ExternalNetworkAllowedProvider?.Invoke() ?? true;
    /// <summary>Privacy gate for any historical chat/action context sent to a model.</summary>
    public bool CanUseHistoryForAi => !PrivateMode && Privacy.SaveConversations && Privacy.UseHistoryForAi;

    public void SetPrivateMode(bool enabled)
    {
        bool fire = false;
        lock (syncRoot)
        {
            if (PrivateMode == enabled) return;
            PrivateMode = enabled;
            if (enabled)
            {
                if (state.Draft.Length > 0) { state.Draft = ""; SaveLocked(); }
                LastContextTrace = []; LastContextBuiltAt = null;
            }
            fire = true;
        }
        if (fire) Changed?.Invoke();
    }

    // ------------------------------ messages ------------------------------
    public void AddUserMessage(string text, string source) => AddMessage("user", text, source);
    public void AddAssistantMessage(string text) => AddMessage("assistant", text, "SENTINEL");

    private void AddMessage(string role, string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = SensitiveDataRedactor.Redact(text);
        bool fire = false;
        lock (syncRoot)
        {
            LastAutomaticMemoryNotice = "";
            var privacy = Privacy;
            // Private mode: no trace at all — neither a conversation turn nor a profile update from private content.
            if (PrivateMode) return;
            if (role == "user" && privacy.SaveMemories)
            {
                ExtractProfile(text);
                if (!text.Contains("[REDACTED]", StringComparison.Ordinal) && !SensitiveDataRedactor.ContainsLikelySecret(text) && !LooksLikeExplicitMemoryCommand(text) &&
                    TryParseStableFact(text, out string factKey, out string factText))
                    LastAutomaticMemoryNotice = UpsertStableFactLocked(factKey, factText, "automatic") switch
                    {
                        NoteAddResult.Added => "Zapisałem lokalnie trwałą informację: " + factText + ". Możesz ją sprawdzić lub zmienić w pamięci.",
                        NoteAddResult.Updated => "Zaktualizowałem lokalnie trwałą informację: " + factText + ". Poprzednia wartość nie jest już traktowana jako aktualna.",
                        NoteAddResult.StaleDuplicate => "Przywróciłem lokalnie oznaczoną wcześniej informację: " + factText + ".",
                        _ => ""
                    };
            }
            if (!privacy.SaveConversations)
            {
                // Durable facts/profile may still be saved when conversation history is disabled.
                if (role == "user" && privacy.SaveMemories) { SaveLocked(); fire = true; }
            }
            else
            {
                AddEntryLocked(role, text, source);
                var conversation = EnsureConversationLocked(state.ActiveSessionId);
                if (role == "user" && (conversation.Title.Length == 0 || conversation.Title == "Nowa rozmowa"))
                    conversation.Title = MakeTitle(text);
                conversation.LastActiveAt = DateTime.Now;
                ApplyRetentionLocked(privacy);
                SaveLocked();
                fire = true;
            }
        }
        if (fire) Changed?.Invoke();
    }

    private static string MakeTitle(string text)
    {
        string single = Regex.Replace(text.Trim(), @"\s+", " ");
        return single.Length <= 60 ? single : single[..57] + "…";
    }

    private ConversationInfo EnsureConversationLocked(string sessionId)
    {
        var conversation = state.Conversations.FirstOrDefault(x => x.Id == sessionId);
        if (conversation != null) return conversation;
        conversation = new() { Id = sessionId, Title = "Nowa rozmowa", CreatedAt = DateTime.Now, LastActiveAt = DateTime.Now, ProjectId = ActiveProject ?? "" };
        state.Conversations.Add(conversation);
        if (state.Conversations.Count > MaxConversations)
            foreach (var old in state.Conversations.Where(x => x.Id != state.ActiveSessionId).OrderBy(x => x.LastActiveAt).Take(state.Conversations.Count - MaxConversations).ToArray())
                state.Conversations.Remove(old);
        return conversation;
    }

    private void AddEntryLocked(string role, string text, string source)
    {
        state.Entries.Add(new() { Timestamp = DateTime.Now, Role = role, Text = text.Trim()[..Math.Min(text.Trim().Length, 16000)], Source = source, SessionId = state.ActiveSessionId, Id = Guid.NewGuid().ToString("N")[..12] });
        if (state.Entries.Count > MaxEntries) state.Entries.RemoveRange(0, state.Entries.Count - MaxEntries);
    }

    // ------------------------------ explicit memories ------------------------------
    public NoteAddResult AddNote(string text) => AddNote(text, null, "user");

    public NoteAddResult AddNote(string text, string? category, string source)
    {
        bool fire = false;
        NoteAddResult result;
        lock (syncRoot)
        {
            var privacy = Privacy;
            if (!privacy.SaveMemories) { LastStorageError = "Zapisywanie wspomnień jest wyłączone (Ustawienia → Pamięć i prywatność)."; return NoteAddResult.Disabled; }
            text = (text ?? "").Trim();
            if (SensitiveDataRedactor.ContainsLikelySecret(text)) { LastStorageError = "Wspomnienie wygląda na zawierające hasło, token lub kod; sekret nie został zapisany."; return NoteAddResult.Invalid; }
            if (text.Length == 0 || text.Length > 4000) { LastStorageError = "Wspomnienie może mieć najwyżej 4000 znaków."; return NoteAddResult.Invalid; }
            if (TryParseStableFact(text, out string factKey, out string factText))
            {
                result = UpsertStableFactLocked(factKey, factText, source);
                if (result is NoteAddResult.Added or NoteAddResult.Updated or NoteAddResult.StaleDuplicate)
                {
                    SaveLocked(); fire = true;
                }
            }
            else
            {
                string key = Normalize(text);
                var existing = state.Notes.FirstOrDefault(n => Normalize(n.Text) == key);
                if (existing != null)
                {
                    if (existing.SupersededAt == null)
                    {
                        if (!IsUserConfirmationSource(source)) return NoteAddResult.Duplicate;
                        existing.Source = source; existing.UpdatedAt = DateTime.Now; existing.LastConfirmedAt = existing.UpdatedAt;
                        existing.Confidence = Math.Max(existing.Confidence, ConfidenceForSource(source, existing.Category));
                        RecordChangeLocked(existing, "potwierdzone ponownie", existing.Text, existing.Text);
                        SaveLocked(); fire = true; result = NoteAddResult.Updated;
                    }
                    else
                    {
                        // A re-assertion of a stale note revives it instead of creating a duplicate.
                        existing.SupersededAt = null; existing.UpdatedAt = DateTime.Now;
                        if (IsUserConfirmationSource(source)) existing.LastConfirmedAt = existing.UpdatedAt;
                        existing.Confidence = ConfidenceForSource(source, existing.Category);
                        RecordChangeLocked(existing, "reaktywowane", existing.Text, existing.Text);
                        SaveLocked(); fire = true; result = NoteAddResult.StaleDuplicate;
                    }
                }
                else if (state.Notes.Count >= MaxNotes) { LastStorageError = "Osiągnięto limit 500 wspomnień. Usuń niepotrzebne wspomnienia."; return NoteAddResult.Limit; }
                else
                {
                    var now = DateTime.Now;
                    string validatedCategory = ValidateCategory(category ?? InferCategory(text));
                    var note = new ConversationMemoryEntry { Timestamp = now, Role = "note", Text = text, Source = source, Id = Guid.NewGuid().ToString("N")[..12], Category = validatedCategory, ProjectId = ActiveProject ?? "", Confidence = ConfidenceForSource(source, validatedCategory), LastConfirmedAt = IsUserConfirmationSource(source) ? now : null };
                    state.Notes.Add(note);
                    RecordChangeLocked(note, "utworzone", "", note.Text);
                    ExtractProfile(text);
                    SaveLocked();
                    fire = true; result = NoteAddResult.Added;
                }
            }
        }
        if (fire) Changed?.Invoke();
        return result;
    }

    public IReadOnlyList<ConversationMemoryEntry> GetNotes()
    {
        lock (syncRoot) return state.Notes.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.UpdatedAt ?? x.Timestamp).Select(Clone).ToArray();
    }

    public ConversationMemoryEntry? FindNote(string id)
    {
        lock (syncRoot) return state.Notes.Where(x => x.Id == id).Select(Clone).FirstOrDefault();
    }

    public bool UpdateNote(string id, string newText) => UpdateNote(id, newText, "user");

    /// <summary>Updates a note while retaining confidence appropriate to its source; user edits confirm, generated refreshes do not.</summary>
    public bool UpdateNote(string id, string newText, string source)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var note = state.Notes.FirstOrDefault(x => x.Id == id);
            newText = (newText ?? "").Trim();
            if (note == null || newText.Length == 0 || newText.Length > 4000 || SensitiveDataRedactor.ContainsLikelySecret(newText)) return false;
            var duplicate = state.Notes.FirstOrDefault(x => x.Id != id && Normalize(x.Text) == Normalize(newText));
            if (duplicate != null) { LastStorageError = "Inne wspomnienie ma już identyczną treść. Edycję przerwano."; return false; }
            string old = note.Text;
            string stableFactKey = "";
            if (TryParseStableFact(newText, out string factKey, out string canonicalText))
            {
                newText = canonicalText; note.FactKey = factKey; stableFactKey = factKey; note.Category = "fakt";
            }
            else note.FactKey = "";
            note.Text = newText; note.UpdatedAt = DateTime.Now; note.Source = source;
            if (IsUserConfirmationSource(source)) { note.LastConfirmedAt = note.UpdatedAt; note.Confidence = 0.95; }
            else note.Confidence = ConfidenceForSource(source, note.Category);
            if (note.Category.Length == 0) note.Category = "notatka";
            RecordChangeLocked(note, "edytowane", old, newText);
            if (stableFactKey.Length > 0)
                foreach (var other in state.Notes.Where(x => x.Id != id && x.SupersededAt == null &&
                    (x.FactKey == stableFactKey || (x.FactKey.Length == 0 && TryParseStableFact(x.Text, out string otherKey, out _) && otherKey == stableFactKey))))
                {
                    other.SupersededAt = DateTime.Now; other.UpdatedAt = DateTime.Now;
                    RecordChangeLocked(other, "zastąpione bieżącą wartością", other.Text, newText);
                }
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public bool DeleteNote(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var note = state.Notes.FirstOrDefault(x => x.Id == id);
            if (note == null) return false;
            // Delete by stable identity only: similar texts elsewhere must survive.
            state.Notes.Remove(note);
            RecordChangeLocked(note, "usunięte", note.Text, "");
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public bool SetPinned(string id, bool pinned)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var note = state.Notes.FirstOrDefault(x => x.Id == id);
            if (note == null || note.Pinned == pinned) return false;
            note.Pinned = pinned; note.UpdatedAt = DateTime.Now;
            RecordChangeLocked(note, pinned ? "przypięte" : "odpięte", note.Text, note.Text);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public bool SetStale(string id, bool stale)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var note = state.Notes.FirstOrDefault(x => x.Id == id);
            if (note == null || (note.SupersededAt != null) == stale) return false;
            note.SupersededAt = stale ? DateTime.Now : null; note.UpdatedAt = DateTime.Now;
            if (stale) note.Confidence = Math.Min(note.Confidence, 0.25);
            else note.Confidence = Math.Min(note.Confidence, 0.55); // Restoring visibility is not a confirmation.
            RecordChangeLocked(note, stale ? "oznaczone nieaktualne" : "reaktywowane", note.Text, note.Text);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<ConversationMemoryEntry> SearchNotes(string query)
    {
        string key = Normalize(query);
        if (key.Length == 0) return GetNotes();
        string[] tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        lock (syncRoot)
            return state.Notes
                .Where(x => tokens.All(t => Normalize(x.Text).Contains(t, StringComparison.Ordinal)))
                .OrderByDescending(x => x.Pinned).ThenByDescending(x => x.UpdatedAt ?? x.Timestamp)
                .Take(50).Select(Clone).ToArray();
    }

    /// <summary>Notes that likely describe the same fact, to warn before a duplicate is stored.</summary>
    public IReadOnlyList<ConversationMemoryEntry> FindSimilarNotes(string text)
    {
        string key = Normalize(text);
        if (key.Length < 3) return [];
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var result = new List<ConversationMemoryEntry>();
        lock (syncRoot)
        {
            foreach (var note in state.Notes)
            {
                string other = Normalize(note.Text);
                if (other == key) continue;
                if (other.Contains(key, StringComparison.Ordinal) || key.Contains(other, StringComparison.Ordinal)) { result.Add(Clone(note)); continue; }
                var otherTokens = other.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                int shared = tokens.Intersect(otherTokens).Count();
                int smaller = Math.Max(1, Math.Min(tokens.Count, otherTokens.Count));
                if (tokens.Count >= 3 && (double)shared / smaller >= 0.6) result.Add(Clone(note));
                if (result.Count >= 5) break;
            }
        }
        return result;
    }

    /// <summary>Heuristic conflict hint: same "klucz:" prefix with a different value. Shown as a question, never auto-resolved.</summary>
    public IReadOnlyList<(ConversationMemoryEntry A, ConversationMemoryEntry B)> FindConflicts()
    {
        var pairs = new List<(ConversationMemoryEntry, ConversationMemoryEntry)>();
        lock (syncRoot)
        {
            var active = state.Notes.Where(x => x.SupersededAt == null).ToArray();
            for (int i = 0; i < active.Length && pairs.Count < 20; i++)
                for (int j = i + 1; j < active.Length && pairs.Count < 20; j++)
                {
                    var (ka, va) = SplitKey(active[i].Text);
                    var (kb, vb) = SplitKey(active[j].Text);
                    if (ka.Length >= 3 && ka == kb && Normalize(va) != Normalize(vb)) pairs.Add((Clone(active[i]), Clone(active[j])));
                }
        }
        return pairs;
    }

    private static (string Key, string Value) SplitKey(string text)
    {
        int index = text.IndexOf(':');
        if (index <= 0) return ("", text);
        return (Normalize(text[..index]), text[(index + 1)..].Trim());
    }

    private static string ValidateCategory(string? category) => Categories.Contains(category) ? category! : "notatka";

    private void ExtractProfile(string text)
    {
        Match name = Regex.Match(text.Trim(), @"^(?:zapami[eę]taj[, :]+)?(?:mam na imi[eę]|nazywam si[eę]|m[oó]w do mnie|zwracaj si[eę] do mnie)\s+([\p{L}][\p{L}\-' ]{0,60})[.!]?$", RegexOptions.IgnoreCase);
        if (name.Success && name.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            SetProfileValueLocked("name", name.Groups[1].Value.Trim(), "explicit user statement", 0.95, DateTime.Now);
        string normalized = Normalize(text);
        if (Regex.IsMatch(normalized, @"^(?:zapamietaj[, :]+)?(?:wole|preferuje) (?:krotkie|zwiezle) odpowiedzi[.!]?$"))
            SetProfileValueLocked("responseStyle", "krótkie odpowiedzi", "explicit user preference", 0.95, DateTime.Now);
        if (Regex.IsMatch(normalized, @"^(?:zapamietaj[, :]+)?(?:wole|preferuje) (?:dlugie|dokladne|szczegolowe) odpowiedzi[.!]?$"))
            SetProfileValueLocked("responseStyle", "szczegółowe odpowiedzi", "explicit user preference", 0.95, DateTime.Now);
    }

    private void SetProfileValueLocked(string key, string value, string source, double confidence, DateTime confirmedAt)
    {
        state.Profile[key] = value;
        if (!state.ProfileMetadata.TryGetValue(key, out var metadata))
            metadata = new(source, confirmedAt, confirmedAt, confidence);
        else
            metadata = new(source, metadata.CreatedAt, confirmedAt, Math.Max(metadata.Confidence, confidence));
        state.ProfileMetadata[key] = metadata;
    }

    private static bool LooksLikeExplicitMemoryCommand(string text)
    {
        string normalized = Normalize(text);
        return Regex.IsMatch(normalized, @"^(?:zapamietaj|notatka|remember|save this)\b");
    }

    /// <summary>Recognizes only unambiguous, affirmative RAM declarations; questions and tentative language do not qualify.</summary>
    private static bool TryParseStableFact(string text, out string key, out string canonicalText)
    {
        key = ""; canonicalText = "";
        string normalized = Normalize(text);
        if (normalized.Length == 0 || normalized.Contains('?') ||
            Regex.IsMatch(normalized, @"\b(?:czy|ile|how|what|do i|maybe|perhaps|possibly|might|i think|i believe|chyba|moze|mysle ze|wydaje mi sie|prawdopodobnie|raczej|nie jestem pewien|not sure)\b") ||
            !Regex.IsMatch(normalized, @"\b(?:ram|pamieci operacyjnej|memory)\b")) return false;
        Match match = Regex.Match(normalized,
            @"\b(?:zmienilem|rozbudowalem|zwiekszylem|mam teraz|changed|upgraded)\s+(?:ram|pamiec ram)\s+(?:na|to|is|to )?\s*(?<gb>\d{1,4})\s*(?:gb|gib)\b");
        if (!match.Success)
            match = Regex.Match(normalized,
                @"\b(?:mam(?: teraz)?|moj komputer ma|komputer ma|my (?:pc|computer) has|i have)\s+(?:(?:ram|pamiec ram)\s+(?:na|is|to)?\s*)?(?<gb>\d{1,4})\s*(?:gb|gib)\s*(?:ram|pamieci(?: operacyjnej)?(?: ram)?|memory)\b");
        if (!match.Success)
            match = Regex.Match(normalized, @"\b(?:ram|pamiec ram)\s*(?::|=|to|is)?\s*(?<gb>\d{1,4})\s*(?:gb|gib)\b");
        if (!match.Success || !int.TryParse(match.Groups["gb"].Value, out int gb) || gb is < 1 or > 1024) return false;
        key = "hardware.ram";
        canonicalText = $"RAM: {gb} GB";
        return true;
    }

    private static bool IsUserConfirmationSource(string source) =>
        source.Equals("user", StringComparison.OrdinalIgnoreCase) || source.Equals("automatic", StringComparison.OrdinalIgnoreCase);

    private static double ConfidenceForSource(string source, string category)
    {
        if (source.Equals("user", StringComparison.OrdinalIgnoreCase)) return 0.95;
        if (source.Equals("automatic", StringComparison.OrdinalIgnoreCase)) return 0.85;
        if (source.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)) return 0.55;
        if (source.StartsWith("import:", StringComparison.OrdinalIgnoreCase)) return 0.50;
        return category == "preferencja" ? 0.75 : 0.65;
    }

    private static double EffectiveConfidence(ConversationMemoryEntry note)
    {
        double confidence = Math.Clamp(double.IsFinite(note.Confidence) ? note.Confidence : 0.5, 0, 1);
        DateTime anchor = note.LastConfirmedAt ?? note.Timestamp;
        double ageDays = Math.Max(0, (DateTime.Now - anchor).TotalDays);
        bool changesOverTime = note.Category == "umiejętność" || note.FactKey.StartsWith("hardware.", StringComparison.OrdinalIgnoreCase);
        if (changesOverTime && ageDays > 30)
            confidence *= Math.Max(0.35, 1 - (ageDays - 30) / 365.0);
        return confidence;
    }

    /// <summary>Upserts the single current value for a stable fact. Historical values stay in the change log, not as competing current notes.</summary>
    private NoteAddResult UpsertStableFactLocked(string factKey, string factText, string source)
    {
        var matches = state.Notes.Where(n => n.FactKey == factKey ||
            (n.FactKey.Length == 0 && TryParseStableFact(n.Text, out string oldKey, out _) && oldKey == factKey))
            .OrderByDescending(n => n.SupersededAt == null)
            .ThenByDescending(n => n.UpdatedAt ?? n.Timestamp).ToArray();
        var existing = matches.FirstOrDefault();
        if (existing != null)
        {
            string oldText = existing.Text;
            bool wasStale = existing.SupersededAt != null;
            bool changed = Normalize(oldText) != Normalize(factText) || wasStale;
            bool metadataUpdated = existing.FactKey != factKey || existing.Category != "fakt";
            bool revalidated = IsUserConfirmationSource(source);
            if (changed || metadataUpdated || revalidated)
            {
                existing.Text = factText; existing.FactKey = factKey; existing.Category = "fakt";
                if (changed) { existing.Source = source; existing.SupersededAt = null; existing.UpdatedAt = DateTime.Now; }
                if (revalidated) { existing.Source = source; existing.LastConfirmedAt = DateTime.Now; existing.Confidence = ConfidenceForSource(source, "fakt"); }
                RecordChangeLocked(existing, changed ? "zaktualizowane" : revalidated ? "potwierdzone ponownie" : "uporządkowane jako fakt", oldText, factText);
            }
            bool retiredConflict = false;
            foreach (var duplicate in matches.Skip(1).Where(n => n.SupersededAt == null))
            {
                duplicate.SupersededAt = DateTime.Now; duplicate.UpdatedAt = DateTime.Now;
                RecordChangeLocked(duplicate, "zastąpione przez bieżącą wartość", duplicate.Text, factText);
                retiredConflict = true;
            }
            if (!changed && !retiredConflict && !metadataUpdated && !revalidated) return NoteAddResult.Duplicate;
            if (!changed && retiredConflict)
                RecordChangeLocked(existing, "duplikaty oznaczone jako nieaktualne", existing.Text, existing.Text);
            return wasStale && Normalize(oldText) == Normalize(factText) ? NoteAddResult.StaleDuplicate : NoteAddResult.Updated;
        }
        if (state.Notes.Count >= MaxNotes)
        {
            LastStorageError = "Osiągnięto limit 500 wspomnień. Usuń niepotrzebne wspomnienia.";
            return NoteAddResult.Limit;
        }
        var now = DateTime.Now;
        var note = new ConversationMemoryEntry { Timestamp = now, Role = "note", Text = factText,
            Source = source, Id = Guid.NewGuid().ToString("N")[..12], Category = "fakt", ProjectId = ActiveProject ?? "", FactKey = factKey,
            Confidence = ConfidenceForSource(source, "fakt"), LastConfirmedAt = IsUserConfirmationSource(source) ? now : null };
        state.Notes.Add(note);
        RecordChangeLocked(note, "utworzone", "", factText);
        return NoteAddResult.Added;
    }

    private static string InferCategory(string text)
    {
        string key = Normalize(text);
        if (key.StartsWith("wole ", StringComparison.Ordinal) || key.StartsWith("preferuje", StringComparison.Ordinal)) return "preferencja";
        if (key.StartsWith("decyzja:", StringComparison.Ordinal)) return "decyzja";
        if (Regex.IsMatch(key, @"^(?:zadanie|do zrobienia|trzeba)\b")) return "zadanie";
        return "notatka";
    }

    private void RecordChangeLocked(ConversationMemoryEntry note, string kind, string oldText, string newText)
    {
        state.Changes.Add(new() { NoteId = note.Id, Kind = kind, OldText = Truncate(oldText), NewText = Truncate(newText), Timestamp = DateTime.Now });
        if (state.Changes.Count > MaxChanges) state.Changes.RemoveRange(0, state.Changes.Count - MaxChanges);
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..197] + "…";

    public IReadOnlyList<MemoryChange> GetChanges()
    {
        lock (syncRoot) return state.Changes.OrderByDescending(x => x.Timestamp).Take(50).ToArray();
    }

    // ------------------------------ conversations ------------------------------
    public IReadOnlyList<ConversationInfo> GetConversations()
    {
        lock (syncRoot) return state.Conversations.OrderByDescending(x => x.LastActiveAt).ToArray();
    }

    public string ActiveConversationTitle
    {
        get { lock (syncRoot) return state.Conversations.FirstOrDefault(x => x.Id == state.ActiveSessionId)?.Title ?? "Nowa rozmowa"; }
    }

    public bool ResumeSession(string sessionId) => ResumeSession(sessionId, out _);

    public bool ResumeSession(string sessionId, out string reason)
    {
        reason = "";
        lock (syncRoot)
        {
            var conversation = state.Conversations.FirstOrDefault(x => x.Id == sessionId);
            if (conversation == null || state.ActiveSessionId == sessionId) { reason = conversation == null ? "Rozmowa nie istnieje." : "Ta rozmowa jest już aktywna."; return false; }
            string? project = ActiveProject;
            if (!string.IsNullOrEmpty(project) && conversation.ProjectId != project)
            { reason = conversation.ProjectId.Length == 0
                    ? "Rozmowa jest globalna, a aktywny jest projekt — konteksty projektów nie mieszają się. Wyłącz projekt albo rozpocznij rozmowę w projekcie."
                    : "Rozmowa należy do innego projektu. Aktywuj tamten projekt albo odłącz rozmowę od niego — konteksty projektów nie mieszają się."; return false; }
            state.ActiveSessionId = sessionId;
            conversation.LastActiveAt = DateTime.Now;
            state.Draft = "";
            SaveLocked();
        }
        SessionChanged?.Invoke();
        Changed?.Invoke();
        return true;
    }

    public bool RenameConversation(string sessionId, string title)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var conversation = state.Conversations.FirstOrDefault(x => x.Id == sessionId);
            title = (title ?? "").Trim();
            if (conversation == null || title.Length == 0 || title.Length > 120) return false;
            conversation.Title = title;
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    // ------------------------------ context for the model ------------------------------
    public IReadOnlyList<ConversationMemoryEntry> GetRecentEntries(int maxEntries = 100)
    {
        lock (syncRoot) return state.Entries.Where(x => x.SessionId == state.ActiveSessionId).TakeLast(Math.Clamp(maxEntries, 0, MaxEntries)).Select(Clone).ToArray();
    }

    public IReadOnlyList<ConversationMemoryEntry> GetAllEntries()
    {
        lock (syncRoot) return state.Entries.Select(Clone).ToArray();
    }

    /// <summary>Removes conversation turns older than the cutoff. Explicit memories, the profile and the
    /// change log are never touched — this is the archive path, not a memory wipe.</summary>
    public int PruneEntriesBefore(DateTime cutoff)
    {
        int removed;
        lock (syncRoot)
        {
            removed = state.Entries.RemoveAll(x => x.Timestamp < cutoff);
            if (removed > 0) SaveLocked();
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    /// <summary>Searches only the ACTIVE conversation; normalized text match, newest first, honest about what was not found.</summary>
    public IReadOnlyList<ConversationMemoryEntry> SearchConversation(string query, int maxResults = 15)
    {
        string needle = Normalize(query ?? "");
        if (needle.Length == 0) return [];
        lock (syncRoot) return state.Entries
            .Where(x => x.SessionId == state.ActiveSessionId && Normalize(x.Text).Contains(needle, StringComparison.Ordinal))
            .OrderByDescending(x => x.Timestamp)
            .Take(Math.Clamp(maxResults, 1, 50))
            .Select(Clone).ToArray();
    }

    /// <summary>Writes the active conversation to a local Markdown file and proves it with a read-back hash.</summary>
    public ConversationExport ExportConversationMarkdown(string? directory = null)
    {
        if (PrivateMode) return new(null, "", 0, ExternalNetworkAllowed
            ? "Tryb prywatny nie utrwala lokalnej historii rozmowy, więc nie ma czego wyeksportować. Funkcje online mogą jednak przekazywać treść usługom zewnętrznym, które mogą ją logować."
            : "Tryb prywatny nie utrwala lokalnej historii rozmowy, więc nie ma czego wyeksportować. Tryb tylko lokalnie blokuje zewnętrzny ruch Sentinel.");
        string title;
        string markdown;
        ConversationMemoryEntry[] turns;
        lock (syncRoot)
        {
            turns = state.Entries.Where(x => x.SessionId == state.ActiveSessionId).OrderBy(x => x.Timestamp).Select(Clone).ToArray();
            title = state.Conversations.FirstOrDefault(x => x.Id == state.ActiveSessionId)?.Title ?? "Nowa rozmowa";
        }
        if (turns.Length == 0)
            return new(null, "", 0, "Ta rozmowa nie ma zapisanych wypowiedzi (zapis rozmów może być wyłączony w ustawieniach prywatności albo to nowa sesja).");
        var builder = new StringBuilder();
        builder.AppendLine("# Rozmowa: " + title);
        builder.AppendLine();
        builder.AppendLine($"- Wyeksportowano: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (czas lokalny)");
        builder.AppendLine($"- Wypowiedzi: {turns.Length}");
        builder.AppendLine("- Eksport lokalny. Plik zawiera treść rozmowy — traktuj go jak dane prywatne.");
        builder.AppendLine();
        foreach (var turn in turns)
        {
            string who = turn.Role == "user" ? "Ja" : "Sentinel X";
            builder.AppendLine("**" + who + "** · " + turn.Timestamp.ToString("dd.MM.yyyy HH:mm") + " · " + turn.Source);
            builder.AppendLine();
            builder.AppendLine(turn.Text);
            builder.AppendLine();
        }
        markdown = builder.ToString();
        try
        {
            string target = Path.Combine(directory ?? Path.Combine(AppPaths.Root, "Reports"));
            Directory.CreateDirectory(target);
            string path = Path.Combine(target, $"rozmowa-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.md");
            File.WriteAllText(path, markdown, Encoding.UTF8);
            string readBack = File.ReadAllText(path, Encoding.UTF8);
            if (readBack != markdown) { try { File.Delete(path); } catch { } return new(null, "", turns.Length, "Zapisany plik nie zgadza się z odczytem kontrolnym — eksport odrzucony."); }
            return new(path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(readBack))), turns.Length, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(null, "", turns.Length, "Nie udało się zapisać eksportu: " + ex.Message); }
    }

    public string GetRecentContext(int maxEntries = 24) => BuildAiContext("", includeRecent: true, maxEntries);
    public string GetStableContext() => BuildAiContext("", includeRecent: false, 0);

    /// <summary>Builds bounded short-term context plus only durable memories relevant to the current request.</summary>
    public string GetRelevantContext(string query, bool includeRecent = true, int maxEntries = 6)
        => BuildAiContext(query ?? "", includeRecent, maxEntries);

    /// <summary>Assembles the model context per the privacy toggles and records an explainable trace (when enabled).</summary>
    private string BuildAiContext(string query, bool includeRecent, int maxEntries)
    {
        var privacy = Privacy;
        if (PrivateMode)
        {
            LastContextTrace = []; LastContextBuiltAt = null;
            if (!privacy.UseMemoriesForAi) return "";
            try { return EphemeralSkillContextProvider?.Invoke(query) ?? ""; }
            catch (Exception ex) { AppLog.Write(ex); return ""; }
        }
        var trace = new List<ContextSlice>();
        var builder = new StringBuilder();
        if (privacy.UseMemoriesForAi)
        {
            ConversationMemoryEntry[] selected;
            Dictionary<string, string> profile;
            Dictionary<string, MemoryProfileMetadata> profileMetadata;
            Dictionary<string, int> relevanceScores = new(StringComparer.Ordinal);
            string? project = ActiveProject;
            lock (syncRoot)
            {
                profile = new Dictionary<string, string>(state.Profile, StringComparer.OrdinalIgnoreCase);
                profileMetadata = new Dictionary<string, MemoryProfileMetadata>(state.ProfileMetadata, StringComparer.OrdinalIgnoreCase);
                // Project-scoped memories remain isolated. Relevance is query-specific; confidence only moderates ties.
                var active = state.Notes.Where(x => x.SupersededAt == null && (project == null || project.Length == 0 || x.ProjectId.Length == 0 || x.ProjectId == project)).ToArray();
                if (string.IsNullOrWhiteSpace(query))
                    selected = active.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.UpdatedAt ?? x.Timestamp).Take(3).Select(Clone).ToArray();
                else
                {
                    var ranked = active.Select(x => (Note: x, Score: ScoreMemoryRelevance(query, x.Text, x.Category)))
                        .Where(x => x.Score > 0)
                        .OrderByDescending(x => x.Score * (0.5 + 0.5 * EffectiveConfidence(x.Note)))
                        .ThenByDescending(x => x.Note.Pinned)
                        .ThenByDescending(x => x.Note.UpdatedAt ?? x.Note.Timestamp).Take(5).ToArray();
                    selected = ranked.Select(x => Clone(x.Note)).ToArray();
                    relevanceScores = ranked.ToDictionary(x => x.Note.Id, x => x.Score, StringComparer.Ordinal);
                }
            }
            bool asksName = IsNameQuery(query);
            var selectedProfile = profile.Where(x => x.Key.Equals("responseStyle", StringComparison.OrdinalIgnoreCase) ||
                asksName && x.Key.Equals("name", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (selectedProfile.Length > 0)
            {
                builder.AppendLine("Zapisany profil użytkownika (jawnie podane preferencje, nie niezmienny fakt):");
                foreach (var item in selectedProfile)
                {
                    if (!profileMetadata.TryGetValue(item.Key, out var metadata))
                        builder.AppendLine($"- {item.Key}: {item.Value} [źródło: legacy; confidence: 55%]");
                    else
                        builder.AppendLine($"- {item.Key}: {item.Value} [źródło: {metadata.Source}; utworzone: {metadata.CreatedAt:yyyy-MM-dd}; ostatnie potwierdzenie: {metadata.LastConfirmedAt?.ToString("yyyy-MM-dd HH:mm") ?? "brak"}; confidence: {metadata.Confidence:P0}]");
                }
                trace.Add(new("profil", "profile", "Profil użytkownika", "preferencja stylu odpowiedzi" + (asksName ? " lub pytanie o imię" : "")));
            }
            if (selected.Length > 0) builder.AppendLine("Trwałe wspomnienia dopasowane do bieżącego zapytania (lokalne deklaracje/preferencje, nie bieżący pomiar; confidence opisuje źródło, nie gwarancję prawdy):");
            int stableBudget = Math.Max(0, StableMemoryBudget - builder.Length);
            foreach (var note in selected)
            {
                if (stableBudget <= 0) break;
                double confidence = EffectiveConfidence(note);
                string source = note.Source.Length > 80 ? note.Source[..77] + "…" : note.Source;
                string ageWarning = confidence < 0.60 ? " · może być nieaktualne — zweryfikuj" : "";
                string confirmed = note.LastConfirmedAt?.ToString("yyyy-MM-dd HH:mm") ?? "brak";
                string line = $"Wspomnienie [źródło: {source}; utworzone: {note.Timestamp:yyyy-MM-dd}; ostatnie potwierdzenie: {confirmed}; confidence: {confidence:P0}; trafność zapytania: {relevanceScores.GetValueOrDefault(note.Id)}{ageWarning}]: {note.Text}";
                if (line.Length > stableBudget)
                {
                    if (stableBudget < 16) break;
                    line = line[..(stableBudget - 12)] + " [skrócono]";
                }
                builder.AppendLine(line);
                stableBudget -= line.Length;
                string reason = string.IsNullOrWhiteSpace(query) ? "krótki zestaw domyślny (maks. 3), bez pełnej bazy" :
                    $"dopasowanie do bieżącego zapytania · trafność {relevanceScores.GetValueOrDefault(note.Id)}" + (note.Pinned ? "; przypięte" : "");
                trace.Add(new("wspomnienie", note.Id, Short(note.Text), reason));
            }
        }
        if (includeRecent && privacy.UseHistoryForAi && privacy.SaveConversations && ConversationMatchesActiveProject())
        {
            ConversationMemoryEntry[] entries;
            lock (syncRoot) entries = state.Entries.Where(x => x.SessionId == state.ActiveSessionId).TakeLast(Math.Clamp(maxEntries, 0, 120)).Select(Clone).ToArray();
            if (entries.Length > 0) builder.AppendLine("Ostatnie wiadomości bieżącej rozmowy:");
            // Keep the newest turns inside the context budget, not the oldest turns of a long conversation.
            var lines = new List<string>();
            int budget = ContextBudget - builder.Length;
            foreach (var entry in entries.Reverse())
            {
                string line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {(entry.Role == "user" ? "Użytkownik" : "Sentinel")}: {entry.Text}";
                if (line.Length > budget) line = line[..Math.Max(0, budget)] + " [skrócono]";
                lines.Add(line);
                budget -= line.Length;
                if (budget <= 0) break;
            }
            foreach (string line in Enumerable.Reverse(lines)) builder.AppendLine(line);
            if (entries.Length > 0) trace.Add(new("rozmowa", state.ActiveSessionId, $"Rozmowa: {ActiveConversationTitle}", $"dopowiedzenie: {Math.Min(entries.Length, lines.Count)} ostatnich wypowiedzi z aktywnej rozmowy"));
        }
        if (privacy.UseMemoriesForAi && EphemeralSkillContextProvider != null)
        {
            try
            {
                string learned = EphemeralSkillContextProvider(query);
                int remaining = Math.Max(0, ContextBudget - builder.Length);
                if (learned.Length > remaining) learned = remaining > 24 ? learned[..(remaining - 14)] + " [skrócono]" : "";
                if (learned.Length > 0) builder.AppendLine(learned);
            }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        if (privacy.ContextPreview) { LastContextTrace = trace; LastContextBuiltAt = DateTime.Now; }
        else { LastContextTrace = []; LastContextBuiltAt = null; }
        return builder.ToString().Trim();
    }

    private bool ConversationMatchesActiveProject()
    {
        string? project = ActiveProject;
        if (project == null || project.Length == 0) return true;
        lock (syncRoot)
        {
            var conversation = state.Conversations.FirstOrDefault(x => x.Id == state.ActiveSessionId);
            // An unassigned (global) conversation stays out of an active project's context by design.
            return conversation != null && conversation.ProjectId == project;
        }
    }

    /// <summary>Moves a conversation between projects (or detaches it back to global). Direct, low risk, but auditable via change events.</summary>
    public bool AssignConversationToProject(string sessionId, string projectId)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var conversation = state.Conversations.FirstOrDefault(x => x.Id == sessionId);
            if (conversation == null || conversation.ProjectId == projectId) return false;
            conversation.ProjectId = projectId;
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<ConversationInfo> GetConversationsForProject(string? projectId)
    {
        lock (syncRoot)
            return string.IsNullOrEmpty(projectId)
                ? state.Conversations.OrderByDescending(x => x.LastActiveAt).ToArray()
                : state.Conversations.Where(x => x.ProjectId == projectId).OrderByDescending(x => x.LastActiveAt).ToArray();
    }

    private static string Short(string text) => text.Length <= 48 ? text : text[..45] + "…";

    private static bool IsNameQuery(string query)
    {
        string normalized = Normalize(query);
        return Regex.IsMatch(normalized, @"\b(?:imie|nazywam|name|call me)\b");
    }

    private static int ScoreMemoryRelevance(string query, string text, string category)
    {
        string normalizedQuery = Normalize(query);
        string normalizedText = Normalize(text + " " + category);
        if (normalizedQuery.Length == 0) return 0;
        if (normalizedText.Contains(normalizedQuery, StringComparison.Ordinal)) return 12;
        var tokens = Regex.Split(normalizedQuery, @"[^\p{L}\p{N}]+").Where(x => x.Length >= 3)
            .Where(x => !ContextStopWords.Contains(x)).Take(48).ToHashSet(StringComparer.Ordinal);
        if (Regex.IsMatch(normalizedQuery, @"\b(?:komputer\w*|computer\w*|pc|laptop\w*|sprzet|hardware|specyfikacja|konfiguracja)\b"))
            tokens.UnionWith(["ram", "procesor", "cpu", "windows", "dysk", "hardware", "komputer"]);
        if (tokens.Count == 0) return 0;
        var words = Regex.Split(normalizedText, @"[^\p{L}\p{N}]+").Where(x => x.Length > 0).ToArray();
        int score = 0;
        foreach (string token in tokens)
        {
            bool match = words.Any(word => word == token || (Math.Min(word.Length, token.Length) >= 4 &&
                (word.StartsWith(token, StringComparison.Ordinal) || token.StartsWith(word, StringComparison.Ordinal))));
            if (match) score += 3;
        }
        return score;
    }

    private static readonly HashSet<string> ContextStopWords = new(StringComparer.Ordinal)
    {
        "co", "czy", "jak", "ile", "mam", "moj", "moja", "moje", "moim", "mojego", "pamietasz", "pamietam", "pamiętasz", "about", "what", "with", "the", "for", "and", "you", "your", "that", "this"
    };

    public IReadOnlyList<ConversationMemoryEntry> FindRelevantNotes(string query, int maxResults = 5, bool includeStale = false)
    {
        string? project = ActiveProject;
        lock (syncRoot)
            return state.Notes.Where(x => (includeStale || x.SupersededAt == null) &&
                    (project == null || project.Length == 0 || x.ProjectId.Length == 0 || x.ProjectId == project))
                .Select(x => (Note: x, Score: ScoreMemoryRelevance(query, x.Text, x.Category)))
                .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.Note.Pinned)
                .ThenByDescending(x => x.Note.UpdatedAt ?? x.Note.Timestamp).Take(Math.Clamp(maxResults, 1, 20))
                .Select(x => Clone(x.Note)).ToArray();
    }

    public string TakeAutomaticMemoryNotice()
    {
        lock (syncRoot)
        {
            string notice = LastAutomaticMemoryNotice;
            LastAutomaticMemoryNotice = "";
            if (notice.Length == 0) return "";
            return VerifyPersistedState(out string evidence) ? notice
                : "Rozpoznałem trwałą informację, ale nie mogę potwierdzić jej zapisu na dysku. Nie będę twierdzić, że przetrwa restart. " + evidence;
        }
    }

    // ------------------------------ draft ------------------------------
    public string GetDraft()
    {
        lock (syncRoot) return state.Draft;
    }

    /// <summary>Persists the unsent draft (never in private mode), throttled to keep typing out of the UI way.</summary>
    public void SaveDraft(string text)
    {
        if (PrivateMode) return;
        text = (text ?? "").Trim();
        if (text.Length > 4000) text = text[..4000];
        lock (syncRoot)
        {
            if (state.Draft == text) return;
            state.Draft = text;
            if ((DateTime.UtcNow - lastDraftSaveUtc) < TimeSpan.FromSeconds(2)) return;
            lastDraftSaveUtc = DateTime.UtcNow;
            SaveLocked();
        }
    }

    public void FlushDraft()
    {
        lock (syncRoot) { lastDraftSaveUtc = DateTime.MinValue; SaveLocked(); }
    }

    // ------------------------------ summarizing / lookup ------------------------------
    public string GetNotesSummary()
    {
        lock (syncRoot)
        {
            var lines = state.Profile.Select(x => $"{(x.Key == "name" ? "Imię" : x.Key == "responseStyle" ? "Styl odpowiedzi" : x.Key)}: {x.Value}").ToList();
            lines.AddRange(state.Notes.Select((x, i) => $"{i + 1}. {(x.SupersededAt != null ? "[nieaktualne] " : "")}{(x.Pinned ? "📌 " : "")}{x.Text}{(x.Category.Length > 0 ? $"  · {x.Category}" : "")}"));
            return lines.Count == 0 ? "Nie mam zapisanych wspomnień ani preferencji." : string.Join("\n", lines);
        }
    }

    public int Forget(string text)
    {
        bool fire = false;
        int count;
        lock (syncRoot)
        {
            string key = Normalize(text);
            if (key.Length == 0) return 0;
            // Deleting by text keeps working for the permission-gated command; page deletions use stable IDs.
            var removed = state.Notes.Where(x => Normalize(x.Text).Contains(key, StringComparison.Ordinal)).ToArray();
            foreach (var note in removed) { state.Notes.Remove(note); RecordChangeLocked(note, "usunięte", note.Text, ""); }
            count = removed.Length;
            foreach (string profileKey in state.Profile.Keys.ToArray())
                if (Normalize(state.Profile[profileKey]).Contains(key, StringComparison.Ordinal) || (profileKey == "name" && key is "imie" or "moje imie") || (profileKey == "responseStyle" && key is "preferencje" or "styl odpowiedzi"))
                { state.Profile.Remove(profileKey); state.ProfileMetadata.Remove(profileKey); count++; }
            // Remove matching raw history too, so forgotten facts do not return as conversation context.
            count += state.Entries.RemoveAll(x => Normalize(x.Text).Contains(key, StringComparison.Ordinal));
            SaveLocked();
            fire = count > 0;
        }
        if (fire) Changed?.Invoke();
        return count;
    }

    public void StartNewSession() => StartNewSession(null);

    public void StartNewSession(string? title)
    {
        lock (syncRoot)
        {
            state.ActiveSessionId = Guid.NewGuid().ToString("N");
            var conversation = EnsureConversationLocked(state.ActiveSessionId);
            if (!string.IsNullOrWhiteSpace(title)) conversation.Title = MakeTitle(title);
            state.Draft = "";
            SaveLocked();
        }
        SessionChanged?.Invoke();
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (syncRoot) { state.Entries.Clear(); state.Draft = ""; state.ActiveSessionId = Guid.NewGuid().ToString("N"); EnsureConversationLocked(state.ActiveSessionId); SaveLocked(); }
        SessionChanged?.Invoke();
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        lock (syncRoot) { state = new(); SaveLocked(); }
        SessionChanged?.Invoke();
        Changed?.Invoke();
    }

    internal bool VerifyPersistedState(out string evidence)
    {
        lock (syncRoot)
        {
            try
            {
                if (LastStorageError != null) { evidence = LastStorageError; return false; }
                string expected = JsonSerializer.Serialize(state, jsonOptions);
                if (File.ReadAllText(memoryPath, Encoding.UTF8) != expected)
                { evidence = "Zapis nie odpowiada bieżącemu stanowi pamięci."; return false; }
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(memoryPath)));
                evidence = $"Odczyt zwrotny: {memoryPath}; SHA-256: {hash}; wiadomości: {state.Entries.Count}; wspomnienia: {state.Notes.Count}; profil: {state.Profile.Count}.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { evidence = "Nie można sprawdzić zapisu pamięci: " + ex.Message; return false; }
        }
    }

    public string Export()
    {
        lock (syncRoot)
        {
            string directory = Path.Combine(Path.GetDirectoryName(memoryPath)!, "Exports");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"pamiec-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state, jsonOptions), Encoding.UTF8);
            return path;
        }
    }

    // ------------------------------ import ------------------------------
    public ImportPreview PreviewImport(string path)
    {
        try
        {
            if (!TryReadImport(path, out var notes, out var profile, out string error)) return new(false, error, 0, 0, 0, 0);
            lock (syncRoot)
            {
                int fresh = 0, duplicates = 0, invalid = 0;
                var seen = state.Notes.Select(x => Normalize(x.Text)).ToHashSet();
                var incoming = new HashSet<string>();
                foreach (var note in notes)
                {
                    string key = Normalize(note.Text ?? "");
                    if (key.Length == 0 || (note.Text ?? "").Length > 4000) { invalid++; continue; }
                    if (seen.Contains(key) || !incoming.Add(key)) { duplicates++; continue; }
                    fresh++;
                }
                int keys = profile.Keys.Count(k => !state.Profile.ContainsKey(k));
                return new(true, $"Do dodania: {fresh} wspomnień, {keys} wpisów profilu. Pominięte duplikaty: {duplicates}. Niepoprawne: {invalid}.", fresh, duplicates, invalid, keys);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(false, "Nie można odczytać importu: " + ex.Message, 0, 0, 0, 0); }
    }

    /// <summary>Merges explicit memories and missing profile keys. Conversation history is deliberately not imported.</summary>
    public string ImportMemories(string path)
    {
        int room;
        var preview = PreviewImport(path);
        if (!preview.Valid) return preview.Message;
        if (!TryReadImport(path, out var notes, out var profile, out string error)) return error;
        bool fire = false;
        lock (syncRoot)
        {
            var seen = state.Notes.Select(x => Normalize(x.Text)).ToHashSet();
            var incoming = new HashSet<string>();
            int added = 0;
            foreach (var note in notes)
            {
                string key = Normalize(note.Text ?? "");
                if (key.Length == 0 || (note.Text ?? "").Length > 4000 || seen.Contains(key) || !incoming.Add(key)) continue;
                if (state.Notes.Count >= MaxNotes) break;
                var created = new ConversationMemoryEntry { Timestamp = note.Timestamp is { } t && t.Year > 2000 ? t : DateTime.Now, Role = "note", Text = (note.Text ?? "").Trim(), Source = "import: " + Path.GetFileName(path), Id = Guid.NewGuid().ToString("N")[..12], Category = ValidateCategory(note.Category), FactKey = note.FactKey ?? "", Confidence = double.IsFinite(note.Confidence) ? Math.Clamp(note.Confidence, 0, 1) : 0.5, LastConfirmedAt = note.LastConfirmedAt };
                state.Notes.Add(created);
                RecordChangeLocked(created, "zaimportowane", "", created.Text);
                added++;
            }
            foreach (var item in profile)
                if (!state.Profile.ContainsKey(item.Key))
                {
                    state.Profile[item.Key] = item.Value;
                    state.ProfileMetadata[item.Key] = new("import: " + Path.GetFileName(path), DateTime.Now, null, 0.50);
                }
            SaveLocked();
            room = added;
            fire = added > 0 || profile.Count > 0;
        }
        if (fire) Changed?.Invoke();
        return $"Zaimportowano {room} nowych wspomnień. {preview.Message} Historia rozmów celowo nie jest importowana.";
    }

    private bool TryReadImport(string path, out List<ConversationMemoryEntry> notes, out Dictionary<string, string> profile, out string error)
    {
        notes = []; profile = new(StringComparer.OrdinalIgnoreCase); error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) { error = "Podaj pełną ścieżkę do pliku JSON eksportu."; return false; }
            if (!File.Exists(path)) { error = "Plik nie istnieje: " + path; return false; }
            if (new FileInfo(path).Length > 5 * 1024 * 1024) { error = "Plik importu przekracza limit 5 MB."; return false; }
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = "Oczekiwano obiektu JSON z polami Notes/Profile (format eksportu SentinelX)."; return false; }
            var root = doc.RootElement;
            if (root.TryGetProperty("Notes", out var notesElement) && notesElement.ValueKind == JsonValueKind.Array)
                notes = JsonSerializer.Deserialize<List<ConversationMemoryEntry>>(notesElement.GetRawText(), jsonOptions) ?? [];
            if (root.TryGetProperty("Profile", out var profileElement) && profileElement.ValueKind == JsonValueKind.Object)
                foreach (var property in profileElement.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value && value.Length <= 500)
                        profile[property.Name] = value;
            if (notes.Count == 0 && profile.Count == 0) { error = "Nie znaleziono wspomnień ani profilu w pliku."; return false; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { error = "Nie można odczytać importu: " + ex.Message; return false; }
    }

    // ------------------------------ lookup helpers ------------------------------
    public bool TryFindTextAfter(string marker, string? excludeText, out string result)
    {
        result = "";
        if (string.IsNullOrWhiteSpace(marker)) return false;
        lock (syncRoot)
        {
            foreach (var entry in state.Entries.Concat(state.Notes).OrderByDescending(x => x.Timestamp))
            {
                if (entry.Role is not ("user" or "note") || string.Equals(entry.Text, excludeText, StringComparison.OrdinalIgnoreCase)) continue;
                int index = entry.Text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (result = entry.Text[(index + marker.Length)..].Trim()).Length > 0) return true;
            }
        }
        return false;
    }

    public bool TryGetPreviousUserMessage(string? currentText, out string message)
    {
        lock (syncRoot)
        {
            var candidates = state.Entries.Where(x => x.Role == "user" && x.SessionId == state.ActiveSessionId).Reverse().ToList();
            // Exclude only the current turn, not every identical older message.
            if (candidates.Count > 0 && string.Equals(candidates[0].Text, currentText, StringComparison.OrdinalIgnoreCase)) candidates.RemoveAt(0);
            message = candidates.FirstOrDefault()?.Text ?? "";
            return message.Length > 0;
        }
    }

    public bool TryGetUserMessageFromAgo(TimeSpan ago, string? currentText, out string message)
    {
        message = "";
        if (ago < TimeSpan.Zero || ago > TimeSpan.FromDays(365)) return false;
        lock (syncRoot)
        {
            DateTime target = DateTime.Now - ago;
            var best = state.Entries.Where(x => x.Role == "user" && !string.Equals(x.Text, currentText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Math.Abs((x.Timestamp - target).TotalSeconds)).FirstOrDefault();
            // Do not present a message from hours ago as a match for "five minutes ago".
            if (best == null || (best.Timestamp - target).Duration() > TimeSpan.FromSeconds(Math.Clamp(ago.TotalSeconds * 0.25, 30, 120))) return false;
            message = best.Text;
            return true;
        }
    }

    // ------------------------------ persistence ------------------------------
    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(memoryPath)) return;
                if (new FileInfo(memoryPath).Length > 20 * 1024 * 1024) throw new IOException("Plik pamięci przekracza 20 MB.");
                string json = File.ReadAllText(memoryPath);
                bool needsConfidenceMigration;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    needsConfidenceMigration = true;
                    BackupBeforeMigration("v1");
                    var old = JsonSerializer.Deserialize<List<ConversationMemoryEntry>>(json, jsonOptions) ?? [];
                    foreach (var entry in old.Where(IsValid))
                    {
                        if (entry.Role == "note") { state.Notes.Add(entry); ExtractProfile(entry.Text); }
                        else { entry.SessionId = state.ActiveSessionId; state.Entries.Add(entry); if (entry.Role == "user") ExtractProfile(entry.Text); }
                    }
                    MigrateToV3Locked();
                }
                else
                {
                    int version = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 2;
                    needsConfidenceMigration = version < 4;
                    state = JsonSerializer.Deserialize<ConversationMemoryState>(json, jsonOptions) ?? new();
                    state.Entries = (state.Entries ?? []).Where(IsValid).TakeLast(MaxEntries).ToList();
                    state.Notes = (state.Notes ?? []).Where(IsValid).TakeLast(MaxNotes).ToList();
                    state.Profile ??= new();
                    state.ProfileMetadata ??= new(StringComparer.OrdinalIgnoreCase);
                    state.Conversations ??= [];
                    state.Changes ??= [];
                    state.Draft ??= "";
                    if (version < 4) BackupBeforeMigration("v" + version);
                    if (version < 3) MigrateToV3Locked();
                }
                MigrateToV4Locked(needsConfidenceMigration);
                state.Version = 4;
                if (string.IsNullOrWhiteSpace(state.ActiveSessionId)) state.ActiveSessionId = Guid.NewGuid().ToString("N");
                EnsureConversationLocked(state.ActiveSessionId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                state = new();
                LastStorageError = "Nie udało się wczytać pamięci: " + ex.Message;
                // Preserve the damaged source before a later save can replace it.
                try { if (File.Exists(memoryPath)) File.Copy(memoryPath, memoryPath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", false); }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { LastStorageError += " Nie udało się utworzyć kopii."; }
            }
        }
    }

    private void MigrateToV3Locked()
    {
        foreach (var entry in state.Entries) { if (entry.Id.Length == 0) entry.Id = Guid.NewGuid().ToString("N")[..12]; if (entry.SessionId.Length == 0) entry.SessionId = state.ActiveSessionId; }
        foreach (var note in state.Notes) { if (note.Id.Length == 0) note.Id = Guid.NewGuid().ToString("N")[..12]; if (note.Category.Length == 0) note.Category = "notatka"; }
        // Rebuild the conversation index from the session markers stored on entries.
        foreach (var group in state.Entries.GroupBy(x => x.SessionId))
        {
            if (state.Conversations.Any(x => x.Id == group.Key)) continue;
            state.Conversations.Add(new() { Id = group.Key, Title = "Rozmowa z " + group.Min(x => x.Timestamp).ToString("dd.MM.yyyy"), CreatedAt = group.Min(x => x.Timestamp), LastActiveAt = group.Max(x => x.Timestamp) });
        }
    }

    private void MigrateToV4Locked(bool legacyFormat)
    {
        state.ProfileMetadata ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in state.Profile.Keys)
            if (!state.ProfileMetadata.ContainsKey(key))
                state.ProfileMetadata[key] = new("legacy profile", DateTime.Now, null, 0.55);
        foreach (var pair in state.ProfileMetadata.ToArray())
        {
            var metadata = pair.Value;
            if (metadata == null || !double.IsFinite(metadata.Confidence) || metadata.Confidence < 0 || metadata.Confidence > 1)
                state.ProfileMetadata[pair.Key] = new("legacy profile", DateTime.Now, null, 0.55);
            else if (metadata.LastConfirmedAt is { } confirmed && confirmed > DateTime.Now)
                state.ProfileMetadata[pair.Key] = metadata with { LastConfirmedAt = null };
        }
        foreach (var note in state.Notes)
        {
            if (legacyFormat)
            {
                // Derive conservative defaults for v1–v3 memories, which had no confidence metadata.
                note.Confidence = ConfidenceForSource(note.Source, note.Category);
                note.LastConfirmedAt = IsUserConfirmationSource(note.Source) ? note.UpdatedAt ?? note.Timestamp : null;
            }
            else if (!double.IsFinite(note.Confidence) || note.Confidence < 0 || note.Confidence > 1)
                note.Confidence = 0.5;
            if (note.LastConfirmedAt is { } confirmed && confirmed > DateTime.Now)
                note.LastConfirmedAt = null;
        }
    }

    private void BackupBeforeMigration(string from)
    {
        try
        {
            string backup = memoryPath + $".{from}-backup-{DateTime.Now:yyyyMMddHHmmss}";
            if (!File.Exists(backup)) File.Copy(memoryPath, backup, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastStorageError = "Nie utworzono kopii przed migracją pamięci: " + ex.Message; }
    }

    private void ApplyRetentionLocked(MemoryPrivacy privacy)
    {
        if (privacy.RetentionDays <= 0) return;
        DateTime cutoff = DateTime.Now.AddDays(-privacy.RetentionDays);
        // Retention covers conversation history only. Explicit memories and the profile are the user's deliberate data.
        state.Entries.RemoveAll(x => x.Timestamp < cutoff);
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(memoryPath)!);
            string temporary = memoryPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, jsonOptions), Encoding.UTF8);
            File.Move(temporary, memoryPath, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastStorageError = "Pamięć działa tylko do zamknięcia aplikacji. Błąd zapisu: " + ex.Message; }
    }

    private static bool IsValid(ConversationMemoryEntry? entry) => entry != null && entry.Role is "user" or "assistant" or "note" && !string.IsNullOrWhiteSpace(entry.Text);
    private static ConversationMemoryEntry Clone(ConversationMemoryEntry x) => new() { Timestamp = x.Timestamp, Role = x.Role, Text = x.Text, Source = x.Source, SessionId = x.SessionId, Id = x.Id, Category = x.Category, Pinned = x.Pinned, UpdatedAt = x.UpdatedAt, SupersededAt = x.SupersededAt, LastConfirmedAt = x.LastConfirmedAt, Confidence = x.Confidence, ProjectId = x.ProjectId, FactKey = x.FactKey };
    internal static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (char c in (text ?? "").ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c);
        return Regex.Replace(result.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }
}
