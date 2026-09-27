using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SentinelX;

/// <summary>Jedna rutyna: nazwa i lista kroków. Krok to zwykłe polecenie, które wpisałbyś do czatu
/// („otwórz chrome”, „plan dnia”, „zadania”) — rutyna wykonuje je po kolei.</summary>
public sealed class RoutineRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Steps { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Wbudowana (startowa) rutyna — można ją zmienić lub usunąć jak każdą inną.</summary>
    public bool BuiltIn { get; set; }
}

public sealed class RoutineState
{
    public int Version { get; set; } = 1;
    public List<RoutineRecord> Routines { get; set; } = [];
}

/// <summary>0.96 · rutyny (sceny): „poranek”, „praca”, „koniec dnia” — jedno polecenie odpala
/// umówioną sekwencję kroków. To ta część Jarvisa, której nie da się mieć bez własnych scen.
/// <para>Gwarancje takie jak w pamięci i projektach: zapis atomowy przez plik tymczasowy,
/// kopia uszkodzonego pliku zamiast nadpisania, odczyt zwrotny z SHA-256
/// (<see cref="VerifyPersistedState"/>). <b>Rutyna nie przyjmuje poleceń niszczących dane</b> —
/// taki krok jest odrzucany przy zapisie, bo rutyna wykonuje się bez pytania o zgodę.</para></summary>
public sealed class RoutineService
{
    public const int MaxRoutines = 40;
    public const int MaxSteps = 12;
    public const int MaxStepLength = 160;
    public const int MaxNameLength = 40;

    /// <summary>Fragmenty, których rutyna nie może zawierać: kasowanie danych, czyszczenie,
    /// zmiany nazw plików i potwierdzanie oczekujących zgód. Wykonywane automatycznie byłyby
    /// dokładnie tym, czego Sentinel nie robi po cichu.</summary>
    private static readonly string[] ForbiddenSteps =
    [
        "usun wszystkie wspomnienia", "usun pamiec", "usun pamięć", "usun rozmowe", "usun rozmowę",
        "usun archiwum", "usun duplikaty", "usun puste pliki", "usun do kosza", "usun zadanie",
        "usun snapshot", "usun przypomnienie", "wyczysc", "sformatuj", "zmien nazwy", "potwierdz",
    ];

    private readonly object syncRoot = new();
    private readonly string storePath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private RoutineState state = new();

    public string? LastStorageError { get; private set; }
    public string StoragePath => storePath;
    public event Action? Changed;

    public RoutineService(string? directory = null)
    {
        storePath = Path.Combine(directory ?? AppPaths.MemoryDirectory, "routines.json");
        Load();
    }

    public IReadOnlyList<RoutineRecord> GetRoutines()
    {
        lock (syncRoot) return state.Routines.OrderBy(x => x.Name, StringComparer.Ordinal).Select(Clone).ToArray();
    }

    public RoutineRecord? Find(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return null;
        string needle = nameOrId.Trim();
        lock (syncRoot)
            return state.Routines.FirstOrDefault(x => string.Equals(x.Id, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Name, needle, StringComparison.OrdinalIgnoreCase)) is { } found ? Clone(found) : null;
    }

    /// <summary>Zapisuje rutynę (nową albo nadpisuje istniejącą o tej samej nazwie).
    /// Zwraca null + <see cref="LastStorageError"/>, gdy nazwa lub kroki są nie do przyjęcia
    /// (limit, pusty krok, polecenie niszczące).</summary>
    public RoutineRecord? Save(string name, IEnumerable<string> steps)
    {
        string cleanName = (name ?? "").Trim();
        IEnumerable<string> source = steps ?? [];
        var cleanSteps = source.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Take(MaxSteps + 1).ToList();
        bool fire = false;
        RoutineRecord saved;
        lock (syncRoot)
        {
            LastStorageError = null;
            if (cleanName.Length is < 1 or > MaxNameLength) { LastStorageError = "Nazwa rutyny musi mieć 1–" + MaxNameLength + " znaków."; return null; }
            if (cleanSteps.Count == 0) { LastStorageError = "Rutyna musi mieć co najmniej jeden krok. Kroki rozdziel znakiem „|”."; return null; }
            if (cleanSteps.Count > MaxSteps) { LastStorageError = "Rutyna może mieć najwyżej " + MaxSteps + " kroków."; return null; }
            foreach (string step in cleanSteps)
            {
                if (step.Length > MaxStepLength) { LastStorageError = "Krok jest za długi (limit " + MaxStepLength + " znaków): „" + Truncate(step, 30) + "”."; return null; }
                string forbidden = ForbiddenFind(step);
                if (forbidden.Length > 0)
                {
                    LastStorageError = "Rutyna nie może zawierać poleceń niszczących dane („" + forbidden + "”), bo wykonuje się bez pytania o zgodę. " +
                        "Wykonaj takie polecenie samodzielnie — wtedy dostaniesz pytanie albo okno zgody.";
                    return null;
                }
            }
            var existing = state.Routines.FirstOrDefault(x => string.Equals(x.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Steps = cleanSteps;
                existing.UpdatedAt = DateTime.Now;
                saved = existing;
            }
            else
            {
                if (state.Routines.Count >= MaxRoutines) { LastStorageError = "Osiągnięto limit " + MaxRoutines + " rutyn. Usuń jedną: „usuń rutynę: nazwa”."; return null; }
                saved = new RoutineRecord
                {
                    Id = Guid.NewGuid().ToString("N")[..10],
                    Name = cleanName,
                    Steps = cleanSteps,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };
                state.Routines.Add(saved);
            }
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return LastStorageError == null ? Clone(saved) : null;
    }

    public bool Remove(string nameOrId)
    {
        bool fire = false;
        lock (syncRoot)
        {
            LastStorageError = null;
            if (string.IsNullOrWhiteSpace(nameOrId)) { LastStorageError = "Podaj nazwę rutyny: „usuń rutynę: poranek”."; return false; }
            string needle = nameOrId.Trim();
            var target = state.Routines.FirstOrDefault(x => string.Equals(x.Id, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Name, needle, StringComparison.OrdinalIgnoreCase));
            if (target == null) { LastStorageError = "Nie mam rutyny o nazwie „" + needle + "”. „rutyny” pokazuje listę."; return false; }
            state.Routines.Remove(target);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    /// <summary>Lista do czatu: nazwa, liczba kroków i kroki (żebyś widział, co się uruchomi).</summary>
    public string Describe()
    {
        var routines = GetRoutines();
        if (routines.Count == 0)
            return "Nie mam jeszcze rutyn. Dodaj: „dodaj rutynę: poranek = która godzina | plan dnia | bateria” (kroki rozdziela „|”).";
        var lines = routines.Select(x => "· " + x.Name + " (" + x.Steps.Count + (x.Steps.Count == 1 ? " krok" : " kroki") + ")" +
            (x.BuiltIn ? "  [startowa]" : "") + "\n    " + string.Join(" → ", x.Steps.Select(s => "„" + s + "”")));
        return "Rutyny (" + routines.Count + "):\n" + string.Join("\n", lines) +
            "\nUruchom: „uruchom rutynę: poranek” albo „rutyna poranek”. Zmień: „dodaj rutynę: poranek = …” (nadpisuje). Usuń: „usuń rutynę: poranek”.";
    }

    internal bool VerifyPersistedState(out string evidence)
    {
        lock (syncRoot)
        {
            try
            {
                if (LastStorageError != null) { evidence = LastStorageError; return false; }
                string expected = JsonSerializer.Serialize(state, jsonOptions);
                if (File.ReadAllText(storePath, Encoding.UTF8) != expected) { evidence = "Zapis rutyn nie odpowiada bieżącemu stanowi."; return false; }
                evidence = "Odczyt zwrotny: " + storePath + "; SHA-256: " + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(storePath))) + "; rutyny: " + state.Routines.Count + ".";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { evidence = "Nie można sprawdzić zapisu rutyn: " + ex.Message; return false; }
        }
    }

    private static string ForbiddenFind(string step)
    {
        string normalized = ConversationMemoryService.Normalize(step);
        foreach (string marker in ForbiddenSteps)
            if (normalized.Contains(ConversationMemoryService.Normalize(marker), StringComparison.Ordinal)) return marker;
        return "";
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(storePath))
                {
                    state = Seed();
                    SaveLocked();
                    return;
                }
                if (new FileInfo(storePath).Length > 2 * 1024 * 1024) throw new IOException("Plik rutyn przekracza 2 MB.");
                var loaded = JsonSerializer.Deserialize<RoutineState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
                if (loaded == null) throw new JsonException("Pusty plik rutyn.");
                state = loaded;
                state.Routines ??= [];
                foreach (var routine in state.Routines)
                {
                    if (routine.Id.Length == 0) routine.Id = Guid.NewGuid().ToString("N")[..10];
                    routine.Steps ??= [];
                    if (routine.Name.Length == 0) routine.Name = "rutyna";
                }
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                state = new();
                LastStorageError = "Nie udało się wczytać rutyn: " + ex.Message;
                try { if (File.Exists(storePath)) File.Copy(storePath, storePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { LastStorageError += " Nie udało się utworzyć kopii."; }
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            string temporary = storePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, jsonOptions), Encoding.UTF8);
            File.Move(temporary, storePath, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastStorageError = "Zmiany rutyn działają tylko do zamknięcia aplikacji. Błąd zapisu: " + ex.Message;
        }
    }

    private static RoutineState Seed()
    {
        var now = DateTime.Now;
        return new RoutineState
        {
            Routines =
            [
                new() { Id = "seed-poranek", Name = "poranek", BuiltIn = true, CreatedAt = now, UpdatedAt = now,
                        Steps = ["która godzina", "plan dnia", "bateria"] },
                new() { Id = "seed-praca", Name = "praca", BuiltIn = true, CreatedAt = now, UpdatedAt = now,
                        Steps = ["wycisz", "zadania"] },
                new() { Id = "seed-koniec", Name = "koniec dnia", BuiltIn = true, CreatedAt = now, UpdatedAt = now,
                        Steps = ["zadania", "przypomnienia", "podsumuj rozmowę"] },
            ],
        };
    }

    private static RoutineRecord Clone(RoutineRecord x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        Steps = [.. x.Steps],
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt,
        BuiltIn = x.BuiltIn,
    };
}
