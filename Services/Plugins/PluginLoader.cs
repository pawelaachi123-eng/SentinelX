using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using SentinelX.Models.Plugins;

namespace SentinelX.Services.Plugins;

/// <summary>
/// Ładuje pluginy z katalogu Plugins/ obok aplikacji. W tej fazie obsługujemy pluginy
/// „wbudowane" (assembly już w procesie) — ładowanie niezweryfikowanych DLL użytkownika
/// wymaga oddzielnej piaskownicy i podpisu cyfrowego, co zostaje odnotowane jako unsupported.
/// </summary>
public sealed class PluginLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly ConcurrentDictionary<string, PluginManifest> manifests = new();

    public IReadOnlyDictionary<string, PluginManifest> Manifests => manifests;

    public IEnumerable<PluginManifest> DiscoverBuiltIn(IEnumerable<ISentinelPlugin> builtIns)
    {
        foreach (var plugin in builtIns)
        {
            var m = plugin.Manifest;
            if (string.IsNullOrWhiteSpace(m.Id)) continue;
            manifests[m.Id] = m;
            yield return m;
        }
    }

    public IEnumerable<PluginManifest> DiscoverFromDisk(string pluginsDirectory)
    {
        var found = new List<PluginManifest>();
        if (!Directory.Exists(pluginsDirectory)) return found;
        // Ładowanie obcych DLL jest domyślnie WYŁĄCZONE (unsupported / sandbox).
        // Użytkownik musi jawnie włączyć w ustawieniach developera. Tu zwracamy listę
        // znalezionych manifestów bez ładowania typów, żeby UI mogło je pokazać.
        foreach (var dir in Directory.GetDirectories(pluginsDirectory))
        {
            var mfPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(mfPath)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(mfPath), JsonOpts);
                if (m != null && !string.IsNullOrWhiteSpace(m.Id))
                {
                    manifests[m.Id] = m;
                    found.Add(m);
                }
            }
            catch { /* corrupt manifest — ignoruj; health pokaże */ }
        }
        return found;
    }

    public ISentinelPlugin? ActivateBuiltIn(ISentinelPlugin plugin) => plugin;

    /// <summary>Ładowanie obcych DLL nie jest obsługiwane w tej wersji.</summary>
    public bool ExternalLoadSupported => false;
    public string ExternalLoadUnsupportedReason =>
        "Ładowanie zewnętrznych DLL wymaga podpisu cyfrowego i sandboxa (AssemblyLoadContext z ograniczonymi uprawnieniami). " +
        "Funkcja planowana w 1.0; obecnie tylko wbudowane pluginy są aktywowane.";
}
