namespace SentinelX.Services.Permissions;
public interface IPermissionService
{
    bool HasPendingAction { get; }
    bool TryRequest(PendingPermissionAction action, out string response);
    string GetPendingSummary();
    Task<PermissionExecutionResult> ConfirmAsync(CancellationToken cancellationToken = default);
    PendingPermissionAction? Cancel();
}
