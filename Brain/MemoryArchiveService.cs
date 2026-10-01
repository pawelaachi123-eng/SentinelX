using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX;

public sealed class ArchiveSummary
{
    public string Month { get; set; } = "";
    public string JsonPath { get; set; } = "";
    public string MarkdownPath { get; set; } = "";
    public int Turns { get; set; }
    public int Conversations { get; set; }
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed record ArchiveResult(bool Success, string Message, ArchiveSummary? Summary);

/// <summary>Monthly conversation archive in its own folder: readable Markdown + JSON with a read-back
/// hash, and pruning of archived turns from the live store. Explicit memories are never archived away.</summary>
public sealed class MemoryArchiveService
{
    private readonly ConversationMemoryService memory;
    private readonly string archiveRoot;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public Func<DateTime> NowProvider { get; set; } = () => DateTime.Now;
    /// <summary>How many recent months stay live. 0 disables automatic archiving.</summary>
    public Func<int> KeepMonthsProvider { get; set; } = () => 0;
    public string? LastError { get; private set; }
    public string ArchiveRoot => archiveRoot;

    public MemoryArchiveService(ConversationMemoryService? memory = null, string? archiveRoot = null)
    {
        this.memory = memory ?? new ConversationMemoryService();
        this.archiveRoot = archiveRoot ?? Path.Combine(AppPaths.MemoryDirectory, "Archives");
    }

    public IReadOnlyList<ArchiveSummary> List()
    {
        var result = new List<ArchiveSummary>();
        if (!Directory.Exists(archiveRoot)) return result;
        foreach (string directory in Directory.EnumerateDirectories(archiveRoot).OrderByDescending(x => x, StringComparer.Ordinal))
        {
            string month = Path.GetFileName(directory);
            string json = Path.Combine(directory, "rozmowy-" + month + ".json");
            string markdown = Path.Combine(directory, "rozmowy-" + month + ".md");
            if (!File.Exists(json)) continue;
            try
            {
                byte[] bytes = File.ReadAllBytes(json);
                var turns = JsonSerializer.Deserialize<List<ConversationMemoryEntry>>(Encoding.UTF8.GetString(bytes), jsonOptions) ?? [];
                result.Add(new ArchiveSummary
                {
                    Month = month, JsonPath = json, MarkdownPath = markdown,
                    Turns = turns.Count, Conversations = turns.Select(x => x.SessionId).Distinct().Count(),
                    Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { result.Add(new ArchiveSummary { Month = month, JsonPath = json, MarkdownPath = markdown, Sha256 = "odczyt nieudany: " + ex.Message }); }
        }
        return result;
    }

    public string Describe()
    {
        var archives = List();
        if (archives.Count == 0)
            return "Brak archiwów. Folder: " + archiveRoot + "\nWpisz „archiwizuj rozmowy”, żeby przenieść tam rozmowy z poprzedniego miesiąca.";
        var lines = archives.Select(x => "· " + x.Month + " · " + x.Conversations + " rozmów · " + x.Turns + " wypowiedzi · " +
            (x.Bytes / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " kB" + (x.Sha256.Length == 64 ? "\n  " + x.JsonPath : "\n  " + x.Sha256));
        return "Archiwa rozmów (folder " + archiveRoot + "):\n" + string.Join("\n", lines) +
            "\nArchiwum to kopia do odczytu — przywracanie do aktywnej rozmowy nie jest obsługiwane.";
    }

    /// <summary>Archives every conversation turn of the given month (default: the previous month) and prunes it from the live store.</summary>
    public ArchiveResult ArchiveMonth(string? month = null)
    {
        DateTime now = NowProvider();
        string target;
        if (string.IsNullOrWhiteSpace(month)) target = new DateTime(now.Year, now.Month, 1).AddMonths(-1).ToString("yyyy-MM");
        else
        {
            if (!DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return new ArchiveResult(false, "Podaj miesiąc w formacie RRRR-MM, np. 2026-08.", null);
            target = parsed.ToString("yyyy-MM");
        }
        if (memory.PrivateMode)
            return new ArchiveResult(false, "Tryb prywatny jest włączony — rozmowa nie jest zapisywana, więc nie ma czego archiwizować.", null);

        DateTime from = DateTime.ParseExact(target, "yyyy-MM", CultureInfo.InvariantCulture);
        DateTime to = from.AddMonths(1);
        var turns = memory.GetAllEntries().Where(x => x.Timestamp >= from && x.Timestamp < to).OrderBy(x => x.Timestamp).ToArray();
        if (turns.Length == 0)
            return new ArchiveResult(true, "Brak rozmów z " + target + " do zarchiwizowania. Nic nie usunięto.", null);

        try
        {
            string directory = Path.Combine(archiveRoot, target);
            Directory.CreateDirectory(directory);
            string jsonPath = Path.Combine(directory, "rozmowy-" + target + ".json");
            string markdownPath = Path.Combine(directory, "rozmowy-" + target + ".md");
            string json = JsonSerializer.Serialize(turns.Select(x => new ConversationMemoryEntry
            {
                Id = x.Id, Role = x.Role, Text = x.Text, Source = x.Source, SessionId = x.SessionId, Timestamp = x.Timestamp, ProjectId = x.ProjectId
            }).ToList(), jsonOptions);
            WriteNew(jsonPath, json);
            string readBack = File.ReadAllText(jsonPath, Encoding.UTF8);
            if (readBack != json) return new ArchiveResult(false, "Zapis archiwum nie zgadza się z odczytem kontrolnym — rozmowy NIE zostały usunięte.", null);
            WriteNew(markdownPath, ToMarkdown(target, turns));

            int pruned = memory.PruneEntriesBefore(to);
            var summary = new ArchiveSummary
            {
                Month = target, JsonPath = jsonPath, MarkdownPath = markdownPath, Turns = turns.Length,
                Conversations = turns.Select(x => x.SessionId).Distinct().Count(),
                Bytes = Encoding.UTF8.GetByteCount(json),
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(readBack)))
            };
            return new ArchiveResult(true,
                "Zarchiwizowano " + turns.Length + " wypowiedzi z " + turns.Select(x => x.SessionId).Distinct().Count() +
                " rozmów (" + target + ") i usunięto je z aktywnego magazynu (" + pruned + " wpisów).\n" +
                "Wspomnienia i profil nie zostały ruszone.\n" + jsonPath + "\nSHA-256: " + summary.Sha256, summary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return new ArchiveResult(false, "Nie udało się zapisać archiwum: " + ex.Message + " Rozmowy pozostały w magazynie.", null);
        }
    }

    /// <summary>Archives and prunes every month older than the retention window. Called at startup.</summary>
    public IReadOnlyList<ArchiveSummary> ArchiveDue() => ArchiveDue(KeepMonthsProvider());

    public IReadOnlyList<ArchiveSummary> ArchiveDue(int keepMonths)
    {
        var archived = new List<ArchiveSummary>();
        if (keepMonths <= 0) return archived;
        DateTime now = NowProvider();
        DateTime cutoff = new DateTime(now.Year, now.Month, 1).AddMonths(-keepMonths);
        var months = memory.GetAllEntries().Select(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, 1))
            .Distinct().Where(x => x < cutoff).OrderBy(x => x).ToArray();
        foreach (DateTime month in months.Take(24))
        {
            var result = ArchiveMonth(month.ToString("yyyy-MM"));
            if (result.Summary != null) archived.Add(result.Summary);
        }
        return archived;
    }

    public bool DeleteArchive(string month, out string reason)
    {
        reason = "";
        string directory = Path.Combine(archiveRoot, month);
        if (!Directory.Exists(directory)) { reason = "Nie ma archiwum " + month + "."; return false; }
        try
        {
            Directory.Delete(directory, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { reason = "Nie udało się usunąć archiwum: " + ex.Message; return false; }
    }

    private static string ToMarkdown(string month, IReadOnlyList<ConversationMemoryEntry> turns)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Rozmowy zarchiwizowane: " + month);
        builder.AppendLine();
        builder.AppendLine("- Zarchiwizowano: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("- Wypowiedzi: " + turns.Count + " · rozmów: " + turns.Select(x => x.SessionId).Distinct().Count());
        builder.AppendLine("- Plik lokalny. Zawiera treść rozmów — traktuj go jak dane prywatne.");
        builder.AppendLine();
        foreach (var group in turns.GroupBy(x => x.SessionId))
        {
            builder.AppendLine("## Rozmowa " + group.Key);
            foreach (var turn in group.OrderBy(x => x.Timestamp))
                builder.AppendLine("- **" + (turn.Role == "user" ? "Ja" : "Sentinel X") + "** " + turn.Timestamp.ToString("dd.MM.yyyy HH:mm") + ": " + turn.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static void WriteNew(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Move(path, path + ".previous", true);
        File.Move(temp, path, true);
    }
}
