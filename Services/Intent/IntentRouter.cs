using SentinelX.Core;
using SentinelX.Services.Creative;

namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.
/// Unknown input is repaired against the known-command catalogue before it reaches the model,
/// and the user is always told what was understood. Since 0.91, inputs that would otherwise fall
/// through to the AI produce a "did you mean…?" question instead of a silent guess, and every
/// successful repair is recorded in the local lessons journal („lekcje”).</summary>
public sealed class IntentRouter : IIntentRouter
{
    private readonly SentinelToolboxService toolbox;
    private readonly Services.Files.IFileService files;
    private readonly CommandRouter router;
    private readonly Services.Monitoring.ReadOnlyCommandService reads;
    private readonly UnderstandingJournal? journal;
    private readonly Services.Files.FileCleanupService? cleanup;
    private string? pendingSuggestion;

    public IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
        CommandRouter router, Services.Monitoring.ReadOnlyCommandService reads, UnderstandingJournal? journal = null,
        Services.Files.FileCleanupService? cleanup = null)
    {
        this.toolbox = toolbox; this.files = files; this.router = router; this.reads = reads; this.journal = journal; this.cleanup = cleanup;
        // The router raises this exactly when it would otherwise hand the input to the AI model.
        router.SuggestionPending += suggestion => pendingSuggestion = suggestion;
    }

    public Task<string> ProcessAsync(string input, CancellationToken token) => ProcessAsync(input, token, null);

    public async Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta)
    {
        token.ThrowIfCancellationRequested();
        input = CommandText.StripWakeWord(input);
        if (input.Length == 0) return "Słucham. Wpisz polecenie.";

        // „//” palette resolution: a typed shortcut becomes the real command before anything else runs.
        string trimmed = input.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var entry = SlashCatalog.TryResolve(trimmed[2..]);
            if (entry is { Kind: SlashKind.Command }) { pendingSuggestion = null; return await ProcessAsync(entry.Target, token, onDelta); }
            if (entry != null) return "„//" + entry.Trigger + "” to skrót interfejsu (" + entry.Label + ") — użyj palety // w polu wpisywania, aby go wybrać.";
            var closest = SlashCatalog.Filter(trimmed[2..]).Take(3).ToArray();
            return closest.Length == 0
                ? "Nie znam skrótu „" + trimmed + "”. Wpisz samo „//”, aby zobaczyć listę dostępnych poleceń."
                : "Nie znam skrótu „" + trimmed + "”. Najbliżej:\n" + string.Join("\n", closest.Select(x => "· //" + x.Trigger + " — " + x.Label)) + "\nWpisz samo „//”, aby zobaczyć całą listę.";
        }

        // 0.96: „jak to rozumiem: …” only explains — it must not execute, clear a pending question or reach the model.
        string? preview = DecisionPreview.TryExplain(input);
        if (preview != null) return preview;

        // A pending "did you mean…?" waits only for an explicit yes; anything else discards it.
        if (pendingSuggestion is { } suggested && CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',') is "tak" or "potwierdz" or "tak to")
        {
            pendingSuggestion = null;
            return await ProcessAsync(suggested, token, onDelta);
        }
        pendingSuggestion = null;

        // Block high-confidence actionable requests for illegal harm before files, utilities, apps or AI can act.
        if (AuthorizedUsePolicy.TryRefuse(input, out string safetyRefusal)) return safetyRefusal;

        // Natural-language Roblox creation is an intent, not a magic command string. Route before typo repair
        // so the user's full brief, title, theme and constraints stay intact and never reach a text-rewrite step.
        if (RobloxGameRequestAnalyzer.TryAnalyze(input, out RobloxGameProjectSpec gameRequest))
        {
            pendingSuggestion = null;
            return await router.CreateRobloxGameRequestAsync(gameRequest, token).ConfigureAwait(false);
        }

        // Preserve source/mesh arguments byte-for-byte. Typo repair is unsafe for code payloads and
        // can silently alter a model dimension or a Luau identifier before the deterministic tool sees it.
        string normalizedCreativeInput = CommandText.Normalize(input).Trim();
        if (RobloxLuauTools.IsCommand(normalizedCreativeInput) || ObjModelGenerator.IsCommand(normalizedCreativeInput) ||
            RobloxGameProjectGenerator.IsCommand(normalizedCreativeInput))
            return await router.ProcessAsync(input, token, onDelta);

        // Typos and short forms: repair only rewrites the text, then the normal pipeline decides.
        var repair = CommandUnderstanding.Repair(input);
        string effective = repair.Success ? repair.Text : input;
        string note = repair.Success
            ? "Zrozumiałem jako: „" + repair.Canonical + "” (" + repair.Summary + ").\n\n"
            : "";
        if (repair.Success) journal?.Append(input.Trim(), repair.Canonical, repair.Summary);

        string? read = reads.Process(effective, token);
        if (read != null) return note + read;
        string? file = await files.ProcessAsync(effective, token);
        if (file != null) return note + file;
        string? cleanupResult = cleanup is null ? null : await cleanup.ProcessAsync(effective, token);
        if (cleanupResult != null) return note + cleanupResult;
        var result = await toolbox.ProcessAsync(effective, token);
        return result.Handled ? note + result.Response : note + await router.ProcessAsync(effective, token, onDelta);
    }
}
