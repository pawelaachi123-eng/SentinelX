namespace SentinelX.Services.Apps;
public interface IAppLauncherService
{
    Task<ActionExecutionResult> LaunchAsync(string target, CancellationToken cancellationToken = default);
    Task<ActionExecutionResult> SearchWebAsync(string query, bool youtube, CancellationToken cancellationToken = default);
    Task<ActionExecutionResult> OpenFolderAsync(string folder, CancellationToken cancellationToken = default);
}
