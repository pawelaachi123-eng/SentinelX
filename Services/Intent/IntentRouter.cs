using SentinelX.Core;

namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.
/// Unknown input is repaired against the known-command catalogue before it reaches the model,
/// and the user is always told what was understood.</summary>
public sealed class IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
    CommandRouter router, Services.Monitoring.ReadOnlyCommandService reads) : IIntentRouter
{
    public Task<string> ProcessAsync(string input, CancellationToken token) => ProcessAsync(input, token, null);

    public async Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta)
    {
        token.ThrowIfCancellationRequested();
        input = CommandText.StripWakeWord(input);
        if (input.Length == 0) return "Słucham. Wpisz polecenie.";

        // Typos and short forms: repair only rewrites the text, then the normal pipeline decides.
        var repair = CommandUnderstanding.Repair(input);
        string effective = repair.Success ? repair.Text : input;
        string note = repair.Success
            ? "Zrozumiałem jako: „" + repair.Canonical + "” (" + repair.Summary + ").\n\n"
            : "";

        string? read = reads.Process(effective, token);
        if (read != null) return note + read;
        string? file = await files.ProcessAsync(effective, token);
        if (file != null) return note + file;
        var result = await toolbox.ProcessAsync(effective, token);
        return result.Handled ? note + result.Response : note + await router.ProcessAsync(effective, token, onDelta);
    }
}
