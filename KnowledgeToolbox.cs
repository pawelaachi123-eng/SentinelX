using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 8 · pozycje 591–670 — wiedza i nauka bez sieci i bez embeddingów: fiszki z notatek,
/// eksport do Anki, plan powtórek ze spacingiem, słownik pojęć, podobieństwo tekstów (kosinus
/// na workach słów — czysta arytmetyka), łączenie notatek po wspólnych tematach, mapa wiedzy,
/// indeks pojęć i pytania kontrolne. Wszystko na tekście, który dostanę w poleceniu.
/// </summary>
public static class KnowledgeToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly string[] StopWords =
    {
        "i","a","oraz","lub","w","we","na","z","ze","do","od","dla","nie","jest","sa","to","ten","ta","te","sie",
        "the","of","and","or","in","on","at","is","are","be","by","an","as","it","with","for","ma","ale","co","jak",
        "po","za","przy","przez","tak","ze","jego","jej","juz","bardzo","kiedy","gdzie",
    };

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "fiszki")) return Flashcards(Payload(raw, "fiszki"));
        if (Is(norm, "anki")) return Anki(Payload(raw, "anki"));
        if (Is(norm, "powtorki")) return Repetitions(Payload(raw, "powtorki"));
        if (Is(norm, "slownik pojec")) return Glossary(Payload(raw, "slownik pojec"));
        if (Is(norm, "podobienstwo")) return Similarity(Payload(raw, "podobienstwo"));
        if (Is(norm, "wspolne tematy")) return SharedTopics(Payload(raw, "wspolne tematy"));
        if (Is(norm, "mapa wiedzy")) return KnowledgeMap(Payload(raw, "mapa wiedzy"));
        if (Is(norm, "indeks pojec")) return Index(Payload(raw, "indeks pojec"));
        if (Is(norm, "pytania kontrolne")) return Quiz(Payload(raw, "pytania kontrolne"));
        return null;
    }

    private static List<(string Term, string Definition)> ParsePairs(string input)
    {
        var pairs = new List<(string, string)>();
        foreach (string part in (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string term = part[..eq].Trim();
            string def = part[(eq + 1)..].Trim();
            if (term.Length > 0 && def.Length > 0 && term.Length <= 60 && def.Length <= 400) pairs.Add((term, def));
        }
        return pairs;
    }

    private static string Flashcards(string input)
    {
        var pairs = ParsePairs(input);
        if (pairs.Count == 0)
            return "Użycie: „fiszki: pojęcie = definicja; drugie pojęcie = druga definicja”. Zrobię z tego pytania i odpowiedzi.";
        var sb = new StringBuilder("FISZKI (").Append(pairs.Count).Append("):").AppendLine();
        int i = 1;
        foreach (var (term, def) in pairs)
            sb.Append(i++).Append(". Q: ").Append(term).Append('?').AppendLine()
              .Append("   A: ").Append(def).AppendLine();
        sb.Append("· powtórki rozłożone w czasie działają lepiej niż czytanie w kółko: „powtorki: RRRR-MM-DD”");
        return sb.ToString();
    }

    private static string Anki(string input)
    {
        var pairs = ParsePairs(input);
        if (pairs.Count == 0)
            return "Użycie: „anki: pojęcie = definicja; …”. Zwrócę TSV do wklejenia do pliku i importu w Anki (przecinek między polami to przecinek tekstu — dlatego TSV).";
        var sb = new StringBuilder("ANKI TSV (kopiuj do pliku .txt, import: pole oddzielone tabulatorem):").AppendLine();
        foreach (var (term, def) in pairs)
            sb.Append(term.Replace('\t', ' ')).Append('\t').Append(def.Replace('\t', ' ')).AppendLine();
        return sb.ToString();
    }

    private static string Repetitions(string input)
    {
        int[] gaps = [1, 3, 7, 16, 35];
        string trimmed = (input ?? "").Trim();
        var sb = new StringBuilder();
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start))
        {
            sb.Append("PLAN POWTÓREK od ").Append(start.ToString("dd.MM.yyyy", Pl)).Append(":").AppendLine();
            foreach (int gap in gaps)
                sb.Append("· +").Append(gap).Append(" dni → ").Append(start.AddDays(gap).ToString("dd.MM.yyyy", Pl)).AppendLine();
        }
        else
        {
            sb.Append("PLAN POWTÓREK (rozstawienie, które zwykle wystarcza):").AppendLine();
            foreach (int gap in gaps) sb.Append("· +").Append(gap).Append(" dni").AppendLine();
        }
        sb.Append("· zasadą jest: powtórz tuż przed zapomnieniem, nie po — jeśli mylisz odpowiedź, skróć odstęp o połowę");
        return sb.ToString();
    }

    private static string Glossary(string input)
    {
        var pairs = ParsePairs(input).OrderBy(x => x.Term, StringComparer.OrdinalIgnoreCase).ToList();
        if (pairs.Count == 0)
            return "Użycie: „slownik pojec: term = wyjaśnienie; …”. Posortuję alfabetycznie i ujednolicę zapis.";
        var sb = new StringBuilder("SŁOWNIK POJĘĆ (").Append(pairs.Count).Append("):").AppendLine();
        foreach (var (term, def) in pairs)
            sb.Append("· ").Append(term).Append(" — ").Append(def).AppendLine();
        return sb.ToString();
    }

    private static string Similarity(string input)
    {
        string[] p = SplitParts(input, 2);
        if (p.Length < 2 || p[0].Trim().Length == 0 || p[1].Trim().Length == 0)
            return "Użycie: „podobienstwo: tekst A | tekst B”. Kosinus na workach słów (0–100%) — arytmetyka, nie magiczne AI.";
        var a = TokenCounts(p[0]);
        var b = TokenCounts(p[1]);
        if (a.Count == 0 || b.Count == 0)
            return "Po odjęciu słów nieważnych nie zostało nic do porównania — dłuższe teksty dadzą sensowny wynik.";
        double dot = a.Where(x => b.ContainsKey(x.Key)).Sum(x => (double)x.Value * b[x.Key]);
        double norm = Math.Sqrt(a.Values.Sum(v => (double)v * v)) * Math.Sqrt(b.Values.Sum(v => (double)v * v));
        double percent = norm > 0 ? Math.Round(100.0 * dot / norm) : 0;
        var shared = a.Keys.Where(b.ContainsKey).Take(5).ToList();
        return "PODOBIEŃSTWO: " + percent.ToString("0", Pl) + "%" + Environment.NewLine +
            "· wspólne słowa treściowe: " + (shared.Count > 0 ? string.Join(", ", shared) : "brak") + Environment.NewLine +
            "· to szorstka miara (bez znaczenia słów): 100% = te same słowa, nie ta sama myśl";
    }

    private static string SharedTopics(string input)
    {
        var texts = (input ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (texts.Count < 2)
            return "Użycie: „wspolne tematy: notatka A | notatka B | notatka C”. Wskażę pary łączone wspólnymi słowami.";
        var sets = texts.Select(x => TokenCounts(x).Keys.Take(12).ToHashSet(StringComparer.Ordinal)).ToList();
        var sb = new StringBuilder("ŁĄCZENIE NOTATEK (po wspólnych słowach treściowych):").AppendLine();
        bool any = false;
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
            {
                var shared = sets[i].Intersect(sets[j]).Take(3).ToList();
                sb.Append("· [").Append(i + 1).Append("]+[").Append(j + 1).Append("]: ")
                  .Append(shared.Count > 0 ? string.Join(", ", shared) : "brak wspólnych słów").AppendLine();
                if (shared.Count > 0) any = true;
            }
        sb.Append(any
            ? "· pary ze wspólnymi słowami warto połączyć linkiem w notatkach — sieć powiązań to prototyp drugiej pamięci"
            : "· notatki nie stykają się tematycznie — albo to osobne obszary, albo za krótkie fragmenty");
        return sb.ToString();
    }

    private static string KnowledgeMap(string input)
    {
        int arrow = (input ?? "").IndexOf('>');
        if (arrow <= 0)
            return "Użycie: „mapa wiedzy: programowanie > języki; algorytmy; narzędzia”. Zrobię konspekt gałęzi wiedzy.";
        string root = input[..arrow].Trim();
        var children = input[(arrow + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToList();
        var sb = new StringBuilder().Append(root).AppendLine();
        foreach (string child in children)
        {
            sb.Append("├─ ").Append(child).AppendLine();
            sb.Append("│    · co wiem już o tym? (3 zdania z pamięci — test prawdziwego rozumienia)").AppendLine();
        }
        sb.Append("· gałąź bez własnych słów to gałąź do nauki, nie do wykucia na pamięć");
        return sb.ToString();
    }

    private static string Index(string input)
    {
        var counts = TokenCounts(input);
        if (counts.Count == 0)
            return "Użycie: „indeks pojec: <wklej tekst notatki>”. Policzę słowa treściowe i zrobię indeks.";
        var sb = new StringBuilder("INDEKS POJĘĆ (").Append(counts.Keys.Count).Append(" unikalnych):").AppendLine();
        foreach (var pair in counts.Take(12))
            sb.Append("· ").Append(pair.Key).Append(": ").Append(pair.Value).Append('×').AppendLine();
        sb.Append("· najczęstsze pojęcie to kandydat na tytuł notatki i słowa kluczowe");
        return sb.ToString();
    }

    private static string Quiz(string input)
    {
        var counts = TokenCounts(input);
        var sentences = Regex.Split(input ?? "", @"(?<=[.!?])\s+").Where(x => x.Trim().Length > 15).ToList();
        if (counts.Count == 0 || sentences.Count == 0)
            return "Użycie: „pytania kontrolne: <kilka zdań notatek>”. Wygeneruję pytania sprawdzające, czy rozumiesz własne notatki.";
        var top = counts.Keys.Take(3).ToList();
        var sb = new StringBuilder("PYTANIA KONTROLNE (odpowiedz z pamięci, potem sprawdź w notatkach):").AppendLine();
        foreach (string term in top)
        {
            string ctx = sentences.FirstOrDefault(s => s.Contains(term, StringComparison.OrdinalIgnoreCase)) ?? sentences[0];
            ctx = ctx.Trim();
            if (ctx.Length > 80) ctx = ctx[..80];
            sb.Append("· Co potrafisz powiedzieć o „").Append(term).Append("”? (kontekst: ").Append(ctx).Append("…)").AppendLine();
        }
        sb.Append("· jeśli nie umiesz odpowiedzieć własnymi słowami — ta fiszka wraca do puli: „fiszki: …”");
        return sb.ToString();
    }

    // ————— pomocnicze —————

    private static Dictionary<string, int> TokenCounts(string input)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches((input ?? "").ToLowerInvariant(), "[a-ząćęłńóśźż0-9]{2,}"))
        {
            string t = Flat(m.Value);
            if (t.Length < 2 || StopWords.Contains(t)) continue;
            counts[t] = counts.GetValueOrDefault(t) + 1;
        }
        return counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    private static string[] SplitParts(string input, int max) =>
        (input ?? "").Split('|', max, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Flat(string input)
    {
        string s = (input ?? "").ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return sb.ToString();
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
}
