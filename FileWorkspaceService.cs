using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Only edits the last file created through this service; every replacement keeps a backup.</summary>
public sealed class FileWorkspaceService : Services.Files.IFileService
{
    private readonly string workspace;
    private readonly string desktop;
    private readonly ActionHistoryService history;
    public string? LastFile { get; private set; }
    public FileWorkspaceService(string? root = null, string? desktop = null, ActionHistoryService? history = null)
    {
        workspace = Path.Combine(root ?? AppPaths.Root, "CreatedFiles");
        this.desktop = desktop ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        this.history = history ?? new ActionHistoryService(root);
    }
    public async Task<string?> ProcessAsync(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var copy = Regex.Match(command, @"^(skopiuj|przenieś|przenies) ten plik (?:jako|do) (.+)$", RegexOptions.IgnoreCase);
        if (copy.Success) return await CopyOrMoveAsync(command, copy.Groups[2].Value.Trim().Trim('"'), copy.Groups[1].Value != "skopiuj", token);
        var search = Regex.Match(command, @"^(?:znajdź|znajdz|szukaj) plik (.+)$", RegexOptions.IgnoreCase);
        if (search.Success)
        {
            var matches = Directory.Exists(workspace) ? Directory.EnumerateFiles(workspace, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Where(path => Path.GetFileName(path).Contains(search.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase)).Take(50).ToArray() : [];
            token.ThrowIfCancellationRequested();
            string text = matches.Length == 0 ? "Brak pasujących plików w folderze Sentinel." : string.Join(Environment.NewLine, matches);
            history.AddResult(history.CreateActionId(), "FILE_SEARCH", command, ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {workspace}; odczyt {DateTime.Now:O}; limit 50 wyników"));
            return text;
        }
        var create = Regex.Match(command, @"^(?:stwórz|stworz|utwórz|utworz)\s+plik\s+([^\r\n:]+?)(?:\s+(na pulpicie|w folderze sentinel))?(?:\s*:\s*([\s\S]*))?$", RegexOptions.IgnoreCase);
        var write = Regex.Match(command, @"^(wpisz|dopisz)\s+do niego\s+([\s\S]+)$", RegexOptions.IgnoreCase);
        var line = Regex.Match(command, @"^(?:zmień|zmien)\s+(\d+)\s+linie?\s+(?:na\s+)?([\s\S]+)$", RegexOptions.IgnoreCase);
        bool read = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.') is "pokaz ten plik" or "przeczytaj ten plik";
        if (!create.Success && !write.Success && !line.Success && !read) return null;
        if (read) return LastFile != null && File.Exists(LastFile) ? LastFile + "\n\n" + await File.ReadAllTextAsync(LastFile, token) : "Najpierw utwórz plik w tej rozmowie.";
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_WRITE", command);
        try
        {
            string path, content;
            if (create.Success)
            {
                string name = create.Groups[1].Value.Trim().Trim('"');
                if (!IsSafeName(name)) throw new InvalidOperationException("Podaj zwykłą nazwę, np. test.txt. Ścieżki i nazwy urządzeń Windows są niedozwolone.");
                string root = create.Groups[2].Value.Equals("na pulpicie", StringComparison.OrdinalIgnoreCase) ? desktop : workspace;
                Directory.CreateDirectory(root);
                path = Path.Combine(root, name); content = create.Groups[3].Value;
                if (content.Length > 1000000) throw new InvalidOperationException("Treść przekracza limit 1 MB.");
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                await file.WriteAsync(Encoding.UTF8.GetBytes(content), token); await file.FlushAsync(token);
            }
            else
            {
                path = LastFile ?? throw new InvalidOperationException("Najpierw utwórz plik. „Do niego” odnosi się wyłącznie do ostatnio utworzonego pliku.");
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidOperationException("Plik nie istnieje albo przekracza limit 1 MB.");
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Edycja dowiązań jest niedozwolona.");
                string before = await File.ReadAllTextAsync(path, token);
                if (line.Success)
                {
                    var lines = before.Replace("\r\n", "\n").Split('\n');
                    if (!int.TryParse(line.Groups[1].Value, out int index) || index < 1 || index > lines.Length) throw new InvalidOperationException("Taka linia nie istnieje.");
                    lines[index - 1] = line.Groups[2].Value; content = string.Join(Environment.NewLine, lines);
                }
                else content = write.Groups[1].Value.Equals("dopisz", StringComparison.OrdinalIgnoreCase) ? before + Environment.NewLine + write.Groups[2].Value : write.Groups[2].Value;
                if (content.Length > 1000000) throw new InvalidOperationException("Treść przekracza limit 1 MB.");
                string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N")[..6] + ".bak";
                File.Copy(path, backup, false);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllTextAsync(temp, content, token); token.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            string actual = await File.ReadAllTextAsync(path, token);
            if (actual != content) throw new IOException("Zapis nie zgadza się z odczytem kontrolnym.");
            LastFile = path;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
            var result = ActionExecutionResult.VerifiedSuccess("Zapisano i sprawdzono plik.", path + "\nSHA-256: " + hash);
            history.AddResult(id, "FILE_WRITE", command, result);
            return "VERIFIED • " + id + "\n" + result.Message + "\n" + result.Evidence;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_WRITE", command, "Przerwano; sprawdź plik i kopię .bak przed ponowieniem."); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var result = ActionExecutionResult.Failure("Nie zapisano potwierdzonej zmiany: " + ex.Message);
            history.AddResult(id, "FILE_WRITE", command, result); return "FAILED • " + id + "\n" + result.Message;
        }
    }
    private async Task<string> CopyOrMoveAsync(string command, string name, bool move, CancellationToken token)
    {
        string id = history.CreateActionId(), type = move ? "FILE_MOVE" : "FILE_COPY";
        history.AddRunning(id, type, command);
        try
        {
            if (!IsSafeName(name)) throw new InvalidOperationException("Podaj zwykłą nazwę pliku bez ścieżki.");
            string source = LastFile ?? throw new InvalidOperationException("Najpierw utwórz plik w tej rozmowie.");
            var info = new FileInfo(source);
            if (!info.Exists || info.Length > 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Plik niedostępny, większy niż 1 MB lub jest dowiązaniem.");
            string target = Path.Combine(Path.GetDirectoryName(source)!, name);
            byte[] before = await File.ReadAllBytesAsync(source, token);
            token.ThrowIfCancellationRequested();
            // No overwrite. Move is restricted to the same directory of a file created by Sentinel.
            if (move) File.Move(source, target, false); else File.Copy(source, target, false);
            LastFile = target; // Preserve the real location even if verification is interrupted.
            byte[] after = await File.ReadAllBytesAsync(target, token);
            string hash = Convert.ToHexString(SHA256.HashData(before));
            if (!before.AsSpan().SequenceEqual(after) || (move && File.Exists(source))) throw new IOException("Weryfikacja pliku nie powiodła się.");
            var result = ActionExecutionResult.VerifiedSuccess(move ? "Przeniesiono plik." : "Skopiowano plik.", $"{source} → {target}\nSHA-256: {hash}");
            history.AddResult(id, type, command, result);
            return $"VERIFIED • {id}\n{result.Message}\n{result.Evidence}";
        }
        catch (OperationCanceledException) { history.AddCancelled(id, type, command, "Przerwano; ukończony zapis nie jest cofany."); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            history.AddResult(id, type, command, ActionExecutionResult.Failure(ex.Message));
            return $"FAILED • {id}\n{ex.Message}";
        }
    }
    internal static bool IsSafeName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return name.Length is > 0 and <= 150 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') &&
            name is not ("." or "..") && stem is not ("CON" or "PRN" or "AUX" or "NUL") && !Regex.IsMatch(stem, @"^(COM|LPT)[0-9]$");
    }
}
