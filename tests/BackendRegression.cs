using System.IO;
using System.Net.Http;
using System.Text;
using SentinelX.Models;
using SentinelX.Core;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;
using SentinelX.Services.Network;
using SentinelX.Services.PhoneCall;
using SentinelX.Services.PhoneBridge;
using SentinelX.Services.Web;

namespace SentinelX.Tests;

internal static class BackendRegression
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string privateAuditDirectory = Path.Combine(directory, "PrivateAudit");
        var privateAudit = new ActionHistoryService(privateAuditDirectory, () => false);
        privateAudit.AddRunning("private-id", "SKILL_LEARN", "private learning topic");
        Check(!Directory.Exists(Path.Combine(privateAuditDirectory, "History")),
            "Private-mode audit suppression must prevent both recovery writes and new command text from touching disk.");
        Check(!CommandUnderstanding.Repair("naucz się Kung Fu").Success &&
              !CommandUnderstanding.Repair("Dlaczego internet laguje").Success,
            "Typo repair must preserve user-supplied learning topics and natural-language executive goals.");
        var cancellationRegistry = new ActionTaskRegistry();
        using var taskA = cancellationRegistry.Begin("task-a", "Ping check", 1, CancellationToken.None);
        using var taskB = cancellationRegistry.Begin("task-b", "Download watch", 1, CancellationToken.None);
        var cancellationToolbox = new SentinelToolboxService(history: new ActionHistoryService(Path.Combine(directory, "CancelHistory")), tasks: cancellationRegistry);
        string ambiguousCancel = cancellationToolbox.CancelActiveTask();
        Check(ambiguousCancel.Contains("kilka aktywnych zadań", StringComparison.Ordinal) &&
              !taskA.Token.IsCancellationRequested && !taskB.Token.IsCancellationRequested,
            "A generic interrupt must ask which task when several are active instead of cancelling the wrong task.");
        cancellationToolbox.CancelActiveTask(taskId: "task-a");
        Check(taskA.Token.IsCancellationRequested && !taskB.Token.IsCancellationRequested,
            "A follow-up that identifies one listed task must cancel only that task.");
        cancellationToolbox.CancelActiveTask(all: true);
        Check(taskB.Token.IsCancellationRequested,
            "An explicit cancel-all request must signal every remaining active task token.");
        var history = new ActionHistoryService(directory);
        var memory = new ConversationMemoryService(Path.Combine(directory, "Memory"));
        bool allowExternalTraffic = true;
        var networkPolicyMemory = new ConversationMemoryService(Path.Combine(directory, "NetworkPolicyMemory"))
        { ExternalNetworkAllowedProvider = () => allowExternalTraffic };
        Check(networkPolicyMemory.ExternalNetworkAllowed, "The network permission provider should be queried dynamically while unlocked.");
        allowExternalTraffic = false;
        Check(!networkPolicyMemory.ExternalNetworkAllowed, "Changing local-only settings must block traffic immediately without recreating services.");
        networkPolicyMemory.SetPrivateMode(true);
        allowExternalTraffic = true;
        Check(networkPolicyMemory.ExternalNetworkAllowed, "Private Mode should keep local persistence off without implicitly overriding the separate local-only network lock.");
        var privateOnlineHandler = new StubWebHandler();
        using var privateOnlineWeb = new WebResearchTool(privateOnlineHandler, () => networkPolicyMemory.ExternalNetworkAllowed);
        string privateOnlineResult = await privateOnlineWeb.SearchAsync("online private session test");
        Check(privateOnlineResult.Contains("Official guide", StringComparison.Ordinal) && privateOnlineHandler.RequestCount > 0,
            "Private Mode may use online tools only when the separate local-only lock is explicitly disabled.");
        networkPolicyMemory.SetPrivateMode(false);
        var learningMemory = new ConversationMemoryService(Path.Combine(directory, "LearningMemory"))
        { ExternalNetworkAllowedProvider = () => false };
        var learningHandler = new StubWebHandler();
        using var learningWeb = new WebResearchTool(learningHandler, () => learningMemory.ExternalNetworkAllowed);
        var learningToolbox = new SentinelToolboxService(history: history, memory: learningMemory, webResearchTool: learningWeb,
            localAiAsk: (_, _, _) => Task.FromResult("Podstawy Kung Fu obejmują postawę, równowagę, koordynację i stopniowe ćwiczenie pod opieką instruktora. Ćwicz bezpiecznie i unikaj sparingu bez nadzoru."));
        var learnedSkill = await learningToolbox.ProcessAsync("naucz się Kung Fu");
        Check(learnedSkill.Handled && learnedSkill.Response.Contains("bez aktualnej weryfikacji", StringComparison.Ordinal) &&
              learningHandler.RequestCount == 0 && learningMemory.GetNotes().Any(x => x.Category == "umiejętność" && x.Text.Contains("Kung Fu", StringComparison.Ordinal) && x.Confidence <= 0.6 && x.LastConfirmedAt == null),
            "Skill learning should use the local model without web egress when locked, persist a clearly unverified skill note, and expose it to memory retrieval.");
        var failedLearningMemory = new ConversationMemoryService(Path.Combine(directory, "FailedLearningMemory"))
        { ExternalNetworkAllowedProvider = () => false };
        var failedSkillLearning = new SkillLearningService(failedLearningMemory, learningWeb,
            (_, _, _) => Task.FromResult("Nie udało się uzyskać odpowiedzi z lokalnego modelu. Wpisz modele AI."),
            () => false);
        string failedSkillResult = (await failedSkillLearning.TryProcessAsync("naucz się Kung Fu"))!;
        Check(failedSkillResult.Contains("Nie potwierdził poprawnej odpowiedzi", StringComparison.Ordinal) &&
              failedLearningMemory.GetNotes().Count == 0,
            "A local-model error response must never be stored as learned skill content.");
        var privateLearningMemory = new ConversationMemoryService(Path.Combine(directory, "PrivateLearningMemory"))
        { ExternalNetworkAllowedProvider = () => false };
        privateLearningMemory.SetPrivateMode(true);
        var privateSkillLearning = new SkillLearningService(privateLearningMemory, learningWeb,
            (_, _, _) => Task.FromResult("Podstawy Kung Fu obejmują równowagę, pracę nóg i bezpieczne ćwiczenia z instruktorem."));
        string privateSkillResult = (await privateSkillLearning.TryProcessAsync("learn about Kung Fu"))!;
        Check(privateSkillResult.Contains("tylko w RAM", StringComparison.Ordinal) && privateLearningMemory.GetNotes().Count == 0 &&
              privateLearningMemory.GetRelevantContext("Kung Fu").Contains("Kung Fu", StringComparison.Ordinal),
            "Private Mode should keep a learned skill in bounded session RAM/context and never persist it to the memory file.");
        var secretMemory = new ConversationMemoryService(Path.Combine(directory, "SecretMemory"));
        secretMemory.AddUserMessage("Moje hasło: VerySecret123!", "TEXT");
        Check(secretMemory.GetRecentEntries().Single().Text.Contains("[REDACTED]", StringComparison.Ordinal) &&
              !secretMemory.GetRecentEntries().Single().Text.Contains("VerySecret123", StringComparison.Ordinal), "Credential-like values must be masked before conversation persistence.");
        Check(secretMemory.AddNote("API key: abcdef123456") == NoteAddResult.Invalid, "Explicit memory must refuse to persist a likely secret.");
        string redactedBearer = SensitiveDataRedactor.Redact("Authorization: Bearer abcdefghijklmnopQRSTUV012345");
        string redactedPrivateKey = SensitiveDataRedactor.Redact("-----BEGIN PRIVATE KEY-----\nsecret-material\n-----END PRIVATE KEY-----");
        Check(!redactedBearer.Contains("abcdefghijklmnop", StringComparison.Ordinal) &&
              !redactedPrivateKey.Contains("secret-material", StringComparison.Ordinal) &&
              SensitiveDataRedactor.ContainsLikelySecret("access_token=abcdefgh12345678"),
            "Bearer tokens, token assignments and PEM private keys must be detected/redacted before local persistence or model context use.");
        var phoneHistory = new PhoneCallTranscriptStore(Path.Combine(directory, "phone-calls.jsonl"));
        var phoneTool = new PhoneCallTool(history: phoneHistory);
        var toolbox = new SentinelToolboxService(history: history, phoneCallTool: phoneTool);
        var router = new ControlledRouter();
        var engine = new ActionEngine(router, toolbox, history, memory, new OfflineAi());
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

        // Watch alerts require sustained samples, respect the configured cooldown and stay silent in focus mode.
        var alertPolicy = new WatcherAlertPolicy();
        var alertSettings = new WatchSettings { Enabled = true, CpuAlertPercent = 80, RamAlertPercent = 90, MinSecondsBeforeAlert = 6, CooldownMinutes = 5 };
        DateTimeOffset alertStart = DateTimeOffset.Now;
        string? alert = null;
        for (int i = 0; i < 4; i++)
            alert = alertPolicy.Observe(new(alertStart.AddSeconds(i * 2), 90, 40, 4, 16, float.NaN, "Game"), alertSettings, explicitlyRequested: false, focusModeActive: false);
        Check(alert != null && alert.Contains("korelacja", StringComparison.Ordinal),
            "Configured resource alerts should require consecutive, sustained evidence and state uncertainty.");
        alert = null;
        for (int i = 4; i < 8; i++)
            alert = alertPolicy.Observe(new(alertStart.AddSeconds(i * 2), 90, 40, 4, 16, float.NaN, "Game"), alertSettings, false, false);
        Check(alert == null, "The Watch cooldown must prevent immediate repeat notifications.");
        alertSettings.Enabled = false;
        alertPolicy.Reset();
        alert = alertPolicy.Observe(new(alertStart.AddMinutes(10), 100, 100, 16, 16, 100, "Game"), alertSettings, false, false);
        Check(alert == null, "Disabled automatic Watch must not notify without an explicit watch request.");
        alertSettings.Enabled = true;
        alert = alertPolicy.Observe(new(alertStart.AddMinutes(10), 100, 100, 16, 16, 100, "Game"), alertSettings, true, true);
        Check(alert == null, "Focus mode must suppress even explicitly requested Watch notifications.");

        string unavailableCall = (await phoneTool.TryProcessAsync("Zadzwoń do restauracji X i zarezerwuj jutro na 18 dla 4 osób"))!;
        Check(unavailableCall.Contains("Nie wykonano połączenia", StringComparison.Ordinal) &&
              !(await phoneHistory.GetRecentAsync()).Any(), "Without a real phone provider, the tool must refuse and never create a fake call-history entry.");
        var routedCall = await toolbox.ProcessAsync("Zadzwoń do restauracji X");
        Check(routedCall.Handled && routedCall.Response.Contains("Nie wykonano połączenia", StringComparison.Ordinal), "Phone calls must be routed as an ordinary toolbox tool instead of falling through to AI text.");
        var invalidVolume = await toolbox.ProcessAsync("ustaw głośność na 101%");
        Check(invalidVolume.Handled && invalidVolume.Response.Contains("od 0 do 100", StringComparison.Ordinal), "Master volume control must validate its reversible range before invoking Windows audio APIs.");
        var desktopAutomation = new DesktopAutomationTool();
        var desktopPermissions = new PermissionCenterService();
        var desktopToolbox = new SentinelToolboxService(history: history, permissions: desktopPermissions, desktopAutomationTool: desktopAutomation);
        string? smokeSetting = Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE");
        try
        {
            Environment.SetEnvironmentVariable("SENTINEL_UI_SMOKE", null);
            var blockedDesktopClick = await desktopToolbox.ProcessAsync("kliknij Zapłać");
            Check(blockedDesktopClick.Handled && blockedDesktopClick.Response.Contains("świeżego kontekstu okna", StringComparison.OrdinalIgnoreCase) &&
                  !desktopPermissions.HasPendingAction, "Desktop invocation must fail closed without a recently observed external window and must not stage a blind click.");
        }
        finally { Environment.SetEnvironmentVariable("SENTINEL_UI_SMOKE", smokeSetting); }
        var localOnlyMemory = new ConversationMemoryService(Path.Combine(directory, "LocalOnlyMemory"))
        { ExternalNetworkAllowedProvider = () => false };
        var localOnlyDesktop = new DesktopAutomationTool(() => localOnlyMemory.ExternalNetworkAllowed);
        var localOnlyDesktopToolbox = new SentinelToolboxService(history: history, memory: localOnlyMemory, desktopAutomationTool: localOnlyDesktop);
        var blockedDesktopHandoff = await localOnlyDesktopToolbox.ProcessAsync("kliknij Zaloguj");
        Check(blockedDesktopHandoff.Handled && blockedDesktopHandoff.Response.Contains("Tryb tylko lokalnie", StringComparison.Ordinal),
            "Local-only mode must stop UI Automation from handing clicks to external applications.");
        Check((await phoneTool.TryProcessAsync("/rozmowy historia"))!.Contains("Nie ma jeszcze", StringComparison.Ordinal), "Slash call-history command must be handled locally.");
        var completedStore = new PhoneCallTranscriptStore(Path.Combine(directory, "completed-phone-calls.jsonl"));
        var completedTool = new PhoneCallTool(new TestPhoneProvider(new(true, Summary: "Gotowe. 18:00, 4 osoby.", Transcript: "Sentinel: Czy jest stolik?\nRestauracja: Tak.")), completedStore);
        Check((await completedTool.TryProcessAsync("Zadzwoń do restauracji X"))!.StartsWith("Gotowe", StringComparison.Ordinal) &&
              (await completedStore.GetRecentAsync()).Count == 1, "Only a provider-confirmed completed call with transcript may be reported and archived.");
        var unconfirmedStore = new PhoneCallTranscriptStore(Path.Combine(directory, "unconfirmed-phone-calls.jsonl"));
        var unconfirmedTool = new PhoneCallTool(new TestPhoneProvider(new(false, Message: "Nie odebrano.")), unconfirmedStore);
        Check((await unconfirmedTool.TryProcessAsync("Zadzwoń do restauracji Y"))!.StartsWith("Połączenie nie zostało potwierdzone", StringComparison.Ordinal) &&
              !(await unconfirmedStore.GetRecentAsync()).Any(), "Unanswered calls must not be reported as completed or archived as transcripts.");

        string sentClipboardContext = "";
        int clipboardModelCalls = 0;
        var clipboard = new ClipboardContextTool(
            _ => Task.FromResult(new ClipboardReadResult(true, "Treść skopiowana przez użytkownika.")),
            (prompt, context, _) => { clipboardModelCalls++; sentClipboardContext = context; return Task.FromResult("Bezpieczne objaśnienie."); },
            () => true);
        Check(await clipboard.TryProcessAsync("wyjaśnij schowek") == "Bezpieczne objaśnienie." && clipboardModelCalls == 1 &&
              sentClipboardContext.Contains("Treść skopiowana", StringComparison.Ordinal),
            "Clipboard context must be read only after an explicit request and passed to the local summarizer as one-turn context.");
        var secretClipboard = new ClipboardContextTool(
            _ => Task.FromResult(new ClipboardReadResult(true, "hasło: VerySecret123")),
            (_, _, _) => { clipboardModelCalls++; return Task.FromResult("should not run"); }, () => true);
        string? secretClipboardResult = await secretClipboard.TryProcessAsync("podsumuj schowek");
        Check(secretClipboardResult!.Contains("Nie przekazałem go do modelu", StringComparison.Ordinal) && clipboardModelCalls == 1,
            "Clipboard secrets must not be sent to the local model.");
        Check(await clipboard.TryProcessAsync("co jest na ekranie") == null,
            "The clipboard reader must not guess when the user did not explicitly ask to use the clipboard.");

        var lockedWebHandler = new StubWebHandler();
        using var lockedWeb = new WebResearchTool(lockedWebHandler, externalNetworkAllowed: () => false);
        string blockedSearch = await lockedWeb.SearchAsync("private typed query");
        string blockedPageRead = await lockedWeb.ReadPageAsync("https://example.org/private-page", summarize: false);
        Check(blockedSearch.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase) &&
              blockedPageRead.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase) && lockedWebHandler.RequestCount == 0,
            "Local-only mode must block web search and page reads before the HTTP handler receives typed query content or sends a URL.");
        var lockedNetwork = new NetworkDiagnosticService(() => false);
        var blockedInternetTest = await lockedNetwork.TestInternetAsync(CancellationToken.None);
        var blockedDnsTest = await lockedNetwork.TestDnsAsync("example.com");
        Check(!blockedInternetTest.Success && blockedInternetTest.Message.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase) &&
              !blockedDnsTest.Success && blockedDnsTest.Message.Contains("tryb tylko lokalnie", StringComparison.OrdinalIgnoreCase),
            "Local-only mode must report external network probes as blocked before sending DNS, ICMP, or HTTPS.");
        var lockedLauncher = new AppLauncherService(externalNetworkAllowed: () => false);
        var blockedProcessControl = await new ProcessToolService(() => false).CloseAppAsync("discord");
        Check(!blockedProcessControl.Success && blockedProcessControl.Message.Contains("Tryb tylko lokalnie", StringComparison.Ordinal),
            "Local-only mode must not send close requests to other applications.");
        var blockedUrl = await lockedLauncher.LaunchAsync("https://example.org");
        Check(!blockedUrl.Success && blockedUrl.Message.Contains("Tryb tylko lokalnie", StringComparison.Ordinal) &&
              blockedUrl.Evidence.Contains("Nie uruchomiono", StringComparison.Ordinal),
            "Local-only mode must block external URL handoff before starting a browser.");
        string sourceOnlyRoot = Path.Combine(directory, "SourceOnlyPrograms");
        var sourceOnly = await new ProgramBuilderService(sourceOnlyRoot, () => false).BuildAsync("Notatnik", null, CancellationToken.None);
        Check(sourceOnly.Status == "SOURCE_READY" && sourceOnly.Executable == null && Directory.Exists(sourceOnly.ProjectDirectory) &&
              !Directory.EnumerateFiles(sourceOnly.ProjectDirectory, "*.exe", SearchOption.AllDirectories).Any() &&
              File.Exists(Path.Combine(sourceOnly.ProjectDirectory, "source-manifest.json")),
            "The coding workflow may generate auditable source files offline, but must never compile or create EXE artifacts.");
        string routedSourceRoot = Path.Combine(directory, "RoutedSources");
        var sourceToolbox = new SentinelToolboxService(history: history,
            programBuilderService: new ProgramBuilderService(routedSourceRoot, () => false));
        var sourceRequest = await sourceToolbox.ProcessAsync("zbuduj program pomodoro");
        Check(sourceRequest.Handled && sourceRequest.Response.Contains("nie wygenerowałem pliku EXE", StringComparison.Ordinal) &&
              !Directory.EnumerateFiles(routedSourceRoot, "*.exe", SearchOption.AllDirectories).Any(),
            "Natural-language source generation must be available in the main toolbox without building an executable.");
        var currentWebHandler = new StubWebHandler("https://1.1.1.1/guide");
        using var currentWeb = new WebResearchTool(currentWebHandler);
        string currentAnswer = (await currentWeb.TryProcessAsync("Jaka będzie pogoda jutro w Krakowie"))!;
        Check(currentAnswer.Contains("Zestawienie", StringComparison.Ordinal) && currentWebHandler.RequestCount >= 2,
            "Current-information questions should automatically invoke sourced research instead of trusting model memory.");
        using var blockedCurrentWeb = new WebResearchTool(new StubWebHandler(), () => false);
        string blockedCurrentAnswer = (await blockedCurrentWeb.TryProcessAsync("Jaka będzie pogoda jutro w Krakowie"))!;
        Check(blockedCurrentAnswer.Contains("tylko lokalnie", StringComparison.OrdinalIgnoreCase),
            "Automatic current-information routing must respect the local-only network lock.");
        var haHandler = new HomeAssistantTestHandler();
        var privateDeviceHistory = new ActionHistoryService(Path.Combine(directory, "PrivateDeviceHistory"), () => false);
        using var deviceTool = new DeviceControlTool(externalNetworkAllowed: () => false, persistenceAllowed: () => false,
            history: privateDeviceHistory, baseUrl: "http://192.168.1.50:8123", accessToken: new string('x', 40),
            registryPath: Path.Combine(directory, "Devices", "paired.json"), handler: haHandler);
        string haDiscovery = await deviceTool.TryProcessAsync("wykryj urządzenia") ?? "";
        Check(haDiscovery.Contains("media_player.salon", StringComparison.Ordinal) &&
              !haDiscovery.Contains("switch.garage_door", StringComparison.Ordinal) && !haDiscovery.Contains("scene.goodnight", StringComparison.Ordinal),
            "Device discovery must list only supported entities and exclude generic switches/scenes from the configured Home Assistant instance.");
        string unsafePairing = await deviceTool.TryProcessAsync("sparuj brama jako switch.garage_door") ?? "";
        Check(!unsafePairing.Contains("Sparowano", StringComparison.Ordinal),
            "The adapter must refuse pairing potentially hazardous generic switches and multi-device scenes.");
        string pairing = await deviceTool.TryProcessAsync("sparuj tv jako media_player.salon") ?? "";
        Check(pairing.Contains("Sparowano", StringComparison.Ordinal) && pairing.Contains("tylko w tej sesji", StringComparison.Ordinal) &&
              !pairing.Contains(new string('x', 40), StringComparison.Ordinal) && !File.Exists(Path.Combine(directory, "Devices", "paired.json")),
            "Manual pairing must verify the selected entity, never echo its authorization token, and honor disabled registry persistence.");
        Check(await deviceTool.TryProcessAsync("uruchom Brave") == null,
            "Device routing must not intercept unrelated app-launch intents.");
        string deviceAction = await deviceTool.TryProcessAsync("włącz TV") ?? "";
        Check(deviceAction.Contains("Zweryfikowano", StringComparison.Ordinal) && haHandler.PowerOn &&
              haHandler.LastAuthorization == "Bearer " + new string('x', 40) &&
              haHandler.RequestUris.All(uri => uri.Host == "192.168.1.50") && !File.Exists(privateDeviceHistory.HistoryPath),
            "Authorized device commands must use only the configured integration, verify state, and honor private audit suppression.");
        string macroPath = Path.Combine(directory, "PhoneBridge", "macros.json");
        Directory.CreateDirectory(Path.GetDirectoryName(macroPath)!);
        File.WriteAllText(macroPath, "{\"macros\":[{\"name\":\"Film\",\"phrases\":[\"tryb testowy\"],\"steps\":[{\"intent\":\"TV_POWER_ON\",\"parameters\":{\"target\":\"tv\"}}]}]}");
        var phoneSkills = new PhoneSkillExecutor(new SystemMonitor(), new AppLauncherService(externalNetworkAllowed: () => false),
            deviceTool, macroPath: macroPath);
        PhoneSkillResult phoneTemperature = await phoneSkills.ExecuteAsync("PC_GPU_TEMP", new Dictionary<string, string>());
        PhoneSkillResult phoneTv = await phoneSkills.ExecuteAsync("TV_POWER_ON", new Dictionary<string, string> { ["target"] = "tv" });
        PhoneSkillResult phoneMacro = await phoneSkills.ExecuteAsync("PHONE_TEXT", new Dictionary<string, string> { ["text"] = "tryb testowy" });
        PhoneSkillResult unsafePhoneLaunch = await phoneSkills.ExecuteAsync("PC_LAUNCH_APP", new Dictionary<string, string> { ["target"] = @"C:\\Windows\\System32\\cmd.exe" });
        File.WriteAllText(macroPath, "{\"macros\":[{\"name\":\"Unsafe\",\"phrases\":[\"tryb niebezpieczny\"],\"steps\":[{\"intent\":\"PC_SHUTDOWN\"}]}]}");
        PhoneSkillResult unsafeMacro = await phoneSkills.ExecuteAsync("PHONE_TEXT", new Dictionary<string, string> { ["text"] = "tryb niebezpieczny" });
        File.WriteAllText(macroPath, "{\"macros\":[{\"name\":\"Invalid late step\",\"phrases\":[\"niepoprawny pozny krok\"],\"steps\":[{\"intent\":\"TV_POWER_ON\",\"parameters\":{\"target\":\"tv\"}},{\"intent\":\"PC_LAUNCH_APP\",\"parameters\":{\"target\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}}]}]}");
        int requestsBeforeInvalidMacro = haHandler.RequestUris.Count;
        PhoneSkillResult invalidLateMacro = await phoneSkills.ExecuteAsync("PHONE_TEXT", new Dictionary<string, string> { ["text"] = "niepoprawny pozny krok" });
        Check(phoneSkills.Skills.Any(x => x.Name == "PCMonitorSkill") && phoneSkills.Skills.Any(x => x.Name == "TVControlSkill") &&
              phoneTemperature.Status == "UNSUPPORTED" && phoneTv.Status == "SUCCESS" && phoneMacro.Status == "SUCCESS" &&
              unsafePhoneLaunch.Status == "FAILED" && unsafeMacro.Status == "FAILED" && invalidLateMacro.Status == "FAILED" &&
              haHandler.RequestUris.Count == requestsBeforeInvalidMacro,
            "Phone skills must use named intents, reject unavailable sensors/arbitrary app paths/destructive macro steps, verify TV state, and preflight all macro steps before the first action.");
        string pairedRegistry = Path.Combine(directory, "Devices", "persistent-paired.json");
        string persistenceResponse;
        using (var persistentDeviceTool = new DeviceControlTool(externalNetworkAllowed: () => false,
            persistenceAllowed: () => true, baseUrl: "http://192.168.1.50:8123", accessToken: new string('y', 40),
            registryPath: pairedRegistry, handler: new HomeAssistantTestHandler()))
            persistenceResponse = (await persistentDeviceTool.TryProcessAsync("sparuj tv jako media_player.salon")) ?? "";
        string pairedJson = File.ReadAllText(pairedRegistry);
        using var reloadedDeviceTool = new DeviceControlTool(externalNetworkAllowed: () => false,
            persistenceAllowed: () => true, baseUrl: "http://192.168.1.50:8123", accessToken: new string('z', 40),
            registryPath: pairedRegistry, handler: new HomeAssistantTestHandler());
        Check(persistenceResponse.Contains("Trwale zapisano", StringComparison.Ordinal) && reloadedDeviceTool.GetPairedDevices().Count == 1 &&
              !pairedJson.Contains("Salon TV", StringComparison.Ordinal) && !pairedJson.Contains(new string('y', 40), StringComparison.Ordinal),
            "Durable device memory must reload only the user alias/entity mapping, not the friendly name or credentials.");
        var routedHaHandler = new HomeAssistantTestHandler();
        var deviceHistory = new ActionHistoryService(Path.Combine(directory, "DeviceHistory"));
        using var routedDeviceTool = new DeviceControlTool(externalNetworkAllowed: () => false, persistenceAllowed: () => false,
            history: deviceHistory, baseUrl: "http://192.168.1.50:8123", accessToken: new string('q', 40),
            registryPath: Path.Combine(directory, "Devices", "routed-paired.json"), handler: routedHaHandler);
        await routedDeviceTool.TryProcessAsync("sparuj tv jako media_player.salon");
        var deviceToolbox = new SentinelToolboxService(history: history, deviceControlTool: routedDeviceTool);
        ToolboxCommandResult routedDeviceAction = await deviceToolbox.ProcessAsync("włącz TV");
        string deviceAudit = File.ReadAllText(deviceHistory.HistoryPath);
        Check(routedDeviceAction.Handled && routedDeviceAction.Response.Contains("Zweryfikowano", StringComparison.Ordinal) && routedHaHandler.PowerOn &&
              deviceAudit.Contains("DEVICE_CONTROL", StringComparison.Ordinal) && deviceAudit.Contains("VERIFIED", StringComparison.Ordinal) &&
              !deviceAudit.Contains(new string('q', 40), StringComparison.Ordinal),
            "The existing toolbox must route device actions through the adapter, verify readback, and write a credential-free action audit.");
        var blockedHaHandler = new HomeAssistantTestHandler();
        using var blockedExternalHa = new DeviceControlTool(externalNetworkAllowed: () => false,
            baseUrl: "https://ha.example.com:8123", accessToken: new string('r', 40),
            registryPath: Path.Combine(directory, "Devices", "blocked-paired.json"), handler: blockedHaHandler);
        string blockedHaResult = await blockedExternalHa.TryProcessAsync("wykryj urządzenia") ?? "";
        Check(blockedHaResult.Contains("tylko lokalnie", StringComparison.OrdinalIgnoreCase) && blockedHaHandler.RequestUris.Count == 0,
            "Local-only policy must block an external Home Assistant request before transmitting credentials or contacting the host.");
        using var web = new WebResearchTool(new StubWebHandler());
        string webResults = await web.SearchAsync("sentinel regression");
        Check(webResults.Contains("Official guide", StringComparison.Ordinal) && webResults.Contains("https://example.org/guide", StringComparison.Ordinal) && webResults.Contains("Useful & current summary", StringComparison.Ordinal), "Web search must return readable sourced snippets.");
        Check(web.TryResolveRecentResult("otwórz pierwszą stronę", out string recentUrl, out bool readRecent, out _) && recentUrl == "https://example.org/guide" && !readRecent, "Browser follow-up must resolve the selected recent result without asking for its URL again.");
        Check(web.TryExtractOpenUrl("otwórz stronę https://example.org/guide", out string openUrl) && openUrl == "https://example.org/guide", "A direct request to open a public URL should launch the browser rather than be mistaken for an app name.");
        Check(web.TryResolveRecentResult("podsumuj pierwszą stronę", out _, out bool summarizeRead, out bool shouldSummarize) && summarizeRead && shouldSummarize, "Page-reading follow-up must retain its requested summary mode.");
        string blockedLocalPage = await web.ReadPageAsync("http://127.0.0.1:11434/api/tags");
        Check(blockedLocalPage.Contains("Odrzucono adres lokalny", StringComparison.Ordinal), "Web page reader must block loopback/private-network targets.");
        using var routedWeb = new WebResearchTool(new StubWebHandler());
        var webToolbox = new SentinelToolboxService(history: history, webResearchTool: routedWeb);
        var routedSearch = await webToolbox.ProcessAsync("wyszukaj w internecie test");
        Check(routedSearch.Handled && routedSearch.Response.Contains("Official guide", StringComparison.Ordinal), "Natural web-search requests must select the web tool without extra UI.");
        Check(!(await webToolbox.ProcessAsync("szukaj wszystkiego: test")).Handled,
            "Unified local search must bypass the broad web-search prefix and reach the workspace router.");
        var contextualMemory = new ConversationMemoryService(Path.Combine(directory, "SearchContext"));
        contextualMemory.AddUserMessage("Chcę znaleźć informacje o sentinel regression.", "TEXT");
        using var contextualWeb = new WebResearchTool(new StubWebHandler());
        var contextualToolbox = new SentinelToolboxService(history: history, webResearchTool: contextualWeb, memory: contextualMemory);
        var contextualSearch = await contextualToolbox.ProcessAsync("znajdź mi to");
        Check(contextualSearch.Handled && contextualSearch.Response.Contains("ostatniego tematu rozmowy", StringComparison.Ordinal) &&
              contextualSearch.Response.Contains("Official guide", StringComparison.Ordinal), "Contextual 'find that' requests must reuse the recent user topic rather than search the word 'that'.");
        Check(AppLaunchPlan.TryParseCommand("Uruchom Brave i YouTube", out string launchTargets) && AppLaunchPlan.Parse(launchTargets).Targets.Count == 2, "One natural command must build a two-tool launch plan.");

        using var metricSource = new SystemMonitor();
        using var performance = new PerformanceHistoryService(metricSource);
        var diagnosticsToolbox = new SentinelToolboxService(history: history, systemMonitor: metricSource, performanceHistory: performance);
        var calculatorResult = await diagnosticsToolbox.ProcessAsync("oblicz 287 × 491");
        Check(calculatorResult.Handled && calculatorResult.Response.Contains("140917", StringComparison.Ordinal) &&
              calculatorResult.Response.Contains("BigInteger", StringComparison.Ordinal),
            "Arithmetic should route deterministically to the bounded calculator and independently verify integer multiplication.");
        var lagDiagnosis = await diagnosticsToolbox.ProcessAsync("CS2 mi ścina");
        Check(lagDiagnosis.Handled && lagDiagnosis.Response.Contains("hipotez", StringComparison.OrdinalIgnoreCase) &&
              lagDiagnosis.Response.Contains("Nie zmieniono ustawień", StringComparison.Ordinal), "Game-lag requests must return measured data and cautious hypotheses without random system changes.");
        var unsupportedTemperature = await diagnosticsToolbox.ProcessAsync("Pokaż temperaturę GPU");
        Check(unsupportedTemperature.Handled && unsupportedTemperature.Response.Contains("Temperatura GPU nie jest dostępna", StringComparison.OrdinalIgnoreCase) &&
              !unsupportedTemperature.Response.Contains("Temperatura GPU: 0", StringComparison.OrdinalIgnoreCase),
              "Unavailable temperature must be stated explicitly, without asserting that legitimate CPU/GPU utilization percentages are nonzero.");
        string executiveRoot = Path.Combine(directory, "ExecutiveAgent");
        var executiveGoals = new GoalMemoryService(Path.Combine(executiveRoot, "goals.json"), () => false);
        using var executiveDownloads = new DownloadContextService(Path.Combine(executiveRoot, "Downloads"));
        using var executiveWatchers = new WatcherService(executiveDownloads, executiveGoals, performance);
        var executiveMemory = new ConversationMemoryService(Path.Combine(executiveRoot, "Memory"));
        executiveMemory.SetPrivateMode(true);
        var executiveNetwork = new TestNetworkService();
        using var executive = new AutopilotService(performance, new ProcessToolService(() => false), executiveNetwork,
            new ActionTaskRegistry(), executiveGoals, new EventMemoryService(), executiveWatchers, executiveMemory);
        string executiveResult = (await executive.TryProcessAsync("Dlaczego internet laguje"))!;
        Check(executiveResult.Contains("Punktowe testy", StringComparison.Ordinal) &&
              executiveResult.Contains("Procesy według Working Set", StringComparison.Ordinal) &&
              executiveResult.Contains("Nie zmieniłem ustawień", StringComparison.Ordinal) && executiveNetwork.InternetChecks == 1 &&
              executiveGoals.GetRecent(1).Single().Status != "RUNNING",
            "The executive goal workflow should combine local context, an allowed network probe and process inspection, verify completion, and make no unsolicited changes.");
        var focusMode = new GameFocusModeService();
        Check(focusMode.Enable().Contains("Nie zmieniono ustawień Windows", StringComparison.Ordinal) && focusMode.IsActive &&
              focusMode.Disable().Contains("nie były zmieniane", StringComparison.Ordinal) && !focusMode.IsActive, "Game focus profile should be reversible and explicitly limited to Sentinel-owned behavior.");

        string goalPath = Path.Combine(directory, "Autopilot", "goals.json");
        var goalStore = new GoalMemoryService(goalPath);
        var waitingGoal = goalStore.Start("Notify when download completes", ["stable_download"], persist: true);
        goalStore.MarkWaiting(waitingGoal.Id, "stable_download", "Waiting for a stable new file.", "Verify file metadata.");
        var restoredGoal = new GoalMemoryService(goalPath).GetRecent(1).Single();
        Check(restoredGoal.Status == "WAITING" && restoredGoal.NextStep == "Verify file metadata.",
            "A durable waiting goal must restore its checkpoint and next step after restart.");
        Check(new GoalMemoryService(goalPath, () => false).GetRecent(1).Count == 0,
            "Private sessions must not load durable goal text into memory.");
        string graphPath = Path.Combine(directory, "Autopilot", "graph.json");
        var graphStore = new GoalMemoryService(graphPath);
        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["summary"] = ["probe-a", "probe-b"]
        };
        var graphGoal = graphStore.Start("Parallel read-only checks", ["probe-a", "probe-b", "summary"], true, graph);
        Check(graphGoal.Steps.Single(x => x.Name == "summary").DependsOn.SequenceEqual(new[] { "probe-a", "probe-b" }) &&
              new GoalMemoryService(graphPath).GetRecent(1).Single().Steps.Single(x => x.Name == "summary").DependsOn.SequenceEqual(new[] { "probe-a", "probe-b" }),
            "Goal checkpoints must persist explicit acyclic dependencies for parallel independent probes.");
        bool rejectedCycle = false;
        try
        {
            graphStore.Start("Invalid graph", ["a", "b"], false, new Dictionary<string, IReadOnlyList<string>>
            { ["a"] = ["b"], ["b"] = ["a"] });
        }
        catch (ArgumentException) { rejectedCycle = true; }
        Check(rejectedCycle, "Cyclic goal dependencies must be rejected before execution.");
        string resumedDownloadsPath = Path.Combine(directory, "ResumeDownloads");
        Directory.CreateDirectory(resumedDownloadsPath);
        DateTimeOffset waitStarted = DateTimeOffset.Now.AddMinutes(-1);
        string matchingDownload = Path.Combine(resumedDownloadsPath, "finished-report.zip");
        string unrelatedDownload = Path.Combine(resumedDownloadsPath, "finished-image.png");
        File.WriteAllText(matchingDownload, "download complete"); File.WriteAllText(unrelatedDownload, "other file");
        File.SetLastWriteTimeUtc(matchingDownload, waitStarted.AddSeconds(5).UtcDateTime);
        File.SetLastWriteTimeUtc(unrelatedDownload, waitStarted.AddSeconds(5).UtcDateTime);
        using var resumedDownloads = new DownloadContextService(resumedDownloadsPath);
        Check(resumedDownloads.SeedRecentFilesSince(waitStarted, "report") == 1,
            "A persisted download watcher must recheck only matching files changed since its checkpoint after app restart.");
        string privateGoalPath = Path.Combine(directory, "PrivateAutopilot", "goals.json");
        var privateGoalStore = new GoalMemoryService(privateGoalPath, () => false);
        privateGoalStore.Start("Private download goal", ["stable_download"], persist: true);
        Check(!File.Exists(privateGoalPath) || !File.ReadAllText(privateGoalPath).Contains("Private download goal", StringComparison.Ordinal),
            "Private or ephemeral goals must not be persisted to disk.");
        string unsupportedResumePath = Path.Combine(directory, "UnsupportedResume", "goals.json");
        var unsupportedSource = new GoalMemoryService(unsupportedResumePath);
        unsupportedSource.Start("Usuń stare pliki", ["delete_files"], persist: true);
        var unsupportedRestored = new GoalMemoryService(unsupportedResumePath);
        using var unsupportedDownloads = new DownloadContextService(Path.Combine(directory, "UnsupportedResume", "Downloads"));
        using var unsupportedWatchers = new WatcherService(unsupportedDownloads, unsupportedRestored, performance);
        var unsupportedNetwork = new TestNetworkService();
        using var unsupportedAutopilot = new AutopilotService(performance, new ProcessToolService(() => false), unsupportedNetwork,
            new ActionTaskRegistry(), unsupportedRestored, new EventMemoryService(), unsupportedWatchers,
            new ConversationMemoryService(Path.Combine(directory, "UnsupportedResume", "Memory")));
        string unsupportedResume = (await unsupportedAutopilot.TryProcessAsync("wznów ostatni cel"))!;
        Check(unsupportedResume.Contains("nie ma on bezpiecznego automatycznego wznowienia", StringComparison.Ordinal) && unsupportedNetwork.InternetChecks == 0,
            "Unsupported interrupted goals must stop for user input instead of replaying potentially destructive actions.");
        string automationRoot = Path.Combine(directory, "AutomationRules");
        Directory.CreateDirectory(automationRoot);
        string automationDownloadsPath = Path.Combine(automationRoot, "Downloads");
        Directory.CreateDirectory(automationDownloadsPath);
        using var automationDownloads = new DownloadContextService(automationDownloadsPath);
        string automationPath = Path.Combine(automationRoot, "rules.json");
        var automationNotices = new List<string>();
        using var rulePerformance = new PerformanceHistoryService(metricSource);
        using (var automations = new AutomationRuleService(rulePerformance, automationDownloads, automationPath, () => true))
        {
            automations.NoticeRaised += automationNotices.Add;
            Check(automations.TryProcess("powiadom mnie gdy CPU przekroczy 90% przez 2 sekundy")!.Contains("lokalnie", StringComparison.Ordinal),
                "A natural-language threshold rule should be bounded, saved, and notification-only.");
            Check(automations.TryProcess("powiadom mnie gdy pobieranie się zakończy")!.Contains("powiadomienie", StringComparison.Ordinal),
                "A stable-download automation should use the same safe notification-only action.");
            Check(automations.GetRules().Count == 2, "Automation rules should remain within a small explicit registry.");
            DateTimeOffset sampleAt = DateTimeOffset.Now;
            automations.ObservePerformance(new PerformanceSample(sampleAt, 95, 30, 5, 16, 10, ""));
            Check(automationNotices.Count == 0, "A performance rule must not fire before its duration condition is met.");
            automations.ObservePerformance(new PerformanceSample(sampleAt.AddSeconds(2), 95, 30, 5, 16, 10, ""));
            Check(automationNotices.Count == 1 && automationNotices[0].Contains("CPU", StringComparison.Ordinal),
                "A sustained condition should raise one restrained local notification.");
            automations.ObserveDownload(new RecentDownload(Path.Combine(automationDownloadsPath, "report.zip"), sampleAt.AddSeconds(3)));
            Check(automationNotices.Count == 2 && automationNotices[1].Contains("report.zip", StringComparison.Ordinal),
                "A matching download event should trigger its user-defined notification rule.");
        }
        using (var reloadedAutomations = new AutomationRuleService(rulePerformance, automationDownloads, automationPath, () => true))
            Check(reloadedAutomations.GetRules().Count == 2, "Allowed automation rules should survive restart through bounded local persistence.");
        string privateAutomationPath = Path.Combine(automationRoot, "private-rules.json");
        using (var privateAutomations = new AutomationRuleService(rulePerformance, automationDownloads, privateAutomationPath, () => false))
        {
            privateAutomations.TryProcess("powiadom mnie gdy pobieranie się zakończy");
            Check(privateAutomations.GetRules().Count == 1 && !File.Exists(privateAutomationPath),
                "Private automation rules may work in RAM but must not be persisted or loaded from disk.");
        }
        using (var hiddenPrivateAutomations = new AutomationRuleService(rulePerformance, automationDownloads, automationPath, () => false))
            Check(hiddenPrivateAutomations.GetRules().Count == 0, "Private sessions must not load durable automation rule text.");

        var eventMemory = new EventMemoryService();
        var highSamples = Enumerable.Range(0, 6).Select(i => new PerformanceSample(DateTimeOffset.Now.AddSeconds(-i * 2), 96, 96, 15, 16, 99, "cs2")).ToArray();
        string firstEvent = eventMemory.RecordPerformanceCheck("cs2", highSamples);
        Check(firstEvent.Contains("single co-occurrence", StringComparison.OrdinalIgnoreCase) &&
              firstEvent.Contains("correlation", StringComparison.OrdinalIgnoreCase),
            "Event Memory must qualify a repeated resource signal in one check as a single correlation, not a cause.");

        string reminderPath = Path.Combine(directory, "reminders.json");
        var reminderService = new ReminderService(reminderPath);
        Check(reminderService.TryProcess("Przypomnij mi za 2 minuty o sprawdzeniu pobierania")!.StartsWith("Ustawiono", StringComparison.Ordinal), "Relative reminders should be stored locally.");
        Check(reminderService.TryProcess("Przypomnij mi za 10 minut o haśle 1234")!.Contains("Nie zapiszę", StringComparison.Ordinal), "Reminder storage must reject likely credentials and one-time codes.");
        Check(reminderService.TakeDue(DateTimeOffset.Now.AddMinutes(3)).Count == 1 && reminderService.List().Contains("Nie ma oczekujących", StringComparison.Ordinal), "Due reminders should be delivered once and removed durably.");
        string cancelText = reminderService.TryProcess("Przypomnij mi za 10 minut o sprawdzeniu aktualizacji")!;
        string reminderId = System.Text.RegularExpressions.Regex.Match(cancelText, @"id ([a-f0-9]{10})").Groups[1].Value;
        Check(reminderId.Length == 10 && reminderService.TryProcess("anuluj przypomnienie " + reminderId)!.StartsWith("Anulowano", StringComparison.Ordinal), "Reminder cancellation should require the exact shown ID.");

        engine.EmergencyStop();
        var blocked = await engine.ExecuteAsync("anything");
        Check(blocked.Action == null && router.Calls == 0, "Emergency stop must prevent routing.");
        engine.Resume();
        await engine.ExecuteAsync("potwierdz", fromVoice: true);
        Check(router.Calls == 0, "Voice approval must not reach a tool.");
        var spoofed = await engine.ExecuteAsync("answer");
        Check(spoofed.Action?.Status == ActionStatus.Unverified, "A model saying VERIFIED is not proof.");
        Check(history.GetRecentEntries().Any(x => x.ActionId == spoofed.Action!.ActionId && x.Status == "UNVERIFIED"), "Request must be persisted.");
        router.Wait = true;
        var first = engine.ExecuteAsync("wait");
        await router.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await engine.ExecuteAsync("second");
        Check(second.Action == null, "Concurrent requests must not execute.");
        var stopRequest = await engine.ExecuteAsync("anuluj");
        Check(stopRequest.Text.Contains("Przerwano", StringComparison.Ordinal), "Natural-language cancel must bypass the busy lane and reach the running action.");
        engine.EmergencyStop();
        var cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancelled.Action?.Status == ActionStatus.Cancelled && engine.IsStopped && !engine.IsBusy, "Stop must cancel and remain latched.");
        engine.Resume(); router.Wait = false;
        var resumed = await engine.ExecuteAsync("again");
        Check(resumed.Action?.Status == ActionStatus.Unverified, "Engine must recover after cancellation.");
        var secretAudit = await engine.ExecuteAsync("API key: KeySecret123456");
        Check(history.GetRecentEntries().Single(x => x.ActionId == secretAudit.Action!.ActionId).Command.Contains("[REDACTED]", StringComparison.Ordinal) &&
              !history.GetRecentEntries().Single(x => x.ActionId == secretAudit.Action!.ActionId).Command.Contains("KeySecret123456", StringComparison.Ordinal),
              "Likely credentials in user action commands must be redacted from the persistent action audit.");
        var store = new AppSettingsService(Path.Combine(directory, "Settings"));
        Check(store.Settings.Memory.LocalOnlyMode, "A fresh settings profile must enable local-only network blocking by default.");
        var localOnlyField = SettingsCatalog.Create(store).Single(x => x.Label == "Tryb tylko lokalnie (blokuj ruch zewnętrzny)");
        Check(localOnlyField.Write("false") == null && !store.Settings.Memory.LocalOnlyMode && localOnlyField.Write("true") == null && store.Settings.Memory.LocalOnlyMode,
            "The local-only privacy lock must be directly configurable in the existing Settings catalog.");
        var field = SettingsCatalog.Create(store).Single(x => x.Label == "Próg VAD");
        Check(field.Write("NaN") != null && field.Write("2") != null, "Invalid VAD values must be rejected.");
        Check(field.Write("0,35") == null, "Polish decimal separator must be accepted.");
        store.Save();
        var roundTripSettings = new AppSettingsService(Path.Combine(directory, "Settings"));
        Check(roundTripSettings.Settings.Voice.VadThreshold == .35 && roundTripSettings.Settings.Memory.LocalOnlyMode,
            "Settings, including the local-only default, must round-trip.");
        File.WriteAllText(store.SettingsPath, "broken JSON");
        var damaged = new AppSettingsService(Path.Combine(directory, "Settings"));
        Check(damaged.LastError != null, "Corrupted settings must be reported.");
        var files = new FileWorkspaceService(directory, directory, history);
        Check((await files.ProcessAsync("utwórz plik original.txt: Zażółć gęślą jaźń", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Create + verify UTF-8 file.");
        Check((await files.ProcessAsync("Skopiuj ten plik jako copy.txt", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Copy + verify file.");
        Check(File.ReadAllText(Path.Combine(directory, "CreatedFiles", "copy.txt")) == "Zażółć gęślą jaźń", "Copy must preserve exact text.");
        Check((await files.ProcessAsync("przenieś ten plik jako moved.txt", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Move + verify file.");
        Check(!File.Exists(Path.Combine(directory, "CreatedFiles", "copy.txt")), "Move must remove the old name.");
        Check((await files.ProcessAsync("skopiuj ten plik jako original.txt", CancellationToken.None))!.StartsWith("Nie udało się", StringComparison.Ordinal), "Copy must never overwrite.");
        Check((await files.ProcessAsync("przenieś ten plik jako ../escape.txt", CancellationToken.None))!.StartsWith("Nie udało się", StringComparison.Ordinal), "Move must reject traversal.");
        Check((await files.ProcessAsync("znajdź plik moved", CancellationToken.None))!.Contains("moved.txt"), "Search Sentinel workspace.");

        string testDesktop = Path.Combine(directory, "UserDesktop"), testDocuments = Path.Combine(directory, "UserDocuments"), testDownloads = Path.Combine(directory, "UserDownloads");
        Directory.CreateDirectory(testDesktop); Directory.CreateDirectory(testDocuments); Directory.CreateDirectory(testDownloads);
        var contextualFiles = new FileWorkspaceService(directory, testDesktop, history, testDocuments, testDownloads);
        Check((await contextualFiles.ProcessAsync("utwórz plik trip-plan.txt na pulpicie: budżet wakacje wyjazd sierpień", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Create file in Desktop.");
        Check((await contextualFiles.ProcessAsync("znajdź dokument o budżet wakacje", CancellationToken.None))!.Contains("trip-plan.txt", StringComparison.Ordinal), "Description search must inspect common user folders and text contents.");
        string yesterdayFile = Path.Combine(testDownloads, "weekly-report.txt");
        File.WriteAllText(yesterdayFile, "weekly report");
        File.SetLastWriteTime(yesterdayFile, DateTime.Today.AddDays(-1).AddHours(12));
        Check((await contextualFiles.ProcessAsync("znajdź pobrany wczoraj plik", CancellationToken.None))!.Contains("weekly-report.txt", StringComparison.Ordinal), "Natural file search should use modification-date metadata for yesterday's download query.");
        Check((await contextualFiles.ProcessAsync("znajdź dokument o budżet wakacje", CancellationToken.None))!.Contains("trip-plan.txt", StringComparison.Ordinal), "Description search must inspect common user folders and text contents.");
        Check((await contextualFiles.ProcessAsync("przenieś go do folderu Dokumenty", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Pronoun context must move the selected file to Documents.");
        Check(File.Exists(Path.Combine(testDocuments, "trip-plan.txt")) && !File.Exists(Path.Combine(testDesktop, "trip-plan.txt")), "File move must update the physical location.");
        Check((await contextualFiles.ProcessAsync("zmień jego nazwę na summer-plan.txt", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Pronoun context must rename the selected file.");
        Check(File.Exists(Path.Combine(testDocuments, "summer-plan.txt")), "Rename must update the physical file.");
        Check((await contextualFiles.ProcessAsync("utwórz folder Projekt podróż w dokumentach", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Create folder in Documents.");
        Check(Directory.Exists(Path.Combine(testDocuments, "Projekt podróż")), "Create folder in Documents.");
        string undoRoot = Path.Combine(directory, "UndoFiles");
        Directory.CreateDirectory(undoRoot);
        var undoPermissions = new PermissionCenterService();
        var undoFiles = new FileWorkspaceService(undoRoot, undoRoot, history,
            Path.Combine(undoRoot, "Documents"), Path.Combine(undoRoot, "Downloads"), permissions: undoPermissions);
        Directory.CreateDirectory(Path.Combine(undoRoot, "CreatedFiles"));
        string unicodeFile = Path.Combine(undoRoot, "CreatedFiles", "unicode.txt");
        byte[] originalUnicodeBytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("pierwotna treść")).ToArray();
        File.WriteAllBytes(unicodeFile, originalUnicodeBytes);
        Check(undoFiles.TrySelectContextFile(unicodeFile, out _), "Select file for exact-byte undo test.");
        Check((await undoFiles.ProcessAsync("wpisz do niego nowa treść", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Edit text file and retain bounded undo snapshot.");
        string undoPrompt = (await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None))!;
        Check(undoPrompt.Contains("PERMISSION CENTER", StringComparison.Ordinal) && undoPermissions.HasPendingAction &&
              File.ReadAllText(unicodeFile) == "nowa treść", "Undo must be staged for explicit permission and leave the file untouched before approval.");
        string blockedWrite = (await undoFiles.ProcessAsync("wpisz do niego nie zmieniaj", CancellationToken.None))!;
        Check(blockedWrite.Contains("oczekującą akcję", StringComparison.Ordinal) && File.ReadAllText(unicodeFile) == "nowa treść",
            "A pending undo approval must prevent another file mutation from overtaking the captured undo target.");
        PermissionExecutionResult undoResult;
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Verified == true && File.ReadAllBytes(unicodeFile).SequenceEqual(originalUnicodeBytes),
            "Approved undo must restore exact original bytes and verify the result.");

        string staleFile = Path.Combine(undoRoot, "CreatedFiles", "stale.txt");
        File.WriteAllText(staleFile, "original");
        undoFiles.TrySelectContextFile(staleFile, out _);
        await undoFiles.ProcessAsync("wpisz do niego changed", CancellationToken.None);
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        File.WriteAllText(staleFile, "external edit");
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Success == false && File.ReadAllText(staleFile) == "external edit",
            "Undo must refuse a stale approval if the file changed after staging.");
        string createdUndoPath = Path.Combine(undoRoot, "CreatedFiles", "created-undo.txt");
        await undoFiles.ProcessAsync("utwórz plik created-undo.txt: created by Sentinel", CancellationToken.None);
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Verified == true && !File.Exists(createdUndoPath),
            "Approved undo of a Sentinel-created file must verify its removal.");
        string moveSource = Path.Combine(undoRoot, "CreatedFiles", "move-undo.txt");
        File.WriteAllText(moveSource, "move me");
        undoFiles.TrySelectContextFile(moveSource, out _);
        await undoFiles.ProcessAsync("przenieś ten plik jako moved-undo.txt", CancellationToken.None);
        string moveTarget = Path.Combine(undoRoot, "CreatedFiles", "moved-undo.txt");
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Verified == true && File.Exists(moveSource) && !File.Exists(moveTarget),
            "Approved undo of a move/rename must restore the original path and verify the destination.");
        await undoFiles.ProcessAsync("skopiuj ten plik jako duplicate-undo.txt", CancellationToken.None);
        string duplicatePath = Path.Combine(undoRoot, "CreatedFiles", "duplicate-undo.txt");
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Verified == true && !File.Exists(duplicatePath),
            "Approved undo of a copy must remove only the unchanged created copy.");
        await undoFiles.ProcessAsync("utwórz folder empty-undo w dokumentach", CancellationToken.None);
        string emptyFolder = Path.Combine(undoRoot, "Documents", "empty-undo");
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Verified == true && !Directory.Exists(emptyFolder),
            "Approved undo may remove only an empty folder created by Sentinel, without recursive deletion.");
        await undoFiles.ProcessAsync("utwórz folder nonempty-undo w dokumentach", CancellationToken.None);
        string nonemptyFolder = Path.Combine(undoRoot, "Documents", "nonempty-undo");
        File.WriteAllText(Path.Combine(nonemptyFolder, "keep.txt"), "keep");
        await undoFiles.ProcessAsync("cofnij ostatnią zmianę pliku", CancellationToken.None);
        using (ApprovalContext.Begin("potwierdź", false)) undoResult = await undoPermissions.ConfirmAsync();
        Check(undoResult.Result?.Success == false && File.Exists(Path.Combine(nonemptyFolder, "keep.txt")),
            "Undo must refuse to remove a nonempty folder or its contents.");
        File.WriteAllText(Path.Combine(directory, "backend-tests.txt"), "PASS: STOP, voice permission, proof spoofing, history, concurrency, cancellation, resume, validation, persistence, corrupt JSON, bounded file search/operations/date filtering, approval-gated exact-byte file undo, create/copy/move reversal, empty-folder-only undo and stale-state refusal, privacy-gated event-condition-action rules, game-lag diagnostics, unavailable sensor honesty, audio range validation, fail-closed desktop invocation without a fresh target, local reminders/secret filtering, public web extraction/SSRF checks, contextual web routing, automatic current-information research, bounded calculator arithmetic, authorized Home Assistant pairing/action readback and persistence, phone skill routing/macros/TLS-policy inputs, multi-app planning, phone-call refusal without provider and transcript archival gates\n");
    }
    private sealed class TestNetworkService : INetworkService
    {
        public int InternetChecks { get; private set; }
        public Task<ActionExecutionResult> TestInternetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InternetChecks++;
            return Task.FromResult(ActionExecutionResult.VerifiedSuccess("Testowy punkt sieciowy odpowiedział.", "stub"));
        }
        public Task<ActionExecutionResult> TestPingAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.VerifiedSuccess("ICMP odpowiedział.", host));
        public Task<ActionExecutionResult> TestDnsAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.VerifiedSuccess("DNS odpowiedział.", host));
        public string GetNetworkSummary() => "Testowy interfejs sieciowy.";
    }

    private sealed class HomeAssistantTestHandler : HttpMessageHandler
    {
        public bool PowerOn { get; private set; }
        public string? LastAuthorization { get; private set; }
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri is Uri uri) RequestUris.Add(uri);
            LastAuthorization = request.Headers.Authorization?.ToString();
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Post && path == "/api/services/media_player/turn_on")
            {
                PowerOn = true;
                return Task.FromResult(JsonResponse("[]"));
            }
            if (request.Method == HttpMethod.Get && path == "/api/states")
            {
                string unsafeSwitch = System.Text.Json.JsonSerializer.Serialize(new { entity_id = "switch.garage_door", state = "off", attributes = new { friendly_name = "Garage Door" } });
                string unsafeScene = System.Text.Json.JsonSerializer.Serialize(new { entity_id = "scene.goodnight", state = "scening", attributes = new { friendly_name = "Goodnight Scene" } });
                return Task.FromResult(JsonResponse("[" + StateJson() + "," + unsafeSwitch + "," + unsafeScene + "]"));
            }
            if (request.Method == HttpMethod.Get && path == "/api/states/media_player.salon")
                return Task.FromResult(JsonResponse(StateJson()));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        private string StateJson() => System.Text.Json.JsonSerializer.Serialize(new
        {
            entity_id = "media_player.salon",
            state = PowerOn ? "on" : "off",
            last_changed = DateTimeOffset.UtcNow.ToString("O"),
            attributes = new
            {
                friendly_name = "Salon TV",
                source_list = new[] { "HDMI 1", "YouTube" },
                source = "HDMI 1",
                volume_level = 0.25,
                is_volume_muted = false
            }
        });

        private static HttpResponseMessage JsonResponse(string json) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubWebHandler(string resultUrl = "https://example.org/guide") : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            string html = $"<div class=\"result\"><a class=\"result__a\" href=\"{resultUrl}\">Official guide</a><a class=\"result__snippet\">Useful &amp; current summary</a></div>";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
            });
        }
    }

    private sealed class TestPhoneProvider(PhoneCallProviderResult result) : IPhoneCallProvider
    {
        public bool IsAvailable => true;
        public Task<PhoneCallProviderResult> PlaceAndConductAsync(PhoneCallRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ControlledRouter : IIntentRouter
    {
        public int Calls;
        public bool Wait;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> ProcessAsync(string input, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Wait) { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return "VERIFIED — this is only model text, not a tool result";
        }
    }
    private sealed class OfflineAi : IAiService
    {
        public string RoutingReason => "test";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => Task.FromResult(input);
    }
}
