using System.IO;
using System.Linq;
using System.Text.Json;
using SentinelX.Core;

namespace SentinelX.Services.Intent;

/// <summary>Sentinel's honest "learning from mistakes": every typo repair is appended to a local,
/// human-readable journal (one JSON line each). Nothing is sent anywhere, the file can be opened
/// and deleted at any time, and the „lekcje” command shows what was learned. This is deterministic
/// local learning — not a model update — and it changes nothing without the user typing the command.</summary>
public sealed class UnderstandingJournal
{
    public const string FileName = "Lessons.jsonl";
    private readonly string journalPath;
    private readonly object gate = new();
    private const int MaxLines = 500;

    private sealed record Lesson(DateTime At, string From, string To, string Summary);

    public UnderstandingJournal(string? directory = null)
    {
        string root = directory ?? AppPaths.MemoryDirectory;
        Directory.CreateDirectory(root);
        journalPath = Path.Combine(root, FileName);
    }

    public string JournalPath => journalPath;

    /// <summary>Appends one repair. Failure to write must never break understanding — errors are swallowed.</summary>
    public void Append(string from, string to, string summary)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return;
        try
        {
            lock (gate)
            {
                var lesson = new Lesson(DateTime.Now, from.Trim(), to.Trim(), summary ?? "");
                File.AppendAllText(journalPath, JsonSerializer.Serialize(lesson) + "\n");
                Trim();
            }
        }
        catch { /* the journal is best-effort: understanding must never fail because of it */ }
    }

    private void Trim()
    {
        var lines = File.ReadAllLines(journalPath);
        if (lines.Length <= MaxLines) return;
        File.WriteAllLines(journalPath, lines.Skip(lines.Length - MaxLines));
    }

    private List<Lesson> ReadAll()
    {
        try
        {
            if (!File.Exists(journalPath)) return [];
            var lessons = new List<Lesson>();
            foreach (string line in File.ReadAllLines(journalPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { lessons.Add(JsonSerializer.Deserialize<Lesson>(line) ?? new Lesson(DateTime.Now, "", "", "")); }
                catch { /* one broken line must not hide the rest */ }
            }
            return lessons;
        }
        catch { return []; }
    }

    public int TotalCount => ReadAll().Count;

    public IReadOnlyList<string> Recent(int count = 10) =>
        ReadAll().TakeLast(count).Select(x => $"{x.At:dd.MM HH:mm} · „{x.From}” → „{x.To}”").ToList();

    /// <summary>The most frequent correction pairs — what Sentinel has actually learned so far.</summary>
    public IReadOnlyList<(string From, string To, int Count)> TopPairs(int count = 5) =>
        ReadAll()
            .Where(x => x.From.Length > 0 && x.To.Length > 0)
            .GroupBy(x => (x.From, x.To))
            .OrderByDescending(g => g.Count())
            .Take(count)
            .Select(g => (g.Key.From, g.Key.To, g.Count()))
            .ToList();

    /// <summary>The „lekcje” report: counts, top pairs and the most recent repairs.</summary>
    public string Report()
    {
        var all = ReadAll();
        if (all.Count == 0)
            return "LEKCJE · uczenie się na błędach\nNie zapisano jeszcze żadnej poprawki. Gdy naprawię literówkę w poleceniu, zapamiętam ją tutaj.\nPlik: " + journalPath;
        var top = all.Where(x => x.From.Length > 0 && x.To.Length > 0)
            .GroupBy(x => (x.From, x.To)).OrderByDescending(g => g.Count()).Take(5).ToArray();
        string lines = "LEKCJE · uczenie się na błędach\nZapisane poprawki: " + all.Count + " (limit " + MaxLines + ", lokalny plik JSONL).\nNajczęstsze:\n"
            + string.Join("\n", top.Select(g => $"· „{g.Key.From}” → „{g.Key.To}”  ×{g.Count()}"))
            + "\nOstatnie:\n" + string.Join("\n", all.TakeLast(5).Select(x => $"· {x.At:dd.MM HH:mm}  „{x.From}” → „{x.To}”"))
            + "\n\nTo deterministyczna lokalna pamięć poprawek — nic nie jest wysyłane ani zmieniane bez Twojego polecenia. Plik: " + journalPath;
        return lines;
    }
}
