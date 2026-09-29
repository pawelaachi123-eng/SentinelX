using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelX.Services.Phone;

/// <summary>Jedna tura rozmowy (kto, co, kiedy) — zapisywana lokalnie jako tekst.</summary>
public sealed record PhoneTurn(string Speaker, string Text, string At);

/// <summary>Rekord jednej sprawy telefonicznej. NIGDY nie zawiera audio — tylko transkrypt tekstowy
/// (nagrywanie dźwięku wymaga wyraźnej zgody i jest tu świadomie nieobecne).</summary>
public sealed record PhoneCallRecord(
    string Id,
    DateTime StartedAt,
    string TargetName,
    string Number,
    string Goal,
    string Status,
    string Summary,
    int DurationSeconds,
    IReadOnlyList<PhoneTurn> Turns,
    string OwnerQuestion,
    string Evidence,
    string TranscriptPath);

/// <summary>Lokalna historia rozmów: indeks JSON + transkrypty Markdown. Zero chmury.</summary>
public sealed class PhoneCallStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string directory;
    private List<PhoneCallRecord>? cache;

    public PhoneCallStore(string directory) => this.directory = directory;

    private string IndexPath => Path.Combine(directory, "indeks.json");

    public string NewCallId() => "ROZ-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public PhoneCallRecord Save(PhoneCallRecord record)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            string transcriptPath = record.TranscriptPath.Length > 0
                ? record.TranscriptPath
                : Path.Combine(directory, record.Id + ".md");
            File.WriteAllText(transcriptPath, RenderTranscript(record, transcriptPath), new UTF8Encoding(false));
            var stored = record with { TranscriptPath = transcriptPath };
            var all = LoadLocked();
            all.RemoveAll(x => x.Id == record.Id);
            all.Add(stored);
            all.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(all, Options), new UTF8Encoding(false));
            cache = all;
            return stored;
        }
    }

    public IReadOnlyList<PhoneCallRecord> List() { lock (gate) return LoadLocked().ToList(); }

    public PhoneCallRecord? Find(string id) { lock (gate) return LoadLocked().FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase)); }

    public PhoneCallRecord? LatestPending() { lock (gate) return LoadLocked().LastOrDefault(x => x.Status == "CZEKA NA CIEBIE"); }

    /// <summary>Zapis decyzji właściciela po pytaniu (tak/nie) — historia musi mówić prawdę.</summary>
    public void ApplyDecision(string id, string status, string summary, string ownerQuestion, IReadOnlyList<PhoneTurn> turns, string evidence)
    {
        lock (gate)
        {
            var all = LoadLocked();
            int index = all.FindIndex(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var updated = all[index] with { Status = status, Summary = summary, OwnerQuestion = ownerQuestion, Turns = turns, Evidence = evidence };
            all[index] = updated;
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(all, Options), new UTF8Encoding(false));
            cache = all;
            File.WriteAllText(updated.TranscriptPath, RenderTranscript(updated, updated.TranscriptPath), new UTF8Encoding(false));
        }
    }

    private List<PhoneCallRecord> LoadLocked()
    {
        if (cache != null) return cache;
        if (!File.Exists(IndexPath)) return [];
        try
        {
            cache = JsonSerializer.Deserialize<List<PhoneCallRecord>>(File.ReadAllText(IndexPath)) ?? [];
        }
        catch { cache = []; }
        return cache;
    }

    private static string RenderTranscript(PhoneCallRecord record, string path)
    {
        var text = new StringBuilder();
        text.AppendLine("# Rozmowa " + record.Id);
        text.AppendLine();
        text.AppendLine("- **Kiedy:** " + record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        text.AppendLine("- **Dokąd:** " + (record.TargetName.Length > 0 ? record.TargetName : "numer") + " · " + record.Number);
        text.AppendLine("- **Cel:** " + record.Goal);
        text.AppendLine("- **Status:** " + record.Status);
        text.AppendLine("- **Podsumowanie:** " + (record.Summary.Length > 0 ? record.Summary : "—"));
        if (record.OwnerQuestion.Length > 0) text.AppendLine("- **Pytanie do Ciebie:** " + record.OwnerQuestion);
        text.AppendLine("- **Dowód wykonania:** " + record.Evidence);
        text.AppendLine("- **Transkrypt:** " + path);
        text.AppendLine();
        text.AppendLine("## Przebieg (tylko tekst — audio nie jest zapisywane)");
        text.AppendLine();
        foreach (var turn in record.Turns)
            text.AppendLine("**" + turn.At + " · " + turn.Speaker + ":** " + turn.Text);
        if (record.Turns.Count == 0) text.AppendLine("(brak wypowiedzi — połączenie nie doszło do skutku)");
        return text.ToString();
    }
}
