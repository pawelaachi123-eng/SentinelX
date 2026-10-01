using System.Threading.Tasks;
using System.Diagnostics;
using System.IO; using System.Threading;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SentinelX.Models.Repository;
using SentinelX.Services.Repository;

namespace SentinelX.Services.CodeAssistant;

/// <summary>
/// Bezpieczny asystent kodu działający na własnym repo. Produkcja NIE pozwala
/// generować i uruchamiać dowolnego kodu bez zgody użytkownika. Każda zmiana przechodzi:
///   branch/backup → patch → build → test → verify → approval → apply
/// W tej fazie build/test używają zewnętrznego procesu dotnet (o ile jest na PATH).
/// </summary>
public sealed class CodeAssistantService
{
    private readonly RepositoryIndex index;
    public CodeAssistantService(RepositoryIndex index) { this.index = index; }

    public async Task<string> ReadSourceAsync(string relativePath, CancellationToken token)
    {
        // Ścieżka jest relatywna do repo: nie pozwalamy na path traversal.
        if (relativePath.Contains("..") || Path.IsPathRooted(relativePath))
            return "Ścieżka musi być relatywna do repo (bez .. i bez napędu).";
        var full = Path.GetFullPath(relativePath);
        if (!File.Exists(full)) return "Plik nie istnieje: " + relativePath;
        try { return await File.ReadAllTextAsync(full, Encoding.UTF8, token).ConfigureAwait(false); }
        catch (Exception ex) { return "Nie można odczytać: " + ex.Message; }
    }

    public IReadOnlyList<RepoSymbol> SearchRepository(string query, RepoSymbolKind? kind = null) =>
        index.Search(query, kind);

    public RepoSymbol? FindSymbol(string name) => index.FindSymbol(name);

    public IReadOnlyList<RepoSymbol> FindReferences(string symbolName) =>
        index.Search(symbolName, null, 200); // prosta referencja = wystąpienie nazwy

    public PatchProposal ProposePatch(string relativePath, string oldText, string newText, string reason)
    {
        return new PatchProposal(relativePath, oldText, newText, reason, DateTimeOffset.UtcNow);
    }

    public async Task<Backup> CreateBackupAsync(string repoRoot, CancellationToken token)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(repoRoot, ".sentinel", "backups", stamp);
        Directory.CreateDirectory(dir);
        // Kopiujemy tylko pliki tekstowe (mało), bin/obj pomijamy
        foreach (var f in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories))
        {
            if (token.IsCancellationRequested) break;
            var rel = Path.GetRelativePath(repoRoot, f);
            if (rel.StartsWith(".git") || rel.Contains("\\bin\\") || rel.Contains("/bin/") ||
                rel.Contains("\\obj\\") || rel.Contains("/obj/") || rel.StartsWith(".sentinel")) continue;
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".cs" or ".xaml" or ".csproj" or ".json" or ".md" or ".yml" or ".yaml")) continue;
            var dest = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try { File.Copy(f, dest, true); } catch { }
        }
        return new Backup(stamp, dir);
    }

    public PatchApplyResult ApplyPatch(PatchProposal patch)
    {
        // Bez zgody użytkownika NIE stosujemy łatek w kodzie produkcyjnym.
        // Ta metoda jest callowana dopiero po jawnej akceptacji.
        if (!File.Exists(patch.RelativePath)) return PatchApplyResult.Fail("Plik nie istnieje.");
        var cur = File.ReadAllText(patch.RelativePath);
        if (!cur.Contains(patch.OldText)) return PatchApplyResult.Fail("Nie znaleziono fragmentu do zamiany.");
        var next = cur.Replace(patch.OldText, patch.NewText);
        if (next == cur) return PatchApplyResult.Fail("Patch nie zmienił nic.");
        File.WriteAllText(patch.RelativePath, next);
        return PatchApplyResult.Ok();
    }

    public async Task<BuildResult> RunBuildAsync(string repoRoot, CancellationToken token)
    {
        var dotnet = FindExecutable("dotnet");
        if (dotnet == null) return new BuildResult(false, "Nie znaleziono `dotnet` w PATH. Zainstaluj .NET SDK 10.", "", 0);
        return await RunProcessAsync(dotnet, "build -c Release --nologo", repoRoot, TimeSpan.FromMinutes(3), token).ConfigureAwait(false);
    }

    public async Task<BuildResult> RunTestsAsync(string repoRoot, CancellationToken token)
    {
        var dotnet = FindExecutable("dotnet");
        if (dotnet == null) return new BuildResult(false, "Nie znaleziono `dotnet` w PATH.", "", 0);
        return await RunProcessAsync(dotnet, "test -c Release --nologo --no-build", repoRoot, TimeSpan.FromMinutes(5), token).ConfigureAwait(false);
    }

    public static string? FindExecutable(string name)
    {
        var ext = Path.DirectorySeparatorChar == '\\' ? ".exe" : "";
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(p.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static async Task<BuildResult> RunProcessAsync(string exe, string args, string cwd, TimeSpan timeout, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe, Arguments = args, WorkingDirectory = cwd,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return new BuildResult(false, "Nie udało się uruchomić procesu.", "", -1);
            string stdout = "";
            string stderr = "";
            try { stdout = await p.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false); } catch { }
            try { stderr = await p.StandardError.ReadToEndAsync(cts.Token).ConfigureAwait(false); } catch { }
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return new BuildResult(p.ExitCode == 0, (stdout ?? "") + "\n" + (stderr ?? ""), "", p.ExitCode);
        }
        catch (Exception ex) { return new BuildResult(false, ex.Message, "", -1); }
    }

    public string GenerateChangelog(IReadOnlyList<History.HistoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Changelog");
        sb.AppendLine();
        sb.AppendLine($"- Wygenerowano: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        foreach (var g in entries.GroupBy(e => e.Kind))
        {
            sb.AppendLine($"## {g.Key}");
            foreach (var e in g.Take(50)) sb.AppendLine($"- [{e.At:HH:mm}] {(e.Success ? "✓" : "✗")} {e.Name}: {e.Summary}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public sealed record PatchProposal(string RelativePath, string OldText, string NewText, string Reason, DateTimeOffset CreatedAt);
public sealed record PatchApplyResult(bool Success, string? Message)
{
    public static PatchApplyResult Ok() => new(true, null);
    public static PatchApplyResult Fail(string m) => new(false, m);
}
public sealed record Backup(string Stamp, string Path);
public sealed record BuildResult(bool Success, string Output, string? Error, int ExitCode);
