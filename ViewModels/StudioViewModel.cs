using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;

namespace SentinelX.ViewModels;

/// <summary>Jedno gotowe polecenie w Studiu: treść do wklejenia i jedno zdanie, co zwróci.</summary>
public sealed record StudioSample(string Command, string Note);

/// <summary>Kategoria Studia: numer porządkowy (bez emoji — czcionka systemowa zawsze je ma),
/// nazwa, opis i lista poleceń. Kolejność kategorii jest stała, więc menu wygląda tak samo
/// po restarcie.</summary>
public sealed record StudioCategory(string Key, string Index, string Title, string Blurb, IReadOnlyList<StudioSample> Samples);

/// <summary>Jedno uruchomienie w historii Studia — polecenie, skrót wyniku i godzina.</summary>
public sealed record StudioRun(string Command, string Preview, string Time);

/// <summary>
/// 0.97 · STUDIO — jedno miejsce, w którym widać i można uruchomić narzędzia lokalne dodane
/// w tym wydaniu (analiza danych, zdrowie, komunikacja, prywatność, modele lokalne) obok tych
/// wcześniejszych. Wszystko idzie tą samą drogą co czat (<see cref="IActionEngine"/>), więc
/// narzędzie samo decyduje o składni, a akcje systemowe nadal pytają o zgodę i zostawiają dowód.
/// Studio nic nie wysyła: wyniki są tylko liczone i pokazywane. Historia trzyma ostatnie 6
/// uruchomień w pamięci procesu — nie zapisuję jej na dysk (wynik może zawierać dane użytkownika).
/// </summary>
public partial class StudioViewModel : ObservableObject
{
    private const int MaxRecent = 6;
    private readonly IActionEngine engine;

    public IReadOnlyList<StudioCategory> Categories { get; }
    public ObservableCollection<StudioRun> Recent { get; } = [];

    [ObservableProperty] private StudioCategory selectedCategory;
    [ObservableProperty] private StudioSample? selectedSample;
    [ObservableProperty] private string input = "";
    [ObservableProperty] private string result = "";
    [ObservableProperty] private string status = "Wybierz gotowe polecenie z listy albo wpisz własne — Enter uruchamia.";
    [ObservableProperty] private string resultHeader = "Wynik pojawi się tutaj.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasRecent;

    /// <summary>Jedna linia pod nagłówkiem: ile poleceń i w jakich dziedzinach.</summary>
    public string CatalogLine =>
        Categories.Sum(x => x.Samples.Count) + " gotowych poleceń w " + Categories.Count + " kategoriach — " +
        string.Join(", ", Categories.Select(x => x.Title.ToLowerInvariant())) +
        ". Wszystko liczy się lokalnie; akcje systemowe zostają w rozmowie i nadal pytają o zgodę.";

    /// <summary>Liczba poleceń w wybranej kategorii (nagłówek listy).</summary>
    public string SelectedCategoryLine => SelectedCategory.Title + " · " + SelectedCategory.Samples.Count + " poleceń";

    public StudioViewModel(IActionEngine engine)
    {
        this.engine = engine;
        Categories =
        [
            new StudioCategory("analiza", "01", "Analiza danych", "Statystyki, korelacje, regresje, metryki klasyfikacji — z wzorem i zastrzeżeniami.",
            [
                new StudioSample("statystyki liczb: 3 4 4 5 9 12", "Suma, średnia, mediana, dominanta, kwartyle, IQR i odstające w jednym raporcie."),
                new StudioSample("kwartyle: 1 2 3 4 5 6 7 8 9 10", "Interpolacja liniowa jak PERCENTILE.INC plus płotki Tukeya."),
                new StudioSample("odchylenie: 10 12 14 16 18", "Wariancja i odchylenie dla próby oraz populacji, zakres średnia ± 1 SD."),
                new StudioSample("korelacja: 1 2 3 4 | 2 4 6 9", "Pearson, kowariancja i Spearman na rangach. Bez wniosków o przyczynie."),
                new StudioSample("regresja: 1 2 3 4 | 2 4 7 8", "Prosta y = a·x + b z R² i przykładem predykcji."),
                new StudioSample("prognoza: 10 12 13 15", "Trzy kolejne wartości z trendu liniowego — z ostrzeżeniem o ekstrapolacji."),
                new StudioSample("histogram: 1 2 2 3 5 8 | 4", "Rozkład na 4 przedziałach, słupki z #."),
                new StudioSample("normalizuj: 2 4 6 10", "Skala min–max 0…1 oraz z-score (średnia 0, odchylenie 1)."),
                new StudioSample("macierz pomylek: 50 10 5 35", "Dokładność, precyzja, czułość, swoistość, F1 i MCC dla TP/FP/FN/TN."),
                new StudioSample("entropia: 8 1 1", "Entropia Shannona w bitach i ile z maksimum dla liczby klas."),
                new StudioSample("outliery: 3 4 4 5 100", "Płotki IQR i pozycje obserwacji odstających (albo jasne „brak”)."),
                new StudioSample("odleglosc: 1 2 3 | 4 6 8", "Euklides, manhattan, Czebyszew i podobieństwo kosinusowe."),
                new StudioSample("rangi: 30 10 20 10", "Rangi z uśrednianiem remisów — podstawa Spearmana."),
                new StudioSample("test t: 1 2 3 4 | 2 4 6 8", "Test t Welcha. P-wartości nie podaję, bo nie mam tablic rozkładu — i tak mówię.")
            ]),
            new StudioCategory("zdrowie", "02", "Zdrowie (arytmetyka)", "Wzory na BMR, tętno, obwody, 1RM i tempo — z jawnym „to nie porada medyczna”.",
            [
                new StudioSample("bmr: 80 180 30 m", "Podstawowa przemiana materii wg Mifflin-St Jeor plus dzienne zapotrzebowanie."),
                new StudioSample("tdee: 80 180 30 m 1,55", "BMR razy współczynnik aktywności 1,2–1,9."),
                new StudioSample("makro: 2400 30 25 45", "Procenty na gramy białka, tłuszczu i węglowodanów z kontrolą bilansu."),
                new StudioSample("hrmax: 35", "Tętno maksymalne (220 − wiek i Tanaka) oraz pięć stref treningowych."),
                new StudioSample("whtr: 80 180", "Wskaźnik talia/wzrost z progiem 0,5."),
                new StudioSample("whr: 80 95 k", "Talia/biodra z progami WHO (0,90 / 0,85) i informacją, którego progu użyto."),
                new StudioSample("1rm: 80 5", "Szacunek maksymalnego ciężaru: Epley, Brzycki i Lombardi."),
                new StudioSample("tempo: 42 10", "Tempo na kilometr, prędkość, czas na 5 km i półmaraton."),
                new StudioSample("kroki: 8000 175", "Kroki na dystans (długość kroku z wzrostu) i orientacyjny wydatek energetyczny."),
                new StudioSample("woda: 80", "30 i 35 ml na kilogram masy ciała."),
                new StudioSample("sen: 23:30", "Godziny pobudki dla 3–6 cykli po 90 minut, z 15 minutami na zaśnięcie."),
                new StudioSample("deficyt: 90 80 0,5", "Ile tygodni do celu przy zadanym tempie i jaki to dzienny deficyt.")
            ]),
            new StudioCategory("komunikacja", "03", "Komunikacja", "Limity wiadomości, szkice maila, agendy, protokoły i ocena tonu.",
            [
                new StudioSample("sms: treść wiadomości", "Limit GSM-7 (160/153) albo UCS-2 (70/67) i liczba wiadomości."),
                new StudioSample("post: krótki wpis o wdrożeniu", "Limit 280 znaków, pozostałe znaki, linki po 23 znaki, hashtagi."),
                new StudioSample("mail: urlop w sierpniu", "Szkielet wiadomości: kontekst, prośba, termin — wyraźnie oznaczony jako szkic."),
                new StudioSample("agenda: wdrożenie nowego modułu", "Spotkanie 30 minut rozpisane na bloki z decyzją na końcu."),
                new StudioSample("protokol: decyzja o wdrożeniu | budżet 20 tys. | termin 15.10", "Punkty rozdzielone kreską plus znacznik czasu."),
                new StudioSample("follow up: oferta z 12.09", "Krótkie przypomnienie w trzech zdaniach."),
                new StudioSample("skroc do: 120 | Tekst, który trzeba skrócić do stu dwudziestu znaków bez utraty sensu.", "Cięcie na granicy słowa z wielokropkiem — bez cichego kasowania treści."),
                new StudioSample("ton: MUSISZ to zrobić natychmiast!!!", "Wykrzykniki, WERSALIKI i słowa nacisku → ocena tonu, nie analiza emocji."),
                new StudioSample("czytelnosc: Ala ma kota. Kot ma Alę. Pies też.", "Długość zdań i udział słów dłuższych niż 12 znaków.")
            ]),
            new StudioCategory("prywatnosc", "04", "Prywatność", "Co i gdzie leży na dysku, co jest szyfrowane, co wychodzi na zewnątrz. Nic nie usuwa.",
            [
                new StudioSample("prywatnosc", "Stan faktyczny: brak telemetrii, brak konta, brak chmury."),
                new StudioSample("gdzie sa moje dane", "Rozmiar i liczba plików w każdym katalogu danych — prawdziwy odczyt z dysku."),
                new StudioSample("duze pliki danych: 5", "Piętnaście największych plików powyżej progu; treści nie czytam."),
                new StudioSample("retencja: 90", "PODGLĄD: co byłoby starsze niż 90 dni. Nic nie kasuje."),
                new StudioSample("wiek danych", "Najstarszy i najnowszy plik w każdym katalogu."),
                new StudioSample("szyfrowanie", "Co jest AES-256-GCM (sejf), a co zwykłym JSON-em."),
                new StudioSample("uprawnienia", "Pliki, klucz autostartu HKCU, WM_CLOSE, lokalne API i sieć (tylko 127.0.0.1)."),
                new StudioSample("co wysylam", "Lista tego, co wychodzi z komputera: brak telemetrii, tylko lokalna Ollama."),
                new StudioSample("eksport danych", "Realne polecenia eksportu i kopia danych z manifestem."),
                new StudioSample("minimalizacja", "Co wyłączyć, żeby zapisywać mniej.")
            ]),
            new StudioCategory("modele", "05", "Modele lokalne", "Co zmieści się w Twojej pamięci i jak nie kusić się o za duży model.",
            [
                new StudioSample("modele lokalne", "Katalog modeli z rolą i rozmiarem — nic nie pobieram."),
                new StudioSample("model karta: qwen2.5:7b", "Parametry, warstwy, KV cache i wagi w q4_K_M z podanym wzorem."),
                new StudioSample("model dopasuj: 8", "Co się zmieści w 8 GB, co na styk, a co nie."),
                new StudioSample("model audyt", "To samo liczone od realnego RAM tego komputera."),
                new StudioSample("model rola: kod", "Sugestia modelu do kodowania, rozmowy, wizji albo embeddingów."),
                new StudioSample("preset modelu: szybki", "Temperatura, kontekst i limit odpowiedzi dla trybu szybkiego."),
                new StudioSample("prompt szablon: kod", "Gotowy prompt systemowy (10 szablonów)."),
                new StudioSample("model kv: qwen2.5:7b 8192", "Ile pamięci zajmie KV cache przy zadanym kontekście."),
                new StudioSample("model porownaj: qwen2.5:7b vs qwen2.5:14b", "Porównanie dwóch modeli pod kątem pamięci i tempa."),
                new StudioSample("model polityka", "Twoje domyślne role, temperatura, kontekst i progi."),
                new StudioSample("model offline", "Co działa bez modelu, a czego bez niego po prostu nie ma.")
            ]),
            new StudioCategory("rdzen", "06", "Rdzeń i narzędzia", "Runtime 0.97, narzędzia offline, tekst, liczby i kod — to, co działa bez sieci.",
            [
                new StudioSample("rdzen", "Stan rdzenia: moduły, kolejka, flagi, cache, kopie."),
                new StudioSample("zdrowie", "Sprawdzenia środowiska — wyjątek to problem z treścią, nie z kodem."),
                new StudioSample("metryki", "Licznik, wskaźnik i percentyle p50/p95."),
                new StudioSample("cron opis: */15 * * * *", "Opis wyrażenia crona po polsku."),
                new StudioSample("cron nastepne: 0 8 * * 1-5", "Najbliższe uruchomienia dla wyrażenia."),
                new StudioSample("diff: Ala ma kota | Ala ma psa", "Diff na LCS z limitem 600 linii."),
                new StudioSample("regex: [0-9]+ | abc123", "Dopasowania z limitem 1 s na katastrofalny wzorzec."),
                new StudioSample("semver podbij minor: 1.2.3", "Podbicie wersji zgodnie z semver."),
                new StudioSample("ip: 10.0.0.1/24", "Adres sieci, rozgłoszenie i liczba hostów."),
                new StudioSample("uuid7", "Identyfikator sortowalny czasowo."),
                new StudioSample("liczba slownie: 1234,56", "Kwota po polsku z odmianą tysięcy."),
                new StudioSample("roi: 2000 15000", "Zwrot z inwestycji wraz z założeniem."),
                new StudioSample("budzet: 6000", "Podział 50/30/20 na potrzeby, zachcianki i oszczędności."),
                new StudioSample("statystyki tekstu: Ala ma kota. Kot ma Alę.", "Znaki, słowa, zdania, czas czytania i najczęstsze słowa.")
            ])
        ];
        selectedCategory = Categories[0];
    }

    /// <summary>Wybór kategorii z zewnątrz (paleta „//”) — nieznany klucz zostawia obecny wybór,
    /// więc literówka w skrócie nic nie psuje.</summary>
    public void ShowCategory(string key)
    {
        StudioCategory? match = Categories.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match != null) SelectedCategory = match;
    }

    /// <summary>Nagłówek listy zależy od wybranej kategorii — bez tego powiadomienia zostałby stary tekst.</summary>
    partial void OnSelectedCategoryChanged(StudioCategory value) => OnPropertyChanged(nameof(SelectedCategoryLine));

    /// <summary>Kliknięcie gotowego polecenia: wstaw do pola i pokaż, co zwróci.</summary>
    [RelayCommand]
    private void UseSample(StudioSample? sample)
    {
        if (sample == null) return;
        SelectedSample = sample;
        Input = sample.Command;
        Status = sample.Note;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        string command = (Input ?? "").Trim();
        if (command.Length == 0) { Status = "Wpisz polecenie albo kliknij gotowe z listy po lewej."; return; }
        IsBusy = true;
        Status = "Liczę lokalnie…";
        try
        {
            IntentResult answer = await engine.ExecuteAsync(command);
            Result = answer.Text ?? "";
            ResultHeader = "Ostatni wynik · " + command;
            Status = answer.Action == null
                ? "Gotowe. To policzone lokalnie — bez internetu i bez modelu, jeśli narzędzie działa offline."
                : "Gotowe. Do akcji dołączono dowód wykonania (zakładka Centrum → Akcje).";
            Recent.Insert(0, new StudioRun(command, OneLine(Result), DateTime.Now.ToString("HH:mm:ss")));
            while (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
            HasRecent = Recent.Count > 0;
        }
        catch (Exception ex)
        {
            Result = "";
            Status = "Nie udało się: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Copy()
    {
        if (Result.Length == 0) { Status = "Nie ma czego kopiować — najpierw uruchom polecenie."; return; }
        try { Clipboard.SetText(Result); Status = "Wynik skopiowany do schowka."; }
        catch (Exception ex) { Status = "Schowek jest niedostępny (" + ex.GetType().Name + ") — zaznacz tekst i skopiuj ręcznie."; }
    }

    [RelayCommand]
    private void Clear()
    {
        Input = "";
        Result = "";
        SelectedSample = null;
        ResultHeader = "Wynik pojawi się tutaj.";
        Status = "Wyczyszczone. Historia uruchomień zostaje poniżej.";
    }

    /// <summary>Skrót wyniku do jednej linii w historii — pełny tekst zostaje w polu wyniku.</summary>
    private static string OneLine(string text)
    {
        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return first.Length <= 90 ? first : first[..90] + "…";
    }
}
