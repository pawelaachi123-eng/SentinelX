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
    private string lastTopic = "";
    private DateTime lastTopicTime;
    private (string Text, DateTime When, string Description, DateTime Expires)? pendingReminder;

    /// <summary>Raised with the best candidate when an input is about to fall through to the AI model
    /// but is close to a known command — the host turns it into a question instead of a silent guess.</summary>
    public event Action<string>? SuggestionPending;

    public CommandRouter(SystemMonitor systemMonitor, SystemInfoService systemInfo, LocalAiService localAi, ConversationMemoryService memory,
        ProjectService? projects = null, TaskService? tasks = null, DiagnosticSnapshotService? snapshots = null,
        MemoryArchiveService? archives = null, WorkspaceInsightsService? insights = null, UnderstandingJournal? journal = null)
    {
        this.systemMonitor = systemMonitor; this.systemInfo = systemInfo; this.localAi = localAi; this.memory = memory;
        this.projects = projects; this.tasks = tasks; this.snapshots = snapshots; this.archives = archives; this.insights = insights;
        this.journal = journal;
    }

    public async Task<string> ProcessAsync(string command, CancellationToken cancellationToken = default, Action<string>? onDelta = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command)) return "";
        string? previewResponse = DecisionPreview.TryExplain(command);
        if (previewResponse != null) return previewResponse;
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
        if (text is "napraw ai" or "napraw silnik ai" or "napraw silnik")
            return await localAi.RepairAsync(cancellationToken);
        if (text is "telefon" or "polacz telefon" or "polacz z telefonem" or "kod qr" or "telefon qr" or "aplikacja na telefon" or "sparuj telefon")
        {
            Services.Link.PhoneHint.Open?.Invoke();
            return Services.Link.PhoneHint.Describe?.Invoke() ?? "Łącze z telefonem uruchomi się za chwilę. Jeśli nie — sprawdź Ustawienia → Telefon.";
        }
        Match model = Regex.Match(command.Trim(), @"^(?:ustaw\s+)?model\s+ai\s+(.+)$", RegexOptions.IgnoreCase);
        if (model.Success)
        {
            try { return await localAi.SetPreferredModelAsync(model.Groups[1].Value.Trim(), cancellationToken); }
            catch (System.Net.Http.HttpRequestException) { return "Nie udało się odczytać modeli. Silnik AI uruchamia się sam — spróbuj za chwilę."; }
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
        if (text is "co nowego" or "lista zmian" or "changelog" or "co sie zmienilo")
            return "CO NOWEGO W 0.99 · RDZEŃ\n" +
                "· Nowa powłoka WinUI 3 w podglądzie: centralny animowany Rdzeń, panele zamiast zakładek, paleta komend (Ctrl+K), Mini Mode i nowoczesne powiadomienia — ten sam backend, co klasyczna aplikacja.\n" +
                "· Klasyczny wygląd WPF działa bez zmian; przełącznik powłoki w Ustawieniach.\n" +
                "\nCO NOWEGO W 0.96 · KUŹNIA\n" +
                "· Podgląd decyzji: „jak to rozumiem: <polecenie>” pokazuje, co Sentinel by zrobił (literówka, narzędzie, pytanie, model AI) — niczego przy tym nie wykonuje ani nie zapisuje.\n" +
                "· „szukaj w zadaniach: fraza” przeszukuje zadania (także zrobione) i przypomnienia; wcześniej przesłaniała je wyszukiwarka internetowa, a przypomnień nie obejmowało.\n" +
                "· 11 nowych narzędzi w kategorii „Kuźnia 0.96”: nazwy zmiennych, kodowanie URL, czas Unix, najczęstsze słowa, rata kredytu, porównanie wersji, numerowanie i odwracanie wierszy, poprawa odstępów.\n" +
                "· Porządek w kodzie: Brain/ (rozumienie i pamięć), Brain/Router/, Tools/ (narzędzia), Testing/ (testy w aplikacji).\n" +
                "\nCO NOWEGO W 0.95 · WARSZTAT\n" +
                "· Nowa strona NARZĘDZIA (🧰 w pasku albo zakładka w Centrum): katalog ponad 60 narzędzi z wyszukiwaniem, przykładem, polem argumentu i wynikiem — wszystko liczy się lokalnie.\n" +
                "· Nowe narzędzia: porównanie tekstów (diff), test wyrażeń regularnych, skrót SHA-256/MD5 pliku, wyciąganie e-maili i linków, sortowanie i usuwanie duplikatów wierszy, kwota słownie, sekundy↔czas, ocena mocy hasła, kody QR (także do sieci Wi-Fi).\n" +
                "· Wygląd: metryki CPU/RAM/GPU na żywo w pasku bocznym, przełącznik motywu jednym kliknięciem (ciemny, głęboka czerń, jasny, jak Windows), pełna paleta jasna i głębokiej czerni.\n" +
                "\nCO NOWEGO W 0.94 · AUTOPILOT\n" +
                "· Telefon: aplikacja na Androida albo przeglądarka (także iPhone) łączy się z komputerem sama — zgoda to jedno kliknięcie na PC. Czat, zadania, notatki, stan komputera i alerty są takie same jak tutaj.\n" +
                "· Silnik AI jest wbudowany (llama.cpp): instaluje się, startuje i naprawia sam. Ollama nie jest potrzebna.\n" +
                "· Autopilot: start z Windows w tle, Watch włączony, alerty na telefon, jedna linia „Wszystko działa samo”.\n" +
                "\nCO NOWEGO W 0.93 · PORZĄDKI\n" +
                "· „usuń duplikaty: folder” — z każdej grupy identycznych plików zostawia 1, resztę po Twoim „potwierdz” przenosi do Kosza.\n" +
                "· „usuń puste pliki: folder” — pliki 0 B po Twoim „potwierdz” do Kosza.\n" +
                "· „zmien nazwy: folder zamien X na Y” — podgląd zmian nazw, wykonuje dopiero po „potwierdz”, nigdy nie nadpisuje.\n" +
                "· Publikacja EXE na GitHubie tylko dla wydań milowych albo na żądanie (bez automatycznego wydania przy każdym pushu).\n" +
                "\nCO NOWEGO W 0.92 · BEZPIECZNE PLIKI\n" +
                "· Bezpieczne pliki: „duplikaty: folder” znajduje identyczne treści (SHA-256), „porzadki: folder” pokazuje, co zajmuje miejsce — oba tylko do odczytu.\n" +
                "· „usuń do kosza: ścieżka” przenosi JEDEN plik do Kosza i dopiero po Twoim „potwierdz” — nic bez zgody.\n" +
                "· Głos: polecenie działa tylko, gdy w zdaniu pada „sentinel” (w dowolnym miejscu); bez niego Sentinel tylko nasłuchuje.";
        if (text is "lekcje" or "czego sie nauczyles" or "pokaz lekcje" or "uczenie")
            return journal?.Report() ?? "Dziennik lekcji nie jest dostępny w tym trybie.";
        if (text is "samokontrola" or "sprawdz sie" or "sprawdz sentinel" or "test sentinel")
            return insights?.SelfCheck() ?? "Samokontrola nie jest dostępna w tym trybie.";
        if (text is "propozycje" or "co proponujesz" or "sugestie")
            return insights?.Suggestions() ?? "Propozycje nie są dostępne w tym trybie.";

        // Honest capability boundaries: these are deliberate refusals, not gaps.
        if (text is "model 3d" or "zbuduj model 3d" or "modeluj 3d" or "generuj model 3d" or "zrob model 3d" or "druk 3d")
            return "Uczciwie: nie buduję modeli 3D. Nie mam tu silnika graficznego ani narzędzi CAD i nie chcę udawać, że mam.\n" +
                "Mogę za to: policzyć wymiary („policz”), przeliczyć jednostki („przelicz”), zapisać zadanie związane z projektem („zrob zadanie: …”) i przypomnieć o nim w terminie.\n" +
                "Do samego modelowania polecam Blendera (darmowy) — mogę dodać zadanie „pobrać Blendera”, jeśli chcesz.";
        if (text is "zmien swoj kod" or "napraw swoj kod" or "napraw sie" or "zmodyfikuj swoj kod" or "ulepsz sie" or "zaktualizuj sie" or "przepisz sie")
            return "Nie modyfikuję własnego kodu — i to jest świadoma decyzja, nie brak umiejętności.\n" +
                "Samodzielna zmiana kodu bez kontroli mogłaby zepsuć aplikację, w której masz swoje dane. Zamiast tego mam bezpieczny odpowiednik:\n" +
                "· „samokontrola” — sprawdzam spójność swoich plików i raportuję,\n" +
                "· „propozycje” — proponuję porządki, ale nic nie wykonuję bez Twojego polecenia,\n" +
                "· aktualizacje przychodzą jako nowe wersje publikowane w Releases repozytorium.";
        if (Regex.IsMatch(text, @"^(?:skanuj|przeskanuj) (?:caly )?dysk"))
            return "Nie skanuję całych dysków automatycznie — to kosztowne i narusza prywatność. Zamiast tego: „pokaz dyski” (pojemność), „top procesy” (co zużywa zasoby), „zabezpieczenia” (stan ochrony Windows).";

        // „szukaj w zadaniach: …” lives in TryHandleTaskCommand (0.96: also reminders, and the toolbox no longer turns it into a web search).
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
        // „dodaj zadanie”, „zrob zadanie” and „nowe zadanie” are one explicit command — the task lands
        // directly in the Tasks tab (Centrum → 📓), not just in the chat reply.
        var addTask = Regex.Match(command, @"^(?:dodaj|zrob|nowe) zadanie[:\s]\s*(.+)$", RegexOptions.IgnoreCase);
        if (addTask.Success)
        {
            string title = addTask.Groups[1].Value.Trim();
            DateTime? due = null; string dueNote = "";
            if (TrySplitReminderTime(title, out string cleanTitle, out DateTime parsedDue, out string dueDescription) && cleanTitle.Length > 0)
            { title = cleanTitle; due = parsedDue; dueNote = $" z terminem {dueDescription} (czas lokalny)"; }
            string projectId = projects?.ActiveProjectId ?? "";
            return tasks.AddTask(title, TaskRecord.PriorityNormal, due, projectId) != null
                ? StorageResult("Zadanie zapisane" + dueNote + ". Znajdziesz je w Centrum → zakładka 📓 Zadania.")
                : tasks.LastStorageError ?? "Nie zapisano zadania.";
        }
        // 0.96 · KUŹNIA: read-only search through tasks (also done ones) and reminders.
        var searchTasks = Regex.Match(text, @"^(?:szukaj w zadaniach|szukaj zadan(?:ia)?|znajdz zadanie)(?:[:\s]+(.+))?$");
        if (searchTasks.Success)
        {
            // The phrase is shown with the user's own letters; the normalized match is only the fallback.
            var typedPhrase = Regex.Match(command, @"^\s*(?:szukaj\s+w\s+zadaniach|szukaj\s+zadań|szukaj\s+zadania|szukaj\s+zadan|znajdź\s+zadanie|znajdz\s+zadanie)\s*:?\s*(.+)$", RegexOptions.IgnoreCase);
            string phrase = typedPhrase.Success ? typedPhrase.Groups[1].Value.Trim() : searchTasks.Groups[1].Success ? searchTasks.Groups[1].Value.Trim() : "";
            if (phrase.Length == 0) return "Podaj frazę, np. „szukaj w zadaniach: raport”. Przeszukuję tytuły zadań (także zrobionych) i przypomnień — tylko odczyt.";
            var (foundTasks, foundReminders) = tasks.Search(phrase);
            if (foundTasks.Count == 0 && foundReminders.Count == 0) return "W zadaniach i przypomnieniach nie ma nic z frazą „" + phrase + "”. Nic nie zmieniałem.";
            var lines = new List<string> { "Znalezione w zadaniach i przypomnieniach dla „" + phrase + "”:" };
            foreach (var found in foundTasks)
                lines.Add("· zadanie: " + found.Title + "  [" + found.Status + "]" + (found.DueAt != null ? "  (termin: " + found.DueAt.Value.ToString("dd.MM.yyyy HH:mm") + ")" : ""));
            foreach (var found in foundReminders)
                lines.Add("· przypomnienie: " + found.Text + "  —  " + found.RemindAt.ToString("dd.MM.yyyy HH:mm") + (found.NotifiedAt != null ? "  [już zadziałało]" : ""));
            lines.Add("To tylko odczyt — zmieniasz zadania w panelu Zadania albo poleceniem „zadanie N zrobione”.");
            return string.Join("\n", lines);
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

        parts.Add(includeRecentHistory ? memory.GetRecentContext(12) : memory.GetStableContext());
        return string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool IsFollowUpQuestion(string text, string followUp, string topic) =>
        !string.IsNullOrWhiteSpace(topic) && Regex.IsMatch(followUp, @"^(?:czy to|czy jest|a |a teraz|dlaczego|czemu|co z tym|ile|jaki procent|procent)") ||
        Regex.IsMatch(text, @"\b(?:wczesniej|przed chwila|tamto|to samo|ten temat|ostatni temat|historia rozmowy)\b");

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

        Interfejs: wpisz „//” w polu czatu — lista poleceń, Tab wybiera, Enter wykonuje. Strona 🧰 Narzędzia to katalog z wyszukiwaniem i polem argumentu. Centrum mieści zakładki: 📓 zadania, 🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka, 🧰 narzędzia.
        Pomiary i system: ile mam RAM · użycie CPU · użycie GPU · dyski · top procesy · czas pracy komputera · która godzina · dzisiejsza data · nazwa komputera · ile rdzeni · architektura · moje ip
        Aplikacje: włącz <nazwa> (cs2, discord, steam, chrome, brave, spotify, notatnik, kalkulator, VS Code, Firefox, VLC, OBS…) · otwórz pobrane / dokumenty / pulpit · skróty
        Diagnostyka: diagnostyka komputera (albo //diag) · eksportuj raport · status zabezpieczeń · zdarzenia windows · programy autostartu · lista usług
        Odczyty stanu: snapshot · snapshoty · porównaj snapshoty · eksportuj porównanie · usuń snapshot N
        Pamięć: zapamiętaj: … · notatka: … · co pamiętasz · pokaż rozmowy · nowa rozmowa · szukaj w rozmowie: fraza · eksportuj rozmowę markdown
        Archiwum: archiwizuj rozmowy · archiwa · usuń archiwum RRRR-MM
        Projekty: nowy projekt: nazwa · projekty · użyj projektu N · aktywny projekt
        Zadania: dodaj zadanie: treść · zrob zadanie: treść · zadania · zadanie N zrobione · szukaj w zadaniach: fraza · przypomnienia · przypomnij mi jutro o 18 o …
        Sentinel: samokontrola · propozycje · lekcje · wersja · co nowego · jak to rozumiem: <polecenie> (podgląd bez wykonania)
        Matematyka: policz 12,5*4 · pierwiastek 144 · silnia 10 · nwd 12 8 · nww 4 6 · czy pierwsza 97 · dzielniki 12 · fibonacci 10 · srednia: 2, 4, 6 · mediana: … · suma: … · min: … · max: … · zaokraglij 3,14159 do 2 · zmiana z 50 do 80 · procent 15 z 240 · ile to procent 30 z 240 · vat 100
        Konwersje: przelicz 5 km na mile · rgb 31 162 195 · kolor 1fa2c3 · rzymskie 2026 · z rzymskich XIV · base64: tekst · dekoduj base64: … · morse: sos · dekoduj morse: … · binarnie: A · dekoduj binarnie: … · hex: Ala · dekoduj hex: …
        Warsztat: porownaj teksty: A ||| B · regex: wzorzec ||| tekst · sha256 pliku: ścieżka · wyciagnij: tekst · posortuj linie: … · unikalne linie: … · kwota slownie: 1234,56 · sekundy: 3661 · na sekundy: 2h 15m · moc hasla: … · qr: tekst · qr wifi: nazwa|hasło
        Kuźnia: nazwa zmiennej: liczba użytkowników · url zakoduj: … · url odkoduj: … · unix: 1700000000 · na unix: 14.11.2023 22:13 utc · czestosc slow: … · rata kredytu: 300000 25 7,5 · porownaj wersje: 1.2.10 ||| 1.10.0 · numeruj linie: a | b · odwroc linie: a | b · popraw odstepy: …
        Tekst: ile slow: tekst · ile znakow: tekst · ile zdan: tekst · palindrom: kajak · anagram: kot, tok · rot13: ala · tytul: ala ma kota · wielkie litery: … · male litery: … · odwroc tekst: … · slug: tekst · transliteruj: tekst · json: {…} · hash tekstu: …
        Kalendarz: ile dni do 24.12 · jaki dzien tygodnia 1.1.2030 · tydzien roku · dzien roku · ile dni do konca roku · wiek: 01.01.1990 · dni robocze 1.1.2024 do 31.1.2024 · wielkanoc 2027 · czas w toki / londyn / berlin / paryz / nowy jork / chicago / los angeles / seoul
        Dokumenty PL: pesel: 11 cyfr · nip: 10 cyfr · iban: PL61… (walidacja lokalna, nic nie jest wysyłane)
        Losowe: losuj 1-100 · rzuc kostka · rzut moneta · lotto · pin 6 · haslo 20 · uuid · wybierz losowo: a, b · bmi 80 180
        Podsumowania: plan dnia · szukaj wszystkiego: fraza · statystyki · backup
        Czat: ponów (przycisk „Ponów”) · zatrzymaj generowanie (przycisk widoczny zawsze)

        Rozumienie: literówki i skróty są poprawiane i zawsze pokazuję, co zrozumiałem. Gdy nie jestem pewien — pytam zamiast zgadywać („Czy chodziło Ci o…”). Nie zgaduję poleceń niszczących — usuwanie wymaga świadomego kliknięcia lub osobnej zgody.
        """;

    private string SnapshotStorage(string success) => snapshots?.LastStorageError == null ? success : snapshots.LastStorageError;
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    private string StorageResult(string success) => memory.LastStorageError == null ? success : memory.LastStorageError;
    internal static string Number(double value, string unit, int decimals = 0) => double.IsFinite(value) && value >= 0 ? value.ToString("F" + decimals, CultureInfo.GetCultureInfo("pl-PL")) + (unit == "%" ? "" : " ") + unit : "odczyt niedostępny";
    internal static string Normalize(string text) => ConversationMemoryService.Normalize(text);
}
