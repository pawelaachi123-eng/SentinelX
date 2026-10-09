using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SentinelX.Services.Creative;

public sealed record RobloxGameProjectSpec(string Genre, string DisplayName, string Slug)
{
    public string OriginalPrompt { get; init; } = "";
    public string Theme { get; init; } = "adventure";
    public string ArtDirection { get; init; } = "stylized, readable silhouettes, mobile-friendly effects";
    public string CoreLoop { get; init; } = "";
    public string TargetDevice { get; init; } = "cross-platform, desktop + touch";
    public bool GrowthIntent { get; init; }
    public bool PolishLanguage { get; init; }
}

public sealed record RobloxGameProjectResult(bool Success, string Message, string Path, string Sha256, int Files)
{
    public string LuaPath { get; init; } = "";
    public string PlacePath { get; init; } = "";
    public string ProjectDirectory { get; init; } = "";
    public string BlenderScriptPath { get; init; } = "";
}

/// <summary>Builds a local Rojo source tree, standalone server Luau, text Roblox place (.rbxlx), Blender build scripts and a verified ZIP.
/// Execution/opening is deliberately delegated to RobloxCreativeWorkflowService; this class never logs in or publishes.</summary>
public sealed class RobloxGameProjectGenerator
{
    private const int MaximumProjectNameLength = 48;
    private readonly string outputDirectory;
    private static readonly Regex CommandPattern = new(
        @"^\s*roblox\s+(?:gra|projekt)\s*(?::\s*(?<args>[\s\S]*))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public RobloxGameProjectGenerator() : this(Path.Combine(AppPaths.Root, "CreatedGames")) { }

    internal RobloxGameProjectGenerator(string outputDirectory) => this.outputDirectory = Path.GetFullPath(outputDirectory);

    public string OutputDirectory => outputDirectory;

    public static bool IsCommand(string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        try { return CommandPattern.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool TryParseCommand(string command, out RobloxGameProjectSpec spec, out string message)
    {
        spec = new("", "", "");
        message = Usage;
        string original = (command ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        string normalized = ConversationMemoryService.Normalize(original);
        Match match;
        Match originalMatch;
        try
        {
            match = CommandPattern.Match(normalized);
            originalMatch = CommandPattern.Match(original);
        }
        catch (RegexMatchTimeoutException) { return false; }
        if (!match.Success || match.Groups["args"].Value.Trim().Length == 0) return false;

        string args = match.Groups["args"].Value.Trim();
        string originalArgs = originalMatch.Success && originalMatch.Groups["args"].Success
            ? originalMatch.Groups["args"].Value.Trim()
            : args;
        int split = args.IndexOf(' ');
        int originalSplit = originalArgs.IndexOf(' ');
        string rawGenre = split < 0 ? args : args[..split];
        string rawName = originalSplit < 0 ? "" : originalArgs[(originalSplit + 1)..].Trim();
        string genre = ConversationMemoryService.Normalize(rawGenre).Trim().ToLowerInvariant() switch
        {
            "simulator" or "symulator" or "sim" => "simulator",
            "obby" or "parkour" or "platformowka" => "obby",
            "tycoon" or "magnat" => "tycoon",
            "rounds" or "survival" or "przetrwanie" or "rundy" => "rounds",
            "racing" or "wyscig" or "race" => "racing",
            "custom" or "wlasna" or "sandbox" => "custom",
            _ => ""
        };
        if (genre.Length == 0)
        {
            message = "Nie rozpoznaję tego gatunku. Obsługiwane profile prototypu: simulator, obby, tycoon, rounds, racing, custom.\n" + Usage;
            return false;
        }

        string displayName = CleanDisplayName(rawName, genre);
        string slug = Slugify(displayName);
        if (slug.Length == 0)
        {
            message = "Podaj nazwę gry z literami lub cyframi albo poproś Sentinel o wymyślenie tytułu, np. „Stwórz mi grę na Robloxie o kosmicznym górnictwie”.";
            return false;
        }
        spec = new(genre, displayName, slug);
        message = "";
        return true;
    }

    public async Task<RobloxGameProjectResult> GenerateFromCommandAsync(string command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!TryParseCommand(command, out RobloxGameProjectSpec spec, out string message))
            return new(false, message, "", "", 0);
        return await GenerateAsync(spec, token).ConfigureAwait(false);
    }

    public async Task<RobloxGameProjectResult> GenerateAsync(RobloxGameProjectSpec spec, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidSpec(spec)) return new(false, "Parametry projektu gry są nieprawidłowe.", "", "", 0);

        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string baseName = "sentinel-roblox-" + spec.Slug + "-" + runId;
        string target = Path.Combine(outputDirectory, baseName + ".zip");
        string temporary = target + ".tmp";
        string projectDirectory = Path.Combine(outputDirectory, baseName);
        var writtenFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            if ((File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Folder docelowy jest dowiązaniem systemu plików.");
            Directory.CreateDirectory(projectDirectory);
            if ((File.GetAttributes(projectDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Nowy folder projektu nie może być dowiązaniem systemu plików.");

            string standalone = RobloxOneFileGameBuilder.Build(spec);
            string placeXml = RobloxPlaceFileBuilder.Build(spec.DisplayName, standalone);
            Dictionary<string, string> files = BuildFiles(spec);
            files["SentinelGame.server.lua"] = standalone;
            files["place/" + spec.Slug + ".rbxlx"] = placeXml;
            files["tools/blender/build_scene.py"] = RobloxBlenderSceneScript.Build(spec);
            if (files.Count is < 9 or > 24 || files.Any(x => !SafeEntryName(x.Key) || x.Value.Length > 250_000))
                return new(false, "Pakiet przekracza bezpieczne ograniczenia generatora.", "", "", 0);

            foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(projectDirectory, file.Key.Replace('/', Path.DirectorySeparatorChar)));
                string safeRoot = Path.GetFullPath(projectDirectory) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(safeRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Ścieżka wyjściowa wyszła poza nowy folder projektu.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await WriteVerifiedTextAsync(path, file.Value, token).ConfigureAwait(false);
                writtenFiles.Add(path);
            }

            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, useAsync: true))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        await using var entryStream = entry.Open();
                        byte[] bytes = new UTF8Encoding(false).GetBytes(file.Value);
                        await entryStream.WriteAsync(bytes, token).ConfigureAwait(false);
                    }
                }
                await stream.FlushAsync(token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            var verification = await VerifyArchiveAsync(temporary, files, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target);
            string luaPath = Path.Combine(projectDirectory, "SentinelGame.server.lua");
            string placePath = Path.Combine(projectDirectory, "place", spec.Slug + ".rbxlx");
            string blenderPath = Path.Combine(projectDirectory, "tools", "blender", "build_scene.py");
            string luaHash = HashFile(luaPath);
            string placeHash = HashFile(placePath);
            string evidence = $"ZIP: {target} · SHA-256 {verification.Sha256}\nStandalone Luau: {luaPath} · SHA-256 {luaHash}\nRoblox XML place: {placePath} · SHA-256 {placeHash}\nProjekt źródłowy: {projectDirectory}\nWpisy ZIP i pliki wynikowe zostały odczytane ponownie. Nie uruchomiono kompilacji Luau/Studio ani renderu Blendera.";
            ActionEvidenceCapture.Record(new ActionHistoryEntry
            {
                ActionId = "SX-ROBLOX-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Timestamp = DateTime.Now,
                ActionType = "ROBLOX_PROJECT_ZIP",
                Command = spec.OriginalPrompt.Length == 0 ? "Utwórz projekt Roblox: " + spec.DisplayName : "Utwórz projekt Roblox na podstawie briefu użytkownika",
                Status = "VERIFIED",
                Message = "Utworzono lokalny projekt źródłowy, pojedynczy skrypt Luau, tekstowy place .rbxlx i ZIP; odczyt kontrolny bajtów jest zgodny.",
                Evidence = evidence,
                RecoveryAdvice = "Otwórz plik .rbxlx w Roblox Studio i naciśnij Play. Skrypt .lua można też umieścić w ServerScriptService nowego testowego place. Sprawdź Output i testuj na kopii; Sentinel nie publikuje gry."
            });

            string summary = "VERIFIED (odczyt lokalnych plików) · Utworzono pakiet prototypu Roblox „" + spec.DisplayName + "” („" + GenreLabel(spec.Genre) + "”)\n" +
                "Projekt: " + projectDirectory + "\nPlik place do Roblox Studio: " + placePath + "\n" +
                "Jednoplikowy skrypt serwera: " + luaPath + "\nŹródła i briefy ZIP: " + target + "\n" +
                "SHA-256 ZIP: " + verification.Sha256 + "\n" +
                "Place .rbxlx jest tekstowym formatem Roblox, a nie binarnym .rbxl. Odczyt plików został zweryfikowany, ale nie potwierdza kompilacji ani działania w Studio. Nie publikuję gry, nie konfiguruje się tu konta, płatności ani assetów; popularności i zarobku nie da się zagwarantować.";
            return new(true, summary, target, verification.Sha256, files.Count)
            {
                LuaPath = luaPath,
                PlacePath = placePath,
                ProjectDirectory = projectDirectory,
                BlenderScriptPath = blenderPath
            };
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            foreach (string path in writtenFiles) TryDelete(path);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Xml.XmlException)
        {
            TryDelete(temporary);
            foreach (string path in writtenFiles) TryDelete(path);
            return new(false, "Nie udało się bezpiecznie utworzyć pakietu projektu: " + ex.Message, "", "", 0);
        }
    }

    private static Dictionary<string, string> BuildFiles(RobloxGameProjectSpec spec)
    {
        string quotedName = LuaQuote(spec.DisplayName);
        string title = MarkdownText(spec.DisplayName);
        string genreLabel = GenreLabel(spec.Genre);
        string actions = spec.Genre switch
        {
            "simulator" or "custom" => "{ { Key = \"Gather\", Label = \"Zbieraj\" }, { Key = \"Sell\", Label = \"Sprzedaj\" }, { Key = \"Upgrade\", Label = \"Ulepsz\" } }",
            "tycoon" => "{ { Key = \"Collect\", Label = \"Odbierz\" }, { Key = \"Upgrade\", Label = \"Rozbuduj\" } }",
            "racing" => "{ { Key = \"JoinRace\", Label = \"Start wyścigu\" } }",
            _ => "{}"
        };
        (int r, int g, int b) accent = spec.Theme switch
        {
            "space" => (92, 125, 255), "underwater" => (53, 202, 220), "fantasy" => (179, 112, 255),
            "cyberpunk" => (39, 244, 198), "cozy" => (255, 184, 114), "nature" => (117, 212, 112),
            "city" => (255, 177, 88), "horror" => (235, 63, 94), "winter" => (138, 211, 255),
            _ => spec.Genre switch
            {
                "simulator" => (62, 211, 174), "obby" => (255, 130, 94), "tycoon" => (255, 196, 76),
                "rounds" => (109, 157, 255), "racing" => (71, 236, 188), _ => (187, 125, 255)
            }
        };
        var config = $$"""
            -- ReplicatedStorage/Shared/Config.lua
            -- Shared display/configuration only. Currency, purchases and progress are validated on the server.
            return {
                Name = {{quotedName}},
                Genre = "{{spec.Genre}}",
                ThemeKey = "{{spec.Theme}}",
                DataStoreName = "SX_{{spec.Slug}}_v1",
                ActionCooldown = 0.25,
                UpgradeBaseCost = 25,
                MaxActionTextLength = 32,
                RoundIntermission = 10,
                RoundDuration = 25,
                CheckpointCount = 10,
                Theme = {
                    Background = Color3.fromRGB(13, 18, 32),
                    Panel = Color3.fromRGB(23, 31, 51),
                    Accent = Color3.fromRGB({{accent.r}}, {{accent.g}}, {{accent.b}}),
                    Accent2 = Color3.fromRGB(111, 129, 255),
                    Text = Color3.fromRGB(245, 248, 255),
                },
                Actions = {{actions}},
            }
            """;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["default.project.json"] = BuildRojoManifest(spec),
            ["README.md"] = BuildReadme(spec),
            ["GAME_DESIGN.md"] = BuildDesignBrief(spec, genreLabel, title),
            ["MONETIZATION.md"] = BuildMonetizationBrief(spec),
            ["PLAYTEST_PLAN.md"] = BuildPlaytestPlan(spec),
            ["src/shared/Config.lua"] = config,
            ["src/server/Main.server.lua"] = BuildServerScript(),
            ["src/client/Main.client.lua"] = BuildClientScript(),
            ["tools/blender/create_blockout.py"] = BuildBlenderScript(spec, accent)
        };
        return files;
    }

    private static string BuildRojoManifest(RobloxGameProjectSpec spec)
    {
        var tree = new Dictionary<string, object>
        {
            ["$className"] = "DataModel",
            ["ReplicatedStorage"] = new Dictionary<string, object>
            {
                ["Shared"] = new Dictionary<string, string> { ["$path"] = "src/shared" }
            },
            ["ServerScriptService"] = new Dictionary<string, string> { ["$path"] = "src/server" },
            ["StarterPlayer"] = new Dictionary<string, object>
            {
                ["StarterPlayerScripts"] = new Dictionary<string, string> { ["$path"] = "src/client" }
            }
        };
        return JsonSerializer.Serialize(new { name = spec.Slug, tree }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildReadme(RobloxGameProjectSpec spec) => $"""
# {MarkdownText(spec.DisplayName)}

Projekt powstał z krótkiego, naturalnego opisu; wybrany prototyp: **{GenreLabel(spec.Genre)} / {MarkdownText(spec.Theme)}**. Pakiet zawiera niezależny plik place XML `.rbxlx`, jednoplikowy Luau, źródła Rojo, responsywny HUD, plan 3D do Blendera i briefy.

## Start bez dodatkowej komendy

1. Otwórz `place/{spec.Slug}.rbxlx` w Roblox Studio (Sentinel może spróbować otworzyć go po wygenerowaniu, jeśli Studio jest zainstalowane).
2. Kliknij **Play**. Prototyp buduje oznaczony świat i serwerową pętlę rozgrywki; sprawdź okno Output i przetestuj go lokalnie z kilkoma graczami.
3. Alternatywnie skopiuj `SentinelGame.server.lua` do `ServerScriptService` nowego place i naciśnij **Play**.
4. Pełne źródła Rojo są w tym folderze i ZIP-ie. Użyj ich do dalszej pracy, gdy masz zainstalowane Rojo i plugin w Studio.

## Co jest, a czego nie ma

- `.rbxlx` to tekstowy format Roblox place; to nie binarny `.rbxl` ani opublikowana gra.
- Sentinel weryfikuje odczyt plików i ZIP-a, ale tylko Roblox Studio może potwierdzić, że place się otwiera i Luau działa. Wynik nie był testowany na żywym Studio w środowisku generatora.
- Projekt nie zawiera cudzych assetów, ID Game Passów, produktów, reklam, konta twórcy ani konfiguracji publikacji.
- Nie gwarantuje popularności, zarobku, płynności na nieznanym sprzęcie ani przewagi nad studiem.

## Główne pliki

- `SentinelGame.server.lua` — samodzielny prototyp do ServerScriptService: świat, interakcje ProximityPrompt, proste leaderstats i serwerowe sprawdzanie postępu.
- `place/{spec.Slug}.rbxlx` — place tekstowy z tym samym server script w ServerScriptService.
- `src/server/Main.server.lua` i `src/client/Main.client.lua` — wersja modułowa Rojo z serwerową walidacją RemoteEvent i adaptacyjnym HUD-em.
- `GAME_DESIGN.md`, `PLAYTEST_PLAN.md`, `MONETIZATION.md` — założenia, testy i neutralne zasady monetyzacji; nie obietnica wyniku.
- `tools/blender/create_blockout.py` — bezpieczny, ręczny blockout; `build_scene.py` — osobny build skryptu Blender do wygenerowania kopii `.blend` i FBX.

DataStore działa tylko po skonfigurowaniu opublikowanego testowego experience i API Services. Najpierw testuj prywatnie, zbieraj opinie i poprawiaj pętlę. Monetyzację ustawiaj ręcznie po sprawdzeniu aktualnych zasad Roblox Creator Hub.
""";

    private static string BuildDesignBrief(RobloxGameProjectSpec spec, string genreLabel, string title) => $"""
# Brief projektu: {title}

## Kierunek

- Gatunek prototypu: **{genreLabel}**; temat: **{MarkdownText(spec.Theme)}**.
- Kierunek artystyczny: {MarkdownText(spec.ArtDirection)}
- Urządzenia: {MarkdownText(spec.TargetDevice)}
- Pętla podstawowa: {MarkdownText(spec.CoreLoop.Length == 0 ? GenreLoop(spec.Genre) : spec.CoreLoop)}
- Jednozdaniowa obietnica: „W {title} gracz szybko rozumie cel, widzi postęp i ma powód, by spróbować jeszcze raz”.
- Pierwszy prototyp ma jedną powtarzalną pętlę, jedno miejsce startu, czytelny feedback i krótki tutorial.

{(spec.OriginalPrompt.Length == 0 ? "" : "## Brief użytkownika (zapis lokalny w projekcie)\n\n" + MarkdownPrompt(spec.OriginalPrompt) + "\n")}

{(spec.GrowthIntent ? "## Cel wzrostu — hipoteza, nie obietnica\n\nBrief wspomina o popularności lub trendzie. Nie da się jej zagwarantować. Projektuj testowalny pierwszy sukces, krótkie sesje, czytelny onboarding, powód do wspólnej gry i prostą telemetrię tylko po właściwej weryfikacji prywatności. Mierz wynik z prawdziwymi testerami; nie stosuj presji zakupowej ani sztucznego FOMO.\n\n" : "")}

## Zasady designu

1. **Czytelność przed ozdobnikami:** wyraźne sylwetki, jeden dominujący akcent, kontrast tekstu, ikony z etykietą i informacja zwrotna dla każdej akcji.
2. **Płynność:** ogranicz liczbę aktywnych części i efektów; używaj zdarzeń oraz `task.wait`, nie ciasnych pętli; profiluj na słabszym urządzeniu i telefonie.
3. **Sprawiedliwy progres:** nagrody i koszty są widoczne, postęp nie wymaga płatności, a porażka nie kasuje nieproporcjonalnie dużego czasu.
4. **Dostępność:** duże przyciski, obsługa dotyku, możliwość wyłączenia intensywnych efektów i brak informacji przekazywanej wyłącznie kolorem.
5. **Unikalność:** przed produkcją opisz, czym pętla różni się od podobnych gier; nie kopiuj cudzych map, UI, nazw ani assetów.

## Pętla i zakres

{GenreLoop(spec.Genre)}

## Plan prototypu

- **Dzień 1:** graybox, ruch, kamera, pętla jednej rundy/poziomu i zapis lokalnego stanu serwera.
- **Dzień 2:** UI, dźwięki własnego autorstwa, feedback, balans pierwszych 5 minut.
- **Dzień 3:** testy z 5–10 osobami z docelowej grupy, naprawa błędów i pomiar ukończenia pierwszego celu.
- To harmonogram małego prototypu, nie gwarancja gotowej, dopracowanej lub zarabiającej gry.

## Wskaźniki do obserwacji

Zapisuj z testów dobrowolnych: czy gracz rozumie cel bez pomocy, czas do pierwszej nagrody, miejsca porzucenia, błędy, FPS na słabszym urządzeniu i opinie po sesji. Nie zbieraj danych osobowych bez uzasadnienia i właściwych zgód.
""";

    private static string BuildMonetizationBrief(RobloxGameProjectSpec spec) => $"""
# Monetyzacja — {MarkdownText(spec.DisplayName)}

Ten starter **nie** tworzy płatnych produktów ani nie publikuje gry. Przychód nie jest gwarantowany. Najpierw sprawdź, czy podstawowa darmowa pętla jest przyjemna i działa dla nowych graczy.

## Bezpieczniejsza kolejność

1. Najpierw testuj bez płatności i popraw błędy oraz onboarding.
2. Rozważ dobrowolne, jasno opisane kosmetyki (np. kolory, efekty, dekoracje) bez przewagi w rywalizacji.
3. Jeśli dodasz Game Pass lub Developer Product, serwer ma weryfikować własność/paragon; nie ufaj cenie, saldo ani wynikowi podanemu przez klienta.
4. Przed zakupem pokaż dokładną zawartość i cenę. Nie używaj fałszywych liczników, presji ani płatnych losowych nagród.
5. Przed wdrożeniem sprawdź aktualną dokumentację i zasady Roblox, regionalne wymogi prawne, ograniczenia wiekowe i ochronę danych.

## Nie dodano celowo

- ID produktu, Game Passa, konta lub grupy — muszą należeć do właściciela experience.
- Zakupów, payoutów, reklam, lootboxów ani kodu obchodzącego polityki.
- Stwierdzenia, że konkretny projekt na pewno zarobi lub stanie się popularny.

Wszelkie płatności skonfiguruj ręcznie w Creator Hub po niezależnym przeglądzie kodu serwerowego i testach.
""";

    private static string BuildPlaytestPlan(RobloxGameProjectSpec spec) => $"""
# Plan testów — {MarkdownText(spec.DisplayName)}

## Funkcjonalne

- Uruchomienie w Studio z jednym i dwoma klientami; brak błędów w Output.
- Sprawdź respawn, ponowne wejście, rozłączenie podczas zapisu i tryb bez DataStore.
- Wysyłaj błędne typy, ujemne liczby, bardzo długie ciągi i spam RemoteEvent; serwer ma odrzucać je bez awarii i bez przyznania waluty.
- Testuj szerokości ekranu 360 px i desktop; przyciski muszą pozostać osiągalne i czytelne.

## Design i wydajność

- Poproś nowych graczy, aby własnymi słowami opisali cel. Nie podpowiadaj, zanim wykonasz pierwszy pomiar.
- Zapisz czas do pierwszej satysfakcjonującej akcji, momenty dezorientacji i powód zakończenia sesji.
- Sprawdź płynność na urządzeniu o słabej wydajności oraz z ograniczeniem efektów.
- Zmieniaj jedną rzecz naraz, powtórz test i zachowaj poprzednią wersję.

## Wydanie

Nie publikuj dopóki nie ma kopii zapasowej, testów serwerowych, zgłoszeń kontaktowych, planu moderacji i weryfikacji bieżących zasad Roblox. Ten plik jest checklistą, nie certyfikatem.
""";

    private static string BuildServerScript() => """
        -- ServerScriptService/Main.server.lua
        -- Server-authoritative prototype. Never trust client-supplied currency, position, price or progress.
        local Players = game:GetService("Players")
        local ReplicatedStorage = game:GetService("ReplicatedStorage")
        local DataStoreService = game:GetService("DataStoreService")
        local Config = require(ReplicatedStorage:WaitForChild("Shared"):WaitForChild("Config"))

        local remotes = ReplicatedStorage:FindFirstChild("SentinelRemotes")
        if remotes and not remotes:IsA("Folder") then error("SentinelRemotes must be a Folder") end
        if not remotes then
            remotes = Instance.new("Folder")
            remotes.Name = "SentinelRemotes"
            remotes.Parent = ReplicatedStorage
        end

        local function getRemote(name, className)
            local value = remotes:FindFirstChild(name)
            if value and not value:IsA(className) then error(name .. " has the wrong class") end
            if not value then
                value = Instance.new(className)
                value.Name = name
                value.Parent = remotes
            end
            return value
        end

        local actionRemote = getRemote("Action", "RemoteEvent")
        local stateRemote = getRemote("State", "RemoteEvent")
        local allowedActions = {}
        for _, definition in ipairs(Config.Actions) do allowedActions[definition.Key] = true end
        local profiles = {}
        local boundPlayers = {}
        local lastActionAt = {}
        local checkpoints = {}
        local raceGates = {}
        local raceGateLastAt = {}
        local raceStart = nil
        local raceFinish = nil
        local roundStatus = "Lobby"
        local dataStore = nil
        pcall(function() dataStore = DataStoreService:GetDataStore(Config.DataStoreName) end)

        local function defaultProfile()
            return { Coins = 0, Energy = 0, Power = 1, UpgradeLevel = 0, Bank = 0, DropperLevel = 1, Stage = 0, Wins = 0, BestRaceMilliseconds = 0, RaceStage = 0, RaceStartedAt = 0 }
        end

        local function safeNumber(value, fallback, maximum)
            if typeof(value) ~= "number" or value ~= value or value == math.huge or value == -math.huge then return fallback end
            return math.clamp(math.floor(value), 0, maximum)
        end

        local function loadProfile(player)
            local profile = defaultProfile()
            if dataStore then
                local ok, saved = pcall(function() return dataStore:GetAsync("u_" .. player.UserId) end)
                if ok and typeof(saved) == "table" then
                    profile.Coins = safeNumber(saved.Coins, 0, 1000000000)
                    profile.Energy = safeNumber(saved.Energy, 0, 1000000)
                    profile.Power = math.max(1, safeNumber(saved.Power, 1, 1000000))
                    profile.UpgradeLevel = safeNumber(saved.UpgradeLevel, 0, 20)
                    profile.Bank = safeNumber(saved.Bank, 0, 1000000000)
                    profile.DropperLevel = math.max(1, safeNumber(saved.DropperLevel, 1, 1000))
                    profile.Stage = safeNumber(saved.Stage, 0, Config.CheckpointCount)
                    profile.Wins = safeNumber(saved.Wins, 0, 1000000)
                    profile.BestRaceMilliseconds = safeNumber(saved.BestRaceMilliseconds, 0, 3600000)
                end
            end
            if player.Parent ~= Players then return nil end
            profiles[player.UserId] = profile
            return profile
        end

        local function saveProfile(player)
            local profile = profiles[player.UserId]
            if not dataStore or not profile then return end
            local payload = {
                Coins = safeNumber(profile.Coins, 0, 1000000000),
                Energy = safeNumber(profile.Energy, 0, 1000000),
                Power = math.max(1, safeNumber(profile.Power, 1, 1000000)),
                UpgradeLevel = safeNumber(profile.UpgradeLevel, 0, 20),
                Bank = safeNumber(profile.Bank, 0, 1000000000),
                DropperLevel = math.max(1, safeNumber(profile.DropperLevel, 1, 1000)),
                Stage = safeNumber(profile.Stage, 0, Config.CheckpointCount),
                Wins = safeNumber(profile.Wins, 0, 1000000),
                BestRaceMilliseconds = safeNumber(profile.BestRaceMilliseconds, 0, 3600000),
            }
            local ok, err = pcall(function()
                dataStore:UpdateAsync("u_" .. player.UserId, function() return payload end)
            end)
            if not ok then warn("DataStore save failed for user " .. player.UserId .. ": " .. tostring(err)) end
        end

        local function sendState(player)
            local profile = profiles[player.UserId]
            if not profile then return end
            stateRemote:FireClient(player, {
                Coins = profile.Coins, Energy = profile.Energy, Power = profile.Power,
                UpgradeLevel = profile.UpgradeLevel, Bank = profile.Bank,
                DropperLevel = profile.DropperLevel, Stage = profile.Stage,
                Wins = profile.Wins, BestRaceMilliseconds = profile.BestRaceMilliseconds, Round = roundStatus,
            })
        end

        local function broadcastState()
            for _, player in ipairs(Players:GetPlayers()) do sendState(player) end
        end

        local function makePart(parent, name, size, position, color, material)
            local part = Instance.new("Part")
            part.Name = name
            part.Size = size
            part.Position = position
            part.Anchored = true
            part.Color = color
            part.Material = material or Enum.Material.SmoothPlastic
            part.TopSurface = Enum.SurfaceType.Smooth
            part.BottomSurface = Enum.SurfaceType.Smooth
            part.Parent = parent
            return part
        end

        local oldWorld = workspace:FindFirstChild("SentinelGeneratedWorld")
        if oldWorld then
            if oldWorld:GetAttribute("SentinelGenerated") ~= true then
                error("Refusing to replace an unowned workspace.SentinelGeneratedWorld")
            end
            oldWorld:Destroy()
        end
        local world = Instance.new("Folder")
        world.Name = "SentinelGeneratedWorld"
        world:SetAttribute("SentinelGenerated", true)
        world.Parent = workspace
        local accent = Config.Theme.Accent
        local floor = makePart(world, "Arena", Vector3.new(240, 2, 160), Vector3.new(0, -1, 0), Color3.fromRGB(29, 39, 59))
        floor.Material = Enum.Material.Slate
        if Config.Genre == "obby" then floor.CanCollide = false end
        local spawn = Instance.new("SpawnLocation")
        spawn.Name = "GeneratedSpawn"
        spawn.Size = Vector3.new(12, 1, 12)
        spawn.Position = Vector3.new(0, 1, 55)
        if Config.Genre == "obby" then
            spawn.Size = Vector3.new(18, 1, 18)
            spawn.Position = Vector3.new(-92, 4, 0)
        elseif Config.Genre == "rounds" then
            spawn.Position = Vector3.new(0, 1, 65)
        end
        spawn.Anchored = true
        spawn.Neutral = true
        spawn.Color = accent
        spawn.Material = Enum.Material.Neon
        spawn.Parent = world

        if Config.Genre == "obby" then
            local failPlane = makePart(world, "FallReset", Vector3.new(260, 2, 180), Vector3.new(0, -24, 0), Color3.fromRGB(255, 86, 107))
            failPlane.Transparency = 1
            failPlane.CanCollide = false
            failPlane.Touched:Connect(function(hit)
                local character = hit:FindFirstAncestorOfClass("Model")
                local humanoid = character and character:FindFirstChildOfClass("Humanoid")
                if humanoid and humanoid.Health > 0 then humanoid.Health = 0 end
            end)
            for index = 1, Config.CheckpointCount do
                local elevation = 4 + ((index % 3) - 1) * 1.25
                local sideOffset = index % 2 == 0 and 4 or -4
                local position = Vector3.new((index - 1) * 18 - 74, elevation, sideOffset)
                local pad = makePart(world, "Checkpoint" .. index, Vector3.new(14, 1.5, 14), position, index % 2 == 0 and accent or Config.Theme.Accent2, Enum.Material.Neon)
                checkpoints[index] = pad
                pad.Touched:Connect(function(hit)
                    local character = hit:FindFirstAncestorOfClass("Model")
                    local player = character and Players:GetPlayerFromCharacter(character)
                    local profile = player and profiles[player.UserId]
                    if not profile or index ~= profile.Stage + 1 then return end
                    profile.Stage = index
                    profile.Coins = math.min(profile.Coins + 5, 1000000000)
                    if index == Config.CheckpointCount then profile.Wins = math.min(profile.Wins + 1, 1000000) end
                    sendState(player)
                end)
            end
            makePart(world, "Finish", Vector3.new(18, 2, 18), Vector3.new(106, 4, 0), Color3.fromRGB(255, 220, 91), Enum.Material.Neon)
        elseif Config.Genre == "simulator" or Config.Genre == "custom" then
            local crystal = makePart(world, "ResourceCrystal", Vector3.new(10, 10, 10), Vector3.new(0, 6, 0), accent, Enum.Material.Neon)
            crystal.Shape = Enum.PartType.Ball
            local ring = makePart(world, "CrystalBase", Vector3.new(24, 1, 24), Vector3.new(0, 1, 0), Config.Theme.Accent2)
        elseif Config.Genre == "tycoon" then
            local plot = makePart(world, "TycoonPlot", Vector3.new(80, 1, 60), Vector3.new(0, 1, 0), Color3.fromRGB(50, 65, 86))
            local machine = makePart(world, "StarterDropper", Vector3.new(12, 14, 12), Vector3.new(-20, 9, 0), accent, Enum.Material.Metal)
            local collector = makePart(world, "BankCollector", Vector3.new(18, 1, 18), Vector3.new(18, 2, 0), Color3.fromRGB(255, 205, 84), Enum.Material.Neon)
        elseif Config.Genre == "racing" then
            spawn.Position = Vector3.new(-100, 1, 0)
            spawn.Size = Vector3.new(16, 1, 18)
            makePart(world, "TimeTrialTrack", Vector3.new(220, 1, 34), Vector3.new(0, -0.25, 0), Color3.fromRGB(37, 49, 70), Enum.Material.Slate)
            raceStart = makePart(world, "RaceStart", Vector3.new(4, 12, 34), Vector3.new(-90, 6, 0), accent, Enum.Material.Neon)
            raceStart.CanCollide = false
            raceStart.Transparency = 0.2
            raceFinish = makePart(world, "RaceFinish", Vector3.new(5, 14, 34), Vector3.new(96, 7, 0), Color3.fromRGB(255, 215, 91), Enum.Material.Neon)
            raceFinish.CanCollide = false
            for index = 1, 8 do
                local x = -70 + (index - 1) * 20
                local z = index % 2 == 0 and 9 or -9
                local gate = makePart(world, "RaceGate" .. index, Vector3.new(2, 10, 30), Vector3.new(x, 5, z), index % 2 == 0 and Config.Theme.Accent2 or accent, Enum.Material.Neon)
                gate.Transparency = 0.42
                gate.CanCollide = false
                raceGates[index] = gate
                gate.Touched:Connect(function(hit)
                    local character = hit:FindFirstAncestorOfClass("Model")
                    local player = character and Players:GetPlayerFromCharacter(character)
                    local profile = player and profiles[player.UserId]
                    local root = character and character:FindFirstChild("HumanoidRootPart")
                    if not profile or not root or profile.RaceStartedAt <= 0 or (root.Position - gate.Position).Magnitude > 32 then return end
                    local now = os.clock()
                    local previous = raceGateLastAt[player.UserId] or 0
                    if now - previous < 0.25 or profile.RaceStage ~= index - 1 then return end
                    raceGateLastAt[player.UserId] = now
                    profile.RaceStage = index
                    profile.Stage = index
                    sendState(player)
                end)
            end
            raceFinish.Touched:Connect(function(hit)
                local character = hit:FindFirstAncestorOfClass("Model")
                local player = character and Players:GetPlayerFromCharacter(character)
                local profile = player and profiles[player.UserId]
                local root = character and character:FindFirstChild("HumanoidRootPart")
                if not profile or not root or profile.RaceStartedAt <= 0 or profile.RaceStage ~= #raceGates or (root.Position - raceFinish.Position).Magnitude > 32 then return end
                local elapsed = math.floor((os.clock() - profile.RaceStartedAt) * 1000)
                profile.RaceStartedAt = 0
                profile.Wins = math.min(profile.Wins + 1, 1000000)
                if elapsed > 0 and (profile.BestRaceMilliseconds == 0 or elapsed < profile.BestRaceMilliseconds) then profile.BestRaceMilliseconds = elapsed end
                profile.Coins = math.min(profile.Coins + math.clamp(math.floor(500000 / math.max(elapsed, 1000)), 25, 500), 1000000000)
                profile.RaceStage = 0
                profile.Stage = 0
                sendState(player)
                task.spawn(saveProfile, player)
            end)
        elseif Config.Genre == "rounds" then
            local hazard = makePart(world, "RoundHazard", Vector3.new(190, 1, 3), Vector3.new(0, 3, 0), Color3.fromRGB(255, 86, 107), Enum.Material.Neon)
            hazard.Transparency = 1
            hazard.CanCollide = false
            hazard.CanTouch = true
            local eliminated = {}
            hazard.Touched:Connect(function(hit)
                if hazard.Transparency >= 1 then return end
                local character = hit:FindFirstAncestorOfClass("Model")
                local humanoid = character and character:FindFirstChildOfClass("Humanoid")
                local player = character and Players:GetPlayerFromCharacter(character)
                if humanoid and humanoid.Health > 0 then
                    if player then eliminated[player.UserId] = true end
                    humanoid.Health = 0
                end
            end)
            task.spawn(function()
                while true do
                    for seconds = Config.RoundIntermission, 1, -1 do
                        roundStatus = "Intermission · " .. seconds
                        broadcastState()
                        task.wait(1)
                    end
                    local participants = {}
                    eliminated = {}
                    for _, player in ipairs(Players:GetPlayers()) do participants[player.UserId] = true end
                    hazard.Transparency = 0.15
                    roundStatus = "Survive!"
                    broadcastState()
                    local elapsed = 0
                    for seconds = Config.RoundDuration, 1, -1 do
                        roundStatus = "Survive · " .. seconds
                        broadcastState()
                        for _ = 1, 20 do
                            hazard.CFrame = CFrame.new(0, 3, 0) * CFrame.Angles(0, elapsed * 0.7, 0)
                            elapsed += 0.05
                            task.wait(0.05)
                        end
                    end
                    hazard.Transparency = 1
                    for _, player in ipairs(Players:GetPlayers()) do
                        local profile = profiles[player.UserId]
                        local character = player.Character
                        local humanoid = character and character:FindFirstChildOfClass("Humanoid")
                        if participants[player.UserId] and not eliminated[player.UserId] and profile and humanoid and humanoid.Health > 0 then
                            profile.Coins = math.min(profile.Coins + 10, 1000000000)
                            profile.Wins = math.min(profile.Wins + 1, 1000000)
                        end
                    end
                    broadcastState()
                    roundStatus = "Round complete"
                    broadcastState()
                    task.wait(3)
                end
            end)
        end

        local function actionAllowed(player, action)
            if typeof(action) ~= "string" or #action > Config.MaxActionTextLength then return false end
            if action ~= "RequestState" and not allowedActions[action] then return false end
            local now = os.clock()
            lastActionAt[player.UserId] = lastActionAt[player.UserId] or {}
            local previous = lastActionAt[player.UserId][action] or 0
            if now - previous < Config.ActionCooldown then return false end
            lastActionAt[player.UserId][action] = now
            return true
        end

        actionRemote.OnServerEvent:Connect(function(player, action)
            if not actionAllowed(player, action) then return end
            local profile = profiles[player.UserId]
            if not profile then return end
            if action == "RequestState" then sendState(player); return end

            if Config.Genre == "simulator" or Config.Genre == "custom" then
                if action == "Gather" then
                    profile.Energy = math.min(profile.Energy + profile.Power, 1000000)
                elseif action == "Sell" and profile.Energy > 0 then
                    profile.Coins = math.min(profile.Coins + profile.Energy, 1000000000)
                    profile.Energy = 0
                elseif action == "Upgrade" then
                    local cost = Config.UpgradeBaseCost * (2 ^ math.min(profile.UpgradeLevel, 20))
                    if profile.Coins >= cost and profile.UpgradeLevel < 20 then
                        profile.Coins -= cost
                        profile.UpgradeLevel += 1
                        profile.Power = math.min(profile.Power + 1, 1000000)
                    end
                else return end
            elseif Config.Genre == "tycoon" then
                if action == "Collect" and profile.Bank > 0 then
                    profile.Coins = math.min(profile.Coins + profile.Bank, 1000000000)
                    profile.Bank = 0
                elseif action == "Upgrade" then
                    local cost = Config.UpgradeBaseCost * (2 ^ math.min(profile.DropperLevel - 1, 20))
                    if profile.Coins >= cost then
                        profile.Coins -= cost
                        profile.DropperLevel = math.min(profile.DropperLevel + 1, 1000)
                    end
                else return end
            elseif Config.Genre == "racing" and action == "JoinRace" then
                local character = player.Character
                local root = character and character:FindFirstChild("HumanoidRootPart")
                if not raceStart or not root or (root.Position - raceStart.Position).Magnitude > 24 then return end
                profile.RaceStage = 0
                profile.Stage = 0
                profile.RaceStartedAt = os.clock()
                roundStatus = "Race in progress"
            else
                return
            end
            sendState(player)
        end)

        local function placeCharacterAtProgress(player, character)
            if Config.Genre ~= "obby" then return end
            task.defer(function()
                for _ = 1, 50 do
                    if player.Parent ~= Players or not character.Parent then return end
                    local profile = profiles[player.UserId]
                    if profile then
                        local checkpoint = checkpoints[profile.Stage]
                        if checkpoint then character:PivotTo(checkpoint.CFrame + Vector3.new(0, 5, 0)) end
                        return
                    end
                    task.wait(0.1)
                end
            end)
        end

        local function bindPlayer(player)
            local userId = player.UserId
            if boundPlayers[userId] then return end
            boundPlayers[userId] = true
            player.CharacterAdded:Connect(function(character)
                placeCharacterAtProgress(player, character)
            end)
            local profile = loadProfile(player)
            if not profile or player.Parent ~= Players then
                profiles[userId] = nil
                boundPlayers[userId] = nil
                return
            end
            sendState(player)
            if player.Character then placeCharacterAtProgress(player, player.Character) end
        end

        Players.PlayerAdded:Connect(function(player) task.spawn(bindPlayer, player) end)
        for _, player in ipairs(Players:GetPlayers()) do task.spawn(bindPlayer, player) end

        if Config.Genre == "tycoon" then
            task.spawn(function()
                while true do
                    task.wait(10)
                    for _, profile in pairs(profiles) do profile.Bank = math.min(profile.Bank + profile.DropperLevel * 5, 1000000000) end
                    broadcastState()
                end
            end)
        end

        Players.PlayerRemoving:Connect(function(player)
            local userId = player.UserId
            saveProfile(player)
            profiles[userId] = nil
            boundPlayers[userId] = nil
            lastActionAt[userId] = nil
        end)

        game:BindToClose(function()
            for _, player in ipairs(Players:GetPlayers()) do saveProfile(player) end
            task.wait(2)
        end)
        """;

    private static string BuildClientScript() => """
        -- StarterPlayer/StarterPlayerScripts/Main.client.lua
        -- UI is generated locally, responds to ViewportSize and supports GuiButton.Activated on touch/mouse.
        local Players = game:GetService("Players")
        local ReplicatedStorage = game:GetService("ReplicatedStorage")
        local TweenService = game:GetService("TweenService")
        local player = Players.LocalPlayer
        local Config = require(ReplicatedStorage:WaitForChild("Shared"):WaitForChild("Config"))
        local remotes = ReplicatedStorage:WaitForChild("SentinelRemotes")
        local actionRemote = remotes:WaitForChild("Action")
        local stateRemote = remotes:WaitForChild("State")

        local gui = Instance.new("ScreenGui")
        gui.Name = "GeneratedGameUI"
        gui.ResetOnSpawn = false
        gui.IgnoreGuiInset = false
        gui.Parent = player:WaitForChild("PlayerGui")

        local scale = Instance.new("UIScale")
        scale.Parent = gui
        local viewportConnection = nil
        local function resize()
            local camera = workspace.CurrentCamera
            if camera then scale.Scale = math.clamp(math.min(camera.ViewportSize.X / 1100, camera.ViewportSize.Y / 760), 0.72, 1) end
        end
        local function bindCamera()
            if viewportConnection then viewportConnection:Disconnect() end
            local camera = workspace.CurrentCamera
            viewportConnection = camera and camera:GetPropertyChangedSignal("ViewportSize"):Connect(resize) or nil
            resize()
        end
        workspace:GetPropertyChangedSignal("CurrentCamera"):Connect(bindCamera)
        bindCamera()

        local panel = Instance.new("Frame")
        panel.Name = "HudPanel"
        panel.AnchorPoint = Vector2.new(0, 1)
        panel.Position = UDim2.new(0, 18, 1, -18)
        panel.Size = UDim2.new(0, 360, 0, 218)
        panel.BackgroundColor3 = Config.Theme.Panel
        panel.BackgroundTransparency = 0.06
        panel.Parent = gui
        Instance.new("UICorner", panel).CornerRadius = UDim.new(0, 18)
        local stroke = Instance.new("UIStroke", panel)
        stroke.Color = Config.Theme.Accent
        stroke.Thickness = 1.5
        stroke.Transparency = 0.15
        local gradient = Instance.new("UIGradient", panel)
        gradient.Color = ColorSequence.new(Config.Theme.Panel, Config.Theme.Background)
        gradient.Rotation = 90

        local padding = Instance.new("UIPadding", panel)
        padding.PaddingTop = UDim.new(0, 14)
        padding.PaddingBottom = UDim.new(0, 12)
        padding.PaddingLeft = UDim.new(0, 16)
        padding.PaddingRight = UDim.new(0, 16)
        local list = Instance.new("UIListLayout", panel)
        list.Padding = UDim.new(0, 8)
        list.SortOrder = Enum.SortOrder.LayoutOrder

        local function label(name, text, size, color, order)
            local item = Instance.new("TextLabel")
            item.Name = name
            item.Size = UDim2.new(1, 0, 0, size + 4)
            item.BackgroundTransparency = 1
            item.Font = Enum.Font.GothamMedium
            item.Text = text
            item.TextSize = size
            item.TextColor3 = color
            item.TextXAlignment = Enum.TextXAlignment.Left
            item.TextWrapped = true
            item.LayoutOrder = order
            item.Parent = panel
            return item
        end

        local title = label("Title", Config.Name, 22, Config.Theme.Text, 1)
        title.Font = Enum.Font.GothamBold
        title.TextWrapped = false
        title.TextTruncate = Enum.TextTruncate.AtEnd
        if #Config.Name > 22 then title.TextSize = 18 end
        local stats = label("Stats", "Ładowanie danych serwera…", 14, Config.Theme.Text, 2)
        stats.Size = UDim2.new(1, 0, 0, 36)
        stats.TextYAlignment = Enum.TextYAlignment.Top
        local round = label("Round", Config.Genre, 13, Config.Theme.Accent, 3)
        local buttons = Instance.new("Frame")
        buttons.Name = "Actions"
        buttons.Size = UDim2.new(1, 0, 0, 44)
        buttons.BackgroundTransparency = 1
        buttons.LayoutOrder = 4
        buttons.Parent = panel
        local grid = Instance.new("UIGridLayout", buttons)
        grid.CellPadding = UDim2.new(0, 8, 0, 6)
        grid.CellSize = UDim2.new(1 / math.max(1, #Config.Actions), -8, 0, 42)
        grid.SortOrder = Enum.SortOrder.LayoutOrder

        for index, definition in ipairs(Config.Actions) do
            local button = Instance.new("TextButton")
            button.Name = definition.Key
            button.LayoutOrder = index
            button.Text = definition.Label
            button.TextSize = 15
            button.Font = Enum.Font.GothamBold
            button.TextColor3 = Config.Theme.Background
            button.BackgroundColor3 = Config.Theme.Accent
            button.AutoButtonColor = false
            button.Parent = buttons
            Instance.new("UICorner", button).CornerRadius = UDim.new(0, 12)
            button.Activated:Connect(function() actionRemote:FireServer(definition.Key) end)
            button.MouseEnter:Connect(function()
                TweenService:Create(button, TweenInfo.new(0.12), {BackgroundColor3 = Config.Theme.Text}):Play()
            end)
            button.MouseLeave:Connect(function()
                TweenService:Create(button, TweenInfo.new(0.12), {BackgroundColor3 = Config.Theme.Accent}):Play()
            end)
        end

        local statLabels = {
            Coins = "Coins", Energy = "Energy", Power = "Power", UpgradeLevel = "Upgrades",
            Bank = "Bank", DropperLevel = "Machines", Stage = "Stage", Wins = "Wins",
            BestRaceMilliseconds = "Best time (ms)",
        }
        local visibleStats = {
            simulator = {"Coins", "Energy", "Power", "UpgradeLevel"},
            custom = {"Coins", "Energy", "Power", "UpgradeLevel"},
            obby = {"Coins", "Stage", "Wins"},
            tycoon = {"Coins", "Bank", "DropperLevel"},
            rounds = {"Coins", "Wins"},
            racing = {"Coins", "Wins", "Stage", "BestRaceMilliseconds"},
        }
        local function displayState(data)
            local parts = {}
            for _, key in ipairs(visibleStats[Config.Genre] or {"Coins"}) do
                if data[key] ~= nil then table.insert(parts, statLabels[key] .. ": " .. tostring(data[key])) end
            end
            stats.Text = #parts > 0 and table.concat(parts, "   ·   ") or "Oczekiwanie na dane serwera…"
            round.Text = tostring(data.Round or Config.Genre)
        end
        stateRemote.OnClientEvent:Connect(displayState)
        actionRemote:FireServer("RequestState")
        """;

    private static string BuildBlenderScript(RobloxGameProjectSpec spec, (int r, int g, int b) accent)
    {
        string safeTitle = spec.DisplayName.Replace("\r", " ").Replace("\n", " ").Replace("'", "\\'");
        return $$"""
            # Optional Blender blockout for {{safeTitle}}.
            # Run in a new/saved Blender copy. This script does not clear the scene or export/upload assets.
            import bpy
            import math
            from mathutils import Vector

            COLLECTION_NAME = "Sentinel Generated Blockout"
            # Blender adds a numeric suffix if this name already exists; never replace the user's collection.
            collection = bpy.data.collections.new(COLLECTION_NAME)
            bpy.context.scene.collection.children.link(collection)

            def material(name, color, metallic=0.0, roughness=0.45):
                mat = bpy.data.materials.new(name=name)
                mat.diffuse_color = (*color, 1.0)
                mat.use_nodes = True
                bsdf = mat.node_tree.nodes.get("Principled BSDF")
                if bsdf:
                    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
                    bsdf.inputs["Metallic"].default_value = metallic
                    bsdf.inputs["Roughness"].default_value = roughness
                return mat

            accent = material("Sentinel Accent", ({{accent.r}} / 255, {{accent.g}} / 255, {{accent.b}} / 255), 0.25, 0.28)
            deep = material("Midnight Blue", (0.055, 0.09, 0.16), 0.1, 0.65)
            highlight = material("Soft Highlight", (0.85, 0.92, 1.0), 0.0, 0.32)

            def move_to_generated(obj):
                for owner in list(obj.users_collection):
                    owner.objects.unlink(obj)
                collection.objects.link(obj)
                return obj

            def cube(name, location, scale, mat, bevel=0.15):
                bpy.ops.mesh.primitive_cube_add(size=1, location=location)
                obj = move_to_generated(bpy.context.object)
                obj.name = name
                obj.dimensions = scale
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
                obj.data.materials.append(mat)
                if bevel > 0:
                    modifier = obj.modifiers.new("Soft bevel", "BEVEL")
                    modifier.width = bevel
                    modifier.segments = 3
                    obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
                return obj

            def ico(name, location, radius, mat):
                bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=radius, location=location)
                obj = move_to_generated(bpy.context.object)
                obj.name = name
                obj.data.materials.append(mat)
                return obj

            cube("Floating stage", (0, 0, -0.7), (24, 18, 1), deep, 0.4)
            cube("Spawn platform", (-7, -3, 0.15), (6, 5, 0.6), accent, 0.35)
            cube("Progress platform 1", (0, 0, 0.35), (5, 5, 0.8), highlight, 0.4)
            cube("Progress platform 2", (7, 3, 0.65), (5, 5, 1.2), accent, 0.4)
            ico("Stylized focal crystal", (0, 0, 2.0), 1.35, accent)
            for i in range(8):
                angle = math.tau * i / 8
                ico("Orbit accent %02d" % i, (math.cos(angle) * 3.2, math.sin(angle) * 3.2, 1.1), 0.22, highlight)

            camera_data = bpy.data.cameras.new("Sentinel Blockout Camera")
            camera = bpy.data.objects.new("Sentinel Blockout Camera", camera_data)
            collection.objects.link(camera)
            camera.location = (22, -28, 23)
            direction = Vector((0, 0, 0)) - camera.location
            camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
            camera_data.lens = 48
            # Keep the scene's active camera unchanged; assign this camera manually if desired.

            light_data = bpy.data.lights.new("Sentinel Softbox", type="AREA")
            light = bpy.data.objects.new("Sentinel Softbox", light_data)
            collection.objects.link(light)
            light.location = (2, -4, 13)
            light_data.energy = 1800
            light_data.shape = "DISK"
            light_data.size = 10
            print("Created a non-destructive blockout collection for {{safeTitle}}. Review and save manually.")
            """;
    }

    private static string GenreLoop(string genre) => genre switch
    {
        "simulator" => "Gracz zbiera energię → sprzedaje ją → kupuje wzrost mocy → zbiera szybciej. Starter ma jeden hub i nie zawiera jeszcze nowych światów, zadań ani docelowego balansu.",
        "obby" => "Gracz uczy się jednej przeszkody → dociera do checkpointu → widzi postęp → próbuje kolejnego odcinka. Starter buduje serię checkpointów i nagradza tylko postęp zatwierdzony przez serwer.",
        "tycoon" => "Maszyna generuje bank → gracz odbiera środki → kupuje ulepszenie → rośnie tempo produkcji. Starter zawiera prostą ekonomię, bez rozbudowanego systemu działek i wielu graczy.",
        "rounds" => "Lobby → krótka runda przetrwania → nagroda za przeżycie → przerwa i następna runda. Starter używa prostego, widocznego zagrożenia; potrzebuje map, wariantów i testów balansu.",
        "racing" => "Start próby czasowej → kolejne bramki w poprawnej kolejności → meta → najlepszy czas i odblokowanie następnego wyzwania. To prototyp biegu po trasie, nie fizyka samochodu.",
        _ => "Wybierz jedną czynność gracza → daj natychmiastowy, czytelny feedback → pokaż postęp → odblokuj kolejny mały cel. Starter używa bezpiecznej pętli zbierania/ulepszania jako miejsca na własny pomysł."
    };

    private static string GenreLabel(string genre) => genre switch
    {
        "simulator" => "simulator", "obby" => "obby / parkour", "tycoon" => "tycoon", "rounds" => "rundy survival",
        "racing" => "wyścig na czas pieszo", _ => "własny prototyp / sandbox"
    };

    private static string CleanDisplayName(string raw, string genre)
    {
        string value = Regex.Replace(raw ?? "", @"[<>:""/\\|?*\p{Cc}]", " ");
        value = Regex.Replace(value, @"\s+", " ").Trim(' ', '.', '-');
        if (value.Length == 0) value = genre switch
        {
            "simulator" => "Crystal Simulator", "obby" => "Skyline Obby", "tycoon" => "Cozy Workshop Tycoon",
            "rounds" => "Last Light Rounds", "racing" => "Velocity Trial", _ => "New Roblox Prototype"
        };
        return value.Length <= MaximumProjectNameLength ? value : value[..MaximumProjectNameLength].TrimEnd();
    }

    private static string Slugify(string text)
    {
        string normalized = ConversationMemoryService.Normalize(text).ToLowerInvariant();
        string slug = Regex.Replace(normalized, @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length <= 32 ? slug : slug[..32].TrimEnd('-');
    }

    private static string LuaQuote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + "\"";

    public static string LuaString(string text) => LuaQuote(text ?? "");
    public static string SlugifyName(string text) => Slugify(text ?? "");
    public static string CleanNameForIntent(string text) => CleanDisplayName(text ?? "", "custom");
    public static string GetLoopFor(string genre, string theme) => GenreLoop(genre ?? "custom");

    private static string MarkdownText(string text) => text.Replace("[", "\\[").Replace("]", "\\]").Replace("\r", " ").Replace("\n", " ");

    private static string MarkdownPrompt(string prompt)
    {
        string normalized = (prompt ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length > 6000) normalized = normalized[..6000];
        return string.Join("\n", normalized.Split('\n').Select(line =>
            "> " + line.Replace("`", "\\`", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)));
    }

    private static bool HasForbiddenControls(string value) => value.Any(character =>
        char.IsControl(character) && character is not ('\r' or '\n' or '\t'));

    private static async Task<string> WriteVerifiedTextAsync(string path, string content, CancellationToken token)
    {
        byte[] expected = new UTF8Encoding(false).GetBytes(content);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            await stream.WriteAsync(expected, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        byte[] actual = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(expected)) throw new IOException("Odczyt kontrolny nie zgadza się dla pliku " + Path.GetFileName(path) + ".");
        return Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant();
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static bool ValidSpec(RobloxGameProjectSpec spec)
    {
        if (spec is null || !(spec.Genre is "simulator" or "obby" or "tycoon" or "rounds" or "racing" or "custom")) return false;
        string displayName = spec.DisplayName ?? "";
        string slug = spec.Slug ?? "";
        string[] themes = ["adventure", "space", "underwater", "fantasy", "cyberpunk", "cozy", "nature", "city", "horror", "winter"];
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumProjectNameLength ||
            !string.Equals(displayName, CleanDisplayName(displayName, spec.Genre), StringComparison.Ordinal) ||
            (spec.OriginalPrompt?.Length ?? 0) > 6000 || (spec.ArtDirection?.Length ?? 0) > 240 || (spec.CoreLoop?.Length ?? 0) > 420 || (spec.TargetDevice?.Length ?? 0) > 100 ||
            !themes.Contains(spec.Theme ?? "", StringComparer.Ordinal) ||
            new[] { spec.OriginalPrompt ?? "", spec.ArtDirection ?? "", spec.CoreLoop ?? "", spec.TargetDevice ?? "" }.Any(HasForbiddenControls)) return false;
        return slug.Length <= 32 && string.Equals(slug, Slugify(displayName), StringComparison.Ordinal) &&
            Regex.IsMatch(slug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    }

    private static bool SafeEntryName(string name) => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("/", StringComparison.Ordinal) &&
        !name.Contains('\\') && name.Split('/').All(part => part.Length > 0 && part is not "." and not "..");

    private static async Task<(int Count, string Sha256)> VerifyArchiveAsync(string path, Dictionary<string, string> expected, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        int entryCount = archive.Entries.Count;
        if (entryCount != expected.Count) throw new InvalidDataException("Liczba wpisów ZIP nie zgadza się z pakietem.");
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(entry.FullName, out string? content)) throw new InvalidDataException("Nieoczekiwany wpis ZIP: " + entry.FullName);
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string actual = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            if (!string.Equals(actual, content, StringComparison.Ordinal)) throw new InvalidDataException("Odczyt kontrolny nie zgadza się dla " + entry.FullName);
        }
        stream.Position = 0;
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return (entryCount, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }

    private const string Usage = "Nie musisz znać żadnej składni. Napisz naturalnie np. „Stwórz mi grę na Robloxie: kooperacyjny wyścig przez neonowy kosmos, płynny na telefonie; nazwij ją Starfall Rally”. Sentinel przygotuje lokalne źródła Luau, tekstowy place .rbxlx, plan 3D i briefy; Studio/Blender mogą zostać otwarte tylko jeśli są zainstalowane. Nie publikuję gry ani nie gwarantuję popularności.";
}
