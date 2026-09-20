using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace SentinelX;

public sealed class AppLauncherService : Services.Apps.IAppLauncherService
{
    private readonly Func<string> browserPreference;
    public AppLauncherService(Func<string>? browserPreference = null) => this.browserPreference = browserPreference ?? (() => "Brave");
    private static readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discorda"] = "discord", ["dyskorda"] = "discord", ["dyskord"] = "discord", ["disc"] = "discord", ["disa"] = "discord", ["dc"] = "discord",
        ["steama"] = "steam", ["stim"] = "steam", ["stima"] = "steam",
        ["brave browser"] = "brave", ["brava"] = "brave", ["brawe"] = "brave", ["brave'a"] = "brave", ["bravea"] = "brave",
        ["chrome'a"] = "chrome", ["chromea"] = "chrome", ["chroma"] = "chrome", ["google chrome"] = "chrome",
        ["yt"] = "youtube", ["jutub"] = "youtube", ["youtuba"] = "youtube", ["youtube'a"] = "youtube", ["jutuba"] = "youtube",
        ["cs"] = "cs2", ["ce es"] = "cs2", ["cs go"] = "cs2", ["countera"] = "cs2", ["counter strike'a"] = "cs2", ["counter strike"] = "cs2", ["counter strike 2"] = "cs2",
        ["notatnika"] = "notatnik", ["notepad"] = "notatnik", ["kalkulatora"] = "kalkulator", ["calculator"] = "kalkulator",
        ["task manager"] = "menedzer zadan", ["menedzera zadan"] = "menedzer zadan", ["menedzer zadan windows"] = "menedzer zadan",
        ["explorer"] = "eksplorator", ["eksploratora"] = "eksplorator", ["eksplorator plikow"] = "eksplorator", ["eksploratora plikow"] = "eksplorator",
        ["ustawienia windows"] = "ustawienia", ["spotifya"] = "spotify", ["spotify'a"] = "spotify", ["gmaila"] = "gmail", ["faceita"] = "faceit"
    };
    private static readonly HashSet<string> knownTargets = new(StringComparer.OrdinalIgnoreCase)
    { "discord", "steam", "cs2", "brave", "chrome", "notatnik", "kalkulator", "menedzer zadan", "eksplorator", "ustawienia", "youtube", "google", "gmail", "faceit", "chatgpt", "spotify" };

    public static string CanonicalizeLaunchTarget(string target)
    {
        string key = Normalize(target).TrimEnd('.', '!', '?').Trim();
        return aliases.TryGetValue(key, out string? name) ? name : key;
    }
    public static bool IsKnownLaunchTarget(string target) => knownTargets.Contains(CanonicalizeLaunchTarget(target));
    public static IReadOnlyList<string> SplitLaunchTargets(string target) => AppLaunchPlan.Parse(target).Targets;

    public async Task<ActionExecutionResult> LaunchAsync(string target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = CanonicalizeLaunchTarget(target);
        if (IsSafeWebUrl(target)) return OpenWebsite(target.Trim(), target.Trim());
        if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.Contains('\\') || target.Contains('/'))
            return ActionExecutionResult.Failure("Dozwolone są nazwy aplikacji i adresy HTTP/HTTPS.", "Nie wykonano przekazanego URI ani ścieżki.");
        switch (normalized)
        {
            case "discord":
                string discordUpdate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord", "Update.exe");
                return File.Exists(discordUpdate)
                    ? await LaunchAndVerifyAsync(discordUpdate, ["Discord"], "Discord", cancellationToken, ["--processStart", "Discord.exe"], 6000)
                    : await LaunchAndVerifyAsync("discord:", ["Discord"], "Discord", cancellationToken, timeoutMilliseconds: 6000);
            case "steam": return await LaunchAndVerifyAsync(GetSteamExe() ?? "steam://open/main", ["steam"], "Steam", cancellationToken, timeoutMilliseconds: 7000);
            case "cs2": return await LaunchAndVerifyAsync("steam://rungameid/730", ["cs2"], "CS2", cancellationToken, timeoutMilliseconds: 20000);
            case "brave":
            case "chrome":
                string? browser = FindBrowser(normalized);
                return browser == null ? ActionExecutionResult.Failure($"Nie znalazłem przeglądarki {normalized}.")
                    : await LaunchAndVerifyAsync(browser, [normalized], normalized == "brave" ? "Brave" : "Google Chrome", cancellationToken);
            case "notatnik": return await LaunchAndVerifyAsync("notepad.exe", ["notepad"], "Notatnik", cancellationToken);
            case "kalkulator": return await LaunchAndVerifyAsync("calc.exe", ["CalculatorApp", "Calculator"], "Kalkulator", cancellationToken);
            case "menedzer zadan": return await LaunchAndVerifyAsync("taskmgr.exe", ["Taskmgr"], "Menedżer zadań", cancellationToken);
            case "eksplorator": return StartShell("explorer.exe", "Eksplorator Windows");
            case "ustawienia": return StartShell("ms-settings:", "Ustawienia Windows");
            case "youtube": return OpenWebsite("https://www.youtube.com/", "YouTube");
            case "google": return OpenWebsite("https://www.google.com/", "Google");
            case "gmail": return OpenWebsite("https://mail.google.com/", "Gmail");
            case "faceit": return OpenWebsite("https://www.faceit.com/", "FACEIT");
            case "chatgpt": return OpenWebsite("https://chatgpt.com/", "ChatGPT");
            case "spotify": return await LaunchAndVerifyAsync("spotify:", ["Spotify"], "Spotify", cancellationToken);
        }
        var matches = await Task.Run(() => FindStartMenuMatches(normalized, cancellationToken), cancellationToken);
        if (matches.Count == 1) return StartShell(matches[0], Path.GetFileNameWithoutExtension(matches[0]));
        return matches.Count > 1
            ? ActionExecutionResult.Failure("Nazwa pasuje do kilku skrótów. Podaj dokładniejszą nazwę.", string.Join(Environment.NewLine, matches))
            : ActionExecutionResult.Failure($"Nie znaleziono aplikacji „{target}”.", "Nie uruchomiono żadnego programu.", "Użyj pełnej nazwy skrótu z menu Start.");
    }

    private static IReadOnlyList<string> FindStartMenuMatches(string normalized, CancellationToken token)
    {
        var matches = new List<string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (Normalize(Path.GetFileNameWithoutExtension(path)) == normalized) matches.Add(path);
                    if (matches.Count >= 3) return matches;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return matches;
    }

    public static bool IsSafeWebUrl(string input) => !input.Any(char.IsControl) &&
        Uri.TryCreate(input.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    public static string BuildSearchUrl(string query, bool youtube)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Brak tekstu do wyszukania.", nameof(query));
        string encoded = Uri.EscapeDataString(query.Trim());
        return youtube ? $"https://www.youtube.com/results?search_query={encoded}" : $"https://www.google.com/search?q={encoded}";
    }
    public Task<ActionExecutionResult> SearchWebAsync(string query, bool youtube, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(ActionExecutionResult.Failure("Brak tekstu do wyszukania."));
        return Task.FromResult(OpenWebsite(BuildSearchUrl(query, youtube), $"Wyszukiwanie {(youtube ? "YouTube" : "Google")}: {query}"));
    }
    public Task<ActionExecutionResult> OpenFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? path = Normalize(folder) switch
        {
            "pulpit" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "dokumenty" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pobrane" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "sentinel" => AppPaths.Root,
            _ => null
        };
        if (path == null) return Task.FromResult(ActionExecutionResult.Failure($"Nie znam folderu „{folder}”."));
        if (!Directory.Exists(path)) return Task.FromResult(ActionExecutionResult.Failure("Folder nie istnieje.", path));
        try
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; info.ArgumentList.Add(path);
            using var process = Process.Start(info);
            return Task.FromResult(ActionExecutionResult.UnverifiedSuccess($"Wysłano polecenie otwarcia folderu {folder}.", $"Folder istnieje: {path}. Nie sprawdzono niezależnie okna Explorer."));
        }
        catch (Exception ex) { return Task.FromResult(ActionExecutionResult.Failure("Nie udało się otworzyć folderu.", ex.Message)); }
    }

    private static async Task<ActionExecutionResult> LaunchAndVerifyAsync(string executable, string[] processNames, string displayName,
        CancellationToken token, string[]? arguments = null, int timeoutMilliseconds = 5000)
    {
        token.ThrowIfCancellationRequested();
        bool alreadyRunning = TryGetProcessEvidence(processNames, out _);
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = true };
            if (arguments != null) foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var launched = Process.Start(info);
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeoutMilliseconds)
            {
                token.ThrowIfCancellationRequested();
                if (TryGetProcessEvidence(processNames, out string evidence))
                    return ActionExecutionResult.VerifiedSuccess(alreadyRunning ? $"{displayName} jest uruchomiony; wysłano polecenie otwarcia." : $"{displayName} uruchomiony.", evidence + " Nie potwierdzono fokusu okna.");
                await Task.Delay(250, token);
            }
            return ActionExecutionResult.UnverifiedSuccess($"Wysłano polecenie uruchomienia {displayName}.", $"Nie wykryto procesu w ciągu {timeoutMilliseconds / 1000} s. Aplikacja mogła nadal się uruchamiać.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ActionExecutionResult.Failure($"Nie udało się uruchomić {displayName}.", ex.Message); }
    }
    private static bool TryGetProcessEvidence(IEnumerable<string> names, out string evidence)
    {
        foreach (string name in names)
        {
            Process[] processes = [];
            try
            {
                processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    evidence = $"Wykryto proces {name}.exe; PID: {string.Join(", ", processes.Select(x => x.Id))}; odczyt: {DateTime.Now:HH:mm:ss}.";
                    return true;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        evidence = ""; return false;
    }
    private static ActionExecutionResult StartShell(string target, string description)
    {
        try
        {
            using var launched = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return ActionExecutionResult.UnverifiedSuccess($"Wysłano polecenie: {description}.", "Windows zaakceptował ShellExecute. Stan docelowego okna nie został niezależnie potwierdzony.");
        }
        catch (Exception ex) { return ActionExecutionResult.Failure($"Nie udało się uruchomić: {description}.", ex.Message); }
    }
    private ActionExecutionResult OpenWebsite(string url, string description)
    {
        if (!IsSafeWebUrl(url)) return ActionExecutionResult.Failure("Odrzucono nieprawidłowy adres. Dozwolone jest tylko HTTP/HTTPS.");
        try
        {
            string preference = browserPreference();
            string? brave = preference == "System" ? null : FindBrowser(preference.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ? "chrome" : "brave");
            var info = new ProcessStartInfo(brave ?? url) { UseShellExecute = brave == null };
            if (brave != null) info.ArgumentList.Add(url);
            using var launched = Process.Start(info);
            return ActionExecutionResult.UnverifiedSuccess($"Wysłano do przeglądarki: {description}.", $"URL: {url}. Nie zweryfikowano zawartości otwartej karty.");
        }
        catch (Exception ex) { return ActionExecutionResult.Failure($"Nie udało się otworzyć {description}.", ex.Message); }
    }
    private static string? FindBrowser(string browser)
    {
        string relative = browser == "brave" ? Path.Combine("BraveSoftware", "Brave-Browser", "Application", "brave.exe") : Path.Combine("Google", "Chrome", "Application", "chrome.exe");
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(x => Path.Combine(Environment.GetFolderPath(x), relative)).FirstOrDefault(File.Exists);
    }
    private static string? GetSteamExe()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? path = key?.GetValue("SteamExe")?.ToString()?.Replace('/', '\\');
            if (path != null && File.Exists(path)) return path;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
        return File.Exists(fallback) ? fallback : null;
    }
    private static string Normalize(string text) => Regex.Replace(CommandRouter.Normalize(text), @"\s+", " ").Trim();
}
