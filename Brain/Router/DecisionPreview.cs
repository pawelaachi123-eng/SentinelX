using System.Text;
using System.Text.RegularExpressions;
using SentinelX.Core;

namespace SentinelX;

/// <summary>0.96 · KUŹNIA — „jak to rozumiem: …”. Shows, in plain Polish, what Sentinel would do with a
/// command: typo or abbreviation repair, a tool from the catalogue, a known command, a question it would ask,
/// or a plain question for the local model. It is a pure function over the catalogues: it never executes
/// anything, writes nothing (a QR preview creates no PNG), reads no file and does not call the AI model.</summary>
public static class DecisionPreview
{
    private const string Usage = "Napisz „jak to rozumiem: <polecenie>”, np. „jak to rozumiem: kwota slownie: 1234,56” — pokażę, co bym zrobił, ale niczego nie wykonam.";

    private static readonly Regex Trigger = new(@"^jak to rozumiem\s*:\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>Stems of commands that change the machine or the data. A typo must never steer towards them.</summary>
    private static readonly string[] RiskyStems = ["usun", "skasuj", "wyczysc", "zamknij", "zabij", "wylacz", "sformatuj", "zatrzymaj"];

    private static readonly (ToolEntry Entry, string Stem, bool ExactOnly)[] ToolIndex = ToolCatalog.Entries.Select(entry =>
    {
        string prefix = ConversationMemoryService.Normalize(entry.Preview).TrimEnd(':').Trim();
        // „policz 12,5*4 + …” is a whole sample command: only its leading words identify the tool.
        string stem = Regex.Replace(prefix, @"\s+[\d(\-].*$", "").Trim();
        bool exact = !entry.TakesArgument && stem == prefix;
        return (entry, exact ? prefix : stem, exact);
    }).ToArray();

    /// <summary>Returns the preview when the input is a „jak to rozumiem: …” request, otherwise null.</summary>
    public static string? TryExplain(string input)
    {
        string trimmed = (input ?? "").Trim();
        if (trimmed.Length == 0) return null;
        string bare = ConversationMemoryService.Normalize(trimmed).TrimEnd('?', '!', '.', ' ', ':');
        if (bare == "jak to rozumiem") return Usage;
        Match match;
        try { match = Trigger.Match(trimmed); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!match.Success) return null;
        string subject = match.Groups["rest"].Value.Trim();
        return subject.Length == 0 ? Usage : Explain(subject);
    }

    public static string Explain(string subject)
    {
        string trimmed = (subject ?? "").Trim();
        var result = new StringBuilder();
        result.Append("JAK TO ROZUMIEM: „").Append(Shorten(trimmed, 120)).AppendLine("”");
        if (Trigger.IsMatch(trimmed))
        {
            result.Append("· To też prośba o podgląd — podglądów nie zagnieżdżam.");
            return result.ToString();
        }
        var repair = CommandUnderstanding.Repair(trimmed);
        string effective = repair.Success ? repair.Text : trimmed;
        if (repair.Success)
            result.Append("· Literówka lub skrót: zrozumiałbym to jako „").Append(repair.Canonical).Append("” (").Append(repair.Summary).AppendLine(").");
        string normalized = ConversationMemoryService.Normalize(effective).TrimEnd('?', '!', '.', ' ');
        result.Append("· ").AppendLine(Classify(normalized));
        result.Append("To tylko podgląd: niczego nie wykonałem, niczego nie zapisałem i nie pytałem modelu AI.");
        return result.ToString();
    }

    private static string Classify(string normalized)
    {
        if (normalized.Length == 0) return "Pusty tekst — nic bym nie zrobił.";
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return "Skrót palety „//”: zamieniłbym go na polecenie z listy (wpisz samo „//”, aby ją zobaczyć).";
        if (RiskyStems.Any(stem => normalized.StartsWith(stem, StringComparison.Ordinal)))
            return "Polecenie, które zmienia stan komputera albo danych. Wymaga świadomej zgody w oknie potwierdzenia (głos jej nie udziela), a takich poleceń nie zgaduję z literówek.";

        ToolEntry? tool = FindTool(normalized);
        if (tool != null) return DescribeTool(tool, normalized);

        if (Regex.IsMatch(normalized, @"^(?:szukaj w zadaniach|szukaj zadan(?:ia)?|znajdz zadanie)(?:[:\s]|$)"))
            return "Szukanie w zadaniach i przypomnieniach: tylko odczyt. Pokażę pasujące wpisy (także zrobione); każde słowo frazy musi w nich wystąpić. Format: „szukaj w zadaniach: fraza”.";
        if (Regex.IsMatch(normalized, @"^(?:dodaj|zrob|nowe) zadanie(?:[:\s]|$)"))
            return "Dodanie zadania: wykonanie zapisze je w Centrum → Zadania (termin z tekstu, jeśli go rozpoznam).";
        if (Regex.IsMatch(normalized, @"^przypomnij(?:\s|$)"))
            return "Przypomnienie: najpierw zapytam o zgodę („tak” albo „nie”) i dopiero po „tak” zapiszę przypomnienie.";

        string? phrase = IntentCatalog.Phrases
            .Where(known => normalized == known || normalized.StartsWith(known + " ", StringComparison.Ordinal) || normalized.StartsWith(known + ":", StringComparison.Ordinal))
            .OrderByDescending(known => known.Length)
            .FirstOrDefault();
        if (phrase != null) return "Znane polecenie „" + phrase + "” — obsłużę je lokalnie, bez modelu AI.";

        var suggestions = CommandUnderstanding.Suggest(normalized);
        if (suggestions.Count > 0)
            return "Nie jestem pewien. Zapytałbym, czy chodzi o: " + string.Join(" · ", suggestions.Select(x => "„" + x + "”")) + " — i nie wykonałbym nic bez Twojego „tak”.";
        return "Nie rozpoznaję tego jako polecenia. Poszłoby do lokalnego modelu AI jako zwykłe pytanie (model niczego nie wykonuje).";
    }

    private static string DescribeTool(ToolEntry tool, string normalized)
    {
        var text = new StringBuilder();
        text.Append("Narzędzie „").Append(tool.Title).Append("” (").Append(tool.Category).Append(") — ").Append(tool.Hint.TrimEnd('.')).Append('.');
        text.Append(tool.Id switch
        {
            "qr" or "qrwifi" => " Uwaga: wykonanie zapisze plik PNG w folderze danych Sentinela — podgląd niczego nie zapisuje.",
            "filehash" => " Wykonanie odczyta wskazany plik (tylko odczyt).",
            "duplicates" or "clutter" => " Wykonanie przeczyta wskazany folder (tylko odczyt, niczego nie usuwa).",
            "openfolder" => " Wykonanie otworzy folder w Eksploratorze.",
            "snapshot" => " Wykonanie zapisze odczyt stanu komputera.",
            "obj-model" => " Wykonanie wygeneruje nowy plik OBJ w folderze danych SentinelX\\CreatedModels (bez nadpisywania i bez uruchamiania programu 3D); podgląd niczego nie zapisuje.",
            "roblox-game-project" => " Wykonanie utworzy lokalne źródła Luau, place .rbxlx i ZIP; jeśli Blender oraz Roblox Studio są zainstalowane, spróbuje wygenerować asset i otworzyć place. Nie opublikuje gry. Podgląd niczego nie zapisuje.",
            "roblox-code" => " Wykonanie pokaże gotowy tekst szablonu do wklejenia; kod nie zostanie uruchomiony ani opublikowany.",
            "luau-audit" => " Wykonanie zrobi heurystyczny przegląd tekstu, nie kompilację ani wykonanie kodu.",
            _ => " Wszystko dzieje się lokalnie, nic nie jest wysyłane."
        });
        string stem = ToolIndex.First(item => item.Entry == tool).Stem;
        if (tool.TakesArgument && (normalized == stem || normalized == stem + ":"))
            text.Append(" Brakuje argumentu — przykład: „").Append(ToolCatalog.BuildCommand(tool, tool.Example)).Append("”.");
        return text.ToString();
    }

    internal static ToolEntry? FindTool(string normalized)
    {
        ToolEntry? best = null;
        int bestLength = -1;
        foreach (var (entry, stem, exactOnly) in ToolIndex)
        {
            bool hit = exactOnly
                ? normalized == stem
                : normalized == stem || normalized.StartsWith(stem + ":", StringComparison.Ordinal) || normalized.StartsWith(stem + " ", StringComparison.Ordinal);
            if (hit && stem.Length > bestLength) { best = entry; bestLength = stem.Length; }
        }
        return best;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
