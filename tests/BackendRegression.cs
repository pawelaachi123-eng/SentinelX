using System.IO;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;
using SentinelX.Services.PhoneCall;
using SentinelX.Services.Web;

namespace SentinelX.Tests;

internal static class BackendRegression
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var history = new ActionHistoryService(directory);
        var memory = new ConversationMemoryService(Path.Combine(directory, "Memory"));
        var phoneHistory = new PhoneCallTranscriptStore(Path.Combine(directory, "phone-calls.jsonl"));
        var phoneTool = new PhoneCallTool(history: phoneHistory);
        var toolbox = new SentinelToolboxService(history: history, phoneCallTool: phoneTool);
        var router = new ControlledRouter();
        var engine = new ActionEngine(router, toolbox, history, memory, new OfflineAi());
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

        string unavailableCall = (await phoneTool.TryProcessAsync("Zadzwoń do restauracji X i zarezerwuj jutro na 18 dla 4 osób"))!;
        Check(unavailableCall.Contains("Nie wykonano połączenia", StringComparison.Ordinal) &&
              !(await phoneHistory.GetRecentAsync()).Any(), "Without a real phone provider, the tool must refuse and never create a fake call-history entry.");
        var routedCall = await toolbox.ProcessAsync("Zadzwoń do restauracji X");
        Check(routedCall.Handled && routedCall.Response.Contains("Nie wykonano połączenia", StringComparison.Ordinal), "Phone calls must be routed as an ordinary toolbox tool instead of falling through to AI text.");
        Check((await phoneTool.TryProcessAsync("/rozmowy historia"))!.Contains("Nie ma jeszcze", StringComparison.Ordinal), "Slash call-history command must be handled locally.");
        var completedStore = new PhoneCallTranscriptStore(Path.Combine(directory, "completed-phone-calls.jsonl"));
        var completedTool = new PhoneCallTool(new TestPhoneProvider(new(true, Summary: "Gotowe. 18:00, 4 osoby.", Transcript: "Sentinel: Czy jest stolik?\nRestauracja: Tak.")), completedStore);
        Check((await completedTool.TryProcessAsync("Zadzwoń do restauracji X"))!.StartsWith("Gotowe", StringComparison.Ordinal) &&
              (await completedStore.GetRecentAsync()).Count == 1, "Only a provider-confirmed completed call with transcript may be reported and archived.");
        var unconfirmedStore = new PhoneCallTranscriptStore(Path.Combine(directory, "unconfirmed-phone-calls.jsonl"));
        var unconfirmedTool = new PhoneCallTool(new TestPhoneProvider(new(false, Message: "Nie odebrano.")), unconfirmedStore);
        Check((await unconfirmedTool.TryProcessAsync("Zadzwoń do restauracji Y"))!.StartsWith("Połączenie nie zostało potwierdzone", StringComparison.Ordinal) &&
              !(await unconfirmedStore.GetRecentAsync()).Any(), "Unanswered calls must not be reported as completed or archived as transcripts.");

        using var web = new WebResearchTool(new StubWebHandler());
        string webResults = await web.SearchAsync("sentinel regression");
        Check(webResults.Contains("Official guide", StringComparison.Ordinal) && webResults.Contains("https://example.org/guide", StringComparison.Ordinal) && webResults.Contains("Useful & current summary", StringComparison.Ordinal), "Web search must return readable sourced snippets.");
        Check(web.TryResolveRecentResult("otwórz pierwszą stronę", out string recentUrl, out bool readRecent, out _) && recentUrl == "https://example.org/guide" && !readRecent, "Browser follow-up must resolve the selected recent result without asking for its URL again.");
        Check(web.TryExtractOpenUrl("otwórz stronę https://example.org/guide", out string openUrl) && openUrl == "https://example.org/guide", "A direct request to open a public URL should launch the browser rather than be mistaken for an app name.");
        Check(web.TryResolveRecentResult("podsumuj pierwszą stronę", out _, out bool summarizeRead, out bool shouldSummarize) && summarizeRead && shouldSummarize, "Page-reading follow-up must retain its requested summary mode.");
        string blockedLocalPage = await web.ReadPageAsync("http://127.0.0.1:11434/api/tags");
        Check(blockedLocalPage.Contains("Odrzucono adres lokalny", StringComparison.Ordinal), "Web page reader must block loopback/private-network targets.");
        using var routedWeb = new WebResearchTool(new StubWebHandler());
        var webToolbox = new SentinelToolboxService(webResearchTool: routedWeb);
        var routedSearch = await webToolbox.ProcessAsync("wyszukaj w internecie test");
        Check(routedSearch.Handled && routedSearch.Response.Contains("Official guide", StringComparison.Ordinal), "Natural web-search requests must select the web tool without extra UI.");
        Check(AppLaunchPlan.TryParseCommand("Uruchom Brave i YouTube", out string launchTargets) && AppLaunchPlan.Parse(launchTargets).Targets.Count == 2, "One natural command must build a two-tool launch plan.");

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
        engine.EmergencyStop();
        var cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancelled.Action?.Status == ActionStatus.Cancelled && engine.IsStopped && !engine.IsBusy, "Stop must cancel and remain latched.");
        engine.Resume(); router.Wait = false;
        var resumed = await engine.ExecuteAsync("again");
        Check(resumed.Action?.Status == ActionStatus.Unverified, "Engine must recover after cancellation.");
        var store = new AppSettingsService(Path.Combine(directory, "Settings"));
        var field = SettingsCatalog.Create(store).Single(x => x.Label == "Próg VAD");
        Check(field.Write("NaN") != null && field.Write("2") != null, "Invalid VAD values must be rejected.");
        Check(field.Write("0,35") == null, "Polish decimal separator must be accepted.");
        store.Save();
        Check(new AppSettingsService(Path.Combine(directory, "Settings")).Settings.Voice.VadThreshold == .35, "Settings must round-trip.");
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

        string testDesktop = Path.Combine(directory, "UserDesktop"), testDocuments = Path.Combine(directory, "UserDocuments");
        Directory.CreateDirectory(testDesktop); Directory.CreateDirectory(testDocuments);
        var contextualFiles = new FileWorkspaceService(directory, testDesktop, history, testDocuments, Path.Combine(directory, "UserDownloads"));
        Check((await contextualFiles.ProcessAsync("utwórz plik trip-plan.txt na pulpicie: budżet wakacje wyjazd sierpień", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Create file in Desktop.");
        Check((await contextualFiles.ProcessAsync("znajdź dokument o budżet wakacje", CancellationToken.None))!.Contains("trip-plan.txt", StringComparison.Ordinal), "Description search must inspect common user folders and text contents.");
        Check((await contextualFiles.ProcessAsync("przenieś go do folderu Dokumenty", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Pronoun context must move the selected file to Documents.");
        Check(File.Exists(Path.Combine(testDocuments, "trip-plan.txt")) && !File.Exists(Path.Combine(testDesktop, "trip-plan.txt")), "File move must update the physical location.");
        Check((await contextualFiles.ProcessAsync("zmień jego nazwę na summer-plan.txt", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Pronoun context must rename the selected file.");
        Check(File.Exists(Path.Combine(testDocuments, "summer-plan.txt")), "Rename must update the physical file.");
        Check((await contextualFiles.ProcessAsync("utwórz folder Projekt podróż w dokumentach", CancellationToken.None))!.StartsWith("Gotowe", StringComparison.Ordinal), "Create folder in Documents.");
        Check(Directory.Exists(Path.Combine(testDocuments, "Projekt podróż")), "Folder must really exist.");
        File.WriteAllText(Path.Combine(directory, "backend-tests.txt"), "PASS: STOP, voice permission, proof spoofing, history, concurrency, cancellation, resume, validation, persistence, corrupt JSON, file create/copy/move/rename, bounded description search, pronoun context, folder creation and safety, public web result extraction and loopback block, natural search routing, multi-app planning, phone-call refusal without provider, transcript archival gates\n");
    }
    private sealed class StubWebHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string html = "<div class=\"result\"><a class=\"result__a\" href=\"https://example.org/guide\">Official guide</a><a class=\"result__snippet\">Useful &amp; current summary</a></div>";
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
