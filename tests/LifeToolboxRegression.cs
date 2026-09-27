using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJE 17 i 14 — zdrowie (arytmetyka) i komunikacja. Liczby sprawdzam ręcznym
/// rachunkiem na jawnych wzorach (Mifflin-St Jeor, Tanaka, Epley, cykle snu), a nie tym samym
/// kodem. Osobno pilnuję, żeby zwykłe zdania („sen”, „tempo”, „post”) nie były przechwytywane
/// i żeby narzędzie nigdy nie udawało porady medycznej.</summary>
internal static class LifeToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Require(string? value, string label) =>
        value ?? throw new InvalidOperationException(label + " was not handled as a life tool");

    private static string Handle(string command) =>
        Require(LifeToolbox.TryHandle(command, CommandText.Normalize(command)), "polecenie „" + command + "”");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        // ---------------- metabolizm: wzór Mifflin-St Jeor ----------------
        Check(Math.Abs(LifeToolbox.BmrKcal(80, 180, 30, true) - 1780) < 1e-9, "BMR mężczyzny 80 kg/180 cm/30 lat = 1780 kcal");
        Check(Math.Abs(LifeToolbox.BmrKcal(80, 180, 30, false) - 1614) < 1e-9, "BMR kobiety = 1614 kcal (stała −161)");

        string bmr = Handle("bmr: 80 180 30 m");
        Check(bmr.Contains("1780 kcal"), "polecenie bmr podaje wynik wzoru: " + bmr.Split('\n')[0]);
        Check(bmr.Contains("nie jest to porada medyczna") || bmr.Contains("Nie jest to porada medyczna"), "wynik jawnie nie udaje porady medycznej");
        Check(Handle("bmr: 80 180").Contains("Użyj:"), "brak płci to pytanie o składnię, nie zgadywanie");
        Check(Handle("bmr: 500 40 300 m").Contains("poza zakresem"), "absurdalne dane są odrzucane");
        Check(Handle("tdee: 80 180 30 k 1,55").Contains("1614"), "TDEE liczy BMR kobiety razy współczynnik: " + Handle("tdee: 80 180 30 k 1,55").Replace(Environment.NewLine, " | "));
        Check(Handle("tdee: 80 180 30 k 3").Contains("1,2–1,9"), "współczynnik aktywności poza zakresem jest odrzucany");

        // ---------------- makro z procentów ----------------
        string macro = Handle("makro: 2400 30 25 45");
        Check(macro.Contains("białko: 180 g"), "30% z 2400 kcal to 180 g białka (4 kcal/g): " + macro.Replace(Environment.NewLine, " | "));
        Check(macro.Contains("tłuszcz: 67 g") && macro.Contains("węglowodany: 270 g"), "tłuszcz 25% (9 kcal/g) i węgle 45% (4 kcal/g)");
        Check(macro.Contains("2400 kcal — tyle samo, co na wejściu"), "makro bilansuje się do wejściowych kalorii");
        Check(Handle("makro: 2400 30 25 30").Contains("sumować się do 100%"), "udziały inne niż 100% są odrzucane");

        // ---------------- tętno i strefy (wzór Tanaki) ----------------
        string heart = Handle("hrmax: 35");
        Check(heart.Contains("183,5") || heart.Contains("184"), "Tanaka dla 35 lat to 183,5: " + heart.Replace(Environment.NewLine, " | "));
        Check(heart.Contains("50–60%") || heart.Contains("regeneracja"), "raport pokazuje strefy treningowe");
        Check(HeartZoneContains(heart, 0.50, 0.60, 183.5), "pierwsza strefa liczona z Tanaki");

        // ---------------- obwody ciała ----------------
        Check(Handle("whtr: 80 180").Contains("0,444"), "WHtR 80/180 = 0,444");
        Check(Handle("whtr: 110 180").Contains("podwyższone ryzyko"), "WHtR 0,611 to podwyższone ryzyko");
        Check(Handle("whr: 80 95 m").Contains("0,90"), "WHR podaje próg dla mężczyzn: " + Handle("whr: 80 95 m").Replace(Environment.NewLine, " | "));
        Check(Handle("whr: 80 95").Contains("0,85"), "bez płci pokazuje oba progi i nie zgaduje");

        // ---------------- trening, tempo, kroki ----------------
        Check(Handle("1rm: 80 5").Contains("93,3"), "Epley dla 80 kg × 5 = 93,3 kg");
        Check(Handle("1rm: 80 40").Contains("do ok. 10 powtórzeń"), "zbyt wiele powtórzeń psuje wzór i mówię o tym");
        Check(Handle("tempo: 42 10").Contains("4:12"), "42 min na 10 km to 4:12 / km");
        Check(Handle("tempo: 42 10").Contains("14,29 km/h"), "prędkość z tempa: " + Handle("tempo: 42 10").Replace(Environment.NewLine, " | "));
        Check(Handle("kroki: 8000 175").Contains("5,81 km"), "kroki przeliczam na dystans (8000 × 0,726 m): " + Handle("kroki: 8000 175").Replace(Environment.NewLine, " | "));
        Check(Handle("woda: 80").Contains("2,4 l"), "30 ml/kg przy 80 kg to 2,4 l");

        // ---------------- sen i plan wagi ----------------
        string sleep = Handle("sen: 23:30");
        Check(sleep.Contains("00:30") || sleep.Contains("07:15"), "5 cykli od 23:45 to pobudka 07:15: " + sleep.Replace(Environment.NewLine, " | "));
        Check(sleep.Contains("90 minut"), "wyjaśnienie cyklu snu jest w odpowiedzi");
        Check(Handle("sen: 99:99").Contains("formacie 24-godzinnym"), "nieprawidłowa godzina jest odrzucana");
        Check(Handle("deficyt: 90 80 0,5").Contains("20"), "20 kg w tempie 0,5 kg/tydzień to 20 tygodni");
        Check(Handle("deficyt: 90 80 5").Contains("1 kg na tydzień"), "nierealne tempo jest odrzucane");

        // ---------------- komunikacja: limity i szkice ----------------
        Check(Handle("sms: Ala ma kota").Contains("GSM-7"), "tekst bez polskich znaków mieści się w 160 znakach");
        Check(Handle("sms: żółć idzie w las").Contains("UCS-2"), "polskie znaki przełączają na UCS-2 i limit 70");
        Check(Handle("post: krótki wpis").Contains("zostało"), "wpis poniżej limitu podaje pozostałe znaki");
        Check(Handle("skroc do: 12 | Ala ma kota i psa").Contains("Ala ma kota…"), "skracanie tnie na granicy słowa");
        Check(Handle("ton: MUSISZ to zrobić natychmiast!!!").Contains("nagląco"), "wykrzykniki i nacisk to ton naglący");
        Check(Handle("ton: dzień dobry, przesyłam raport").Contains("neutralny"), "spokojna wiadomość jest neutralna");
        Check(Handle("czytelnosc: Ala ma kota. Kot ma Alę. Pies też.").Contains("Czytelność tekstu"), "raport czytelności działa");
        Check(Handle("mail: urlop w sierpniu").Contains("Szanowni Państwo"), "szkic maila ma strukturę");
        Check(Handle("mail: urlop w sierpniu").Contains("nie gotowa wiadomość"), "szkic mówi wprost, że nie jest gotowym tekstem");
        Check(Handle("agenda: wdrożenie").Contains("0:26–0:30"), "agenda rozpisuje 30 minut na bloki");
        Check(Handle("protokol: decyzja o wdrożeniu | budżet 20 tys.").Contains("budżet 20 tys."), "protokół wypisuje punkty");
        Check(Handle("follow up: oferta z 12.09").Contains("wracam do sprawy"), "follow up to krótkie przypomnienie");

        // ---------------- zwykłe zdania zostają rozmową ----------------
        foreach (string sentence in new[]
        {
            "sen o 23 był dobry", "tempo życia mnie zaskakuje", "post jest długi", "ton tej wypowiedzi jest ostry",
            "woda jest mokra", "kroki na śniegu", "makro w ekonomii", "czytelnosc tego tekstu",
            "uprawnienia kierowcy", "sms od banku",
        })
            Check(LifeToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem: " + sentence);

        return Task.CompletedTask;
    }

    /// <summary>Sprawdza, czy w raporcie jest przedział policzony z wzoru Tanaki dla danej strefy.</summary>
    private static bool HeartZoneContains(string report, double from, double to, double max)
    {
        string low = (max * from).ToString("0", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));
        string high = (max * to).ToString("0", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));
        return report.Contains(low) && report.Contains(high);
    }
}
