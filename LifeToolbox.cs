using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJE 17 i 14 · pozycje 1201–1250 oraz 1021–1070 — zdrowie (arytmetyka, nie porada medyczna)
/// i komunikacja (szkice, limity znaków, ocena tonu). Wszystko liczone lokalnie, na jawnych
/// wzorach, z zaznaczeniem, gdzie wynik jest przybliżeniem, a gdzie w ogóle nie wolno go traktować
/// jak diagnozy. Polecenia wymagają dwukropka — „sen”, „tempo” czy „ton” to zwykłe słowa i nie
/// mogą zabierać zdań.
/// </summary>
public static class LifeToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly string[] UrgentWords = ["pilne", "natychmiast", "musisz", "musicie", " wymagam", "ostatecznie", "alarm"];

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        if (Starts(text, "bmr")) return Bmr(Payload(raw, "bmr"));
        if (Starts(text, "tdee")) return Tdee(Payload(raw, "tdee"));
        if (Starts(text, "makro")) return Macro(Payload(raw, "makro"));
        if (Starts(text, "hrmax", "tetno maksymalne")) return HeartRate(Payload(raw, "hrmax", "tetno maksymalne"));
        if (Starts(text, "whtr")) return WaistToHeight(Payload(raw, "whtr"));
        if (Starts(text, "whr")) return WaistToHip(Payload(raw, "whr"));
        if (Starts(text, "1rm", "max powtorzen")) return OneRepMax(Payload(raw, "1rm", "max powtorzen"));
        if (Starts(text, "tempo")) return Pace(Payload(raw, "tempo"));
        if (Starts(text, "kroki")) return Steps(Payload(raw, "kroki"));
        if (Starts(text, "woda")) return Water(Payload(raw, "woda"));
        if (Starts(text, "sen", "godzina snu")) return Sleep(Payload(raw, "sen", "godzina snu"));
        if (Starts(text, "deficyt")) return Deficit(Payload(raw, "deficyt"));
        if (Starts(text, "sms")) return Sms(Payload(raw, "sms"));
        if (Starts(text, "post", "wpis")) return Post(Payload(raw, "post", "wpis"));
        if (Starts(text, "mail", "szkic maila")) return Mail(Payload(raw, "szkic maila", "mail"));
        if (Starts(text, "agenda")) return Agenda(Payload(raw, "agenda"));
        if (Starts(text, "protokol", "notatka ze spotkania")) return Minutes(Payload(raw, "protokol", "notatka ze spotkania"));
        if (Starts(text, "follow up", "przypomnij sie")) return FollowUp(Payload(raw, "follow up", "przypomnij sie"));
        if (Starts(text, "skroc do", "utnij do")) return Shorten(Payload(raw, "skroc do", "utnij do"));
        if (Starts(text, "ton", "ocena tonu")) return Tone(Payload(raw, "ton", "ocena tonu"));
        if (Starts(text, "czytelnosc")) return Readability(Payload(raw, "czytelnosc"));
        return null;
    }

    /// <summary>Prefiks znaczy coś tylko z dwukropkiem — inaczej „sen o 23” albo „ton wypowiedzi” wpadałyby do narzędzia.</summary>
    private static bool Starts(string text, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (text.StartsWith(prefix + ":", StringComparison.Ordinal)) return true;
            if (string.Equals(text, prefix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string Payload(string raw, params string[] prefixes)
    {
        string text = (raw ?? "").Trim();
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = text[prefix.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..];
            return rest.Trim();
        }
        return text;
    }

    internal static double[] Numbers(string payload)
    {
        var matches = Regex.Matches(payload ?? "", @"-?\d+(?:[.,]\d+)?");
        var values = new List<double>();
        foreach (Match match in matches)
        {
            if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                values.Add(value);
        }
        return values.ToArray();
    }

    private static string N(double value, int digits = 3) => value.ToString("0." + new string('#', digits), Pl);

    /// <summary>Mifflin-St Jeor — najczęściej używany wzór; zwraca 0 dla niepoprawnych danych.</summary>
    public static double BmrKcal(double weightKg, double heightCm, double age, bool male) =>
        male ? 10 * weightKg + 6.25 * heightCm - 5 * age + 5
             : 10 * weightKg + 6.25 * heightCm - 5 * age - 161;

    public static string Bmr(string payload)
    {
        double[] values = Numbers(payload);
        bool? male = Sex(payload);
        if (values.Length < 3 || male == null)
            return "Użyj: „bmr: 80 180 30 m” — masa (kg), wzrost (cm), wiek i płeć (m = mężczyzna, k = kobieta). Wzór Mifflin-St Jeor.";
        double weight = values[0], height = values[1], age = values[2];
        if (weight is < 20 or > 400 || height is < 90 or > 260 || age is < 10 or > 120)
            return "Dane poza zakresem sensowności (masa 20–400 kg, wzrost 90–260 cm, wiek 10–120 lat). Nie liczę na absurdzie.";
        double bmr = BmrKcal(weight, height, age, male.Value);
        return "Podstawowa przemiana materii (Mifflin-St Jeor): " + N(bmr, 0) + " kcal/dzień." + Environment.NewLine +
            "· dane: " + N(weight, 1) + " kg · " + N(height, 0) + " cm · " + N(age, 0) + " lat · płeć: " + (male.Value ? "mężczyzna" : "kobieta") + Environment.NewLine +
            "· dzienne zapotrzebowanie (BMR × aktywność):" + Environment.NewLine +
            "   – siedzący (1,2): " + N(bmr * 1.2, 0) + " kcal · lekka aktywność (1,375): " + N(bmr * 1.375, 0) + " kcal" + Environment.NewLine +
            "   – umiarkowana (1,55): " + N(bmr * 1.55, 0) + " kcal · wysoka (1,725): " + N(bmr * 1.725, 0) + " kcal · bardzo wysoka (1,9): " + N(bmr * 1.9, 0) + " kcal" + Environment.NewLine +
            "· To wzór szacunkowy (błąd rzędu 10%) — nie zna Twojego składu ciała ani chorób. Nie jest to porada medyczna.";
    }

    private static bool? Sex(string payload)
    {
        string text = (payload ?? "").ToLowerInvariant();
        if (Regex.IsMatch(text, @"\b(m|mężczyzna|mezczyzna|male)\b")) return true;
        if (Regex.IsMatch(text, @"\b(k|kobieta|female|f)\b")) return false;
        return null;
    }

    public static string Tdee(string payload)
    {
        double[] values = Numbers(payload);
        bool? male = Sex(payload);
        if (values.Length < 4 || male == null)
            return "Użyj: „tdee: 80 180 30 m 1,55” — masa, wzrost, wiek, płeć i współczynnik aktywności (1,2–1,9).";
        double factor = values[3];
        if (factor is < 1.0 or > 2.5) return "Współczynnik aktywności mieści się zwykle w 1,2–1,9 (siedzący…bardzo aktywny). Dostałem " + N(factor, 2) + ".";
        double bmr = BmrKcal(values[0], values[1], values[2], male.Value);
        return "Dzienne zapotrzebowanie (TDEE): " + N(bmr * factor, 0) + " kcal." + Environment.NewLine +
            "· BMR: " + N(bmr, 0) + " kcal × współczynnik " + N(factor, 2) + Environment.NewLine +
            "· Utrzymanie wagi to ten poziom; jego zmiana o ~500 kcal na dobę to ok. 0,5 kg tygodniowo." + Environment.NewLine +
            "· Kalkulacja, nie zalecenie: przy chorobach, lekach albo ciąży zapotrzebowanie ustala lekarz, nie wzór.";
    }

    public static string Macro(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 4) return "Użyj: „makro: 2400 30 25 45” — kalorie i udziały procentowe białka, tłuszczu i węglowodanów.";
        double kcal = values[0], proteinPct = values[1], fatPct = values[2], carbPct = values[3];
        double sum = proteinPct + fatPct + carbPct;
        if (Math.Abs(sum - 100) > 0.5) return "Udziały muszą sumować się do 100% (dostałem " + N(sum, 1) + "%). Popraw wartości — nie normalizuję ich po cichu.";
        if (kcal is < 800 or > 8000) return "Kalorie poza sensownym zakresem 800–8000 kcal.";
        double protein = kcal * proteinPct / 100 / 4, fat = kcal * fatPct / 100 / 9, carbs = kcal * carbPct / 100 / 4;
        return "Makroskładniki dla " + N(kcal, 0) + " kcal (" + N(proteinPct, 0) + "/" + N(fatPct, 0) + "/" + N(carbPct, 0) + "%):" + Environment.NewLine +
            "· białko: " + N(protein, 0) + " g (4 kcal/g) · tłuszcz: " + N(fat, 0) + " g (9 kcal/g) · węglowodany: " + N(carbs, 0) + " g (4 kcal/g)" + Environment.NewLine +
            "· kontrola: " + N(protein * 4 + fat * 9 + carbs * 4, 0) + " kcal — tyle samo, co na wejściu" + Environment.NewLine +
            "· Białko poniżej 1,2 g na kg masy ciała przy treningu siłowym to zwykle za mało; powyżej 2,5 g/kg nie daje już przewagi." + Environment.NewLine +
            "· Nie dobieram diety ani nie oceniam stanu zdrowia — to tylko przeliczenie procentów na gramy.";
    }

    public static string HeartRate(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 1) return "Użyj: „hrmax: 35” (wiek w latach).";
        double age = values[0];
        if (age is < 10 or > 110) return "Wiek poza zakresem 10–110 lat.";
        double classic = 220 - age, tanaka = 208 - 0.7 * age;
        var zones = new (string Name, double From, double To)[]
        {
            ("regeneracja 50–60%", 0.50, 0.60),
            ("spalanie 60–70%", 0.60, 0.70),
            ("tlenowa 70–80%", 0.70, 0.80),
            ("próg 80–90%", 0.80, 0.90),
            ("maksymalna 90–100%", 0.90, 1.00),
        };
        var lines = zones.Select(z => "· " + z.Name.PadRight(22) + N(tanaka * z.From, 0) + "–" + N(tanaka * z.To, 0) + " uderzeń/min");
        return "Tętno maksymalne (wiek " + N(age, 0) + "):" + Environment.NewLine +
            "· wzór klasyczny 220 − wiek: " + N(classic, 0) + " · wzór Tanaki 208 − 0,7 × wiek: " + N(tanaka, 0) + Environment.NewLine +
            "· strefy liczone z wzoru Tanaki (dokładniejszy, ale nadal szacunek):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Rozrzut między ludźmi w tym samym wieku to nawet ±15 uderzeń. Strefy z zegarka są lepsze, bo mierzą Ciebie.";
    }

    public static string WaistToHeight(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „whtr: 80 180” — obwód talii i wzrost w centymetrach.";
        double waist = values[0], height = values[1];
        if (waist <= 0 || height <= 0) return "Podaj realne wartości w centymetrach.";
        double ratio = waist / height;
        string band = ratio < 0.4 ? "poniżej typowego zakresu" : ratio < 0.5 ? "zakres prawidłowy" : ratio < 0.6 ? "podwyższone ryzyko" : "wyraźnie podwyższone ryzyko";
        return "Wskaźnik talia/wzrost (WHtR): " + N(ratio, 3) + " — " + band + "." + Environment.NewLine +
            "· Progi orientacyjne: poniżej 0,5 to strefa, którą większość wytycznych uznaje za prawidłową." + Environment.NewLine +
            "· Mierzy się w pasie na wysokości pępka, na wydechu, bez wciągania brzucha." + Environment.NewLine +
            "· To przesiewowa miara, nie diagnoza. Nie oceniam Twojego zdrowia — wskazuję liczbę i próg.";
    }

    public static string WaistToHip(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „whr: 80 95” — obwód talii i bioder w centymetrach. Opcjonalnie dopisz „m” albo „k”.";
        double waist = values[0], hip = values[1];
        if (hip <= 0 || waist <= 0) return "Podaj realne wartości w centymetrach.";
        double ratio = waist / hip;
        bool? male = Sex(payload);
        string threshold = male == true ? "0,90 (mężczyźni)" : male == false ? "0,85 (kobiety)" : "0,90 dla mężczyzn i 0,85 dla kobiet";
        return "Wskaźnik talia/biodra (WHR): " + N(ratio, 3) + "." + Environment.NewLine +
            "· Progi WHO: " + threshold + " — powyżej nich mówi się o otyłości brzusznej." + Environment.NewLine +
            "· Gdy nie podasz płci, pokazuję oba progi i nie zgaduję, kogo dotyczą." + Environment.NewLine +
            "· Miara orientacyjna; przy bardzo niskiej lub wysokiej masie ciała bywa myląca.";
    }

    public static string OneRepMax(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „1rm: 80 5” — ciężar i liczba powtórzeń (najlepiej 10 lub mniej).";
        double weight = values[0], reps = values[1];
        if (reps is < 1 or > 20) return "Wzory maksymalnego powtórzenia są sensowne do ok. 10 powtórzeń (dopuszczam 20). Dostałem " + N(reps, 0) + ".";
        double epley = weight * (1 + reps / 30);
        double brzycki = weight * 36 / (37 - reps);
        double lombardi = weight * Math.Pow(reps, 0.10);
        return "Szacowany ciężar maksymalny (1RM) dla " + N(weight, 1) + " kg × " + N(reps, 0) + " powtórzeń:" + Environment.NewLine +
            "· Epley: " + N(epley, 1) + " kg · Brzycki: " + N(brzycki, 1) + " kg · Lombardi: " + N(lombardi, 1) + " kg" + Environment.NewLine +
            "· Zakres szacunków: " + N(Math.Min(epley, Math.Min(brzycki, lombardi)), 1) + "–" + N(Math.Max(epley, Math.Max(brzycki, lombardi)), 1) + " kg" + Environment.NewLine +
            "· Im więcej powtórzeń, tym gorsze przybliżenie. Prawdziwe 1RM trzeba zmierzyć pod okiem kogoś, kto asekuruje." + Environment.NewLine +
            "· Nie planuję za Ciebie progresji obciążeń — to arytmetyka, nie trening.";
    }

    public static string Pace(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „tempo: 42 10” — czas w minutach i dystans w kilometrach.";
        double minutes = values[0], km = values[1];
        if (km <= 0) return "Dystans musi być większy od zera.";
        double perKm = minutes / km;
        int paceMin = (int)Math.Floor(perKm);
        int paceSec = (int)Math.Round((perKm - paceMin) * 60);
        if (paceSec == 60) { paceMin++; paceSec = 0; }
        double speed = km / (minutes / 60.0);
        return "Tempo: " + paceMin + ":" + paceSec.ToString("00", CultureInfo.InvariantCulture) + " / km · prędkość: " + N(speed, 2) + " km/h." + Environment.NewLine +
            "· dystans " + N(km, 2) + " km w " + N(minutes, 1) + " min · 5 km w tym tempie: " + N(perKm * 5, 1) + " min · półmaraton: " + N(perKm * 21.0975 / 60, 2) + " h" + Environment.NewLine +
            "· Tempo liczone z dystansu i czasu; wzniesienia, wiatr i upał zmieniają to, co czujesz, a nie arytmetykę.";
    }

    public static string Steps(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „kroki: 8000 175” — liczba kroków i wzrost w centymetrach.";
        double steps = values[0], heightCm = values[1];
        if (steps <= 0 || heightCm is < 90 or > 260) return "Podaj liczbę kroków i wzrost w centymetrach (90–260).";
        double stride = heightCm * 0.415 / 100.0;
        double km = steps * stride / 1000.0;
        double kcal = km * 0.9 * 70 / 70.0;
        return "Kroki → dystans:" + Environment.NewLine +
            "· " + N(steps, 0) + " kroków przy wzroście " + N(heightCm, 0) + " cm ≈ " + N(km, 2) + " km (długość kroku ~" + N(stride, 2) + " m)" + Environment.NewLine +
            "· orientacyjny wydatek: ~" + N(kcal, 0) + " kcal na każde 70 kg masy ciała (przy 55 kg: ~" + N(kcal * 55 / 70, 0) + " kcal, przy 90 kg: ~" + N(kcal * 90 / 70, 0) + " kcal)" + Environment.NewLine +
            "· Długość kroku to średnia statystyczna dla wzrostu — pomiar telefonem jest dokładniejszy." + Environment.NewLine +
            "· Nie oceniam, ile kroków „powinieneś” zrobić: to nie liczba, którą da się ustalić jednym wzorem.";
    }

    public static string Water(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 1) return "Użyj: „woda: 80” (masa ciała w kilogramach).";
        double weight = values[0];
        if (weight is < 20 or > 400) return "Masa poza zakresem 20–400 kg.";
        return "Zapotrzebowanie na wodę (masa " + N(weight, 1) + " kg):" + Environment.NewLine +
            "· 30 ml/kg: " + N(weight * 30 / 1000, 2) + " l · 35 ml/kg: " + N(weight * 35 / 1000, 2) + " l" + Environment.NewLine +
            "· przy treningu dodaj 0,4–0,8 l na każdą godzinę wysiłku, a w upale drugie tyle" + Environment.NewLine +
            "· Część wody bierzesz w jedzeniu i napojach — kawa i herbata też się liczą." + Environment.NewLine +
            "· Przy chorobach nerek albo serca ilość wody ustala lekarz, nie wzór z kilogramów.";
    }

    public static string Sleep(string payload)
    {
        string time = Regex.Match(payload ?? "", @"\d{1,2}[:.]\d{2}").Value.Replace('.', ':');
        if (time.Length == 0) return "Użyj: „sen: 23:30” — godzina zaśnięcia. Policze pełne cykle 90-minutowe z 15 minutami na zaśnięcie.";
        var parts = time.Split(':');
        if (!int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute) || hour > 23 || minute > 59)
            return "Podaj godzinę w formacie 24-godzinnym, np. „sen: 23:30”.";
        var start = new TimeSpan(hour, minute, 0).Add(TimeSpan.FromMinutes(15));
        var cycles = new List<string>();
        for (int count = 6; count >= 3; count--)
        {
            var wake = start.Add(TimeSpan.FromMinutes(90.0 * count));
            cycles.Add("· " + count + " cykli (" + N(1.5 * count, 1) + " h snu): pobudka " + wake.ToString(@"hh\:mm", CultureInfo.InvariantCulture) +
                (count == 5 ? " — najczęściej mieści się w normie" : ""));
        }
        return "Godziny pobudki przy zaśnięciu o " + time + " (15 min na zasypianie):" + Environment.NewLine +
            string.Join(Environment.NewLine, cycles) + Environment.NewLine +
            "· Cykl snu to średnio 90 minut; budzik na końcu cyklu jest łatwiejszy niż w środku." + Environment.NewLine +
            "· To planowanie arytmetyczne. Zaburzenia snu (bezsenność, chrapanie z bezdechami) to temat dla lekarza, nie dla kalkulatora.";
    }

    public static string Deficit(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 3) return "Użyj: „deficyt: 90 80 0,5” — waga startowa, waga docelowa (kg) i tempo (kg na tydzień).";
        double from = values[0], to = values[1], perWeek = values[2];
        if (perWeek <= 0 || perWeek > 2) return "Tempo 0,2–1 kg na tydzień jest sensowne (dopuszczam do 2). Dostałem " + N(perWeek, 2) + " kg.";
        if (Math.Abs(from - to) < 1e-9) return "Waga startowa i docelowa są takie same — nie ma czego planować.";
        double kg = Math.Abs(from - to);
        double weeks = kg / perWeek;
        double dailyDeficit = perWeek * 7700 / 7;
        return "Plan zmiany wagi " + N(from, 1) + " → " + N(to, 1) + " kg w tempie " + N(perWeek, 2) + " kg/tydzień:" + Environment.NewLine +
            "· różnica: " + N(kg, 1) + " kg · tygodnie: " + N(weeks, 1) + " · miesięcy: " + N(weeks / 4.345, 1) + Environment.NewLine +
            "· to ok. " + N(dailyDeficit, 0) + " kcal " + (from > to ? "deficytu" : "nadwyżki") + " na dobę (1 kg tłuszczu ≈ 7700 kcal — to uproszczenie)" + Environment.NewLine +
            "· Waga nie spada liniowo: woda i glikogen potrafią zmienić wynik o 2 kg w tydzień." + Environment.NewLine +
            "· To arytmetyka celu, nie plan dietetyczny ani medyczny. Przy chorobach i lekach skonsultuj tempo z lekarzem.";
    }

    public static string Sms(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „sms: treść wiadomości” — policzę znaki i liczbę wiadomości.";
        bool gsm = Regex.IsMatch(text, @"^[A-Za-z0-9\s\.,\?!:;'""()\-+*/%&@$#_=<>\u00A0\u000C]*$");
        int perSegment = gsm ? 160 : 70;
        int[] lengths = gsm ? [160, 153] : [70, 67];
        int segments = text.Length <= lengths[0] ? 1 : (int)Math.Ceiling((double)text.Length / lengths[1]);
        return "SMS: " + text.Length + " znaków, " + segments + " wiadomość(i) w standardzie " + (gsm ? "GSM-7 (bez polskich znaków)" : "UCS-2 (polskie znaki lub emoji)") + "." + Environment.NewLine +
            "· limit jednej wiadomości: " + lengths[0] + " znaków · przy łączeniu: " + lengths[1] + " na część" + Environment.NewLine +
            (gsm ? "· Polskie znaki (ą ć ę ł ń ó ś ź ż) albo emoji przełączają wiadomość na UCS-2 i obcinają limit do 70 znaków." : "· Znaki diakrytyczne/emoji są wykryte — krótszy limit jest tego skutkiem, nie błędem.") + Environment.NewLine +
            "· Dzielenie liczone znakowo; operator może dzielić inaczej.";
    }

    public static string Post(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „post: treść wpisu” — sprawdzę limit 280 znaków (X/Twitter) i policzę pozostałe.";
        int limit = 280;
        int remaining = limit - text.Length;
        int hashtags = Regex.Matches(text, @"#\w+").Count;
        int links = Regex.Matches(text, @"https?://\S+").Count;
        return "Wpis: " + text.Length + " z " + limit + " znaków (" + (remaining >= 0 ? "zostało " + remaining : "przekroczone o " + (-remaining)) + ")." + Environment.NewLine +
            "· linki: " + links + " (każdy liczy się zwykle jako 23 znaki) · hashtagi: " + hashtags + Environment.NewLine +
            "· zdania: " + Regex.Matches(text, @"[.!?](\s|$)").Count + " · akapity: " + text.Split('\n').Length + Environment.NewLine +
            (remaining < 0 ? "· Skróć tekst albo rozbij na wątek — nie tnę go po cichu." : "· Mieści się w limicie. Ja tego nigdzie nie wysyłam — wpis zostaje u Ciebie.");
    }

    public static string Mail(string payload)
    {
        string topic = (payload ?? "").Trim();
        if (topic.Length == 0) return "Użyj: „mail: urlop w sierpniu” — podaj temat, a złożę szkielet wiadomości.";
        return "Szkic wiadomości — temat: " + topic + Environment.NewLine + Environment.NewLine +
            "Szanowni Państwo," + Environment.NewLine +
            Environment.NewLine +
            "piszę w sprawie: " + topic + "." + Environment.NewLine +
            Environment.NewLine +
            "1. Kontekst — jedna lub dwie linie: co się stało i kiedy." + Environment.NewLine +
            "2. Czego potrzebuję — jedno zdanie, bez ogólników." + Environment.NewLine +
            "3. Termin — konkretna data, nie „jak najszybciej”." + Environment.NewLine +
            Environment.NewLine +
            "Z poważaniem," + Environment.NewLine +
            "[Twoje imię]" + Environment.NewLine + Environment.NewLine +
            "· To szkielet do wypełnienia treścią, nie gotowa wiadomość. Nie znam odbiorcy ani faktów — nie wymyślam ich za Ciebie." + Environment.NewLine +
            "· Sprawdź tytuł i termin: to dwie rzeczy, które ludzie przegapiają najczęściej.";
    }

    public static string Agenda(string payload)
    {
        string topic = (payload ?? "").Trim();
        if (topic.Length == 0) return "Użyj: „agenda: wdrożenie nowego modułu” — rozpiszę spotkanie 30 minut na bloki.";
        return "Agenda: " + topic + " (30 minut)" + Environment.NewLine +
            "· 0:00–0:03 — cel spotkania i decyzja, którą chcemy podjąć" + Environment.NewLine +
            "· 0:03–0:10 — stan obecny: fakty, liczby, blokery (kto mówi — jedna osoba)" + Environment.NewLine +
            "· 0:10–0:20 — warianty rozwiązania i koszt każdego" + Environment.NewLine +
            "· 0:20–0:26 — decyzja i właściciel zadania" + Environment.NewLine +
            "· 0:26–0:30 — podsumowanie: kto, co, do kiedy" + Environment.NewLine +
            "· Nie wysyłam zaproszeń i nie zakładam wydarzeń w kalendarzu — to plan na kartce.";
    }

    public static string Minutes(string payload)
    {
        string text = (payload ?? "").Trim();
        var points = text.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (points.Length == 0) return "Użyj: „protokol: decyzja o wdrożeniu | budżet 20 tys. | termin 15.10” — punkty rozdziel kreską.";
        var lines = points.Select(x => "· " + x);
        return "Protokół ze spotkania (" + DateTime.Now.ToString("dd.MM.yyyy HH:mm", Pl) + ")" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Do ustalenia: właściciel i termin dla każdego punktu — bez tego protokół jest listą życzeń." + Environment.NewLine +
            "· Zapisane lokalnie w rozmowie, jeśli wpiszesz to jako polecenie; nie wysyłam tego nikomu.";
    }

    public static string FollowUp(string payload)
    {
        string context = (payload ?? "").Trim();
        if (context.Length == 0) return "Użyj: „follow up: oferta z 12.09” — złożę krótką wiadomość przypominającą.";
        return "Szkic przypomnienia — kontekst: " + context + Environment.NewLine + Environment.NewLine +
            "Dzień dobry," + Environment.NewLine +
            Environment.NewLine +
            "wracam do sprawy: " + context + ". " + Environment.NewLine +
            Environment.NewLine +
            "Czy potrzebuje Pan/Pani jeszcze coś ode mnie, żeby ruszyć dalej? Jeśli temat jest nieaktualny, proszę o krótką informację — zamknę go u siebie." + Environment.NewLine +
            Environment.NewLine +
            "Z poważaniem," + Environment.NewLine +
            "[Twoje imię]" + Environment.NewLine + Environment.NewLine +
            "· Trzy zdania to maksimum: przypomnienie ma otwierać sprawę, nie przypominać o sobie.";
    }

    public static string Shorten(string payload)
    {
        string[] parts = (payload ?? "").Split('|', 2);
        if (parts.Length < 2) return "Użyj: „skroc do: 120 | treść, którą chcesz skrócić”.";
        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) || limit < 10)
            return "Podaj limit znaków (co najmniej 10) przed kreską, np. „skroc do: 120 | …”.";
        string text = parts[1].Trim();
        if (text.Length <= limit) return "Tekst ma " + text.Length + " znaków — mieści się w limicie " + limit + ". Skracać nie trzeba.";
        int cut = text.LastIndexOf(' ', Math.Min(limit, text.Length - 1));
        if (cut < limit / 2) cut = limit;
        string shortened = text[..cut].TrimEnd(',', ';', ':', ' ');
        return "Skrócone do " + limit + " znaków (było " + text.Length + "):" + Environment.NewLine + shortened + "…" + Environment.NewLine +
            "· Ciąłem na granicy słowa i dodałem wielokropek. Nie usuwam treści w środku zdania bez śladu — resztę widać powyżej." + Environment.NewLine +
            "· Jeśli sens wymaga przepisania, a nie ucięcia — powiedz, co ma zostać, a zrobię krótszą wersję ręcznie.";
    }

    public static string Tone(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „ton: treść wiadomości” — policzę długość, zdania i sygnały, które brzmią ostro.";
        string[] words = Regex.Split(text, @"\s+").Where(x => x.Length > 0).ToArray();
        int sentences = Math.Max(1, Regex.Matches(text, @"[.!?](\s|$)").Count);
        int exclamations = Regex.Matches(text, @"!").Count;
        int capsWords = words.Count(w => w.Length > 2 && w == w.ToUpperInvariant() && Regex.IsMatch(w, @"\p{L}"));
        var urgent = UrgentWords.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToArray();
        string verdict = exclamations + capsWords + urgent.Length >= 4 ? "brzmi nagląco/ostro" :
            exclamations + capsWords + urgent.Length >= 2 ? "lekko naciskający" : "neutralny";
        return "Ocena tonu wiadomości (" + words.Length + " słów, " + sentences + " zdań):" + Environment.NewLine +
            "· wykrzykniki: " + exclamations + " · słowa WERSALIKAMI: " + capsWords + " · słowa nacisku: " + (urgent.Length == 0 ? "brak" : string.Join(", ", urgent)) + Environment.NewLine +
            "· średnio " + N((double)words.Length / sentences, 1) + " słowa na zdanie — " + (words.Length / sentences > 25 ? "zdania są długie" : "zdania są czytelne") + Environment.NewLine +
            "· Wniosek: ton " + verdict + "." + Environment.NewLine +
            "· To liczenie sygnałów, nie analiza emocji. Nie oceniam intencji nadawcy — pokazuję, co widać w tekście.";
    }

    public static string Readability(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „czytelnosc: treść” — podam długość zdań i udział trudnych słów.";
        string[] words = Regex.Split(text, @"\s+").Where(x => x.Length > 1).ToArray();
        if (words.Length == 0) return "Nie widzę w tym tekście słów do policzenia.";
        int sentences = Math.Max(1, Regex.Matches(text, @"[.!?](\s|$)").Count);
        double avgWords = (double)words.Length / sentences;
        int longWords = words.Count(w => w.Trim('.', ',', '!', '?', ';', ':').Length > 12);
        double longShare = (double)longWords / words.Length * 100;
        string verdict = avgWords <= 15 && longShare <= 15 ? "czyta się łatwo" : avgWords <= 22 && longShare <= 25 ? "czyta się przeciętnie" : "czyta się ciężko";
        return "Czytelność tekstu:" + Environment.NewLine +
            "· słów: " + words.Length + " · zdań: " + sentences + " · średnio " + N(avgWords, 1) + " słowa na zdanie" + Environment.NewLine +
            "· słowa dłuższe niż 12 znaków: " + longWords + " (" + N(longShare, 1) + "%)" + Environment.NewLine +
            "· Wniosek: " + verdict + " — najkrótsza rada to dzielić zdania powyżej 20 słów." + Environment.NewLine +
            "· Nie liczę wskaźników dla języka angielskiego (Flesch itd.) dla polskiego tekstu — myliłyby bardziej niż pomagały.";
    }
}
