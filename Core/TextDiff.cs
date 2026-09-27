using System.Text;

namespace SentinelX;

/// <summary>0.97 · line diff on an LCS table (#1019 text diff / #1020 file diff). Pure, deterministic, bounded.</summary>
public static class TextDiff
{
    public const int MaxLines = 1500;

    public sealed record Line(string Kind, string Text)
    {
        public override string ToString() => Kind + Text;
    }

    public sealed record Result(int Added, int Removed, int Same, bool Truncated, IReadOnlyList<Line> Lines)
    {
        public bool Identical => Added == 0 && Removed == 0;
    }

    /// <summary>Kind is " " (common), "-" (only left) or "+" (only right).</summary>
    public static Result Compare(string left, string right, int maxLines = MaxLines)
    {
        string[] a = Split(left), b = Split(right);
        bool truncated = a.Length > maxLines || b.Length > maxLines;
        if (truncated) { a = a.Take(maxLines).ToArray(); b = b.Take(maxLines).ToArray(); }

        // Longest common subsequence: O(n*m) with a hard cap, so a huge file can never hang the UI.
        int n = a.Length, m = b.Length;
        var table = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                table[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);

        var rows = new List<Line>(n + m);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal)) { rows.Add(new Line(" ", a[x])); x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) { rows.Add(new Line("-", a[x])); x++; }
            else { rows.Add(new Line("+", b[y])); y++; }
        }
        while (x < n) rows.Add(new Line("-", a[x++]));
        while (y < m) rows.Add(new Line("+", b[y++]));

        return new Result(rows.Count(l => l.Kind == "+"), rows.Count(l => l.Kind == "-"),
            rows.Count(l => l.Kind == " "), truncated, rows);
    }

    /// <summary>Human-readable Polish summary. Long diffs are cut, never silently invented.</summary>
    public static string Describe(string left, string right, string leftName = "A", string rightName = "B", int context = 60)
    {
        Result result = Compare(left, right);
        var builder = new StringBuilder();
        builder.AppendLine(result.Identical
            ? "Pliki są identyczne (" + result.Same + " linii) — zero różnic do pokazania."
            : "Różnice " + leftName + " → " + rightName + ": +" + result.Added + " / -" + result.Removed + " (bez zmian: " + result.Same + " linii)");
        if (result.Identical) return builder.ToString().TrimEnd();

        int shown = 0;
        foreach (Line line in result.Lines)
        {
            if (line.Kind == " ") continue;
            if (shown >= context) { builder.AppendLine("… pokazano pierwsze " + context + " zmienionych linii z " + (result.Added + result.Removed) + "."); break; }
            string marker = line.Kind == "+" ? "+ " : "- ";
            string text = line.Text.Length > 180 ? line.Text[..180] + "…" : line.Text;
            builder.AppendLine(marker + text);
            shown++;
        }
        if (result.Truncated) builder.AppendLine("Uwaga: plik był dłuższy niż " + MaxLines + " linii — porównałem tylko początek.");
        return builder.ToString().TrimEnd();
    }

    private static string[] Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
