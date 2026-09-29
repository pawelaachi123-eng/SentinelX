using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace SentinelX;

/// <summary>
/// File tools over Sentinel-created files and common user folders. Search is bounded, skips reparse
/// points, and remembers one unambiguous selection so follow-ups like "move it to Documents" work.
/// </summary>
public sealed class FileWorkspaceService : Services.Files.IFileService
{
    private const int MaxSearchFiles = 20_000;
    private const int MaxReturnedMatches = 8;
    private const long MaxReadableTextBytes = 1024 * 1024;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".json", ".xml", ".log", ".ini", ".cfg", ".yaml", ".yml", ".cs", ".html", ".htm" };
    private static readonly HashSet<string> NeverShellOpenExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".msi", ".msp", ".scr", ".vbs", ".js", ".lnk", ".url" };
    private static readonly HashSet<string> SearchStopWords = new(StringComparer.Ordinal)
        { "plik", "pliku", "plikach", "z", "ze", "w", "we", "na", "do", "ten", "ta", "to", "ktory", "ktora", "ktore", "moj", "moja", "moje", "o", "i", "oraz", "dla", "jest", "byl", "byla", "bylo" };

    private readonly string workspace;
    private readonly string desktop;
    private readonly string documents;
    private readonly string downloads;
    private readonly ActionHistoryService history;
    private List<string> pendingMatches = [];

    /// <summary>Last file created, found, opened, or moved by this service in this process.</summary>
    public string? LastFile { get; private set; }

    public FileWorkspaceService(string? root = null, string? desktop = null, ActionHistoryService? history = null,
        string? documents = null, string? downloads = null)
    {
        workspace = Path.Combine(root ?? AppPaths.Root, "CreatedFiles");
        this.desktop = desktop ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        this.documents = documents ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        this.downloads = downloads ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        this.history = history ?? new ActionHistoryService(root);
    }

    public async Task<string?> ProcessAsync(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string raw = (command ?? string.Empty).Trim();
        string normalized = ConversationMemoryService.Normalize(raw).Trim().TrimEnd('.', '!', '?');

        if (pendingMatches.Count > 0 && (normalized is "anuluj" or "cancel"))
        {
            pendingMatches.Clear();
            return "Anulowano wybór pliku.";
        }
        if (pendingMatches.Count > 0 && TryChooseMatch(normalized, out string? chosen))
        {
            pendingMatches.Clear();
            LastFile = chosen;
            return "Zaznaczyłem plik: " + chosen;
        }
        // An unrelated command abandons a pending choice rather than reusing stale selection results.
        if (pendingMatches.Count > 0) pendingMatches.Clear();

        var describedSearch = Regex.Match(raw,
            @"^(?:znajdź|znajdz|wyszukaj|szukaj)(?: mi)?\s+(?:plik|dokument|prezentację|prezentacje|arkusz)(?:\s+(?:o|z|na temat|który zawiera|ktory zawiera|zawiera|zawierający|zawierająca|zawierajacy|zawierajaca))?\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (describedSearch.Success) return await SearchFilesAsync(describedSearch.Groups[1].Value, raw, token);

        var search = Regex.Match(raw, @"^(?:znajdź|znajdz|wyszukaj|szukaj)(?: mi)?\s+plik\s+(.+)$", RegexOptions.IgnoreCase);
        if (search.Success) return await SearchFilesAsync(search.Groups[1].Value, raw, token);

        var moveToFolder = Regex.Match(normalized,
            @"^(?:przenies|przesun)\s+(?:go|to|ten plik|ten dokument|plik|dokument)\s+(?:do|na)\s+(?:folderu?\s+)?(?<where>pulpit|desktop|dokumenty|documents|pobrane|downloads)$",
            RegexOptions.IgnoreCase);
        if (moveToFolder.Success) return await MoveSelectedToKnownFolderAsync(moveToFolder.Groups["where"].Value, raw, token);

        var rename = Regex.Match(raw,
            """^(?:(?:zmień|zmien)\s+(?:(?:jego|tego)\s+)?(?:nazwę|nazwe)|przemianuj)\s+(?:(?:tego|ten|jego)\s+)?(?:pliku|dokumentu|go|jego)?\s*(?:na\s+)?(?<name>.+?)\s*$""",
            RegexOptions.IgnoreCase);
        if (rename.Success) return RenameSelected(rename.Groups["name"].Value.Trim().Trim('"', '„', '”'), raw);

        var createFolder = Regex.Match(raw,
            """^(?:utwórz|utworz|stwórz|stworz)\s+folder\s+(?<name>[^\\:*?"<>|]+?)(?:\s+(?:na pulpicie|w dokumentach|w folderze dokumenty|w folderze sentinel))?$""",
            RegexOptions.IgnoreCase);
        if (createFolder.Success) return CreateFolder(createFolder.Groups["name"].Value.Trim(), raw);

        var copy = Regex.Match(raw, @"^(skopiuj|przenieś|przenies)\s+ten plik\s+(?:jako|do)\s+(.+)$", RegexOptions.IgnoreCase);
        if (copy.Success)
            return await CopyOrMoveAsync(raw, copy.Groups[2].Value.Trim().Trim('"'), !ConversationMemoryService.Normalize(copy.Groups[1].Value).Equals("skopiuj", StringComparison.Ordinal), token);

        var create = Regex.Match(raw,
            @"^(?:stwórz|stworz|utwórz|utworz)\s+plik\s+(?<name>[^\r\n:]+?)(?:\s+(?<where>na pulpicie|w dokumentach|w folderze dokumenty|w folderze sentinel))?(?:\s*:\s*(?<content>[\s\S]*))?$",
            RegexOptions.IgnoreCase);
        var write = Regex.Match(raw, @"^(wpisz|dopisz)\s+do niego\s+([\s\S]+)$", RegexOptions.IgnoreCase);
        var line = Regex.Match(raw, @"^(?:zmień|zmien)\s+(\d+)\s+linie?\s+(?:na\s+)?([\s\S]+)$", RegexOptions.IgnoreCase);

        bool show = normalized is "pokaz ten plik" or "przeczytaj ten plik" or "pokaz go" or "przeczytaj go";
        bool open = normalized is "otworz ten plik" or "otworz go";
        bool read = show || open;
        bool closeReference = normalized is "zamknij ten plik" or "zamknij go";
        if (closeReference) return "Nie mam bezpiecznej kontroli nad zamykaniem dokumentu. Zamknij go w aplikacji; nie zakończę procesu na ślepo.";
        if (!create.Success && !write.Success && !line.Success && !read) return null;
        if (read) return await OpenOrReadSelectedAsync(show, token);

        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_WRITE", raw);
        try
        {
            string path, content;
            if (create.Success)
            {
                string name = create.Groups["name"].Value.Trim().Trim('"');
                if (!IsSafeName(name)) throw new InvalidOperationException("Podaj zwykłą nazwę pliku bez ścieżki, np. notatka.txt.");
                string root = ResolveFolder(create.Groups["where"].Value);
                Directory.CreateDirectory(root);
                path = Path.Combine(root, name);
                content = create.Groups["content"].Value;
                if (content.Length > 1_000_000) throw new InvalidOperationException("Treść przekracza limit 1 MB.");
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                await file.WriteAsync(Encoding.UTF8.GetBytes(content), token);
                await file.FlushAsync(token);
            }
            else
            {
                path = LastFile ?? throw new InvalidOperationException("Nie wiem, do którego pliku się odnosisz. Najpierw znajdź go albo podaj nazwę.");
                EnsureSafeUserFile(path);
                if (!File.Exists(path) || new FileInfo(path).Length > MaxReadableTextBytes || !TextExtensions.Contains(Path.GetExtension(path)))
                    throw new InvalidOperationException("Edycja jest dostępna tylko dla istniejących plików tekstowych do 1 MB.");
                string before = await File.ReadAllTextAsync(path, token);
                if (line.Success)
                {
                    var lines = before.Replace("\r\n", "\n").Split('\n');
                    if (!int.TryParse(line.Groups[1].Value, out int index) || index < 1 || index > lines.Length) throw new InvalidOperationException("Taka linia nie istnieje.");
                    lines[index - 1] = line.Groups[2].Value;
                    content = string.Join(Environment.NewLine, lines);
                }
                else content = write.Groups[1].Value.Equals("dopisz", StringComparison.OrdinalIgnoreCase)
                    ? before + Environment.NewLine + write.Groups[2].Value : write.Groups[2].Value;
                if (content.Length > 1_000_000) throw new InvalidOperationException("Treść przekracza limit 1 MB.");
                string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N")[..6] + ".bak";
                File.Copy(path, backup, false);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temp, content, token);
                    token.ThrowIfCancellationRequested();
                    File.Move(temp, path, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            string actual = await File.ReadAllTextAsync(path, token);
            if (actual != content) throw new IOException("Zapis nie zgadza się z odczytem kontrolnym.");
            LastFile = path;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
            var result = ActionExecutionResult.VerifiedSuccess("Zapisano i sprawdzono plik.", path + "\nSHA-256: " + hash);
            history.AddResult(id, "FILE_WRITE", raw, result);
            return "Gotowe. Zapisano i zweryfikowano plik: " + path;
        }
        catch (OperationCanceledException)
        {
            history.AddCancelled(id, "FILE_WRITE", raw, "Przerwano; sprawdź plik i kopię .bak przed ponowieniem.");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            var result = ActionExecutionResult.Failure("Nie zapisano potwierdzonej zmiany: " + ex.Message);
            history.AddResult(id, "FILE_WRITE", raw, result);
            return "Nie udało się: " + ex.Message;
        }
    }

    private async Task<string> SearchFilesAsync(string description, string command, CancellationToken token)
    {
        string query = description.Trim().Trim('"', '\'');
        if (query.Length is < 2 or > 160) return "Podaj krótki opis lub nazwę pliku do znalezienia.";
        var roots = SearchRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = await Task.Run(() => Search(roots, query, token), token);
        token.ThrowIfCancellationRequested();
        string id = history.CreateActionId();
        if (matches.Count == 0)
        {
            history.AddResult(id, "FILE_SEARCH", command, ActionExecutionResult.VerifiedSuccess("Wyszukiwanie zakończone; nie znaleziono dopasowania.", $"Zakres: {string.Join("; ", roots)}; limit {MaxSearchFiles} plików."));
            return "Nie znalazłem pasującego pliku w Pulpicie, Dokumentach, Pobranych, Obrazach ani plikach utworzonych przez Sentinel.";
        }

        if (matches.Count == 1 || (matches.Count > 1 && matches[0].Score >= matches[1].Score + Math.Max(12, matches[1].Score / 2)))
        {
            LastFile = matches[0].Path;
            history.AddResult(id, "FILE_SEARCH", command, ActionExecutionResult.VerifiedSuccess("Znaleziono plik.", LastFile));
            return "Znalazłem: " + LastFile;
        }

        pendingMatches = matches.Take(MaxReturnedMatches).Select(x => x.Path).ToList();
        string list = string.Join(Environment.NewLine, pendingMatches.Select((path, index) => $"{index + 1}. {path}"));
        history.AddResult(id, "FILE_SEARCH", command, ActionExecutionResult.VerifiedSuccess("Znaleziono kilka możliwych plików.", list));
        return "Znalazłem kilka pasujących plików. Który wybrać? Powiedz numer.\n" + list;
    }

    private List<(string Path, int Score)> Search(IReadOnlyList<string> roots, string query, CancellationToken token)
    {
        string normalizedQuery = ConversationMemoryService.Normalize(query);
        string[] terms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length > 1 && !SearchStopWords.Contains(x)).Distinct().ToArray();
        if (terms.Length == 0) terms = [normalizedQuery];
        var results = new List<(string Path, int Score)>();
        int scanned = 0;
        foreach (string root in roots)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
                foreach (string path in Directory.EnumerateFiles(root, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (++scanned > MaxSearchFiles) return results.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(g => (Path: g.Key, Score: g.Max(x => x.Score))).OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(30).ToList();
                    try
                    {
                        var info = new FileInfo(path);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        string name = ConversationMemoryService.Normalize(Path.GetFileNameWithoutExtension(path));
                        int score = string.Equals(name, normalizedQuery, StringComparison.Ordinal) ? 100 : name.Contains(normalizedQuery, StringComparison.Ordinal) ? 70 : 0;
                        foreach (string term in terms)
                            if (name.Contains(term, StringComparison.Ordinal)) score += 18;
                        if (score < 18 && TextExtensions.Contains(info.Extension) && info.Length <= MaxReadableTextBytes)
                        {
                            try
                            {
                                string text = ConversationMemoryService.Normalize(File.ReadAllText(path));
                                if (text.Contains(normalizedQuery, StringComparison.Ordinal)) score += 22;
                                foreach (string term in terms)
                                    if (text.Contains(term, StringComparison.Ordinal)) score += 10;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) { }
                        }
                        if (score >= 18) results.Add((path, score));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return results.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(g => (Path: g.Key, Score: g.Max(x => x.Score)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(30).ToList();
    }

    private async Task<string> OpenOrReadSelectedAsync(bool showContents, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string path = LastFile ?? string.Empty;
        if (path.Length == 0 || !File.Exists(path)) return "Nie mam pliku wskazanego w tej rozmowie. Najpierw wyszukaj go, np. „znajdź plik budżet”.";
        try
        {
            EnsureSafeUserFile(path);
            string extension = Path.GetExtension(path);
            if (showContents)
            {
                if (!TextExtensions.Contains(extension) || new FileInfo(path).Length > MaxReadableTextBytes)
                    return "Mogę pokazać treść tylko obsługiwanego pliku tekstowego do 1 MB; otwórz go w przypisanej aplikacji poleceniem „otwórz go”.";
                string contents = await File.ReadAllTextAsync(path, token);
                return Path.GetFileName(path) + "\n\n" + contents;
            }
            if (NeverShellOpenExtensions.Contains(extension)) return "Nie uruchamiam pliku wykonywalnego, skryptu ani skrótu.";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return "Wysłano „" + Path.GetFileName(path) + "” do domyślnej aplikacji.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return "Nie udało się otworzyć pliku: " + ex.Message; }
    }

    private string CreateFolder(string name, string command)
    {
        if (!IsSafeRelativePath(name)) return "Podaj nazwę folderu bez ścieżek bezwzględnych ani segmentu „..”.";
        string normalized = ConversationMemoryService.Normalize(command);
        string root = normalized.Contains("pulpicie", StringComparison.Ordinal) ? desktop
            : normalized.Contains("sentinel", StringComparison.Ordinal) ? workspace : documents;
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!IsWithin(path, root)) return "Odrzucono ścieżkę poza wybranym folderem.";
        try
        {
            if (Directory.Exists(path)) return "Folder już istnieje: " + path;
            string id = history.CreateActionId();
            Directory.CreateDirectory(path);
            if (!Directory.Exists(path)) return "Nie udało się potwierdzić utworzenia folderu.";
            history.AddResult(id, "FOLDER_CREATE", command, ActionExecutionResult.VerifiedSuccess("Utworzono folder.", path));
            return "Gotowe. Utworzyłem folder: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return "Nie udało się utworzyć folderu: " + ex.Message; }
    }

    private async Task<string> MoveSelectedToKnownFolderAsync(string destinationName, string command, CancellationToken token)
    {
        string? source = LastFile;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return "Nie wiem, który plik przenieść. Najpierw wyszukaj go albo podaj nazwę.";
        try
        {
            EnsureSafeUserFile(source);
            string destinationRoot = ResolveFolder(destinationName);
            Directory.CreateDirectory(destinationRoot);
            string target = Path.Combine(destinationRoot, Path.GetFileName(source));
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return "Plik jest już w tym folderze.";
            if (File.Exists(target) || Directory.Exists(target)) return "Nie przeniosłem pliku, bo w folderze docelowym istnieje już element o tej nazwie: " + target;
            token.ThrowIfCancellationRequested();
            File.Move(source, target);
            LastFile = target;
            if (File.Exists(source) || !File.Exists(target)) return "Przeniesienie nie przeszło weryfikacji; sprawdź oba foldery.";
            history.AddResult(history.CreateActionId(), "FILE_MOVE", command, ActionExecutionResult.VerifiedSuccess("Przeniesiono plik.", source + " → " + target));
            return "Gotowe. Przeniosłem plik do " + Path.GetFileName(destinationRoot) + ".";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return "Nie udało się przenieść pliku: " + ex.Message; }
    }

    private string RenameSelected(string newName, string command)
    {
        if (string.IsNullOrWhiteSpace(LastFile) || !File.Exists(LastFile)) return "Nie wiem, który plik zmienić. Najpierw wyszukaj go albo podaj nazwę.";
        if (!IsSafeName(newName)) return "Podaj nową nazwę pliku bez ścieżki.";
        try
        {
            EnsureSafeUserFile(LastFile);
            string source = LastFile;
            string target = Path.Combine(Path.GetDirectoryName(source)!, newName);
            if (File.Exists(target) || Directory.Exists(target)) return "Nie zmieniłem nazwy, bo cel już istnieje: " + target;
            File.Move(source, target);
            if (File.Exists(source) || !File.Exists(target)) return "Zmiana nazwy nie przeszła weryfikacji.";
            LastFile = target;
            history.AddResult(history.CreateActionId(), "FILE_RENAME", command, ActionExecutionResult.VerifiedSuccess("Zmieniono nazwę pliku.", source + " → " + target));
            return "Gotowe. Nowa nazwa: " + Path.GetFileName(target) + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return "Nie udało się zmienić nazwy: " + ex.Message; }
    }

    private async Task<string> CopyOrMoveAsync(string command, string name, bool move, CancellationToken token)
    {
        string id = history.CreateActionId(), type = move ? "FILE_MOVE" : "FILE_COPY";
        history.AddRunning(id, type, command);
        try
        {
            if (!IsSafeName(name)) throw new InvalidOperationException("Podaj zwykłą nazwę pliku bez ścieżki.");
            string source = LastFile ?? throw new InvalidOperationException("Najpierw utwórz lub znajdź plik.");
            EnsureSafeUserFile(source);
            var info = new FileInfo(source);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Plik niedostępny lub jest dowiązaniem.");
            string target = Path.Combine(Path.GetDirectoryName(source)!, name);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Plik docelowy już istnieje; nadpisywanie jest wyłączone.");
            token.ThrowIfCancellationRequested();
            if (move) File.Move(source, target, false); else File.Copy(source, target, false);
            LastFile = target;
            token.ThrowIfCancellationRequested();
            byte[] after = await File.ReadAllBytesAsync(target, token);
            string hash = Convert.ToHexString(SHA256.HashData(after));
            if (!File.Exists(target) || (move && File.Exists(source))) throw new IOException("Weryfikacja pliku nie powiodła się.");
            var result = ActionExecutionResult.VerifiedSuccess(move ? "Przeniesiono plik." : "Skopiowano plik.", $"{source} → {target}\nSHA-256: {hash}");
            history.AddResult(id, type, command, result);
            return "Gotowe. " + result.Message;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, type, command, "Przerwano; ukończone zmiany nie są cofane."); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            history.AddResult(id, type, command, ActionExecutionResult.Failure(ex.Message));
            return "Nie udało się: " + ex.Message;
        }
    }

    private string ResolveFolder(string? name) => ConversationMemoryService.Normalize(name ?? string.Empty) switch
    {
        "pulpit" or "desktop" or "na pulpicie" => desktop,
        "pobrane" or "downloads" => downloads,
        "sentinel" or "w folderze sentinel" => workspace,
        "dokumenty" or "documents" or "w dokumentach" or "w folderze dokumenty" => documents,
        _ => workspace
    };

    private void EnsureSafeUserFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!SearchRoots().Where(Directory.Exists).Any(root => IsWithin(full, root)))
            throw new UnauthorizedAccessException("Plik jest poza dozwolonymi folderami użytkownika.");
        if (!File.Exists(full)) throw new FileNotFoundException("Nie znaleziono pliku.", full);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Operacje na dowiązaniach są zablokowane.");
    }

    private static bool IsWithin(string path, string root)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelativePath(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 240 && !Path.IsPathRooted(name) &&
        name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).All(x => x is not ("." or "..") && x.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    internal static bool IsSafeName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return name.Length is > 0 and <= 150 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') &&
            name is not ("." or "..") && stem is not ("CON" or "PRN" or "AUX" or "NUL") && !Regex.IsMatch(stem, @"^(COM|LPT)[0-9]$");
    }

    private bool TryChooseMatch(string normalized, out string? path)
    {
        path = null;
        if (normalized is "anuluj" or "cancel" or "nie") return false;
        var numeric = Regex.Match(normalized, @"^(?:nr\s*)?(\d+)$");
        int index = numeric.Success && int.TryParse(numeric.Groups[1].Value, out int parsed) ? parsed : normalized switch
        {
            "pierwszy" or "pierwsza" or "pierwsze" => 1,
            "drugi" or "druga" or "drugie" => 2,
            "trzeci" or "trzecia" or "trzecie" => 3,
            "czwarty" or "czwarta" or "czwarte" => 4,
            "piaty" or "piata" or "piate" => 5,
            "szosty" or "szosta" or "szoste" => 6,
            "siodmy" or "siodma" or "siodme" => 7,
            "osmy" or "osma" or "osme" => 8,
            _ => 0
        };
        if (index < 1 || index > pendingMatches.Count) return false;
        path = pendingMatches[index - 1];
        return true;
    }

    private IReadOnlyList<string> SearchRoots() =>
        new[] { workspace, desktop, documents, downloads, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
