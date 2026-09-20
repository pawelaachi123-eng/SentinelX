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
}

public sealed class ConversationMemoryState
{
    public int Version { get; set; } = 2;
    public string ActiveSessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ConversationMemoryEntry> Entries { get; set; } = [];
    public List<ConversationMemoryEntry> Notes { get; set; } = [];
    public Dictionary<string, string> Profile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Bounded conversation history and separate durable explicit memories, stored only locally.</summary>
public sealed class ConversationMemoryService : Services.Memory.IConversationMemory
{
    private const int MaxEntries = 720;
    private const int MaxNotes = 500;
    private readonly object syncRoot = new();
    private readonly string memoryPath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private ConversationMemoryState state = new();
    public string? LastStorageError { get; private set; }
    public string StoragePath => memoryPath;
    public string ActiveSessionId { get { lock (syncRoot) return state.ActiveSessionId; } }
    public int Count { get { lock (syncRoot) return state.Entries.Count + state.Notes.Count; } }
    public int NoteCount { get { lock (syncRoot) return state.Notes.Count; } }
    public string UserName { get { lock (syncRoot) return state.Profile.GetValueOrDefault("name", ""); } }

    public ConversationMemoryService(string? memoryDirectory = null)
    {
        memoryPath = Path.Combine(memoryDirectory ?? Path.Combine(AppPaths.Root, "Memory"), "conversation-memory.json");
        Load();
    }

    public void AddUserMessage(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (syncRoot)
        {
            ExtractProfile(text);
            AddEntryLocked("user", text, source);
            SaveLocked();
        }
    }

    public void AddAssistantMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (syncRoot) { AddEntryLocked("assistant", text, "SENTINEL"); SaveLocked(); }
    }

    public void AddNote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (syncRoot)
        {
            text = text.Trim();
            if (text.Length > 4000) { LastStorageError = "Wspomnienie może mieć najwyżej 4000 znaków."; return; }
            if (!state.Notes.Any(n => n.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                if (state.Notes.Count >= MaxNotes) { LastStorageError = "Osiągnięto limit 500 wspomnień. Usuń niepotrzebne wspomnienia."; return; }
                state.Notes.Add(new() { Timestamp = DateTime.Now, Role = "note", Text = text, Source = "MEMORY" });
            }
            ExtractProfile(text);
            SaveLocked();
        }
    }

    private void AddEntryLocked(string role, string text, string source)
    {
        state.Entries.Add(new() { Timestamp = DateTime.Now, Role = role, Text = text.Trim()[..Math.Min(text.Trim().Length, 16000)], Source = source, SessionId = state.ActiveSessionId });
        if (state.Entries.Count > MaxEntries) state.Entries.RemoveRange(0, state.Entries.Count - MaxEntries);
    }

    public IReadOnlyList<ConversationMemoryEntry> GetRecentEntries(int maxEntries = 100)
    {
        lock (syncRoot) return state.Entries.Where(x => x.SessionId == state.ActiveSessionId).TakeLast(Math.Clamp(maxEntries, 0, MaxEntries)).Select(Clone).ToArray();
    }

    public IReadOnlyList<ConversationMemoryEntry> GetAllEntries()
    {
        lock (syncRoot) return state.Entries.Select(Clone).ToArray();
    }

    public string GetRecentContext(int maxEntries = 24)
    {
        lock (syncRoot)
        {
            var builder = new StringBuilder("Zapisany profil i wspomnienia:\n");
            foreach (var item in state.Profile) builder.AppendLine($"{item.Key}: {item.Value}");
            foreach (var note in state.Notes.TakeLast(30)) builder.AppendLine("Wspomnienie: " + note.Text);
            builder.AppendLine("Ostatnie wiadomości bieżącej rozmowy:");
            // Keep the newest turns inside the context budget, not the oldest turns of a long conversation.
            var recent = new List<string>();
            int budget = 12000;
            foreach (var entry in state.Entries.Where(x => x.SessionId == state.ActiveSessionId).TakeLast(Math.Clamp(maxEntries, 0, 120)).Reverse())
            {
                string line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {(entry.Role == "user" ? "Użytkownik" : "Sentinel")}: {entry.Text}";
                if (line.Length > budget) line = line[..Math.Max(0, budget)] + " [skrócono]";
                recent.Add(line);
                budget -= line.Length;
                if (budget <= 0) break;
            }
            foreach (string line in Enumerable.Reverse(recent)) builder.AppendLine(line);
            return builder.ToString().Trim();
        }
    }

    public string GetStableContext()
    {
        lock (syncRoot)
        {
            var builder = new StringBuilder("Zapisany profil i trwałe wspomnienia:\n");
            foreach (var item in state.Profile) builder.AppendLine($"{item.Key}: {item.Value}");
            foreach (var note in state.Notes.TakeLast(30)) builder.AppendLine("Wspomnienie: " + note.Text);
            return builder.ToString().Trim();
        }
    }

    public string GetNotesSummary()
    {
        lock (syncRoot)
        {
            var lines = state.Profile.Select(x => $"{(x.Key == "name" ? "Imię" : x.Key == "responseStyle" ? "Styl odpowiedzi" : x.Key)}: {x.Value}").ToList();
            lines.AddRange(state.Notes.Select((x, i) => $"{i + 1}. {x.Text}"));
            return lines.Count == 0 ? "Nie mam zapisanych wspomnień ani preferencji." : string.Join("\n", lines);
        }
    }

    public int Forget(string text)
    {
        lock (syncRoot)
        {
            string key = Normalize(text);
            if (key.Length == 0) return 0;
            int count = state.Notes.RemoveAll(x => Normalize(x.Text).Contains(key, StringComparison.Ordinal));
            foreach (string profileKey in state.Profile.Keys.ToArray())
                if (Normalize(state.Profile[profileKey]).Contains(key, StringComparison.Ordinal) || (profileKey == "name" && key is "imie" or "moje imie") || (profileKey == "responseStyle" && key is "preferencje" or "styl odpowiedzi"))
                { state.Profile.Remove(profileKey); count++; }
            // Remove matching raw history too, so forgotten facts do not return as conversation context.
            count += state.Entries.RemoveAll(x => Normalize(x.Text).Contains(key, StringComparison.Ordinal));
            SaveLocked();
            return count;
        }
    }

    public void StartNewSession()
    {
        lock (syncRoot) { state.ActiveSessionId = Guid.NewGuid().ToString("N"); SaveLocked(); }
    }

    public void Clear()
    {
        lock (syncRoot) { state.Entries.Clear(); state.ActiveSessionId = Guid.NewGuid().ToString("N"); SaveLocked(); }
    }

    public void ClearAll()
    {
        lock (syncRoot) { state = new(); SaveLocked(); }
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

    private void ExtractProfile(string text)
    {
        Match name = Regex.Match(text.Trim(), @"^(?:zapami[eę]taj[, :]+)?(?:mam na imi[eę]|nazywam si[eę]|m[oó]w do mnie|zwracaj si[eę] do mnie)\s+([\p{L}][\p{L}\-' ]{0,60})[.!]?$", RegexOptions.IgnoreCase);
        if (name.Success && name.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            state.Profile["name"] = name.Groups[1].Value.Trim();
        string normalized = Normalize(text);
        if (Regex.IsMatch(normalized, @"^(?:zapamietaj[, :]+)?(?:wole|preferuje) (?:krotkie|zwiezle) odpowiedzi[.!]?$")) state.Profile["responseStyle"] = "krótkie odpowiedzi";
        if (Regex.IsMatch(normalized, @"^(?:zapamietaj[, :]+)?(?:wole|preferuje) (?:dlugie|dokladne|szczegolowe) odpowiedzi[.!]?$")) state.Profile["responseStyle"] = "szczegółowe odpowiedzi";
    }

    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(memoryPath)) return;
                if (new FileInfo(memoryPath).Length > 20 * 1024 * 1024) throw new IOException("Plik pamięci przekracza 20 MB.");
                string json = File.ReadAllText(memoryPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var old = JsonSerializer.Deserialize<List<ConversationMemoryEntry>>(json, jsonOptions) ?? [];
                    foreach (var entry in old.Where(IsValid))
                    {
                        if (entry.Role == "note") { state.Notes.Add(entry); ExtractProfile(entry.Text); }
                        else { entry.SessionId = state.ActiveSessionId; state.Entries.Add(entry); if (entry.Role == "user") ExtractProfile(entry.Text); }
                    }
                }
                else state = JsonSerializer.Deserialize<ConversationMemoryState>(json, jsonOptions) ?? new();
                state.Entries = (state.Entries ?? []).Where(IsValid).TakeLast(MaxEntries).ToList();
                state.Notes = (state.Notes ?? []).Where(IsValid).TakeLast(MaxNotes).ToList();
                state.Profile ??= new();
                if (string.IsNullOrWhiteSpace(state.ActiveSessionId)) state.ActiveSessionId = Guid.NewGuid().ToString("N");
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
    private static ConversationMemoryEntry Clone(ConversationMemoryEntry x) => new() { Timestamp = x.Timestamp, Role = x.Role, Text = x.Text, Source = x.Source, SessionId = x.SessionId };
    internal static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (char c in (text ?? "").ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c);
        return Regex.Replace(result.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }
}
