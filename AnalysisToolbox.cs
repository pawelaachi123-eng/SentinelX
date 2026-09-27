using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 15 · pozycje 1071–1140 — analiza danych bez sieci i bez zgadywania. Wszystkie funkcje są
/// czyste: te same liczby dają ten sam wynik, więc każdą odpowiedź sprawdza test bez komputera
/// użytkownika. Konwencja wejścia jest jedna: liczby rozdzielone spacjami albo średnikami,
/// a przecinek jest częścią ułamka („1,5”). Każda odpowiedź mówi, jakiego wzoru użyła i kiedy
/// wynik przestaje być sensowny (mała próba, brak tabel statystycznych).
/// </summary>
public static class AnalysisToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        // 0.97 · SEKCJA 19: wykres jako obraz PNG (ChartCommands; regresja podaje folder testowy).
        var chart = Regex.Match(text, @"^wykres[:\s]+(.+)$", RegexOptions.Singleline);
        if (chart.Success) return ChartCommands.Handle(chart.Groups[1].Value.Trim());

        if (Starts(text, "statystyki liczb", "analiza liczb", "opis zbioru"))
            return Describe(Numbers(Payload(raw, "statystyki liczb", "analiza liczb", "opis zbioru")));
        if (Starts(text, "kwartyle", "kwartyl"))
            return QuantilesReport(Numbers(Payload(raw, "kwartyle", "kwartyl")));
        if (Starts(text, "odchylenie", "wariancja"))
            return Dispersion(Numbers(Payload(raw, "odchylenie", "wariancja")));
        if (Starts(text, "skosnosc", "kurtoza", "ksztalt rozkladu"))
            return Shape(Numbers(Payload(raw, "skosnosc", "kurtoza", "ksztalt rozkladu")));
        if (Starts(text, "wspolczynnik zmiennosci", "zmiennosc procentowa", "cv"))
            return Variability(Numbers(Payload(raw, "wspolczynnik zmiennosci", "zmiennosc procentowa", "cv")));
        if (Starts(text, "przedzial ufnosci", "przedzial"))
            return Confidence(Numbers(Payload(raw, "przedzial ufnosci", "przedzial")));
        if (Starts(text, "korelacja", "kowariancja"))
            return Correlation(Payload(raw, "korelacja", "kowariancja"));
        if (Starts(text, "regresja", "prosta regresji"))
            return Regression(Payload(raw, "regresja", "prosta regresji"));
        if (Starts(text, "prognoza", "trend liniowy"))
            return Forecast(Payload(raw, "prognoza", "trend liniowy"));
        if (Starts(text, "trend"))
            return TrendLine(Numbers(Payload(raw, "trend")));
        if (Starts(text, "histogram", "rozklad"))
            return Histogram(Payload(raw, "histogram", "rozklad"));
        if (Starts(text, "normalizuj", "standaryzuj", "skala"))
            return Normalize(Payload(raw, "normalizuj", "standaryzuj", "skala"));
        if (Starts(text, "odleglosc", "metryka"))
            return Distances(Payload(raw, "odleglosc", "metryka"));
        if (Starts(text, "macierz pomylek", "dokladnosc klasyfikacji", "klasyfikacja"))
            return Classification(Payload(raw, "macierz pomylek", "dokladnosc klasyfikacji", "klasyfikacja"));
        if (Starts(text, "entropia"))
            return Entropy(Payload(raw, "entropia"));
        if (Starts(text, "gini", "nierownomiernosc"))
            return Gini(Payload(raw, "gini", "nierownomiernosc"));
        if (Starts(text, "outliery", "anomalie", "wykryj anomalie", "odstajace"))
            return Outliers(Payload(raw, "outliery", "anomalie", "wykryj anomalie", "odstajace"));
        if (Starts(text, "wygladzanie", "wygładzanie", "wygładz"))
            return Smoothing(Payload(raw, "wygladzanie", "wygładzanie", "wygładz"));
        if (Starts(text, "rangi", "ranking"))
            return Ranks(Payload(raw, "rangi", "ranking"));
        if (Starts(text, "percentyl", "centyl"))
            return Percentile(Payload(raw, "percentyl", "centyl"));
        if (Starts(text, "test t", "test studenta"))
            return TTest(Payload(raw, "test t", "test studenta"));
        return null;
    }

    /// <summary>
    /// Prefiks jest prawdziwy WYŁĄCZNIE z dwukropkiem („trend: 1 2 3” albo „trend:” sam).
    /// Powód jest praktyczny: „trend”, „ranking”, „skala” czy „prognoza” to zwykłe polskie słowa,
    /// więc wersja ze spacją zabierałaby zdania i zamiast modelu użytkownik dostawałby podpowiedź
    /// o składni. Dwukropek to jawna deklaracja: to polecenie, nie rozmowa.
    /// </summary>
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

    /// <summary>Liczby z tekstu. Przecinek i kropka są separatorami ułamka — liczby rozdzielaj spacjami lub średnikami.</summary>
    public static double[] Numbers(string payload)
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
    private static string List(IEnumerable<double> values) => string.Join(", ", values.Select(x => N(x)));

    public static double Mean(double[] values) => values.Length == 0 ? 0 : values.Average();

    /// <summary>Wariancja próby (n−1); dla jednej obserwacji zwraca 0 i mówi o tym w opisie.</summary>
    public static double Variance(double[] values)
    {
        if (values.Length < 2) return 0;
        double mean = values.Average();
        return values.Sum(x => (x - mean) * (x - mean)) / (values.Length - 1);
    }

    public static double StdDev(double[] values) => Math.Sqrt(Variance(values));

    /// <summary>Kwantyl z interpolacją liniową (ten sam wariant co PERCENTILE.INC w arkuszach).</summary>
    public static double Quantile(double[] values, double quantile)
    {
        if (values.Length == 0) return 0;
        double[] sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 1) return sorted[0];
        double position = (sorted.Length - 1) * Math.Clamp(quantile, 0, 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (position - lower) * (sorted[upper] - sorted[lower]);
    }

    public static double Pearson(double[] x, double[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        if (n < 2) return 0;
        double mx = x.Take(n).Average(), my = y.Take(n).Average();
        double numerator = 0, dx = 0, dy = 0;
        for (int i = 0; i < n; i++)
        {
            double a = x[i] - mx, b = y[i] - my;
            numerator += a * b; dx += a * a; dy += b * b;
        }
        return dx == 0 || dy == 0 ? 0 : numerator / Math.Sqrt(dx * dy);
    }

    /// <summary>Rangi z uśrednianiem remisów — potrzebne do korelacji Spearmana.</summary>
    public static double[] Rank(double[] values)
    {
        var order = values.Select((value, index) => (value, index)).OrderBy(pair => pair.value).ToArray();
        var ranks = new double[values.Length];
        int i = 0;
        while (i < order.Length)
        {
            int j = i;
            while (j + 1 < order.Length && Math.Abs(order[j + 1].value - order[i].value) < 1e-12) j++;
            double average = (i + j) / 2.0 + 1;
            for (int k = i; k <= j; k++) ranks[order[k].index] = average;
            i = j + 1;
        }
        return ranks;
    }

    private static string TooFew(double[] values, int needed, string what)
    {
        if (values.Length < needed)
            return "Potrzebuję co najmniej " + needed + " obserwacji, żeby policzyć " + what + " — dostałem " + values.Length + ". Nie zgaduję na jednej liczbie.";
        return "";
    }

    public static string Describe(double[] values)
    {
        if (values.Length == 0) return "Podaj liczby, np. „statystyki liczb: 3 4 4 5 9 12”. Liczby rozdzielaj spacjami albo średnikami (przecinek to część ułamka).";
        double[] sorted = values.OrderBy(x => x).ToArray();
        double mean = Mean(values), median = Quantile(values, 0.5), sd = StdDev(values);
        double q1 = Quantile(values, 0.25), q3 = Quantile(values, 0.75), iqr = q3 - q1;
        int outliers = values.Count(x => x < q1 - 1.5 * iqr || x > q3 + 1.5 * iqr);
        return "Statystyki zbioru (" + values.Length + " obserwacji, uporządkowane: " + List(sorted) + "):" + Environment.NewLine +
            "· suma: " + N(values.Sum()) + " · minimum: " + N(sorted[0]) + " · maksimum: " + N(sorted[^1]) + " · rozstęp: " + N(sorted[^1] - sorted[0]) + Environment.NewLine +
            "· średnia: " + N(mean) + " · mediana: " + N(median) + " · dominanta: " + Mode(sorted) + Environment.NewLine +
            "· kwartyle: Q1 " + N(q1) + " · Q3 " + N(q3) + " · IQR " + N(iqr) + " · przedział międzykwartylowy obejmuje " + N(iqr / Math.Max(1e-9, sorted[^1] - sorted[0]) * 100, 1) + "% rozstępu" + Environment.NewLine +
            "· wariancja próby: " + N(Variance(values)) + " · odchylenie standardowe: " + N(sd) + Environment.NewLine +
            "· obserwacje odstające poza płotkami 1,5 × IQR: " + outliers + (outliers > 0 ? " (sprawdź „outliery: " + string.Join(" ", values) + "”)" : "") + Environment.NewLine +
            (values.Length < 5 ? "· Uwaga: mniej niż 5 obserwacji — to opis liczb, nie rozkład. Nie wyciągam z tego wniosków o populacji." : "· To opis tej próby; przy małej próbie nie mów o całej populacji.");
    }

    private static string Mode(double[] sorted)
    {
        var groups = sorted.GroupBy(x => x).Select(g => (Value: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ToArray();
        if (groups.Length == 0) return "brak";
        if (groups[0].Count == 1) return "brak (każda wartość występuje raz)";
        return N(groups[0].Value) + " (×" + groups[0].Count + ")";
    }

    public static string QuantilesReport(double[] values)
    {
        if (values.Length < 2) return "Do kwartyli potrzebne są co najmniej 2 liczby, np. „kwartyle: 1 2 3 4 5”.";
        double q1 = Quantile(values, 0.25), q2 = Quantile(values, 0.5), q3 = Quantile(values, 0.75);
        return "Kwartyle (" + values.Length + " obserwacji, metoda interpolacji liniowej jak PERCENTILE.INC):" + Environment.NewLine +
            "· Q1 (25%): " + N(q1) + " · Q2/mediana (50%): " + N(q2) + " · Q3 (75%): " + N(q3) + Environment.NewLine +
            "· IQR (Q3 − Q1): " + N(q3 - q1) + " · rozstęp ćwiartkowy: " + N((q3 - q1) / 2) + Environment.NewLine +
            "· płotki Tukeya: " + N(q1 - 1.5 * (q3 - q1)) + " … " + N(q3 + 1.5 * (q3 - q1)) + " (poza nimi: obserwacja odstająca)" + Environment.NewLine +
            "· 5. i 95. percentyl: " + N(Quantile(values, 0.05)) + " … " + N(Quantile(values, 0.95));
    }

    public static string Dispersion(double[] values)
    {
        string few = TooFew(values, 2, "odchylenie");
        if (few.Length > 0) return few;
        double mean = Mean(values), variance = Variance(values), sd = StdDev(values);
        double population = values.Sum(x => (x - mean) * (x - mean)) / values.Length;
        return "Rozproszenie (" + values.Length + " obserwacji, średnia " + N(mean) + "):" + Environment.NewLine +
            "· wariancja próby (n−1): " + N(variance) + " · odchylenie standardowe próby: " + N(sd) + Environment.NewLine +
            "· wariancja populacji (n): " + N(population) + " · odchylenie populacji: " + N(Math.Sqrt(population)) + Environment.NewLine +
            "· zakres typowy (średnia ± 1 SD): " + N(mean - sd) + " … " + N(mean + sd) + Environment.NewLine +
            "· Zakładam rozkład zbliżony do normalnego tylko po to, żeby pokazać zakres — przy kilku liczbach to nie jest założenie o świecie.";
    }

    public static string Shape(double[] values)
    {
        string few = TooFew(values, 4, "skośność i kurtozę");
        if (few.Length > 0) return few;
        double mean = Mean(values), sd = StdDev(values), n = values.Length;
        if (sd == 0) return "Wszystkie wartości są identyczne — nie ma kształtu rozkładu do opisania.";
        double skew = values.Sum(x => Math.Pow((x - mean) / sd, 3)) * n / ((n - 1) * (n - 2));
        double kurt = values.Sum(x => Math.Pow((x - mean) / sd, 4)) * n * (n + 1) / ((n - 1) * (n - 2) * (n - 3)) -
            3 * (n - 1) * (n - 1) / ((n - 2) * (n - 3));
        string skewText = Math.Abs(skew) < 0.5 ? "rozkład w przybliżeniu symetryczny" : skew > 0 ? "ogon w prawo (przewaga niższych wartości)" : "ogon w lewo (przewaga wyższych wartości)";
        string kurtText = kurt > 0.5 ? "więcej wartości skrajnych niż w rozkładzie normalnym" : kurt < -0.5 ? "mniej wartości skrajnych niż w rozkładzie normalnym" : "ogony zbliżone do normalnego";
        return "Kształt rozkładu (" + values.Length + " obserwacji):" + Environment.NewLine +
            "· skośność: " + N(skew) + " — " + skewText + Environment.NewLine +
            "· kurtoza (nadwyżkowa): " + N(kurt) + " — " + kurtText + Environment.NewLine +
            "· Uwaga: przy n = " + values.Length + " te miary są czułe na pojedyncze wartości; traktuj je jako opis, nie diagnozę.";
    }

    public static string Variability(double[] values)
    {
        string few = TooFew(values, 2, "współczynnik zmienności");
        if (few.Length > 0) return few;
        double mean = Mean(values);
        if (Math.Abs(mean) < 1e-12) return "Średnia wynosi 0 — współczynnika zmienności nie da się policzyć (dzielenie przez zero). Użyj odchylenia standardowego.";
        double cv = StdDev(values) / Math.Abs(mean) * 100;
        string verdict = cv < 10 ? "bardzo jednorodne" : cv < 25 ? "umiarkowanie zróżnicowane" : cv < 50 ? "wyraźnie zróżnicowane" : "bardzo niejednorodne";
        return "Współczynnik zmienności: " + N(cv, 1) + "% — dane " + verdict + "." + Environment.NewLine +
            "· Wzór: odchylenie standardowe ÷ |średnia| × 100%. Sensowny tylko dla wielkości dodatnich (np. czas, kwota, masa)." + Environment.NewLine +
            "· To miara względna — pozwala porównać dwa zbiory o różnych jednostkach, ale nie mówi nic o kształcie rozkładu.";
    }

    public static string Confidence(double[] values)
    {
        string few = TooFew(values, 3, "przedział ufności");
        if (few.Length > 0) return few;
        double mean = Mean(values), sd = StdDev(values), n = values.Length;
        double se = sd / Math.Sqrt(n);
        return "Przedział ufności dla średniej (przybliżenie normalne):" + Environment.NewLine +
            "· średnia: " + N(mean) + " · błąd standardowy: " + N(se) + Environment.NewLine +
            "· 90%: " + N(mean - 1.645 * se) + " … " + N(mean + 1.645 * se) + Environment.NewLine +
            "· 95%: " + N(mean - 1.96 * se) + " … " + N(mean + 1.96 * se) + Environment.NewLine +
            "· 99%: " + N(mean - 2.576 * se) + " … " + N(mean + 2.576 * se) + Environment.NewLine +
            "· Uczciwie: to wzór z rozkładu normalnego. Przy n = " + n + " właściwy byłby rozkład t-Studenta — nie mam tablic, więc nie udaję dokładniejszego wyniku.";
    }

    private static (double[] First, double[] Second) TwoSeries(string payload)
    {
        string text = payload ?? "";
        int split = text.IndexOf('|');
        if (split < 0) split = text.IndexOf(';');
        if (split < 0) return ([], []);
        return (Numbers(text[..split]), Numbers(text[(split + 1)..]));
    }

    public static string Correlation(string payload)
    {
        var (x, y) = TwoSeries(payload);
        if (x.Length < 3 || y.Length < 3)
            return "Użyj: „korelacja: 1 2 3 4 | 2 4 6 9”. Potrzebuję dwóch ciągów po co najmniej 3 liczby, rozdzielonych pionową kreską.";
        if (x.Length != y.Length)
            return "Ciągi mają różną długość (" + x.Length + " i " + y.Length + "). Dopasuj liczbę obserwacji — nie skracam danych po cichu.";
        int n = x.Length;
        double r = Pearson(x, y);
        double mx = x.Average(), my = y.Average();
        double covariance = x.Select((value, i) => (value - mx) * (y[i] - my)).Sum() / (n - 1);
        double rho = Pearson(Rank(x), Rank(y));
        string strength = Math.Abs(r) < 0.2 ? "brak związku liniowego" : Math.Abs(r) < 0.4 ? "słaby" : Math.Abs(r) < 0.6 ? "umiarkowany" : Math.Abs(r) < 0.8 ? "silny" : "bardzo silny";
        if (r > 0) strength += ", dodatni"; else if (r < 0) strength += ", ujemny";
        return "Korelacja (" + n + " par obserwacji):" + Environment.NewLine +
            "· Pearson r: " + N(r) + " · R²: " + N(r * r) + " (tylko dla związku liniowego)" + Environment.NewLine +
            "· Spearman rho (rangi, odporny na skrajne wartości): " + N(rho) + Environment.NewLine +
            "· kowariancja próby: " + N(covariance) + Environment.NewLine +
            "· Odczyt: " + strength + ". To nie dowód przyczyny — korelacja pokazuje współzmienność, a nie sprawcę." + Environment.NewLine +
            (n < 8 ? "· Przy " + n + " parach pojedyncza obserwacja potrafi odwrócić wynik. Nie podejmuj na tym decyzji." : "· Przy " + n + " parach wynik jest stabilniejszy, ale nadal opisowy.");
    }

    public static string Regression(string payload)
    {
        var (x, y) = TwoSeries(payload);
        if (x.Length < 3 || y.Length < 3)
            return "Użyj: „regresja: 1 2 3 4 | 2 4 7 8”. Potrzebuję dwóch ciągów po co najmniej 3 liczby (x | y).";
        if (x.Length != y.Length) return "Ciągi mają różną długość (" + x.Length + " i " + y.Length + ") — nie zgaduję brakujących punktów.";
        var fit = Fit(x, y);
        return "Regresja liniowa y = a·x + b (" + x.Length + " punktów):" + Environment.NewLine +
            "· a (nachylenie): " + N(fit.Slope) + " · b (wyraz wolny): " + N(fit.Intercept) + Environment.NewLine +
            "· R²: " + N(fit.R2) + " · korelacja: " + N(fit.Correlation) + Environment.NewLine +
            "· Odczyt: " + (fit.R2 > 0.8 ? "punkty leżą blisko prostej" : fit.R2 > 0.5 ? "związek jest widoczny, ale z rozrzutem" : "prosta słabo opisuje te punkty — nie używaj jej do prognozy") + Environment.NewLine +
            "· Przykład użycia: dla x = " + N(x[^1] + 1) + " model przewiduje y ≈ " + N(fit.Slope * (x[^1] + 1) + fit.Intercept) + "." + Environment.NewLine +
            "· Prognoza poza zakresem danych to ekstrapolacja — im dalej od obserwacji, tym mniej warta.";
    }

    private sealed record LinearFit(double Slope, double Intercept, double R2, double Correlation);

    private static LinearFit Fit(double[] x, double[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        double mx = x.Take(n).Average(), my = y.Take(n).Average();
        double sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); }
        double slope = sxx == 0 ? 0 : sxy / sxx;
        double intercept = my - slope * mx;
        double r = Pearson(x, y);
        return new LinearFit(slope, intercept, r * r, r);
    }

    public static string Forecast(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 3) return "Użyj: „prognoza: 10 12 13 15”. Potrzebuję co najmniej 3 kolejnych wartości.";
        double[] x = Enumerable.Range(1, values.Length).Select(i => (double)i).ToArray();
        var fit = Fit(x, values);
        var next = Enumerable.Range(values.Length + 1, 3).Select(i => fit.Slope * i + fit.Intercept).ToArray();
        return "Prognoza na podstawie trendu liniowego (" + values.Length + " wartości):" + Environment.NewLine +
            "· trend: " + (fit.Slope >= 0 ? "+" : "") + N(fit.Slope) + " na krok · R²: " + N(fit.R2) + Environment.NewLine +
            "· najbliższe 3 wartości: " + List(next) + Environment.NewLine +
            "· Zakładam, że trend się utrzyma. Gdy dane falują (R² poniżej 0,5), ta prognoza jest tylko ozdobnikiem — i tak to napisałem.";
    }

    public static string TrendLine(double[] values)
    {
        if (values.Length < 3) return "Użyj: „trend: 10 12 13 15”. Potrzebuję co najmniej 3 kolejnych wartości.";
        double[] x = Enumerable.Range(1, values.Length).Select(i => (double)i).ToArray();
        var fit = Fit(x, values);
        string direction = fit.Slope > 0 ? "rosnący" : fit.Slope < 0 ? "malejący" : "płaski";
        return "Trend liniowy: " + direction + " o " + N(Math.Abs(fit.Slope)) + " na krok." + Environment.NewLine +
            "· R²: " + N(fit.R2) + " — " + (fit.R2 > 0.7 ? "trend opisuje dane dobrze" : "dane mają spory rozrzut wobec prostej") + Environment.NewLine +
            "· Zmiana od pierwszego do ostatniego: " + N(values[^1] - values[0]) + Environment.NewLine +
            "· Trend to nie przyczyna: pokazuję kierunek, nie wyjaśnienie.";
    }

    public static string Histogram(string payload)
    {
        var (series, bins) = TwoSeries(payload);
        double[] values = series.Length > 0 ? series : Numbers(payload);
        int binsCount = bins.Length > 0 ? Math.Clamp((int)bins[0], 2, 20) : 5;
        if (values.Length < 2) return "Użyj: „histogram: 1 2 2 3 5 8 | 4” (dane | liczba przedziałów, domyślnie 5).";
        double min = values.Min(), max = values.Max();
        if (max - min < 1e-12) return "Wszystkie wartości są identyczne (" + N(min) + ") — histogram ma jeden słupek.";
        double width = (max - min) / binsCount;
        int[] counts = new int[binsCount];
        foreach (double value in values)
        {
            int index = (int)((value - min) / width);
            if (index >= binsCount) index = binsCount - 1;
            counts[index]++;
        }
        var lines = new List<string>();
        for (int i = 0; i < binsCount; i++)
        {
            string range = N(min + i * width) + "–" + N(min + (i + 1) * width);
            lines.Add("· " + range.PadLeft(18) + " | " + new string('#', counts[i]) + " " + counts[i]);
        }
        return "Histogram (" + values.Length + " obserwacji, " + binsCount + " przedziałów po " + N(width) + "):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Przedziały są równej szerokości, ostatni domyka maksimum. To obraz danych, nie dopasowanie rozkładu.";
    }

    public static string Normalize(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „normalizuj: 2 4 6 10”. Potrzebuję co najmniej 2 liczb.";
        double min = values.Min(), max = values.Max(), mean = Mean(values), sd = StdDev(values);
        double[] minMax = max - min < 1e-12 ? values.Select(_ => 0.0).ToArray() : values.Select(x => (x - min) / (max - min)).ToArray();
        double[] z = sd < 1e-12 ? values.Select(_ => 0.0).ToArray() : values.Select(x => (x - mean) / sd).ToArray();
        return "Normalizacja (" + values.Length + " liczb):" + Environment.NewLine +
            "· min–max (0…1): " + List(minMax) + Environment.NewLine +
            "· z-score (średnia 0, odchylenie 1): " + List(z) + Environment.NewLine +
            "· min–max jest czułe na pojedynczą skrajną wartość; z-score zakłada rozkład zbliżony do normalnego." + Environment.NewLine +
            "· Nie zmieniam danych wejściowych — to tylko przeliczenie na pokazanie.";
    }

    public static string Distances(string payload)
    {
        var (first, second) = TwoSeries(payload);
        if (first.Length == 0 || first.Length != second.Length)
            return "Użyj: „odleglosc: 1 2 3 | 4 6 8”. Oba wektory muszą mieć tyle samo współrzędnych.";
        double euclidean = Math.Sqrt(first.Select((value, i) => Math.Pow(value - second[i], 2)).Sum());
        double manhattan = first.Select((value, i) => Math.Abs(value - second[i])).Sum();
        double chebyshev = first.Select((value, i) => Math.Abs(value - second[i])).Max();
        double cosine = Similarity(first, second);
        return "Odległości (" + first.Length + " współrzędnych):" + Environment.NewLine +
            "· euklidesowa: " + N(euclidean) + " · manhattan (miejska): " + N(manhattan) + " · Czebyszewa: " + N(chebyshev) + Environment.NewLine +
            "· podobieństwo kosinusowe: " + N(cosine) + " (1 = ten sam kierunek, 0 = prostopadłe)" + Environment.NewLine +
            "· Różne metryki odpowiadają na różne pytania: manhattan liczy kroki po osiach, euklidesowa — linię prostą.";
    }

    private static double Similarity(double[] a, double[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static string Classification(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 4)
            return "Użyj: „macierz pomylek: 50 10 5 35” w kolejności TP FP FN TN (trafione, fałszywe alarmy, pominięte, poprawne odrzucenia).";
        double tp = values[0], fp = values[1], fn = values[2], tn = values[3];
        double total = tp + fp + fn + tn;
        if (total <= 0) return "Suma pól macierzy pomyłek musi być większa od zera.";
        double accuracy = (tp + tn) / total;
        double precision = tp + fp == 0 ? 0 : tp / (tp + fp);
        double recall = tp + fn == 0 ? 0 : tp / (tp + fn);
        double specificity = tn + fp == 0 ? 0 : tn / (tn + fp);
        double f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        double balanced = (recall + specificity) / 2;
        double mccDenominator = Math.Sqrt((tp + fp) * (tp + fn) * (tn + fp) * (tn + fn));
        double mcc = mccDenominator == 0 ? 0 : (tp * tn - fp * fn) / mccDenominator;
        return "Macierz pomyłek TP=" + N(tp, 0) + " FP=" + N(fp, 0) + " FN=" + N(fn, 0) + " TN=" + N(tn, 0) + " (" + N(total, 0) + " przypadków):" + Environment.NewLine +
            "· dokładność (accuracy): " + Percent(accuracy) + " · zbalansowana dokładność: " + Percent(balanced) + Environment.NewLine +
            "· precyzja: " + Percent(precision) + " · czułość (recall): " + Percent(recall) + " · swoistość: " + Percent(specificity) + Environment.NewLine +
            "· F1: " + N(f1) + " · MCC (odporny na niezbalansowane klasy): " + N(mcc) + Environment.NewLine +
            "· Przy niezbalansowanych klasach sama dokładność kłamie — patrz na czułość, precyzję i MCC." + Environment.NewLine +
            "· Bez przedziałów ufności nie wiem, czy różnica między dwoma modelami jest realna, czy przypadkowa.";
    }

    private static string Percent(double value) => (value * 100).ToString("0.##", Pl) + "%";

    public static string Entropy(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „entropia: 8 1 1”. Podaj liczby wystąpień albo wartości — policzę udziały.";
        double total = values.Where(x => x > 0).Sum();
        if (total <= 0) return "Suma wartości musi być dodatnia.";
        double entropy = -values.Where(x => x > 0).Select(x => x / total * Math.Log2(x / total)).Sum();
        double max = Math.Log2(values.Count(x => x > 0));
        return "Entropia Shannona: " + N(entropy) + " bita." + Environment.NewLine +
            "· maksimum dla " + values.Count(x => x > 0) + " klas: " + N(max) + " bita · wykorzystane " + Percent(max == 0 ? 0 : entropy / max) + Environment.NewLine +
            "· 0 bita = pełna pewność (jedna klasa dominuje), maksimum = rozkład równy." + Environment.NewLine +
            "· Wzór: −Σ p·log₂(p) po dodatnich udziałach; zera pomijam, bo granica p·log₂(p) → 0.";
    }

    public static string Gini(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 1) return "Użyj: „gini: 2 3 5” — podaj udziały albo liczebności klas.";
        double total = values.Sum();
        if (total <= 0) return "Suma wartości musi być dodatnia.";
        double impurity = 1 - values.Select(x => Math.Pow(x / total, 2)).Sum();
        double[] sorted = values.OrderBy(x => x).ToArray();
        double sumOfWeighted = 0, cumulative = 0;
        for (int i = 0; i < sorted.Length; i++) { cumulative += sorted[i]; sumOfWeighted += cumulative; }
        double coefficient = sorted.Length <= 1 || cumulative == 0 ? 0 : (sorted.Length + 1 - 2 * sumOfWeighted / cumulative) / sorted.Length;
        return "Gini:" + Environment.NewLine +
            "· nieczystość Giniego (dla klasyfikacji): " + N(impurity) + " — 0 przy jednej klasie, rośnie przy wyrównanych udziałach" + Environment.NewLine +
            "· współczynnik Giniego (nierównomierność, 0 = równo): " + N(coefficient) + Environment.NewLine +
            "· Dwie różne miary o tej samej nazwie: jedna opisuje klasy w drzewie decyzyjnym, druga nierówność rozkładu. Nie mieszam ich.";
    }

    public static string Outliers(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 4) return "Użyj: „outliery: 3 4 4 5 100”. Potrzebuję co najmniej 4 liczb, żeby płotki IQR miały sens.";
        double q1 = Quantile(values, 0.25), q3 = Quantile(values, 0.75), iqr = q3 - q1;
        double lower = q1 - 1.5 * iqr, upper = q3 + 1.5 * iqr;
        var found = values.Select((value, index) => (value, index)).Where(x => x.value < lower || x.value > upper).ToArray();
        if (found.Length == 0)
            return "Brak obserwacji odstających: wszystkie " + values.Length + " wartości mieszczą się w płotkach " + N(lower) + " … " + N(upper) + " (Q1 " + N(q1) + ", Q3 " + N(q3) + ", 1,5 × IQR).";
        return "Obserwacje odstające (" + found.Length + " z " + values.Length + "):" + Environment.NewLine +
            string.Join(Environment.NewLine, found.Select(x => "· pozycja " + (x.index + 1) + ": " + N(x.value) + (x.value < lower ? " (poniżej dolnego płotka)" : " (powyżej górnego płotka)"))) + Environment.NewLine +
            "· Płotki Tukeya: " + N(lower) + " … " + N(upper) + " (Q1 " + N(q1) + ", Q3 " + N(q3) + ", IQR " + N(iqr) + ")." + Environment.NewLine +
            "· Odstająca nie znaczy błędna: to sygnał do sprawdzenia, nie polecenie usunięcia.";
    }

    public static string Smoothing(string payload)
    {
        var (series, alphaPart) = TwoSeries(payload);
        double[] values = series.Length > 0 ? series : Numbers(payload);
        double alpha = alphaPart.Length > 0 ? alphaPart[0] : 0.5;
        if (values.Length < 2) return "Użyj: „wygladzanie: 10 14 12 18 | 0,5”. Potrzebuję co najmniej 2 wartości i współczynnika 0–1.";
        if (alpha <= 0 || alpha > 1) return "Współczynnik wygładzania musi być w zakresie (0, 1>, np. 0,3. Bez tego wygładzanie nie ma sensu.";
        var smoothed = new List<double> { values[0] };
        double previous = values[0];
        for (int i = 1; i < values.Length; i++) { previous = alpha * values[i] + (1 - alpha) * previous; smoothed.Add(previous); }
        return "Wygładzanie wykładnicze (alpha = " + N(alpha, 2) + "):" + Environment.NewLine +
            "· dane: " + List(values) + Environment.NewLine +
            "· wygładzone: " + List(smoothed) + Environment.NewLine +
            "· Im mniejsze alpha, tym większa bezwładność i mniejszy wpływ bieżącej wartości. To filtr wygładzający, nie prognoza." + Environment.NewLine +
            "· Pierwszą wartość przyjmuję jako punkt startowy — nie mam obserwacji sprzed początku szeregu.";
    }

    public static string Ranks(string payload)
    {
        double[] values = Numbers(payload);
        if (values.Length < 2) return "Użyj: „rangi: 30 10 20 10”. Podaj co najmniej 2 liczby.";
        double[] ranks = Rank(values);
        var order = values.Select((value, index) => (value, rank: ranks[index], index)).OrderBy(x => x.rank).ToArray();
        return "Rangi (" + values.Length + " liczb, remisy uśrednione):" + Environment.NewLine +
            string.Join(Environment.NewLine, order.Select(x => "· " + (x.rank % 1 == 0 ? ((int)x.rank).ToString(Pl) : x.rank.ToString("0.#", Pl)) + ". " + N(x.value) + " (pozycja wejściowa " + (x.index + 1) + ")")) + Environment.NewLine +
            "· Rangi są podstawą korelacji Spearmana — odpornej na skrajne wartości.";
    }

    public static string Percentile(string payload)
    {
        var (series, point) = TwoSeries(payload);
        if (point.Length == 0) return "Użyj: „percentyl: 1 2 3 4 5 | 90” (dane | percentyl 0–100).";
        double[] values = series;
        double p = point[0];
        if (values.Length < 2) return "Potrzebuję co najmniej 2 liczb po lewej stronie kreski.";
        if (p < 0 || p > 100) return "Percentyl podaje się w zakresie 0–100, dostałem " + N(p, 2) + ".";
        return "Percentyl " + N(p, 2) + ": " + N(Quantile(values, p / 100)) + Environment.NewLine +
            "· dane (posortowane): " + List(values.OrderBy(x => x)) + Environment.NewLine +
            "· Interpolacja liniowa jak PERCENTILE.INC: wartość między sąsiednimi obserwacjami." + Environment.NewLine +
            "· Mediana to percentyl 50 — sprawdź „kwartyle: …”, żeby zobaczyć cały obraz.";
    }

    public static string TTest(string payload)
    {
        var (first, second) = TwoSeries(payload);
        if (first.Length < 3 || second.Length < 3) return "Użyj: „test t: 1 2 3 4 | 2 4 6 8”. Potrzebuję dwóch grup po co najmniej 3 liczby.";
        double m1 = first.Average(), m2 = second.Average(), v1 = Variance(first), v2 = Variance(second);
        double n1 = first.Length, n2 = second.Length;
        double denominator = Math.Sqrt(v1 / n1 + v2 / n2);
        if (denominator < 1e-12) return "Obie grupy mają zerowe odchylenie — test t nie ma tu czego mierzyć (brak zmienności).";
        double t = (m1 - m2) / denominator;
        double df = Math.Pow(v1 / n1 + v2 / n2, 2) / (Math.Pow(v1 / n1, 2) / (n1 - 1) + Math.Pow(v2 / n2, 2) / (n2 - 1));
        return "Test t Welcha dla dwóch grup:" + Environment.NewLine +
            "· grupa A: " + first.Length + " obserwacji, średnia " + N(m1) + " · grupa B: " + second.Length + ", średnia " + N(m2) + Environment.NewLine +
            "· t = " + N(t) + " · stopnie swobody ≈ " + N(df, 2) + " · różnica średnich: " + N(m1 - m2) + Environment.NewLine +
            "· |t| ≈ 2 odpowiada w przybliżeniu progowi 5% przy kilkunastu obserwacjach — ale to tylko orientacja." + Environment.NewLine +
            "· Uczciwie: nie mam tablic rozkładu t, więc nie podaję p-wartości. Wynik mówi, jak duża jest różnica względem rozrzutu, a nie czy jest istotna.";
    }
}
