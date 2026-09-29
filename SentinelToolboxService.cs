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
        public IReadOnlyList<ActionTaskSnapshot> GetTasks() => tasks.GetTasks();
        public int CancelAllTasks() => tasks.CancelAll();
        public string GetTaskSummary() => tasks.GetTasks().Count == 0 ? "Brak zadań w tej sesji." : string.Join("\n\n", tasks.GetTasks().Select(t => $"{t.Id} [{t.Status}] {t.CompletedSteps}/{t.TotalSteps}\n{t.Description}\n{t.CurrentStep}"));
        public bool HasPendingAction => permissionCenter.HasPendingAction;
        public string PendingSummary => permissionCenter.GetPendingSummary();
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
        private readonly DesktopAutomationTool desktopAutomation;


        public SentinelToolboxService(Func<string>? browserPreference = null, ActionHistoryService? history = null,
            Services.Permissions.IPermissionService? permissions = null, Services.Apps.IAppLauncherService? launcher = null,
            ProcessToolService? processes = null, Services.Network.INetworkService? network = null,
            PcDiagnosticService? diagnostics = null, ActionTaskRegistry? tasks = null, ConversationMemoryService? memory = null,
            Services.History.HistoryExportService? historyExport = null, Services.Memory.MemoryActionService? memoryActionService = null,
            PhoneCallTool? phoneCallTool = null, WebResearchTool? webResearchTool = null,
            SystemMonitor? systemMonitor = null, PerformanceHistoryService? performanceHistory = null,
            GameFocusModeService? gameFocusMode = null, DesktopAutomationTool? desktopAutomationTool = null)
        {
            actionHistory = history ?? new ActionHistoryService();
            permissionCenter = permissions ?? new PermissionCenterService();
            appLauncher = launcher ?? new AppLauncherService(browserPreference);
            processTools = processes ?? new ProcessToolService();
            networkTools = network ?? new NetworkDiagnosticService();
            this.diagnostics = diagnostics ?? new PcDiagnosticService();
            this.tasks = tasks ?? new ActionTaskRegistry();
            conversationMemory = memory ?? new ConversationMemoryService();
            memoryActions = memoryActionService ?? new(conversationMemory, permissionCenter, actionHistory);
            this.historyExport = historyExport ?? new(actionHistory);
            phoneCalls = phoneCallTool ?? new PhoneCallTool();
            audioControl = new AudioControlTool();
            webResearch = webResearchTool ?? new WebResearchTool();
            var metricSource = systemMonitor ?? new SystemMonitor();
            var performanceBuffer = performanceHistory ?? new PerformanceHistoryService(metricSource);
            diagnosticsTool = new DiagnosticsTool(processTools, networkTools, performanceBuffer);
            this.gameFocusMode = gameFocusMode ?? new GameFocusModeService();
            desktopAutomation = desktopAutomationTool ?? new DesktopAutomationTool();
        }

        // =========================================================
        // MAIN TOOL ROUTER
        // =========================================================

        public async Task<ToolboxCommandResult>
            ProcessAsync(
                string command, System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToolboxCommandResult? desktopResponse = TryProcessDesktopCommand(command ?? "");
            if (desktopResponse != null) return desktopResponse;
            string? phoneResponse = await phoneCalls.TryProcessAsync(command ?? "", cancellationToken);
            if (phoneResponse != null) return ToolboxCommandResult.HandledWith(phoneResponse);
            string? audioResponse = audioControl.TryProcess(command ?? "", actionHistory, cancellationToken);
            if (audioResponse != null) return ToolboxCommandResult.HandledWith(audioResponse);
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


            // Searching the conversation is a local memory feature — never route it to a web search.
            string normalizedForSearch = ConversationMemoryService.Normalize(command);
            if (normalizedForSearch.StartsWith("szukaj w rozmowie", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("szukaj wszystkiego", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("znajdz w rozmowie", StringComparison.Ordinal) ||
                normalizedForSearch.StartsWith("przeszukaj rozmowe", StringComparison.Ordinal))
                return ToolboxCommandResult.NotHandled();

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
                    if (lastVerifiedApplication.Length == 0) return ToolboxCommandResult.HandledWith("Którą aplikację zamknąć? Nie mam potwierdzonego ostatniego celu.");
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
            bool failure = response.StartsWith("Nie udało", StringComparison.Ordinal) || response.StartsWith("Odrzucono", StringComparison.Ordinal);
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
                    if (result.Verified) lastVerifiedApplication = AppLauncherService.CanonicalizeLaunchTarget(target);
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
