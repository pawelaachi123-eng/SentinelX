using System.Linq;
using System.Collections.Concurrent;
using SentinelX.Models.Integrations;

namespace SentinelX.Services.Integrations;

/// <summary>
/// Silnik integracji z autoryzowanymi usługami/urządzeniami użytkownika.
/// Każdy provider implementuje IIntegrationProvider; nic nie jest hardkodowane.
/// Bez autoryzacji integracja jest widoczna w UI ale nie może nic zrobić.
/// </summary>
public sealed class IntegrationEngine
{
    private readonly ConcurrentDictionary<string, IIntegrationProvider> providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IntegrationDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IntegrationState> states = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, IntegrationStatus>? StatusChanged;

    public void Register(IIntegrationProvider provider)
    {
        providers[provider.Definition.Id] = provider;
        definitions[provider.Definition.Id] = provider.Definition;
        states.GetOrAdd(provider.Definition.Id, _ => new IntegrationState { Id = provider.Definition.Id });
    }

    public IReadOnlyList<IntegrationDefinition> List() => definitions.Values.ToList();
    public IntegrationState? StateOf(string id) => states.TryGetValue(id, out var s) ? s : null;

    public async Task<bool> PairAsync(string id, object? credentials, CancellationToken ct)
    {
        if (!providers.TryGetValue(id, out var p)) return false;
        SetStatus(id, IntegrationStatus.Pairing);
        try
        {
            var ok = await p.AuthenticateAsync(credentials, ct).ConfigureAwait(false);
            if (!ok) { SetStatus(id, IntegrationStatus.Error); return false; }
            var state = states.GetOrAdd(id, _ => new IntegrationState { Id = id });
            state.AuthenticatedAt = DateTimeOffset.UtcNow;
            state.LastError = null;
            SetStatus(id, IntegrationStatus.Connected);
            return true;
        }
        catch (Exception ex)
        {
            var s = states.GetOrAdd(id, _ => new IntegrationState { Id = id });
            s.LastError = ex.Message;
            SetStatus(id, IntegrationStatus.Error);
            return false;
        }
    }

    public async Task RevokeAsync(string id, CancellationToken ct)
    {
        if (providers.TryGetValue(id, out var p)) { try { await p.RevokeAsync(ct).ConfigureAwait(false); } catch { } }
        if (states.TryGetValue(id, out var s)) { s.AuthenticatedAt = null; s.GrantedScopes.Clear(); s.LastError = null; }
        SetStatus(id, IntegrationStatus.Revoked);
    }

    public async Task<IntegrationResult> ExecuteAsync(string id, string actionId, object? payload, CancellationToken ct)
    {
        if (!definitions.TryGetValue(id, out var def)) return IntegrationResult.Fail("Nieznana integracja.");
        if (!states.TryGetValue(id, out var st) || st.Status != IntegrationStatus.Connected)
            return IntegrationResult.Fail("Integracja nie jest połączona.");
        var action = def.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null) return IntegrationResult.Fail("Nieznana akcja.");
        if (!st.GrantedScopes.Contains(action.RequiredPermission))
            return IntegrationResult.Fail("Brak uprawnienia.");
        if (!providers.TryGetValue(id, out var p)) return IntegrationResult.Fail("Brak dostawcy.");
        try { return await p.ExecuteActionAsync(actionId, payload, ct).ConfigureAwait(false); }
        catch (Exception ex) { return IntegrationResult.Fail(ex.Message); }
    }

    private void SetStatus(string id, IntegrationStatus status)
    {
        if (states.TryGetValue(id, out var s)) s.Status = status;
        StatusChanged?.Invoke(id, status);
    }
}

public interface IIntegrationProvider
{
    IntegrationDefinition Definition { get; }
    Task<bool> AuthenticateAsync(object? credentials, CancellationToken ct);
    Task RevokeAsync(CancellationToken ct);
    Task<IntegrationResult> ExecuteActionAsync(string actionId, object? payload, CancellationToken ct);
}

public sealed record IntegrationResult(bool Success, string? Message, object? Data = null)
{
    public static IntegrationResult Ok(string? msg = null, object? data = null) => new(true, msg, data);
    public static IntegrationResult Fail(string msg) => new(false, msg);
}
