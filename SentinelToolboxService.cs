using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using SentinelX.Services.PhoneCall;
using SentinelX.Services.Web;

namespace SentinelX
{
    public sealed class SentinelToolboxService
    {
        private readonly PcDiagnosticService diagnostics;
        private readonly DiagnosticsTool diagnosticsTool;
        private readonly GameFocusModeService gameFocusMode;
        private readonly ActionTaskRegistry tasks;
        private string lastVerifiedApplication = "";
        private DateTime lastVerifiedApplicationAt;
        public IReadOnlyList<ActionTaskSnapshot> GetTasks() => tasks.GetTasks();
        public int CancelAllTasks() => tasks.CancelAll();
        public string CancelActiveTask(bool all = false, string? taskId = null)
        {
            var active = tasks.GetTasks().Where(x => x.Status is "RUNNING" or "CANCELLING").ToArray();
            if (active.Length == 0) return "Nie ma aktywnego zadania do przerwania.";
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                var selected = active.FirstOrDefault(x => x.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
                if (selected == null) return "Nie znalazłem aktywnego zadania o tym identyfikatorze.";
                if (!tasks.Cancel(selected.Id)) return "To zadanie zakończyło się, zanim odebrało żądanie przerwania.";
                return $"Wysłano żądanie bezpiecznego przerwania zadania {selected.Id} („{selected.Description}”).";
            }
            if (all)
            {
                int cancelled = tasks.CancelAll();
                return cancelled == 0 ? "Nie było aktywnego zadania do przerwania." : $"Wysłano żądanie bezpiecznego przerwania {cancelled} aktywnym zadaniom.";
            }
            if (active.Length > 1)
                return "Mam kilka aktywnych zadań. Które przerwać?\n" + string.Join("\n", active.Select(x => $"· {x.Id}: {x.Description} [{x.CurrentStep}]")) + "\nMożesz też powiedzieć „anuluj wszystko”.";
            if (!tasks.Cancel(active[0].Id)) return "To zadanie zakończyło się, zanim odebrało żądanie przerwania.";
            return $"Wysłano żądanie bezpiecznego przerwania zadania {active[0].Id} („{active[0].Description}”).";
        }
        public string GetTaskSummary() => tasks.GetTasks().Count == 0 ? "Brak zadań w tej sesji." : string.Join("\n\n", tasks.GetTasks().Select(t => $"{t.Id} [{t.Status}] {t.CompletedSteps}/{t.TotalSteps}\n{t.Description}\n{t.CurrentStep}"));
        public bool HasPendingAction => permissionCenter.HasPendingAction;
        public string PendingSummary => permissionCenter.GetPendingSummary();

        /// <summary>Short-lived, in-process task context for resolving follow-ups; durable memories are retrieved separately by query.</summary>
        public string GetWorkingContext()
        {
            var parts = new List<string>();
            if (permissionCenter.HasPendingAction)
            {
                string pending = SensitiveDataRedactor.Redact(permissionCenter.GetPendingSummary());
                if (pending.Length > 500) pending = pending[..497] + "…";
                parts.Add("Czeka na decyzję użytkownika: " + pending);
            }
            foreach (var task in tasks.GetTasks().Where(x => x.Status is "RUNNING" or "CANCELLING").Take(3))
            {
                string description = SensitiveDataRedactor.Redact(task.Description);
                string step = SensitiveDataRedactor.Redact(task.CurrentStep);
                parts.Add($"Aktywne zadanie {task.Id} [{task.Status}], krok {task.CompletedSteps}/{task.TotalSteps}: {description}" +
                    (step.Length == 0 ? "" : $"; bieżący krok: {step}"));
            }
            if (lastVerifiedApplication.Length > 0 && DateTime.Now - lastVerifiedApplicationAt <= TimeSpan.FromMinutes(20))
                parts.Add("Ostatnia potwierdzona aplikacja uruchomiona w tej sesji: " + lastVerifiedApplication + ".");
            if (conversationMemory.CanUseHistoryForAi)
            {
                var recent = actionHistory.GetRecentEntries(20)
                    .Where(x => x.SessionId == ActionHistoryService.CurrentSessionId && x.ActionType != "REQUEST" &&
                        x.Status != "RUNNING" && DateTime.Now - x.Timestamp <= TimeSpan.FromMinutes(20))
                    .OrderByDescending(x => x.Timestamp).Take(4).Reverse();
                foreach (var entry in recent)
                {
                    string message = SensitiveDataRedactor.Redact(entry.Message ?? "");
                    if (message.Length > 300) message = message[..297] + "…";
                    if (message.Length > 0)
                        parts.Add($"Ostatni wynik narzędzia [{entry.ActionType}/{entry.Status}]: {message}");
                }
            }
            return string.Join("\n", parts);
        }

        public string TakeAutomaticMemoryNotice() => conversationMemory.TakeAutomaticMemoryNotice();
        private readonly CalculatorTool calculator = new();
        private readonly DeviceControlTool? deviceControl;
        private readonly ActionHistoryService actionHistory;
        private readonly Services.Memory.MemoryActionService memoryActions;
        private readonly ConversationMemoryService conversationMemory;
        private readonly Services.History.HistoryExportService historyExport;

        private readonly Services.Permissions.IPermissionService permissionCenter;

        private readonly Services.Apps.IAppLauncherService appLauncher;

        private readonly ProcessToolService processTools;

        private readonly Services.Network.INetworkService networkTools;
        private readonly PhoneCallTool phoneCalls;
        private readonly AudioControlTool audioControl;
        private readonly WebResearchTool webResearch;
        private readonly SkillLearningService skillLearning;
        private readonly DesktopAutomationTool desktopAutomation;
        private readonly ClipboardContextTool clipboardContext;
        private readonly ProgramBuilderService programBuilder;
        private readonly SentinelX.Services.Readiness.IReadinessService? readiness;
        private readonly AutopilotService? autopilot;


        public SentinelToolboxService(Func<string>? browserPreference = null, ActionHistoryService? history = null,
            Services.Permissions.IPermissionService? permissions = null, Services.Apps.IAppLauncherService? launcher = null,
            ProcessToolService? processes = null, Services.Network.INetworkService? network = null,
            PcDiagnosticService? diagnostics = null, ActionTaskRegistry? tasks = null, ConversationMemoryService? memory = null,
            Services.History.HistoryExportService? historyExport = null, Services.Memory.MemoryActionService? memoryActionService = null,
            PhoneCallTool? phoneCallTool = null, WebResearchTool? webResearchTool = null,
            SystemMonitor? systemMonitor = null, PerformanceHistoryService? performanceHistory = null,
            GameFocusModeService? gameFocusMode = null, DesktopAutomationTool? desktopAutomationTool = null,
            AutopilotService? autopilotService = null,
            Func<string, string, System.Threading.CancellationToken, Task<string>>? localAiAsk = null,
            Func<bool>? localAiLastResponseSucceeded = null, ClipboardContextTool? clipboardContextTool = null,
            ProgramBuilderService? programBuilderService = null,
            SentinelX.Services.Readiness.IReadinessService? readinessService = null,
            DeviceControlTool? deviceControlTool = null)
        {
            conversationMemory = memory ?? new ConversationMemoryService();
            actionHistory = history ?? new ActionHistoryService(persistenceAllowedProvider: () => !conversationMemory.IsEphemeral);
            actionHistory.PersistenceAllowedProvider = () => !conversationMemory.IsEphemeral;
            deviceControl = deviceControlTool;
            permissionCenter = permissions ?? new PermissionCenterService();
            appLauncher = launcher ?? new AppLauncherService(browserPreference, () => conversationMemory.ExternalNetworkAllowed);
            processTools = processes ?? new ProcessToolService(() => conversationMemory.ExternalNetworkAllowed);
            networkTools = network ?? new NetworkDiagnosticService(() => conversationMemory.ExternalNetworkAllowed);
            this.diagnostics = diagnostics ?? new PcDiagnosticService();
            this.tasks = tasks ?? new ActionTaskRegistry();
            memoryActions = memoryActionService ?? new(conversationMemory, permissionCenter, actionHistory);
            this.historyExport = historyExport ?? new(actionHistory);
            phoneCalls = phoneCallTool ?? new PhoneCallTool();
            audioControl = new AudioControlTool();
            webResearch = webResearchTool ?? new WebResearchTool(externalNetworkAllowed: () => conversationMemory.ExternalNetworkAllowed);
            skillLearning = new SkillLearningService(conversationMemory, webResearch, localAiAsk, localAiLastResponseSucceeded);
            var metricSource = systemMonitor ?? new SystemMonitor();
            var performanceBuffer = performanceHistory ?? new PerformanceHistoryService(metricSource);
            diagnosticsTool = new DiagnosticsTool(processTools, networkTools, performanceBuffer);
            this.gameFocusMode = gameFocusMode ?? new GameFocusModeService();
            autopilot = autopilotService;
            desktopAutomation = desktopAutomationTool ?? new DesktopAutomationTool(() => conversationMemory.ExternalNetworkAllowed);
            clipboardContext = clipboardContextTool ?? new ClipboardContextTool(localAiAsk: localAiAsk,
                localAiLastResponseSucceeded: localAiLastResponseSucceeded);
            programBuilder = programBuilderService ?? new ProgramBuilderService();
            readiness = readinessService;
        }

        // =========================================================
        // MAIN TOOL ROUTER
        // =========================================================

        public async Task<ToolboxCommandResult>
            ProcessAsync(
                string command, System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string requested = command ?? "";
            string normalizedRequest = ConversationMemoryService.Normalize(requested).Trim().TrimEnd('.', '!', '?');
            string? calculation = calculator.TryProcess(requested);
            if (calculation != null) return ToolboxCommandResult.HandledWith(RecordReadOnlyResult("CALCULATOR", requested, calculation));
            if (deviceControl != null)
            {
                string? deviceResponse = await deviceControl.TryProcessAsync(requested, cancellationToken).ConfigureAwait(false);
                if (deviceResponse != null) return ToolboxCommandResult.HandledWith(deviceResponse);
            }
            if (readiness != null && normalizedRequest is ("samokontrola" or "sprawdz siebie" or "sprawdz sentinela" or "status gotowosci"))
            {
                try
                {
                    var checks = await readiness.CheckAsync(cancellationToken).ConfigureAwait(false);
                    string report = "Samokontrola komponentów — tylko odczyt; nie uruchamiałem mikrofonu, nie generowałem odpowiedzi AI i niczego nie zmieniałem.\n" +
                        string.Join("\n", checks.Select(x => $"• {x.Title}: {x.StateLabel} — {x.Detail}" + (x.State == SentinelX.Models.ReadinessState.Ready ? "" : $" Następny krok: {x.NextStep}.")));
                    return ToolboxCommandResult.HandledWith(report);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { return ToolboxCommandResult.HandledWith("Nie udało się dokończyć samokontroli; nie oznaczam komponentów jako gotowych. Szczegóły: " + ex.Message); }
            }
            Match buildSource = Regex.Match(ConversationMemoryService.Normalize(requested).Trim(),
                @"^(?:zbuduj|stworz|utworz) (?:mi )?program (?<template>notatnik|kalkulator|pomodoro)$");
            if (buildSource.Success)
            {
                string template = ProgramBuilderService.Templates.First(x => ConversationMemoryService.Normalize(x) == buildSource.Groups["template"].Value);
                ProgramBuildResult result = await programBuilder.BuildAsync(template, null, cancellationToken).ConfigureAwait(false);
                return ToolboxCommandResult.HandledWith($"{result.Status}: {result.Message}\nFolder źródeł: {result.ProjectDirectory}\nDowód: {result.Evidence}");
            }
            string? skillResponse = await skillLearning.TryProcessAsync(requested, cancellationToken).ConfigureAwait(false);
            if (skillResponse != null) return ToolboxCommandResult.HandledWith(skillResponse);
            string? autopilotResponse = autopilot == null ? null : await autopilot.TryProcessAsync(requested, cancellationToken).ConfigureAwait(false);
            if (autopilotResponse != null) return ToolboxCommandResult.HandledWith(autopilotResponse);
            string? clipboardResponse = await clipboardContext.TryProcessAsync(requested, cancellationToken).ConfigureAwait(false);
            if (clipboardResponse != null) return ToolboxCommandResult.HandledWith(clipboardResponse);
            ToolboxCommandResult? desktopResponse = TryProcessDesktopCommand(requested);
            if (desktopResponse != null) return desktopResponse;
            string? phoneResponse = await phoneCalls.TryProcessAsync(command ?? "", cancellationToken);
            if (phoneResponse != null) return ToolboxCommandResult.HandledWith(phoneResponse);
            string? audioResponse = audioControl.TryProcess(command ?? "", actionHistory, cancellationToken);
            if (audioResponse != null) return ToolboxCommandResult.HandledWith(audioResponse);
            string normalizedForSearch = ConversationMemoryService.Normalize(command ?? "");
            if (normalizedForSearch.StartsWith("szukaj w rozmowie", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("szukaj wszystkiego", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("znajdz w rozmowie", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("przeszukaj rozmowe", StringComparison.Ordinal))
                return ToolboxCommandResult.NotHandled();
            ToolboxCommandResult? contextualSearch = await TryContextualSearchAsync(command ?? "", cancellationToken);
            if (contextualSearch != null) return contextualSearch;
            if (webResearch.TryExtractOpenUrl(command ?? "", out string explicitWebUrl))
                return await ExecuteImmediateAsync("OPEN_WEB_RESULT", command ?? "", () => appLauncher.LaunchAsync(explicitWebUrl, cancellationToken), concise: true);
            string? webResponse = await webResearch.TryProcessAsync(command ?? "", cancellationToken);
            if (webResponse != null) return ToolboxCommandResult.HandledWith(RecordReadOnlyResult("WEB_RESEARCH", command ?? "", webResponse));
            if (webResearch.TryResolveRecentResult(command ?? "", out string resultUrl, out bool readResult, out bool summarizeResult))
            {
                if (readResult)
                {
                    string page = await webResearch.ReadPageAsync(resultUrl, summarizeResult, cancellationToken);
                    return ToolboxCommandResult.HandledWith(RecordReadOnlyResult("WEB_READ", command ?? "", page));
                }
                return await ExecuteImmediateAsync("OPEN_WEB_RESULT", command ?? "", () => appLauncher.LaunchAsync(resultUrl, cancellationToken), concise: true);
            }
            string query = Core.CommandText.Normalize(command ?? "");
            if (query is "wlacz tryb gry" or "wlacz profil gry" or "wlacz tryb grania")
                return ToolboxCommandResult.HandledWith(gameFocusMode.Enable());
            if (query is "wylacz tryb gry" or "wylacz profil gry" or "wylacz tryb grania")
                return ToolboxCommandResult.HandledWith(gameFocusMode.Disable());
            if (query is "status trybu gry" or "tryb gry")
                return ToolboxCommandResult.HandledWith(gameFocusMode.IsActive ? "Profil gry Sentinel jest aktywny; nie zmienia ustawień Windows." : "Profil gry Sentinel jest wyłączony.");
            string? diagnosticToolResponse = await diagnosticsTool.TryProcessAsync(command ?? "", cancellationToken);
            if (diagnosticToolResponse != null) return ToolboxCommandResult.HandledWith(RecordReadOnlyResult("PC_DIAGNOSTICS", command ?? "", diagnosticToolResponse));
            string? memoryResponse = memoryActions.TryRequest(command ?? "");
            if (memoryResponse != null) return ToolboxCommandResult.HandledWith(memoryResponse);
            if (query is "eksportuj historie json" or "eksportuj historie csv")
            {
                string id = actionHistory.CreateActionId();
                var export = await historyExport.ExportAsync(query.EndsWith("csv", StringComparison.Ordinal) ? "csv" : "json", cancellationToken);
                actionHistory.AddResult(id, "EXPORT_HISTORY", command ?? "", export);
                return ToolboxCommandResult.HandledWith(FormatActionResponse(id, export));
            }
            if (query is "zadania" or "aktywne zadania" or "..tasks") return ToolboxCommandResult.HandledWith(GetTaskSummary());
            if (query.StartsWith("dowod ")) return ToolboxCommandResult.HandledWith(actionHistory.GetActionDetails(command![6..]));
            string? diagnostic = query switch
            {
                "zabezpieczenia" or "defender" or "zapora" or "status zabezpieczen" => (await diagnostics.GetSecurityAsync(cancellationToken)).Format(),
                "lista autostartu" or "programy autostartu" => (await diagnostics.GetStartupAsync(cancellationToken)).Format(),
                "uslugi" or "lista uslug" => (await diagnostics.GetServicesAsync(cancellationToken)).Format(),
                "bledy windows" or "zdarzenia windows" => (await diagnostics.GetEventsAsync(cancellationToken)).Format(),
                "dyski" or "pokaz dyski" => (await diagnostics.GetDisksAsync(cancellationToken)).Format(),
                "diagnostyka komputera" or "raport komputera" => (await diagnostics.CollectAsync(cancellationToken)).ToMarkdown(),
                "eksportuj raport" => FormatActionResponse(actionHistory.CreateActionId(), await diagnostics.ExportReportAsync(cancellationToken)),
                _ => null
            };
            if (diagnostic != null) return ToolboxCommandResult.HandledWith(diagnostic);
            if (query.StartsWith("analizuj plik ", StringComparison.Ordinal))
                return ToolboxCommandResult.HandledWith(FormatActionResponse(actionHistory.CreateActionId(), await diagnostics.InspectFileAsync(command![13..], cancellationToken)));
            if (string.IsNullOrWhiteSpace(
                    command))
            {
                return ToolboxCommandResult.NotHandled();
            }


            string normalized =
                Normalize(
                    command);


            // =====================================================
            // PERMISSION CENTER
            // =====================================================

            if (normalized == "potwierdz" ||
                normalized == "potwierdz akcje" ||
                normalized == "confirm" || normalized == "potwierdz usuniecie wspomnien")
            {
                return ToolboxCommandResult.HandledWith(
                    await ConfirmPendingActionAsync(cancellationToken));
            }


            if (normalized == "anuluj" ||
                normalized == "anuluj akcje" ||
                normalized == "cancel")
            {
                return ToolboxCommandResult.HandledWith(
                    CancelPendingAction());
            }


            if (normalized == "co czeka na potwierdzenie" ||
                normalized == "oczekujaca akcja" ||
                normalized == "oczekujace akcje")
            {
                return ToolboxCommandResult.HandledWith(
                    permissionCenter
                        .GetPendingSummary());
            }


            // =====================================================
            // ACTION HISTORY
            // =====================================================

            if (normalized == "historia akcji" ||
                normalized == "ostatnie akcje" ||
                normalized == "action history")
            {
                return ToolboxCommandResult.HandledWith(
                    actionHistory
                        .GetRecentSummary(
                            10));
            }

            if (normalized is "co robiles przed chwila" or "co robiles" or "ostatnie polecenia" or "pokaz ostatnie dzialania")
                return ToolboxCommandResult.HandledWith(actionHistory.GetRecentSummary(5));

            if (normalized is "dlaczego to robisz" or "dlaczego to robisz teraz" or "co robisz teraz")
            {
                var activeTask = tasks.GetTasks().FirstOrDefault(x => x.Status is "RUNNING" or "CANCELLING");
                if (activeTask != null)
                    return ToolboxCommandResult.HandledWith($"Wykonuję: {activeTask.Description}. Krok: {activeTask.CurrentStep}. Powód: kontynuuję zadanie, które zleciłeś.");
                var activeAction = actionHistory.GetRecentEntries().FirstOrDefault(x => x.Status == "RUNNING");
                if (activeAction != null)
                    return ToolboxCommandResult.HandledWith($"Trwa akcja {activeAction.ActionType} dla polecenia „{activeAction.Command}”. Sprawdzam wynik przed zgłoszeniem sukcesu.");
                return ToolboxCommandResult.HandledWith("Nie wykonuję teraz żadnego działania w tle. Ostatnie wykonane kroki: \n" + actionHistory.GetRecentSummary(3));
            }


            // =====================================================
            // PROCESSES
            // =====================================================

            if (normalized == "top procesy" ||
                normalized == "procesy" ||
                normalized == "co zuzywa ram" ||
                normalized == "co zjada ram" ||
                normalized == "najwiecej ram")
            {
                return ToolboxCommandResult.HandledWith(
                    processTools
                        .GetTopMemoryProcesses(
                            10));
            }


            // =====================================================
            // NETWORK
            // =====================================================

            if (normalized == "test internetu" ||
                normalized == "sprawdz internet" ||
                normalized == "sprawdz polaczenie" ||
                normalized == "ping internetu")
            {
                string actionId =
                    actionHistory
                        .CreateActionId();


                ActionExecutionResult result =
                    await networkTools
                        .TestInternetAsync(cancellationToken);


                actionHistory
                    .AddResult(
                        actionId,
                        "NETWORK_TEST",
                        command,
                        result);


                return ToolboxCommandResult.HandledWith(
                    FormatActionResponse(
                        actionId,
                        result));
            }


            if (normalized == "siec" ||
                normalized == "status sieci" ||
                normalized == "karta sieciowa" ||
                normalized == "informacje o sieci")
            {
                return ToolboxCommandResult.HandledWith(
                    networkTools
                        .GetNetworkSummary());
            }


            // =====================================================
            // WEB SEARCH
            // =====================================================

            if (TryExtractAfterAnyPrefix(
                    command,
                    out string youtubeQuery,
                    "wyszukaj na youtube ",
                    "szukaj na youtube "))
            {
                return await ExecuteImmediateAsync(
                    "YOUTUBE_SEARCH",
                    command,
                    () =>
                        appLauncher
                            .SearchWebAsync(
                                youtubeQuery,
                                true));
            }


            if (TryExtractAfterAnyPrefix(
                    command,
                    out string webQuery,
                    "wyszukaj ",
                    "szukaj w google ",
                    "szukaj "))
            {
                return await ExecuteImmediateAsync(
                    "WEB_SEARCH",
                    command,
                    () =>
                        appLauncher
                            .SearchWebAsync(
                                webQuery,
                                false));
            }


            // =====================================================
            // FOLDERS
            // =====================================================

            if (TryExtractAfterAnyPrefix(
                    command,
                    out string folder,
                    "otwórz folder ",
                    "otworz folder "))
            {
                return await ExecuteImmediateAsync(
                    "OPEN_FOLDER",
                    command,
                    () =>
                        appLauncher
                            .OpenFolderAsync(
                                folder));
            }


            if (normalized == "otworz pobrane" ||
                normalized == "pokaz pobrane")
            {
                return await ExecuteImmediateAsync(
                    "OPEN_FOLDER",
                    command,
                    () =>
                        appLauncher
                            .OpenFolderAsync(
                                "pobrane"));
            }


            if (normalized == "otworz pulpit" ||
                normalized == "pokaz pulpit")
            {
                return await ExecuteImmediateAsync(
                    "OPEN_FOLDER",
                    command,
                    () =>
                        appLauncher
                            .OpenFolderAsync(
                                "pulpit"));
            }


            if (normalized == "otworz dokumenty" ||
                normalized == "pokaz dokumenty")
            {
                return await ExecuteImmediateAsync(
                    "OPEN_FOLDER",
                    command,
                    () =>
                        appLauncher
                            .OpenFolderAsync(
                                "dokumenty"));
            }


            if (normalized == "otworz folder sentinel" ||
                normalized == "folder sentinel")
            {
                return await ExecuteImmediateAsync(
                    "OPEN_FOLDER",
                    command,
                    () =>
                        appLauncher
                            .OpenFolderAsync(
                                "sentinel"));
            }


            // Close the sole detected hung GUI directly, but preserve approval because unsaved work may be lost.
            if (normalized is "zamknij zawieszony program" or "zamknij zawieszona aplikacje" or
                "zamknij program ktory sie zawiesil" or "zamknij aplikacje ktora nie odpowiada")
            {
                var hung = processTools.GetUnresponsiveApps();
                if (hung.Count == 0) return ToolboxCommandResult.HandledWith("Nie wykrywam teraz zawieszonego okna.");
                if (hung.Count > 1)
                    return ToolboxCommandResult.HandledWith("Wykryłem kilka nieodpowiadających okien. Wybierz aplikację, np. „zamknij Discord”:\n" +
                        string.Join("\n", hung.Select(x => $"· {x.Name} (PID {x.ProcessId})")));
                string target = hung[0].Name;
                if (!processTools.CanCloseSafely(target))
                    return ToolboxCommandResult.HandledWith($"Wykryłem zawieszone okno {target}, ale nie ma ono bezpiecznego zamykania w Sentinel. Nie wymuszę zakończenia procesu.");
                return RequestCloseAction(command, target);
            }

            // =====================================================
            // CLOSE APP
            //
            // ALWAYS GOES THROUGH PERMISSION CENTER.
            // =====================================================

            if (TryExtractAfterAnyPrefix(
                    command,
                    out string closeTarget,
                    "zamknij ",
                    "wyłącz aplikację ",
                    "wylacz aplikacje "))
            {
                if (Normalize(closeTarget) is "go" or "ja" or "to")
                {
                    if (lastVerifiedApplication.Length == 0 || DateTime.Now - lastVerifiedApplicationAt > TimeSpan.FromMinutes(20))
                    {
                        lastVerifiedApplication = ""; lastVerifiedApplicationAt = default;
                        return ToolboxCommandResult.HandledWith("Którą aplikację zamknąć? Nie mam świeżego, potwierdzonego celu z ostatnich 20 minut.");
                    }
                    closeTarget = lastVerifiedApplication;
                }
                return RequestCloseAction(
                    command,
                    closeTarget);
            }


            // =====================================================
            // LAUNCH APP / WEBSITE
            // =====================================================

            if (AppLaunchPlan.TryParseCommand(command, out string launchTarget))
            {
                var plan = AppLaunchPlan.Parse(launchTarget);
                if (!plan.IsValid) return ToolboxCommandResult.HandledWith(plan.Error!);
                return await ExecuteLaunchPlanAsync(command, plan, cancellationToken);
            }


            // =====================================================
            // TOOLBOX HELP
            // =====================================================

            if (normalized == "toolbox" ||
                normalized == "narzedzia sentinel")
            {
                return ToolboxCommandResult.HandledWith(
                    """
                    SENTINEL TOOLBOX

                    Przykładowe komendy:

                    włącz Discord
                    włącz Steam
                    włącz CS2
                    włącz Brave
                    włącz YouTube
                    otwórz ustawienia

                    wyszukaj karta graficzna RTX (wyniki z linkami)
                    zbadaj temat: porównaj do 3 znalezionych stron
                    otwórz drugą stronę / podsumuj pierwszy wynik
                    znajdź oficjalny numer telefonu firmy X (kandydaci ze źródłami)
                    otwórz stronę https://example.com
                    czytaj stronę https://example.com
                    wyszukaj na YouTube CS2 settings
                    naucz się Kung Fu / ucz się o Pythonie (brief z ograniczonych źródeł)
                    umiejętności (lista lokalnych notatek skill)

                    znajdź plik o budżecie wakacyjnym
                    przenieś go do folderu Dokumenty
                    utwórz folder Raporty na pulpicie
                    otwórz go / pokaż go

                    otwórz pobrane
                    otwórz dokumenty
                    otwórz folder Sentinel

                    top procesy
                    co zjada RAM

                    test internetu
                    status sieci

                    zadzwoń do [firma] i opisz sprawę (wymaga aktywnego mostu telefonicznego)
                    historia rozmów / /rozmowy historia

                    zamknij Discord
                    potwierdz
                    anuluj

                    historia akcji
                    """
                );
            }


            return ToolboxCommandResult.NotHandled();
        }


        // =========================================================
        // IMMEDIATE ACTION
        // =========================================================

        /// <summary>Returns bounded visible UI Automation labels for an explicit screen-context request.
        /// Editable values are never read, and captured labels are not copied into the persistent action audit.</summary>
        public string ReadDesktopContext(string command)
        {
            if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1")
                return "Desktop UI Automation is disabled during UI smoke tests.";
            DesktopTarget? target = desktopAutomation.GetFreshTarget();
            string context = desktopAutomation.ReadVisibleControls();
            bool failed = target == null || context.StartsWith("Nie udało się", StringComparison.OrdinalIgnoreCase) ||
                context.StartsWith("Nie można", StringComparison.OrdinalIgnoreCase) ||
                context.StartsWith("UI Automation", StringComparison.OrdinalIgnoreCase) ||
                context.StartsWith("Okno ", StringComparison.OrdinalIgnoreCase) && context.Contains("nie udostępnia tekstowych kontrolek", StringComparison.OrdinalIgnoreCase);
            string id = actionHistory.CreateActionId();
            ActionExecutionResult result = failed
                ? ActionExecutionResult.Failure("Nie uzyskano kontekstu UI Automation.", "Nie zapisano tekstu kontrolek w audycie.")
                : ActionExecutionResult.UnverifiedSuccess("Pobrano ograniczony kontekst tekstowych kontrolek UI Automation; nie wykonano OCR ani odczytu wartości pól.",
                    $"Liczba znaków kontekstu: {context.Length}. Tekst kontrolek nie został utrwalony w audycie.");
            actionHistory.AddResult(id, "UIA_READ", command, result);
            return context;
        }

        private ToolboxCommandResult? TryProcessDesktopCommand(string command)
        {
            if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1") return null;
            string normalized = Core.CommandText.Normalize(command).Trim().TrimEnd('.', '!', '?', ',');
            DesktopTarget? pendingTarget = desktopAutomation.GetPendingSelectionTarget();
            bool clickRequest = Regex.IsMatch(command.Trim(), @"^(?:kliknij|naciśnij|wciśnij|wybierz|click|press|choose)\s+.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            bool pendingChoice = pendingTarget != null && Regex.IsMatch(normalized, @"^(?:(?:kliknij|click)\s+)?(?:nr\s*)?\d+$", RegexOptions.CultureInvariant);
            if (!conversationMemory.ExternalNetworkAllowed && (clickRequest || pendingChoice))
            {
                desktopAutomation.ClearPendingCandidates();
                return ToolboxCommandResult.HandledWith("Tryb tylko lokalnie blokuje sterowanie oknami zewnętrznych aplikacji, które mogą wysłać dane poza komputer. Nie kliknięto niczego.");
            }
            if (pendingTarget != null)
            {
                Match selection = Regex.Match(normalized, @"^(?:(?:kliknij|click)\s+)?(?:nr\s*)?(\d+)$", RegexOptions.CultureInvariant);
                if (selection.Success)
                {
                    if (!int.TryParse(selection.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) ||
                        !desktopAutomation.TryTakePendingCandidate(index, out DesktopTarget? selectedTarget, out DesktopControlCandidate? selected, out int choiceCount))
                        return ToolboxCommandResult.HandledWith("Nie ma takiej pozycji. Wybierz numer z listy albo wpisz „anuluj”.");
                    return RequestDesktopInvoke(command, selectedTarget!, selected!, index, choiceCount);
                }
                if (normalized is "anuluj" or "anuluj akcje" or "cancel") desktopAutomation.ClearPendingCandidates();
                else if (!Regex.IsMatch(normalized, @"^(?:kliknij|nacisnij|wcisnij|wybierz|click|press|choose)\s+.+$", RegexOptions.CultureInvariant))
                    desktopAutomation.ClearPendingCandidates();
            }

            Match click = Regex.Match(command.Trim(), @"^(?:kliknij|naciśnij|wciśnij|wybierz|click|press|choose)\s+(?:(?:na|on|the)\s+)?(.+?)\s*[.!?]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!click.Success) return null;
            desktopAutomation.ClearPendingCandidates();
            string exactName = click.Groups[1].Value.Trim().Trim('"', '\'', '„', '”', '»', '«', ' ');
            exactName = Regex.Replace(exactName, @"^(?:(?:przycisk|hiperłącze|link|button|the button|the link)\s+)+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            exactName = Regex.Replace(exactName, @"\s+(?:przycisk|hiperłącze|link|button)$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            if (exactName.Length is 0 or > 160)
                return ToolboxCommandResult.HandledWith("Podaj krótką, dokładną etykietę widocznego przycisku lub linku.");
            DesktopTarget? target = desktopAutomation.GetFreshTarget();
            if (target == null)
                return ToolboxCommandResult.HandledWith("Nie mam świeżego kontekstu okna. Uaktywnij docelową aplikację, wróć do Sentinela i ponów polecenie.");
            IReadOnlyList<DesktopControlCandidate> candidates = desktopAutomation.FindClickableControls(exactName, out string scanError);
            if (scanError.Length != 0)
                return ToolboxCommandResult.HandledWith("Nie udało się bezpiecznie odczytać przycisków. Niczego nie kliknięto. " + scanError);
            DesktopTarget? afterScan = desktopAutomation.GetFreshTarget();
            if (afterScan == null || afterScan.Handle != target.Handle || afterScan.ProcessId != target.ProcessId)
                return ToolboxCommandResult.HandledWith("Okno zmieniło się podczas wyszukiwania kontrolki. Nie kliknięto niczego; ponów polecenie przy właściwym oknie.");
            if (candidates.Count == 0)
                return ToolboxCommandResult.HandledWith($"Nie znaleziono widocznego, aktywnego przycisku ani hiperłącza o dokładnej nazwie „{exactName}” w {target.ProcessName}. Niczego nie kliknięto.");
            if (candidates.Count == 1) return RequestDesktopInvoke(command, target, candidates[0]);

            desktopAutomation.SetPendingCandidates(target, candidates);
            string choices = string.Join("\n", candidates.Select((candidate, index) =>
                $"{index + 1}. {candidate.ControlType.Replace("ControlType.", "", StringComparison.Ordinal)} „{SensitiveDataRedactor.Redact(candidate.Name)}”" +
                (candidate.AutomationId.Length == 0 ? "" : $" (ID: {SensitiveDataRedactor.Redact(candidate.AutomationId)})")));
            return ToolboxCommandResult.HandledWith("Znaleziono kilka pasujących kontrolek. Nie kliknięto niczego. Wskaż numer:\n" + choices);
        }

        private ToolboxCommandResult RequestDesktopInvoke(string command, DesktopTarget target, DesktopControlCandidate candidate, int choiceNumber = 0, int choiceCount = 0)
        {
            if (!conversationMemory.ExternalNetworkAllowed) return ToolboxCommandResult.HandledWith("Tryb tylko lokalnie zablokował przekazanie kliknięcia do innej aplikacji. Nie kliknięto niczego.");
            DesktopTarget? current = desktopAutomation.GetFreshTarget();
            if (current == null || current.Handle != target.Handle || current.ProcessId != target.ProcessId)
                return ToolboxCommandResult.HandledWith("Okno docelowe wygasło lub zmieniło się. Nie poproszono o zgodę i nie kliknięto kontrolki.");
            string safeCommand = SensitiveDataRedactor.Redact(command);
            string actionId = actionHistory.CreateActionId();
            string description = SensitiveDataRedactor.Redact($"Wywołać {candidate.ControlType.Replace("ControlType.", "", StringComparison.Ordinal)} „{candidate.Name}”" +
                (candidate.AutomationId.Length == 0 ? "" : $" (ID: {candidate.AutomationId})") +
                (choiceNumber > 0 ? $" — wybrana pozycja {choiceNumber} z {choiceCount}" : "") +
                $" w {target.ProcessName}" + (target.Title.Length == 0 ? "" : $" — {target.Title}") +
                "? Skutek aplikacyjny nie jest gwarantowany; potwierdź tylko, jeśli to właściwa kontrolka.");
            var action = new PendingPermissionAction
            {
                ActionId = actionId, ActionType = "UI_AUTOMATION_INVOKE", OriginalCommand = safeCommand,
                Description = description, RiskLevel = "HIGH",
                CancellableExecutor = _ => Task.FromResult(InvokeDesktopCandidate(actionId, safeCommand, target, candidate))
            };
            bool accepted = permissionCenter.TryRequest(action, out string response);
            if (accepted) actionHistory.AddPending(actionId, action.ActionType, safeCommand, description);
            return ToolboxCommandResult.HandledWith(response);
        }

        private ActionExecutionResult InvokeDesktopCandidate(string actionId, string command, DesktopTarget target, DesktopControlCandidate candidate)
        {
            ActionExecutionResult result = desktopAutomation.InvokeButtonAfterApproval(target, candidate);
            actionHistory.AddResult(actionId, "UI_AUTOMATION_INVOKE", command, result);
            return result;
        }

        private async Task<ToolboxCommandResult?> TryContextualSearchAsync(string command, CancellationToken token)
        {
            string current = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '?', '!');
            if (current is not ("znajdz mi to" or "znajdz to" or "wyszukaj to" or "poszukaj tego" or "znajdz tamto")) return null;
            var prior = conversationMemory.GetRecentEntries(20).LastOrDefault(x =>
                x.Role == "user" && ConversationMemoryService.Normalize(x.Text).Trim().TrimEnd('.', '?', '!') != current &&
                x.Text.Trim().Length >= 8 && !IsSearchInstruction(x.Text));
            if (prior == null)
                return ToolboxCommandResult.HandledWith("Nie widzę wcześniejszego tematu do wyszukania w tej rozmowie. Podaj krótki opis; fraza trafi do DuckDuckGo.");
            if (SensitiveDataRedactor.ContainsLikelySecret(prior.Text))
                return ToolboxCommandResult.HandledWith("Poprzednia wiadomość wygląda na zawierającą sekret. Nie wysyłam jej do wyszukiwarki; podaj publiczną frazę bez danych poufnych.");
            string query = prior.Text.Trim();
            if (query.Length > 180) query = query[..180];
            string result = await webResearch.SearchAsync(query, token: token);
            string response = "Szukam na podstawie ostatniego tematu rozmowy („" + query + "”):\n\n" + result;
            return ToolboxCommandResult.HandledWith(RecordReadOnlyResult("WEB_CONTEXT_SEARCH", command, response));
        }

        private string RecordReadOnlyResult(string type, string command, string response)
        {
            string id = actionHistory.CreateActionId();
            string evidence = response.Length > 4000 ? response[..4000] + "… [ucięto w historii]" : response;
            bool failure = response.StartsWith("Nie udało", StringComparison.Ordinal) || response.StartsWith("Nie mogę", StringComparison.Ordinal) || response.StartsWith("Odrzucono", StringComparison.Ordinal);
            ActionExecutionResult result = failure
                ? ActionExecutionResult.Failure("Narzędzie zgłosiło błąd odczytu.", evidence)
                : ActionExecutionResult.VerifiedSuccess("Odczyt narzędzia zakończony; treść źródeł nie jest niezależnie zweryfikowana.", evidence);
            actionHistory.AddResult(id, type, command, result);
            return response;
        }

        private static bool IsSearchInstruction(string value)
        {
            string normalized = ConversationMemoryService.Normalize(value).Trim();
            return normalized.StartsWith("znajdz ", StringComparison.Ordinal) || normalized.StartsWith("wyszukaj ", StringComparison.Ordinal) ||
                normalized.StartsWith("szukaj ", StringComparison.Ordinal) || normalized.StartsWith("poszukaj ", StringComparison.Ordinal);
        }

        private async Task<ToolboxCommandResult>
            ExecuteImmediateAsync(
                string actionType,
                string command,
                Func<Task<ActionExecutionResult>> executor,
                bool concise = false)
        {
            string actionId =
                actionHistory
                    .CreateActionId();


            ActionExecutionResult result;


            try
            {
                actionHistory.AddRunning(actionId, actionType, command);
                result =
                    await executor();
            }
            catch (OperationCanceledException)
            { actionHistory.AddCancelled(actionId, actionType, command, "Akcja została przerwana; zakończone zmiany nie są cofane."); throw; }
            catch (Exception ex)
            {
                result =
                    ActionExecutionResult.Failure(
                        "Akcja zakończyła się błędem.",
                        ex.Message);
            }


            actionHistory
                .AddResult(
                    actionId,
                    actionType,
                    command,
                    result);


            if (concise)
                return ToolboxCommandResult.HandledWith(!result.Success ? "Nie udało się otworzyć strony."
                    : result.Verified ? "Gotowe."
                    : "Wysłano stronę do przeglądarki; nie potwierdziłem, że karta została wyświetlona.");
            return ToolboxCommandResult.HandledWith(
                FormatActionResponse(
                    actionId,
                    result));
        }

        private async Task<ToolboxCommandResult> ExecuteLaunchPlanAsync(string command, AppLaunchPlan plan, CancellationToken token)
        {
            string parent = actionHistory.CreateActionId();
            using var task = tasks.Begin(parent, command, plan.Targets.Count, token);
            var output = new List<string>();
            bool failed = false, unverified = false;
            foreach (string target in plan.Targets)
            {
                task.Token.ThrowIfCancellationRequested();
                string id = actionHistory.CreateActionId();
                task.SetStep("Uruchamianie: " + target);
                actionHistory.AddRunning(id, "LAUNCH", target, parent);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var result = await appLauncher.LaunchAsync(target, task.Token);
                    if (result.Verified)
                    {
                        lastVerifiedApplication = AppLauncherService.CanonicalizeLaunchTarget(target);
                        lastVerifiedApplicationAt = DateTime.Now;
                    }
                    actionHistory.AddResult(id, "LAUNCH", target, result, clock.ElapsedMilliseconds, parent);
                    failed |= !result.Success; unverified |= !result.Verified;
                    output.Add(result.Success && result.Verified ? "Gotowe: " + target + "."
                        : result.Success ? "Wysłano polecenie otwarcia " + target + ", ale nie potwierdzono uruchomienia."
                        : "Nie udało się otworzyć " + target + ".");
                    task.CompleteStep();
                }
                catch (OperationCanceledException)
                { actionHistory.AddCancelled(id, "LAUNCH", target, "Przerwano weryfikację; aplikacja mogła już wystartować."); task.Complete("CANCELLED"); throw; }
                catch (Exception ex)
                { var result = ActionExecutionResult.Failure("Nie udało się uruchomić: " + target, ex.Message); actionHistory.AddResult(id, "LAUNCH", target, result, clock.ElapsedMilliseconds, parent); output.Add("Nie udało się otworzyć " + target + "."); failed = true; task.CompleteStep(); }
            }
            task.Complete(failed ? "FAILED" : unverified ? "UNVERIFIED" : "VERIFIED");
            return ToolboxCommandResult.HandledWith(string.Join("\n\n", output));
        }


        // =========================================================
        // REQUEST CLOSE
        // =========================================================

        private ToolboxCommandResult RequestCloseAction(
            string originalCommand,
            string target)
        {
            if (string.IsNullOrWhiteSpace(
                    target))
            {
                return ToolboxCommandResult.HandledWith(
                    "Podaj aplikację do zamknięcia.");
            }


            string actionId =
                actionHistory
                    .CreateActionId();


            PendingPermissionAction action =
                new PendingPermissionAction
                {
                    ActionId =
                        actionId,

                    ActionType =
                        "CLOSE_APP",

                    OriginalCommand =
                        originalCommand,

                    Description =
                        $"Zamknąć aplikację: {target}? Niezapisana praca w tej aplikacji może zostać utracona.",

                    RiskLevel =
                        "MEDIUM",

                    CancellableExecutor =
                        async token =>
                        {
                            ActionExecutionResult result =
                                await processTools
                                    .CloseAppAsync(
                                        target, token);
                            if (result.Verified && Normalize(lastVerifiedApplication) == Normalize(target))
                            {
                                lastVerifiedApplication = ""; lastVerifiedApplicationAt = default;
                            }


                            actionHistory
                                .AddResult(
                                    actionId,
                                    "CLOSE_APP",
                                    originalCommand,
                                    result);


                            return result;
                        }
                };


            bool accepted =
                permissionCenter
                    .TryRequest(
                        action,
                        out string response);


            if (accepted)
            {
                actionHistory
                    .AddPending(
                        actionId,
                        "CLOSE_APP",
                        originalCommand,
                        action.Description);
            }


            return ToolboxCommandResult.HandledWith(
                response);
        }


        // =========================================================
        // CONFIRM
        // =========================================================

        private async Task<string>
            ConfirmPendingActionAsync(CancellationToken cancellationToken)
        {
            PermissionExecutionResult execution =
                await permissionCenter
                    .ConfirmAsync(cancellationToken);


            if (execution.ApprovalRejected) return execution.Result?.Message ?? "Zgoda odrzucona.";
            if (!execution.HadPendingAction ||
                execution.Action == null)
            {
                return
                    "Brak akcji do potwierdzenia.";
            }


            ActionExecutionResult result =
                execution.Result ??
                ActionExecutionResult.Failure(
                    "Brak wyniku akcji.");


            actionHistory.AddResult(execution.Action.ActionId, execution.Action.ActionType, execution.Action.OriginalCommand, result);
            return FormatActionResponse(
                execution.Action.ActionId,
                result);
        }


        // =========================================================
        // CANCEL
        // =========================================================

        public string CancelPendingAction()
        {
            PendingPermissionAction? action =
                permissionCenter
                    .Cancel();


            if (action == null)
            {
                return
                    "Brak akcji do anulowania.";
            }


            actionHistory
                .AddCancelled(
                    action.ActionId,
                    action.ActionType,
                    action.OriginalCommand);


            return
                $"""
                ANULOWANO

                Action ID:
                {action.ActionId}

                Nie wykonano zmian.
                """;
        }


        // =========================================================
        // RESPONSE FORMAT
        // =========================================================

        private static string FormatActionResponse(
            string actionId,
            ActionExecutionResult result)
        {
            string status;


            if (!result.Success)
            {
                status =
                    "FAILED";
            }
            else if (result.Verified)
            {
                status =
                    "VERIFIED";
            }
            else
            {
                status =
                    "NOT VERIFIED";
            }


            return
                $"""
                {status}

                Action ID:
                {actionId}

                {result.Message}

                Dowód:
                {(string.IsNullOrWhiteSpace(result.Evidence)
                    ? "brak"
                    : result.Evidence)}
                """;
        }


        // =========================================================
        // PREFIX PARSER
        // =========================================================

        private static bool TryExtractAfterAnyPrefix(
            string text,
            out string result,
            params string[] prefixes)
        {
            foreach (string prefix
                     in prefixes)
            {
                if (!text.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }


                result =
                    text
                        .Substring(
                            prefix.Length)
                        .Trim();


                return
                    !string.IsNullOrWhiteSpace(
                        result);
            }


            result =
                string.Empty;


            return false;
        }


        // =========================================================
        // NORMALIZATION
        // =========================================================

        private static string Normalize(
            string text)
        {
            return text
                .Trim()
                .ToLowerInvariant()
                .Trim(
                    ' ',
                    '.',
                    ',',
                    '?',
                    '!',
                    ':',
                    ';')
                .Replace('ą', 'a')
                .Replace('ć', 'c')
                .Replace('ę', 'e')
                .Replace('ł', 'l')
                .Replace('ń', 'n')
                .Replace('ó', 'o')
                .Replace('ś', 's')
                .Replace('ż', 'z')
                .Replace('ź', 'z');
        }
    }
}
