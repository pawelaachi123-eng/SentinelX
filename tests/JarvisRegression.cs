using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.Jarvis;

namespace SentinelX.Tests;

/// <summary>
/// 0.96 · regresja warstwy JARVIS. Testy są celowo przyziemne: rozpoznawanie poleceń (nic nie może
/// podkraść istniejącego polecenia), granice odmów (brak zgody, brak urządzenia, brak modelu),
/// zapis danych z odczytem zwrotnym i odkładaniem uszkodzonego pliku oraz parsowanie odpowiedzi
/// Open-Meteo/OpenAI-shaped na wstrzykniętym transporcie. CI nie ma sieci ani Ollamy — nic tu ich nie potrzebuje.
/// </summary>
internal static class JarvisRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string Local(string name) => Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N"));

        // ---------------- multimedia: rozpoznawanie i granica „stop” ----------------
        Check(MediaCommands.Match("pauza", out string pauseLabel) == MediaKey.PlayPause, "„pauza” steruje odtwarzaczem");
        Check(pauseLabel.Length > 0 && pauseLabel.ToLowerInvariant().Contains("pauza"), "etykieta pauzy jest polska");
        Check(MediaCommands.Match("nastepny utwor", out _) == MediaKey.Next && MediaCommands.Match("poprzedni utwor", out _) == MediaKey.Previous,
            "nawigacja utworów działa");
        Check(MediaCommands.Match("stop odtwarzanie", out _) == MediaKey.Stop, "„stop odtwarzanie” to zatrzymanie odtwarzacza");
        Check(MediaCommands.Match("stop", out _) == null, "samo „stop” NIE wolno mapować na odtwarzacz — to STOP awaryjny");
        Check(MediaCommands.Match("wylacz komputer", out _) == null, "wyłączenie komputera nie jest poleceniem multimedialnym");
        Check(MediaService.DetectPlayer(["spotify.exe", "chrome.exe"]) != null, "Spotify jest rozpoznany jako odtwarzacz");
        Check(MediaService.DetectPlayer(["notepad.exe"]) == null, "Notatnik to nie odtwarzacz");
        var volume = new MediaService(key => { });
        MediaOutcome mute = volume.MutePlayer();
        Check(mute.Success && mute.Evidence.Contains("SendInput", StringComparison.OrdinalIgnoreCase), "wyciszenie zgłasza, że klawisz został wysłany, a nie że muzyka ucichła");

        // ---------------- rozpoznawanie pogody: argument z originalu ----------------
        Check(JarvisRouter.TryWeatherCity("pogoda", "pogoda", out string emptyCity) && emptyCity.Length == 0, "samo „pogoda” bierze miasto z ustawień");
        Check(JarvisRouter.TryWeatherCity("Pogoda Lwów", "pogoda lwow", out string lwow) && lwow == "Lwów", "nazwa miasta zachowuje polskie znaki");
        Check(JarvisRouter.TryWeatherCity("sprawdź pogodę w Krakowie", "sprawdz pogodę w krakowie", out string krakow) && krakow.StartsWith("Krakow", StringComparison.Ordinal),
            "forma „w + miejscownik” jest obsłużona");
        Check(!JarvisRouter.TryWeatherCity("ile mam ramu", "ile mam ramu", out _), "odczyty RAM nie są pogodą");
        Check(WeatherService.NameCandidates("Krakowie").Contains("Krakow", StringComparer.Ordinal), "końcówka przypadka jest odcinana jako trzecia próba");
        Check(WeatherService.Describe(95) == "burza" && WeatherService.IconFor(61) == "🌦", "kody WMO mają polski opis");
        Check(WeatherService.ParseGeocoding("""{"results":[]}""", "X") == null, "pusty wynik geokodowania nie jest miastem");

        // ---------------- głośność: tylko pełne procenty, bez zgadywania ----------------
        Check(JarvisRouter.TryVolume("glosniej", out int up) && up == 5, "domyślny skok głośności to 5 procent");
        Check(JarvisRouter.TryVolume("ciszej 12", out int down) && down == -12, "podana wartość obniża głośność");
        Check(!JarvisRouter.TryVolume("ciszej 80", out _), "skok powyżej 50% jest odrzucany zamiast być cichym żartem");

        // ---------------- dom: nie podkradamy launchera aplikacji ----------------
        Check(JarvisRouter.TryHomeCommand("dom: włącz światło salon", "dom: wlacz swiatlo salon", out string lamp, out bool on) && on && lamp.Contains("salon", StringComparison.OrdinalIgnoreCase),
            "przedrostek „dom:” włącza urządzenie");
        Check(!JarvisRouter.TryHomeCommand("włącz spotify", "wlacz spotify", out _, out _), "uruchamianie aplikacji zostaje przy launcherze");
        // Bez przedrostka „dom:” przechodzą wyłącznie rzeczowniki urządzeń domowych — i to jest granica,
        // której pilnujemy: „wyłącz światła” = dom, „wyłącz spotify” = jednak nie nasza sprawa.
        Check(JarvisRouter.TryHomeCommand("wyłącz światła", "wylacz swiatla", out string lights, out bool off) && !off && lights == "światła",
            "“wyłącz światła” jest domem, bo to nazwa urządzenia");
        Check(!JarvisRouter.TryHomeCommand("wyłącz spotify", "wylacz spotify", out _, out _), "“wyłącz spotify” nadal nie jest domem");
        Check(HomeAutomationService.AllowedServices.All(x => x.EndsWith("_on", StringComparison.Ordinal) || x.EndsWith("_off", StringComparison.Ordinal)),
            "biała lista dopuszcza tylko włączanie i wyłączanie");
        Check(!HomeAutomationService.AllowedServices.Contains("homeassistant.restart", StringComparer.Ordinal), "restart Home Assistanta jest poza listą");
        Check(HomeAutomationService.ServiceFor(new HomeEntity("light.salon", "Salon", "light", "on"), on: false, out string service, out _) && service == "light.turn_off",
            "serwis dobierany po domenie encji");
        Check(!HomeAutomationService.ServiceFor(new HomeEntity("climate.gosc", "Gosc", "climate", "on"), true, out _, out string reason) && reason.Length > 0,
            "domeny wymagające parametrów (klimat) są odmawiane z powodem");
        Check(HomeAutomationService.ServiceUrl("http://192.168.1.9:8123/", "light.turn_on") == "http://192.168.1.9:8123/api/services/light/turn_on", "adres serwisu bez podwójnego ukośnika");
        var states = JsonDocument.Parse("""{"results":[{"entity_id":"light.salon","attributes":{"friendly_name":"Światło salon"},"state":"on"}]}""").RootElement;
        var entities = HomeAutomationService.ParseStates(states);
        Check(entities.Count == 1 && entities[0].FriendlyName == "Światło salon" && entities[0].IsOn, "stan encji jest czytany z atrybutów");
        Check(HomeAutomationService.Find(entities, "światło salon").Count == 1 && HomeAutomationService.Find(entities, "nic takiego").Count == 0,
            "wyszukiwanie encji trafia i nie trafia uczciwie");

        // ---------------- sekwencje: zapis, podgląd, stop na błędzie ----------------
        Check(RoutineService.SplitSteps("który jest dzień; policz 2+2\nplan dnia").Count == 3, "średnik i nowy wiersz dzielą kroki");
        Check(RoutineService.SplitSteps("policz 2+2")[0] == "policz 2+2", "działanie nie jest dzielone po plusie");
        Check(RoutineService.RefuseIfDangerous("usuń pliki z pulpitu") != null, "krok niszczący ma powód odmowy");
        Check(RoutineService.RefuseIfDangerous("potwierdź") != null, "sekwencja nie może udawać zgody");
        Check(RoutineService.RefuseIfDangerous("plan dnia") == null, "zwyczajny odczyt jest dozwolony");

        var routines = new RoutineService(Local("memory"));
        var created = routines.Create("poranek", "który jest dzień; plan dnia; ile mam ramu");
        Check(created.Success && created.Message.Contains("3 kroków"), "utworzenie sekwencji podaje liczbę kroków");
        Check(routines.Find("PORANEK") != null, "znajdowanie nazwy ignoruje wielkość liter");
        Routine? poranek = routines.Find("poranek")!;
        Check(RoutineService.Preview(poranek).Contains("Żadnego kroku nie wykonałem"), "podgląd deklaruje, że nic nie wykonał");
        var duplicate = routines.Create("poranek", "data");
        Check(!duplicate.Success && duplicate.Message.Contains("już istnieje"), "drugi raz z tą samą nazwą się nie zapisuje");
        var dangerous = routines.Create("wieczor", "zamknij wszystkie aplikacje");
        Check(!dangerous.Success, "sekwencja z krokiem niszczącym nie powstaje");

        var runner = new StepRunner();
        int sent = 0;
        runner.Attach(async (command, token) =>
        {
            await Task.Yield();
            sent++;
            return command.Contains("plan dnia", StringComparison.Ordinal)
                ? new StepOutcome(false, false, "Brak otwartych zadań.", "SX-TEST", "FAILED")
                : new StepOutcome(true, true, "ok", "SX-TEST", "VERIFIED");
        });
        var run = await routines.RunAsync(poranek, runner, null, default);
        Check(!run.Success && run.Message.Contains("NIE zostało wysłane"), "błędny krok zatrzymuje resztę sekwencji");
        Check(sent == 2, $"krok po błędzie nie jest wysyłany (wysłane: {sent})");
        Check(routines.All().Single().LastRunSummary.Length > 0, "uruchomienie zostaje zapisane w liście");
        var detached = new StepRunner();
        var stopped = await routines.RunAsync(poranek, detached, null, default);
        Check(!stopped.Success && stopped.Message.Contains("nie jest podłączona"), "bez podłączonej kolejki sekwencja odmawia zamiast udawać wykonanie");

        string store = routines.StorePath;
        File.WriteAllText(store, "{ to nie jest json");
        var reloaded = new RoutineService(Path.GetDirectoryName(store)!);
        Check(reloaded.All().Count == 0 && reloaded.LastStorageError!.Contains("odczytać"), "uszkodzona lista nie jest cicho zastępowana pustą");
        Check(Directory.GetFiles(Path.GetDirectoryName(store)!, "routines.json.corrupt-*").Length == 1, "uszkodzony plik zostaje odłożony, a nie nadpisany");
        Check(routines.Delete("poranek").Success && routines.All().Count == 0, "usunięcie działa po nazwie");

        // ---------------- agent: katalog narzędzi tylko-do-odczytu ----------------
        Check(AgentToolCatalog.Tools.Count >= 20, "katalog ma pełne 20 narzędzi");
        foreach (AgentTool tool in AgentToolCatalog.Tools)
        {
            string head = tool.Command.Split('{')[0].Trim();
            Check(IntentCatalog.Phrases.Contains(ConversationMemoryService.Normalize(head)),
                "narzędzie „" + tool.Name + "” odwołuje się do polecenia, które routing rozpoznaje („" + head + "”)");
        }
        string joined = string.Join(" ", AgentToolCatalog.Tools.Select(x => x.Command + " " + x.Description).ToArray());
        foreach (string forbidden in new[] { "usuń", "skasuj", "zamknij", "formatuj", "wyłącz komputer", "potwierdź" })
            Check(!joined.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "żadne narzędzie nie udostępnia „" + forbidden + "”");
        Check(AgentToolCatalog.Find("ram")!.BuildCommand("") == "ile mam ramu", "narzędzie bez argumentu buduje gotowe polecenie");
        Check(AgentToolCatalog.Find("search")!.BuildCommand("przeprowadzka").Contains("szukaj wszystkiego: przeprowadzka"), "argument trafia do polecenia");
        Check(!AgentToolCatalog.Find("search")!.BuildCommand("").Contains("…"), "pusty argument nie wstawia cudzysłowu-łańcucha");
        Check(AgentToolCatalog.Describe().Contains("NIE ma", StringComparison.Ordinal), "opis katalogu mówi wprost, czego agent nie może");
        string body = AgentService.BuildBody("qwen3:4b", [], AgentToolCatalog.Tools, null);
        Check(body.Contains("\"tools\"") && body.Contains("\"stream\":false") && body.Contains("\"type\":\"function\""),
            "żądanie ma narzędzia w kształcie OpenAI i odpowiedź bez strumienia");
        var turn = AgentService.ParseTurnForTest("""{"message":{"role":"assistant","content":"","tool_calls":[{"id":"c1","function":{"name":"ram","arguments":"{}"}}]},"done":true}""");
        Check(turn.ToolCalls.Count == 1 && turn.ToolCalls[0].Name == "ram", "wywołanie narzędzia jest parsowane z odpowiedzi modelu");
        Check(AgentService.ParseTurnForTest("""{"message":{"content":"Dysk ma 20% wolnego."},"done":true}""").ToolCalls.Count == 0,
            "zwykła odpowiedź nie ma narzędzi");
        Check(new AgentToolCall("c1", "weather", """{"argument":"Kraków"}""").Argument == "Kraków", "argument JSON jest wyciągany z zachowaniem polskich znaków");
        Check(new AgentToolCall("c2", "calculate", "\"19.99*3\"").Argument.Contains("19.99"), "model oddający argument jako string nie jest błędem");

        // ---------------- wizja: tylko modele z obrazem, obraz bez dysku ----------------
        Check(VisionService.IsVisionModel("llava:13b") && VisionService.IsVisionModel("qwen2.5vl:7b"), "modele wizyjne są rozpoznawane po nazwie");
        Check(!VisionService.IsVisionModel("qwen3:4b"), "model tekstowy nie jest brany za wizyjny");
        Check(VisionService.SelectVisionModel(["qwen3:4b", "llava:13b"], "") == "llava:13b", "wybór modelu omija tekstowe");
        Check(VisionService.SelectVisionModel(["qwen3:4b"], "") == null, "bez modelu wizyjnego nie ma co wybierać");
        string visionBody = VisionService.BuildBody("llava:13b", "Opisz ekran", "AAEC");
        Check(visionBody.Contains("\"images\":[\"AAEC\"]"), "obraz leci w polu images, nie w treści");
        Check(VisionService.ReadAnswer("""{"message":{"content":"Edytor tekstu."}}""") == "Edytor tekstu.", "odpowiedź wizyjna jest wyciągana");
        Check(VisionService.Suggestions.Any(x => x.Contains("ollama pull", StringComparison.Ordinal)), "odmowa podaje komendę pobrania modelu");

        // ---------------- pamięć semantyczna: liczenie i magazyn ----------------
        Check(SemanticMemoryIndex.ParseEmbedding("""{"embedding":[1,2,3]}""")!.Length == 3, "ksztalt {embedding:[…]} jest czytany");
        Check(SemanticMemoryIndex.ParseEmbedding("""{"embeddings":[[1,2]]}""")!.Length == 2, "ksztalt {embeddings:[[…]]} jest czytany");
        Check(SemanticMemoryIndex.ParseEmbedding("bzdura") == null, "odpowiedź bez wektora nie jest wektorem");
        Check(SemanticMemoryIndex.Cosine([1, 0], [1, 0]) > 0.999 && Math.Abs(SemanticMemoryIndex.Cosine([1, 0], [0, 1])) < 1e-9, "cosinus liczy się poprawnie");
        Check(SemanticMemoryIndex.Cosine([1, 2], [1]) == 0, "niezgodne wymiary dają zero, nie NaN");
        Check(SemanticMemoryIndex.Cosine([double.NaN, 1], [1, 1]) == 0, "NaN nie przechodzi przez Similarity");
        Check(SemanticMemoryIndex.EmbeddingsBody("m", "txt").Contains("\"prompt\":\"txt\"") && SemanticMemoryIndex.EmbedBody("m", "txt").Contains("\"input\":[\"txt\"]"),
            "oba końce Ollamy mają własne ciało żądania");
        var indexItems = SemanticMemoryIndex.ReadIndex(JsonDocument.Parse(
            """{"schemaVersion":1,"model":"nomic-embed-text","items":[{"id":"n1","kind":"wspomnienie","text":"Kasia lubi góry","vector":[1,2],"indexedAt":"2026-01-01T00:00:00+01:00"}]}""").RootElement);
        Check(indexItems.Count == 1 && indexItems[0].Text.Contains("góry"), "indeks przeżywa zapis/odczyt z polskimi znakami");
        Check(SemanticMemoryIndex.ReadIndex(JsonDocument.Parse("{}").RootElement).Count == 0, "plik bez pozycji to pusty indeks, nie błąd");

        // ---------------- ustawienia: granice i adres Home Assistanta ----------------
        var config = new JarvisSettings { WeatherCacheMinutes = 0, AgentMaxSteps = 99, HomeBaseUrl = "http://admin:[email protected]/api?x=1", EmbeddingModel = "zly model!" };
        JarvisSettingsGuard.Validate(config);
        Check(config.WeatherCacheMinutes >= 5 && config.AgentMaxSteps <= 8, "wartości spoza zakresu są sprowadzane do zakresu");
        Check(config.HomeBaseUrl.Length == 0, "adres z logowaniem i ścieżką jest odrzucany (pozostaje „nie skonfigurowano”)");
        Check(config.EmbeddingModel.Length == 0, "nazwa modelu, której Ollama nie przyjmie, nie jest zapisywana");
        Check(JarvisSettingsGuard.NormalizeUrl("http://192.168.1.9:8123") == "http://192.168.1.9:8123", "poprawny adres lokalny przechodzi");

        // ---------------- router: nic nie podkrada istniejących poleceń ----------------
        var jarvisConfig = new JarvisSettings { WeatherEnabled = false, HomeEnabled = false, AgentEnabled = false, SemanticSearchEnabled = false };
        var ollama = new LocalAiService(new GamingModeService(), new Handler((request, token) => Task.FromResult(Json(new { models = new[] { new { name = "qwen3:4b" } } }))),
            Local("ai-settings"));
        var weather = new WeatherService(() => jarvisConfig, new Handler((request, token) => Task.FromResult(Json(new { }))), Local("weather-cache"));
        var router = new JarvisRouter(weather, new MediaService(key => { }), new HomeAutomationService(() => jarvisConfig),
            new AgentService(new StepRunner(), ollama, () => jarvisConfig), new VisionService(ollama, () => jarvisConfig),
            new SemanticMemoryIndex(ollama, new ConversationMemoryService(Local("memory")), () => jarvisConfig), new RoutineService(Local("memory")), new StepRunner(), () => jarvisConfig);
        foreach (string notOurs in new[] { "ile mam ramu", "top procesy", "diagnostyka komputera", "włącz spotify", "plan dnia", "zadania", "pauza na 5 minut" })
            Check(await router.ProcessAsync(notOurs, ConversationMemoryService.Normalize(notOurs), default) == null,
                "„" + notOurs + "” nie zostaje przejęte przez warstwę JARVIS");
        string off = await router.ProcessAsync("pogoda", "pogoda", default) ?? "";
        Check(off.Contains("wyłączona", StringComparison.OrdinalIgnoreCase) && off.Contains("Ustawienia", StringComparison.Ordinal),
            "wyłączona pogoda odmawia i mówi, gdzie ją włączyć (bez udawanej temperatury)");
        string overview = await router.ProcessAsync("jarvis", "jarvis", default) ?? "";
        Check(overview.Contains("Sekwencje") && overview.Contains("wymaga internetu"), "opis warstwy wymienia każdą rodzinę i jej warunki");
        Check(JarvisRouter.LooksLikeHomeDevice("swiatlo nad stolikiem") && !JarvisRouter.LooksLikeHomeDevice("spotify"),
            "bez przedrostka „dom” przechodzą tylko nazwy urządzeń domowych");

        // ---------------- tło okna: błąd DWM nie może wywalić aplikacji ----------------
        Check(SentinelX.Utilities.WindowEffects.Apply(null!, "Mica", true).Contains("Brak okna"), "brak uchwytu = notatka, a nie wyjątek");

        // ---------------- pogoda na wstrzykniętym transporcie: cache i brak sieci ----------------
        int requests = 0;
        var online = new Handler(async (request, token) =>
        {
            Interlocked.Increment(ref requests);
            await Task.Yield();
            return request.RequestUri!.AbsolutePath.Contains("geocoding") || request.RequestUri!.AbsolutePath.Contains("/v1/search")
                ? Json(new { results = new[] { new { name = "Kraków", latitude = 50.06, longitude = 19.94 } } })
                : Json(new
                {
                    current = new { temperature_2m = 21.4, weather_code = 2, wind_speed_10m = 7.2, relative_humidity_2m = 61 },
                    daily = new
                    {
                        time = new[] { "2026-09-26" }, temperature_2m_max = new[] { 24.0 }, temperature_2m_min = new[] { 12.5 },
                        precipitation_probability_max = new[] { 40 },
                    },
                });
        });
        var live = new WeatherService(() => new JarvisSettings { WeatherEnabled = true, DefaultCity = "Kraków", WeatherCacheMinutes = 30 }, online, Local("cache"));
        var first = await live.GetAsync(null, default);
        Check(first.Success && first.Message.Contains("21,4") && first.Message.Contains("Kraków"), "prognoza ma temperaturę z przecinkiem dziesiętnym i miasto");
        Check(requests == 2, $"raz geokodowanie i raz prognoza (jest {requests})");
        var second = await live.GetAsync(null, default);
        Check(second.Success && second.Evidence.Contains("świeży") && requests == 2, "drugi raz czyta cache bez łączenia się z siecią");
        var offline = new WeatherService(() => new JarvisSettings { WeatherEnabled = true, DefaultCity = "Kraków", WeatherCacheMinutes = 0 },
            new Handler((request, token) => Task.FromException<HttpResponseMessage>(new HttpRequestException("brak kabla"))), Path.GetDirectoryName(live.CachePath("Kraków"))!);
        var stale = await offline.GetAsync(null, default);
        Check(stale.Success && stale.Reading!.FromCache && stale.Message.Contains("ZAPISANY"), "przy braku sieci wraca zapis z jasnym oznaczeniem, że jest z pliku");
        var noCache = new WeatherService(() => new JarvisSettings { WeatherEnabled = true, DefaultCity = "Nowe Miasto Nigdy" },
            new Handler((request, token) => Task.FromException<HttpResponseMessage>(new HttpRequestException("brak kabla"))), Local("cache-empty"));
        var honest = await noCache.GetAsync(null, default);
        Check(!honest.Success && honest.Message.ToLowerInvariant().Contains("brak"), "bez sieci i bez zapisu: odmowa, nie wymyślona temperatura");
        var timeout = new WeatherService(() => new JarvisSettings { WeatherEnabled = true, DefaultCity = "Kraków" },
            new Handler(async (request, token) => { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("nigdy"); }), Local("cache-hang"));
        var hung = await timeout.GetAsync("Kraków", CancellationTokenSource.CreateLinkedTokenSource(default).Token);
        Check(!hung.Success || hung.Message.Length > 0, "zawieszony serwer nie wiesza polecenia");
    }
}
