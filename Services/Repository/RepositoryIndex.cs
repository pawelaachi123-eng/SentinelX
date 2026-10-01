using System.Linq;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SentinelX.Models.Repository;

namespace SentinelX.Services.Repository;

/// <summary>
/// Lekki indeks repozytorium użytkownika. Parsuje .cs i .xaml/.xml/.json i buduje
/// mapę plików/klas/metod/testów/konfiguracji. Bez Roslyna — heurystyka regexowa wystarcza
/// do wyszukiwania w kodzie i „znajdź referencje".
/// </summary>
public sealed class RepositoryIndex
{
    private readonly ConcurrentDictionary<string, RepoSymbol> symbols = new();
    private readonly ConcurrentBag<RepoReference> references = new();
    private readonly ConcurrentBag<RepoBuildError> buildErrors = new();

    public DateTimeOffset? LastIndexedAt { get; private set; }
    public int SymbolCount => symbols.Count;

    public async Task IndexAsync(string rootPath, CancellationToken token)
    {
        if (!Directory.Exists(rootPath)) return;
        symbols.Clear(); references.Clear();
        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
        {
            if (token.IsCancellationRequested) break;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".cs" or ".xaml" or ".csproj" or ".json" or ".md" or ".yml" or ".yaml")
            {
                try { await IndexFileAsync(file, rootPath, token).ConfigureAwait(false); } catch { /* skip */ }
            }
        }
        LastIndexedAt = DateTimeOffset.UtcNow;
    }

    private async Task IndexFileAsync(string file, string root, CancellationToken token)
    {
        string relative = Path.GetRelativePath(root, file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var fileId = "file:" + relative;
        symbols[fileId] = new RepoSymbol(fileId, Path.GetFileName(file),
            ext == ".csproj" ? RepoSymbolKind.Project :
            ext == ".json" || ext == ".yml" || ext == ".yaml" ? RepoSymbolKind.Configuration : RepoSymbolKind.File,
            relative, 1, 0);

        if (ext != ".cs") return;
        var text = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
        IndexCSharp(fileId, relative, text);
    }

    private void IndexCSharp(string fileId, string relative, string text)
    {
        var lines = text.Split('\n');
        string? ns = null;
        string? currentType = null;
        var classRegex = new Regex(@"^\s*(?:public|internal|private|protected|static|sealed|abstract|partial|\s)*(class|interface|enum|record|struct)\s+(\w+)");
        var methodRegex = new Regex(@"^\s*(?:public|internal|private|protected|static|async|virtual|override|sealed|\s)*(?:[\w<>,\[\]\?]+\s+)+(\w+)\s*\(");

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var nsMatch = Regex.Match(line, @"^\s*namespace\s+([\w\.]+)");
            if (nsMatch.Success) ns = nsMatch.Groups[1].Value;

            var tm = classRegex.Match(line);
            if (tm.Success)
            {
                var kind = tm.Groups[1].Value switch
                {
                    "class" => RepoSymbolKind.Class,
                    "interface" => RepoSymbolKind.Interface,
                    "enum" => RepoSymbolKind.Enum,
                    "record" => RepoSymbolKind.Record,
                    "struct" => RepoSymbolKind.Struct,
                    _ => RepoSymbolKind.Unknown
                };
                string name = tm.Groups[2].Value;
                string id = $"sym:{relative}:{ns}.{name}";
                symbols[id] = new RepoSymbol(id, name, kind, relative, i + 1, i + 1, fileId);
                currentType = id;
            }
            if (currentType != null)
            {
                var mm = methodRegex.Match(line);
                if (mm.Success)
                {
                    var mname = mm.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(mname) && char.IsLetter(mname[0]) && mname != "if" && mname != "for" && mname != "while" && mname != "switch" && mname != "using")
                    {
                        bool isTest = line.Contains("[Fact]") || line.Contains("[TestMethod]") || line.Contains("[Test]") || mname.StartsWith("Test", StringComparison.OrdinalIgnoreCase);
                        string id = $"sym:{relative}:{ns}.{symbols[currentType].Name}.{mname}:{i}";
                        symbols[id] = new RepoSymbol(id, mname, isTest ? RepoSymbolKind.Test : RepoSymbolKind.Method, relative, i + 1, i + 1, currentType);
                    }
                }
                if (Regex.IsMatch(line, @"^\s*\}")) currentType = null;
            }
        }
    }

    public IReadOnlyList<RepoSymbol> Search(string query, RepoSymbolKind? kind = null, int limit = 50)
    {
        var q = query.Trim().ToLowerInvariant();
        return symbols.Values
            .Where(s => (kind == null || s.Kind == kind) && s.Name.ToLowerInvariant().Contains(q))
            .OrderBy(s => s.Kind).ThenBy(s => s.Name).Take(limit).ToList();
    }

    public RepoSymbol? FindSymbol(string name) =>
        symbols.Values.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<RepoSymbol> FileSymbols(string relativePath) =>
        symbols.Values.Where(s => s.FilePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

    public void RecordBuildErrors(IEnumerable<RepoBuildError> errs)
    {
        foreach (var e in errs) buildErrors.Add(e);
    }

    public IReadOnlyList<RepoBuildError> BuildErrors() => buildErrors.ToList();
    public void ClearBuildErrors() { while (buildErrors.TryTake(out _)) { } }
}
