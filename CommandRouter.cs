using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

public sealed class CommandRouter
{
    private readonly SystemMonitor systemMonitor;
    private readonly SystemInfoService systemInfo;
    private readonly LocalAiService localAi;
    private readonly ConversationMemoryService memory;
    private readonly ProjectService? projects;
    private readonly TaskService? tasks;
    private string lastTopic = "";
    private DateTime lastTopicTime;
    private (string Text, DateTime When, string Description, DateTime Expires)? pendingReminder;

    public CommandRouter(SystemMonitor systemMonitor, SystemInfoService systemInfo, LocalAiService localAi, ConversationMemoryService memory, ProjectService? projects = null, TaskService? tasks = null)
    { this.systemMonitor = systemMonitor; this.systemInfo = systemInfo; this.localAi = localAi; this.memory = memory; this.projects = projects; this.tasks = tasks; }

    public async Task<string> ProcessAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command)) return "";
        string text = Normalize(command).TrimEnd('?', '!', '.', ' ');
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
        return await localAi.AskAsync(command, BuildSystemContext(topicForContext, includeSystemFacts, includeRecentHistory), cancellationToken);
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
                return "Nie rozpoznano terminu. Napisz np.: „przypomnij mi jutro o 18 o telefonie do mamy”, „przypomnij za 30 minut o herbacie” albo „przypomnij w piątek o 15 o raporcie”.";
            pendingReminder = (reminderText, when, description, DateTime.Now.AddMinutes(5));
            return $"Rozumiem: przypomnienie „{reminderText}” na {description} (czas lokalny). Zapiszę to dopiero po Twojej zgodzie — odpowiedz „tak” albo „nie”.";
        }
        var addTask = Regex.Match(command, @"^dodaj zadanie[:\s]\s*(.+)$", RegexOptions.IgnoreCase);
        if (addTask.Success)
        {
            string title = addTask.Groups[1].Value.Trim();
            DateTime? due = null; string dueNote = "";
            if (TrySplitReminderTime(title, out string cleanTitle, out DateTime parsedDue, out string dueDescription) && cleanTitle.Length > 0)
            { title = cleanTitle; due = parsedDue; dueNote = $" z terminem {dueDescription} (czas lokalny)"; }
            string projectId = projects?.ActiveProjectId ?? "";
            return tasks.AddTask(title, TaskRecord.PriorityNormal, due, projectId) != null
                ? StorageResult("Zadanie zapisane" + dueNote + ". Zarządzasz nim w panelu Zadania.")
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

    /// <summary>Splits „telefon do mamy jutro o 18" into text + a parsed time phrase; the phrase must sit at the very end.</summary>
    private static bool TrySplitReminderTime(string remainder, out string text, out DateTime when, out string description)
    {
        text = ""; when = default; description = "";
        var match = Regex.Match(remainder, @"^(?<body>.+?)\s+(?<time>za \d+ (?:min\w+|godzin\w*|dni|dzień)|za pół godziny|pojutrze(?: o \d{1,2}(?::\d{2})?)?|jutro(?: o \d{1,2}(?::\d{2})?)?|dzi[śs](?:iaj)? o \d{1,2}(?::\d{2})?|w (?:poniedziałek|wtorek|środ[ęe]|czwartek|piątek|sobot[ęe]|niedziel[ęea])(?: o \d{1,2}(?::\d{2})?)?|(?:na )?\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?(?: o \d{1,2}(?::\d{2})?)?|o \d{1,2}(?::\d{2})?|\d{1,2}:\d{2})$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        if (!PolishTimeParser.TryParse(match.Groups["time"].Value, DateTime.Now, out DateTime parsed, out description)) return false;
        if (parsed <= DateTime.Now) return false;
        text = match.Groups["body"].Value.Trim().TrimEnd(',', '.', '!');
        if (text.Length == 0) return false;
        when = parsed;
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
        if (text.StartsWith("zapamietaj ", StringComparison.Ordinal) || text.StartsWith("zapamietaj:", StringComparison.Ordinal))
        {
            string noteText = command[(command.IndexOf(' ') + 1)..];
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
    private string StorageResult(string success) => memory.LastStorageError == null ? success : memory.LastStorageError;
    internal static string Number(double value, string unit, int decimals = 0) => double.IsFinite(value) && value >= 0 ? value.ToString("F" + decimals, CultureInfo.GetCultureInfo("pl-PL")) + (unit == "%" ? "" : " ") + unit : "odczyt niedostępny";
    internal static string Normalize(string text) => ConversationMemoryService.Normalize(text);
}
