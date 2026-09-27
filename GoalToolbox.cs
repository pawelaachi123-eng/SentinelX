using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 20 · pozycje 1401–1500 — agentic: rozkładanie celu na kamienie milowe, walidacja planu
/// kroków, budżet czasu z buforem, plan wycofania operacji, polityka autonomii, klasyfikacja ryzyka
/// akcji, karta samooceny i definicja sukcesu (SMART). Reguły i szablony, zero magii — Sentinel
/// deklaruje, czego wymaga od siebie przy akcjach o rosnącym ryzyku.
/// </summary>
public static class GoalToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "cel rozloz")) return Decompose(Payload(raw, "cel rozloz"));
        if (Is(norm, "plan krokow")) return StepPlan(Payload(raw, "plan krokow"));
        if (Is(norm, "czas na zadanie")) return TimeBudget(Payload(raw, "czas na zadanie"));
        if (Is(norm, "plan wycofania")) return Rollback(Payload(raw, "plan wycofania"));
        if (Is(norm, "polityka autonomii")) return Autonomy();
        if (Is(norm, "ryzyko")) return Risk(Payload(raw, "ryzyko"));
        if (Is(norm, "samoocena")) return SelfEval(Payload(raw, "samoocena"));
        if (Is(norm, "definicja sukcesu")) return SuccessDefinition(Payload(raw, "definicja sukcesu"));
        return null;
    }

    private static string Decompose(string input)
    {
        string goal = (input ?? "").Trim();
        if (goal.Length < 3)
            return "Użycie: „cel rozloz: nauczyć się gitary”. Rozłożę cel na pierwszy krok, kamienie milowe i miernik — szablon, nie wróżba.";
        bool learning = Flat(goal).Contains("naucz") || Flat(goal).Contains("nauka");
        return "CEL: " + goal + Environment.NewLine +
            "· sukces = jedno zdanie, które da się sprawdzić („gram X od nuty bez zatrzymania”)" + Environment.NewLine +
            "· pierwszy krok (dziś, 15 min): " + (learning
                ? "znajdź źródło lekcji i zrób pierwszą sesję — bez kupowania sprzętu"
                : "najmniejsza wersja zadania, którą skończysz dziś") + Environment.NewLine +
            "· kamienie milowe: tydzień — pierwsza widoczna próbka; miesiąc — stabilna rutyna; kwartał — cel osiągnięty" + Environment.NewLine +
            "· ryzyka: cel za duży (tnij), brak miernika (zdefiniuj), cisza po tygodniu (zmniejsz krok, nie porzucaj)" + Environment.NewLine +
            "· miernik: co tydzień zapisuj, co zrobiono — „plan krokow: …” pomoże rozpisać kolejny tydzień";
    }

    private static string StepPlan(string input)
    {
        var steps = (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0).Take(12).ToList();
        if (steps.Count < 2)
            return "Użycie: „plan krokow: napisz kod; przetestuj; wydaj wersję”. Sprawdzam plan: liczba kroków, krok weryfikujący, ryzyka kolejności.";
        bool hasVerification = steps.Any(s => Flat(s).Contains("sprawdz") || Flat(s).Contains("test") || Flat(s).Contains("ocen") || Flat(s).Contains("zmierz"));
        var sb = new StringBuilder("PLAN (").Append(steps.Count).Append(" kroków):").AppendLine();
        for (int i = 0; i < steps.Count; i++)
            sb.Append("  ").Append(i + 1).Append(". ").Append(steps[i]).AppendLine();
        if (!hasVerification)
            sb.Append("· UWAGA: żaden krok nie weryfikuje wyniku — dodaj na końcu „sprawdź/ocen…”, inaczej plan potwierdzi cokolwiek").AppendLine();
        else
            sb.Append("· krok weryfikujący jest — dobrze").AppendLine();
        if (steps.Count > 6) sb.Append("· 7+ kroków w jednym planie: rozważ dwa etapy z własną weryfikacją po każdym").AppendLine();
        sb.Append("· wykonuj po jednym kroku i potwierdzaj wynik przed następnym — plan bez punktów kontrolnych to listopad marzeń");
        return sb.ToString();
    }

    private static string TimeBudget(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[0] > 100 || nums[1] < 1 || nums[1] > 600)
            return "Użycie: „czas na zadanie: 4 60” (liczba kroków, minuty na krok). Policzę sumę z buforem 25% i liczbę sesji.";
        double total = nums[0] * nums[1];
        double buffered = Math.Ceiling(total * 1.25);
        double sessions = Math.Ceiling(buffered / 90);
        return "BUDŻET CZASU: " + N(nums[0]) + " kroków × " + N(nums[1]) + " min = " + N(total) + " min" + Environment.NewLine +
            "· z buforem 25% na niespodzianki: " + N(buffered) + " min" + Environment.NewLine +
            "· sesje po 90 min: " + N(sessions) + " (między nimi prawdziwa przerwa, nie mail)" + Environment.NewLine +
            "· jeśli bufor wychodzi regularnie niewykorzystany — szacujesz za ostrożnie i możesz go ścisnąć";
    }

    private static string Rollback(string input)
    {
        string what = (input ?? "").Trim();
        if (what.Length == 0)
            return "Użycie: „plan wycofania: migracja bazy”. Rozpiszę, jak wycofać operację, zanim jej zaczniesz — porządek jest częścią wykonania.";
        return "PLAN WYCOFANIA: " + what + Environment.NewLine +
            "1. kopia zapasowa stanu PRZED operacją (bez tego planu nie ma)" + Environment.NewLine +
            "2. suchy bieg na kopii — operacja wykonana gdzie indziej, obserwowana" + Environment.NewLine +
            "3. punkt cofnięcia: dokładnie co przywracasz (pliki? rekordy? konfigurację?) i jak sprawdzasz, że wróciło" + Environment.NewLine +
            "4. test po cofnięciu: ten sam zestaw sprawdzeń co po wykonaniu" + Environment.NewLine +
            "· jeśli nie umiesz opisać kroku 3, operacja jest nieodwracalna — wtedy najpierw dwuetapowa zgoda i widoczne ostrzeżenie, tak jak tu, w Sentinelu";
    }

    private static string Autonomy() =>
        "POLITYKA AUTONOMII SENTINELA (jak pracują jego akcje):" + Environment.NewLine +
        "· ODCZYT — pomiary, listy, analiza tekstu: wykonuję od razu, dowód w historii" + Environment.NewLine +
        "· ZMIANA — zapis notatki, kopia danych, kopiowanie modeli: wykonuję i pokazuję co dokładnie zrobiłem" + Environment.NewLine +
        "· DESTRUKCYJNE — usuwanie, zamykanie systemu, pobieranie z sieci: zgoda dwuetapowa (plan → potwierdzenie), jednorazowa, z terminem" + Environment.NewLine +
        "· szara strefa: pytam zamiast zgadywać — odpowiedź „nie wiem” jest dozwolona dla agenta i wymagana uczciwością" + Environment.NewLine +
        "· dowód wykonania: status VERIFIED tylko gdy akcja zostawiła mierzalny ślad — nie do prestiżu, ale do kontroli";

    private static string Risk(string input)
    {
        string what = Flat(input ?? "");
        if (what.Length < 3)
            return "Użycie: „ryzyko: usunąć plik z dysku”. Zaklasyfikuję operację i powiem, jakiej zgody by wymagała.";
        string level;
        if (ContainsAny(what, "usun", "sformatuj", "wyczysc", "zamknij", "wylacz", "skasuj")) level = "DESTRUKCYJNE";
        else if (ContainsAny(what, "zmien", "nadpisz", "przenies", "zamien", "zaktualizuj")) level = "ŚREDNIE";
        else if (ContainsAny(what, "dodaj", "utworz", "zapisz", "pobierz", "zainstaluj")) level = "NISKO (zapis)";
        else level = "ODCZYT";
        string consent = level switch
        {
            "DESTRUKCYJNE" => "zgoda dwuetapowa + kopia zapasowa + jasne ostrzeżenie",
            "ŚREDNIE" => "jedna jawna zgoda + podsumowanie zmiany",
            "NISKO (zapis)" => "wykonanie + informacja co zapisano",
            _ => "wykonanie od razu (nic nie zmienia)",
        };
        return "RYZYKO: " + level + Environment.NewLine +
            "· wymagana kontrola: " + consent + Environment.NewLine +
            "· klasyfikacja po czasownikach operacji — ostateczna decyzja zawsze należy do ciebie";
    }

    private static string SelfEval(string input)
    {
        string[] p = SplitParts(input, 2);
        if (p.Length < 2 || p[0].Length == 0)
            return "Użycie: „samoocena: raport sprzedaży | tabela i trzy wnioski”. Dostaniesz kartę pytań do uczciwej oceny wykonania.";
        return "SAMOOCENA: " + p[0] + Environment.NewLine +
            "· oczekiwany rezultat: " + p[1] + Environment.NewLine +
            "1. Czy rezultat da się zweryfikować bez mojej dobrej woli? (jeśli nie — brak dowodu)" + Environment.NewLine +
            "2. Czy wszystko z oczekiwanego jest spełnione, czy część? (wylicz wprost)" + Environment.NewLine +
            "3. Co poszło inaczej niż w planie i dlaczego?" + Environment.NewLine +
            "4. Co zrobisz inaczej przy następnym razie?" + Environment.NewLine +
            "· samoocena bez punktu 4 to opis, nie nauka";
    }

    private static string SuccessDefinition(string input)
    {
        string goal = (input ?? "").Trim();
        if (goal.Length < 3)
            return "Użycie: „definicja sukcesu: posprzątać archiwum maili”. Zamienię cel w definicję SMART do odhaczenia.";
        return "DEFINICJA SUKCESU (SMART): " + goal + Environment.NewLine +
            "· S — konkret: co dokładnie będzie zrobione?" + Environment.NewLine +
            "· M — miara: jaka liczba/objaw powie „gotowe”? (nawet szacunkowa)" + Environment.NewLine +
            "· A — osiągalne: czy da się to w twoim czasie? jeśli nie — zmniejsz, nie rezygnuj" + Environment.NewLine +
            "· R — istotne: po co ci to? (jedno zdanie; brak odpowiedzi = cel cudzy)" + Environment.NewLine +
            "· T — termin: kiedy dokładnie sprawdzisz wynik? (data, nie „kiedyś”)";
    }

    // ————— pomocnicze —————

    private static bool ContainsAny(string what, params string[] words) => words.Any(what.Contains);

    private static double[] Numbers(string input, int min)
    {
        var result = new List<double>();
        string normalized = Regex.Replace(input ?? "", @"(?<=\d),(?=\d)", ".");
        foreach (string token in Regex.Split(normalized, "[\\s;+]+"))
        {
            double v = Num(token);
            if (double.IsFinite(v)) result.Add(v);
            if (result.Count >= 8) break;
        }
        return result.Count >= min ? result.ToArray() : [];
    }

    private static string[] SplitParts(string input, int max) =>
        (input ?? "").Split('|', max, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Flat(string input)
    {
        string s = (input ?? "").ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool Is(string norm, string trigger) => norm == trigger || norm.StartsWith(trigger + ":");
    private static string Payload(string raw, params string[] prefixes)
    {
        string t = (raw ?? "").Trim();
        foreach (string p in prefixes)
        {
            if (!t.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = t[p.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..].Trim();
            return rest;
        }
        return t;
    }

    private static string N(double v) => v.ToString("0.##", Pl);

    private static double Num(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
