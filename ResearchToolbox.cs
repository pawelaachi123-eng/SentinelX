using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 12 · pozycje 881–940 — research bez internetu. Sentinel niczego nie pobiera: pracuje na
/// tekście i danych, które dostaje w poleceniu. Buduje cytowania, bibliografię, plan badania,
/// słowa kluczowe, zapytania do wyszukiwarki (do wklejenia), macierze porównania, karty faktów
/// i pytania badawcze. Gdyby ktoś poprosił o „wyszukaj w sieci” — odpowiedź brzmi: to nie tu.
/// </summary>
public static class ResearchToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly string[] StopWords =
    {
        "i","a","oraz","lub","w","we","na","z","ze","do","od","dla","nie","jest","sa","to","ten","ta","te","sie",
        "the","of","and","or","in","on","at","is","are","be","by","an","as","it","with","for","ma","ale","co","jak",
        "po","za","przy","przez","ktory","ktora","ktore","tak","ze","jego","jej","juz","jeszcze","bedzie","byl",
    };

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "cytuj apa")) return Cite(Payload(raw, "cytuj apa"), "apa");
        if (Is(norm, "cytuj ieee")) return Cite(Payload(raw, "cytuj ieee"), "ieee");
        if (Is(norm, "cytuj")) return "Style: „cytuj apa: autor | rok | tytuł | źródło” albo „cytuj ieee: …”. Sentinel nie pobiera publikacji — tylko formatuje dane, które mu dasz.";
        if (Is(norm, "bibliografia")) return Bibliography(Payload(raw, "bibliografia"));
        if (Is(norm, "wiarygodnosc")) return Credibility(Payload(raw, "wiarygodnosc"));
        if (Is(norm, "plan badan")) return ResearchPlan(Payload(raw, "plan badan"));
        if (Is(norm, "slowa kluczowe")) return Keywords(Payload(raw, "slowa kluczowe"));
        if (Is(norm, "zapytanie")) return Query(Payload(raw, "zapytanie"));
        if (Is(norm, "macierz porownania")) return Matrix(Payload(raw, "macierz porownania"));
        if (Is(norm, "podsumuj notatki")) return SummarizeNotes(Payload(raw, "podsumuj notatki"));
        if (Is(norm, "fakt zapisz")) return FactCard(Payload(raw, "fakt zapisz"));
        if (Is(norm, "pytania badawcze")) return ResearchQuestions(Payload(raw, "pytania badawcze"));
        return null;
    }

    private static string Cite(string input, string style)
    {
        string[] p = SplitParts(input, 4);
        if (p.Length < 3 || p[0].Length == 0 || p[1].Length == 0 || p[2].Length == 0)
            return "Użycie: „cytuj apa: Kowalski | 2024 | Tytuł | Wydawnictwo”. Formatuję dane, które podasz — niczego nie sprawdzam w sieci.";
        string author = p[0], year = p[1], title = p[2], source = p.Length > 3 ? p[3] : "";
        return style == "apa"
            ? "APA: " + author + " (" + year + "). " + title + "." + (source.Length > 0 ? " " + source + "." : "")
            : "IEEE: [1] " + author + ", „" + title + "”" + (source.Length > 0 ? ", " + source : "") + ", " + year + ".";
    }

    private static string Bibliography(string input)
    {
        var entries = (input ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count == 0)
            return "Użycie: „bibliografia: Nowak 2020 | Adamska 2021 | Kowalski 2019”. Posortuję i ponumeruję alfabetycznie.";
        var sb = new StringBuilder("BIBLIOGRAFIA (alfabetycznie, ").Append(entries.Count).Append(" pozycji):").AppendLine();
        int i = 1;
        foreach (string e in entries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            sb.Append('[').Append(i++).Append("] ").Append(e).AppendLine();
        sb.Append("· spójny format robi różnicę: wybierz jeden styl („cytuj apa: …”) i trzymaj się go w całej liście");
        return sb.ToString();
    }

    private static string Credibility(string input)
    {
        string what = Flat(input).Trim();
        string base_ =
            "· KTO: autor znany z imienia i nazwiska? ma kwalifikacje w tej dziedzinie?" + Environment.NewLine +
            "· KIEDY: data publikacji i aktualizacji — dane się starzeją" + Environment.NewLine +
            "· ŹRÓDŁA: czy tekst sam podaje źródła, które da się sprawdzić?" + Environment.NewLine +
            "· KONFLIKT: kto płaci za tę treść? co autor zyskuje, gdy uwierzysz?" + Environment.NewLine +
            "· POWTARZALNOŚĆ: czy niezależne źródło podaje to samo?";
        return what switch
        {
            "praca naukowa" or "nauka" or "artykul naukowy" =>
                "WIARYGODNOŚĆ — PRACA NAUKOWA (zwykle najwyższa, ale sprawdź):" + Environment.NewLine +
                "· recenzowana? (peer review) i gdzie opublikowana (predatory journals istnieją)" + Environment.NewLine +
                "· liczba cytowań i replikacje — pojedyncze badanie to nie dowód" + Environment.NewLine + base_,
            "dokumentacja" =>
                "WIARYGODNOŚĆ — DOKUMENTACJA (dobra dla szczegółów, stronnicza wobec produktu):" + Environment.NewLine +
                "· czy to dokumentacja twórcy (dla własnego produktu) czy niezależna?" + Environment.NewLine +
                "· sprawdź wersję — dokumentacja starej wersji potrafi zmylić" + Environment.NewLine + base_,
            "wiadomosci" or "prasa" =>
                "WIARYGODNOŚĆ — MEDIA (szkło powiększające):" + Environment.NewLine +
                "· czy to informacja, czy opinia? nagłówek często obiecuje więcej niż treść" + Environment.NewLine +
                "· kto jest cytowany? „znawcy mówią” bez nazwisk to czerwona flaga" + Environment.NewLine + base_,
            "blog" =>
                "WIARYGODNOŚĆ — BLOG (doświadczenie autora, brak recenzji):" + Environment.NewLine +
                "· czy autor pokazuje dane/kod, czy tylko wnioski? czy opisuje też porażki?" + Environment.NewLine +
                "· sprawdź datę — stare wpisy o narzędziach bywają nieaktualne" + Environment.NewLine + base_,
            "forum" or "media spolecznosciowe" =>
                "WIARYGODNOŚĆ — FORUM/SOCJALNE (najniższa, najczęściej zmieniana):" + Environment.NewLine +
                "· głosy to nie fakty: popularna odpowiedź bywa błędna, a jego autor może nie być ekspertem" + Environment.NewLine +
                "· szukaj odpowiedzi z dowodem (link, pomiar), nie z emocją" + Environment.NewLine + base_,
            _ => "WIARYGODNOŚĆ ŹRÓDŁA — pięć pytań (wpisz typ: „wiarygodnosc: praca naukowa | dokumentacja | wiadomosci | blog | forum”):" + Environment.NewLine + base_,
        };
    }

    private static string ResearchPlan(string input)
    {
        string topic = (input ?? "").Trim();
        if (topic.Length == 0)
            return "Użycie: „plan badan: lokalne modele na słabym sprzęcie”. Dostaniesz szkielet badania do uzupełnienia.";
        return "PLAN BADANIA: " + topic + Environment.NewLine +
            "· pytanie główne: na co dokładnie chcesz odpowiedzieć? (jedno zdanie, mierzalne)" + Environment.NewLine +
            "· pytania szczegółowe: 3 sztuki — tak szerokie, by pokryć temat, tak wąskie, by dało się odpowiedzieć" + Environment.NewLine +
            "· źródła: dokumentacja (fakty) → artykuły (kontekst) → fora/praktyka (pułapki w praktyce)" + Environment.NewLine +
            "· metoda: co uznasz za odpowiedź? (pomiar, porównanie, wywiad — zapisz PRZED zbieraniem)" + Environment.NewLine +
            "· ryzyka: dane nieaktualne, źródła jednego typu, efekt potwierdzenia — załóż kontrapunkt" + Environment.NewLine +
            "· kryterium stopu: kiedy uznajesz badanie za skończone? (termin albo sytość źródeł)";
    }

    private static string Keywords(string input)
    {
        var counts = TokenCounts(input);
        if (counts.Count == 0)
            return "Użycie: „slowa kluczowe: <wklej tekst>”. Policzę słowa treściowe (bez „i”, „nie”, „the”…).";
        var sb = new StringBuilder("SŁOWA KLUCZOWE (").Append(counts.Values.Sum()).Append(" słów treściowych):").AppendLine();
        foreach (var pair in counts.Take(8))
            sb.Append("· ").Append(pair.Key).Append(": ").Append(pair.Value).AppendLine();
        sb.Append("· do zapytania w wyszukiwarce weź 2–3 najczęstsze: „zapytanie: …”");
        return sb.ToString();
    }

    private static string Query(string input)
    {
        string phrase = (input ?? "").Trim();
        if (phrase.Length < 2)
            return "Użycie: „zapytanie: lokalne modele ai”. Zbuduję 4 warianty zapytania do wklejenia w wyszukiwarkę — nie pobieram nic sam.";
        return "ZAPYTANIA (wklej do wyszukiwarki; Sentinel niczego nie pobiera):" + Environment.NewLine +
            "1) „" + phrase + "” — dokładna fraza" + Environment.NewLine +
            "2) „" + phrase + "” filetype:pdf — raporty i artykuły" + Environment.NewLine +
            "3) „" + phrase + "” (site:wikipedia.org OR site:edu.pl) — tło i nauka" + Environment.NewLine +
            "4) „" + phrase + "” -site:facebook.* -site:pinterest.* — bez pętli portalów";
    }

    private static string Matrix(string input)
    {
        string[] p = SplitParts(input, 3);
        if (p.Length < 3) return "Użycie: „macierz porownania: Opcja A | Opcja B | cena;jakość;wsparcie”. Dostaniesz tabelę do uzupełnienia.";
        var criteria = p[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToList();
        var sb = new StringBuilder();
        sb.Append("| kryterium | ").Append(p[0]).Append(" | ").Append(p[1]).Append(" |").AppendLine();
        sb.Append("|---|---|---|").AppendLine();
        foreach (string c in criteria) sb.Append("| ").Append(c).Append(" |  |  |").AppendLine();
        sb.Append("· wypełniaj samymi faktami mierzalnymi; „lepsze/gorsze” zostaw na końcowy wniosek");
        return sb.ToString();
    }

    private static string SummarizeNotes(string input)
    {
        var sentences = Regex.Split(input ?? "", @"(?<=[.!?])\s+").Where(x => x.Trim().Length > 15).ToList();
        if (sentences.Count < 2)
            return "Użycie: „podsumuj notatki: <kilka zdań>”. Wybiorę 2 najbardziej treściowe zdania twojego tekstu — nie dopisuję nic od siebie.";
        var counts = TokenCounts(input);
        var ranked = sentences.Select((s, i) => (Text: s.Trim(), Index: i, Score: TokenCounts(s).Where(x => counts.ContainsKey(x.Key)).Sum(x => x.Value)))
            .OrderByDescending(x => x.Score).Take(2).OrderBy(x => x.Index).ToList();
        return "PODSUMOWANIE NOTATEK (twoje zdania, wybrane wg gęstości słów treściowych):" + Environment.NewLine +
            string.Join(Environment.NewLine, ranked.Select(x => "· " + x.Text)) + Environment.NewLine +
            "· to streszczenie wycinkowe (twoje zdania), nie przepisanie myślami — do wniosków potrzebujesz siebie";
    }

    private static string FactCard(string input)
    {
        string[] p = SplitParts(input, 2);
        if (p.Length < 2 || p[0].Length == 0)
            return "Użycie: „fakt zapisz: <fakt> | <źródło>”. Karta faktu z miejscem na źródło — wklej ją do swoich notatek.";
        return "FAKT: " + p[0] + Environment.NewLine +
            "· źródło: " + p[1] + Environment.NewLine +
            "· zapisano: " + DateTime.Now.ToString("dd.MM.yyyy", Pl) + " · status: do weryfikacji u źródła" + Environment.NewLine +
            "· fakt bez źródła to plotka — dlatego pole źródła jest obowiązkowe";
    }

    private static string ResearchQuestions(string input)
    {
        string topic = (input ?? "").Trim();
        if (topic.Length < 3)
            return "Użycie: „pytania badawcze: prywatność lokalnych modeli”. Rozwinę temat w 6 pytań (5W+H).";
        return "PYTANIA BADAWCZE: " + topic + Environment.NewLine +
            "· KTO: kto jest zainteresowany i kto ponosi konsekwencje? " + Environment.NewLine +
            "· CO: co dokładnie chcę wiedzieć o „" + topic + "”?" + Environment.NewLine +
            "· KIEDY: od kiedy to istnieje i co się zmieniło ostatnio?" + Environment.NewLine +
            "· GDZIE: gdzie to sprawdzać (dokumentacja, pomiary, osoby)? " + Environment.NewLine +
            "· JAK: jak to działa i jak to zweryfikować samodzielnie?" + Environment.NewLine +
            "· DLACZEGO: dlaczego jest tak, a nie inaczej — i kto na tym zyskuje?";
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
