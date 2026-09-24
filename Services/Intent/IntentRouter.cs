using SentinelX.Core;

namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.
/// Unknown input is repaired against the known-command catalogue before it reaches the model,
/// and the user is always told what was understood. Since 0.91, inputs in the grey zone between
/// "understood" and "unknown" produce a question instead of a silent guess, and every successful
/// repair is recorded in the local lessons journal („lekcje”).</summary>
public sealed class IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
    CommandRouter router, Services.Monitoring.ReadOnlyCommandService reads, UnderstandingJournal? journal = null) : IIntentRouter
{
    private string? pendingSuggestion;

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

        // A pending "did you mean…?" waits only for an explicit yes; anything else discards it.
        if (pendingSuggestion is { } suggested && CommandText.Normalize(input).Trim().TrimEnd('.', '!', '?', ',') is "tak" or "potwierdz" or "tak to")
        {
            pendingSuggestion = null;
            return await ProcessAsync(suggested, token, onDelta);
        }
        pendingSuggestion = null;

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
        var result = await toolbox.ProcessAsync(effective, token);
        if (result.Handled) return note + result.Response;

        // Grey zone: a command-like phrase close to a known one gets a question, never a silent guess.
        if (!repair.Success)
        {
            var suggestions = CommandUnderstanding.Suggest(input);
            if (suggestions.Count > 0)
            {
                pendingSuggestion = suggestions[0];
                string options = string.Join("\n", suggestions.Select(x => "· „" + x + "”"));
                return "Nie jestem pewien, o co chodzi. Czy chodziło Ci o:\n" + options +
                    "\nOdpisz „tak”, aby wykonać pierwszą opcję, albo napisz polecenie dokładniej. Niczego nie wykonałem.";
            }
        }

        return note + await router.ProcessAsync(effective, token, onDelta);
    }
}
