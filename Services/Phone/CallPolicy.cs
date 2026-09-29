using System.Globalization;
using System.Text.RegularExpressions;
using SentinelX;

namespace SentinelX.Services.Phone;

/// <summary>Wyekstrahowane zadanie telefoniczne z polecenia właściciela. Zero wymyślania —
/// czego nie ma w poleceniu, tego nie ma w zadaniu.</summary>
public sealed record PhoneTask(
    string RawCommand,
    string PlaceName,
    string Number,
    string WhenText,
    int PartySize,
    string SubjectText,
    string CallbackNumber,
    string OwnerName)
{
    public bool HasNumber => Number.Length > 0;
    public bool MissingEssentials => PlaceName.Length == 0 && !HasNumber;
    public string KnownDataLine =>
        "Imię: " + (OwnerName.Length > 0 ? OwnerName : "nie podane")
        + " · numer kontaktowy: " + (CallbackNumber.Length > 0 ? CallbackNumber : "nie podany")
        + " · termin: " + (WhenText.Length > 0 ? WhenText : "nie podany")
        + " · liczba osób: " + (PartySize > 0 ? PartySize.ToString(CultureInfo.InvariantCulture) : "nie podana");
}

/// <summary>Czysta (bez I/O) polityka rozmowy: ekstrakcja zadania, czujnik ZMIANY MATERIALNEJ,
/// czujnik POTWIERDZENIA i budowa krótkiego podsumowania. Deterministyczna — Sentinel pyta
/// właściciela zawsze, gdy rozmówca proponuje inną godzinę, datę, cenę, opłatę lub warunek.</summary>
public static partial class CallPolicy
{
    [GeneratedRegex(@"\+?48[\s-]?\d{3}[\s-]?\d{3}[\s-]?\d{3}|\b\d{9,11}\b")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b\d{1,2}[:.]\d{2}\b")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"dla\s+(\d{1,2}|dw(o|óch|óch)|trzech|czterech|cztery|pi(e|ę)ciu|pieciu|sze(s|ś)ciu|siedmiu|o(s|ś)miu|osmiu|dziewi(e|ę)ciu|dziewiciu|dziesi(e|ę)ciu)\s+os", RegexOptions.IgnoreCase)]
    private static partial Regex PartyRegex();

    private static readonly string[] DayWords = ["jutro", "dzis", "pojutze", "poniedzialek", "wtorek", "srode", "sroda", "czwartek", "piatek", "sobote", "sobota", "niedziele", "niedziela", "weekend"];

    private static readonly string[] MaterialKeywords =
    [
        "oplata", "zaliczka", "kaucja", "przedplata", "doplata", "koszt", "cena wyzsza", "drozej",
        "zusznio", "nie ma wolnych", "brak wolnych", "zajete", "wypelnione", "nie mozemy",
        "innego dnia", "innym dniu", "inna godzina", "innej godzinie", "mozemy zaoferowac", "mozemy zaproponowac", "mamy",
    ];

    private static readonly string[] ConfirmationKeywords =
    [
        "zarezerwowane", "zarezerwowalem", "zarezerwowana", "potwierdzam", "potwierdzone", "potwierdzam rezerwacje",
        "rezerwacja przyjeta", "przyjeta", "zapisalem", "zapisane", "zapisuje", "masz stolik", "stolik bedzie",
        "do zobaczenia", "do uslyszenia", "udanej rezerwacji", "zapiszemy", "zamowienie przyjete", "zamowienie zlozone",
    ];

    /// <summary>Ekstrakcja zadania z znormalizowanego polecenia. Diakrytyki są już zdjęte przez CommandText.Normalize.</summary>
    public static PhoneTask Parse(string rawCommand, string normalized, string callbackNumber, string ownerName = "")
    {
        string place = ExtractPlace(normalized);
        string number = FindPhoneNumber(normalized) ?? "";
        string when = ExtractWhen(normalized);
        int party = ExtractParty(normalized);
        string subject = ExtractSubject(normalized);
        return new PhoneTask(rawCommand, place, number, when, party, subject, callbackNumber, ownerName);
    }

    /// <summary>Pierwszy numer telefonu w tekście; 9-cyfrowe zapisuje jako +48…</summary>
    public static string? FindPhoneNumber(string text)
    {
        var match = PhoneRegex().Match(text ?? "");
        if (!match.Success) return null;
        string digits = Regex.Replace(match.Value, @"\D", "");
        if (digits.Length == 9) return "+48" + digits;
        if (digits.Length == 11 && digits.StartsWith("48", StringComparison.Ordinal)) return "+" + digits;
        return match.Value.Trim();
    }

    /// <summary>Czujnik zmiany materialnej: inna godzina, słowa o opłacie/zaliczce/braku miejsc.
    /// Zwraca pytanie do właściciela albo null, gdy zmiany nie ma.</summary>
    public static string? DetectMaterialChange(string farEndText, PhoneTask task)
    {
        string text = ConversationMemoryService.Normalize(farEndText ?? "");
        if (text.Length == 0) return null;
        var offered = TimeRegex().Matches(text);
        var requested = TimeRegex().Match(task.WhenText);
        foreach (var offer in offered)
        {
            string offerText = offer.ToString().Replace('.', ':');
            if (!requested.Success || offerText != requested.Value.Replace('.', ':'))
                return "Rozmówca proponuje " + offerText
                    + (requested.Success ? " zamiast " + requested.Value : "")
                    + ". Przyjąć? (tak/nie)";
        }
        foreach (string keyword in MaterialKeywords)
            if (text.Contains(keyword, StringComparison.Ordinal))
                return "Rozmówca mówi o dodatkowych warunkach („" + keyword + "”). Przyjąć? (tak/nie)";
        return null;
    }

    /// <summary>Czy rozmówca potwierdził wykonanie celu (rezerwację/zamówienie).</summary>
    public static bool IsConfirmation(string farEndText)
    {
        string text = ConversationMemoryService.Normalize(farEndText ?? "");
        foreach (string keyword in ConfirmationKeywords)
            if (text.Contains(keyword, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Krótkie, ludzkie podsumowanie: „Gotowe. Jutro 18:00, 4 osoby.”</summary>
    public static string SummaryFor(PhoneTask task)
    {
        var parts = new List<string>();
        if (task.WhenText.Length > 0) parts.Add(task.WhenText);
        if (task.PartySize > 0) parts.Add(task.PartySize.ToString(CultureInfo.InvariantCulture) + " os.");
        if (task.SubjectText.Length > 0 && task.WhenText.Length == 0) parts.Add(task.SubjectText);
        return parts.Count > 0 ? "Gotowe. " + string.Join(", ", parts) + "." : "Gotowe — rozmówca potwierdził.";
    }

    public static string PauseLineFor(string question) =>
        "Dobrze, jeszcze potwierdzę i oddzwonię. Dziękuję bardzo.";

    private static string ExtractPlace(string normalized)
    {
        var match = Regex.Match(normalized, @"\bzadzwon(?:iec)?\s+do\s+(?<target>.+?)(?=\s+i\s|\s+i\b|,|\s+na\s+\+?\d|$)");
        if (!match.Success)
            match = Regex.Match(normalized, @"\btelefon(?:em)?\s+do\s+(?<target>.+?)(?=\s+i\s|,|\s+na\s+\+?\d|$)");
        if (!match.Success) return "";
        string target = match.Groups["target"].Value.Trim();
        // „restauracji Fiesta” → „Fiesta” (rodzajnik miejsca nie jest nazwą)
        string[] kinds = ["restauracji ", "restauracja ", "pizzerii ", "pizzeria ", "kawiarni ", "kawiarnia ", "gabinetu ", "gabinet ", "firy ", "firma ", "salonu ", "salon ", "klienta ", "klient "];
        foreach (string kind in kinds)
            if (target.StartsWith(kind, StringComparison.Ordinal)) { target = target[kind.Length..]; break; }
        return target.Trim().TrimEnd('.', ',').Trim();
    }

    private static string ExtractWhen(string normalized)
    {
        var pieces = new List<string>();
        foreach (string day in DayWords)
        {
            var dayMatch = Regex.Match(normalized, @"\b" + day + @"\b");
            if (dayMatch.Success) { pieces.Add(dayMatch.Value); break; }
        }
        var time = TimeRegex().Match(normalized);
        if (time.Success) pieces.Add(time.Value.Replace('.', ':'));
        return string.Join(" ", pieces);
    }

    private static int ExtractParty(string normalized)
    {
        var match = PartyRegex().Match(normalized);
        if (!match.Success) return 0;
        string word = match.Groups[1].Value;
        if (int.TryParse(word, out int digits)) return digits;
        return word switch
        {
            "dwo" or "dwoch" => 2,
            "trzech" => 3,
            "czterech" or "cztery" => 4,
            var w when w.StartsWith("pi", StringComparison.Ordinal) => 5,
            var w when w.StartsWith("sze", StringComparison.Ordinal) => 6,
            "siedmiu" => 7,
            var w when w.StartsWith("o", StringComparison.Ordinal) && w.Contains("sm", StringComparison.Ordinal) => 8,
            var w when w.StartsWith("dziewi", StringComparison.Ordinal) => 9,
            var w when w.StartsWith("dziesi", StringComparison.Ordinal) => 10,
            _ => 0,
        };
    }

    private static string ExtractSubject(string normalized)
    {
        if (normalized.Contains("stolik", StringComparison.Ordinal)) return "stolik";
        if (normalized.Contains("rezerwacj", StringComparison.Ordinal)) return "rezerwacja";
        if (normalized.Contains("zamow", StringComparison.Ordinal) || normalized.Contains("zamów", StringComparison.Ordinal)) return "zamowienie";
        return "";
    }
}

/// <summary>Energy-VAD do składania wypowiedzi rozmówcy z relacji głośnika (16 kHz mono float).
/// Czysty i deterministyczny — bez modelu, testowalny w regresji.</summary>
public sealed class UtteranceBuffer
{
    private readonly double rmsThreshold;
    private readonly int endSilenceWindow;
    private readonly int minSpeechWindow;
    private readonly int maxWindow;
    private readonly List<float> speech = [];
    private int speechMilliseconds;
    private int trailingSilenceMilliseconds;
    private readonly int sampleRate;

    public UtteranceBuffer(double rmsThreshold = 0.012, int sampleRate = 16000,
        int endSilenceMilliseconds = 800, int minimumSpeechMilliseconds = 250, int maximumSpeechMilliseconds = 15000)
    {
        this.rmsThreshold = rmsThreshold;
        this.sampleRate = sampleRate;
        endSilenceWindow = endSilenceMilliseconds;
        minSpeechWindow = minimumSpeechMilliseconds;
        maxWindow = maximumSpeechMilliseconds;
    }

    /// <summary>Podaje kolejny kawałek audio; zwraca pełną wypowiedź albo null.</summary>
    public float[]? Feed(float[] chunk)
    {
        const int windowMilliseconds = 20;
        int window = sampleRate * windowMilliseconds / 1000;
        for (int offset = 0; offset < chunk.Length; offset += window)
        {
            int count = Math.Min(window, chunk.Length - offset);
            double sum = 0;
            for (int i = offset; i < offset + count; i++) sum += chunk[i] * chunk[i];
            double rms = Math.Sqrt(sum / Math.Max(1, count));
            int milliseconds = count * 1000 / sampleRate;
            if (rms >= rmsThreshold)
            {
                speech.AddRange(chunk.AsSpan(offset, count).ToArray());
                speechMilliseconds += milliseconds;
                trailingSilenceMilliseconds = 0;
            }
            else if (speechMilliseconds > 0)
            {
                speech.AddRange(chunk.AsSpan(offset, count).ToArray());
                speechMilliseconds += milliseconds;
                trailingSilenceMilliseconds += milliseconds;
            }
            bool longEnough = speechMilliseconds >= minSpeechWindow;
            if (longEnough && (trailingSilenceMilliseconds >= endSilenceWindow || speechMilliseconds >= maxWindow))
                return Flush();
        }
        return null;
    }

    /// <summary>Wymusza koniec wypowiedzi (np. koniec połączenia) — zwraca to, co zebrane.</summary>
    public float[]? Flush()
    {
        if (speechMilliseconds < minSpeechWindow) { Reset(); return null; }
        var result = speech.ToArray();
        Reset();
        return result;
    }

    private void Reset()
    {
        speech.Clear();
        speechMilliseconds = 0;
        trailingSilenceMilliseconds = 0;
    }
}
