using System.IO;
using System.Text.Json;

namespace SentinelX.Services.Maintenance;

public sealed record RunHealthState(int ConsecutiveFailures, long LastStartUtc, bool CleanExit);

/// <summary>Crash counting for ordinary runs (no update journal needed).
/// BeginRun stamps the start; only a clean exit clears the dirty flag, so any
/// crash — including a normal run with no update pending — is counted.</summary>
public static class RunHealth
{
    public static string StatePath(string root, string tag) => Path.Combine(root, "Updates", "runs-" + tag + ".json");

    public static RunHealthState Read(string root, string tag)
    {
        string path = StatePath(root, tag);
        if (!File.Exists(path)) return new(0, 0, true);
        try
        {
            if (new FileInfo(path).Length > 65536) throw new UpdateFailure("state");
            return JsonSerializer.Deserialize<RunHealthState>(File.ReadAllText(path)) ?? throw new UpdateFailure("state");
        }
        catch (Exception e) when (e is IOException or JsonException or UpdateFailure)
        {
            try { File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch { }
            return new(1, 0, false);
        }
    }

    public static RunHealthState BeginRun(string root, string tag)
    {
        var previous = Read(root, tag);
        int failures = previous.CleanExit ? 0 : previous.ConsecutiveFailures + 1;
        var state = new RunHealthState(failures, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
        Save(root, tag, state);
        return state;
    }

    public static void EndRun(string root, string tag)
    {
        var previous = Read(root, tag);
        Save(root, tag, previous with { CleanExit = true, ConsecutiveFailures = 0 });
    }

    private static void Save(string root, string tag, RunHealthState state)
    {
        string path = StatePath(root, tag);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        if (JsonSerializer.Deserialize<RunHealthState>(File.ReadAllText(temp)) == null) throw new UpdateFailure("state_verify");
        File.Move(temp, path, true);
    }
}
