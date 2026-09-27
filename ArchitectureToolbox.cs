using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 5 · pozycje 341–420 — architektura bez rysowania i bez sieci: graf modułów z kolejnością
/// budowy, cykle, sprzężenia, naruszenia warstw, rejestr długu technicznego, ADR-i, karty modułów,
/// budżet latencji, pojemność, plan migracji i wdrożenie kanaryjskie. Wszystko liczone na danych
/// z polecenia — żadnych plików, żadnych uruchomień, żadnych zmyślonych metryk.
/// </summary>
public static class ArchitectureToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "moduly")) return Graph(Payload(raw, "moduly"));
        if (Is(norm, "cykle")) return Cycles(Payload(raw, "cykle"));
        if (Is(norm, "sprzezenie")) return Coupling(Payload(raw, "sprzezenie"));
        if (Is(norm, "warstwy")) return Layers(Payload(raw, "warstwy"));
        if (Is(norm, "dlug techniczny")) return Debt(Payload(raw, "dlug techniczny"));
        if (Is(norm, "adr")) return Adr(Payload(raw, "adr"));
        if (Is(norm, "styl")) return Style(Payload(raw, "styl"));
        if (Is(norm, "c4")) return C4(Payload(raw, "c4"));
        if (Is(norm, "kapacyt")) return Capacity(Payload(raw, "kapacyt"));
        if (Is(norm, "pojemnosc kolejki")) return QueueDrain(Payload(raw, "pojemnosc kolejki"));
        if (Is(norm, "latencja")) return Latency(Payload(raw, "latencja"));
        if (Is(norm, "migracja bazy")) return Migration(Payload(raw, "migracja bazy"));
        if (Is(norm, "wdrozenie kanary")) return Canary(Payload(raw, "wdrozenie kanary"));
        if (Is(norm, "karta modulu")) return ModuleCard(Payload(raw, "karta modulu"));
        return null;
    }

    private static string Graph(string input)
    {
        var graph = ParseGraph(input);
        if (graph.Count == 0)
            return "Podaj moduły i zależności, np. „moduly: ui>logika,serwis logika>repo repo”. Wskazuję kolejność budowy i cykle — niczego nie uruchamiam.";
        var order = Topological(graph, out var cyclic);
        var sb = new StringBuilder();
        sb.Append("GRAF MODUŁÓW (odczyt, nic nie uruchamiam):").AppendLine();
        sb.Append("· węzły: ").Append(graph.Count).Append(", krawędzie: ").Append(graph.Sum(x => x.Value.Count)).AppendLine();
        if (cyclic.Count == 0)
            sb.Append("· kolejność budowy (zależności pierwsze): ").Append(string.Join(" → ", order)).AppendLine();
        else
            sb.Append("· CYKL obejmuje: ").Append(string.Join(" → ", cyclic)).Append(" → ").Append(cyclic[0])
              .Append(" — kolejności topologicznej nie ma; rozbij ten cykl (wspólne wsparcie albo interfejs).").AppendLine();
        var leaves = graph.Where(x => x.Value.Count == 0).Select(x => x.Key).OrderBy(x => x).ToList();
        if (leaves.Count > 0)
            sb.Append("· liście (bez zależności): ").Append(string.Join(", ", leaves)).AppendLine();
        sb.Append("· dalej: „sprzezenie: …” (gdzie jest ciasno), „warstwy: …” (naruszenia), „adr: …” (zapis decyzji)");
        return sb.ToString();
    }

    private static string Cycles(string input)
    {
        var graph = ParseGraph(input);
        if (graph.Count == 0)
            return "Podaj zależności, np. „cykle: a>b b>c c>a”. Pokażę, czy da się zbudować bez cyklu.";
        Topological(graph, out var cyclic);
        if (cyclic.Count == 0)
            return "Cykli nie ma — graf jest acykliczny, da się zbudować moduł po module.";
        return "CYKL(I): " + string.Join(", ", cyclic) + Environment.NewLine +
            "· cykl nie musi być zły (wzajemne odwołania bywają celowe), ale blokuje osobne budowanie i testowanie" + Environment.NewLine +
            "· typowe wyjścia: wspólny trzeci moduł z typami, interfejs zależności, zdarzenia zamiast wywołań";
    }

    private static string Coupling(string input)
    {
        var graph = ParseGraph(input);
        if (graph.Count == 0)
            return "Podaj zależności, np. „sprzezenie: a>b,c b>c c”. Policzę wejścia i wyjścia każdego modułu.";
        var fanIn = graph.ToDictionary(x => x.Key, _ => 0, StringComparer.Ordinal);
        foreach (var deps in graph.Values)
            foreach (string dep in deps.Distinct())
                if (fanIn.ContainsKey(dep)) fanIn[dep]++;
        var sb = new StringBuilder();
        sb.Append("SPRZĘŻENIA MODUŁÓW (z podanych zależności):").AppendLine();
        foreach (var node in graph.Keys.OrderBy(x => x))
            sb.Append("· ").Append(node).Append(": wyjścia ").Append(graph[node].Distinct().Count())
              .Append(", wejścia ").Append(fanIn[node]).AppendLine();
        var worst = graph.OrderByDescending(x => x.Value.Distinct().Count() + fanIn[x.Key]).First();
        sb.Append("· najciaśniej: ").Append(worst.Key).Append(" — tu zmiana kosztuje najwięcej; rozważ interfejs, jeśli wyjść+wejść jest więcej niż 4").AppendLine();
        sb.Append("· sprzężenie to nie grzech — grzechem jest ukryte sprzężenie przez wspólne dane globalne");
        return sb.ToString();
    }

    private static string Layers(string input)
    {
        string[] parts = (input ?? "").Split('|', 2);
        string[] layers = (parts.Length > 0 ? parts[0] : "").Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (layers.Length < 2)
            return "Podaj warstwy i wywołania, np. „warstwy: ui>logika>dane | ui>dane, logika>ui”. Sprawdzam przeskoki i wywołania w górę.";
        int Index(string name)
        {
            for (int i = 0; i < layers.Length; i++)
                if (string.Equals(layers[i], (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        string callsPart = parts.Length > 1 ? parts[1] : "";
        int marker = callsPart.IndexOf("wywolania", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) callsPart = callsPart[(marker + "wywolania".Length)..].TrimStart(':', ' ');
        var violations = new List<string>();
        foreach (string call in callsPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int arrow = call.IndexOf('>');
            if (arrow <= 0) continue;
            int from = Index(call[..arrow]), to = Index(call[(arrow + 1)..]);
            if (from < 0 || to < 0) violations.Add(call.Trim() + " — nieznana warstwa");
            else if (to < from) violations.Add(call.Trim() + " — wywołanie W GÓRĘ (niższa warstwa nie może wołać wyższej)");
            else if (to > from + 1) violations.Add(call.Trim() + " — przeskoki warstw (omija pośrednią)");
        }
        var sb = new StringBuilder();
        sb.Append("WARSTWY: ").Append(string.Join(" → ", layers)).AppendLine();
        if (violations.Count == 0) sb.Append("· naruszeń nie znalazłem (wywołania do sąsiedniej warstwy lub w dół)").AppendLine();
        else
        {
            sb.Append("· naruszenia: ").Append(violations.Count).AppendLine();
            foreach (string v in violations) sb.Append("  - ").Append(v).AppendLine();
        }
        sb.Append("· reguła: wołaj tylko o warstwę w dół; w górę — zdarzeniami i interfejsami");
        return sb.ToString();
    }

    private static string Debt(string input)
    {
        var items = new List<(string Name, double Score)>();
        foreach (string part in (input ?? "").Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string name = part[..eq].Trim();
            double score = Num(part[(eq + 1)..]);
            if (name.Length > 0 && name.Length <= 40 && double.IsFinite(score) && score >= 0 && score <= 10) items.Add((name, score));
        }
        if (items.Count == 0)
            return "Oceń obszary w skali 0–10, np. „dlug techniczny: autoryzacja=7; eksport=2”. Zrobię z tego rejestr z priorytetem.";
        var sorted = items.OrderByDescending(x => x.Score).ToList();
        double total = items.Sum(x => x.Score);
        var sb = new StringBuilder();
        sb.Append("REJESTR DŁUGU TECHNICZNEGO (twoje oceny 0–10, suma ").Append(N(total)).Append("):").AppendLine();
        foreach (var item in sorted)
        {
            string verdict = item.Score >= 7 ? "WYSOKI — zaplanuj refactor, zanim dojdzie kolejna zmiana" :
                item.Score >= 4 ? "średni — poprawiaj przy okazji prac w tym obszarze" : "niski — zostaw, tylko nie dokładaj";
            sb.Append("· ").Append(item.Name).Append(" = ").Append(N(item.Score))
              .Append(" → ").Append(verdict).AppendLine();
        }
        sb.Append("· udział czołowej pozycji: ").Append(N(100.0 * sorted[0].Score / total)).Append("% długu — z nią zacznij");
        return sb.ToString();
    }

    private static string Adr(string input)
    {
        string[] p = SplitParts(input, 4);
        if (p.Length < 4 || p.Any(x => x.Length == 0))
            return "Użycie: „adr: tytuł | kontekst | decyzja | konsekwencje”. Zwrócę gotową kartę decyzji do wklejenia do repo.";
        string slug = Regex.Replace(Flat(p[0]), "[^a-z0-9]+", "-").Trim('-');
        var sb = new StringBuilder();
        sb.Append("ADR-").Append(DateTime.Now.ToString("yyyy-MM-dd", Pl)).Append('-').Append(slug).AppendLine();
        sb.Append("Status: proponowany · Data: ").Append(DateTime.Now.ToString("dd.MM.yyyy", Pl)).AppendLine();
        sb.Append("Tytuł: ").Append(p[0]).AppendLine();
        sb.Append("Kontekst: ").Append(p[1]).AppendLine();
        sb.Append("Decyzja: ").Append(p[2]).AppendLine();
        sb.Append("Konsekwencje: ").Append(p[3]).AppendLine();
        sb.Append("· dopisz alternatywy, które odrzuciłeś — za pół roku nikt nie pamięta, co było do rozważenia");
        return sb.ToString();
    }

    private static string Style(string input)
    {
        string what = Flat(input).Trim();
        string mono =
            "MONOLIT: jeden proces, jedna baza, proste wdrożenie · najlepszy do momentu, gdy zespół i ruch rosną" + Environment.NewLine +
            "· koszt zmiany na późniejszy etap: wysoki — ale przedwiarne cięcie kosztuje jeszcze więcej";
        string modular =
            "MODULARNY MONOLIT: jeden proces, twarde granice między modułami (osobne projekty, jawnie API)" + Environment.NewLine +
            "· najlepszy kompromis dla małych zespołów: prostota wdrożenia + możliwość wydzielenia modułu później";
        string micro =
            "MIKROUSŁUGI: osobne procesy i bazy, rozmawiają siecią · izolacja awarii i zespołów, kosztem operacji" + Environment.NewLine +
            "· nie opłaca się poniżej kilku zespołów; niezawodność sieci i obserwowalność stają się pracą codzienną";
        return what switch
        {
            "monolit" => mono,
            "modularny" or "modularny monolit" => modular,
            "mikro" or "mikroslugi" => micro,
            _ => "Style do rozważenia (wpisz „styl: monolit”, „styl: modularny” albo „styl: mikro”):" + Environment.NewLine + mono + Environment.NewLine + modular + Environment.NewLine + micro,
        };
    }

    private static string C4(string input)
    {
        string[] p = SplitParts(input, 3);
        if (p.Length < 3 || p.Any(x => x.Length == 0))
            return "Użycie: „c4: nazwa | odpowiedzialność | technologia”. Zwrócę kartę kontenera (poziom C4-2) do wklejenia do dokumentacji.";
        return "KONTENER: " + p[0] + Environment.NewLine +
            "· odpowiedzialność: " + p[1] + Environment.NewLine +
            "· technologia: " + p[2] + Environment.NewLine +
            "· pytania kontrolne: co kontener NIE robi? gdzie są jego dane? co się stanie, gdy padnie?";
    }

    private static string Capacity(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] <= 0 || nums[1] <= 0)
            return "Użycie: „kapacyt: 5000 250” (docelowe żądania/s, żądania/s na jedną instancję). Wyliczę liczbę instancji z rezerwą.";
        int needed = (int)Math.Ceiling(nums[0] / nums[1]);
        return "POJEMNOŚĆ: " + N(nums[0]) + " ż./s przy " + N(nums[1]) + " ż./s na instancję" + Environment.NewLine +
            "· czynne instancje: " + needed + " · z rezerwą na awarię/rolling deploy: " + (needed + 1) + Environment.NewLine +
            "· rezerwę licz od ruchu rzeczywistego (p95, nie średnia) — średnia kłamie szczytach" + Environment.NewLine +
            "· jeśli instancji wychodzi więcej niż ~10, najpierw sprawdź, co żre czas odpowiedzi, nie skaluj w poziomie";
    }

    private static string QueueDrain(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] <= 0 || nums[1] <= 0)
            return "Użycie: „pojemnosc kolejki: 5000 100” (zaległość, przerób na minutę). Policzę czas opróżnienia.";
        double minutes = nums[0] / nums[1];
        string verdict = minutes > 60 ? "za długo — rozważ drugiego konsumenta albo tymczasowo szybsze przetwarzanie" :
            minutes > 15 ? "na styku — ustaw alert na wzrost zaległości" : "w porządku";
        return "KOLEJKA: zaległość " + N(nums[0]) + " przy " + N(nums[1]) + "/min → opróżnienie w " + N(minutes) + " min (" + verdict + ").";
    }

    private static string Latency(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2) return "Użycie: „latencja: 50 20 10 5” (budżet w ms per krok). Zsumuję i wskażę największy kawałek.";
        double total = nums.Sum();
        int maxIndex = 0;
        for (int i = 1; i < nums.Length; i++) if (nums[i] > nums[maxIndex]) maxIndex = i;
        var sb = new StringBuilder();
        sb.Append("BUDŻET LATENCJI: suma ").Append(N(total)).Append(" ms w ").Append(nums.Length).Append(" krokach").AppendLine();
        for (int i = 0; i < nums.Length; i++)
            sb.Append("· krok ").Append(i + 1).Append(": ").Append(N(nums[i])).Append(" ms (")
              .Append(N(total > 0 ? 100.0 * nums[i] / total : 0)).Append("%)").Append(i == maxIndex ? " ← największy kawałek" : "").AppendLine();
        sb.Append("· budżet kończy się zawsze w tym samym miejscu — optymalizuj krok ").Append(maxIndex + 1).Append(", reszta to kosmetyka");
        return sb.ToString();
    }

    private static string Migration(string input)
    {
        string what = (input ?? "").Trim();
        if (what.Length == 0)
            return "Użycie: „migracja bazy: rabat” (nazwa zmiany). Dostaniesz plan bez przestoju: rozszerz → przesuń → zwiń.";
        return "PLAN MIGRACJI BEZ PRZESTOJU: " + what + Environment.NewLine +
            "1. ROZSZERZ: dodaj nowe pole/tabelę obok starego (NULL albo domyślne) — stary kod działa dalej" + Environment.NewLine +
            "2. PRZESUŃ: podwójny zapis (stare i nowe), skrypt uzupełnia stany historyczne, czytasz z nowego" + Environment.NewLine +
            "3. ZWIŃ: po obserwacji usuń stary zapis i stare pole — dopiero teraz NOT NULL, jeśli trzeba" + Environment.NewLine +
            "· nigdy nie łącz kroku 1 i 3 w jedno wdrożenie; rola bazy to jedyna rzecz, której nie da się cofnąć hotfixem";
    }

    private static string Canary(string input)
    {
        double percent = Numbers(input, 1) is { Length: > 0 } n ? n[0] : double.NaN;
        if (!double.IsFinite(percent) || percent <= 0 || percent > 100)
            return "Użycie: „wdrozenie kanary: 5” (pierwszy procent ruchu). Rozpiszę etapy i punkty cofnięcia.";
        return "WDROŻENIE KANARYJSKIE (start " + N(percent) + "% ruchu):" + Environment.NewLine +
            "· etapy: " + N(percent) + "% → 5% → 25% → 50% → 100%, każdy minimum jeden pełny cykl ruchu (doba, jeśli masz dobowe szczyty)" + Environment.NewLine +
            "· obserwuj: błędy, latencję p95, kluczową metrykę biznesową — nie „czy padło”, tylko „czy jest gorzej”" + Environment.NewLine +
            "· cofnięcie: powrót do poprzedniej wersji jest decyzją o 3:00 w nocy — sprawdź ją na etapie " + N(percent) + "%, nie przy 100%" + Environment.NewLine +
            "· jeśli różnica między wersjami jest nieodwracalna (migracja danych), kanarek nie wystarczy — potrzebny plan dual-write";
    }

    private static string ModuleCard(string input)
    {
        string[] p = SplitParts(input, 3);
        if (p.Length < 2 || p[0].Length == 0)
            return "Użycie: „karta modulu: nazwa | odpowiedzialność | publiczne API”. Karta pomaga pilnować jednej odpowiedzialności.";
        var sb = new StringBuilder();
        sb.Append("MODUŁ: ").Append(p[0]).AppendLine();
        sb.Append("· odpowiedzialność: ").Append(p.Length > 1 ? p[1] : "(podaj)").AppendLine();
        sb.Append("· publiczne API: ").Append(p.Length > 2 ? p[2] : "(podaj)").AppendLine();
        sb.Append("· pytanie SRP: czy odpowiedzialność da się powiedzieć jednym zdaniem bez „oraz”? Jeśli nie — to dwa moduły").AppendLine();
        sb.Append("· pytanie zależności: czego moduł NIE widzi? (baza innych modułów, globalne stany) — tego pilnuj przy review");
        return sb.ToString();
    }

    // ————— pomocnicze —————

    private static Dictionary<string, List<string>> ParseGraph(string input)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string part in Regex.Split(input ?? "", @"[;\n|]+"))
        {
            string s = part.Trim();
            if (s.Length == 0) continue;
            int arrow = s.IndexOf('>');
            if (arrow < 0)
            {
                string node = s.Trim();
                if (node.Length is > 0 and <= 40 && !graph.ContainsKey(node)) graph[node] = [];
                continue;
            }
            string from = s[..arrow].Trim();
            if (from.Length is 0 or > 40) continue;
            if (!graph.ContainsKey(from)) graph[from] = [];
            foreach (string to in s[(arrow + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (to.Length is 0 or > 40) continue;
                if (!graph[from].Contains(to)) graph[from].Add(to);
                if (!graph.ContainsKey(to)) graph[to] = [];
            }
        }
        return graph;
    }

    private static List<string> Topological(Dictionary<string, List<string>> graph, out List<string> cycle)
    {
        var remaining = graph.ToDictionary(x => x.Key, x => x.Value.Distinct().Count(d => graph.ContainsKey(d)), StringComparer.Ordinal);
        var dependents = graph.ToDictionary(x => x.Key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var pair in graph)
            foreach (string dep in pair.Value.Distinct())
                if (dependents.ContainsKey(dep)) dependents[dep].Add(pair.Key);
        var ready = new SortedSet<string>(remaining.Where(x => x.Value == 0).Select(x => x.Key), StringComparer.Ordinal);
        var order = new List<string>();
        while (ready.Count > 0)
        {
            string node = ready.Min!;
            ready.Remove(node);
            order.Add(node);
            foreach (string other in dependents[node])
            {
                if (!remaining.ContainsKey(other)) continue;
                remaining[other]--;
                if (remaining[other] == 0) ready.Add(other);
            }
        }
        cycle = remaining.Where(x => x.Value > 0).Select(x => x.Key).OrderBy(x => x).ToList();
        return order;
    }

    private static string[] SplitParts(string input, int max)
    {
        string[] parts = (input ?? "").Split('|', max, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts;
    }

    private static double[] Numbers(string input, int min)
    {
        var result = new List<double>();
        string normalized = Regex.Replace(input ?? "", @"(?<=\d),(?=\d)", ".");
        foreach (string token in Regex.Split(normalized, "[\\s;+]+"))
        {
            double v = Num(token);
            if (double.IsFinite(v)) result.Add(v);
            if (result.Count >= 12) break;
        }
        return result.Count >= min ? result.ToArray() : [];
    }

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
