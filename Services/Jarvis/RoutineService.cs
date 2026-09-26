using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using SentinelX.Services.Actions;

namespace SentinelX.Services.Jarvis;

/// <summary>Nazwana sekwencja poleceń (backlog P0 „sekwencje działań”). Kroki to zwykłe polecenia —
/// dokładnie to, co użytkownik mógłby wpisać, nic więcej.</summary>
public sealed record Routine(string Id, string Name, IReadOnlyList<string> Steps, DateTime CreatedAt, DateTime? LastRunAt, string LastRunSummary)
{
    public string ShortSummary => LastRunSummary.Length == 0 ? "nigdy nie uruchomiona" : "ostatni raz: " + LastRunSummary;
}

public sealed record RoutineResult(bool Success, string Message, string Evidence)
{
    public static RoutineResult Ok(string message, string evidence = "") => new(true, message, evidence);
    public static RoutineResult Bad(string message, string evidence = "") => new(false, message, evidence);
}

/// <summary>
/// 0.96 · sekwencje: nazwane listy kroków z podglądem PRZED wykonaniem, statusem każdego kroku
/// i zatrzymaniem na błędzie. Trzy decyzje projektowe, świadomie konserwatywne:
///  • krok to zdanie z katalogu poleceń — nie kod, nie skrypt, nie „co model wymyślił”;
///  • przy kroku, który wymaga zgody, sekwencja STAJE i mówi o co chodzi (zgoda zostaje kliknięciem);
///  • kroków niszczących nie da się zapisać („usuń”, „skasuj”, „zamknij”, „wyłącz komputer”,
///    „potwierdź”) — tak jak katalog napraw literówek nie zawiera takich poleceń.
/// Magazyn ma ten sam zestaw gwarancji co reszta plików: zapis atomowy, odczyt zwrotny,
/// kopia uszkodzonego pliku zamiast nadpisania.
/// </summary>
public sealed class RoutineService
{
    internal const int MaxRoutines = 40;
    internal const int MaxSteps = 12;
    internal const int MaxStepCharacters = 200;
    private const int SchemaVersion = 1;

    /// <summary>Cokolwiek niszczącego albo udającego zgodę — odrzucane przy zapisie i przy odczycie pliku.</summary>
    internal static readonly string[] Forbidden =
    [
        "usun", "usuń", "skasuj", "wyczyść", "wyczysc", "formatuj", "nadpisz", "zamknij", "wylacz", "wyłącz",
        "zabij", "potwierdz", "potwierdź", "reboot", "shutdown",
    ];

    private readonly string storePath;
    private readonly object gate = new();
    private List<Routine>? loaded;

    public RoutineService(string? memoryDirectory = null) =>
        storePath = Path.Combine(memoryDirectory ?? AppPaths.MemoryDirectory, "routines.json");

    public string StorePath => storePath;
    public string? LastStorageError { get; private set; }

    public IReadOnlyList<Routine> All() { lock (gate) return (loaded ??= Load()).ToArray(); }

    public Routine? Find(string name)
    {
        string needle = ConversationMemoryService.Normalize(name);
        if (needle.Length == 0) return null;
        var items = All();
        return items.FirstOrDefault(x => ConversationMemoryService.Normalize(x.Name) == needle)
            ?? items.FirstOrDefault(x => ConversationMemoryService.Normalize(x.Name).Contains(needle, StringComparison.Ordinal));
    }

    public RoutineResult Create(string name, string stepsText)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 60) return RoutineResult.Bad("Nazwa sekwencji musi mieć od 1 do 60 znaków.");
        IReadOnlyList<string> steps = SplitSteps(stepsText);
        if (steps.Count == 0) return RoutineResult.Bad("Podaj kroki rozdzielone średnikiem, np.: utwórz sekwencję: poranek = który jest dzień; plan dnia; ile mam ramu");
        if (steps.Count > MaxSteps) return RoutineResult.Bad($"Maksymalnie {MaxSteps} kroków w sekwencji (jest {steps.Count}).");

        foreach (string step in steps)
        {
            string? refusal = RefuseIfDangerous(step);
            if (refusal != null) return RoutineResult.Bad(refusal);
        }
        if (Find(name) != null) return RoutineResult.Bad($"Sekwencja „{name}” już istnieje. Usuń ją („usuń sekwencję: {name}”) albo wybierz inną nazwę.");

        var items = new List<Routine>(All());
        if (items.Count >= MaxRoutines) return RoutineResult.Bad($"Limit {MaxRoutines} sekwencji jest osiągnięty. Usuń którąś przed dodaniem następnej.");
        var routine = new Routine(Guid.NewGuid().ToString("N")[..12], name, steps, DateTime.Now, null, "");
        items.Add(routine);
        return Save(items, $"Zapisano sekwencję „{name}” ({steps.Count} kroków).\nPodgląd: {Preview(routine)}\nUruchom: „uruchom sekwencję: {name}”.") ;
    }

    public RoutineResult Delete(string name)
    {
        Routine? routine = Find(name);
        if (routine == null) return RoutineResult.Bad($"Nie ma sekwencji „{name}”. Lista: „sekwencje”.");
        var items = All().Where(x => x.Id != routine.Id).ToList();
        return Save(items, $"Usunąłem sekwencję „{routine.Name}”. Kroki nie były wykonane, więc nic poza listą się nie zmieniło.");
    }

    /// <summary>Podgląd bez wykonania — to ta sama numeracja, którą zobaczysz przy uruchomieniu.</summary>
    public static string Preview(Routine routine) =>
        "Podgląd „" + routine.Name + "” (" + routine.Steps.Count + " kroków):\n" +
        string.Join("\n", routine.Steps.Select((step, index) => $"{index + 1}. {step}")) +
        "\nŻadnego kroku nie wykonałem.";

    /// <summary>Uruchamia krok po kroku przez pełen potok (StepRunner). Błąd albo oczekująca zgoda = stop.</summary>
    public async Task<RoutineResult> RunAsync(Routine routine, StepRunner runner, Action<string>? onProgress, CancellationToken token)
    {
        if (!runner.IsReady)
            return RoutineResult.Bad("Kolejka wykonań nie jest podłączona — sekwencje działają w głównym oknie Sentinel.");
        var report = new StringBuilder();
        int done = 0, failed = 0;
        report.Append($"▶ Sekwencja „{routine.Name}” — {routine.Steps.Count} kroków\n");
        for (int index = 0; index < routine.Steps.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            string step = routine.Steps[index];
            string? refusal = RefuseIfDangerous(step);
            if (refusal != null)
            {
                failed++;
                report.Append($"\n⛔ {index + 1}. {step}\n   {refusal}\n   Sekwencja zatrzymana — pozostałych {routine.Steps.Count - index - 1} kroków NIE zostało wysłane.");
                break;
            }
            onProgress?.Invoke($"\n· krok {index + 1}/{routine.Steps.Count}: {step}");
            StepOutcome outcome = await runner.RunAsync(step, token);
            if (outcome.Status.Equals("WAITING_PERMISSION", StringComparison.OrdinalIgnoreCase))
            {
                failed++;
                report.Append($"\n⏸ {index + 1}. {step}\n   Krok czeka na Twoją zgodę w oknie (Centrum → Akcje). Sekwencja staje: zgody nie podejmuje automat i nie podejmuje jej głos.");
                break;
            }
            if (!outcome.Success)
            {
                failed++;
                report.Append($"\n✖ {index + 1}. {step}\n   {(outcome.Text.Length > 0 ? outcome.Text : "krok odrzucony")} · status: {outcome.Status}\n" +
                    $"   Sekwencja zatrzymana na błędzie — pozostałych {routine.Steps.Count - index - 1} kroków NIE zostało wysłane.");
                break;
            }
            done++;
            string evidence = outcome.Verified ? "zweryfikowany dowodem" : "bez potwierdzenia";
            report.Append($"\n✔ {index + 1}. {step} — {evidence}" + (outcome.Text.Length > 0 ? "\n   " + Summarize(outcome.Text) : ""));
        }
        string summary = $"{done} wykonanych, {failed} problematycznych";
        report.Append($"\n\nKoniec sekwencji: {summary}. Wszystkie kroki (także udane) NIE są cofane automatycznie.");
        var items = All().Select(x => x.Id == routine.Id ? x with { LastRunAt = DateTime.Now, LastRunSummary = summary } : x).ToList();
        Save(items, "");
        return new RoutineResult(failed == 0, report.ToString(), summary);
    }

    public string Describe()
    {
        var items = All();
        if (items.Count == 0)
            return "Nie mam żadnych sekwencji. Utwórz: „utwórz sekwencję: poranek = który jest dzień; plan dnia; ile mam ramu”.\n" +
                "Sekwencja to nazwane kroki z podglądem — przy błędzie staje, a zgody nie wykonuje sama.";
        return "SEKWENCJE (" + items.Count + "):\n" + string.Join("\n", items.Select((x, i) =>
        {
            // Streść kroki POZA interpolacją: dwukropek w ternary kończyłby wyrażenie w stringu.
            string trail = x.Steps.Count <= 3 ? string.Join(" → ", x.Steps) : string.Join(" → ", x.Steps.Take(2)) + " → …";
            return $"{i + 1}. {x.Name} — {x.Steps.Count} kroków ({trail}) · {x.ShortSummary}";
        }));
    }

    // ------------------------------------------------------------------ czyste funkcje

    /// <summary>Kroki dzielimy TYLKO po średniku i nowym wierszu — „policz 2+2” zostaje całe.</summary>
    public static IReadOnlyList<string> SplitSteps(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parts = new List<string>();
        foreach (string raw in Regex.Split(text, @"[;\n\r]+"))
        {
            string step = raw.Trim().Trim('-', '•', ' ');
            if (step.Length == 0) continue;
            if (step.Length > MaxStepCharacters) step = step[..MaxStepCharacters];
            parts.Add(step);
            if (parts.Count >= MaxSteps) break;
        }
        return parts;
    }

    /// <summary>Zwraca powód odmowy albo null, gdy krok jest dozwolony.</summary>
    public static string? RefuseIfDangerous(string step)
    {
        string normalized = ConversationMemoryService.Normalize(step ?? "");
        if (normalized.Length == 0) return "Pusty krok — nie ma czego wykonać.";
        foreach (string forbidden in Forbidden)
        {
            string word = forbidden;
            if (ContainsWord(normalized, word))
                return $"Krok „{step}” zawiera słowo „{word}”. Sekwencje nie mogą zawierać poleceń niszczących ani udawania zgody — " +
                    "wykonaj taki krok osobno, z okna, i zobacz, o co prosi system.";
        }
        return null;
    }

    /// <summary>Czy normalizowany tekst zawiera dany wyraz jako osobne słowo (prefiks fleksji dozwolony).</summary>
    private static bool ContainsWord(string normalized, string word)
    {
        foreach (string token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token == word || token.StartsWith(word, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Summarize(string text)
    {
        string single = text.Replace("\n", " ", StringComparison.Ordinal).Trim();
        return single.Length <= 220 ? single : single[..220] + "…";
    }

    // ------------------------------------------------------------------ magazyn

    private List<Routine> Load()
    {
        try
        {
            LastStorageError = null;
            if (!File.Exists(storePath)) return [];
            using var document = JsonDocument.Parse(File.ReadAllText(storePath));
            var items = Read(document.RootElement);
            // Filtrowanie przy odczycie: plik można otworzyć w edytorze, a sekwencja z „usuń …”
            // nie może przez to trafić do wykonania.
            var safe = items.Where(x => x.Steps.All(step => RefuseIfDangerous(step) == null)).ToList();
            int dropped = items.Count - safe.Count;
            if (dropped > 0) LastStorageError = $"Z listy sekwencji odłożyłem {dropped} pozycj" + (dropped == 1 ? "ę" : "e") + " z krokami niszczącymi.";
            return safe;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        { ParkCorrupt(); LastStorageError = "Nie udało się odczytać listy sekwencji: " + ex.Message; return []; }
    }

    public static List<Routine> Read(JsonElement root)
    {
        var items = new List<Routine>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("routines", out var list) || list.ValueKind != JsonValueKind.Array) return items;
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string name = Text(item, "name");
            var steps = new List<string>();
            if (item.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array)
                foreach (JsonElement step in stepsElement.EnumerateArray())
                    if (step.ValueKind == JsonValueKind.String) steps.Add(step.GetString() ?? "");
            steps.RemoveAll(x => x.Trim().Length == 0);
            if (name.Length == 0 || steps.Count == 0) continue;
            DateTime created = ParseDate(Text(item, "createdAt"));
            DateTime? lastRun = ParseDateNullable(Text(item, "lastRunAt"));
            items.Add(new Routine(Text(item, "id").Length > 0 ? Text(item, "id") : name, name, steps, created, lastRun, Text(item, "lastRunSummary")));
        }
        return items;
    }

    private RoutineResult Save(List<Routine> items, string successMessage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            string payload = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                routines = items.Select(x => new
                {
                    id = x.Id, name = x.Name, steps = x.Steps, createdAt = x.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                    lastRunAt = x.LastRunAt?.ToString("O", CultureInfo.InvariantCulture) ?? "", lastRunSummary = x.LastRunSummary,
                }),
            }, new JsonSerializerOptions { WriteIndented = true });
            string temp = storePath + ".tmp";
            File.WriteAllText(temp, payload);
            using (var verify = JsonDocument.Parse(File.ReadAllText(temp)))
                if (Read(verify.RootElement).Count != items.Count)
                {
                    File.Delete(temp);
                    LastStorageError = "Lista sekwencji nie przeżyła odczytu zwrotnego — stary plik zostaje nietknięty.";
                    return RoutineResult.Bad(LastStorageError);
                }
            if (File.Exists(storePath)) File.Copy(storePath, storePath + ".previous", true);
            File.Move(temp, storePath, true);
            lock (gate) loaded = items;
            LastStorageError = null;
            return RoutineResult.Ok(successMessage, $"{storePath} · {items.Count} sekwencji · {DateTimeOffset.Now:O}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { LastStorageError = "Nie udało się zapisać listy sekwencji: " + ex.Message; return RoutineResult.Bad(LastStorageError); }
    }

    private void ParkCorrupt()
    {
        try
        {
            if (!File.Exists(storePath)) return;
            string parked = storePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int suffix = 1;
            while (File.Exists(parked)) parked = storePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix++;
            File.Move(storePath, parked, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTime ParseDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed) ? parsed : DateTime.Now;
    private static DateTime? ParseDateNullable(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed) ? parsed : null;
}
