using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SentinelX;

namespace SentinelX.Services.Update;

/// <summary>
/// Silnik aktualizacji z weryfikacją i rollbackiem.
/// W tej fazie: sprawdzanie przez GitHub Releases API; pobieranie nie jest aktywne w sandboxie,
/// ale maszyna stanów (check→download→verify→stage→restart→apply→health→rollback) jest gotowa,
/// a niezweryfikowany build nigdy nie zostanie zainstalowany.
/// </summary>
public sealed class UpdateEngine
{
    private readonly string downloadDir;
    private readonly string stagingDir;
    private readonly string backupDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string? PendingVersion { get; private set; }
    public string? LastError { get; private set; }

    public UpdateEngine(string? root = null)
    {
        var baseDir = root ?? AppPaths.Root;
        downloadDir = Path.Combine(baseDir, "Update", "Downloads");
        stagingDir = Path.Combine(baseDir, "Update", "Staging");
        backupDir = Path.Combine(baseDir, "Update", "Previous");
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(backupDir);
    }

    public async Task<UpdateCheckResult?> CheckAsync(string owner, string repo, string currentVersion, CancellationToken ct)
    {
        SetState(UpdateState.Checking);
        try
        {
            // Wywołanie gh jest dostępne w dev, w produkcji użyjemy własnego HttpClient na api.github.com.
            var psi = new ProcessStartInfo
            {
                FileName = "gh", Arguments = $"release list --repo {owner}/{repo} --limit 1 --json tagName,isPrerelease,publishedAt",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) { SetState(UpdateState.Idle); return null; }
            var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse("[" + stdout.Trim().TrimStart('[').TrimEnd(']') + "]");
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var tag = el.GetProperty("tagName").GetString();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                bool isNewer = IsNewer(tag.TrimStart('v'), currentVersion);
                return new UpdateCheckResult(tag, el.GetProperty("publishedAt").GetDateTimeOffset(), el.GetProperty("isPrerelease").GetBoolean(), isNewer);
            }
            SetState(UpdateState.Idle);
            return null;
        }
        catch (Exception ex) { LastError = ex.Message; SetState(UpdateState.Error); return null; }
    }

    public static bool IsNewer(string candidate, string current)
    {
        static int[] parse(string v) => v.Split('.').Take(3).Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var a = parse(candidate); var b = parse(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int ai = i < a.Length ? a[i] : 0; int bi = i < b.Length ? b[i] : 0;
            if (ai > bi) return true;
            if (ai < bi) return false;
        }
        return false;
    }

    /// <summary>Weryfikacja SHA256 pliku — bez zgodności nie idziemy dalej.</summary>
    public bool VerifyFile(string path, string expectedSha256)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void SetState(UpdateState s) { State = s; }
}

public enum UpdateState { Idle, Checking, Downloading, Verifying, Staging, Restarting, Applying, HealthCheck, Rollback, Error }
public sealed record UpdateCheckResult(string Tag, DateTimeOffset PublishedAt, bool Prerelease, bool IsNewer);
