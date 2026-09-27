using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 4 · pozycje 186–220 — analiza kodu źródłowego bez wysyłania czegokolwiek do sieci.
/// <para>Zasady: tylko odczyt, twarde limity (liczba plików, rozmiar pliku, głębokość, czas),
/// pomijane katalogi techniczne (node_modules, .git, bin, obj, dist). Wszystkie wnioski są
/// heurystykami opartymi na tekście — narzędzie mówi o tym wprost i nie udaje kompilatora ani
/// analizatora semantycznego.</para>
/// </summary>
public static class CodeInsightsService
{
    private const int MaxFiles = 2000;
    private const long MaxFileBytes = 1024 * 1024;
    private const int MaxLinesPerFile = 4000;
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(12);

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "bin", "obj", "dist", "build", "out", "target",
        ".vs", ".idea", ".venv", "venv", "__pycache__", "packages", "vendor", "coverage", ".next", ".cache"
    };

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".py"] = "Python", [".js"] = "JavaScript", [".jsx"] = "JavaScript (JSX)",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript (TSX)", [".rs"] = "Rust", [".go"] = "Go",
        [".java"] = "Java", [".kt"] = "Kotlin", [".c"] = "C", [".h"] = "C (nagłówek)", [".cpp"] = "C++",
        [".hpp"] = "C++ (nagłówek)", [".swift"] = "Swift", [".rb"] = "Ruby", [".php"] = "PHP",
        [".sql"] = "SQL", [".sh"] = "Shell", [".ps1"] = "PowerShell", [".xaml"] = "XAML",
        [".html"] = "HTML", [".css"] = "CSS", [".scss"] = "SCSS", [".md"] = "Markdown",
        [".json"] = "JSON", [".yml"] = "YAML", [".yaml"] = "YAML", [".toml"] = "TOML", [".xml"] = "XML"
    };

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";
        var scan = Regex.Match(text, @"^(?:skan kodu|przeskanuj kod|analiza kodu)[:\s]+(.+)$");
        if (scan.Success) return Scan(Payload(raw, "skan kodu", "przeskanuj kod", "analiza kodu"));

        var stats = Regex.Match(text, @"^(?:statystyki kodu|metryki kodu|kod statystyki)[:\s]+(.+)$");
        if (stats.Success) return Statistics(Payload(raw, "statystyki kodu", "metryki kodu", "kod statystyki"));

        var deps = Regex.Match(text, @"^(?:zaleznosci kodu|zaleznosci|graf zaleznosci|importy kodu)[:\s]+(.+)$");
        if (deps.Success) return Dependencies(Payload(raw, "graf zaleznosci", "zaleznosci kodu", "importy kodu", "zaleznosci"));

        var security = Regex.Match(text, @"^(?:bezpieczenstwo kodu|audyt kodu|sast|skan bezpieczenstwa)[:\s]+(.+)$");
        if (security.Success) return Security(Payload(raw, "skan bezpieczenstwa", "audyt kodu", "bezpieczenstwo kodu", "sast"));

        var todos = Regex.Match(text, @"^(?:todo|todo kodu|zadania w kodzie|todo/fixme)[:\s]+(.+)$");
        if (todos.Success) return TodoScan(Payload(raw, "zadania w kodzie", "todo kodu", "todo/fixme", "todo"));

        var duplicates = Regex.Match(text, @"^(?:duplikaty kodu|powtorzenia w kodzie|duplikacja kodu)[:\s]+(.+)$");
        if (duplicates.Success) return Duplicates(Payload(raw, "powtorzenia w kodzie", "duplikaty kodu", "duplikacja kodu"));

        var licenses = Regex.Match(text, @"^(?:licencje|licencje kodu|sprawdz licencje)[:\s]+(.+)$");
        if (licenses.Success) return Licenses(Payload(raw, "sprawdz licencje", "licencje kodu", "licencje"));

        var functions = Regex.Match(text, @"^(?:funkcje kodu|najdluzsze funkcje|zlozonosc kodu)[:\s]+(.+)$");
        if (functions.Success) return Functions(Payload(raw, "najdluzsze funkcje", "funkcje kodu", "zlozonosc kodu"));

        var tree = Regex.Match(text, @"^(?:drzewo kodu|struktura kodu|drzewo)[:\s]+(.+)$");
        if (tree.Success) return Tree(Payload(raw, "struktura kodu", "drzewo kodu", "drzewo"));

        return null;
    }

    private static string Payload(string raw, params string[] prefixes)
    {
        string text = (raw ?? "").Trim();
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = text[prefix.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..];
            return rest.Trim().Trim('"');
        }
        return text;
    }

    internal sealed record SourceFile(string FullPath, string RelativePath, string Language, long Size, string[] Lines);

    /// <summary>Wczytuje pliki źródłowe w limicie czasu i rozmiaru. Tylko odczyt.</summary>
    internal static (List<SourceFile> Files, bool Truncated, bool TimedOut) Load(string root, int maxFiles = MaxFiles)
    {
        var files = new List<SourceFile>();
        bool truncated = false;
        var watch = Stopwatch.StartNew();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            if (watch.Elapsed > TimeLimit) return (files, truncated, true);
            var (directory, depth) = stack.Pop();
            string[] subdirectories;
            string[] entries;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                entries = Directory.GetFiles(directory);
            }
            catch (Exception)
            {
                continue;
            }
            if (depth < 8)
                foreach (string subdirectory in subdirectories)
                    if (!SkipDirectories.Contains(Path.GetFileName(subdirectory))) stack.Push((subdirectory, depth + 1));
            foreach (string entry in entries)
            {
                if (files.Count >= maxFiles) { truncated = true; break; }
                string extension = Path.GetExtension(entry);
                if (!Languages.ContainsKey(extension)) continue;
                var info = new FileInfo(entry);
                if (info.Length > MaxFileBytes) continue;
                try
                {
                    var lines = File.ReadAllLines(entry).Take(MaxLinesPerFile).ToArray();
                    files.Add(new SourceFile(entry, Path.GetRelativePath(root, entry).Replace('\\', '/'),
                        Languages[extension], info.Length, lines));
                }
                catch (Exception) { /* plik nieczytelny — pomijam, nie udaję, że go przeanalizowałem */ }
            }
        }
        return (files, truncated, false);
    }

    private static bool IsComment(string line, string language)
    {
        string trimmed = line.TrimStart();
        if (trimmed.Length == 0) return false;
        if (language is "Python" or "Shell" or "YAML" or "TOML") return trimmed.StartsWith('#');
        if (language is "HTML" or "Markdown" or "XML" or "XAML") return trimmed.StartsWith("<!--") || trimmed.StartsWith("//");
        return trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith('*') || trimmed.StartsWith("///");
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    // ============================================================
    // Skan ogólny
    // ============================================================

    public static string Scan(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root);
        if (files.Count == 0) return "Nie znalazłem plików kodu w " + root + " (obsługiwane rozszerzenia: " + string.Join(", ", Languages.Keys.Take(12)) + " …).";

        int code = 0, comments = 0, blanks = 0;
        var byLanguage = new Dictionary<string, (int Files, int Lines)>(StringComparer.OrdinalIgnoreCase);
        var todos = new List<string>();
        var longestLines = new List<(string File, int Number, int Length)>();
        foreach (var file in files)
        {
            int fileCode = 0, fileComments = 0, fileBlanks = 0;
            for (int index = 0; index < file.Lines.Length; index++)
            {
                string line = file.Lines[index];
                if (IsBlank(line)) fileBlanks++;
                else if (IsComment(line, file.Language)) fileComments++;
                else fileCode++;
                var todo = Regex.Match(line, @"\b(TODO|FIXME|HACK|XXX)\b[:\s]*(.{0,80})");
                if (todo.Success) todos.Add(file.RelativePath + ":" + (index + 1) + " · " + todo.Groups[1].Value + " " + todo.Groups[2].Value.Trim());
                if (line.Length > 160) longestLines.Add((file.RelativePath, index + 1, line.Length));
            }
            code += fileCode;
            comments += fileComments;
            blanks += fileBlanks;
            byLanguage.TryGetValue(file.Language, out var current);
            byLanguage[file.Language] = (current.Files + 1, current.Lines + file.Lines.Length);
        }

        var linesOut = new List<string>
        {
            "Skan kodu: " + root,
            "· plików: " + files.Count + " · linii: " + (code + comments + blanks).ToString("N0", CultureInfo.GetCultureInfo("pl-PL")) +
                " (kod " + code.ToString("N0") + ", komentarze " + comments.ToString("N0") + ", puste " + blanks.ToString("N0") + ")",
            "· języki: " + string.Join(", ", byLanguage.OrderByDescending(x => x.Value.Lines).Take(6)
                .Select(x => x.Key + " " + x.Value.Files + " plików/" + x.Value.Lines + " linii")),
            "· TODO/FIXME/HACK: " + todos.Count + (todos.Count > 0 ? " (szczegóły: „todo: " + root + "”)" : ""),
            "· linie dłuższe niż 160 znaków: " + longestLines.Count,
            "· największe pliki: " + string.Join(", ", files.OrderByDescending(x => x.Size).Take(3)
                .Select(x => x.RelativePath + " (" + (x.Size / 1024) + " KiB)"))
        };
        if (truncated) linesOut.Add("· UWAGA: przeanalizowałem pierwsze " + MaxFiles + " plików — to nie jest pełny skan.");
        if (timedOut) linesOut.Add("· UWAGA: przerwałem po " + TimeLimit.TotalSeconds + " s — to nie jest pełny skan.");
        linesOut.Add("· To analiza tekstowa (bez kompilatora): liczniki i wzorce, nie wnioski o poprawności kodu.");
        return string.Join(Environment.NewLine, linesOut);
    }

    public static string Statistics(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root);
        if (files.Count == 0) return "Brak plików kodu w " + root + ".";
        var rows = files.GroupBy(x => x.Language)
            .Select(group => new
            {
                Language = group.Key,
                Files = group.Count(),
                Lines = group.Sum(x => x.Lines.Length),
                Code = group.Sum(x => x.Lines.Count(line => !IsBlank(line) && !IsComment(line, x.Language))),
                Comments = group.Sum(x => x.Lines.Count(line => IsComment(line, x.Language)))
            })
            .OrderByDescending(x => x.Code).ToArray();
        var lines = new List<string> { "Statystyki kodu (posortowane po liniach kodu):", "język · pliki · linie kodu · komentarze · udział komentarzy" };
        foreach (var row in rows)
        {
            double ratio = row.Code + row.Comments == 0 ? 0 : row.Comments * 100.0 / (row.Code + row.Comments);
            lines.Add("· " + row.Language + " · " + row.Files + " · " + row.Code.ToString("N0") + " · " + row.Comments.ToString("N0") +
                " · " + ratio.ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + "%");
        }
        int totalCode = rows.Sum(x => x.Code);
        lines.Add("· razem linii kodu: " + totalCode.ToString("N0"));
        if (truncated) lines.Add("· UWAGA: limit " + MaxFiles + " plików — to nie jest pełny skan.");
        if (timedOut) lines.Add("· UWAGA: przerwane po limicie czasu.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string TodoScan(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root);
        var items = new List<string>();
        foreach (var file in files)
        {
            for (int index = 0; index < file.Lines.Length; index++)
            {
                var match = Regex.Match(file.Lines[index], @"\b(TODO|FIXME|HACK|XXX)\b[:\s]*(.{0,100})");
                if (match.Success)
                    items.Add("· " + file.RelativePath + ":" + (index + 1) + " [" + match.Groups[1].Value + "] " + match.Groups[2].Value.Trim());
            }
        }
        if (items.Count == 0) return "Nie znalazłem oznaczeń TODO/FIXME/HACK w " + root + " (" + files.Count + " plików). To nie znaczy, że nie ma pracy do zrobienia — tylko że nikt jej tu nie oznaczył.";
        var summary = items.GroupBy(x => Regex.Match(x, @"\[(\w+)\]").Groups[1].Value)
            .Select(g => g.Key + ": " + g.Count());
        return "Oznaczenia w kodzie (" + items.Count + " · " + string.Join(", ", summary) + "):" + Environment.NewLine +
            string.Join(Environment.NewLine, items.Take(60)) +
            (items.Count > 60 ? Environment.NewLine + "… i " + (items.Count - 60) + " więcej." : "") +
            (truncated ? Environment.NewLine + "· UWAGA: limit " + MaxFiles + " plików." : "") +
            (timedOut ? Environment.NewLine + "· UWAGA: przerwane po limicie czasu." : "");
    }

    // ============================================================
    // Zależności
    // ============================================================

    public static string Dependencies(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root, 800);
        if (files.Count == 0) return "Brak plików kodu w " + root + ".";

        var internalModules = files.Select(x => x.RelativePath.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var external = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string module = file.RelativePath.Contains('/') ? file.RelativePath.Split('/')[0] : Path.GetFileNameWithoutExtension(file.RelativePath);
            foreach (string dependency in ExtractImports(file))
            {
                if (dependency.StartsWith('.') || dependency.StartsWith('/'))
                {
                    string? target = ResolveRelative(file.RelativePath, dependency);
                    if (target is not null)
                    {
                        if (!edges.TryGetValue(module, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); edges[module] = set; }
                        set.Add(target);
                    }
                    else unresolved[$"{file.RelativePath} → {dependency}"] = 1;
                    continue;
                }
                string head = dependency.Split('.')[0].Split('/')[0];
                if (internalModules.Contains(head))
                {
                    if (!edges.TryGetValue(module, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); edges[module] = set; }
                    set.Add(head);
                }
                else
                {
                    external.TryGetValue(head, out int count);
                    external[head] = count + 1;
                }
            }
        }

        var cycles = FindCycles(edges);
        var lines = new List<string>
        {
            "Zależności kodu: " + root + " · plików: " + files.Count,
            "· moduły wewnętrzne: " + edges.Count + " z połączeniami (krawędzie: " + edges.Sum(x => x.Value.Count) + ")",
            "· zależności zewnętrzne: " + external.Count + (external.Count > 0 ? " — najczęstsze: " +
                string.Join(", ", external.OrderByDescending(x => x.Value).Take(8).Select(x => x.Key + " (" + x.Value + ")")) : ""),
            "· cykle: " + (cycles.Count == 0 ? "brak" : cycles.Count.ToString()),
            "· najbardziej zależne moduły: " + (edges.Count == 0 ? "brak danych" : string.Join(", ",
                edges.OrderByDescending(x => x.Value.Count).Take(5).Select(x => x.Key + " → " + x.Value.Count)))
        };
        lines.AddRange(cycles.Take(8).Select(x => "· CYKL: " + string.Join(" → ", x)));
        if (unresolved.Count > 0) lines.Add("· importy względne, których nie umiem rozwiązać: " + unresolved.Count + " (to mogą być pliki nieindeksowane albo błędy)");
        lines.Add("· Analiza jest tekstowa (bez uruchamiania kompilatora) — kierunek zależności i cykle liczę z importów w plikach.");
        if (truncated) lines.Add("· UWAGA: próg 800 plików.");
        if (timedOut) lines.Add("· UWAGA: przerwane po limicie czasu.");
        return string.Join(Environment.NewLine, lines);
    }

    internal static IEnumerable<string> ExtractImports(SourceFile file)
    {
        var found = new List<string>();
        int scanned = 0;
        foreach (string line in file.Lines)
        {
            if (++scanned > 400) break;
            string trimmed = line.Trim();
            if (file.Language == "C#")
            {
                var match = Regex.Match(trimmed, @"^using\s+([\w\.]+)\s*;");
                if (match.Success) { found.Add(match.Groups[1].Value); continue; }
            }
            if (file.Language == "Python")
            {
                var from = Regex.Match(trimmed, @"^from\s+([\w\.]+)\s+import\s");
                if (from.Success) { found.Add(from.Groups[1].Value); continue; }
                var import = Regex.Match(trimmed, @"^import\s+([\w\.,\s]+)$");
                if (import.Success) { found.AddRange(import.Groups[1].Value.Split(',').Select(x => x.Trim().Split(' ')[0])); continue; }
            }
            if (file.Language.StartsWith("JavaScript", StringComparison.Ordinal) || file.Language.StartsWith("TypeScript", StringComparison.Ordinal))
            {
                var from = Regex.Match(trimmed, @"from\s+['""]([^'""]+)['""]");
                if (from.Success) { found.Add(from.Groups[1].Value); continue; }
                var require = Regex.Match(trimmed, @"require\(\s*['""]([^'""]+)['""]\s*\)");
                if (require.Success) { found.Add(require.Groups[1].Value); continue; }
            }
            if (file.Language == "Go")
            {
                var goImport = Regex.Match(trimmed, @"^\s*[\w\.]*\s*[""]([^""]+)[""]\s*$");
                if (goImport.Success && trimmed.Contains('"')) found.Add(goImport.Groups[1].Value);
            }
            if (file.Language == "Rust")
            {
                var rustUse = Regex.Match(trimmed, @"^use\s+([\w:]+)");
                if (rustUse.Success) found.Add(rustUse.Groups[1].Value.Split("::")[0]);
            }
        }
        return found.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveRelative(string fromRelative, string dependency)
    {
        string directory = Path.GetDirectoryName(fromRelative)?.Replace('\\', '/') ?? "";
        var segments = new List<string>(directory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (string part in dependency.Split('/'))
        {
            if (part is "." or "") continue;
            if (part == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); continue; }
            segments.Add(part);
        }
        return segments.Count > 0 ? segments[0] : null;
    }

    internal static List<List<string>> FindCycles(Dictionary<string, HashSet<string>> edges)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string node)
        {
            if (onStack.Contains(node))
            {
                int start = stack.FindIndex(x => string.Equals(x, node, StringComparison.OrdinalIgnoreCase));
                if (start >= 0 && cycles.Count < 20)
                {
                    var cycle = stack.Skip(start).ToList();
                    cycle.Add(node);
                    bool duplicate = cycles.Any(existing => existing.Count == cycle.Count && existing.OrderBy(x => x).SequenceEqual(cycle.OrderBy(x => x)));
                    if (!duplicate) cycles.Add(cycle);
                }
                return;
            }
            if (!visited.Add(node)) return;
            stack.Add(node);
            onStack.Add(node);
            if (edges.TryGetValue(node, out var targets))
                foreach (string target in targets) Visit(target);
            onStack.Remove(node);
            stack.RemoveAt(stack.Count - 1);
        }

        foreach (string node in edges.Keys) Visit(node);
        return cycles;
    }

    // ============================================================
    // SAST (heurystyczny)
    // ============================================================

    private sealed record RiskPattern(string Name, string Severity, string Advice, Regex Pattern);

    private static readonly RiskPattern[] Risks =
    [
        new("dynamiczne wykonanie kodu", "wysokie", "eval/exec przyjmujący dane wejściowe potrafi wykonać dowolny kod — zamień na jawny parser.",
            new Regex(@"(?<![\w\.])eval\s*\(|(?<![\w\.])exec\s*\(", RegexOptions.Compiled)),
        new("wywołanie powłoki", "wysokie", "os.system / shell=True przekazuje treść do powłoki; użyj listy argumentów bez powłoki.",
            new Regex(@"os\.system\s*\(|shell\s*=\s*True|subprocess\.[a-z_]+\([^)]*shell\s*=\s*True", RegexOptions.Compiled)),
        new("wczytanie pickla", "średnie", "pickle.loads na niezaufanych danych pozwala wykonać kod — użyj JSON.",
            new Regex(@"pickle\.loads?\s*\(|yaml\.load\s*\((?![^)]*SafeLoader)", RegexOptions.Compiled)),
        new("słaby skrót", "średnie", "MD5/SHA-1 nie nadają się do haseł ani podpisów — użyj SHA-256+ albo Argon2/bcrypt.",
            new Regex(@"MD5\.|SHA1\.|sha1\(|md5\(|hashlib\.md5", RegexOptions.Compiled)),
        new("prawdopodobny sekret w kodzie", "wysokie", "Klucze i hasła trzymaj w zmiennych środowiskowych albo w sejfie, nie w źródłach.",
            new Regex(@"(?i)(api[_-]?key|secret|password|passwd|token)\s*[:=]\s*[""'][^""'\s]{8,}[""']", RegexOptions.Compiled)),
        new("niezaszyfrowany transport", "średnie", "Adres http:// wysyła dane otwartym tekstem — użyj https://.",
            new Regex(@"http://(?!localhost|127\.0\.0\.1|0\.0\.0\.0)", RegexOptions.Compiled)),
        new("SQL składany tekstem", "wysokie", "Sklejanie zapytań otwiera wstrzyknięcie SQL — używaj parametrów.",
            new Regex(@"(?i)(?:SELECT|INSERT|UPDATE|DELETE)[^;]{0,80}[""']\s*\+|\+\s*[""'][^""']*(?:WHERE|VALUES)", RegexOptions.Compiled)),
        new("klucz prywatny w repozytorium", "krytyczne", "Prywatny klucz w pliku źródłowym trzeba natychmiast unieważnić i przenieść do sejfu.",
            new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----", RegexOptions.Compiled)),
        new("wyłączona weryfikacja certyfikatu", "wysokie", "Ignorowanie błędów TLS pozwala podszyć się pod serwer.",
            new Regex(@"(?i)verify\s*=\s*False|ServerCertificateCustomValidationCallback\s*=\s*[^;]*=>\s*true|InsecureSkipVerify", RegexOptions.Compiled)),
        new("uprawnienia pliku 777", "średnie", "Każdy może zapisać ten plik — zawęź uprawnienia.",
            new Regex(@"chmod\s+(?:0?777|a\+rwx)", RegexOptions.Compiled))
    ];

    public static string Security(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root);
        var findings = new List<(string Severity, string Name, string Location, string Advice)>();
        foreach (var file in files)
        {
            for (int index = 0; index < file.Lines.Length; index++)
            {
                string line = file.Lines[index];
                if (IsComment(line, file.Language)) continue;   // komentarz to nie kod
                foreach (var risk in Risks)
                {
                    if (!risk.Pattern.IsMatch(line)) continue;
                    findings.Add((risk.Severity, risk.Name, file.RelativePath + ":" + (index + 1), risk.Advice));
                }
            }
        }
        if (findings.Count == 0)
            return "Audyt heurystyczny kodu (" + files.Count + " plików): nie znalazłem wzorców z listy ryzyka." + Environment.NewLine +
                "· To NIE jest dowód bezpieczeństwa — to tylko brak trafień w 10 wzorcach tekstowych. Prawdziwy audyt wymaga przeglądu logiki i testów.";
        var lines = new List<string>
        {
            "Audyt heurystyczny kodu (" + files.Count + " plików): " + findings.Count + " trafień",
            "· wagi: " + string.Join(", ", findings.GroupBy(x => x.Severity).OrderByDescending(g => g.Count())
                .Select(g => g.Key + " " + g.Count()))
        };
        foreach (var finding in findings.Take(40))
            lines.Add("· [" + finding.Severity + "] " + finding.Name + " — " + finding.Location + Environment.NewLine + "    → " + finding.Advice);
        if (findings.Count > 40) lines.Add("… i " + (findings.Count - 40) + " dalszych trafień.");
        lines.Add("· Wzorce są tekstowe (bez analizy przepływu danych): trafienie wymaga obejrzenia, brak trafienia niczego nie gwarantuje.");
        if (truncated) lines.Add("· UWAGA: limit " + MaxFiles + " plików.");
        if (timedOut) lines.Add("· UWAGA: przerwane po limicie czasu.");
        return string.Join(Environment.NewLine, lines);
    }

    // ============================================================
    // Duplikaty
    // ============================================================

    public static string Duplicates(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root, 600);
        const int window = 6;
        var blocks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var significant = new List<(int Number, string Text)>();
            for (int index = 0; index < file.Lines.Length; index++)
            {
                string trimmed = file.Lines[index].Trim();
                if (trimmed.Length < 12 || IsComment(file.Lines[index], file.Language)) continue;
                significant.Add((index + 1, Regex.Replace(trimmed, @"\s+", " ")));
            }
            for (int start = 0; start + window <= significant.Count; start++)
            {
                string key = string.Join("\u0001", significant.Skip(start).Take(window).Select(x => x.Text));
                if (!blocks.TryGetValue(key, out var list)) { list = new List<string>(); blocks[key] = list; }
                list.Add(file.RelativePath + ":" + significant[start].Number);
            }
        }
        var groups = blocks.Where(x => x.Value.Count > 1).OrderByDescending(x => x.Value.Count).Take(10).ToArray();
        if (groups.Length == 0) return "Nie znalazłem powtórzonych bloków " + window + " linii w " + files.Count + " plikach.";
        var lines = new List<string> { "Powtórzone bloki (" + window + " znaczących linii): " + groups.Length + " grup" };
        foreach (var group in groups)
        {
            var preview = group.Key.Split('\u0001').First();
            lines.Add("· ×" + group.Value.Count + " — np. „" + (preview.Length > 70 ? preview[..70] + "…" : preview) + "”");
            lines.AddRange(group.Value.Take(4).Select(x => "    " + x));
        }
        lines.Add("· To wykrywanie tekstowe (identyczne linie), nie porównanie semantyczne — podobny kod o innej pisowni nie zostanie pokazany.");
        if (truncated) lines.Add("· UWAGA: próg 600 plików.");
        if (timedOut) lines.Add("· UWAGA: przerwane po limicie czasu.");
        return string.Join(Environment.NewLine, lines);
    }

    // ============================================================
    // Licencje
    // ============================================================

    private static readonly (string Key, string Name)[] LicenseSignatures =
    [
        ("Apache License", "Apache-2.0"),
        ("MIT License", "MIT"),
        ("GNU GENERAL PUBLIC LICENSE", "GPL"),
        ("GNU LESSER GENERAL PUBLIC LICENSE", "LGPL"),
        ("Mozilla Public License", "MPL-2.0"),
        ("BSD", "BSD"),
        ("The Unlicense", "Unlicense"),
        ("Creative Commons", "CC")
    ];

    public static string Licenses(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var licenseFiles = new List<string>();
        var detected = new List<string>();
        foreach (string path in SafeEnumerate(root, "LICENSE*", 3).Concat(SafeEnumerate(root, "COPYING*", 3)).Take(20))
        {
            licenseFiles.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            try
            {
                string head = string.Join("\n", File.ReadAllLines(path).Take(40));
                foreach (var signature in LicenseSignatures)
                    if (head.Contains(signature.Key, StringComparison.OrdinalIgnoreCase)) { detected.Add(signature.Name); break; }
            }
            catch (Exception) { /* nieczytelny plik licencji */ }
        }
        var (files, truncated, _) = Load(root, 1000);
        int withSpdx = 0;
        foreach (var file in files)
        {
            if (file.Lines.Take(5).Any(line => line.Contains("SPDX-License-Identifier", StringComparison.OrdinalIgnoreCase))) withSpdx++;
        }
        var lines = new List<string>
        {
            "Licencje w " + root + ":",
            "· pliki licencji: " + (licenseFiles.Count == 0 ? "nie znalazłem (LICENSE/COPYING)" : string.Join(", ", licenseFiles)),
            "· rozpoznana licencja: " + (detected.Count == 0 ? "nie rozpoznałem typu" : string.Join(", ", detected.Distinct())),
            "· pliki z nagłówkiem SPDX: " + withSpdx + "/" + files.Count,
            "· To nie jest porada prawna ani zgodność licencyjna: wykrywam tekst nagłówka, a nie obowiązki wynikające z licencji."
        };
        if (truncated) lines.Add("· UWAGA: próg 1000 plików.");
        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern, int depth)
    {
        var results = new List<string>();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (directory, current) = stack.Pop();
            try
            {
                results.AddRange(Directory.GetFiles(directory, pattern));
                if (current >= depth) continue;
                foreach (string subdirectory in Directory.GetDirectories(directory))
                    if (!SkipDirectories.Contains(Path.GetFileName(subdirectory))) stack.Push((subdirectory, current + 1));
            }
            catch (Exception) { /* brak dostępu — pomijam */ }
        }
        return results;
    }

    // ============================================================
    // Funkcje i złożoność
    // ============================================================

    public static string Functions(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var (files, truncated, timedOut) = Load(root, 800);
        var functions = new List<(string Location, string Name, int Length, int Complexity, string Language)>();
        foreach (var file in files)
        {
            if (file.Language is "JSON" or "YAML" or "Markdown" or "XML" or "HTML" or "CSS" or "XAML" or "TOML") continue;
            var declarations = new List<(int Line, string Name, int Indent)>();
            for (int index = 0; index < file.Lines.Length; index++)
            {
                string line = file.Lines[index];
                if (IsComment(line, file.Language)) continue;
                var declaration = Regex.Match(line, @"^\s*(?:public|private|protected|internal|static|async|virtual|override|sealed|partial|export|default|final|pub|func|fn|def|function|void|int|string|bool|double|Task|List)\b[^;=]*\b([A-Za-z_]\w*)\s*\([^;]*$");
                if (declaration.Success && !line.TrimEnd().EndsWith(';'))
                    declarations.Add((index, declaration.Groups[1].Value, Indent(line)));
            }
            for (int position = 0; position < declarations.Count; position++)
            {
                var (line, name, indent) = declarations[position];
                int end = file.Lines.Length;
                if (file.Language == "Python")
                {
                    for (int index = line + 1; index < file.Lines.Length; index++)
                    {
                        if (file.Lines[index].Trim().Length == 0) continue;
                        if (Indent(file.Lines[index]) <= indent) { end = index; break; }
                    }
                }
                else
                {
                    int depth = 0;
                    bool started = false;
                    for (int index = line; index < file.Lines.Length; index++)
                    {
                        foreach (char character in file.Lines[index])
                        {
                            if (character == '{') { depth++; started = true; }
                            else if (character == '}') depth--;
                        }
                        if (started && depth <= 0) { end = index + 1; break; }
                    }
                }
                int length = end - line;
                int complexity = 1;
                for (int index = line; index < Math.Min(end, file.Lines.Length); index++)
                {
                    string body = file.Lines[index];
                    if (IsComment(body, file.Language)) continue;
                    complexity += Regex.Matches(body, @"\b(if|else if|elif|for|foreach|while|case|catch|when|switch)\b").Count;
                    complexity += Regex.Matches(body, @"&&|\|\||\?\?").Count;
                }
                functions.Add((file.RelativePath + ":" + (line + 1), name, length, complexity, file.Language));
            }
        }
        if (functions.Count == 0) return "Nie rozpoznałem żadnej deklaracji funkcji w " + files.Count + " plikach (to heurystyka tekstowa).";
        var ordered = functions.OrderByDescending(x => x.Length).Take(12).ToArray();
        var complex = functions.OrderByDescending(x => x.Complexity).Take(5).ToArray();
        var lines = new List<string>
        {
            "Funkcje: rozpoznane " + functions.Count + " deklaracji · najdłuższe:",
        };
        lines.AddRange(ordered.Select(x => "· " + x.Location + " — " + x.Name + " · " + x.Length + " linii · złożoność ~" + x.Complexity));
        lines.Add("Najwyższa złożoność (przybliżenie liczbą rozgałęzień):");
        lines.AddRange(complex.Select(x => "· " + x.Location + " — " + x.Name + " · ~" + x.Complexity + " punktów decyzyjnych, " + x.Length + " linii"));
        lines.Add("· Heurystyka: deklaracje i rozgałęzienia liczone z tekstu. Długość funkcji zależy od formatowania — traktuj to jako wskazówkę, nie pomiar kompilatora.");
        if (truncated) lines.Add("· UWAGA: próg 800 plików.");
        if (timedOut) lines.Add("· UWAGA: przerwane po limicie czasu.");
        return string.Join(Environment.NewLine, lines);
    }

    private static int Indent(string line)
    {
        int count = 0;
        foreach (char character in line)
        {
            if (character == ' ') count++;
            else if (character == '\t') count += 4;
            else break;
        }
        return count;
    }

    // ============================================================
    // Drzewo
    // ============================================================

    public static string Tree(string root)
    {
        if (!Directory.Exists(root)) return "Nie ma katalogu: " + root;
        var lines = new List<string> { "Struktura: " + root };
        void Walk(string directory, int depth)
        {
            if (depth > 3 || lines.Count > 120) return;
            string[] subdirectories, entries;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                entries = Directory.GetFiles(directory);
            }
            catch (Exception) { return; }
            var codeFiles = entries.Where(x => Languages.ContainsKey(Path.GetExtension(x))).ToArray();
            if (codeFiles.Length > 0 || depth == 0)
                lines.Add(new string(' ', depth * 2) + "· " + Path.GetFileName(directory) + "/ — plików kodu: " + codeFiles.Length +
                    (codeFiles.Length > 0 ? " · " + string.Join(", ", Languages.Keys.Where(k => codeFiles.Any(f => f.EndsWith(k, StringComparison.OrdinalIgnoreCase))).Take(4)) : ""));
            foreach (string subdirectory in subdirectories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(subdirectory);
                if (SkipDirectories.Contains(name)) continue;
                Walk(subdirectory, depth + 1);
            }
        }
        Walk(root, 0);
        lines.Add("(głębokość do 3 poziomów, pomijam katalogi techniczne: node_modules, .git, bin, obj, dist, target…)");
        return string.Join(Environment.NewLine, lines);
    }
}
