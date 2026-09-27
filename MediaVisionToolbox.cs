using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJE 9 i 10 · pozycje 671–800 — media i obraz bez kamery, mikrofonu i sieci: kontrast WCAG,
/// PPI i proporcje ekranu, rozmiar/bitrate wideo, czas i rozmiar audio, tempo mowy, zamiana decybeli
/// na mnożnik. Czysta arytmetyka na podanych liczbach — żadnych urządzeń nie otwieram.
/// </summary>
public static class MediaVisionToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "kontrast")) return Contrast(Payload(raw, "kontrast"));
        if (Is(norm, "ppi")) return Ppi(Payload(raw, "ppi"));
        if (Is(norm, "proporcje")) return Aspect(Payload(raw, "proporcje"));
        if (Is(norm, "bitrate wideo")) return VideoBitrate(Payload(raw, "bitrate wideo"));
        if (Is(norm, "audio czas")) return AudioDuration(Payload(raw, "audio czas"));
        if (Is(norm, "audio rozmiar")) return AudioSize(Payload(raw, "audio rozmiar"));
        if (Is(norm, "tempo mowy")) return SpeechPace(Payload(raw, "tempo mowy"));
        if (Is(norm, "db")) return Decibels(Payload(raw, "db"));
        return null;
    }

    private static string Contrast(string input)
    {
        string[] parts = (input ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !TryParseColor(parts[0], out var a) || !TryParseColor(parts[1], out var b))
            return "Użycie: „kontrast: #ffffff #000000” (albo krótko #fff #000). Liczę stosunek WCAG i klasy AA/AAA.";
        double la = Luminance(a), lb = Luminance(b);
        double ratio = (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        string verdict = ratio >= 7 ? "AAA — świetny" : ratio >= 4.5 ? "AA dla zwykłego tekstu" : ratio >= 3 ? "tylko AA dla dużego tekstu (18 pt+)" : "za niski — czytelność ucierpi";
        return "KONTRAST WCAG: " + ratio.ToString("0.##", Pl) + ":1 — " + verdict + Environment.NewLine +
            "· luminancja: kolor A " + N(Math.Round(la, 3)) + ", kolor B " + N(Math.Round(lb, 3)) + Environment.NewLine +
            "· granice: 4,5:1 zwykły tekst (AA) · 3:1 duży tekst i elementy UI · 7:1 AAA";
    }

    private static string Ppi(string input)
    {
        double[] nums = Numbers(input, 3);
        if (nums.Length < 3 || nums[0] < 100 || nums[1] < 100 || nums[2] <= 0)
            return "Użycie: „ppi: 1920 1080 24” (szerokość, wysokość, przekątna w calach). Policzę gęstość pikseli.";
        double diagonal = Math.Sqrt(nums[0] * nums[0] + nums[1] * nums[1]);
        double ppi = diagonal / nums[2];
        return "GĘSTOŚĆ PIKSELI: " + N(Math.Round(diagonal, 0)) + " px przekątnej / " + N(nums[2]) + "\" → ≈" + N(Math.Round(ppi, 0)) + " PPI (piksel " + N(Math.Round(25.4 / ppi, 2)) + " mm)" + Environment.NewLine +
            "· orientacyjnie: ~90 PPI biurko · ~150+ laptop · ~300+ telefon „retina” — poniżej widać pojedyncze piksele";
    }

    private static string Aspect(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[1] < 1)
            return "Użycie: „proporcje: 1920 1080”. Skrócę stosunek (16:9) i policzę piksele.";
        long w = (long)nums[0], h = (long)nums[1];
        long gcd = Gcd(w, h);
        double mp = w * h / 1_000_000.0;
        return "PROPORCJE: " + (w / gcd) + ":" + (h / gcd) + " · " + N(w) + "×" + N(h) + " = " + N(Math.Round(mp, 2)) + " Mpx" + Environment.NewLine +
            "· 16:9 standard wideo · 16:10 wyżej na stronie · 4:3 stare/retro · 21:9 ultrawide — kadr zawsze dopasuj do treści, nie odwrotnie";
    }

    private static string VideoBitrate(string input)
    {
        var m = Regex.Match(Flat(input ?? ""), @"(\d{1,4})\s*(720p|1080p|1440p|4k)");
        if (!m.Success)
            return "Użycie: „bitrate wideo: 90 1080p” (sekundy, rozdzielczość). Wyliczę orientacyjny rozmiar pliku.";
        double seconds = Num(m.Groups[1].Value);
        string res = m.Groups[2].Value;
        double rate = res switch { "720p" => 5, "1080p" => 8, "1440p" => 16, "4k" => 25, _ => 8 };
        double mb = rate * seconds / 8.0;
        return "WIDEO " + res + ": ~" + N(rate) + " Mb/s × " + N(seconds) + " s → ≈" + N(Math.Round(mb, 1)) + " MB (bez dźwięku)" + Environment.NewLine +
            "· +dźwięk ~0,4 MB/min (AAC 64 kb/s) · rozmiar skacze z długością liniowo, z rozdzielczością kwadratowo" + Environment.NewLine +
            "· to szacunek dla typowego kodowania H.264; H.265/AV1 dają ~40–50% mniej";
    }

    private static string AudioDuration(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] <= 0 || nums[1] < 32 || nums[1] > 2000)
            return "Użycie: „audio czas: 50 320” (rozmiar MB, bitrate kb/s). Policzę czas odtwarzania.";
        double seconds = nums[0] * 8_000_000 / (nums[1] * 1000);
        var t = TimeSpan.FromSeconds(seconds);
        return "AUDIO: " + N(nums[0]) + " MB przy " + N(nums[1]) + " kb/s → " + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00", Pl) + " (" + N(seconds, 0) + " s)" + Environment.NewLine +
            "· MP3/AAC 128–320 kb/s dla muzyki, 64–96 kb/s wystarcza dla mowy";
    }

    private static string AudioSize(string input)
    {
        var m = Regex.Match((input ?? "").Trim(), @"(\d{1,3}):(\d{2})\s+(\d{2,4})");
        if (!m.Success)
            return "Użycie: „audio rozmiar: 3:30 320” (minuty:sekundy, bitrate kb/s). Policzę rozmiar pliku.";
        double seconds = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
        double kbps = Num(m.Groups[3].Value);
        double mb = seconds * kbps * 1000 / 8_000_000.0;
        return "AUDIO: " + m.Groups[1].Value + ":" + m.Groups[2].Value + " przy " + N(kbps) + " kb/s → ≈" + N(Math.Round(mb, 1)) + " MB";
    }

    private static string SpeechPace(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[1] <= 0)
            return "Użycie: „tempo mowy: 420 3” (słów, minut). Ocenę tempo mówienia.";
        double wpm = nums[0] / nums[1];
        string verdict = wpm < 110 ? "wolno — wykład albo trudny materiał, tak ma prawo wyglądać" :
            wpm <= 160 ? "naturalnie — typowa rozmowa i audycje" : "szybko — reklamy i czytanki na czas";
        return "TEMPO: " + N(wpm, 0) + " słów/min — " + verdict + Environment.NewLine +
            "· czytane na głos po polsku zwykle 110–160 słów/min; nagrania instruktażowe celowo wolniej (90–130)";
    }

    private static string Decibels(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2) return "Użycie: „db: 20 3” (podstawowa wartość, decybele różnicy). Przeliczę na mnożnik mocy.";
        double ratio = Math.Pow(10, nums[1] / 10.0);
        string meaning = Math.Abs(nums[1] - 3) < 0.01 ? " (±3 dB to podwojenie/połowę mocy — granica percepcji głośności)" :
            nums[1] == 10 ? " (10 dB = dziesięć razy większa moc)" : "";
        return "DECYBELE: " + N(nums[0]) + " " + (nums[1] >= 0 ? "+" : "") + N(nums[1]) + " dB → ×" + N(Math.Round(ratio, 2)) + " mocy = " + N(nums[0] * ratio, 0) + meaning;
    }

    // ————— pomocnicze —————

    private static bool TryParseColor(string input, out (byte R, byte G, byte B) color)
    {
        color = (0, 0, 0);
        string hex = (input ?? "").Trim().TrimStart('#').ToLowerInvariant();
        if (hex.Length == 3) hex = string.Concat(hex[0].ToString(), hex[0].ToString(), hex[1].ToString(), hex[1].ToString(), hex[2].ToString(), hex[2].ToString());
        if (hex.Length != 6 || !Regex.IsMatch(hex, "^[0-9a-f]{6}$")) return false;
        color = (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
        return true;
    }

    private static double Luminance((byte R, byte G, byte B) c)
    {
        double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

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

    private static double[] Numbers(string input, int min)
    {
        var result = new List<double>();
        string normalized = Regex.Replace(input ?? "", @"(?<=\d),(?=\d)", ".");
        foreach (string token in Regex.Split(normalized, "[\\s;+]+"))
        {
            double v = Num(token);
            if (double.IsFinite(v)) result.Add(v);
            if (result.Count >= 6) break;
        }
        return result.Count >= min ? result.ToArray() : [];
    }

    private static string N(double v, int? digits = null) => (digits ?? 2) switch { 0 => v.ToString("0", Pl), _ => v.ToString("0.##", Pl) };

    private static double Num(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
