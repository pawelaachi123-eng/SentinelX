using System.Text.Json;
using System.IO;
using System.Text.Json.Serialization;
using SentinelX;

namespace SentinelX.Services.Health;

/// <summary>
/// Jeśli aplikacja crashuje kilka razy podряд ruszamy w trybie awaryjnym: pluginy OFF,
/// automacje OFF, minimalny zestaw usług, diagnostyka ON.
/// </summary>
public sealed class SafeModeService
{
    private readonly string statePath;
    private CrashState state = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public SafeModeService(string? directory = null)
    {
        statePath = Path.Combine(directory ?? AppPaths.LogsDirectory, "crash-state.json");
        Load();
    }

    public bool SafeModeRecommended => state.ConsecutiveCrashes >= 3;
    public int ConsecutiveCrashes => state.ConsecutiveCrashes;
    public DateTimeOffset? LastCrashAt => state.LastCrashAt;

    public void RegisterSuccessfulStart()
    {
        state.ConsecutiveCrashes = 0;
        state.LastCrashAt = null;
        state.LastDangerousOperation = null;
        Save();
    }

    public void RegisterCrash(string? operation)
    {
        state.ConsecutiveCrashes++;
        state.LastCrashAt = DateTimeOffset.UtcNow;
        // Nie zapamiętujemy niebezpiecznej operacji do automatycznego wznowienia.
        state.LastDangerousOperation = null;
        Save();
    }

    /// <summary>Niebezpieczne operacje (np. build/test user code) są zapamiętywane tylko do
    /// momentu ich pomyślnego zakończenia — po crashu NIE są wznawiane automatycznie.</summary>
    public void BeginDangerousOperation(string description) => state.PendingDangerousOperation = description;
    public void EndDangerousOperation() => state.PendingDangerousOperation = null;
    public string? PendingDangerousOperation => state.PendingDangerousOperation;

    private void Load()
    {
        try
        {
            if (File.Exists(statePath))
                state = JsonSerializer.Deserialize<CrashState>(File.ReadAllText(statePath), JsonOpts) ?? new();
        }
        catch { state = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch { /* best effort */ }
    }

    private sealed class CrashState
    {
        public int ConsecutiveCrashes { get; set; }
        public DateTimeOffset? LastCrashAt { get; set; }
        public string? LastDangerousOperation { get; set; }
        [JsonIgnore] public string? PendingDangerousOperation { get; set; }
    }
}
