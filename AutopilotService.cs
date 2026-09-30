using System.Text;
using SentinelX.Services.History;
using SentinelX.Services.Network;

namespace SentinelX;

/// <summary>Core command adapter for bounded, read-only Autopilot workflows and explicit Watcher requests.</summary>
public sealed class AutopilotService : IDisposable
{
    private readonly PerformanceHistoryService performance;
    private readonly ProcessToolService processes;
    private readonly INetworkService network;
    private readonly ActionTaskRegistry tasks;
    private readonly GoalMemoryService goals;
    private readonly EventMemoryService eventMemory;
    private readonly WatcherService watchers;
    private readonly ConversationMemoryService memory;
    private readonly AutomationRuleService? automationRules;

    public event Action<string>? NoticeRaised
    {
        add { watchers.NoticeRaised += value; if (automationRules != null) automationRules.NoticeRaised += value; }
        remove { watchers.NoticeRaised -= value; if (automationRules != null) automationRules.NoticeRaised -= value; }
    }
    public event Action<string>? ProgressRaised;

    public AutopilotService(PerformanceHistoryService performance, ProcessToolService processes, INetworkService network,
        ActionTaskRegistry tasks, GoalMemoryService goals, EventMemoryService eventMemory, WatcherService watchers,
        ConversationMemoryService memory, AutomationRuleService? automationRules = null)
    {
        this.performance = performance;
        this.processes = processes;
        this.network = network;
        this.tasks = tasks;
        this.goals = goals;
        this.eventMemory = eventMemory;
        this.watchers = watchers;
        this.memory = memory;
        this.automationRules = automationRules;
        memory.Changed += MemoryChanged;
        if (!memory.CanPersistAutopilotGoals) goals.DisableActivePersistence();
    }

    private void MemoryChanged()
    {
        if (!memory.CanPersistAutopilotGoals) goals.DisableActivePersistence();
    }

    public void Dispose()
    {
        memory.Changed -= MemoryChanged;
        automationRules?.Dispose();
        ProgressRaised = null;
    }

    public async Task<string?> TryProcessAsync(string command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        string query = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?', ',');
        string? automationResponse = automationRules?.TryProcess(command);
        if (automationResponse != null)
        {
            bool accepted = !automationResponse.StartsWith("Nie", StringComparison.Ordinal) &&
                !automationResponse.StartsWith("Obsługiwane", StringComparison.Ordinal);
            Record(command, "AUTOMATION_RULE", accepted, automationResponse, (automationRules?.GetRules().Count ?? 0) + " bounded local rule(s)");
            return automationResponse;
        }
        if (query is "wznow ostatni cel" or "kontynuuj przerwane zadanie" or "wznow diagnostyke")
            return await ResumeSafeInterruptedGoalAsync(command, token).ConfigureAwait(false);
        if (query is "status autopilota" or "status celu" or "postep autopilota" or "co z moim celem" or "pokaz cel autopilota")
        {
            string status = goals.FormatLatest() + "\n\n" + eventMemory.FormatStatus() + "\n\nOBSERWATORY:\n" + watchers.GetWatchStatus();
            Record(command, "AUTOPILOT_STATUS", true, "Odczytano ostatni lokalny checkpoint i aktywne obserwatory.", status);
            return status;
        }
        if (query is "pamiec zdarzen" or "status pamieci zdarzen" or "event memory")
        {
            string status = eventMemory.FormatStatus();
            Record(command, "EVENT_MEMORY_STATUS", true, "Odczytano krótką pamięć korelacji.", status);
            return status;
        }
        if (query is "wyczysc pamiec zdarzen" or "usun pamiec zdarzen")
        {
            eventMemory.Clear();
            string status = eventMemory.FormatStatus();
            Record(command, "EVENT_MEMORY_CLEAR", true, "Wyczyszczono RAM-owe markery zdarzeń.", status);
            return "Wyczyszczono lokalną Event Memory. Dane były tylko w RAM i nie były zapisywane na dysk.";
        }
        if (query is "status obserwatorow" or "status watcherow" or "co monitorujesz" or "co obserwujesz")
        {
            string status = watchers.GetWatchStatus();
            Record(command, "WATCHER_STATUS", true, "Odczytano lokalny stan obserwatorów.", status);
            return status;
        }
        if (IsDownloadWatch(query))
        {
            string? target = ExtractDownloadTarget(command);
            string response = watchers.BeginDownloadWatch(target, persist: memory.CanPersistAutopilotGoals);
            bool started = response.StartsWith("Autopilot czeka", StringComparison.Ordinal) || response.StartsWith("Już czekam", StringComparison.Ordinal);
            Record(command, "WATCH_DOWNLOAD", started, response, watchers.GetWatchStatus());
            return response;
        }
        if (query is "monitoruj wydajnosc gry" or "obserwuj wydajnosc gry" or "monitoruj gre" or "obserwuj gre")
        {
            string response = watchers.BeginPerformanceWatch();
            Record(command, "WATCH_PERFORMANCE", watchers.IsPerformanceWatchActive, response, watchers.GetWatchStatus());
            return response;
        }
        if (query is "przestan monitorowac wydajnosc gry" or "zatrzymaj obserwacje wydajnosci" or "wylacz obserwacje gry")
        {
            watchers.StopPerformanceWatch();
            bool stopped = !watchers.IsPerformanceWatchActive;
            string response = "Zatrzymałem ręcznie uruchomioną obserwację wydajności. Ogólne alerty Watch pozostają aktywne, jeśli włączono je w Ustawieniach. Krótki bufor Black Box nadal działa w RAM podczas pracy aplikacji.";
            Record(command, "STOP_WATCH_PERFORMANCE", stopped, response, watchers.GetWatchStatus());
            return response;
        }
        if (query is "monitoruj czy cs2 sie zamknie" or "obserwuj czy cs2 sie zamknie" or "monitoruj awarie cs2" or "obserwuj awarie cs2")
        {
            string response = watchers.BeginCs2ExitWatch();
            Record(command, "WATCH_CS2_EXIT", watchers.IsCs2ExitWatchActive, response, watchers.GetWatchStatus());
            return response;
        }
        if (query is "przestan monitorowac cs2" or "zatrzymaj obserwacje cs2")
        {
            watchers.StopCs2ExitWatch();
            bool stopped = !watchers.IsCs2ExitWatchActive;
            string response = "Zatrzymałem obserwację zakończenia procesu CS2.";
            Record(command, "STOP_WATCH_CS2", stopped, response, watchers.GetWatchStatus());
            return response;
        }
        if ((query.Contains("monitoruj", StringComparison.Ordinal) || query.Contains("obserwuj", StringComparison.Ordinal)) &&
            query.Contains("temperatur", StringComparison.Ordinal))
        {
            const string response = "Nie uruchomiłem obserwatora temperatur: w tej konfiguracji brak dostawcy czujników CPU/GPU.";
            RecordStatus(command, "WATCH_TEMPERATURE_UNAVAILABLE", "UNVERIFIED", response, "Brak providera temperatur.");
            return response;
        }
        if ((query.Contains("monitoruj", StringComparison.Ordinal) || query.Contains("obserwuj", StringComparison.Ordinal)) &&
            (query.Contains("packet loss", StringComparison.Ordinal) || query.Contains("utrate pakietow", StringComparison.Ordinal) || query.Contains("serwer gry", StringComparison.Ordinal)))
        {
            const string response = "Nie uruchomiłem monitoringu utraty pakietów do serwera gry: nie mam adresu serwera ani pomiaru trasy/strat. Dostępny test ICMP/DNS/HTTPS jest punktowy i nie mierzy jakości serwera gry.";
            RecordStatus(command, "WATCH_PACKET_LOSS_UNAVAILABLE", "UNVERIFIED", response, "Dostępny tester nie mierzy packet loss do serwera gry.");
            return response;
        }
        if (IsNetworkGoal(query))
            return await RunNetworkWorkflowCoreAsync(command, token).ConfigureAwait(false);
        if (IsPerformanceGoal(query))
            return await RunPerformanceWorkflowCoreAsync(command, token).ConfigureAwait(false);
        return null;
    }

    private async Task<string> ResumeSafeInterruptedGoalAsync(string command, CancellationToken token)
    {
        var interrupted = goals.GetRecent(20).FirstOrDefault(x => x.Status == "INTERRUPTED");
        if (interrupted == null)
            return "Nie ma przerwanego, zapisanego celu do wznowienia. Mogę uruchomić nową diagnostykę, jeśli opiszesz objaw.";

        string priorGoal = ConversationMemoryService.Normalize(interrupted.Goal);
        string rerunCommand;
        if (priorGoal.Contains("opoznien internetu", StringComparison.Ordinal) || priorGoal.Contains("internet lag", StringComparison.Ordinal))
            rerunCommand = "Sprawdź opóźnienia Internetu";
        else if (priorGoal.Contains("wydajnosci", StringComparison.Ordinal) || priorGoal.Contains("cs2", StringComparison.Ordinal))
            rerunCommand = interrupted.Goal;
        else
            return $"Odnaleziono przerwany cel „{interrupted.Goal}”, ale nie ma on bezpiecznego automatycznego wznowienia. Nie uruchomiłem ponownie żadnych działań; podaj decyzję, jak kontynuować.";

        string result = priorGoal.Contains("opoznien internetu", StringComparison.Ordinal) || priorGoal.Contains("internet lag", StringComparison.Ordinal)
            ? await RunNetworkWorkflowCoreAsync(rerunCommand, token).ConfigureAwait(false)
            : await RunPerformanceWorkflowCoreAsync(rerunCommand, token).ConfigureAwait(false);
        return $"Odnaleziono przerwany cel „{interrupted.Goal}”. Ponowiłem bezpieczną diagnostykę od początku — to nie jest dokładne wznowienie poprzedniego kroku.\n\n{result}";
    }

    private async Task<string> RunNetworkWorkflowCoreAsync(string originalCommand, CancellationToken token)
    {
        var plan = new[] { "recent_computer_context", "connectivity_probe", "process_snapshot", "bounded_report" };
        var dependencyGraph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["connectivity_probe"] = ["recent_computer_context"],
            ["process_snapshot"] = ["connectivity_probe"],
            ["bounded_report"] = ["process_snapshot"]
        };
        var goal = goals.Start("Sprawdzenie opóźnień Internetu (tylko odczyt)", plan,
            persist: memory.CanPersistAutopilotGoals, dependencies: dependencyGraph);
        using var task = tasks.Begin(goal.Id, goal.Goal, plan.Length, token);
        token = task.Token;
        var response = new StringBuilder("Sprawdzam objaw bezpiecznymi odczytami.\n");
        bool anyFailure = false, anyUnverified = false;
        try
        {
            performance.Start();
            await RunStep("recent_computer_context", "Ostatni kontekst komputera", "Punktowe testy łączności", async () =>
            {
                token.ThrowIfCancellationRequested();
                string context = performance.FormatRecentWindow(TimeSpan.FromMinutes(3));
                response.AppendLine("\nKontekst z krótkiego Black Box (RAM):\n" + context);
                Record(originalCommand, "BLACK_BOX_NETWORK_CONTEXT", true, "Odczytano lokalny bufor bez eksportu na dysk.", context);
                await Task.CompletedTask;
                return ("VERIFIED", "Odczytano bufor bieżącej sesji.");
            });

            await RunStep("connectivity_probe", "Ping, DNS i HTTPS", "Bieżąca lista procesów", async () =>
            {
                token.ThrowIfCancellationRequested();
                ActionExecutionResult result = await network.TestInternetAsync(token).ConfigureAwait(false);
                string retryEvidence = "";
                if (!result.Success && !result.Message.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase))
                {
                    string first = result.Message + (result.Evidence.Length == 0 ? "" : "\n" + result.Evidence);
                    await Task.Delay(300, token).ConfigureAwait(false);
                    var retry = await network.TestInternetAsync(token).ConfigureAwait(false);
                    retryEvidence = "Pierwszy pomiar nie potwierdził łączności; wykonano jedno ponowienie.\n" + first + "\n";
                    if (retry.Success || retry.Verified) result = retry;
                    else retryEvidence += "Ponowienie: " + retry.Message + "\n" + retry.Evidence;
                }
                string evidence = retryEvidence + result.Message + (result.Evidence.Length == 0 ? "" : "\n" + result.Evidence);
                response.AppendLine("\nPunktowe testy (ICMP 1.1.1.1, DNS i HTTPS):\n" + evidence);
                response.AppendLine("Nie testowałem trasy ani utraty pakietów do serwera gry.");
                RecordStatus(originalCommand, "NETWORK_PROBE", result.Status, result.Message, evidence);
                string stepStatus = result.Message.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase)
                    ? "UNVERIFIED" : result.Status;
                if (stepStatus is "FAILED" or "CANCELLED") anyFailure = true;
                else if (stepStatus != "VERIFIED") anyUnverified = true;
                return (stepStatus, result.Message);
            });

            await RunStep("process_snapshot", "Procesy według zajętości RAM", "Raport z ograniczeniami", async () =>
            {
                token.ThrowIfCancellationRequested();
                string result = await ReadProcessesWithOneRetryAsync(token).ConfigureAwait(false);
                response.AppendLine("\nProcesy według Working Set (to nie jest pomiar transferu sieciowego):\n" + result);
                Record(originalCommand, "PROCESS_MEMORY_READ", true, "Pobrano migawkę procesów; bez twierdzeń o użyciu sieci przez aplikacje.", result);
                return ("VERIFIED", "Pobrano migawkę procesów według RAM.");
            });

            await RunStep("bounded_report", "Wniosek", "Gotowe", async () =>
            {
                response.AppendLine("\nWniosek: sprawdziłem dostępne punkty łączności, krótki kontekst CPU/RAM/GPU i listę procesów. Sentinel w tej konfiguracji nie mierzy per-app transferu, Wi-Fi/interferencji ani packet loss/trasy do serwera gry, więc nie przypisuję przyczyny na podstawie samej korelacji. Nie zmieniłem ustawień.");
                Record(originalCommand, "NETWORK_DIAGNOSTIC_REPORT", true, "Raport tylko do odczytu z jawnymi ograniczeniami.", "");
                await Task.CompletedTask;
                return ("VERIFIED", "Zebrano dostępne pomiary i ograniczenia; niczego nie zmieniono.");
            });

            string status = anyFailure ? "FAILED" : anyUnverified ? "UNVERIFIED" : "VERIFIED";
            goals.Finish(goal.Id, status, "Sprawdzenie zakończone; pełny wynik jest w bieżącej odpowiedzi.", "Jeśli lag występuje tylko w grze, podaj nazwę gry lub serwer, aby zawęzić pomiar.");
            task.Complete(status);
            RaiseProgress("");
            response.AppendLine("\nCel: " + status + " · „" + goal.Goal + "”. Nie wykonano zmian systemowych.");
            if (!memory.CanPersistAutopilotGoals)
                response.AppendLine("Checkpoint pozostaje w RAM — trwały zapis celów jest wyłączony.");
            return response.ToString();
        }
        catch (OperationCanceledException)
        {
            goals.Finish(goal.Id, "CANCELLED", "Przerwano dalsze odczyty.", "Uruchom sprawdzenie ponownie, gdy będzie potrzebne.");
            task.Complete("CANCELLED");
            RaiseProgress("");
            return response.ToString() + "\n\nSprawdzanie przerwano; wcześniejsze odczyty nie zostały cofnięte i niczego nie zmieniono.";
        }
        catch (Exception ex)
        {
            goals.Finish(goal.Id, "FAILED", "Sprawdzenie przerwane: " + ex.Message, "Sprawdź status celu przed ponowieniem.");
            task.Complete("FAILED");
            RaiseProgress("");
            Record(originalCommand, "NETWORK_DIAGNOSTIC_WORKFLOW", false, ex.Message, ex.ToString());
            return response.ToString() + "\n\nNie udało się dokończyć odczytów: " + ex.Message + ". Nie zmieniono ustawień.";
        }

        async Task RunStep(string name, string label, string next, Func<Task<(string Status, string Result)>> action)
        {
            token.ThrowIfCancellationRequested();
            task.SetStep(label);
            RaiseProgress(label);
            goals.SetStep(goal.Id, name, "RUNNING", "", next);
            try
            {
                var result = await action().ConfigureAwait(false);
                if (result.Status is "FAILED" or "CANCELLED") anyFailure = true;
                else if (result.Status != "VERIFIED") anyUnverified = true;
                goals.SetStep(goal.Id, name, result.Status, result.Result, next);
                task.CompleteStep();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                anyFailure = true;
                string message = label + " niedostępne: " + ex.Message;
                response.AppendLine("\n" + message);
                goals.SetStep(goal.Id, name, "FAILED", "Narzędzie niedostępne; kontynuuję bez tego dowodu.", next);
                Record(originalCommand, "EXECUTIVE_STEP", false, message, ex.ToString());
                task.CompleteStep();
            }
        }
    }

    private async Task<string> RunPerformanceWorkflowCoreAsync(string originalCommand, CancellationToken token)
    {
        var plan = new[] { "black_box_lookback", "current_metrics", "process_memory", "connectivity_probe", "cautious_report" };
        string normalizedGoal = ConversationMemoryService.Normalize(originalCommand);
        bool cs2Goal = normalizedGoal.Contains("cs2", StringComparison.Ordinal);
        bool repairRequested = normalizedGoal.Contains("napraw", StringComparison.Ordinal) || normalizedGoal.Contains("ogarnij", StringComparison.Ordinal);
        string goalTitle = cs2Goal ? "Diagnoza CS2 (tylko odczyt)" : "Diagnostyka wydajności komputera (tylko odczyt)";
        var dependencyGraph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["current_metrics"] = ["black_box_lookback"],
            ["process_memory"] = ["current_metrics"],
            ["connectivity_probe"] = ["current_metrics"],
            ["cautious_report"] = ["process_memory", "connectivity_probe"]
        };
        var goal = goals.Start(goalTitle, plan, persist: memory.CanPersistAutopilotGoals, dependencies: dependencyGraph);
        using var task = tasks.Begin(goal.Id, goal.Goal, plan.Length, token);
        token = task.Token; // Registry cancellation must stop the workflow's actual read-only probes.
        var response = new StringBuilder("AUTOPILOT · diagnostyka tylko do odczytu\n");
        bool anyFailure = false, anyUnverified = false;
        try
        {
            token.ThrowIfCancellationRequested();
            performance.Start();
            await RunStep("black_box_lookback", "Historia z Black Box", "Bieżąca metryka", async () =>
            {
                var history = performance.GetRecent(TimeSpan.FromSeconds(90));
                string text = performance.FormatIncident(TimeSpan.FromSeconds(90));
                string game = history.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.RunningGame))?.RunningGame ?? "";
                string eventSummary = eventMemory.RecordPerformanceCheck(game, history);
                response.AppendLine("\n1. Ostatnie 90 s (RAM):\n" + text + "\n" + eventSummary);
                Record(originalCommand, "BLACK_BOX_READ", true, "Odczytano " + history.Count + " próbek; bez eksportu na dysk.", text + "\n" + eventSummary);
                await Task.CompletedTask;
                return ("VERIFIED", $"Odczytano {history.Count} próbek z bufora RAM.");
            });

            token.ThrowIfCancellationRequested();
            await RunStep("current_metrics", "Bieżące CPU/RAM/GPU", "Procesy o największym Working Set", async () =>
            {
                PerformanceSample sample;
                bool fresh = true;
                try { sample = performance.SampleIfDue(); }
                catch (Exception first)
                {
                    token.ThrowIfCancellationRequested();
                    var fallback = performance.GetRecent(TimeSpan.FromMinutes(6)).LastOrDefault();
                    if (fallback == null) throw new InvalidOperationException("Odczyt metryk nie powiódł się i w Black Box nie ma próbki zapasowej.", first);
                    sample = fallback;
                    fresh = false;
                    response.AppendLine("\nOdczyt bieżącej próbki nie powiódł się; używam ostatniej historycznej próbki, nie bieżącego pomiaru.");
                }
                string metrics = $"{sample.CapturedAt:HH:mm:ss} · CPU {Format(sample.CpuPercent)} · RAM {Format(sample.RamPercent)} ({FormatGb(sample.UsedRamGb)}/{FormatGb(sample.TotalRamGb)} GiB) · GPU {Format(sample.GpuPercent)} · gra: {(sample.RunningGame.Length == 0 ? "nie wykryto" : sample.RunningGame)}";
                response.AppendLine("\n2. Metryki: " + metrics);
                RecordStatus(originalCommand, "PERFORMANCE_SAMPLE", fresh ? "VERIFIED" : "UNVERIFIED", fresh ? "Odczyt lokalnych liczników Windows." : "Użyto ostatniej próbki historycznej.", metrics);
                await Task.CompletedTask;
                return (fresh ? "VERIFIED" : "UNVERIFIED", (fresh ? "Pobrano jedną próbkę; " : "Próbka może być nieaktualna; ") + metrics);
            });

            token.ThrowIfCancellationRequested();
            string processStepReport = "", networkStepReport = "";
            // These two independent, read-only probes run concurrently; no mutation step is parallelized.
            await Task.WhenAll(
                RunStep("process_memory", "Procesy według RAM", "Raport ograniczeń", async () =>
                {
                    string text = await ReadProcessesWithOneRetryAsync(token).ConfigureAwait(false);
                    processStepReport = "\n3. Procesy (Working Set, nie CPU/GPU):\n" + text;
                    Record(originalCommand, "PROCESS_MEMORY_READ", true, "Pobrano bieżącą listę Working Set.", text);
                    return ("VERIFIED", "Odczytano listę procesów; szczegóły tylko w odpowiedzi, nie w checkpointcie.");
                }),
                RunStep("connectivity_probe", "Punktowe testy Internetu", "Raport ograniczeń", async () =>
                {
                ActionExecutionResult result;
                string firstEvidence = "";
                bool alreadyRetried = false;
                try { result = await network.TestInternetAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception first)
                {
                    alreadyRetried = true;
                    await Task.Delay(400, token).ConfigureAwait(false);
                    try { result = await network.TestInternetAsync(token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception second)
                    {
                        anyFailure = true;
                        string message = "Test łączności nie powiódł się po jednym bezpiecznym ponowieniu: " + second.Message;
                        networkStepReport = "\n4. Sieć: " + message;
                        Record(originalCommand, "NETWORK_PROBE", false, message, first.Message + " | " + second.Message);
                        return ("FAILED", message);
                    }
                    firstEvidence = "Pierwszy odczyt zgłosił wyjątek: " + first.Message + "\n";
                }
                if (!result.Success && !alreadyRetried)
                {
                    alreadyRetried = true;
                    string initial = result.Message + (string.IsNullOrWhiteSpace(result.Evidence) ? "" : "\n" + result.Evidence);
                    await Task.Delay(400, token).ConfigureAwait(false);
                    try
                    {
                        var retry = await network.TestInternetAsync(token).ConfigureAwait(false);
                        if (retry.Success) { firstEvidence = "Pierwszy test nie potwierdził łączności; ponowienie: " + initial + "\n"; result = retry; }
                        else firstEvidence = "Pierwszy test: " + initial + "\nPonowienie: " + retry.Message + "\n";
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        firstEvidence = "Pierwszy test: " + initial + "\nPonowienie zgłosiło wyjątek: " + ex.Message + "\n";
                    }
                }
                string networkText = firstEvidence + result.Message + (string.IsNullOrWhiteSpace(result.Evidence) ? "" : "\n" + result.Evidence);
                networkStepReport = "\n4. Sieć: " + networkText + "\nTen punktowy test nie mierzy packet loss ani trasy do serwera gry.";
                RecordStatus(originalCommand, "NETWORK_PROBE", result.Status, result.Message, firstEvidence + result.Evidence);
                if (!result.Success) anyFailure = true;
                else if (!result.Verified) anyUnverified = true;
                return (result.Status, result.Message);
                }));
            response.AppendLine(processStepReport);
            response.AppendLine(networkStepReport);

            token.ThrowIfCancellationRequested();
            await RunStep("cautious_report", "Ostrożne wnioski", "Gotowe", async () =>
            {
                response.AppendLine("\nWniosek: zebrane odczyty pokazują współwystępowanie zasobów i objawu, nie dowodzą przyczyny. Test sieci nie jest pomiarem serwera gry. Dostępne są CPU, RAM i licznik GPU Windows; brak temperatur, VRAM, taktowań, FPS, frametime i aktywności dysku. Nie zmieniłem ustawień ani procesów.");
                if (repairRequested)
                    response.AppendLine("Potraktowałem prośbę jako cel naprawy i najpierw wykonałem bezpieczną diagnozę. Nie potwierdzam, że problem naprawiono; dostępne pomiary nie uzasadniają konkretnej zmiany. Nie zastosuję losowej optymalizacji.");
                Record(originalCommand, "AUTOPILOT_REPORT", true, "Raport zawiera ograniczenia pomiarów; bez zmian systemowych.", "");
                await Task.CompletedTask;
                return ("VERIFIED", "Zebrano raport z ograniczeniami i bez zmian systemowych.");
            });

            string status = anyFailure ? "FAILED" : anyUnverified ? "UNVERIFIED" : "VERIFIED";
            goals.Finish(goal.Id, status, "Diagnostyka tylko do odczytu zakończona. Pełne szczegóły są w bieżącej odpowiedzi.", "Porównaj wynik z momentem wystąpienia ścinki; korelacja nie dowodzi przyczyny.");
            task.Complete(status);
            response.AppendLine("\nAutopilot: " + status + " · cel „" + goal.Goal + "” · następny krok: porównanie przy kolejnym objawie.");
            RaiseProgress("");
            if (!memory.CanPersistAutopilotGoals)
                response.AppendLine("Checkpoint celu pozostaje tylko w RAM, ponieważ tryb prywatny lub ustawienia pamięci wyłączają trwały zapis celów.");
            else if (goals.LastStorageError != null)
                response.AppendLine("Uwaga: zapis checkpointu lokalnego nie został potwierdzony: " + goals.LastStorageError);
            return response.ToString();
        }
        catch (OperationCanceledException)
        {
            goals.Finish(goal.Id, "CANCELLED", "Przerwano dalsze kroki; nie wykonano zmian systemowych.", "Uruchom diagnostykę ponownie, gdy będzie potrzebna.");
            task.Complete("CANCELLED");
            RaiseProgress("");
            return response.ToString() + "\n\nAutopilot przerwany. Zebrane wcześniej odczyty nie zostały cofnięte; nie wykonano zmian systemowych.";
        }
        catch (Exception ex)
        {
            anyFailure = true;
            goals.Finish(goal.Id, "FAILED", "Diagnostyka przerwana: " + ex.Message, "Sprawdź status celu przed ponowieniem.");
            task.Complete("FAILED");
            RaiseProgress("");
            Record(originalCommand, "AUTOPILOT_WORKFLOW", false, ex.Message, ex.ToString());
            return response.ToString() + "\n\nAutopilot nie dokończył diagnostyki: " + ex.Message + "\nNie wykonano zmian systemowych.";
        }

        async Task RunStep(string name, string label, string next, Func<Task<(string Status, string Result)>> action)
        {
            token.ThrowIfCancellationRequested();
            task.SetStep(label);
            RaiseProgress(label);
            goals.SetStep(goal.Id, name, "RUNNING", "", next);
            try
            {
                var result = await action().ConfigureAwait(false);
                if (result.Status is "FAILED" or "CANCELLED") anyFailure = true;
                else if (result.Status != "VERIFIED") anyUnverified = true;
                goals.SetStep(goal.Id, name, result.Status, result.Result, next);
                task.CompleteStep();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                anyFailure = true;
                string note = label + " niedostępne: " + ex.Message;
                lock (response) response.AppendLine("\n" + note);
                goals.SetStep(goal.Id, name, "FAILED", "Narzędzie niedostępne; kontynuuję bez tego dowodu.", next);
                Record(originalCommand, "AUTOPILOT_STEP", false, note, ex.ToString());
                task.CompleteStep();
            }
        }
    }

    private async Task<string> ReadProcessesWithOneRetryAsync(CancellationToken token)
    {
        try { token.ThrowIfCancellationRequested(); return processes.GetTopMemoryProcesses(8); }
        catch (Exception first)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(200, token).ConfigureAwait(false);
            try { return processes.GetTopMemoryProcesses(8); }
            catch (Exception second) { throw new InvalidOperationException("Odczyt procesów nie powiódł się po jednym bezpiecznym ponowieniu.", new AggregateException(first, second)); }
        }
    }

    private void RaiseProgress(string step)
    {
        try { ProgressRaised?.Invoke(step); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private static void Record(string command, string type, bool success, string message, string evidence) =>
        RecordStatus(command, type, success ? "VERIFIED" : "FAILED", message, evidence);

    private static void RecordStatus(string command, string type, string status, string message, string evidence)
    {
        ActionEvidenceCapture.Record(new ActionHistoryEntry
        {
            ActionId = "AUTO-" + Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.Now,
            ActionType = type,
            Command = command,
            Status = status,
            Message = message,
            Evidence = evidence
        });
    }

    private static bool IsNetworkGoal(string query) =>
        query is "sprawdz dlaczego internet laguje" or "dlaczego internet laguje" or "internet laguje" or
            "sprawdz dlaczego wifi laguje" or "wifi laguje" or "sprawdz wysoki ping" or "dlaczego mam wysoki ping" or
            "diagnozuj ping" or "sprawdz dlaczego internet jest wolny" or "internet jest wolny" or "wolne wifi" or
            "sprawdz lagi internetu" or "sprawdz polaczenie" ||
        ((query.Contains("internet", StringComparison.Ordinal) || query.Contains("wifi", StringComparison.Ordinal) ||
          query.Contains("ping", StringComparison.Ordinal)) &&
         (query.Contains("lag", StringComparison.Ordinal) || query.Contains("woln", StringComparison.Ordinal) ||
          query.Contains("wysoki", StringComparison.Ordinal) || query.Contains("problem", StringComparison.Ordinal)));

    private static bool IsPerformanceGoal(string query) =>
        query is "sprawdz dlaczego mam scinki" or "sprawdz dlaczego mam scinki w grze" or "cs2 mi scina" or
            "cs2 mi sie zacina" or "gra mi scina" or "gra sie zacina" or "diagnozuj scinki" or "sprawdz scinki" or
            "diagnozuj wydajnosc gry" or "zdiagnozuj wydajnosc gry" or "zdiagnozuj cs2" or "diagnozuj cs2" or
            "zdiagnozuj problem w cs2" or "sprawdz wydajnosc cs2" or "napraw cs2" or "ogarnij cs2" or
            "dlaczego komputer jest wolny" or "komputer jest wolny" or "komputer laguje" or "napraw komputer" or
            "przed chwila mi scielo" or "przed chwila mi scinalo" or "sentinel przed chwila mi scielo" or
            "co sie dzialo przed chwila" or "cs2 sie zawiesil" or "cs2 sie wywalil" or "cs2 sie wylaczyl" or
            "gra sie zawiesila" or "aplikacja przestala odpowiadac" ||
        ((query.Contains("cs2", StringComparison.Ordinal) || query.Contains("komputer", StringComparison.Ordinal) ||
          query.Contains("scink", StringComparison.Ordinal) || query.Contains("lag", StringComparison.Ordinal) ||
          query.Contains("gra", StringComparison.Ordinal) || query.Contains("aplikacja", StringComparison.Ordinal)) &&
         (query.Contains("napraw", StringComparison.Ordinal) || query.Contains("woln", StringComparison.Ordinal) ||
          query.Contains("scin", StringComparison.Ordinal) || query.Contains("zacina", StringComparison.Ordinal) ||
          query.Contains("zawies", StringComparison.Ordinal) || query.Contains("wywal", StringComparison.Ordinal) ||
          query.Contains("crash", StringComparison.Ordinal) || query.Contains("przed chwila", StringComparison.Ordinal)));

    private static bool IsDownloadWatch(string query) =>
        query is "monitoruj pobieranie" or "obserwuj pobieranie" or "powiadom mnie gdy pobieranie sie skonczy" or
            "powiadom mnie po zakonczeniu pobierania" ||
        query.StartsWith("monitoruj pobieranie ", StringComparison.Ordinal) ||
        query.StartsWith("obserwuj pobieranie ", StringComparison.Ordinal);

    private static string? ExtractDownloadTarget(string command)
    {
        string input = (command ?? "").Trim().TrimEnd('.', '!', '?', ',');
        const string monitor = "monitoruj pobieranie ";
        const string observe = "obserwuj pobieranie ";
        string target = input.StartsWith(monitor, StringComparison.OrdinalIgnoreCase) ? input[monitor.Length..].Trim()
            : input.StartsWith(observe, StringComparison.OrdinalIgnoreCase) ? input[observe.Length..].Trim() : "";
        return target.Length == 0 ? null : target;
    }

    private static string Format(double value) => double.IsFinite(value)
        ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "niedostępne";
    private static string FormatGb(double value) => double.IsFinite(value)
        ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "niedostępne";
}
