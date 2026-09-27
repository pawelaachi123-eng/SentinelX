using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

/// <summary>0.96 · wspólna obsługa rutyn dla obu interfejsów (nowy shell i tryb zgodności).
/// Wykonanie jest jawne: rutyna rusza dopiero po Twoim poleceniu, krok po kroku, z wynikiem każdego
/// kroku w odpowiedzi. Przerwanie („przerwij”) zatrzymuje kolejne kroki — wykonanych nie cofam.
/// <para>Zabezpieczenia: rutyna nie przyjmie polecenia niszczącego dane (odrzuca je
/// <see cref="RoutineService"/> przy zapisie), a zagnieżdżone uruchamianie rutyn ma twardy limit
/// głębokości, więc „rutyna w rutynie” nie zakręci się w kółko.</para></summary>
public static class RoutineCommands
{
    private const int MaxDepth = 3;
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>Zwraca odpowiedź, gdy polecenie dotyczy rutyn; null = to nie jest komenda rutyny.</summary>
    /// <param name="command">Tekst użytkownika (z polskimi znakami) — z niego biorę argumenty.</param>
    /// <param name="normalized">Znormalizowana forma do porównań stałych (bez polskich znaków).</param>
    /// <param name="runner">Funkcja wykonująca jedno polecenie pełnym stosem (narzędzia → router).</param>
    public static async Task<string?> TryHandleAsync(string command, string normalized, RoutineService? routines,
        Func<string, CancellationToken, Task<string>> runner, CancellationToken token)
    {
        if (routines == null || string.IsNullOrWhiteSpace(command)) return null;
        if (normalized is "rutyny" or "lista rutyn" or "moje rutyny" or "jakie rutyny") return routines.Describe();

        var remove = Regex.Match(command, @"^(?:usun|usuń|skasuj)\s+rutyn[eęą]?\s*:\s*(.+)$", RegexOptions.IgnoreCase);
        if (remove.Success)
        {
            string name = remove.Groups[1].Value.Trim().Trim('.', '!', '?');
            return routines.Remove(name)
                ? "Usunąłem rutynę „" + name + "”. Pozostałe rutyny, zadania i pamięć są nietknięte."
                : routines.LastStorageError ?? "Nie usunąłem rutyny.";
        }

        var add = Regex.Match(command, @"^(?:dodaj|nowa|zdefiniuj|zmien|zmień|nadpisz)\s+rutyn[eęą]?\s*:\s*(.+)$", RegexOptions.IgnoreCase);
        if (add.Success) return Add(routines, add.Groups[1].Value);

        var run = Regex.Match(command, @"^(?:uruchom|odpal|wykonaj|wlacz|włącz)\s+rutyn[eęą]?\s*:?\s*(.+)$", RegexOptions.IgnoreCase);
        if (!run.Success) run = Regex.Match(command, @"^rutyna\s+(.+)$", RegexOptions.IgnoreCase);
        if (run.Success) return await RunAsync(routines, run.Groups[1].Value.Trim(), runner, token);
        return null;
    }

    private static string Add(RoutineService routines, string payload)
    {
        int separator = payload.IndexOf('=');
        if (separator < 0)
            return "Rutynę zapisz tak: „dodaj rutynę: poranek = która godzina | plan dnia | bateria”. " +
                   "Nazwa przed „=”, kroki po nim — rozdzielone znakiem „|” (do " + RoutineService.MaxSteps + " kroków).";
        string name = payload[..separator].Trim();
        string[] steps = payload[(separator + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var saved = routines.Save(name, steps);
        if (saved == null) return routines.LastStorageError ?? "Nie zapisałem rutyny.";
        return "Rutyna „" + saved.Name + "” zapisana (" + saved.Steps.Count + " " + (saved.Steps.Count == 1 ? "krok" : "kroki") + "):\n" +
            string.Join("\n", saved.Steps.Select((step, i) => "  " + (i + 1) + ". " + step)) +
            "\nUruchamiasz ją: „uruchom rutynę: " + saved.Name + "”.";
    }

    private static async Task<string> RunAsync(RoutineService routines, string name,
        Func<string, CancellationToken, Task<string>> runner, CancellationToken token)
    {
        if (Depth.Value >= MaxDepth)
            return "Zatrzymałem się: rutyny zagnieżdżone mają limit " + MaxDepth + " poziomów, żeby nie kręcić się w kółko. Uruchom tę rutynę bezpośrednio.";
        string clean = name.Trim().Trim('.', '!', '?');
        var routine = routines.Find(clean);
        if (routine == null)
            return "Nie mam rutyny o nazwie „" + clean + "”. „rutyny” pokazuje listę. Dodajesz tak: " +
                   "„dodaj rutynę: poranek = która godzina | plan dnia”.";

        Depth.Value = Depth.Value + 1;
        try
        {
            var lines = new List<string>
            {
                "Rutyna „" + routine.Name + "” — " + routine.Steps.Count + " " + (routine.Steps.Count == 1 ? "krok" : "kroki") + ". Wykonuję po kolei; każdy krok może odmówić.",
            };
            int done = 0;
            for (int i = 0; i < routine.Steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string step = routine.Steps[i];
                string outcome;
                // Bez ConfigureAwait(false): kroki wracają na ten sam kontekst (WPF = wątek STA),
                // więc rutyna może bezpiecznie dotknąć schowka czy okien tak jak zwykłe polecenie.
                try { outcome = await runner(step, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { outcome = "Nie wykonano: " + ex.Message; }
                done++;
                lines.Add((i + 1) + ". „" + step + "” → " + Shorten(outcome));
            }
            lines.Add("Koniec rutyny „" + routine.Name + "” (" + done + " z " + routine.Steps.Count + " kroków). " +
                      "Przerwanie zatrzymuje kolejne kroki — wykonanych nie cofam.");
            return string.Join("\n", lines);
        }
        finally { Depth.Value = Depth.Value - 1; }
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(bez odpowiedzi)";
        string first = text.Split('\n')[0].Trim();
        if (first.Length == 0) first = text.Trim();
        return first.Length <= 160 ? first : first[..159] + "…";
    }
}
