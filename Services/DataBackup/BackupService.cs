using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SentinelX;

namespace SentinelX.Services.DataBackup;

/// <summary>
/// Backup/restore ustawień, profilów, automatyzacji, layoutów i wybranych pamięci.
/// Sekrety (tokeny itp.) NIE są trzymane jako plaintext.
/// </summary>
public sealed class BackupService
{
    private readonly string root;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public BackupService(string? root = null) { this.root = Path.Combine(root ?? AppPaths.Root, "Backups"); Directory.CreateDirectory(this.root); }

    public async Task<string> CreateBackupAsync(BackupSet set, CancellationToken token)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(root, stamp);
        Directory.CreateDirectory(dir);

        var manifest = new BackupManifest { Stamp = stamp, CreatedAt = DateTimeOffset.UtcNow, Sections = new List<string>() };
        if (set.Settings != null) { await WriteJson(dir, "settings.json", set.Settings, token); manifest.Sections.Add("settings"); }
        if (set.Profiles != null) { await WriteJson(dir, "profiles.json", set.Profiles, token); manifest.Sections.Add("profiles"); }
        if (set.Automations != null) { await WriteJson(dir, "automations.json", set.Automations, token); manifest.Sections.Add("automations"); }
        if (set.Layout != null) { await WriteJson(dir, "layout.json", set.Layout, token); manifest.Sections.Add("layout"); }
        if (set.SelectedMemory != null)
        {
            // Pamięci zapisujemy bez tajemnic — w tym miejscu nie ma pól secret.
            await WriteJson(dir, "memory.json", set.SelectedMemory, token);
            manifest.Sections.Add("memory");
        }
        manifest.SecretsEncrypted = false;
        await WriteJson(dir, "manifest.json", manifest, token);
        return dir;
    }

    public async Task<BackupSet?> RestoreAsync(string stamp, CancellationToken token)
    {
        var dir = Path.Combine(root, stamp);
        if (!Directory.Exists(dir)) return null;
        var manifest = await ReadJson<BackupManifest>(Path.Combine(dir, "manifest.json"), token);
        var set = new BackupSet();
        if (manifest != null)
        {
            if (manifest.Sections.Contains("settings")) set.Settings = await ReadJson<object>(Path.Combine(dir, "settings.json"), token);
            if (manifest.Sections.Contains("profiles")) set.Profiles = await ReadJson<object>(Path.Combine(dir, "profiles.json"), token);
            if (manifest.Sections.Contains("automations")) set.Automations = await ReadJson<object>(Path.Combine(dir, "automations.json"), token);
            if (manifest.Sections.Contains("layout")) set.Layout = await ReadJson<object>(Path.Combine(dir, "layout.json"), token);
            if (manifest.Sections.Contains("memory")) set.SelectedMemory = await ReadJson<object>(Path.Combine(dir, "memory.json"), token);
        }
        return set;
    }

    public IReadOnlyList<string> ListBackups() =>
        Directory.GetDirectories(root).Select(Path.GetFileName).OrderByDescending(n => n).ToList()!;

    private static async Task WriteJson<T>(string dir, string name, T value, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        await File.WriteAllTextAsync(Path.Combine(dir, name), json, Encoding.UTF8, token).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJson<T>(string path, CancellationToken token)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch { return default; }
    }
}

public sealed class BackupSet
{
    public object? Settings { get; set; }
    public object? Profiles { get; set; }
    public object? Automations { get; set; }
    public object? Layout { get; set; }
    public object? SelectedMemory { get; set; }
}

internal sealed class BackupManifest
{
    public string Stamp { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Sections { get; set; } = new();
    public bool SecretsEncrypted { get; set; }
}
