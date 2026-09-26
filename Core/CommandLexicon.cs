using System.Text.RegularExpressions;

namespace SentinelX.Core;

/// <summary>0.94 · wspólne leksykon rozumienia: synonimy czasowników, odmiana (ogiery), słowa
/// dekoracyjne i dopasowanie nieostre słów. Używane przez CommandUnderstanding i wyszukiwanie
/// w pamięci. Zasada bezpieczeństwa: leksykon służy wyłącznie do PORÓWNYWANIA tekstu — zawsze
/// odtwarzamy kanoniczną frazę z bezpiecznego katalogu, nigdy nie przepisujemy na polecenia
/// niszczące (katalog ich nie zawiera).</summary>
public static class CommandLexicon
{
    /// <summary>Synonimy czasowników — składane do formy kanonicznej WYŁĄCZNIE na potrzeby
    /// dopasowania. Tekst wykonywany to zawsze fraza z katalogu, nie przepisane słowo.</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        // uruchamianie
        ["odpal"] = "wlacz", ["odpalaj"] = "wlacz", ["odpalcie"] = "wlacz", ["zaladuj"] = "wlacz",
        ["uruchom"] = "wlacz", ["uruchomic"] = "wlacz", ["uruchamiaj"] = "wlacz", ["wlanczaj"] = "wlacz",
        ["wlaczyc"] = "wlacz", ["wlaczmy"] = "wlacz", ["start"] = "wlacz", ["wystartuj"] = "wlacz",
        // pokazywanie
        ["sprawdz"] = "pokaz", ["sprawdzic"] = "pokaz", ["sprawdzmy"] = "pokaz", ["sprawdzam"] = "pokaz",
        ["zobacz"] = "pokaz", ["zobaczmy"] = "pokaz", ["podaj"] = "pokaz", ["podac"] = "pokaz",
        ["pokazac"] = "pokaz", ["wyswietl"] = "pokaz", ["wyswietlic"] = "pokaz", ["wypisz"] = "pokaz",
        ["pokazmy"] = "pokaz", ["pokazuja"] = "pokaz",
        // mówienie
        ["powiedziec"] = "powiedz", ["powiedzmy"] = "powiedz", ["napisz"] = "powiedz", ["napisac"] = "powiedz",
        // tworzenie
        ["zrob"] = "dodaj", ["zrobic"] = "dodaj", ["utworz"] = "dodaj", ["utworzyc"] = "dodaj", ["stworz"] = "dodaj",
        ["dodaje"] = "dodaj", ["dodac"] = "dodaj", ["wpisz"] = "dodaj", ["wpisac"] = "dodaj",
        // liczenie
        ["oblicz"] = "policz", ["policzyc"] = "policz", ["przelicz"] = "policz", ["przeliczyc"] = "policz",
        ["wylicz"] = "policz", ["zsumuj"] = "suma",
    };

    /// <summary>Słowa, które można pominąć w zdaniu polecenia bez zmiany intencji:
    /// uprzejmości, przysłówki, spójniki potoczne. NIE zawierają czasowników intencji
    /// (napisz, opowiedz, narysuj…), określeń zakresu („dziś” — treść, nie dekoracja)
    /// ani przyimków argumentów (na, do, o, w, z).</summary>
    public static readonly IReadOnlySet<string> Decorations = new HashSet<string>(StringComparer.Ordinal)
    {
        "prosze", "prosie", "poprosze", "mi", "cie", "ci", "nam", "szybko", "moze",
        "chyba", "wiesz", "jakby", "no", "wiec", "tylko", "dokladnie", "konkretnie", "po prostu",
        "wlasnie", "zwyczajnie", "takze", "tez",
        "dzieki", "dziekuje", "z gory", "bym", "bys", "sobie", "sorki", "przepraszam",
    };

    /// <summary>Słowa pytające/pomocnicze, które poprzedzają lub następują po właściwym poleceniu
    /// („jakie jest użycie procesora”, „powiedz mi ile mam ramu”). Występują tylko POZA frazą
    /// polecenia. Świadomie NIE ma tu „dziś” ani „teraz” — to treść, nie dekoracja; zdania
    /// z nimi trafiają do szarej strefy pytań zamiast wykonywać się po cichu.</summary>
    public static readonly IReadOnlySet<string> Inquiry = new HashSet<string>(StringComparer.Ordinal)
    {
        "powiedz", "powiedziec", "sprawdz", "sprawdzic", "sprawdzmy", "pokaz", "pokazac", "podaj", "podac",
        "wyswietl", "wyswietlic", "wypisz", "zacznij", "zaczel", "zaczelam", "kontynuuj", "sprobuj",
        "jakie", "jaki", "jaka", "jacy", "ktora", "ktore", "ktory", "co", "gdzie", "kiedy",
        "jest", "sa", "mam", "masz", "mamy", "moj", "moja", "moje", "moich", "obecne", "obecny", "obecna",
        "lacznie", "razem", "prosze", "mi", "czy", "moglbys", "moglabys",
        "moglby", "moglaby", "mozesz", "moze", "chcialbym", "chcialabym", "chce", "chcial", "chciala",
        "jak", "cos", "sam", "sama", "wtedy", "wiec", "no", "po", "prostu", "na", "szybko",
        "dla", "mnie", "sie", "to", "tam", "ten", "ta", "te", "ty", "wy", "pan", "pani",
        "sprawdziles", "sprawdzilas", "podasz", "pokazesz", "powiedzie",
    };

    /// <summary>Czasowniki rozmowy, które ZABRANIAJĄ automatycznej ekstrakcji polecenia:
    /// gdy użytkownik prosi o tekst („napisz wiersz o tym ile mam ramu”), polecenie w środku
    /// nie może przechwycić zdania.</summary>
    private static readonly string[] ConversationVerbs =
    [
        "napisz", "napisac", "opowiedz", "opowiadaj", "opowiedziec", "narysuj", "narysowac",
        "przetlumacz", "przetlumaczyc", "wytlumacz", "wyjasnij", "wyjasnic", "opisz", "opisac",
        "skomponuj", "wymysl", "wymyslic", "zaprojektuj", "zbuduj", "zmodeluj", "zagraj", "spiewaj",
        "podsumuj", "streść", "stresc", "tłumacz", "tlumacz",
    ];

    /// <summary>Forma do porównań: małe litery, bez znaków przestankowych na krawędziach,
    /// synonimy złożone do formy kanonicznej.</summary>
    public static string CompareForm(string word)
    {
        string trimmed = (word ?? "").Trim().TrimEnd('„', '”', '"', '!', '?', '.', ',', ':', ';')
            .TrimStart('„', '”', '"').ToLowerInvariant();
        return Synonyms.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
    }

    /// <summary>Czy słowo jest dekoracją (można je pominąć bez zmiany intencji).</summary>
    public static bool IsDecoration(string word) => Decorations.Contains((word ?? "").ToLowerInvariant().TrimEnd('.', ',', '!', '?', ':'));

    /// <summary>Czy słowo jest dopuszczalne POZA frazą polecenia (dekoracja albo słowo pytające).</summary>
    public static bool IsOutsideWord(string word)
    {
        string form = (word ?? "").ToLowerInvariant().TrimEnd('.', ',', '!', '?', ':');
        return Decorations.Contains(form) || Inquiry.Contains(form) || form.Length == 0;
    }

    /// <summary>Czy tekst zawiera czasownik rozmowy — wtedy nic nie przechwytujemy automatycznie.</summary>
    public static bool ContainsConversationVerb(IEnumerable<string> words)
    {
        foreach (string word in words)
        {
            string form = CompareForm(word);
            foreach (string verb in ConversationVerbs)
                if (form == verb || form.StartsWith(verb, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Bezpieczne ogierki polskiej odmiany: „pamieci”→„pamiec”, „ramu”→„ram”,
    /// „komputera”→„komputer”. Tylko do porównań; przycięcie wymaga ≥3 znaków w ogonie.</summary>
    public static string Stem(string word)
    {
        string w = (word ?? "").Trim().TrimEnd('„', '”', '"', '!', '?', '.', ',', ':', ';').ToLowerInvariant();
        if (w.Length <= 3) return w;
        string[] suffixes = ["owiego", "owego", "owych", "owemu", "owymi", "niami", "anie", "enie", "nosci", "owsc", "ami", "ach", "owi", "osc", "om", "ow", "em", "ie", "ej", "ym", "u", "y", "a", "e", "i"];
        foreach (string suffix in suffixes)
        {
            if (w.Length - suffix.Length >= 3 && w.EndsWith(suffix, StringComparison.Ordinal))
                return w[..^suffix.Length];
        }
        return w;
    }

    /// <summary>Czy dwa słowa opisują tę samą formę: dokładne, odmiana (ogiery), albo literówka.
    /// Krótkie słowa (poniżej 4 znaków) muszą być dokładne — jak w naprawie poleceń.</summary>
    public static bool WordsMatch(string a, string b)
    {
        string ca = CompareForm(a), cb = CompareForm(b);
        if (ca.Length == 0 || cb.Length == 0) return false;
        if (string.Equals(ca, cb, StringComparison.Ordinal)) return true;
        if (ca.Length < 4 || cb.Length < 4) return false;
        if (Stem(ca) == Stem(cb)) return true;
        // Jedna literówka/transpozycja na krótkie słowo — ten sam próg co naprawa pojedynczych słów.
        return CommandUnderstanding.Similarity(ca, cb) >= 0.86;
    }

    /// <summary>Dopasowanie nieostre do wyszukiwania treści (wspomnienia, rozmowa): dokładne,
    /// odmiana (ten sam ogier) albo jedna literówka przy słowach ≥5 znaków. Wyszukiwanie
    /// pozostaje tekstowe — bez semantyki; krótkie słowa muszą być dokładne.</summary>
    public static bool SearchTokenMatches(string queryToken, string textToken)
    {
        string ca = CompareForm(queryToken), cb = CompareForm(textToken);
        if (ca.Length == 0 || cb.Length == 0) return false;
        if (string.Equals(ca, cb, StringComparison.Ordinal)) return true;
        if (ca.Length < 5 || cb.Length < 5) return false;
        if (Stem(ca) == Stem(cb)) return true;
        return CommandUnderstanding.Distance(ca, cb, 2) <= 1;
    }
}
