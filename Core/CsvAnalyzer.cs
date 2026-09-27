using System.Globalization;
using System.Text;
using System.IO;

namespace SentinelX.Core;

/// <summary>0.97 · read-only CSV/TSV profiler (#1071 reader, #1075 data profiler). Never rewrites the file it reads.</summary>
public static class CsvAnalyzer
{
    public const long MaxBytes = 4L * 1024 * 1024;
    public const int MaxRows = 20000;
    public const int MaxColumns = 64;

    public sealed record Column(string Header, int Filled, int Empty, int Distinct, bool Numeric, double Min, double Max, double Average, int MaxLength)
    {
        public int Total => Filled + Empty;
        public double EmptyPercent => Total == 0 ? 0 : Empty * 100.0 / Total;
    }

    public sealed record Report(string Path, char Separator, int Rows, IReadOnlyList<Column> Columns, int DuplicateRows, long Bytes, bool Truncated)
    {
        public bool Usable => Rows > 0 && Columns.Count > 0;
    }

    public static Report AnalyzePath(string path, char? separator = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Nie ma takiego pliku.", path);
        if (info.Length > MaxBytes) throw new IOException("Plik ma " + (info.Length / 1024 / 1024) + " MB — analizuję pliki do " + (MaxBytes / 1024 / 1024) + " MB, żeby nie blokować aplikacji.");
        string text = File.ReadAllText(path);
        return Analyze(text, Path.GetFileName(path), separator);
    }

    public static Report Analyze(string text, string name = "(dane)", char? separator = null)
    {
        char sep = separator ?? DetectSeparator(text);
        List<List<string>> table = Parse(text, sep);
        if (table.Count == 0) throw new InvalidDataException("Plik jest pusty albo nie da się go czytać jako CSV.");

        List<string> headers = table[0];
        bool hasHeader = headers.All(h => h.Length > 0) && headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() == headers.Count && table.Count > 1;
        var rows = hasHeader ? table.Skip(1).ToList() : table;
        if (!hasHeader) headers = headers.Select((_, i) => "kolumna " + (i + 1)).ToList();

        int columns = Math.Min(headers.Count, MaxColumns);
        var stats = new List<Column>(columns);
        for (int c = 0; c < columns; c++)
        {
            var values = rows.Select(r => c < r.Count ? r[c].Trim() : "").ToList();
            int filled = values.Count(v => v.Length > 0);
            int empty = values.Count - filled;
            var numbers = new List<double>();
            foreach (string value in values.Where(v => v.Length > 0))
            {
                string normalized = value.Replace(',', '.');
                if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) numbers.Add(number);
            }
            bool numeric = filled > 0 && numbers.Count == filled;
            stats.Add(new Column(headers[c], filled, empty, values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                numeric, numbers.Count > 0 ? numbers.Min() : 0, numbers.Count > 0 ? numbers.Max() : 0,
                numbers.Count > 0 ? numbers.Average() : 0, values.Count > 0 ? values.Max(v => v.Length) : 0));
        }

        int duplicates = rows.Select(r => string.Join('\u0001', r)).GroupBy(x => x, StringComparer.Ordinal).Count(g => g.Count() > 1);
        return new Report(name, sep, rows.Count, stats, duplicates, Encoding.UTF8.GetByteCount(text), rows.Count >= MaxRows);
    }

    public static string Describe(Report report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PROFIL DANYCH · " + report.Path + " (separator: " + (report.Separator == '\t' ? "TAB" : report.Separator.ToString()) + ")");
        builder.AppendLine("Wierszy: " + report.Rows.ToString("N0", new CultureInfo("pl-PL")) + " • Kolumn: " + report.Columns.Count + " • Rozmiar: " + (report.Bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB");
        if (report.DuplicateRows > 0) builder.AppendLine("Powtarzające się wiersze: " + report.DuplicateRows + " — sprawdź, czy to nie jest podwójny import.");
        builder.AppendLine();
        builder.AppendLine("KOLUMNY (wypełnione/puste/uniq/min/max/śr)");
        foreach (Column column in report.Columns)
        {
            string range = column.Numeric
                ? " [" + column.Min.ToString("0.##", CultureInfo.InvariantCulture) + " … " + column.Max.ToString("0.##", CultureInfo.InvariantCulture) + "] śr " + column.Average.ToString("0.##", CultureInfo.InvariantCulture)
                : " tekst do " + column.MaxLength + " zn.";
            builder.AppendLine("· " + Trim(column.Header, 24) + ": " + column.Filled + " wypełn., " + column.Empty + " pustych (" + column.EmptyPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%), " +
                column.Distinct + " unikalnych" + range);
        }
        if (report.Truncated) builder.AppendLine();
        if (report.Truncated) builder.AppendLine("Uwaga: plik jest większy niż " + MaxRows + " wierszy — podałem statystyki z pierwszej części.");
        builder.AppendLine();
        builder.AppendLine("Plik został tylko odczytany — nic w nim nie zmieniłem.");
        return builder.ToString().TrimEnd();
    }

    public static List<List<string>> Parse(string text, char separator)
    {
        var rows = new List<List<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        foreach (char ch in text)
        {
            if (quoted)
            {
                if (ch == '"') quoted = false; else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case '\r': break;
                case '\n':
                    cells.Add(cell.ToString()); rows.Add(cells);
                    cells = []; cell.Clear();
                    if (rows.Count >= MaxRows + 1) return rows;
                    break;
                default:
                    if (ch == separator) { cells.Add(cell.ToString()); cell.Clear(); }
                    else cell.Append(ch);
                    break;
            }
        }
        if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); rows.Add(cells); }
        return rows.Where(r => r.Count > 1 || (r.Count == 1 && r[0].Trim().Length > 0)).ToList();
    }

    public static char DetectSeparator(string text)
    {
        string firstLine = text.Split('\n').FirstOrDefault() ?? "";
        int commas = firstLine.Count(c => c == ','), semicolons = firstLine.Count(c => c == ';'), tabs = firstLine.Count(c => c == '\t');
        if (tabs > commas && tabs > semicolons) return '\t';
        return semicolons > commas ? ';' : ',';
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
