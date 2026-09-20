namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.</summary>
public sealed class IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
    CommandRouter router) : IIntentRouter
{
    public async Task<string> ProcessAsync(string input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string? file = await files.ProcessAsync(input, token);
        if (file != null) return file;
        var result = await toolbox.ProcessAsync(input, token);
        return result.Handled ? result.Response : await router.ProcessAsync(input, token);
    }
}
