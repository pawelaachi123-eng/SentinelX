namespace SentinelX.Core;

public enum ReadOnlyIntent { RamTotal, RamUsed, RamSummary, RamFree, RamPercent, Cpu, Gpu, Disks, Uptime, Clock, Date }
/// <summary>Whole-command aliases only. Never fuzzy-match a negation or silently infer a mutation.
/// 0.94: the table grew — natural phrasings („jak działa procesor”, „ile mam miejsca na dysku”)
/// resolve deterministically instead of falling through to the model.</summary>
public static class ReadOnlyIntentCatalog
{
    public static IReadOnlyDictionary<string, ReadOnlyIntent> Aliases { get; } = Build();
    private static IReadOnlyDictionary<string, ReadOnlyIntent> Build()
    {
        var result = new Dictionary<string, ReadOnlyIntent>(StringComparer.Ordinal);
        void Add(ReadOnlyIntent intent, params string[] aliases) { foreach (var text in aliases) result.Add(text, intent); }
        Add(ReadOnlyIntent.RamTotal, "ile mam ram", "ile mam ramu", "ile mam pamieci ram", "ile mam pamieci operacyjnej", "calkowity ram", "ile jest ramu",
            "ile mam ramu lacznie", "ile ramu jest zainstalowane", "pojemnosc ram", "jak duzo ramu mam", "ile pamieci operacyjnej mam", "pamiec operacyjna");
        Add(ReadOnlyIntent.RamUsed, "ile uzywam ram", "ile uzywam ramu", "ile uzywam pamieci ram", "uzycie ram", "uzycie ramu", "zuzycie ram", "zuzycie ramu", "zajetosc ram",
            "jak duzo ramu uzywam", "obciazenie pamieci", "uzycie pamieci", "ile ramu jest zajete", "zajeta pamiec");
        Add(ReadOnlyIntent.RamSummary, "ram", "pokaz ram", "status ram", "pokaz pamiec ram",
            "pokaz zuzycie ram", "podsumowanie ram", "pamiec ram", "stan pamieci ram", "jak dziala pamiec ram");
        Add(ReadOnlyIntent.RamFree, "wolny ram", "wolna pamiec", "ile mam wolnego ramu", "ile wolnej pamieci", "dostepna pamiec ram",
            "ile mam wolnej pamieci", "wolna pamiec operacyjna", "ile wolnego ramu zostalo");
        Add(ReadOnlyIntent.RamPercent, "procent ram", "uzycie ram procent", "ile procent ram", "jaki procent ram", "zuzycie ram w procentach",
            "procent uzycia ram", "jaki procent pamieci", "ile procent pamieci");
        Add(ReadOnlyIntent.Cpu, "uzycie cpu", "ile uzywam cpu", "zuzycie cpu", "uzycie procesora", "obciazenie procesora", "pokaz cpu", "cpu",
            "jak dziala procesor", "obciazenie cpu", "procesor uzycie", "ile procesor uzywa", "pokaz uzycie procesora",
            "uzycie mojego procesora", "stan procesora", "pokaz procesor");
        Add(ReadOnlyIntent.Gpu, "uzycie gpu", "obciazenie gpu", "ile uzywam gpu", "pokaz gpu",
            "obciazenie karty graficznej", "uzycie karty graficznej", "jak dziala karta graficzna", "karta graficzna uzycie");
        Add(ReadOnlyIntent.Disks, "dyski", "pokaz dyski", "miejsce na dysku", "wolne miejsce na dyskach", "ile miejsca na dyskach",
            "ile mam miejsca na dysku", "stan dyskow", "pokaz stan dyskow", "pojemnosc dyskow", "wolne miejsce na dysku");
        Add(ReadOnlyIntent.Uptime, "uptime", "jak dlugo dziala komputer", "ile dziala komputer", "czas pracy komputera",
            "od kiedy dziala komputer", "jak dlugo komputer dziala", "czas dzialania komputera");
        Add(ReadOnlyIntent.Clock, "ktora godzina", "jaka jest godzina", "godzina",
            "ktora teraz godzina", "pokaz godzine", "jaka jest teraz godzina", "godzina teraz");
        Add(ReadOnlyIntent.Date, "jaka dzis data", "jaka jest data", "dzisiejsza data", "data",
            "jaki dzisiaj dzien", "jaka jest dzisiejsza data", "dzien dzisiejszy", "data dzisiejsza", "jaki dzien jest dzisiaj");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ReadOnlyIntent>(result);
    }
    public static bool TryResolve(string input, out ReadOnlyIntent intent)
    {
        string text = CommandText.Normalize(input);
        if (text.StartsWith("prosze ", StringComparison.Ordinal)) text = text[7..];
        return Aliases.TryGetValue(text, out intent);
    }
}
