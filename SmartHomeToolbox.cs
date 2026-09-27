using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 18 · pozycje 1251–1300 — smart home bez sprzętu i bez sieci: licznik energii i kosztów,
/// termostat i oszczędności, scena domowa, YAML automatyzacji do wklejenia, poprawność tematów MQTT
/// i symbole wieloznaczne, prąd z mocy, światło w lumenach, bateria czujnika, porównanie taryf.
/// Sentinel niczego nie łączy i nie steruje — liczy i wypisuje; sterowanie zostaje u użytkownika.
/// </summary>
public static class SmartHomeToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "energia")) return Energy(Payload(raw, "energia"));
        if (Is(norm, "koszt urzadzen")) return DevicesCost(Payload(raw, "koszt urzadzen"));
        if (Is(norm, "termostat")) return Thermostat(Payload(raw, "termostat"));
        if (Is(norm, "scena dom")) return Scene(Payload(raw, "scena dom"));
        if (Is(norm, "yaml automatyzacji")) return AutomationYaml(Payload(raw, "yaml automatyzacji"));
        if (Is(norm, "mqtt")) return Mqtt(Payload(raw, "mqtt"));
        if (Is(norm, "prad")) return Current(Payload(raw, "prad"));
        if (Is(norm, "luminy")) return Lumens(Payload(raw, "luminy"));
        if (Is(norm, "czujnik baterii")) return SensorBattery(Payload(raw, "czujnik baterii"));
        if (Is(norm, "tarif")) return Tariff(Payload(raw, "tarif"));
        return null;
    }

    private static string Energy(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] <= 0 || nums[0] > 100000 || nums[1] <= 0 || nums[1] > 24)
            return "Użycie: „energia: 100 5 1,0” (moc W, godziny na dobę, zł za kWh). Liczę zużycie i koszt — bez odpytywania żadnych urządzeń.";
        double price = nums.Length > 2 && nums[2] > 0 ? nums[2] : 0;
        double kwhDay = nums[0] / 1000.0 * nums[1];
        return "ENERGIA: " + N(nums[0]) + " W × " + N(nums[1]) + " h/dzień" + Environment.NewLine +
            "· zużycie: " + N(kwhDay) + " kWh/dzień · " + N(kwhDay * 30) + " kWh/miesiąc · " + N(kwhDay * 365) + " kWh/rok" + Environment.NewLine +
            (price > 0
                ? "· koszt przy " + N(price) + " zł/kWh: " + N(kwhDay * price) + " zł/dzień · " + N(kwhDay * 30 * price) + " zł/miesiąc · " + N(kwhDay * 365 * price) + " zł/rok"
                : "· podaj cenę (trzeci argument), aby policzyć koszt w złotych");
    }

    private static string DevicesCost(string input)
    {
        var devices = new List<(double W, double H, double Price)>();
        foreach (string part in (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            double[] nums = Numbers(part, 2);
            if (nums.Length >= 2 && nums[0] > 0 && nums[1] > 0 && nums[1] <= 24)
                devices.Add((nums[0], nums[1], nums.Length > 2 && nums[2] > 0 ? nums[2] : 0));
        }
        if (devices.Count == 0)
            return "Użycie: „koszt urzadzen: 100 5 1,0; 60 10 1,0” (W, godziny, zł/kWh — po średniku kolejne urządzenia).";
        var sb = new StringBuilder("KOSZT URZĄDZEŃ (na dzień):").AppendLine();
        double totalKwh = 0, totalCost = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            double kwh = devices[i].W / 1000.0 * devices[i].H;
            totalKwh += kwh;
            totalCost += devices[i].Price > 0 ? kwh * devices[i].Price : 0;
            sb.Append("· urządzenie ").Append(i + 1).Append(": ").Append(N(devices[i].W)).Append(" W × ").Append(N(devices[i].H))
              .Append(" h = ").Append(N(kwh)).Append(" kWh").Append(devices[i].Price > 0 ? " = " + N(kwh * devices[i].Price) + " zł" : "").AppendLine();
        }
        sb.Append("· RAZEM: ").Append(N(totalKwh)).Append(" kWh/dzień").Append(totalCost > 0 ? " · " + N(totalCost) + " zł/dzień · " + N(totalCost * 365) + " zł/rok" : "");
        return sb.ToString();
    }

    private static string Thermostat(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[1] < 15 || nums[1] > 28 || nums[0] < 15 || nums[0] > 28)
            return "Użycie: „termostat: 21 20” (obecna i docelowa temperatura). Oszczędność liczę z reguły kciukowej, nie z twojego domu.";
        double diff = nums[0] - nums[1];
        if (diff <= 0)
            return "Podwyższanie temperatury nie oszczędza — ale wygoda też się liczy: jedna stała temperatura zwykle wypada lepiej niż huśtawka.";
        double percent = diff * 6.0;
        return "TERMOSTAT: " + N(nums[0]) + "°C → " + N(nums[1]) + "°C (obniżenie o " + N(diff) + " K)" + Environment.NewLine +
            "· reguła kciukowa: każdy 1 K niższej temperatury ≈ 5–7% mniejsze zużycie energii grzewczej → tu ≈" + N(percent) + "%" + Environment.NewLine +
            "· to oszacowanie dla typowego domu; twoje ściany i okna mają własne zdanie — sprawdź na liczniku przez tydzień" + Environment.NewLine +
            "· schodzenie w nocy o 3–4 K zwykle się opłaca; głębsze wychładzanie już nie zawsze (ogrzewanie z powrotem kosztuje)";
    }

    private static string Scene(string input)
    {
        string[] p = SplitParts(input, 2);
        if (p.Length < 2 || p[0].Length == 0)
            return "Użycie: „scena dom: film | światła 20, rolety 90, tv on”. Karta sceny do przeniesienia do twojego systemu (Sentinel niczego nie steruje).";
        var sb = new StringBuilder("SCENA: ").Append(p[0]).AppendLine();
        foreach (string step in p[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (step.Length > 0) sb.Append("· ").Append(step).AppendLine();
        sb.Append("· kolejność ma znaczenie: najpierw światła/rolety, potem sprzęt, który reaguje na jasność czujnikami");
        return sb.ToString();
    }

    private static string AutomationYaml(string input)
    {
        string[] p = SplitParts(input, 2);
        var time = Regex.Match(p.Length > 0 ? p[0] : "", @"^\d{1,2}:\d{2}$");
        if (!time.Success || p.Length < 2 || p[1].Length == 0)
            return "Użycie: „yaml automatyzacji: 22:00 | wyłącz światła w salonie”. Wygeneruję YAML w stylu Home Assistant — do wklejenia u siebie.";
        return "automation:" + Environment.NewLine +
            "  - alias: \"Automatyzacja o " + p[0].Trim() + "\"" + Environment.NewLine +
            "    trigger:" + Environment.NewLine +
            "      - platform: time" + Environment.NewLine +
            "        at: \"" + p[0].Trim() + "\"" + Environment.NewLine +
            "    action:" + Environment.NewLine +
            "      - service: automation.describe  # tu wpisz serwis i encję: " + p[1].Trim() + Environment.NewLine +
            "    mode: single" + Environment.NewLine +
            "· serwis i encję (light.turn_off, cover.set_position…) uzupełnij według swojego systemu; Sentinel niczego nie łączy";
    }

    private static string Mqtt(string input)
    {
        string topic = (input ?? "").Trim();
        if (topic.Length == 0)
            return "Użycie: „mqtt: dom/parter/lampa/stan”. Sprawdzę konwencję tematu albo wyjaśnię symbole (dom/+/stan).";
        if (topic.Contains('+') || topic.Contains('#'))
            return "SYMBOLE W MQTT (tylko w subskrypcji, nigdy w publikacji):" + Environment.NewLine +
                "· + — jeden poziom: „dom/+/stan” łapie dom/parter/stan i dom/pietro/stan, nie dom/parter/lampa/stan" + Environment.NewLine +
                "· # — wiele poziomów (na końcu): „dom/#” łapie wszystko pod dom/" + Environment.NewLine +
                "· publisher wysyła pełny temat; filtry to sprawa subskrybenta";
        var levels = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (levels.Length < 2 || levels.Length > 6 || topic.StartsWith('/') || topic.EndsWith('/') || levels.Any(l => !Regex.IsMatch(l, "^[a-zA-Z0-9_-]{1,40}$")))
            return "Temat „" + topic + "” łamie konwencje: 2–6 poziomów, znaki [a-z0-9_-], bez spacji i bez ukośników na brzegach. Przykład: dom/parter/lampa/stan.";
        return "TEMAT: " + topic + " — poprawny (" + levels.Length + " poziomy)" + Environment.NewLine +
            "· konwencja: dom/poziom/urządzenie/atrybut; stan w atrybucie (stan, moc, bateria), nie w temacie" + Environment.NewLine +
            "· tematy tylko małymi literami — MQTT rozróżnia wielkość liter i o tym się zapomina";
    }

    private static string Current(string input)
    {
        double[] nums = Numbers(input, 1);
        if (nums.Length < 1 || nums[0] <= 0 || nums[0] > 50000)
            return "Użycie: „prad: 1500” (moc w W przy 230 V). Policzę prąd i powiem, czy zwykłe gniazdo wystarczy.";
        double amps = nums[0] / 230.0;
        string verdict = amps <= 10 ? "bezpiecznie w obwodzie 10 A" : amps <= 16 ? "mieści się w 16 A, nie w 10 A" : "za dużo dla pojedynczego gniazda — obwód dedykowany";
        return "PRĄD: " + N(nums[0]) + " W przy 230 V → " + N(amps) + " A (" + verdict + ")" + Environment.NewLine +
            "· czajnik + czajniczek w jednym gniazdku to klasyka wybijania bezpieczników — licz SUMĘ mocy, nie pojedyncze urządzenie";
    }

    private static string Lumens(string input)
    {
        double[] nums = Numbers(input, 1);
        if (nums.Length < 1 || nums[0] <= 0 || nums[0] > 500)
            return "Użycie: „luminy: 18” (powierzchnia w m²). Pokażę, ile lumenów daje komfortowe światło dla różnych pomieszczeń.";
        double area = nums[0];
        return "ŚWIATŁO dla " + N(area) + " m² (lumeny = m² × luksy):" + Environment.NewLine +
            "· salon " + N(150 * area) + " lm (150 lx) · sypialnia " + N(100 * area) + " lm (100 lx)" + Environment.NewLine +
            "· kuchnia/biuro " + N(300 * area) + " lm (300 lx) · łazienka " + N(200 * area) + " lm (200 lx)" + Environment.NewLine +
            "· na opakowaniu żarówki szukaj lumenów, nie watów; waty mówią o rachunku, nie o świetle";
    }

    private static string SensorBattery(string input)
    {
        double[] nums = Numbers(input, 3);
        if (nums.Length < 3 || nums[0] <= 0 || nums[1] <= 0 || nums[2] <= 0 || nums[2] > 24)
            return "Użycie: „czujnik baterii: 3000 15 8” (pojemność mAh, prąd mA, godziny aktywności na dobę). Oszacuję czas pracy.";
        double avg = nums[1] * nums[2] / 24.0;
        double hours = nums[0] / avg;
        return "BATERIA: " + N(nums[0]) + " mAh, prąd " + N(nums[1]) + " mA przez " + N(nums[2]) + " h/dzień (średnio " + N(avg) + " mA)" + Environment.NewLine +
            "· czas pracy: ~" + N(hours) + " h ≈ " + N(hours / 24.0) + " dni" + Environment.NewLine +
            "· realnie odejmij 20–30% na samorozładowanie i zimę — baterie nie kłamią, po prostu mają swoje daty";
    }

    private static string Tariff(string input)
    {
        double[] nums = Numbers(input, 3);
        if (nums.Length < 3 || nums[0] <= 0 || nums[1] <= 0 || nums[2] <= 0)
            return "Użycie: „tarif: 2000 1,0 0,85” (roczne kWh, zł/kWh w taryfie A, zł/kWh w taryfie B). Różnica roczna na stole.";
        double a = nums[0] * nums[1], b = nums[0] * nums[2];
        return "TARYFY na " + N(nums[0]) + " kWh/rok:" + Environment.NewLine +
            "· taryfa A (" + N(nums[1]) + " zł/kWh): " + N(a) + " zł/rok" + Environment.NewLine +
            "· taryfa B (" + N(nums[2]) + " zł/kWh): " + N(b) + " zł/rok → różnica " + N(Math.Abs(a - b)) + " zł/rok" + Environment.NewLine +
            "· taryfy dzienne/nocne opłacają się tylko, gdy naprawdę przenosisz zużycie (bojler, ładowanie) — policz swój rozkład, nie obietnice";
    }

    // ————— pomocnicze —————

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

    private static string N(double v) => v.ToString("0.##", Pl);

    private static double Num(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
