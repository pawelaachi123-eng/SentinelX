using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;
using SentinelX.Models.Plugins;

namespace SentinelX.Services.Plugins;

/// <summary>
/// Rejestr pluginów + lifecycle + health. Pluginy mogą być enabled/disabled per manifest;
/// niepowodzenie startu jednego pluginu nie blokuje startu innych (wyspowy failure).
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly PluginLoader loader = new();
    private readonly ConcurrentDictionary<string, PluginRuntime> runtime = new();
    private readonly List<PluginManifest> pendingExternal = new();
    private readonly HashSet<string> enabled = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PluginRuntime> RuntimePlugins => runtime;
    public event Action<string, PluginLifecycleState>? StateChanged;

    public PluginManager(IEnumerable<ISentinelPlugin> builtIns)
    {
        foreach (var m in loader.DiscoverBuiltIn(builtIns))
        {
            enabled.Add(m.Id); // wbudowane domyślnie włączone
        }
    }

    public void ScanDirectory(string pluginsDirectory)
    {
        foreach (var m in loader.DiscoverFromDisk(pluginsDirectory))
        {
            pendingExternal.Add(m);
            // nie aktywujemy automatycznie — zewnętrzne pluginy wymagają zgody
        }
    }

    public async Task StartAllAsync(Func<PluginManifest, IPluginContext> contextFactory, CancellationToken token = default)
    {
        foreach (var m in loader.Manifests.Values.OrderBy(m => m.Id))
        {
            if (!enabled.Contains(m.Id)) continue;
            // built-in instance nie są nam tu dostępne — start wywoływany jest z zewnątrz
            if (runtime.TryGetValue(m.Id, out var rt))
                await StartOneAsync(rt, contextFactory(m), token).ConfigureAwait(false);
        }
    }

    public async Task StartOneAsync(PluginRuntime rt, IPluginContext ctx, CancellationToken token)
    {
        try
        {
            await rt.Plugin.StartAsync(ctx, token).ConfigureAwait(false);
            rt.State = PluginLifecycleState.Started;
            StateChanged?.Invoke(rt.Manifest.Id, PluginLifecycleState.Started);
        }
        catch (Exception ex)
        {
            rt.State = PluginLifecycleState.Faulted;
            rt.LastError = ex.Message;
            StateChanged?.Invoke(rt.Manifest.Id, PluginLifecycleState.Faulted);
        }
    }

    public async Task StopAllAsync(CancellationToken token = default)
    {
        foreach (var rt in runtime.Values)
        {
            try { await rt.Plugin.StopAsync(token).ConfigureAwait(false); rt.State = PluginLifecycleState.Stopped; }
            catch { /* best effort */ rt.State = PluginLifecycleState.Faulted; }
        }
    }

    public void RegisterBuiltInInstance(ISentinelPlugin plugin, Func<IPluginContext> contextFactory)
    {
        var rt = new PluginRuntime(plugin.Manifest, plugin) { State = PluginLifecycleState.Loaded };
        runtime[plugin.Manifest.Id] = rt;
    }

    public async Task<PluginHealthLevel> CheckHealthAsync(string id, CancellationToken token)
    {
        if (!runtime.TryGetValue(id, out var rt)) return PluginHealthLevel.Unknown;
        try
        {
            var h = await rt.Plugin.CheckHealthAsync(token).ConfigureAwait(false);
            rt.Health = h;
            return h;
        }
        catch
        {
            rt.Health = PluginHealthLevel.Unhealthy;
            return PluginHealthLevel.Unhealthy;
        }
    }

    public IEnumerable<PluginManifest> PendingExternal => pendingExternal;
    public void SetEnabled(string id, bool on) { if (on) enabled.Add(id); else enabled.Remove(id); }
    public bool IsEnabled(string id) => enabled.Contains(id);

    public void Dispose()
    {
        foreach (var rt in runtime.Values) { try { rt.Plugin.Dispose(); } catch { } }
        runtime.Clear();
    }
}

public sealed class PluginRuntime
{
    public PluginManifest Manifest { get; }
    public ISentinelPlugin Plugin { get; }
    public PluginLifecycleState State { get; set; }
    public PluginHealthLevel Health { get; set; } = PluginHealthLevel.Unknown;
    public string? LastError { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public PluginRuntime(PluginManifest m, ISentinelPlugin p) { Manifest = m; Plugin = p; }
}
