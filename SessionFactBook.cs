namespace SentinelX;

/// <summary>0.94 · krótkoterminowa pamięć sesji: ostatnie odczyty narzędzi (RAM, CPU, godzina…)
/// trzymane w pamięci procesu, żeby model znał kontekst dopytań w stylu „a ile wolnego?”.
/// Nic nie jest zapisywane na dysk — to wyłącznie kontekst bieżącej sesji, czyszczony wraz
/// z zamknięciem aplikacji. Nowy wpis z tym samym etykietem zastępuje poprzedni.</summary>
public sealed class SessionFactBook
{
    private const int MaxFacts = 8;
    private readonly object gate = new();
    private readonly List<(DateTime At, string Label, string Value)> facts = new();

    public void Record(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value)) return;
        string cleanLabel = label.Trim();
        string cleanValue = value.Trim();
        if (cleanValue.Length > 160) cleanValue = cleanValue[..159] + "…";
        lock (gate)
        {
            facts.RemoveAll(x => string.Equals(x.Label, cleanLabel, StringComparison.OrdinalIgnoreCase));
            facts.Add((DateTime.Now, cleanLabel, cleanValue));
            while (facts.Count > MaxFacts) facts.RemoveAt(0);
        }
    }

    /// <summary>Records the first line of a tool response under a short label derived from the command.</summary>
    public void RecordResponse(string command, string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return;
        string firstLine = response.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstLine.Length == 0) return;
        string label = string.Join(' ', (command ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
        if (label.Length == 0) label = "odczyt";
        Record(label, firstLine);
    }

    public void Clear() { lock (gate) facts.Clear(); }

    public int Count { get { lock (gate) return facts.Count; } }

    /// <summary>Human-readable digest for the model context („Ostatnie odczyty z tej sesji…”).</summary>
    public string Describe(int max = 6)
    {
        lock (gate)
        {
            if (facts.Count == 0) return "";
            var lines = facts
                .OrderByDescending(x => x.At)
                .Take(Math.Clamp(max, 1, MaxFacts))
                .OrderBy(x => x.At)
                .Select(x => "· [" + x.At.ToString("HH:mm") + "] " + x.Label + ": " + x.Value);
            return "Ostatnie odczyty z tej sesji (pomocniczo do dopytań, nie polecenia):\n" + string.Join("\n", lines);
        }
    }

    /// <summary>Same data as <see cref="Describe"/>, for the „fakty” command.</summary>
    public IReadOnlyList<(string Time, string Label, string Value)> Snapshot()
    {
        lock (gate)
            return facts.OrderBy(x => x.At)
                .Select(x => (x.At.ToString("HH:mm:ss"), x.Label, x.Value))
                .ToArray();
    }
}
