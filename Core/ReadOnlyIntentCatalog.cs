namespace SentinelX.Core;

public enum ReadOnlyIntent { RamTotal, RamUsed, RamFree, RamPercent, Cpu, Gpu, Disks, Uptime, Clock, Date }
/// <summary>Whole-command aliases only. Never fuzzy-match a negation or silently infer a mutation.</summary>
public static class ReadOnlyIntentCatalog
{
    public static IReadOnlyDictionary<string, ReadOnlyIntent> Aliases { get; } = Build();
    private static IReadOnlyDictionary<string, ReadOnlyIntent> Build()
    {
        var result = new Dictionary<string, ReadOnlyIntent>(StringComparer.Ordinal);
        void Add(ReadOnlyIntent intent, params string[] aliases) { foreach (var text in aliases) result.Add(text, intent); }
        Add(ReadOnlyIntent.RamTotal, "ile mam ram", "ile mam ramu", "ile mam pamieci ram", "ile mam pamieci operacyjnej", "calkowity ram", "ile jest ramu");
        Add(ReadOnlyIntent.RamUsed, "ile uzywam ram", "ile uzywam ramu", "ile uzywam pamieci ram", "uzycie ram", "uzycie ramu", "zuzycie ram", "zuzycie ramu", "pokaz pamiec", "pokaz ram", "zajetosc ram", "ram", "status ram");
        Add(ReadOnlyIntent.RamFree, "wolny ram", "wolna pamiec", "ile mam wolnego ramu", "ile wolnej pamieci", "dostepna pamiec ram");
        Add(ReadOnlyIntent.RamPercent, "procent ram", "uzycie ram procent", "ile procent ram", "jaki procent ram", "zuzycie ram w procentach");
        Add(ReadOnlyIntent.Cpu, "uzycie cpu", "ile uzywam cpu", "zuzycie cpu", "uzycie procesora", "obciazenie procesora", "pokaz cpu", "cpu");
        Add(ReadOnlyIntent.Gpu, "uzycie gpu", "obciazenie gpu", "ile uzywam gpu", "pokaz gpu");
        Add(ReadOnlyIntent.Disks, "dyski", "pokaz dyski", "miejsce na dysku", "wolne miejsce na dyskach", "ile miejsca na dyskach");
        Add(ReadOnlyIntent.Uptime, "uptime", "jak dlugo dziala komputer", "ile dziala komputer", "czas pracy komputera");
        Add(ReadOnlyIntent.Clock, "ktora godzina", "jaka jest godzina", "godzina");
        Add(ReadOnlyIntent.Date, "jaka dzis data", "jaka jest data", "dzisiejsza data", "data");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ReadOnlyIntent>(result);
    }
    public static bool TryResolve(string input, out ReadOnlyIntent intent)
    {
        string text = CommandText.Normalize(input);
        if (text.StartsWith("prosze ", StringComparison.Ordinal)) text = text[7..];
        return Aliases.TryGetValue(text, out intent);
    }
}
