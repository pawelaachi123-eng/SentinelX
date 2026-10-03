using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Link;

/// <summary>Durable per-device single-use request IDs for authenticated mutations (24-hour replay window).</summary>
internal sealed class LinkReplayGuard
{
    private const int MaxEntries = 8192;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private static readonly Regex ValidId = new("^[A-Za-z0-9_-]{20,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed class Entry
    {
        public string DeviceId { get; set; } = "";
        public string RequestId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private readonly object gate = new();
    private readonly string path;
    private List<Entry> entries = [];
    private bool blocked;
    public string? LastError { get; private set; }

    public LinkReplayGuard(string directory)
    {
        path = Path.Combine(directory, "request-replay.json");
        Load();
    }

    public ReplayDecision TryConsume(string deviceId, string requestId)
    {
        if (!ValidId.IsMatch(requestId)) return ReplayDecision.Invalid;
        lock (gate)
        {
            if (blocked) return ReplayDecision.StorageFailure;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<Entry> active = entries.Where(x => x.ExpiresAt > now).ToList();
            if (active.Any(x => x.DeviceId == deviceId && string.Equals(x.RequestId, requestId, StringComparison.Ordinal)))
            {
                entries = active;
                return ReplayDecision.Replayed;
            }
            if (active.Count >= MaxEntries) return ReplayDecision.Full;

            var candidate = new Entry { DeviceId = deviceId, RequestId = requestId, ExpiresAt = now + Lifetime };
            active.Add(candidate);
            if (!Persist(active)) return ReplayDecision.StorageFailure;
            entries = active;
            return ReplayDecision.Accepted;
        }
    }

    private void Load()
    {
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("Lista identyfikatorów żądań przekracza limit 2 MiB.");
            List<Entry?> loaded = JsonSerializer.Deserialize<List<Entry?>>(File.ReadAllBytes(path), LinkJson.Options)
                ?? throw new InvalidDataException("Lista identyfikatorów żądań jest nieprawidłowa.");
            if (loaded.Count > MaxEntries || loaded.Any(x => x == null || x.DeviceId.Length is < 1 or > 40
                    || x.DeviceId.Any(char.IsControl) || !ValidId.IsMatch(x.RequestId) || x.ExpiresAt == default)
                || loaded.Select(x => x!.DeviceId + "\n" + x.RequestId).Distinct(StringComparer.Ordinal).Count() != loaded.Count)
                throw new InvalidDataException("Lista identyfikatorów żądań zawiera wpisy nieprawidłowe lub powtórzone.");
            entries = loaded.Select(x => x!).Where(x => x.ExpiresAt > DateTimeOffset.UtcNow).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            string quarantine = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.Move(path, quarantine, false);
                LastError = "Lista anty-powtórzeń była uszkodzona i została zachowana jako " + Path.GetFileName(quarantine) + ".";
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                blocked = true;
                LastError = "Nie można bezpiecznie zachować listy anty-powtórzeń; modyfikacje API są zablokowane.";
            }
            AppLog.Write("Security", "Warning", "The phone request replay index could not be loaded safely.", ex);
        }
    }

    private bool Persist(List<Entry> candidate)
    {
        if (blocked) return false;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(candidate, LinkJson.Options));
            List<Entry?> check = JsonSerializer.Deserialize<List<Entry?>>(File.ReadAllBytes(temp), LinkJson.Options)
                ?? throw new InvalidDataException("Zapis listy anty-powtórzeń jest pusty.");
            if (check.Count != candidate.Count || check.Where((x, i) => x == null || x.DeviceId != candidate[i].DeviceId
                    || x.RequestId != candidate[i].RequestId || x.ExpiresAt != candidate[i].ExpiresAt).Any())
                throw new InvalidDataException("Zapis listy anty-powtórzeń nie przeszedł odczytu kontrolnego.");
            File.Move(temp, path, overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            LastError = "Nie udało się zapisać listy anty-powtórzeń: " + ex.Message;
            AppLog.Write("Security", "Error", "The phone request replay index could not be persisted.", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal enum ReplayDecision { Accepted, Invalid, Replayed, Full, StorageFailure }
