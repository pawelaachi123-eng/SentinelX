using System;
using System.Threading.Tasks;

namespace SentinelX
{
    public sealed class SentinelToolboxService
    {
        private readonly PcDiagnosticService diagnostics;
        private readonly ActionTaskRegistry tasks;
        private string lastVerifiedApplication = "";
        public IReadOnlyList<ActionTaskSnapshot> GetTasks() => tasks.GetTasks();
        public int CancelAllTasks() => tasks.CancelAll();
        public string GetTaskSummary() => tasks.GetTasks().Count == 0 ? "Brak zadań w tej sesji." : string.Join("\n\n", tasks.GetTasks().Select(t => $"{t.Id} [{t.Status}] {t.CompletedSteps}/{t.TotalSteps}\n{t.Description}\n{t.CurrentStep}"));
        public bool HasPendingAction => permissionCenter.HasPendingAction;
        public string PendingSummary => permissionCenter.GetPendingSummary();
        private readonly ActionHistoryService actionHistory;
        private readonly Services.Memory.MemoryActionService memoryActions;
        private readonly Services.History.HistoryExportService historyExport;

        private readonly Services.Permissions.IPermissionService permissionCenter;

        private readonly Services.Apps.IAppLauncherService appLauncher;

        private readonly ProcessToolService processTools;

        private readonly Services.Network.INetworkService networkTools;


        public SentinelToolboxService(Func<string>? browserPreference = null, ActionHistoryService? history = null,
            Services.Permissions.IPermissionService? permissions = null, Services.Apps.IAppLauncherService? launcher = null,
            ProcessToolService? processes = null, Services.Network.INetworkService? network = null,
            PcDiagnosticService? diagnostics = null, ActionTaskRegistry? tasks = null, ConversationMemoryService? memory = null,
            Services.History.HistoryExportService? historyExport = null, Services.Memory.MemoryActionService? memoryActionService = null)
        {
            actionHistory = history ?? new ActionHistoryService();
            permissionCenter = permissions ?? new PermissionCenterService();
            appLauncher = launcher ?? new AppLauncherService(browserPreference);
            processTools = processes ?? new ProcessToolService();
            networkTools = network ?? new NetworkDiagnosticService();
            this.diagnostics = diagnostics ?? new PcDiagnosticService();
            this.tasks = tasks ?? new ActionTaskRegistry();
            memoryActions = memoryActionService ?? new(memory ?? new ConversationMemoryService(), permissionCenter, actionHistory);
            this.historyExport = historyExport ?? new(actionHistory);
        }

        // =========================================================
        // MAIN TOOL ROUTER
        // =========================================================

        public async Task<ToolboxCommandResult>
            ProcessAsync(
                string command, System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string query = Core.CommandText.Normalize(command ?? "");
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

                    wyszukaj karta graficzna RTX
                    wyszukaj na YouTube CS2 settings

                    otwórz pobrane
                    otwórz dokumenty
                    otwórz folder Sentinel

                    top procesy
                    co zjada RAM

                    test internetu
                    status sieci

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

        private async Task<ToolboxCommandResult>
            ExecuteImmediateAsync(
                string actionType,
                string command,
                Func<Task<ActionExecutionResult>> executor)
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
                    output.Add(FormatActionResponse(id, result)); task.CompleteStep();
                }
                catch (OperationCanceledException)
                { actionHistory.AddCancelled(id, "LAUNCH", target, "Przerwano weryfikację; aplikacja mogła już wystartować."); task.Complete("CANCELLED"); throw; }
                catch (Exception ex)
                { var result = ActionExecutionResult.Failure("Nie udało się uruchomić: " + target, ex.Message); actionHistory.AddResult(id, "LAUNCH", target, result, clock.ElapsedMilliseconds, parent); output.Add(FormatActionResponse(id, result)); failed = true; task.CompleteStep(); }
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
