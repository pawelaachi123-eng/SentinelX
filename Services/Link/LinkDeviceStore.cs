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
    }

    private const int MaxDevices = 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string path;
    private List<Record> records = [];

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
    public (LinkDeviceInfo Device, string Token) Add(string name)
    {
        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        LinkDeviceInfo info;
        lock (gate)
        {
            while (records.Count >= MaxDevices) records.Remove(records.OrderBy(x => x.LastSeen).First());
            var record = new Record
            {
                Id = "d" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Name = name, TokenHash = Hash(token), AddedAt = DateTimeOffset.Now, LastSeen = DateTimeOffset.Now
            };
            records.Add(record);
            Save();
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
        lock (gate)
        {
            foreach (Record record in records)
            {
                byte[] expected;
                try { expected = Convert.FromHexString(record.TokenHash); }
                catch (FormatException) { continue; }
                if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual)) continue;
                record.LastSeen = DateTimeOffset.Now;
                return ToInfo(record);
            }
        }
        return null;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (gate)
        {
            removed = records.RemoveAll(x => x.Id == id) > 0;
            if (removed) Save();
        }
        if (removed) RaiseChanged();
        return removed;
    }

    public void RemoveAll()
    {
        lock (gate) { records = []; Save(); }
        RaiseChanged();
    }

    private static LinkDeviceInfo ToInfo(Record r) => new(r.Id, r.Name, r.AddedAt, r.LastSeen);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private void Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return;
            records = JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(path), JsonOptions) ?? [];
            records = records.Where(x => x != null && x.Id.Length is > 0 and <= 40 && x.TokenHash.Length == 64).Take(MaxDevices).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            records = [];
            LastError = "Nie udało się odczytać listy telefonów: " + ex.Message;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, JsonOptions));
            File.Move(temp, path, true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = "Nie udało się zapisać listy telefonów: " + ex.Message;
            AppLog.Write(ex);
        }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
