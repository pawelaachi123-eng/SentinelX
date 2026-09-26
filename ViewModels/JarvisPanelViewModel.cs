using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;

namespace SentinelX.ViewModels;

/// <summary>Jeden przycisk panelu: etykieta, polecenie do wysłania i to, czy jest główny.</summary>
public sealed record JarvisAction(string Label, string Command, bool Primary = false)
{
    /// <summary>Podstawia wpisaną wartość w miejsce <c>{input}</c>. Zwraca null, gdy wymagana jest treść.</summary>
    public string? Resolve(string input, string requiredHint, out string? error)
    {
        error = null;
        if (!Command.Contains("{input}", StringComparison.Ordinal)) return Command;
        if (string.IsNullOrWhiteSpace(input)) { error = requiredHint; return null; }
        return Command.Replace("{input}", input.Trim(), StringComparison.Ordinal);
    }
}

/// <summary>Jedna sekcja panelu JARVIS (pogoda, multimedia, dom, sekwencje, agent, ekran, pamięć).</summary>
public sealed record JarvisSection(string Key, string Icon, string Label, string Caption, string InputLabel, string InputHint, string RequiredHint, IReadOnlyList<JarvisAction> Actions);

/// <summary>
/// 0.96 · panel warstwy JARVIS w Centrum. Panel niczego nie wykonuje sam: każdy przycisk wysyła
/// tekstowe polecenie przez IActionEngine — dokładnie tę samą kolejkę co czat (STOP awaryjny,
/// centrum zgód, audyt z requestId, dowody). Dzięki temu nie istnieje „druga ścieżka wykonania”
/// ukryta w interfejsie, a to, co widać w polu raportu, da się powtórzyć wpisując polecenie.
/// </summary>
public sealed partial class JarvisPanelViewModel : ObservableObject, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    private readonly CancellationTokenSource disposeCts = new();
    private bool busy;

    public IReadOnlyList<JarvisSection> Sections { get; }
    [ObservableProperty] private JarvisSection? selectedSection;
    [ObservableProperty] private string input = "";
    [ObservableProperty] private string report = "";
    [ObservableProperty] private string status = "Wybierz sekcję i użyj przycisku — każde polecenie widać w raporcie przed i po wykonaniu.";
    [ObservableProperty] private bool isBusy;
    public ObservableCollection<string> Notes { get; } = [];

    public JarvisPanelViewModel(IActionEngine engine, IUiDispatcher dispatcher)
    {
        this.engine = engine; this.dispatcher = dispatcher;
        Sections =
        [
            new("pogoda", "🌦", "Pogoda", "Open-Meteo, bez konta i bez klucza API. To jedyne polecenie w Sentinelu, które wychodzi do internetu — wyłączony = żadnego łączenia.",
                "Miasto (puste = z ustawień)", "np. Kraków albo Lwów", "Wpisz miasto albo zostaw puste, żeby użyć miasta z ustawień.",
                [new("Pogoda", "pogoda", true), new("Czy będzie padać", "czy bedzie padac"), new("Temperatura", "temperatura na zewnatrz"), new("Pogoda: {input}", "pogoda {input}")]),
            new("multimedia", "🎧", "Multimedia", "Klawisze sterowania odtwarzaczem (Spotify, VLC, przeglądarka) + głośność z odczytem zwrotnym. Tytułu utworu Sentinel nie czyta.",
                "Opcjonalnie: o ile procent zmienić głośność", "np. 10", "Wpisz liczbę od 1 do 50.",
                [new("Co gra", "co gra", true), new("⏮", "poprzedni utwor"), new("⏯", "pauza"), new("⏭", "nastepny utwor"), new("⏹", "stop odtwarzanie"), new("🔇", "wycisz odtwarzacz"), new("Głośniej", "glosniej"), new("Ciszej", "ciszej"), new("Głośniej o {input}", "glosniej {input}")]),
            new("dom", "🏠", "Dom", "Home Assistant w sieci lokalnej. Wyłączone, dopóki nie włączysz w Ustawienia → Jarvis — bo steruje fizycznymi urządzeniami. Token tylko ze zmiennej SENTINEL_HA_TOKEN.",
                "Nazwa urządzenia lub scenerii", "np. światło salon", "Wpisz nazwę urządzenia widoczną na liście encji.",
                [new("Stan integracji", "dom status", true), new("Lista encji", "dom lista"), new("Włącz: {input}", "dom: wlacz {input}"), new("Wyłącz: {input}", "dom: wylacz {input}"), new("Scena: {input}", "scena: {input}")]),
            new("sekwencje", "🔁", "Sekwencje", "Nazwane listy kroków. Podgląd przed uruchomieniem, stop na błędzie, kroki niszczące odrzucane przy zapisie i przy odczycie.",
                "Nazwa albo „nazwa = krok1; krok2”", "utwórz: poranek = który jest dzień; plan dnia", "Wpisz nazwę sekwencji (albo „nazwa = kroki” przy tworzeniu).",
                [new("Lista", "sekwencje", true), new("Utwórz: {input}", "utwórz sekwencję: {input}"), new("Podgląd: {input}", "podgląd sekwencji: {input}"), new("Uruchom: {input}", "uruchom sekwencję: {input}"), new("Usuń: {input}", "usuń sekwencję: {input}")]),
            new("agent", "🤖", "Agent", "Lokalny model pyta o narzędzia wyłącznie do odczytu, z limitem kroków. Nie ma dostępu do zgody „potwierdź” i nie zmodyfikuje niczego.",
                "Cel do wykonania", "np. sprawdź dysk i temperaturę, potem podsumuj", "Napisz, co agent ma sprawdzić.",
                [new("Status", "agent status", true), new("Narzędzia", "narzedzia agenta"), new("Wykonaj: {input}", "agent: {input}")]),
            new("ekran", "👁", "Ekran", "Lokalny model wizyjny patrzy na zrzut ekranu. Obraz jest pamięciowy — nie zapisujemy go na dysk i nie wysyłamy do żadnej usługi.",
                "Opcjonalnie: o co zapytać o ekran", "np. co jest w tym oknie błędu", "",
                [new("Opisz ekran", "co jest na ekranie", true), new("Przeczytaj ekran", "przeczytaj ekran"), new("Zapytaj: {input}", "opisz {input}")]),
            new("pamiec", "🧠", "Pamięć", "Indeks semantyczny to DODATEK do wyszukiwania tekstowego (liczony lokalnie przez Ollamę). Wyszukiwanie po literach zostało bez zmian.",
                "Fraza do szukania semantycznego", "np. o przeprowadzce", "Wpisz, czego szukać.",
                [new("Status indeksu", "indeks semantyczny", true), new("Zbuduj indeks", "indeks semantyczny: zbuduj"), new("Szukaj: {input}", "szukaj semantycznie: {input}")]),
        ];
        SelectedSection = Sections[0];
        engine.Changed += SyncStatus;
    }

    partial void OnSelectedSectionChanged(JarvisSection? value)
    {
        if (value == null) return;
        Input = "";
        Status = value.Caption;
    }

    private void SyncStatus() => dispatcher.Post(() =>
    {
        if (engine.IsStopped) Status = "⛔ STOP awaryjny jest aktywny — panel nic nie wyśle, dopóki go nie zwolnisz.";
        else if (engine.HasPendingPermission) Status = "Krok czeka na Twoją zgodę w Centrum → Akcje.";
    });

    [RelayCommand]
    private async Task RunAsync(JarvisAction? action)
    {
        if (action == null) return;
        if (busy) { Report = "Jedno polecenie na raz — poczekaj na koniec bieżącego albo użyj STOP."; return; }
        if (engine.IsStopped) { Report = "STOP awaryjny blokuje wykonania. Zwolnij go w Centrum, potem spróbuj ponownie."; return; }
        string? command = action.Resolve(Input, SelectedSection?.RequiredHint ?? "Wpisz wartość w polu obok.", out string? error);
        if (command == null) { Report = error ?? "Czego mam użyć?"; return; }
        busy = true; IsBusy = true;
        Report = "▶ " + command;
        Notes.Insert(0, "▶ " + command + " · " + DateTime.Now.ToString("HH:mm:ss"));
        while (Notes.Count > 8) Notes.RemoveAt(Notes.Count - 1);
        try
        {
            IntentResult result = await engine.ExecuteAsync(command, disposeCts.Token);
            string text = result.Text.Length == 0 ? "(pusta odpowiedź)" : result.Text;
            Report = text + Describe(result.Action);
        }
        catch (OperationCanceledException) { Report = "Przerwano. Nic nie zostało dokończone; wcześniejsze kroki NIE są cofane automatycznie."; }
        catch (Exception ex) { Report = "Nie udało się wykonać: " + ex.Message; }
        finally { busy = false; IsBusy = false; }
    }

    private static string Describe(ActionRecord? record) => record == null ? "" :
        "\n\n———\nStatus: " + record.Status + (record.Evidence.Length > 0 ? "\nDowód: " + Trim(record.Evidence) : "") + "\nId: " + record.ActionId;

    private static string Trim(string text)
    {
        string single = new StringBuilder(text).Replace("\n", " · ").ToString();
        return single.Length <= 400 ? single : single[..400] + "…";
    }

    public void Dispose() { engine.Changed -= SyncStatus; disposeCts.Cancel(); disposeCts.Dispose(); }
}
