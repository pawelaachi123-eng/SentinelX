using System.Globalization;
using System.Text;

namespace SentinelX;

/// <summary>0.97 · offline password strength estimate (#143). Nothing leaves the machine; the password is never stored.</summary>
public static class PasswordStrength
{
    private static readonly string[] Common =
    [
        "haslo", "password", "123456", "12345678", "123456789", "qwerty", "qwertyuiop", "abc123", "zaq12wsx",
        "admin", "welcome", "letmein", "iloveyou", "monkey", "dragon", "sunshine", "princess", "football",
        "polska", "sentinel", "narodowe", "master", "login", "test", "qazwsx", "1q2w3e4r"
    ];

    public sealed record Report(int Score, string Verdict, double EntropyBits, IReadOnlyList<string> Findings, IReadOnlyList<string> Fixes)
    {
        public bool Weak => Score < 60;
    }

    public static Report Analyze(string? password)
    {
        string value = password ?? "";
        var findings = new List<string>();
        var fixes = new List<string>();
        if (value.Length == 0) return new Report(0, "Brak hasła — nic do oceny.", 0, ["Hasło jest puste."], ["Podaj hasło: siła hasła: twoje-haslo"]);

        int classes = (value.Any(char.IsLower) ? 1 : 0) + (value.Any(char.IsUpper) ? 1 : 0) +
                      (value.Any(char.IsDigit) ? 1 : 0) + (value.Any(ch => !char.IsLetterOrDigit(ch)) ? 1 : 0);
        int pool = (value.Any(char.IsLower) ? 26 : 0) + (value.Any(char.IsUpper) ? 26 : 0) +
                   (value.Any(char.IsDigit) ? 10 : 0) + (value.Any(ch => !char.IsLetterOrDigit(ch)) ? 33 : 0);
        double entropy = value.Length * Math.Log2(Math.Max(pool, 2));

        int score = 0;
        score += Math.Min(50, value.Length * 4);
        score += (classes - 1) * 10;
        if (value.Length >= 16) score += 10;
        if (value.Length < 8) score -= 20;

        findings.Add("Długość: " + value.Length + " znaków, klasy znaków: " + classes + "/4.");
        findings.Add("Szacowana entropia: " + entropy.ToString("0", CultureInfo.InvariantCulture) + " bitów (przy założeniu losowości).");

        string lowered = value.ToLowerInvariant();
        foreach (string common in Common)
            if (lowered.Contains(common, StringComparison.Ordinal))
            { score -= 45; findings.Add("Zawiera popularne słowo lub wzór: „" + common + "”."); fixes.Add("Usuń słownikowe słowa i popularne frazy."); break; }

        if (HasRun(value, out char runChar))
        { score -= 15; findings.Add("Powtarzający się znak: „" + runChar + "” obok siebie."); fixes.Add("Nie powtarzaj tego samego znaku pod rząd."); }
        if (HasSequence(value))
        { score -= 15; findings.Add("Wykryłem ciąg po kolei (np. abc, 123, qwerty)."); fixes.Add("Zmień kolejność — ciągi zgaduje się pierwsze."); }
        if (HasDate(value))
        { score -= 10; findings.Add("Hasło wygląda na datę (rok/rok+liczba)."); fixes.Add("Nie używaj dat urodzenia ani rocznic."); }

        int clamped = Math.Clamp(score, 0, 100);
        string verdict = clamped switch
        {
            >= 80 => "Mocne — trudne do odgadnięcia, jeśli nie używasz go nigdzie indziej.",
            >= 60 => "Przyzwoite — da się obronić, ale dłuższe byłoby lepsze.",
            >= 35 => "Słabe — do wymiany w serwisach, na których Ci zależy.",
            _ => "Bardzo słabe — traktuj je jako tymczasowe."
        };
        if (value.Length < 12) fixes.Add("Celuj w 14+ znaków: długość bije kombinacje znaków.");
        if (classes < 3) fixes.Add("Dodaj wielkie litery, cyfry i znaki specjalne.");
        if (fixes.Count == 0) fixes.Add("Dobra robota — jedyna rada: nie używaj tego hasła w dwóch miejscach.");
        return new Report(clamped, verdict, entropy, findings, fixes);
    }

    public static string Describe(string? password)
    {
        Report report = Analyze(password);
        var builder = new StringBuilder();
        builder.AppendLine("SIŁA HASŁA · " + report.Score + "/100 — " + report.Verdict);
        foreach (string finding in report.Findings) builder.AppendLine("· " + finding);
        builder.AppendLine("CO POPRAWIĆ");
        foreach (string fix in report.Fixes) builder.AppendLine("· " + fix);
        builder.AppendLine("Hasło nie zostało zapisane ani wysłane — policzyłem je w pamięci i zapominam.");
        return builder.ToString().TrimEnd();
    }

    private static bool HasRun(string value, out char run)
    {
        run = '\0';
        int count = 1;
        for (int i = 1; i < value.Length; i++)
        {
            count = value[i] == value[i - 1] ? count + 1 : 1;
            if (count < 3) continue;
            run = value[i]; return true;
        }
        return false;
    }

    private static bool HasSequence(string value)
    {
        string lowered = value.ToLowerInvariant();
        const string keyboard = "qwertyuiopasdfghjklzxcvbnm";
        for (int i = 0; i + 3 <= lowered.Length; i++)
        {
            string window = lowered[i..(i + 3)];
            if (window.Length < 3) continue;
            bool alphaUp = window[1] - window[0] == 1 && window[2] - window[1] == 1;
            bool digitUp = char.IsDigit(window[0]) && window[1] - window[0] == 1 && window[2] - window[1] == 1;
            if (alphaUp || digitUp) return true;
            if (window.Length == 3 && keyboard.Contains(window, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool HasDate(string value)
    {
        for (int i = 0; i + 4 <= value.Length; i++)
        {
            string window = value[i..(i + 4)];
            if (!window.All(char.IsDigit)) continue;
            if (int.TryParse(window, out int year) && year >= 1900 && year <= DateTime.Now.Year + 1) return true;
        }
        return false;
    }
}
