using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentinelX.Core;
using SentinelX.Services.Intent;

namespace SentinelX;

public sealed class CommandRouter
{
    private readonly SystemMonitor systemMonitor;
    private readonly SystemInfoService systemInfo;
    private readonly LocalAiService localAi;
    private readonly ConversationMemoryService memory;
    private readonly ProjectService? projects;
    private readonly TaskService? tasks;
    private readonly DiagnosticSnapshotService? snapshots;
    private readonly MemoryArchiveService? archives;
    private readonly WorkspaceInsightsService? insights;
    private readonly UnderstandingJournal? journal;
    private readonly SessionFactBook? facts;
    private readonly Core.LearnedPatterns? learned;
    /// <summary>0.96 · warstwa JARVIS (pogoda, multimedia, dom, agent, wizja, indeks, sekwencje).
    /// Opcjonalna celowo: bez niej klasyczne polecenia działają identycznie, a testy i ścieżka
    /// --self-test nie potrzebują nowych serwisów. Nie jest wstrzykiwana do ActionEngine, więc
    /// nie tworzy cyklu router → silnik → router.</summary>
    private readonly Services.Jarvis.JarvisRouter? jarvis;
    private string lastTopic = "";
    private DateTime lastTopicTime;
    private (string Text, DateTime When, string Description, DateTime Expires)? pendingReminder;

    /// <summary>Common words ignored by the extractive conversation summary („podsumuj rozmowę”).</summary>
    private static readonly HashSet<string> SummaryStopWords = new(StringComparer.Ordinal)
    {
        "ktory", "ktora", "ktore", "jakie", "jakis", "jakas", "przez", "wtedy", "pomoc", "pomocy",
        "prosze", "dziekuje", "moglbys", "mozesz", "chcialbym", "znowu", "zawsze", "nigdy", "moze",
        "tylko", "wlasnie", "dlaczego", "wiec", "potem", "teraz", "dobrze", "super", "ok", "tak",
        "siebie", "zrobic", "zrobilem", "powiedzial", "powiedzialas", "powiedz", "wiesz", "nasz",
    };

    /// <summary>Raised with the best candidate when an input is about to fall through to the AI model
    /// but is close to a known command — the host turns it into a question instead of a silent guess.</summary>
    public event Action<string>? SuggestionPending;

    public CommandRouter(SystemMonitor systemMonitor, SystemInfoService systemInfo, LocalAiService localAi, ConversationMemoryService memory,
        ProjectService? projects = null, TaskService? tasks = null, DiagnosticSnapshotService? snapshots = null,
        MemoryArchiveService? archives = null, WorkspaceInsightsService? insights = null, UnderstandingJournal? journal = null,
        SessionFactBook? facts = null, Core.LearnedPatterns? learned = null, Services.Jarvis.JarvisRouter? jarvis = null)
    {
        this.systemMonitor = systemMonitor; this.systemInfo = systemInfo; this.localAi = localAi; this.memory = memory;
        this.projects = projects; this.tasks = tasks; this.snapshots = snapshots; this.archives = archives; this.insights = insights;
        this.journal = journal; this.facts = facts; this.learned = learned; this.jarvis = jarvis;
    }

    public async Task<string> ProcessAsync(string command, CancellationToken cancellationToken = default, Action<string>? onDelta = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command)) return "";
        string text = Normalize(command).TrimEnd('?', '!', '.', ' ');
        string? snapshotResponse = await TryHandleSnapshotCommandAsync(command.Trim(), text, cancellationToken);
        if (snapshotResponse != null) return snapshotResponse;
        string? utilityResponse = UtilityToolbox.Process(command.Trim(), text);
        if (utilityResponse != null) return utilityResponse;
        string? metaResponse = TryHandleMetaCommand(text);
        if (metaResponse != null) return metaResponse;
        string? workspaceResponse = TryHandleWorkspaceCommand(command.Trim(), text);
        if (workspaceResponse != null) return workspaceResponse;
        string? memoryResponse = TryHandleMemoryCommand(command.Trim(), text);
        if (memoryResponse != null) return memoryResponse;
        string? projectResponse = TryHandleProjectCommand(command.Trim(), text);
        if (projectResponse != null) return projectResponse;
        string? taskResponse = TryHandleTaskCommand(command.Trim(), text);
        if (taskResponse != null) return taskResponse;
        if (text is "modele ai" or "lista modeli" or "status ai" or "test ai" or "sprawdz ai")
            return await localAi.GetStatusAsync(cancellationToken);
        Match model = Regex.Match(command.Trim(), @"^(?:ustaw\s+)?model\s+ai\s+(.+)$", RegexOptions.IgnoreCase);
        if (model.Success)
        {
            try { return await localAi.SetPreferredModelAsync(model.Groups[1].Value.Trim(), cancellationToken); }
            catch (System.Net.Http.HttpRequestException) { return "Nie udało się odczytać modeli. Uruchom lokalną Ollama."; }
        }
        if (text is "anuluj" or "przerwij" or "przerwij odpowiedz")
        { localAi.CancelCurrentRequest(); return "Przerwano."; }
        if (text is "ktora godzina" or "jaka jest godzina" or "godzina") return DateTime.Now.ToString("HH:mm");
        if (text is "jaka dzis data" or "jaka jest data" or "dzisiejsza data") return DateTime.Now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL"));

        // Follow-up state expires and only applies to an entire short follow-up, never another topic's question.
        if (DateTime.Now - lastTopicTime > TimeSpan.FromMinutes(5)) lastTopic = "";
        string followUp = Regex.Replace(text, @"^(?:a |to |a teraz )", "");
        if (lastTopic is "RAM" or "CPU")
        {
            if (followUp is "ile uzywam" or "ile jest uzywane" or "ile zajete")
                return lastTopic == "RAM" ? Number(systemMonitor.GetUsedRamGB(), "GB", 1) : Number(systemMonitor.GetCpuUsage(), "%");
            if (followUp is "jaki procent" or "ile procent" or "procent")
                return Number(lastTopic == "RAM" ? systemMonitor.GetRamUsagePercent() : systemMonitor.GetCpuUsage(), "%");
        }

        if (text is "ram" or "pokaz ram" or "status ram")
        { return $"{Number(systemMonitor.GetUsedRamGB(), "GB", 1)} z {Number(systemMonitor.GetTotalRamGB(), "GB", 1)} ({Number(systemMonitor.GetRamUsagePercent(), "%")})."; }
        if (Regex.IsMatch(text, @"^(?:(?:pokaz|podaj|wyswietl) )?(?:cpu (?:i )?ram|ram (?:i )?cpu|uzycie cpu i ram)$"))
        { return $"CPU: {Number(systemMonitor.GetCpuUsage(), "%")}. RAM: {Number(systemMonitor.GetUsedRamGB(), "GB", 1)} z {Number(systemMonitor.GetTotalRamGB(), "GB", 1)}."; }
        if (Regex.IsMatch(text, @"^(?:(?:pokaz|podaj|wyswietl) )?(?:cpu|uzycie cpu|zuzycie cpu|ile cpu|ile procent cpu|jakie jest uzycie cpu|uzycie procesora|ile uzywam cpu)$"))
        { return Number(systemMonitor.GetCpuUsage(), "%"); }
        if (Regex.IsMatch(text, @"^(?:ile mam (?:pamieci )?ramu?|calkowity ram|ile jest ramu|ile mam pamieci operacyjnej)$"))
        { return Number(systemMonitor.GetTotalRamGB(), "GB", 1); }
        if (Regex.IsMatch(text, @"^(?:(?:pokaz|podaj) )?(?:procent ram|jaki procent ram|ile procent ram|uzycie ram procent|zuzycie ram w procentach|uzycie ram w procentach)$"))
        { return Number(systemMonitor.GetRamUsagePercent(), "%"); }
        if (Regex.IsMatch(text, @"^(?:ile uzywam (?:pamieci )?ramu?|uzycie ramu?|zuzycie ramu?|ile ramu? jest uzywane|ile pamieci ram uzywam)$"))
        { return Number(systemMonitor.GetUsedRamGB(), "GB", 1); }
        if (text is "jaki mam procesor" or "jaki procesor" or "nazwa cpu") { return systemInfo.GetCpuName(); }
        if (text is "jaki windows" or "wersja windows" or "jaka jest wersja windows") { return systemInfo.GetWindowsVersion(); }
        if (text is "uptime" or "jak dlugo dziala komputer" or "ile dziala komputer") { return systemInfo.GetUptime(); }
        if (text is "pokaz dyski" or "miejsce na dysku" or "dyski") { return systemInfo.GetDiskInfo(); }

        string topicForContext = lastTopic;
        bool includeRecentHistory = IsFollowUpQuestion(text, followUp, topicForContext);
        bool includeSystemFacts = string.IsNullOrEmpty(topicForContext) ? MentionsComputerStateNoTopic(text, followUp) : topicForContext is "RAM" or "CPU" or "SYSTEM" or "DISK";
        if (!Regex.IsMatch(followUp, @"^(?:czy to|czy jest|a |dlaczego|czemu|co z tym)")) lastTopic = "";

        // 0.96 · warstwa JARVIS: ostatnia warstwa deterministyczna, zaraz przed pytaniem o domysł.
        // Świadomie TAK PÓŹNO: żadne istniejące polecenie (launchery aplikacji, pliki, zadania) nie może
        // zostać przejęte przez „włącz światło”, a i tak wcześniej niż model — odpowiedź modelu nigdy
        // nie wykonuje polecenia, więc nowe funkcje nie mogą mu wchodzić w drogę.
        if (jarvis != null)
        {
            string? jarvisResponse = await jarvis.ProcessAsync(command.Trim(), text, cancellationToken, onDelta);
            if (jarvisResponse != null) return jarvisResponse;
        }

        // 0.91: this is the single honest "I would otherwise guess" point — every deterministic handler
        // above already declined, so a close-but-not-quite known command becomes a question, not a guess.
        var suggestions = CommandUnderstanding.Suggest(text);
        if (suggestions.Count > 0)
        {
            SuggestionPending?.Invoke(suggestions[0]);
            return "Nie jestem pewien, o co chodzi. Czy chodziło Ci o:\n" + string.Join("\n", suggestions.Select(x => "· „" + x + "”")) +
                "\nOdpisz „tak”, aby wykonać pierwszą opcję, albo napisz polecenie dokładniej. Niczego nie wykonałem.";
        }
        return await localAi.AskAsync(command, BuildSystemContext(topicForContext, includeSystemFacts, includeRecentHistory), cancellationToken, onDelta);
    }

    /// <summary>0.91 · CENTRUM: Sentinel talking honestly about itself — version, changelog, lessons,
    /// self-check, suggestions, capability boundaries. All read-only; refusals are intentional design,
    /// not missing features.</summary>
    private string? TryHandleMetaCommand(string text)
    {
        if (text is "wersja" or "jaka wersja" or "wersja sentinel" or "wersja aplikacji")
            return "Sentinel X " + AppConstants.Version + " · " + systemInfo.GetWindowsVersion() + " · .NET " + Environment.Version;
        if (text is "zrozum" or "jak mnie rozumiesz")
            return "Pokażę, jak rozumiem zdanie — bez wykonania.\nWpisz: zrozum: <Twoje zdanie>, np. „zrozum: sprawdź proszę ile mam ramu”.";
        if (text is "co wiesz o mnie" or "co o mnie wiesz" or "kim jestem dla ciebie")
        {
            string name = memory.UserName;
            var lines = new List<string>
            {
                "CO WIEM O TOBIE — wyłącznie z lokalnej pamięci, nic nie wysyłam.",
                name.Length > 0 ? "· imię: " + name : "· imię: nie zapisane (powiedz „mam na imię …”)",
                "· trwałe wspomnienia: " + memory.NoteCount + " (komenda „co pamiętasz” pokazuje wszystkie)",
                "· zapisane rozmowy: " + memory.GetConversations().Count + " (komenda „pokaż rozmowy”)",
            };
            string projectLine = projects?.ActiveProject is { } active ? "· aktywny projekt: " + active.Name : "· aktywny projekt: brak (pamięć globalna)";
            lines.Add(projectLine);
            if (memory.NoteCount > 0)
            {
                var recent = memory.GetNotes().Take(3).ToArray();
                lines.Add("· ostatnie wspomnienia:");
                foreach (var note in recent) lines.Add("  – " + Truncate(note.Text, 100));
            }
            lines.Add("Wspomnienia możesz zmienić w panelu Pamięć — edycja i usuwanie są tam jawne.");
            return StorageResult(string.Join("\n", lines));
        }
        if (text is "podsumuj rozmowe" or "podsumowanie rozmowy" or "o czym rozmawialismy")
        {
            var turns = memory.GetAllEntries().Where(x => x.SessionId == memory.ActiveSessionId).OrderBy(x => x.Timestamp).ToArray();
            if (turns.Length == 0) return "Ta rozmowa nie ma jeszcze zapisanych wypowiedzi — nie ma czego podsumować.";
            var userTurns = turns.Where(x => x.Role == "user").ToArray();
            var topWords = turns
                .SelectMany(x => ConversationMemoryService.Normalize(x.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length > 3 && !SummaryStopWords.Contains(w))
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(6).Select(g => g.Key).ToArray();
            var lines = new List<string>
            {
                "PODSUMOWANIE ROZMOWY — wyciąg z zapisanych wypowiedzi, bez zmyślania.",
                "· rozmowa: " + memory.ActiveConversationTitle,
                "· wypowiedzi: " + turns.Length + " (w tym Twoich: " + userTurns.Length + "), od " + turns[0].Timestamp.ToString("dd.MM HH:mm") + " do " + turns[^1].Timestamp.ToString("dd.MM HH:mm"),
                "· zaczęło się od: „" + Truncate(userTurns.Length > 0 ? userTurns[0].Text : turns[0].Text, 120) + "”",
            };
            if (topWords.Length > 0) lines.Add("· najczęściej powtarzane słowa: " + string.Join(", ", topWords));
            lines.Add("· ostatnia Twoja wypowiedź: „" + Truncate(userTurns.Length > 0 ? userTurns[^1].Text : turns[^1].Text, 120) + "”");
            lines.Add("Pełna rozmowa: „eksportuj rozmowę markdown” albo zakładka rozmowy.");
            return string.Join("\n", lines);
        }
        if (text is "fakty" or "ostatnie fakty")
        {
            var snapshot = facts?.Snapshot() ?? [];
            return snapshot.Count == 0
                ? "Brak zapisanych odczytów z tej sesji. Fakty sesji powstają po odczytach typu „ile mam ramu” i żyją tylko do zamknięcia aplikacji."
                : "Fakty sesji (tylko w pamięci aplikacji, nie na dysku):\n" +
                  string.Join("\n", snapshot.Select(x => "· [" + x.Time + "] " + x.Label + ": " + x.Value));
        }
        if (text is "co nowego" or "lista zmian" or "changelog" or "co sie zmienilo")
            return "CO NOWEGO W 0.95 · JARVIS\n" +
                "· Timer, budzik i stoper: „timer 5 minut herbata”, „budzik 7:00”, „stoper start/stop” — powiadomienie ⏰ tym samym kanałem co przypomnienia.\n" +
                "· Głośność i dźwięk: „głośność”, „głośność 40”, „wycisz”, „przywróć dźwięk”. Zrzut ekranu: „zrzut ekranu” zapisuje PNG w folderze danych.\n" +
                "· Schowek: „kopiuj: tekst” i „co w schowku”.\n" +
                "· Self-repair: „napraw sie” naprawia uszkodzone pliki (przywracanie z kopii, odkładanie na bok) — dane, nigdy kod.\n" +
                "· Self-improve: uczę się z Twoich poprawek i akceptowanych propozycji — drugi raz to samo wejście rozumiem od razu. „ulepsz sie” pokazuje, czego się nauczyłem.\n" +
                "\nCO NOWEGO W 0.94 · ZROZUMIENIE, PAMIĘĆ I NARZĘDZIA\n" +
                "· Rozumiem zdania, nie tylko komendy: „sprawdź proszę ile mam ramu”, „mógłbyś powiedzieć która godzina” albo „odpal discorda” działają wprost — zawsze pokazuję „Zrozumiałem jako: …”. \n" +
                "· Polecenie ukryte w dłuższym zdaniu jest znajdowane bez zgadywania; zdania z „dziś”/„teraz” i niejasne trafiają do pytań „Czy chodziło Ci o…”, nigdy nie wykonują się po cichu.\n" +
                "· „zrozum: <zdanie>” — pokazuję kroki rozumienia i NIC nie wykonuję (test na sucho).\n" +
                "· Pamięć i kontekst: data, aktywny projekt i wątek rozmowy w kontekście AI, ostatnie odczyty sesji do dopytań („a ile wolnego?”), komendy „fakty”, „co wiesz o mnie”, „podsumuj rozmowę”.\n" +
                "· Wyszukiwanie wspomnień i rozmów toleruje literówki i odmianę („pamieci” → „pamiec”); nadal bez semantyki — tekstowo i uczciwie.\n" +
                "· Zadania: priorytet z tekstu („dodaj zadanie: pilne kupić mleko”) i komenda „zadanie N priorytet wysoki|niski|normalny”.\n" +
                "· ~30 nowych narzędzi offline: VAT 23/8/5/0%, znizka, napiwek, raty kredytu, odsetki, procent składany, logarytm, potęga, modulo, sin/cos/tan, średnia ważona, równanie kwadratowe, kalendarz miesiąca, dodaj/odejmij dni, dni między datami, rok przestępny, kwartał, EAN/ISBN/LUHN/REGON, literowanie, częstość słów, Scrabble, sortowanie, cytat, karta do losowania, nazwa użytkownika, rozdzielczość, bateria, strefa czasu i więcej miast świata.\n" +
                "\nCO NOWEGO W 0.93 · PORZĄDKI\n" +
                "· „usuń duplikaty: folder” — z każdej grupy identycznych plików zostawia 1, resztę po Twoim „potwierdz” przenosi do Kosza.\n" +
                "· „usuń puste pliki: folder” — pliki 0 B po Twoim „potwierdz” do Kosza.\n" +
                "· „zmien nazwy: folder zamien X na Y” — podgląd zmian nazw, wykonuje dopiero po „potwierdz”, nigdy nie nadpisuje.\n" +
                "· Publikacja EXE na GitHubie tylko dla wydań milowych albo na żądanie (bez automatycznego wydania przy każdym pushu).\n" +
                "\nCO NOWEGO W 0.92 · BEZPIECZNE PLIKI\n" +
                "· Bezpieczne pliki: „duplikaty: folder” znajduje identyczne treści (SHA-256), „porzadki: folder” pokazuje, co zajmuje miejsce — oba tylko do odczytu.\n" +
                "· „usuń do kosza: ścieżka” przenosi JEDEN plik do Kosza i dopiero po Twoim „potwierdz” — nic bez zgody.\n" +
                "· Głos: polecenie działa tylko, gdy w zdaniu pada „sentinel” (w dowolnym miejscu); bez niego Sentinel tylko nasłuchuje.\n" +
                "\nCO NOWEGO W 0.91 · CENTRUM\n" +
                "· Jedna zakładka CENTRUM zamiast wielu kart — rozmowa plus ikony: 📓 zadania, 🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka.\n" +
                "· Paleta // w polu wpisywania: wpisz „//”, a Tab wybiera polecenie.\n" +
                "· Głos domyślnie nasłuchuje od startu (możesz wyłączyć jednym kliknięciem).\n" +
                "· Gdy nie jestem pewien polecenia — pytam zamiast zgadywać.\n" +
                "· Nowe narzędzia offline: PESEL, NIP, IBAN, morse, binarnie, hex, wielkanoc, dni robocze, świat, lotto i inne — wpisz „pomoc”.\n" +
                "· „zrob zadanie: treść” dodaje zadanie wprost do zakładki 📓.\n" +
                "· „lekcje” pokazuje, czego nauczyłem się z Twoich poprawek; „samokontrola” sprawdza moje pliki; „propozycje” podpowiada porządki — nic bez Twojej zgody.";
        if (text is "lekcje" or "czego sie nauczyles" or "pokaz lekcje" or "uczenie")
            return journal?.Report() ?? "Dziennik lekcji nie jest dostępny w tym trybie.";
        if (text is "samokontrola" or "sprawdz sie" or "sprawdz sentinel" or "test sentinel")
            return insights?.SelfCheck() ?? "Samokontrola nie jest dostępna w tym trybie.";
        if (text is "propozycje" or "co proponujesz" or "sugestie")
            return insights?.Suggestions() ?? "Propozycje nie są dostępne w tym trybie.";

        // 0.95 · self-repair, self-improve, stoper — warstwa Jarvisa. Bez samomodyfikacji kodu.
        if (text is "napraw sie" or "napraw się" or "self repair" or "napraw dane" or "napraw pamiec" or "napraw pamięć" or "napraw pliki" or "naprawa")
        {
            var repairLines = new List<string>
            {
                "SELF-REPAIR SENTINEL X — naprawiam DANE, nigdy kod. Niczego nie kasuję: uszkodzone pliki odkładam na bok z kopią.",
            };
            repairLines.Add(insights?.SelfRepair() ?? "Inspekcja plików nie jest dostępna w tym trybie.");
            if (learned != null) { learned.RepairFile(out string learnedReport); repairLines.Add(learnedReport); }
            repairLines.Add("Kod aplikacji pozostaje nietknięty — zmiany kodu to nowe wersje w CI. „samokontrola” pokazuje stan bez zmian.");
            return string.Join("\n", repairLines);
        }
        if (text is "ulepsz sie" or "ulepsz się" or "self improve" or "samoulepszanie" or "naucz sie" or "naucz się" or "jak sie uczysz")
        {
            return "Nie modyfikuję własnego kodu — i to jest świadoma decyzja: kod zmienia się wersjami w CI, z testami.\n" +
                "Ulepszam się za to bezpiecznie, z danych i Twoich poprawek:\n" +
                "· uczę się wzorców — literówki i zaakceptowane propozycje „Czy chodziło Ci o…” zapamiętuję jako trwałe skojarzenia,\n" +
                "· „napraw sie” naprawia moje pliki (przywracanie z kopii, odkładanie uszkodzonych),\n" +
                "· „samokontrola” sprawdza spójność, „lekcje” pokazuje, czego się nauczyłem, „propozycje” sugeruje porządki.\n\n" +
                (learned?.Describe() ?? "Nie mam jeszcze nauczonych wzorców.") + "\n\n" +
                "Nauczę się też wprost: popraw mnie przy odpowiedzi albo zaakceptuj propozycję — następnym razem zadziała od razu.";
        }
        if (text is "stoper" or "stoper start" or "stoper stop" or "stoper ile" or "ile leci stoper" or "jak dlugo leci stoper" or "jak długo leci stoper")
        {
            if (text is "stoper stop") return Core.StopwatchRegistry.Stop();
            if (text is "stoper start") return Core.StopwatchRegistry.Start();
            return Core.StopwatchRegistry.Status();
        }

        // Honest capability boundaries: these are deliberate refusals, not gaps.
        if (text is "model 3d" or "zbuduj model 3d" or "modeluj 3d" or "generuj model 3d" or "zrob model 3d" or "druk 3d")
            return "Uczciwie: nie buduję modeli 3D. Nie mam tu silnika graficznego ani narzędzi CAD i nie chcę udawać, że mam.\n" +
                "Mogę za to: policzyć wymiary („policz”), przeliczyć jednostki („przelicz”), zapisać zadanie związane z projektem („zrob zadanie: …”) i przypomnieć o nim w terminie.\n" +
                "Do samego modelowania polecam Blendera (darmowy) — mogę dodać zadanie „pobrać Blendera”, jeśli chcesz.";
        if (text is "zmien swoj kod" or "napraw swoj kod" or "zmodyfikuj swoj kod" or "zaktualizuj sie" or "przepisz sie" or "zmien zrodla")
            return "Nie modyfikuję własnego kodu — i to jest świadoma decyzja, nie brak umiejętności.\n" +
                "Samodzielna zmiana kodu bez kontroli mogłaby zepsuć aplikację, w której masz swoje dane. Mam za to bezpieczne odpowiedniki:\n" +
                "· „napraw sie” — sam naprawiam swoje uszkodzone pliki (dane, nie kod),\n" +
                "· „ulepsz sie” — pokazuję, czego się nauczyłem i jak się uczę z Twoich poprawek,\n" +
                "· „samokontrola” — sprawdzam spójność swoich plików i raportuję,\n" +
                "· „propozycje” — proponuję porządki, ale nic nie wykonuję bez Twojego polecenia,\n" +
                "· aktualizacje kodu przychodzą jako nowe wersje publikowane w Releases repozytorium.";
        if (Regex.IsMatch(text, @"^(?:skanuj|przeskanuj) (?:caly )?dysk"))
            return "Nie skanuję całych dysków automatycznie — to kosztowne i narusza prywatność. Zamiast tego: „pokaz dyski” (pojemność), „top procesy” (co zużywa zasoby), „zabezpieczenia” (stan ochrony Windows).";

        if (tasks != null)
        {
            var searchTasks = Regex.Match(text, @"^szukaj (?:w )?zadaniach[:\s]+(.+)$");
            if (searchTasks.Success)
            {
                string needle = ConversationMemoryService.Normalize(searchTasks.Groups[1].Value);
                var found = tasks.GetTasks(includeDone: true)
                    .Where(x => ConversationMemoryService.Normalize(x.Title).Contains(needle, StringComparison.Ordinal)).Take(10).ToArray();
                if (found.Length == 0) return "Nie znalazłem zadań pasujących do: " + searchTasks.Groups[1].Value.Trim();
                return "Znalezione zadania (" + found.Length + "):\n" + string.Join("\n", found.Select((x, i) =>
                    $"{i + 1}. {x.Title}  [{x.Status}{(x.DueAt == null ? "" : ", termin " + x.DueAt.Value.ToString("dd.MM HH:mm"))}]"));
            }
        }
        return null;
    }

    private string? TryHandleProjectCommand(string command, string text)
    {
        if (projects == null) return null;
        if (text is "aktywny projekt" or "status projektu")
        {
            var active = projects.ActiveProject;
            return active == null
                ? "Żaden projekt nie jest aktywny — kontekst AI jest globalny. Wpisz „nowy projekt: nazwa” albo „projekty”."
                : StorageResult($"Aktywny projekt: {active.Name} ({active.Status}). Kontekst AI widzi tylko wpisy globalne i ten projekt.");
        }
        if (text is "projekty" or "lista projektow" or "moje projekty" or "pokaz projekty")
        {
            var list = projects.GetProjects();
            if (list.Count == 0) return "Nie ma jeszcze żadnego projektu. Wpisz „nowy projekt: nazwa”, aby utworzyć pierwszy.";
            var active = projects.ActiveProjectId;
            return "Projekty:\n" + string.Join("\n", list.Select((x, i) => $"{i + 1}. {(x.Id == active ? "▶ " : "")}{x.Name}  ({x.Status})"))
                + "\nWpisz „użyj projektu N”, aby aktywować wybrany.";
        }
        var create = Regex.Match(command.Trim(), @"^nowy projekt[:\s]\s*(.+)$", RegexOptions.IgnoreCase);
        if (create.Success)
        {
            var created = projects.Create(create.Groups[1].Value.Trim(), "", activate: true);
            return created != null
                ? StorageResult($"Utworzono i aktywowano projekt „{created.Name}”. Nowe notatki i rozmowy trafiają do niego do czasu wyłączenia.")
                : projects.LastStorageError ?? "Nie utworzono projektu.";
        }
        var use = Regex.Match(command.Trim(), @"^(?:użyj|uzyj|aktywuj) projektu?\s+(\d{1,3})$", RegexOptions.IgnoreCase);
        if (use.Success && int.TryParse(use.Groups[1].Value, out int index))
        {
            var list = projects.GetProjects();
            if (index < 1 || index > list.Count) return "Podaj numer projektu z listy (1…" + Math.Max(1, list.Count) + ").";
            return projects.Activate(list[index - 1].Id)
                ? StorageResult($"Aktywowano projekt „{list[index - 1].Name}”.")
                : projects.LastStorageError ?? "Nie udało się aktywować projektu.";
        }
        if (text is "wylacz projekt" or "zdezaktywuj projekt" or "bez projektu")
        {
            projects.Deactivate();
            return StorageResult("Kontekst projektu wyłączony — AI znów widzi całą pamięć globalną.");
        }
        return null;
    }

    private string? TryHandleTaskCommand(string command, string text)
    {
        if (tasks == null) return null;

        // A reminder proposal waits for an explicit yes; anything else clears it silently and processes normally.
        if (pendingReminder is { } pending)
        {
            if (DateTime.Now > pending.Expires) pendingReminder = null;
            else if (text is "tak" or "potwierdz")
            {
                pendingReminder = null;
                var created = tasks.AddReminder(pending.Text, pending.When, "");
                return created != null
                    ? StorageResult($"Przypomnienie ustawione na {pending.Description}. Zadziała tylko, gdy aplikacja jest uruchomiona; spóźnione pokażą się jako przegapione.")
                    : tasks.LastStorageError ?? "Nie ustawiono przypomnienia.";
            }
            else if (text is "nie" or "anuluj") { pendingReminder = null; return "Anulowano — nic nie ustawiono."; }
            else pendingReminder = null;
        }

        var remind = Regex.Match(command, @"^przypomnij(?:\s+mi)?(?:\s+że)?\s+(.+)$", RegexOptions.IgnoreCase);
        if (remind.Success)
        {
            if (!TrySplitReminderTime(remind.Groups[1].Value.Trim(), out string reminderText, out DateTime when, out string description))
                return description.Length > 0
                    ? "Termin: " + description + " Nic nie zapisano."
                    : "Nie rozpoznano terminu. Napisz np.: „przypomnij mi jutro o 18 o telefonie do mamy”, „przypomnij za 30 minut o herbacie” albo „przypomnij o herbacie za 30 minut”.";
            pendingReminder = (reminderText, when, description, DateTime.Now.AddMinutes(5));
            return $"Rozumiem: przypomnienie „{reminderText}” na {description} (czas lokalny). Zapiszę to dopiero po Twojej zgodzie — odpowiedz „tak” albo „nie”.";
        }

        // 0.95 · timer i budzik: odliczanie armowane od razu — samo polecenie jest żądaniem,
        // więc nie wymaga „tak” (inaczej niż przypomnienia z opisem). Powiadomienie przychodzi
        // tym samym kanałem ⏰, tylko gdy aplikacja jest uruchomiona.
        var timer = Regex.Match(command, @"^timer[:\s]+(.+)$", RegexOptions.IgnoreCase);
        if (timer.Success)
        {
            string rest = timer.Groups[1].Value.Trim();
            // Czas to zwykle dwa słowa („5 minut”) — spróbuj ich, potem jednego, reszta to opis.
            string[] words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool parsed = false; TimeSpan span = default; string durationDescription = "";
            int usedWords = 0;
            if (words.Length >= 2 && UtilityToolbox.TryParseDuration(words[0] + " " + words[1], out span, out durationDescription)) { parsed = true; usedWords = 2; }
            else if (words.Length >= 1 && UtilityToolbox.TryParseDuration(words[0], out span, out durationDescription)) { parsed = true; usedWords = 1; }
            if (!parsed)
                return "Czas timera podaj liczbą i jednostką: „timer 5 minut”, „timer 30 sekund”, „timer 1 godzina”. Opis po czasie jest opcjonalny („timer 10 minut herbata”).";
            string label = usedWords > 0 && words.Length > usedWords ? string.Join(' ', words.Skip(usedWords)) : "";
            var createdTimer = tasks.AddReminder("Timer" + (label.Length > 0 ? ": " + label : ""), DateTime.Now + span, "");
            return createdTimer != null
                ? "⏱ Timer ustawiony na " + durationDescription + " — przypomnę o " + createdTimer.RemindAt.ToString("HH:mm") + ". Zadziała tylko, gdy aplikacja jest uruchomiona."
                : tasks.LastStorageError ?? "Nie udało się ustawić timera.";
        }
        var alarm = Regex.Match(command, @"^budzik[:\s]+(\d{1,2})[:.](\d{2})$", RegexOptions.IgnoreCase);
        var alarmHour = Regex.Match(command, @"^budzik[:\s]+(\d{1,2})$", RegexOptions.IgnoreCase);
        if (alarm.Success || alarmHour.Success)
        {
            int hour = int.Parse((alarm.Success ? alarm : alarmHour).Groups[1].Value);
            int minute = alarm.Success ? int.Parse(alarm.Groups[2].Value) : 0;
            if (hour > 23 || minute > 59) return "Godzina budzika to 0:00–23:59, np. „budzik 7:00” albo „budzik 6:30”.";
            var when = DateTime.Today.AddHours(hour).AddMinutes(minute);
            if (when <= DateTime.Now) when = when.AddDays(1);
            var createdAlarm = tasks.AddReminder("Budzik — „" + when.ToString("HH:mm") + "”", when, "");
            return createdAlarm != null
                ? "⏰ Budzik ustawiony na " + when.ToString("HH:mm") + " (" + (when.Date == DateTime.Today ? "dziś" : "jutro") + "). Zadziała tylko, gdy aplikacja jest uruchomiona."
                : tasks.LastStorageError ?? "Nie udało się ustawić budzika.";
        }
        // „dodaj zadanie”, „zrob zadanie” and „nowe zadanie” are one explicit command — the task lands
        // directly in the Tasks tab (Centrum → 📓), not just in the chat reply. 0.94: priorytet z tekstu.
        var addTask = Regex.Match(command, @"^(?:dodaj|zrob|nowe) zadanie[:\s]\s*(.+)$", RegexOptions.IgnoreCase);
        if (addTask.Success)
        {
            string title = addTask.Groups[1].Value.Trim();
            const string highMarkers = @"\b(?:piln(?:y|a|e)?|w(?:a|ą)zn(?:y|a|e)|na juz|asap|priorytet wysoki|natychmiast|gor(?:a|ą)c(?:y|a|e))\b";
            const string lowMarkers = @"\b(?:nie pilne|niski priorytet|priorytet niski|kiedy(?:s|ś)|bez spiechu|lu(?:z|ź)no)\b";
            string priority = TaskRecord.PriorityNormal;
            if (Regex.IsMatch(title, highMarkers, RegexOptions.IgnoreCase) && !Regex.IsMatch(title, lowMarkers, RegexOptions.IgnoreCase))
                priority = TaskRecord.PriorityHigh;
            else if (Regex.IsMatch(title, lowMarkers, RegexOptions.IgnoreCase))
                priority = TaskRecord.PriorityLow;
            if (priority != TaskRecord.PriorityNormal)
            {
                title = Regex.Replace(title, highMarkers, "", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, lowMarkers, "", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, @"\s+", " ").Trim(' ', ':', '-', '–', ',', '.');
            }
            if (title.Length == 0) return "Zadanie musi mieć treść poza słowem priorytetu, np. „dodaj zadanie: pilne kupić mleko”.";
            DateTime? due = null; string dueNote = "";
            if (TrySplitReminderTime(title, out string cleanTitle, out DateTime parsedDue, out string dueDescription) && cleanTitle.Length > 0)
            { title = cleanTitle; due = parsedDue; dueNote = $" z terminem {dueDescription} (czas lokalny)"; }
            string projectId = projects?.ActiveProjectId ?? "";
            string priorityNote = priority == TaskRecord.PriorityHigh ? " (priorytet WYSOKI)"
                : priority == TaskRecord.PriorityLow ? " (priorytet niski)" : "";
            return tasks.AddTask(title, priority, due, projectId) != null
                ? StorageResult("Zadanie zapisane" + priorityNote + dueNote + ". Znajdziesz je w Centrum → zakładka 📓 Zadania.")
                : tasks.LastStorageError ?? "Nie zapisano zadania.";
        }
        if (text is "zadania" or "moje zadania" or "lista zadan")
        {
            var open = tasks.GetTasks().Take(10).ToArray();
            if (open.Length == 0) return "Brak otwartych zadań. Dodaj: „dodaj zadanie: treść”.";
            var projectNames = projects != null ? projects.GetProjects(includeArchived: true).ToDictionary(x => x.Id, x => x.Name) : new Dictionary<string, string>();
            return "Otwarte zadania:\n" + string.Join("\n", open.Select((x, i) =>
                $"{i + 1}. {(x.Priority == TaskRecord.PriorityHigh ? "‼ " : "")}{x.Title}{(x.DueAt != null ? $"  (termin: {x.DueAt:dd.MM HH:mm})" : "")}{(x.ProjectId.Length > 0 && projectNames.TryGetValue(x.ProjectId, out string? name) ? $"  [{name}]" : "")}"))
                + "\nWpisz „zadanie N zrobione”, aby zamknąć wybrane.";
        }
        var done = Regex.Match(text, @"^zadanie (\d{1,3}) zrobione$");
        if (done.Success && int.TryParse(done.Groups[1].Value, out int taskIndex))
        {
            var open = tasks.GetTasks().Take(10).ToArray();
            if (taskIndex < 1 || taskIndex > open.Length) return "Podaj numer zadania z listy (1…" + Math.Max(1, open.Length) + ").";
            var target = open[taskIndex - 1];
            return tasks.SetTaskStatus(target.Id, TaskRecord.StatusDone) ? StorageResult($"Zrobione: „{target.Title}”. Zostaje w panelu Zadania → filtr Zrobione.") : "Nie udało się zmienić statusu.";
        }
        // 0.94: priorytet zadania z czatu — jawna zmiana, z numerem z listy „zadania”.
        var setPriority = Regex.Match(text, @"^zadanie (\d{1,3}) priorytet (wysoki|niski|normalny)$");
        if (setPriority.Success && int.TryParse(setPriority.Groups[1].Value, out int priorityIndex))
        {
            var open = tasks.GetTasks().Take(10).ToArray();
            if (priorityIndex < 1 || priorityIndex > open.Length) return "Podaj numer zadania z listy (1…" + Math.Max(1, open.Length) + ").";
            string newPriority = setPriority.Groups[2].Value switch
            {
                "wysoki" => TaskRecord.PriorityHigh,
                "niski" => TaskRecord.PriorityLow,
                _ => TaskRecord.PriorityNormal
            };
            var target = open[priorityIndex - 1];
            return tasks.SetTaskPriority(target.Id, newPriority)
                ? StorageResult($"Priorytet zadania „{target.Title}”: {newPriority}.")
                : "Nie udało się zmienić priorytetu.";
        }
        if (text is "przypomnienia" or "moje przypomnienia" or "lista przypomnien")
        {
            var upcoming = tasks.GetReminders().Where(x => x.NotifiedAt == null).Take(10).ToArray();
            if (upcoming.Length == 0) return "Brak aktywnych przypomnień. Napisz np. „przypomnij mi jutro o 18 o raporcie”.";
            return "Aktywne przypomnienia:\n" + string.Join("\n", upcoming.Select((x, i) => $"{i + 1}. {x.Text}  —  {x.RemindAt:dd.MM.yyyy HH:mm}"))
                + "\nPrzypominam tylko, gdy aplikacja działa; spóźnione pojawią się jako przegapione.";
        }
        if (Regex.IsMatch(text, @"^(usun|skasuj) (zadanie|przypomnienie)"))
            return "Usuwanie zadań i przypomnień robi się wyłącznie w panelu Zadania — świadomy klik, nie polecenie tekstowe. Nie wykonano zmian.";
        return null;
    }

    /// <summary>Splits a reminder sentence into text + a parsed time phrase. Both natural Polish orders work:
    /// trailing („telefon do mamy jutro o 18") and leading („jutro o 18 telefon do mamy"). Trailing wins on ambiguity.</summary>
    private static bool TrySplitReminderTime(string remainder, out string text, out DateTime when, out string description)
    {
        text = ""; when = default; description = "";
        const string timePhrases = @"za \d+ (?:min\w+|godzin\w*|dni|dzień)|za pół godziny|pojutrze(?: o \d{1,2}(?::\d{2})?)?|jutro(?: o \d{1,2}(?::\d{2})?)?|dzi[śs](?:iaj)? o \d{1,2}(?::\d{2})?|w (?:poniedziałek|wtorek|środ[ęe]|czwartek|piątek|sobot[ęe]|niedziel[ęea])(?: o \d{1,2}(?::\d{2})?)?|(?:na )?\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?(?: o \d{1,2}(?::\d{2})?)?|o \d{1,2}(?::\d{2})?|\d{1,2}:\d{2}";
        var trailing = Regex.Match(remainder, "^(?<body>.+?)\\s+(?<time>" + timePhrases + ")$", RegexOptions.IgnoreCase);
        var leading = Regex.Match(remainder, "^(?<time>" + timePhrases + @")\s*,?\s*(?<body>.+)$", RegexOptions.IgnoreCase);
        var match = trailing.Success ? trailing : leading;
        if (!match.Success) return false;
        if (!PolishTimeParser.TryParse(match.Groups["time"].Value, DateTime.Now, out DateTime parsed, out description)) return false;
        if (parsed <= DateTime.Now) { description = description.Length > 0 ? description : "Ten termin już minął (rozumiem: " + match.Groups["time"].Value + ")."; return false; }
        text = match.Groups["body"].Value.Trim().Trim(',', '.', '!').Trim();
        if (text.StartsWith("o ", StringComparison.OrdinalIgnoreCase) && text.Length > 2) text = text[2..].TrimStart(); // „jutro o 18 o herbacie" → „herbacie"
        if (text.Length == 0) return false;
        when = parsed;
        description = description.Length > 0 ? description : "";
        return true;
    }

    private static bool MentionedComputerState(string text)
    {
        return Regex.IsMatch(text, @"\b(?:komputer|pc|windows|cpu|procesor|ram|pamiec|pamieci|dysk|internet|sie[cć]|gra|grania|lagi|scinki|fps|temperatur)");
    }

    private string? TryHandleMemoryCommand(string command, string text)
    {
        if (text is "nowa rozmowa" or "nowa sesja" or "zacznij nowa rozmowe")
        {
            memory.StartNewSession(); lastTopic = "";
            return StorageResult("Rozpoczęto nową rozmowę. Wcześniejsze rozmowy są zachowane — wpisz „pokaż rozmowy”, aby wrócić.");
        }
        if (text is "pokaz rozmowy" or "lista rozmow" or "moje rozmowy")
        {
            var conversations = memory.GetConversations().Take(10).ToArray();
            if (conversations.Length == 0) return "Nie ma jeszcze zapisanych rozmów.";
            var lines = conversations.Select((x, i) => $"{i + 1}. {(x.Id == memory.ActiveSessionId ? "▶ " : "")}{x.Title}  (ostatnio: {x.LastActiveAt:dd.MM HH:mm})");
            return "Zapisane rozmowy:\n" + string.Join("\n", lines) + "\nWpisz „wznów rozmowę N”, aby wrócić do wybranej.";
        }
        var resume = Regex.Match(text, @"^wznow rozmowe (\d{1,3})$");
        if (resume.Success && int.TryParse(resume.Groups[1].Value, out int index))
        {
            var conversations = memory.GetConversations().Take(10).ToArray();
            if (index < 1 || index > conversations.Length) return "Podaj numer rozmowy z listy (1…" + Math.Max(1, conversations.Length) + ").";
            var target = conversations[index - 1];
            lastTopic = "";
            return memory.ResumeSession(target.Id)
                ? StorageResult($"Wznowiono rozmowę „{target.Title}”. Widok rozmowy został przeładowany.")
                : "Ta rozmowa jest już aktywna.";
        }
        if (text is "tryb prywatny" or "wlacz tryb prywatny" or "wylacz tryb prywatny")
        {
            bool enable = text == "tryb prywatny" ? !memory.PrivateMode : text == "wlacz tryb prywatny";
            memory.SetPrivateMode(enable);
            return enable
                ? "Tryb prywatny WŁĄCZONY. Treść rozmowy nie jest nigdzie zapisywana — po zamknięciu nie będzie czego przywrócić. Trwałe wspomnienia i zgody działają bez zmian."
                : "Tryb prywatny WYŁĄCZONY. Rozmowa jest zapisywana zgodnie z ustawieniami prywatności.";
        }
        var searchMemory = Regex.Match(text, @"^(?:szukaj|znajdz) w pamieci (.+)$");
        if (searchMemory.Success)
        {
            string query = searchMemory.Groups[1].Value.Trim(' ', '„', '”', '"');
            var found = memory.SearchNotes(query).Take(10).ToArray();
            if (found.Length == 0) return "Nie znaleziono wspomnień pasujących do: " + query;
            return "Pasujące wspomnienia:\n" + string.Join("\n", found.Select((x, i) => $"{i + 1}. {(x.Pinned ? "📌 " : "")}{(x.SupersededAt != null ? "[nieaktualne] " : "")}{x.Text}"));
        }
        if (text is "co poszlo do modelu" or "pokaz kontekst ai" or "dlaczego to pamietasz")
        {
            var trace = memory.LastContextTrace;
            if (!memory.PrivacyContextVisible) return "Podgląd kontekstu AI jest wyłączony (Ustawienia → Pamięć i prywatność). Źródła kontekstu nie są rejestrowane.";
            if (trace.Count == 0) return "Od uruchomienia nie wysłano jeszcze zapytania z kontekstem do modelu.";
            var lines = trace.Select(x => $"• {x.Kind}: {x.Label} — {x.Reason}");
            return $"Kontekst ostatniego zapytania ({memory.LastContextBuiltAt:HH:mm:ss}):\n" + string.Join("\n", lines);
        }
        if (text is "co pamietasz" or "pokaz pamiec" or "lista wspomnien" or "moje preferencje") return memory.GetNotesSummary();
        if (text is "jak mam na imie" or "pamietasz moje imie" or "jak sie nazywam")
            return memory.UserName.Length > 0 ? $"Masz na imię {memory.UserName}." : "Nie mam zapisanego imienia. Możesz powiedzieć: mam na imię…";
        if (Regex.IsMatch(text, @"^(?:mam na imie|nazywam sie|mow do mnie|zwracaj sie do mnie) "))
        {
            // MainWindow stores the user message before routing; this also supports standalone router use.
            if (memory.UserName.Length > 0) return StorageResult($"Zapamiętam: {memory.UserName}.");
        }
        if (Regex.IsMatch(text, @"^(?:wole|preferuje) (?:krotkie|zwiezle|dlugie|dokladne|szczegolowe) odpowiedzi$")) return StorageResult("Zapamiętam tę preferencję odpowiedzi.");
        bool isNote = text.StartsWith("zapamietaj ", StringComparison.Ordinal) || text.StartsWith("zapamietaj:", StringComparison.Ordinal)
            || text.StartsWith("notatka ", StringComparison.Ordinal) || text.StartsWith("notatka:", StringComparison.Ordinal);
        if (isNote)
        {
            int prefix = text.StartsWith("zapamietaj", StringComparison.Ordinal) ? "zapamietaj".Length : "notatka".Length;
            string noteText = command[Math.Min(command.Length, prefix)..].TrimStart(':', ' ').Trim();
            var result = memory.AddNote(noteText);
            IReadOnlyList<ConversationMemoryEntry> similar = result == NoteAddResult.Added ? memory.FindSimilarNotes(noteText) : [];
            string hint = similar.Count > 0 ? $"\nPodobne istniejące wspomnienie: „{similar[0].Text}”. Jeśli wpisy się wykluczają, oznacz stare jako nieaktualne w panelu Pamięć." : "";
            return result switch
            {
                NoteAddResult.Added => StorageResult("Zapamiętane lokalnie." + hint),
                NoteAddResult.StaleDuplicate => StorageResult("To wspomnienie było oznaczone jako nieaktualne — przywróciłem je zamiast tworzyć duplikat."),
                NoteAddResult.Duplicate => "Takie wspomnienie już istnieje. Nic nie zapisano.",
                NoteAddResult.Disabled => memory.LastStorageError ?? "Zapisywanie wspomnień jest wyłączone.",
                _ => memory.LastStorageError ?? "Nie zapisano wspomnienia."
            };
        }
        if (text is "status pamieci" or "ile pamietasz") return StorageResult($"Pamięć: {memory.Count} wpisów, w tym {memory.NoteCount} trwałych wspomnień.");
        if (Services.Memory.MemoryActionService.IsMutation(command) || text == "potwierdz usuniecie wspomnien")
            return "Operacje usuwania pamięci wymagają zgody w centrum poleceń. Nie wykonano zmian.";
        if (text is "eksportuj pamiec" or "eksportuj rozmowe")
        {
            try { return "Eksport lokalny zapisany: " + memory.Export(); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return "Nie udało się wyeksportować pamięci: " + ex.Message; }
        }
        if (text is "eksportuj rozmowe markdown" or "zapisz rozmowe markdown" or "eksportuj rozmowe do pliku")
        {
            var export = memory.ExportConversationMarkdown();
            return export.Success
                ? StorageResult($"Zapisano rozmowę lokalnie ({export.Turns} wypowiedzi). Plik nie został wysłany do internetu.\n{export.Path}\nSHA-256: {export.Sha256}")
                : "Nie zapisano eksportu. " + export.Error;
        }
        var search = Regex.Match(text, @"^(?:szukaj w rozmowie|znajdz w rozmowie|przeszukaj rozmowe)[:\s]+(.+)$");
        if (search.Success)
        {
            string query = search.Groups[1].Value.Trim().Trim('„', '”', '"');
            var found = memory.SearchConversation(query);
            if (found.Count == 0)
                return $"Nie znalazłem „{query}” w tej rozmowie. Szukam wyłącznie w aktywnej rozmowie: {memory.ActiveConversationTitle}.";
            var lines = found.Select((x, i) =>
            {
                string who = x.Role == "user" ? "Ty" : "Sentinel";
                return (i + 1) + ". [" + x.Timestamp.ToString("dd.MM HH:mm") + "] " + who + ": " + Truncate(x.Text, 160);
            });
            return $"Znalezione w tej rozmowie ({found.Count}, najnowsze pierwsze):\n" + string.Join("\n", lines) +
                "\nSzukanie dotyczy wyłącznie aktywnej rozmowy i jest dopasowaniem tekstu po normalizacji — bez literówek i odmiany.";
        }
        if (text is "co powiedzialem wczesniej" or "co mowilem wczesniej" or "co powiedzialem przed chwila")
            return memory.TryGetPreviousUserMessage(command, out string previous) ? previous : "Nie mam wcześniejszej wypowiedzi w tej rozmowie.";
        var after = Regex.Match(text, @"^co (?:powiedzialem|mowilem) po (.+)$");
        if (after.Success)
            return memory.TryFindTextAfter(after.Groups[1].Value.Trim(' ', '„', '”', '"'), command, out string result) ? result : "Nie znalazłem pasującej wcześniejszej wypowiedzi.";
        var minutes = Regex.Match(text, @"^co (?:powiedzialem|mowilem) (\d{1,6}) minut(?:y|e)? temu$");
        if (minutes.Success && int.TryParse(minutes.Groups[1].Value, out int countMinutes))
            return memory.TryGetUserMessageFromAgo(TimeSpan.FromMinutes(countMinutes), command, out string old) ? old : "Nie znalazłem wypowiedzi z tego przedziału czasu.";
        return null;
    }

    private string BuildSystemContext(string topic, bool includeSystemFacts, bool includeRecentHistory)
    {
        var parts = new List<string>();
        // 0.94: data i godzina zawsze — dopytania „co dzisiaj”, „ile dni do…” i daty względne
        // przestają zgadywać. Aktywny projekt i tytuł rozmowy trzymają kontekst w jednym miejscu.
        parts.Add("Dzisiaj jest " + DateTime.Now.ToString("yyyy-MM-dd") + " (" +
            DateTime.Now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL")) + "), godzina " + DateTime.Now.ToString("HH:mm") + ".");
        try
        {
            string projectLine = projects?.ActiveProject is { } activeProject && activeProject.Name.Length > 0
                ? "Aktywny projekt: " + activeProject.Name + "."
                : "Żaden projekt nie jest aktywny — pamięć jest globalna.";
            parts.Add(projectLine + " Rozmowa: " + memory.ActiveConversationTitle + ".");
        }
        catch { /* kontekst pomocniczy nigdy nie może wywrócić zapytania */ }
        if (includeSystemFacts)
        {
            try
            {
                parts.Add($"Odczyt bieżący: {DateTime.Now:yyyy-MM-dd HH:mm:ss}. Procesor: {systemInfo.GetCpuName()}. CPU: {Number(systemMonitor.GetCpuUsage(), "%")}. RAM: {Number(systemMonitor.GetUsedRamGB(), "GB", 1)} / {Number(systemMonitor.GetTotalRamGB(), "GB", 1)} ({Number(systemMonitor.GetRamUsagePercent(), "%")}). Windows: {systemInfo.GetWindowsVersion()}. Czas działania: {systemInfo.GetUptime()}.");
            }
            catch { parts.Add("Bieżące odczyty komputera są niedostępne."); }
        }

        if (!string.IsNullOrWhiteSpace(topic))
            parts.Add("Ostatni temat skrótu: " + topic + ".");

        // 0.94: ostatnie odczyty narzędzi z tej sesji — żeby „a ile wolnego?” miało sens.
        string sessionFacts = facts?.Describe() ?? "";
        if (sessionFacts.Length > 0) parts.Add(sessionFacts);

        parts.Add(includeRecentHistory ? memory.GetRecentContext(12) : memory.GetStableContext());
        return string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool IsFollowUpQuestion(string text, string followUp, string topic) =>
        !string.IsNullOrWhiteSpace(topic) && Regex.IsMatch(followUp, @"^(?:czy to|czy jest|a |a teraz|dlaczego|czemu|co z tym|ile|jaki procent|procent)") ||
        Regex.IsMatch(text, @"\b(?:wczesniej|przed chwila|tamto|to samo|ten temat|ostatni temat|historia rozmowy|w takim razie|co z tamtym|co z tym|ile dokladnie|co dokladnie|rozwin|opowiedz wiecej|wyjasnij to|skad to|po co to|ile w tym|a co z)\b") ||
        Regex.IsMatch(text, @"^(?:a |no a |wiec |ok,? |w takim razie )");

    private static bool MentionsComputerStateNoTopic(string text, string followUp) =>
        MentionedComputerState(text + " " + followUp);

    private void SetTopic(string topic) { lastTopic = topic; lastTopicTime = DateTime.Now; }
    private async Task<string?> TryHandleSnapshotCommandAsync(string command, string text, CancellationToken cancellationToken)
    {
        if (snapshots == null) return null;
        if (text is "snapshot" or "zrob snapshot" or "snapshot diagnostyki" or "zapisz odczyt diagnostyczny")
        {
            try
            {
                var captured = await snapshots.CaptureAsync("", cancellationToken);
                if (captured == null) return "Nie udało się zapisać odczytu. " + (snapshots.LastStorageError ?? "");
                return SnapshotStorage($"Zapisano odczyt diagnostyczny: {captured.Label} · {captured.CapturedAt:dd.MM.yyyy HH:mm:ss} · {captured.Sections.Count} sekcji. " +
                    "To obraz stanu w jednej chwili, nie diagnoza kondycji sprzętu. Wpisz „snapshoty”, a potem „porównaj snapshoty”.");
            }
            catch (OperationCanceledException) { return "Przerwano zapisywanie odczytu. Nic nie zostało zapisane."; }
        }
        if (text is "snapshoty" or "lista snowshotow" or "lista snapshotow" or "pokaz snapshoty")
            return SnapshotStorage(snapshots.List());
        var pair = Regex.Match(text, @"^porownaj snapshoty? (\d{1,2}) (?:z )?(\d{1,2})$");
        bool latestPair = text is "porownaj snapshoty" or "porownaj snapshot" or "rozni sie cos" or "co sie zmienilo w diagnostyce";
        if (pair.Success || latestPair)
        {
            var all = snapshots.GetSnapshots().OrderBy(x => x.CapturedAt).ToArray();
            if (all.Length < 2) return "Potrzebuję dwóch zapisanych odczytów, żeby je porównać. Wpisz „snapshot” teraz, a drugi później.";
            DiagnosticSnapshot first, second;
            if (pair.Success)
            {
                int a = int.Parse(pair.Groups[1].Value), b = int.Parse(pair.Groups[2].Value);
                if (a < 1 || b < 1 || a > all.Length || b > all.Length) return "Podaj numery odczytów z listy (1…" + all.Length + ").";
                first = all[a - 1]; second = all[b - 1];
            }
            else { first = all[^2]; second = all[^1]; }
            var diff = snapshots.Compare(first.Id, second.Id, out string reason);
            if (diff == null) return reason;
            var lines = diff.Sections.SelectMany(section =>
                section.Changed.Select(x => $"· {section.SectionTitle}: {x.From} → {x.To}")
                    .Concat(section.Added.Select(x => $"· {section.SectionTitle}: pojawiło się {x}"))
                    .Concat(section.Removed.Select(x => $"· {section.SectionTitle}: zniknęło {x}")));
            string body = diff.TotalChanges == 0 && diff.SectionsOnlyInFirst.Count == 0
                ? "Brak różnic — te odczyty opisują ten sam stan."
                : string.Join("\n", lines.Take(24));
            return SnapshotStorage($"Porównanie {first.Label} ({first.CapturedAt:dd.MM HH:mm:ss}) → {second.Label} ({second.CapturedAt:dd.MM HH:mm:ss}):\n{diff.Headline}\n{body}" +
                (diff.TotalChanges > 24 ? "\n…i kolejne różnice — pełna lista po wpisaniu „eksportuj porównanie”." : "") +
                "\nRóżnica nie wyjaśnia przyczyny; pokazuje tylko, co zmieniło się między odczytami.");
        }
        if (text is "eksportuj porownanie" or "eksportuj porownanie snapshotow")
        {
            var all = snapshots.GetSnapshots().OrderBy(x => x.CapturedAt).ToArray();
            if (all.Length < 2) return "Nie mam dwóch odczytów do porównania — najpierw wpisz „snapshot”.";
            try
            {
                var result = await snapshots.ExportComparisonAsync(all[^2].Id, all[^1].Id, cancellationToken);
                return result.Status switch
                {
                    "VERIFIED" => "VERIFIED\n" + result.Message + "\n" + result.Evidence,
                    "UNVERIFIED" => "Zapisano bez pełnego dowodu odczytu zwrotnego.\n" + result.Message + "\n" + result.Evidence,
                    _ => "FAILED\n" + result.Message + (result.Evidence.Length > 0 ? "\n" + result.Evidence : "")
                };
            }
            catch (OperationCanceledException) { return "Przerwano eksport porównania."; }
        }
        var delete = Regex.Match(text, @"^usun snapshot (\d{1,2})$");
        if (delete.Success)
        {
            var all = snapshots.GetSnapshots().OrderBy(x => x.CapturedAt).ToArray();
            if (!int.TryParse(delete.Groups[1].Value, out int index) || index < 1 || index > all.Length)
                return "Podaj numer odczytu z listy (1…" + Math.Max(1, all.Length) + ").";
            return snapshots.Delete(all[index - 1].Id)
                ? SnapshotStorage($"Usunięto wyłącznie odczyt {all[index - 1].Label}. Pozostałe odczyty i raporty są nietknięte.")
                : "Nie usunięto odczytu. " + (snapshots.LastStorageError ?? "");
        }
        return null;
    }

    private string? TryHandleWorkspaceCommand(string command, string text)
    {
        if (text is "pomoc" or "co umiesz" or "lista komend" or "komendy") return Help;
        if (text is "skroty" or "skroty komend" or "jakie skroty")
            return "Obsługiwane skróty (wpisz dokładnie tak, bez polskich znaków):\n" +
                string.Join("\n", Core.IntentCatalog.Abbreviations.OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => "· " + x.Key + " → " + x.Value)) +
                "\nSkróty są jawne i stałe — nie zgaduję znaczenia innych zbitek liter.";
        if (insights == null && archives == null) return null;
        if (insights != null && text is "statystyki" or "ile mam danych" or "stan danych") return insights.Statistics();
        if (insights != null && text is "backup" or "kopia zapasowa" or "zrob backup")
        {
            string result = insights.Backup(out string path);
            return path.Length == 0 ? result : "VERIFIED\n" + result;
        }
        if (insights != null && (text is "plan dnia" or "co dzis" or "podsumuj dzien" or "co mam dzisiaj")) return insights.Briefing();
        if (insights != null)
        {
            var searchAll = Regex.Match(text, @"^szukaj wszystkiego[:\s]+(.+)$");
            if (searchAll.Success) return insights.SearchAll(searchAll.Groups[1].Value.Trim());
        }
        if (archives != null && text is "archiwizuj rozmowy" or "archiwizuj rozmowe")
        {
            var result = archives.ArchiveMonth();
            return result.Success ? "VERIFIED\n" + result.Message : result.Message;
        }
        if (archives != null && text is "archiwa" or "lista archiwow" or "pokaz archiwa") return archives.Describe();
        if (archives != null)
        {
            var delete = Regex.Match(text, @"^usun archiwum (\d{4}-\d{2})$");
            if (delete.Success)
                return archives.DeleteArchive(delete.Groups[1].Value, out string reason)
                    ? "Usunięto wyłącznie archiwum " + delete.Groups[1].Value + ". Aktywne rozmowy i wspomnienia są nietknięte."
                    : "Nie usunięto archiwum. " + reason;
        }
        return null;
    }

    private const string Help = """
        SENTINEL X — CO UMIEM (wszystko działa lokalnie)

        Interfejs: wpisz „//” w polu czatu — lista poleceń, Tab wybiera, Enter wykonuje. Centrum mieści zakładki: 📓 zadania, 🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka.
        Pomiary i system: ile mam RAM · użycie CPU · użycie GPU · dyski · top procesy · czas pracy komputera · która godzina · dzisiejsza data · nazwa komputera · ile rdzeni · architektura · moje ip
        Aplikacje: włącz <nazwa> (cs2, discord, steam, chrome, brave, spotify, notatnik, kalkulator, VS Code, Firefox, VLC, OBS…) · otwórz pobrane / dokumenty / pulpit · skróty
        Diagnostyka: diagnostyka komputera (albo //diag) · eksportuj raport · status zabezpieczeń · zdarzenia windows · programy autostartu · lista usług
        Odczyty stanu: snapshot · snapshoty · porównaj snapshoty · eksportuj porównanie · usuń snapshot N
        Pamięć: zapamiętaj: … · notatka: … · co pamiętasz · co wiesz o mnie · pokaż rozmowy · nowa rozmowa · podsumuj rozmowę · fakty · szukaj w rozmowie: fraza · eksportuj rozmowę markdown (szukanie toleruje literówki)
        Archiwum: archiwizuj rozmowy · archiwa · usuń archiwum RRRR-MM
        Projekty: nowy projekt: nazwa · projekty · użyj projektu N · aktywny projekt
        Zadania: dodaj zadanie: treść · dodaj zadanie: pilne treść (priorytet z tekstu) · zrob zadanie: treść · zadania · zadanie N zrobione · zadanie N priorytet wysoki|niski|normalny · szukaj w zadaniach: fraza · przypomnienia · przypomnij mi jutro o 18 o …
        Sentinel: samokontrola · napraw sie (naprawa danych) · ulepsz sie (co się nauczyłem) · propozycje · lekcje · wersja · co nowego · zrozum: zdanie (pokazuję rozumienie, nic nie wykonuję) · fakty · co wiesz o mnie · podsumuj rozmowę
        Jarvis: timer 5 minut · budzik 7:00 · stoper start/stop · głośność / głośność 40 · wycisz · przywróć dźwięk · zrzut ekranu · kopiuj: tekst · co w schowku
        JARVIS 0.96: pogoda · pogoda Kraków · czy będzie padać · co gra · pauza · następny utwór · głośniej · ciszej · dom status · dom: włącz światło salon · scena: noc · agent: <cel> · agent status · narzedzia agenta · co jest na ekranie · przeczytaj ekran · indeks semantyczny: zbuduj · szukaj semantycznie: fraza · sekwencje · utwórz sekwencję: nazwa = krok1; krok2 · podgląd sekwencji: nazwa · uruchom sekwencję: nazwa · jarvis (opis tej warstwy)
          → pogoda wymaga internetu (Open-Meteo, bez klucza); dom i agent są wyłączone, dopóki ich nie włączysz w Ustawienia → Jarvis; sekwencje nie przyjmują kroków niszczących, a zgoda nigdy nie jest automatyczna
        Matematyka: policz 12,5*4 · pierwiastek 144 · silnia 10 · nwd 12 8 · nww 4 6 · czy pierwsza 97 · dzielniki 12 · fibonacci 10 · srednia: 2, 4, 6 · mediana: … · suma: … · min: … · max: … · srednia wazona: 4 2, 5 1 · zaokraglij 3,14159 do 2 · zmiana z 50 do 80 · procent 15 z 240 · ile to procent 30 z 240 · logarytm 1000 10 · potega 2 10 · modulo 10 3 · abs -5 · sin 30 · cos 60 · tan 45 · rownanie 1 -3 2
        Finanse i zakupy: vat 23 100 · vat 8 100 brutto · vat 5 250 · znizka 200 30 · napiwek 150 10 · raty 100000 7 25 (kwota, %, lata) · odsetki 1000 5 3 · procent skladany 1000 5 10
        Konwersje: przelicz 5 km na mile · rgb 31 162 195 · kolor 1fa2c3 · rzymskie 2026 · z rzymskich XIV · base64: tekst · dekoduj base64: … · morse: sos · dekoduj morse: … · binarnie: A · dekoduj binarnie: … · hex: Ala · dekoduj hex: …
        Tekst: ile slow: tekst · ile znakow: tekst · ile zdan: tekst · palindrom: kajak · anagram: kot, tok · rot13: ala · tytul: ala ma kota · wielkie litery: … · male litery: … · odwroc tekst: … · odwroc slowa: ala ma kota · posortuj slowa: c a b · bez powtorzen: a a b · powtorzenia slow: tekst · czestotliwosc slow: tekst · literuj: Ala · skrable: kot · tylko cyfry: ab12 · tylko litery: ab12 · slug: tekst · transliteruj: tekst · json: {…} · hash tekstu: …
        Kalendarz: ile dni do 24.12 · ile dni miedzy 1.1.2024 a 1.1.2025 · dodaj 10 dni do 1.1.2030 · odejmij 5 dni od 1.1.2030 · jaki dzien tygodnia 1.1.2030 · kalendarz 9 2026 · kwartal 15.3.2026 · rok przestepny 2024 · tydzien roku · dzien roku · ile dni do konca roku · wiek: 01.01.1990 · dni robocze 1.1.2024 do 31.1.2024 · wielkanoc 2027 · czas w toki / londyn / berlin / nowy jork / madryt / rzym / moskwa / dubaj / sydney / … · czas w strefie UTC+2
        Dokumenty PL i kody: pesel: 11 cyfr · nip: 10 cyfr · iban: PL61… · ean: 13 cyfr · isbn: … · luhn: 4111… · regon: 9 cyfr (walidacja lokalna, nic nie jest wysyłane)
        Komputer: nazwa uzytkownika · nazwa komputera · ile rdzeni · architektura · rozdzielczosc ekranu · bateria · strefa czasu · moje ip
        Losowe: losuj 1-100 · rzuc kostka · kostka 2d6 · wylosuj karte · rzut moneta · lotto · pin 6 · haslo 20 · uuid · wybierz losowo: a, b · bmi 80 180 · cytat
        Podsumowania: plan dnia · szukaj wszystkiego: fraza · statystyki · backup
        Czat: ponów (przycisk „Ponów”) · zatrzymaj generowanie (przycisk widoczny zawsze)

        Rozumienie: rozumiem całe zdania („sprawdź proszę ile mam ramu”), synonimy („odpal” = „włącz”) i polecenia ukryte w zdaniu — zawsze pokazuję „Zrozumiałem jako: …”. Literówki i skróty są poprawiane jawnie. Gdy nie jestem pewien — pytam zamiast zgadywać („Czy chodziło Ci o…”). „zrozum: zdanie” pokazuje kroki rozumienia bez wykonania. Nie zgaduję poleceń niszczących — usuwanie wymaga świadomego kliknięcia lub osobnej zgody.
        """;

    private string SnapshotStorage(string success) => snapshots?.LastStorageError == null ? success : snapshots.LastStorageError;
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    private string StorageResult(string success) => memory.LastStorageError == null ? success : memory.LastStorageError;
    internal static string Number(double value, string unit, int decimals = 0) => double.IsFinite(value) && value >= 0 ? value.ToString("F" + decimals, CultureInfo.GetCultureInfo("pl-PL")) + (unit == "%" ? "" : " ") + unit : "odczyt niedostępny";
    internal static string Normalize(string text) => ConversationMemoryService.Normalize(text);
}
