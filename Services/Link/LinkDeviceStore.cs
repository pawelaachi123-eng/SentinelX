using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Link;

/// <summary>Phones the user has approved on the PC. Only the SHA-256 of each token is kept, so the file alone cannot be used to log in.</summary>
public sealed class LinkDeviceStore
{
    private sealed class Record
    {
        public Record() { }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string TokenHash { get; set; } = "";
        public DateTimeOffset AddedAt { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public LinkPhoneCapabilities Capabilities { get; set; } = LinkPhoneCapabilities.None;
    }

    private const int MaxDevices = 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string path;
    private List<Record> records = [];
    private bool persistenceBlocked;
    private DateTimeOffset lastActivityWrite = DateTimeOffset.MinValue;

    public string? LastError { get; private set; }
    public event Action? Changed;

    public LinkDeviceStore(string? directory = null)
    {
        path = Path.Combine(directory ?? Path.Combine(AppPaths.Root, "Link"), "devices.json");
        Load();
    }

    public IReadOnlyList<LinkDeviceInfo> List()
    {
        lock (gate) return records.Select(ToInfo).ToArray();
    }

    /// <summary>Registers a phone and returns its one-time token. When the limit is reached the least recently used phone is replaced.</summary>
    public (LinkDeviceInfo Device, string Token) Add(string name, LinkPhoneCapabilities? capabilities = null)
    {
        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        LinkDeviceInfo info;
        lock (gate)
        {
            List<Record> previous = records;
            var next = records.ToList();
            while (next.Count >= MaxDevices) next.Remove(next.OrderBy(x => x.LastSeen).First());
            var record = new Record
            {
                Id = "d" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Name = name, TokenHash = Hash(token), AddedAt = DateTimeOffset.Now, LastSeen = DateTimeOffset.Now,
                Capabilities = capabilities ?? LinkPhoneCapabilities.None
            };
            next.Add(record);
            records = next;
            if (!Save())
            {
                records = previous;
                throw new IOException(LastError ?? "Nie udało się zapisać sparowanego telefonu.");
            }
            info = ToInfo(record);
        }
        RaiseChanged();
        return (info, token);
    }

    /// <summary>Returns the phone that owns <paramref name="token"/>, or null. Constant-time comparison of the hashes.</summary>
    public LinkDeviceInfo? Validate(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        LinkDeviceInfo? matched = null;
        bool activityWriteAttempted = false;
        lock (gate)
        {
            foreach (Record record in records)
            {
                byte[] expected;
                try { expected = Convert.FromHexString(record.TokenHash); }
                catch (FormatException) { continue; }
                if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual)) continue;
                record.LastSeen = DateTimeOffset.Now;
                matched = ToInfo(record);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - lastActivityWrite >= TimeSpan.FromMinutes(5))
                {
                    _ = Save(); // Activity telemetry is best-effort; authentication itself remains available.
                    lastActivityWrite = now; // Bound disk writes even when the data directory is read-only.
                    activityWriteAttempted = true;
                }
                break;
            }
        }
        if (activityWriteAttempted) RaiseChanged();
        return matched;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (gate)
        {
            List<Record> previous = records;
            var next = records.Where(x => x.Id != id).ToList();
            if (next.Count == previous.Count) return false;
            records = next;
            removed = Save();
            if (!removed) records = previous; // Never claim revocation unless it is durable.
        }
        RaiseChanged(); // Also refresh the UI when persistence failed so the warning is visible.
        return removed;
    }

    public bool RemoveAll()
    {
        bool removed;
        lock (gate)
        {
            List<Record> previous = records;
            records = [];
            removed = Save();
            if (!removed) records = previous;
        }
        RaiseChanged();
        return removed;
    }

    private static LinkDeviceInfo ToInfo(Record r) => new(r.Id, r.Name, r.AddedAt, r.LastSeen) { Capabilities = r.Capabilities };
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private void Load()
    {
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 256 * 1024)
                throw new InvalidDataException("Lista telefonów przekracza limit 256 KB.");
            List<Record?> loaded = JsonSerializer.Deserialize<List<Record?>>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Lista telefonów jest pusta lub nieprawidłowa.");
            if (loaded.Count > MaxDevices || loaded.Any(x => !IsValidRecord(x))
                || loaded.Select(x => x!.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Count
                || loaded.Select(x => x!.TokenHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != loaded.Count)
                throw new InvalidDataException("Lista telefonów zawiera wpisy nieprawidłowe lub powtórzone.");
            records = loaded.Select(x => x!).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            records = [];
            string quarantine = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.Move(path, quarantine, false);
                LastError = "Nie udało się odczytać listy telefonów; uszkodzony plik zachowano jako " + Path.GetFileName(quarantine) + ". " + ex.Message;
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                persistenceBlocked = true;
                LastError = "Nie udało się odczytać ani zachować listy telefonów; zapis i parowanie są zablokowane, aby nie nadpisać danych. " + ex.Message;
            }
            AppLog.Write("Devices", "Warning", "Paired-device storage could not be loaded safely.", ex);
        }
    }

    private static bool IsValidRecord(Record? record) => record != null
        && !string.IsNullOrWhiteSpace(record.Id) && record.Id.Length <= 40
        && !string.IsNullOrWhiteSpace(record.Name) && record.Name.Length <= 32 && !record.Name.Any(char.IsControl)
        && !string.IsNullOrEmpty(record.TokenHash) && record.TokenHash.Length == 64 && record.TokenHash.All(Uri.IsHexDigit)
        && record.AddedAt != default && record.LastSeen != default && record.Capabilities != null;

    private bool Save()
    {
        if (persistenceBlocked) return false;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(records, JsonOptions);
            File.WriteAllText(temp, json);
            List<Record?> check = JsonSerializer.Deserialize<List<Record?>>(File.ReadAllText(temp), JsonOptions)
                ?? throw new InvalidDataException("Zapis listy telefonów jest pusty.");
            if (check.Count != records.Count || check.Any(item => item == null) || check.Where((item, index) =>
                    item!.Id != records[index].Id || item.Name != records[index].Name || item.TokenHash != records[index].TokenHash ||
                    item.AddedAt != records[index].AddedAt || item.LastSeen != records[index].LastSeen || item.Capabilities != records[index].Capabilities).Any())
                throw new InvalidDataException("Zapis listy telefonów nie przeszedł odczytu kontrolnego.");
            File.Move(temp, path, true);
            LastError = null;
            lastActivityWrite = DateTimeOffset.UtcNow;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            LastError = "Nie udało się zapisać listy telefonów: " + ex.Message;
            AppLog.Write("Devices", "Error", "Paired-device data could not be saved.", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
