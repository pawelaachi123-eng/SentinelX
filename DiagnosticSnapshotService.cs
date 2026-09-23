using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX;

/// <summary>One point-in-time diagnostic reading. Snapshots are read-only evidence, never a hardware verdict.</summary>
public sealed class DiagnosticSnapshot
{
    public string Id { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public string Label { get; set; } = "";
    public List<DiagnosticSection> Sections { get; set; } = [];
}

/// <summary>Persisted shape of a reading: plain settable properties, so the file round-trips without ctor magic.</summary>
public sealed class StoredSnapshot
{
    public string Id { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public string Label { get; set; } = "";
    public List<StoredSection> Sections { get; set; } = [];
}

public sealed class StoredSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class SnapshotState
{
    public int Version { get; set; } = 1;
    public List<StoredSnapshot> Snapshots { get; set; } = [];
}

public sealed class LineChange
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class SectionDiff
{
    public string SectionTitle { get; set; } = "";
    public List<string> Added { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public List<LineChange> Changed { get; set; } = [];
    public bool Empty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
    public int Count => Added.Count + Removed.Count + Changed.Count;
}

public sealed class DiagnosticDiff
{
    public string FirstLabel { get; set; } = "";
    public string SecondLabel { get; set; } = "";
    public DateTimeOffset FirstCapturedAt { get; set; }
    public DateTimeOffset SecondCapturedAt { get; set; }
    public List<SectionDiff> Sections { get; set; } = [];
    public List<string> SectionsOnlyInFirst { get; set; } = [];
    public int TotalChanges => Sections.Sum(x => x.Count);
    public string Headline => TotalChanges == 0 && SectionsOnlyInFirst.Count == 0
        ? "Brak różnic między tymi odczytami — stan opisany przez te sekcje się nie zmienił."
        : "Znaleziono " + TotalChanges + " różnic w " + Sections.Count(x => !x.Empty) + " sekcjach" +
          (SectionsOnlyInFirst.Count > 0 ? "; sekcji brak w drugim odczycie: " + SectionsOnlyInFirst.Count : "") + ".";

    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Porównanie snapshotów diagnostycznych Sentinel X");
        builder.AppendLine();
        builder.AppendLine("- Odczyt A: " + FirstLabel + " · " + FirstCapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + " (czas lokalny)");
        builder.AppendLine("- Odczyt B: " + SecondLabel + " · " + SecondCapturedAt.ToString("yyyy-MM-dd HH:mm:ss") + " (czas lokalny)");
        builder.AppendLine("- Wynik: " + Headline);
        builder.AppendLine();
        builder.AppendLine("To porównanie dwóch odczytów wykonanych przez aplikację. Nie jest diagnozą kondycji sprzętu ani dowodem przyczyny.");
        builder.AppendLine();
        foreach (var section in Sections)
        {
            builder.AppendLine("## " + section.SectionTitle);
            if (section.Empty) { builder.AppendLine("- bez zmian"); builder.AppendLine(); continue; }
            foreach (var change in section.Changed) builder.AppendLine("- zmienione: `" + change.From + "` → `" + change.To + "`");
            foreach (string added in section.Added) builder.AppendLine("- pojawiło się: `" + added + "`");
            foreach (string removed in section.Removed) builder.AppendLine("- zniknęło: `" + removed + "`");
            builder.AppendLine();
        }
        if (SectionsOnlyInFirst.Count > 0)
        {
            builder.AppendLine("## Sekcje nieodczytane w drugim odczycie");
            foreach (string missing in SectionsOnlyInFirst) builder.AppendLine("- " + missing);
            builder.AppendLine();
        }
        return builder.ToString();
    }
}

/// <summary>Point-in-time diagnostic snapshots: capture, honest line-level comparison, local export with read-back proof.</summary>
public sealed class DiagnosticSnapshotService
{
    private const int MaxSnapshots = 20;
    private readonly object syncRoot = new();
    private readonly string storePath;
    private readonly PcDiagnosticService diagnostics;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly List<DiagnosticSnapshot> snapshots = [];

    public string? LastStorageError { get; private set; }
    public string StoragePath => storePath;
    public event Action? Changed;

    public DiagnosticSnapshotService(PcDiagnosticService? diagnostics = null, string? directory = null)
    {
        this.diagnostics = diagnostics ?? new PcDiagnosticService();
        storePath = Path.Combine(directory ?? AppPaths.Root, "snapshots.json");
        Load();
    }

    public IReadOnlyList<DiagnosticSnapshot> GetSnapshots()
    {
        lock (syncRoot) return snapshots.OrderByDescending(x => x.CapturedAt).Select(Clone).ToArray();
    }

    public DiagnosticSnapshot? Find(string id)
    {
        lock (syncRoot) return snapshots.Where(x => x.Id == id).Select(Clone).FirstOrDefault();
    }

    public async Task<DiagnosticSnapshot?> CaptureAsync(string label = "", CancellationToken token = default)
    {
        var report = await diagnostics.CollectAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        DiagnosticSnapshot snapshot;
        lock (syncRoot)
        {
            snapshot = new DiagnosticSnapshot
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                CapturedAt = report.CapturedAt,
                Label = (label ?? "").Trim() is { Length: > 0 and <= 80 } text ? text : "Odczyt " + (snapshots.Count + 1),
                Sections = report.Sections.ToList()
            };
            snapshots.Add(snapshot);
            TrimLocked(); // oldest readings go first: the store stays bounded
            SaveLocked();
        }
        Changed?.Invoke();
        return Clone(snapshot);
    }

    public bool Delete(string id)
    {
        bool removed;
        lock (syncRoot)
        {
            var snapshot = snapshots.FirstOrDefault(x => x.Id == id);
            if (snapshot == null) { LastStorageError = "Nie znaleziono takiego odczytu."; return false; }
            removed = snapshots.Remove(snapshot);
            SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public string List()
    {
        var stored = GetSnapshots();
        if (stored.Count == 0)
            return "Brak zapisanych odczytów diagnostycznych. Wpisz „snapshot”, żeby zapisać aktualny stan, a potem „porównaj snapshoty”.";
        var lines = stored.Select((x, i) => (i + 1) + ". " + x.Label + " · " + x.CapturedAt.ToString("dd.MM.yyyy HH:mm:ss") + " · " + x.Sections.Count + " sekcji · id " + x.Id);
        return "Zapisane odczyty diagnostyczne (najnowszy pierwszy):\n" + string.Join("\n", lines) +
            "\nLimit " + MaxSnapshots + " odczytów — najstarsze są usuwane automatycznie. Porównanie: „porównaj snapshoty”.";
    }

    public DiagnosticDiff? Compare(string firstId, string secondId, out string reason)
    {
        reason = "";
        var first = Find(firstId);
        var second = Find(secondId);
        if (first == null || second == null) { reason = "Wybierz dwa istniejące odczyty."; return null; }
        if (first.Id == second.Id) { reason = "Wybrano ten sam odczyt dwa razy — porównanie nie ma sensu."; return null; }
        return Compare(first, second);
    }

    /// <summary>Pure comparison so it can be tested without touching the machine.</summary>
    public static DiagnosticDiff Compare(DiagnosticSnapshot first, DiagnosticSnapshot second)
    {
        var diff = new DiagnosticDiff
        {
            FirstLabel = first.Label, SecondLabel = second.Label,
            FirstCapturedAt = first.CapturedAt, SecondCapturedAt = second.CapturedAt
        };
        foreach (var before in first.Sections)
        {
            var after = second.Sections.FirstOrDefault(x => x.Id == before.Id);
            if (after == null) { diff.SectionsOnlyInFirst.Add(before.Title); continue; }
            var section = new SectionDiff { SectionTitle = before.Title };
            if (!string.Equals(before.Status, after.Status, StringComparison.Ordinal))
                section.Changed.Add(new LineChange { From = "stan odczytu: " + before.Status, To = "stan odczytu: " + after.Status });
            CompareLines(before.Content, after.Content, section);
            if (!section.Empty) diff.Sections.Add(section);
        }
        foreach (var onlyAfter in second.Sections.Where(x => first.Sections.All(y => y.Id != x.Id)))
        {
            var section = new SectionDiff { SectionTitle = onlyAfter.Title + " (nowa sekcja)" };
            section.Added.AddRange(ReadableLines(onlyAfter.Content));
            if (!section.Empty) diff.Sections.Add(section);
        }
        return diff;
    }

    private static void CompareLines(string before, string after, SectionDiff section)
    {
        var beforePairs = Pairs(before);
        var afterPairs = Pairs(after);
        foreach (var pair in beforePairs)
        {
            if (!afterPairs.TryGetValue(pair.Key, out string? value)) section.Removed.Add(pair.Key + ": " + pair.Value);
            else if (!string.Equals(value, pair.Value, StringComparison.Ordinal)) section.Changed.Add(new LineChange { From = pair.Key + ": " + pair.Value, To = pair.Key + ": " + value });
        }
        foreach (var pair in afterPairs.Where(x => !beforePairs.ContainsKey(x.Key)))
            section.Added.Add(pair.Key + ": " + pair.Value);
        // Free-form lines have no key: report them as appeared/disappeared, order-insensitively.
        string[] beforePlain = ReadableLines(before).Where(x => !ContainsPair(x)).ToArray();
        string[] afterPlain = ReadableLines(after).Where(x => !ContainsPair(x)).ToArray();
        foreach (string line in beforePlain.Except(afterPlain, StringComparer.Ordinal)) section.Removed.Add(line);
        foreach (string line in afterPlain.Except(beforePlain, StringComparer.Ordinal)) section.Added.Add(line);
    }

    private static bool ContainsPair(string line) => Pairs(line).Count > 0;

    private static Dictionary<string, string> Pairs(string content)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in ReadableLines(content))
        {
            int separator = line.IndexOf(':');
            if (separator is < 1 or > 60) continue;
            string key = line[..separator].Trim();
            if (key.Length == 0) continue;
            pairs[key] = line[(separator + 1)..].Trim();
        }
        return pairs;
    }

    private static string[] ReadableLines(string content) =>
        (content ?? "").Replace("\r\n", "\n").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

    /// <summary>Writes Markdown + JSON to the local Reports folder and proves the write with a read-back hash.</summary>
    public async Task<ActionExecutionResult> ExportComparisonAsync(string firstId, string secondId, CancellationToken token = default, string? directory = null)
    {
        var diff = Compare(firstId, secondId, out string reason);
        if (diff == null) return ActionExecutionResult.Failure(reason);
        try
        {
            string target = directory ?? Path.Combine(AppPaths.Root, "Reports");
            Directory.CreateDirectory(target);
            string basename = "snapshots-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            string markdownPath = Path.Combine(target, basename + ".md");
            string jsonPath = Path.Combine(target, basename + ".json");
            string markdown = diff.ToMarkdown();
            await WriteNewFileAsync(markdownPath, markdown, token).ConfigureAwait(false);
            string readBack = await File.ReadAllTextAsync(markdownPath, token).ConfigureAwait(false);
            if (readBack != markdown) return ActionExecutionResult.Failure("Zapisany plik nie zgadza się z odczytem kontrolnym — eksport odrzucony.", markdownPath);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(readBack)));
            try { await WriteNewFileAsync(jsonPath, JsonSerializer.Serialize(diff, jsonOptions), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return ActionExecutionResult.UnverifiedSuccess("Zapisano porównanie tekstowe; plik JSON nie został zapisany: " + ex.Message, markdownPath); }
            return ActionExecutionResult.VerifiedSuccess(
                "Zapisano porównanie dwóch odczytów lokalnie. Nic nie zostało wysłane do internetu.",
                markdownPath + "\n" + jsonPath + "\nSHA-256 (Markdown): " + hash);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return ActionExecutionResult.Failure("Nie udało się zapisać porównania: " + ex.Message); }
    }

    /// <summary>Read-back proof of the store: same reading ids in the file and in memory, with the file hash.</summary>
    internal bool VerifyPersistedState(out string evidence)
    {
        evidence = "";
        try
        {
            if (!File.Exists(storePath)) { evidence = "Brak pliku odczytów — nic nie zapisano."; return false; }
            byte[] bytes = File.ReadAllBytes(storePath);
            // ReadAllText tolerates a BOM; Encoding.UTF8.GetString(bytes) would keep it and break parsing.
            var loaded = JsonSerializer.Deserialize<SnapshotState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
            if (loaded == null) { evidence = "Plik odczytów jest pusty."; return false; }
            var fileIds = (loaded.Snapshots ?? []).Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] memoryIds;
            lock (syncRoot) memoryIds = snapshots.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            evidence = "Plik: " + storePath + "\nRozmiar: " + bytes.Length + " B\nOdczytów w pliku: " + fileIds.Length +
                "\nOdczytów w pamięci: " + memoryIds.Length + "\nSHA-256: " + Convert.ToHexString(SHA256.HashData(bytes));
            return fileIds.SequenceEqual(memoryIds, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
        { evidence = "Odczyt kontrolny nie powiódł się: " + ex.GetType().Name + ": " + ex.Message; return false; }
    }

    private void TrimLocked()
    {
        if (snapshots.Count <= MaxSnapshots) return;
        var keep = snapshots.OrderByDescending(x => x.CapturedAt).Take(MaxSnapshots).ToArray();
        snapshots.Clear();
        snapshots.AddRange(keep);
    }

    private void Load()
    {
        lock (syncRoot)
        {
            snapshots.Clear();
            try
            {
                if (!File.Exists(storePath)) return;
                if (new FileInfo(storePath).Length > 10 * 1024 * 1024) throw new IOException("Plik odczytów przekracza 10 MB.");
                var loaded = JsonSerializer.Deserialize<SnapshotState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
                if (loaded?.Snapshots == null) throw new JsonException("Pusty plik odczytów.");
                foreach (var stored in loaded.Snapshots)
                {
                    if (stored == null) continue;
                    snapshots.Add(new DiagnosticSnapshot
                    {
                        Id = stored.Id.Length > 0 ? stored.Id : Guid.NewGuid().ToString("N")[..12],
                        CapturedAt = stored.CapturedAt,
                        Label = stored.Label ?? "",
                        Sections = (stored.Sections ?? [])
                            .Where(x => x != null)
                            .Select(x => new DiagnosticSection(x.Id ?? "", x.Title ?? "", x.Status ?? "", x.Content ?? ""))
                            .ToList()
                    });
                }
                TrimLocked();
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException)
            {
                snapshots.Clear();
                LastStorageError = "Nie udało się wczytać odczytów diagnostycznych: " + ex.Message;
                try { if (File.Exists(storePath)) File.Copy(storePath, storePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
                catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { LastStorageError += " Nie udało się utworzyć kopii uszkodzonego pliku."; }
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            var state = new SnapshotState
            {
                Version = 1,
                Snapshots = snapshots.Select(x => new StoredSnapshot
                {
                    Id = x.Id, CapturedAt = x.CapturedAt, Label = x.Label,
                    Sections = x.Sections.Select(section => new StoredSection
                    { Id = section.Id, Title = section.Title, Status = section.Status, Content = section.Content }).ToList()
                }).ToList()
            };
            string temp = storePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, jsonOptions), new UTF8Encoding(false));
            File.Move(temp, storePath, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastStorageError = "Odczyty działają tylko do zamknięcia aplikacji. Błąd zapisu: " + ex.Message; }
    }

    private static async Task WriteNewFileAsync(string path, string text, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch { try { File.Delete(path); } catch { } throw; }
    }

    private static DiagnosticSnapshot Clone(DiagnosticSnapshot x) => new()
    { Id = x.Id, CapturedAt = x.CapturedAt, Label = x.Label, Sections = x.Sections.ToList() };
}
