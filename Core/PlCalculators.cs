using System.Globalization;
using System.Text;

namespace SentinelX.Core;

/// <summary>0.97 · kalkulatory, o które prosiłeś na liście 1550: finanse (#1141–1200) i zdrowie
/// (#1201–1250). Czyste funkcje: zero sieci, zero zapisu na dysk, zero „magicznych” liczb bez
/// podanego wzoru — każda odpowiedź mówi, z czego wynika.
/// <para>To kalkulator, nie doradca: pokazuje matematykę i jej założenia. Nie znam Twoich
/// zarobków, wydatków ani stanu zdrowia, dopóki mi ich nie podasz w poleceniu.</para></summary>
public static class PlCalculators
{
    private static readonly CultureInfo Pl = new("pl-PL");

    public static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = text.Replace(" ", "").Replace(',', '.').Replace("%", "").Replace("zł", "", StringComparison.OrdinalIgnoreCase);
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static string Money(double value) => value.ToString("N2", Pl) + " zł";
    private static string Num(double value) => value.ToString("0.##", Pl);

    // -------------------------------------------------------------------- finanse

    /// <summary>#1193 · inflacja: ile realnie jest warta kwota po latach i ile siły nabywczej znika.</summary>
    public static string Inflacja(double amount, double ratePercent, int years)
    {
        if (years is < 0 or > 60) return "Liczba lat musi być od 0 do 60.";
        if (ratePercent is < -50 or > 500) return "Stopa inflacji musi być od -50% do 500%.";
        double factor = Math.Pow(1 + ratePercent / 100d, years);
        double real = amount / factor;
        var builder = new StringBuilder();
        builder.AppendLine("INFLACJA · " + Money(amount) + " przy " + Num(ratePercent) + "% rocznie przez " + years + " lat");
        builder.AppendLine("· Siła nabywcza po " + years + " latach: " + Money(real));
        builder.AppendLine("· Ubyło: " + Money(amount - real) + " (" + Num((1 - 1 / factor) * 100) + "% wartości)");
        builder.AppendLine("Wzór: kwota / (1 + stopa)^lata. Nie zgaduję przyszłej inflacji — liczę dla wartości, którą podałeś.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1146 · cel oszczędzania: ile miesięcy do celu przy wpłacie i oprocentowaniu rocznym.</summary>
    public static string CelOszczedzania(double goal, double monthly, double ratePercent)
    {
        if (goal <= 0 || monthly <= 0) return "Cel i miesięczna wpłata muszą być większe od zera.";
        double monthlyRate = ratePercent / 100d / 12d;
        double balance = 0;
        int months = 0;
        while (balance < goal && months < 600)
        {
            balance = balance * (1 + monthlyRate) + monthly;
            months++;
        }
        double withoutInterest = goal / monthly;
        var builder = new StringBuilder();
        builder.AppendLine("CEL OSZCZĘDZANIA · " + Money(goal));
        if (months >= 600) { builder.AppendLine("· Przy " + Money(monthly) + " miesięcznie nie domknę celu w 50 lat — zwiększ wpłatę."); return builder.ToString().TrimEnd(); }
        builder.AppendLine("· Wpłata: " + Money(monthly) + " miesięcznie, oprocentowanie " + Num(ratePercent) + "% w skali roku");
        builder.AppendLine("· Czas: " + months + " miesięcy (" + (months / 12d).ToString("0.0", Pl) + " roku)");
        builder.AppendLine("· Suma wpłat: " + Money(monthly * months) + " · odsetki: " + Money(balance - monthly * months));
        builder.AppendLine("· Bez odsetek trwałoby to " + Math.Ceiling(withoutInterest) + " miesięcy — procenty skracają drogę o " +
            Math.Max(0, (int)Math.Ceiling(withoutInterest) - months) + " mies.");
        builder.AppendLine("Założenia: wpłata na koniec każdego miesiąca, stałe oprocentowanie, bez podatków i prowizji.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1152 · spłata długu: harmonogram raty stałej, a dla kilku długów porównanie
    /// kuli śnieżnej (od najmniejszego) i lawiny (od najdroższego).</summary>
    public static string SplataDlugu(IReadOnlyList<(string Name, double Balance, double Payment, double RatePercent)> debts)
    {
        if (debts.Count == 0) return "Podaj długi, np. „spłata długu: 5000 200 12” albo „spłata długu: 5000 200 12 | 12000 300 8”.";
        if (debts.Count == 1) return Schedule(debts[0]);
        var builder = new StringBuilder();
        builder.AppendLine("SPŁATA DŁUGÓW · " + debts.Count + " zobowiązań · porównanie strategii");
        double totalBalance = debts.Sum(d => d.Balance);
        double totalPayment = debts.Sum(d => d.Payment);
        builder.AppendLine("· Razem do spłaty: " + Money(totalBalance) + " · raty minimalne: " + Money(totalPayment) + " miesięcznie");
        foreach ((string label, Func<IReadOnlyList<(string, double, double, double)>, IReadOnlyList<(string, double, double, double)>> order) in new (string, Func<IReadOnlyList<(string, double, double, double)>, IReadOnlyList<(string, double, double, double)>>)[]
        {
            ("Kula śnieżna (najpierw najmniejszy dług — najszybszy efekt psychologiczny)", Snowball),
            ("Lawina (najpierw najwyższe oprocentowanie — najtaniej matematycznie)", Avalanche),
        })
        {
            (int months, double interest) = Simulate(order(debts.Select(d => (d.Name, d.Balance, d.Payment, d.RatePercent)).ToList()));
            builder.AppendLine("· " + label + ": " + months + " miesięcy (" + (months / 12d).ToString("0.0", Pl) + " roku), odsetki " + Money(interest));
        }
        builder.AppendLine("Symulacja: rata minimalna na każdy dług, a wolne środki zamykają pierwszy dług z kolejki (potem jego rata idzie na następny).");
        builder.AppendLine("Liczę na Twoich liczbach — nie znam umów, kar za wcześniejszą spłatę ani zmiennego oprocentowania.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1191 · ROI: zwrot z inwestycji w procentach i w złotówkach.</summary>
    public static string Roi(double invested, double returned)
    {
        if (invested <= 0) return "Wkład musi być większy od zera.";
        double gain = returned - invested;
        var builder = new StringBuilder();
        builder.AppendLine("ROI · " + Money(invested) + " → " + Money(returned));
        builder.AppendLine("· Zysk: " + Money(gain) + " · ROI: " + Num(gain / invested * 100) + "%");
        builder.AppendLine("· Próg: zwrot " + (returned >= invested ? "powyżej" : "poniżej") + " wkładu");
        builder.AppendLine("Wzór: (zwrot − wkład) / wkład. Nie uwzględniam czasu ani podatku — to czysty stosunek kwot.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1190 · próg rentowności: ile sztuk (i ile przychodu) trzeba, żeby wyjść na zero.</summary>
    public static string ProgRentownosci(double price, double variableCost, double fixedCost)
    {
        if (price <= variableCost) return "Cena musi być wyższa od kosztu zmiennego — inaczej każda sztuka dokłada stratę.";
        if (fixedCost < 0) return "Koszty stałe nie mogą być ujemne.";
        double units = fixedCost / (price - variableCost);
        var builder = new StringBuilder();
        builder.AppendLine("PRÓG RENTOWNOŚCI · cena " + Money(price) + ", koszt zmienny " + Money(variableCost) + ", koszty stałe " + Money(fixedCost));
        builder.AppendLine("· Marża na sztuce: " + Money(price - variableCost) + " (" + Num((price - variableCost) / price * 100) + "% ceny)");
        builder.AppendLine("· Próg: " + Math.Ceiling(units).ToString("N0", Pl) + " szt. = " + Money(Math.Ceiling(units) * price) + " przychodu");
        builder.AppendLine("Wzór: koszty stałe / (cena − koszt zmienny). Stałe koszty dzielę w całości — bez rozliczenia na okres.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1189 · amortyzacja (liniowa i degresywna 2×): roczny odpis i wartość po czasie.</summary>
    public static string Deprecjacja(double value, int years, double residualPercent = 0)
    {
        if (value <= 0) return "Wartość początkowa musi być większa od zera.";
        if (years is < 1 or > 50) return "Okres amortyzacji: 1–50 lat.";
        if (residualPercent is < 0 or >= 100) return "Wartość końcowa: 0–99% wartości początkowej.";
        double residual = value * residualPercent / 100d;
        double yearly = (value - residual) / years;
        var builder = new StringBuilder();
        builder.AppendLine("AMORTYZACJA · " + Money(value) + " przez " + years + " lat" + (residual > 0 ? ", wartość końcowa " + Num(residualPercent) + "%" : ""));
        builder.AppendLine("· Liniowo: " + Money(yearly) + " rocznie (" + Money(yearly / 12) + " miesięcznie)");
        builder.AppendLine("· Wartość po połowie okresu: " + Money(Math.Max(residual, value - yearly * (years / 2d))));
        builder.AppendLine("· Po całym okresie: " + Money(residual));
        builder.AppendLine("To amortyzacja księgowa (liniowa). Podatki i stawki urzędowe są poza tym kalkulatorem.");
        return builder.ToString().TrimEnd();
    }

    // -------------------------------------------------------------------- zdrowie

    /// <summary>#1217 · BMR (Mifflin-St Jeor): podstawowa przemiana materii.</summary>
    public static string Bmr(double weightKg, double heightCm, int age, bool male)
    {
        if (weightKg is < 30 or > 300 || heightCm is < 120 or > 250 || age is < 14 or > 100)
            return "Podaj wagę 30–300 kg, wzrost 120–250 cm i wiek 14–100 lat, np. „bmr: 80 180 30 m”.";
        double bmr = 10 * weightKg + 6.25 * heightCm - 5 * age + (male ? 5 : -161);
        var builder = new StringBuilder();
        builder.AppendLine("BMR · waga " + Num(weightKg) + " kg, wzrost " + Num(heightCm) + " cm, wiek " + age + " (" + (male ? "mężczyzna" : "kobieta") + ")");
        builder.AppendLine("· Podstawowa przemiana materii: " + Num(bmr) + " kcal/dzień");
        builder.AppendLine("· Wzór Mifflin-St Jeor: 10·waga + 6,25·wzrost − 5·wiek " + (male ? "+ 5" : "− 161"));
        builder.AppendLine("To spoczynkowy wydatek energetyczny — bez aktywności. „tdee: …” policzy całość.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1217 · TDEE: BMR × współczynnik aktywności.</summary>
    public static string Tdee(double weightKg, double heightCm, int age, bool male, int level)
    {
        if (level is < 1 or > 5) return "Poziom aktywności: 1 — siedzący, 2 — lekka, 3 — umiarkowana, 4 — wysoka, 5 — bardzo wysoka.";
        if (weightKg is < 30 or > 300 || heightCm is < 120 or > 250 || age is < 14 or > 100)
            return "Podaj wagę 30–300 kg, wzrost 120–250 cm i wiek 14–100 lat, np. „tdee: 80 180 30 m 3”.";
        double[] factors = [1.2, 1.375, 1.55, 1.725, 1.9];
        string[] names = ["siedzący tryb (biuro, bez treningów)", "lekka aktywność (1–3 treningi)", "umiarkowana (3–5 treningów)",
            "wysoka (6–7 treningów)", "bardzo wysoka (praca fizyczna + treningi)"];
        double bmr = 10 * weightKg + 6.25 * heightCm - 5 * age + (male ? 5 : -161);
        double tdee = bmr * factors[level - 1];
        var builder = new StringBuilder();
        builder.AppendLine("TDEE · " + names[level - 1] + " (×" + factors[level - 1].ToString("0.###", Pl) + ")");
        builder.AppendLine("· BMR: " + Num(bmr) + " kcal · TDEE: " + Num(tdee) + " kcal/dzień");
        builder.AppendLine("· Utrzymanie wagi: " + Num(tdee) + " kcal · redukcja (-15%): " + Num(tdee * 0.85) + " kcal · masa (+10%): " + Num(tdee * 1.10) + " kcal");
        builder.AppendLine("Szacunek ze wzoru, nie pomiar: dokładny wynik daje tylko kalorymetria albo kilka tygodni notowania wagi.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1202 · makro: podział kalorii na białko, tłuszcze i węglowodany w gramach.</summary>
    public static string Makro(double calories, double weightKg = 0)
    {
        if (calories is < 800 or > 8000) return "Podaj dzienną liczbę kalorii między 800 a 8000, np. „makro: 2400”.";
        double proteinGrams = weightKg > 0 ? Math.Clamp(weightKg * 1.8, 60, 260) : calories * 0.25 / 4;
        double fatGrams = calories * 0.30 / 9;
        double carbGrams = (calories - proteinGrams * 4 - fatGrams * 9) / 4;
        if (carbGrams < 0) carbGrams = 0;
        var builder = new StringBuilder();
        builder.AppendLine("MAKRO · " + Num(calories) + " kcal" + (weightKg > 0 ? " przy wadze " + Num(weightKg) + " kg" : ""));
        builder.AppendLine("· Białko: " + Num(proteinGrams) + " g (" + Num(proteinGrams * 4) + " kcal) — " + (weightKg > 0 ? "1,8 g/kg masy ciała" : "25% energii"));
        builder.AppendLine("· Tłuszcze: " + Num(fatGrams) + " g (" + Num(fatGrams * 9) + " kcal) — 30% energii");
        builder.AppendLine("· Węglowodany: " + Num(carbGrams) + " g (" + Num(carbGrams * 4) + " kcal) — reszta");
        builder.AppendLine("Ogólny podział sportowy, nie dieta medyczna: przy chorobach decyduje lekarz albo dietetyk.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1203 · woda: orientacyjne dzienne zapotrzebowanie i rozkład na szklanki.</summary>
    public static string Woda(double weightKg)
    {
        if (weightKg is < 30 or > 300) return "Podaj wagę 30–300 kg, np. „woda: 80”.";
        double ml = weightKg * 35;
        var builder = new StringBuilder();
        builder.AppendLine("NAWODNIENIE · waga " + Num(weightKg) + " kg");
        builder.AppendLine("· Orientacyjnie: " + (ml / 1000).ToString("0.00", Pl) + " l dziennie (" + Num(ml) + " ml)");
        builder.AppendLine("· To " + (ml / 250).ToString("0.0", Pl) + " szklanki po 250 ml");
        builder.AppendLine("· W upale albo przy treningie dolicz 0,5–1 l na każdą godzinę wysiłku");
        builder.AppendLine("Wzór: 35 ml na kilogram masy ciała. Zapotrzebowanie rośnie przy gorączce, upale i karmieniu piersią.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1218 · tętno: maksimum i strefy treningowe.</summary>
    public static string Tetno(int age)
    {
        if (age is < 14 or > 100) return "Podaj wiek 14–100 lat, np. „tetno: 30”.";
        double max = 220 - age;
        var builder = new StringBuilder();
        builder.AppendLine("TĘTNO · wiek " + age + " lat · maksymalne (220 − wiek): " + Num(max) + " uderzeń/min");
        (string name, double low, double high)[] zones =
        [
            ("Z1 regeneracja", 0.50, 0.60), ("Z2 wytrzymałość", 0.60, 0.70), ("Z3 tempo", 0.70, 0.80),
            ("Z4 próg", 0.80, 0.90), ("Z5 maksimum", 0.90, 1.00)
        ];
        foreach ((string name, double low, double high) in zones)
            builder.AppendLine("· " + name + " (" + (low * 100).ToString("0", Pl) + "–" + (high * 100).ToString("0", Pl) + "%): " +
                (max * low).ToString("0", Pl) + "–" + (max * high).ToString("0", Pl) + " uderzeń/min");
        builder.AppendLine("Wzór 220 − wiek to szacunek z dużym błędem osobniczym; strefy ustawia się najlepiej z próby wysiłkowej.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>#1227 · cykle snu: wyjścia z pełnych cykli 90 minut (plus ~15 min na zaśnięcie).</summary>
    public static string CykleSnu(TimeSpan bedtime, TimeSpan? wake = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CYKLE SNU · położenie o " + bedtime.ToString(@"hh\:mm") + " · zakładam ~15 minut na zaśnięcie");
        DateTime start = DateTime.Today.Add(bedtime).AddMinutes(15);
        if (wake.HasValue)
        {
            double minutes = (wake.Value - bedtime).TotalMinutes;
            if (minutes < 0) minutes += 24 * 60;
            double cycles = (minutes - 15) / 90d;
            builder.AppendLine("· Pobudka o " + wake.Value.ToString(@"hh\:mm") + ": " + Num(minutes / 60) + " h snu = " + cycles.ToString("0.0", Pl) + " cykli po 90 min");
            builder.AppendLine("· " + (cycles >= 5 ? "Wystarczająco — 5 pełnych cykli to dobry wynik." : "Mało: celuj w 5–6 cykli (7,5–9 h snu)."));
        }
        builder.AppendLine("· Dobre pory pobudki (pełne cykle):");
        for (int cycles = 4; cycles <= 6; cycles++)
            builder.AppendLine("   " + cycles + " cykli (" + (cycles * 1.5).ToString("0.0", Pl) + " h): " + start.AddMinutes(cycles * 90).ToString("HH:mm"));
        builder.AppendLine("Cykl 90 minut to uśrednienie — własny rytm poznasz po kilku tygodniach notowania samopoczucia.");
        return builder.ToString().TrimEnd();
    }

    private static string Schedule((string Name, double Balance, double Payment, double RatePercent) debt)
    {
        if (debt.Payment <= 0) return "Rata musi być większa od zera.";
        double monthlyRate = debt.RatePercent / 100d / 12d;
        if (debt.Balance * monthlyRate >= debt.Payment)
            return "Rata " + Money(debt.Payment) + " nie pokrywa nawet odsetek od " + Money(debt.Balance) + " przy " + Num(debt.RatePercent) + "% — kwota będzie rosła.";
        double balance = debt.Balance;
        double interest = 0;
        int months = 0;
        while (balance > 0.01 && months < 600)
        {
            double monthlyInterest = balance * monthlyRate;
            interest += monthlyInterest;
            balance += monthlyInterest - debt.Payment;
            months++;
        }
        var builder = new StringBuilder();
        builder.AppendLine("SPŁATA DŁUGU · " + Money(debt.Balance) + " · rata " + Money(debt.Payment) + " · oprocentowanie " + Num(debt.RatePercent) + "%");
        builder.AppendLine("· Czas: " + months + " miesięcy (" + (months / 12d).ToString("0.0", Pl) + " roku)");
        builder.AppendLine("· Suma rat: " + Money(debt.Payment * months) + " · w tym odsetki: " + Money(interest));
        builder.AppendLine("· Koszt długu: " + Num(interest / debt.Balance * 100) + "% pożyczonej kwoty");
        builder.AppendLine("Stałe oprocentowanie, rata co miesiąc, bez prowizji i ubezpieczeń — sprawdź umowę, jeśli ma RRSO.");
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<(string, double, double, double)> Snowball(IReadOnlyList<(string, double, double, double)> debts)
        => debts.OrderBy(d => d.Item2).ToList();

    private static IReadOnlyList<(string, double, double, double)> Avalanche(IReadOnlyList<(string, double, double, double)> debts)
        => debts.OrderByDescending(d => d.Item4).ToList();

    private static (int Months, double Interest) Simulate(IReadOnlyList<(string, double, double, double)> ordered)
    {
        var balances = ordered.Select(d => d.Item2).ToList();
        var annual = ordered.Select(d => d.Item4 / 100d / 12d).ToList();
        var minimums = ordered.Select(d => d.Item3).ToList();
        double interest = 0;
        int months = 0;
        while (balances.Any(b => b > 0.01) && months < 600)
        {
            months++;
            for (int i = 0; i < balances.Count; i++)
            {
                if (balances[i] <= 0.01) continue;
                double monthlyInterest = balances[i] * annual[i];
                interest += monthlyInterest;
                balances[i] += monthlyInterest - minimums[i];
            }
            // A closed debt frees its minimum for the next one in the queue (the whole point of both methods).
            for (int i = 0; i < balances.Count; i++)
            {
                if (balances[i] > 0.01) continue;
                double freed = minimums[i];
                minimums[i] = 0;
                int next = balances.FindIndex(b => b > 0.01);
                if (next >= 0) minimums[next] += freed;
            }
        }
        return (months, interest);
    }
}
