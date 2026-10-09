using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Core;
using SentinelX.Services.Creative;
using SentinelX.Services.History;
using SentinelX.Services.Intent;
using SentinelX.Services.Monitoring;
namespace SentinelX.Tests;

/// <summary>0.96 · KUŹNIA: the decision preview („jak to rozumiem: …”) and the task search, both through the real
/// IntentRouter → CommandRouter pipeline. The preview must never execute, write or reach the model.</summary>
internal static class RoutingRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static int QrFiles() => Directory.Exists(Path.Combine(AppPaths.Root, "Qr")) ? Directory.GetFiles(Path.Combine(AppPaths.Root, "Qr")).Length : 0;

    private static Dictionary<string, string> ReadProjectArchive(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files.Add(entry.FullName, reader.ReadToEnd());
        }
        return files;
    }

    private sealed class CapturingAiHandler : HttpMessageHandler
    {
        public string UserPrompt { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/tags")
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"name\":\"qwen3:4b\"}]}", Encoding.UTF8, "application/json") };
            if (path == "/api/chat")
            {
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                UserPrompt = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() ?? "";
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"Odpowiedź testowa.\"}}", Encoding.UTF8, "application/json") };
            }
            return new(HttpStatusCode.NotFound);
        }
    }

    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var history = new ActionHistoryService(root);
        var memory = new ConversationMemoryService(Path.Combine(root, "Memory"));
        var toolbox = new SentinelToolboxService(history: history, permissions: new PermissionCenterService(), memory: memory, historyExport: new Services.History.HistoryExportService(history, root));
        using var monitor = new SystemMonitor();
        using var localAi = new LocalAiService(new GamingModeService());
        var tasks = new TaskService(Path.Combine(root, "tasks"));
        var modelGenerator = new ObjModelGenerator(Path.Combine(root, "CreatedModels"));
        string gameTestRoot = Path.Combine(root, "RobloxGameTests-" + Guid.NewGuid().ToString("N"));
        var gameGenerator = new RobloxGameProjectGenerator(Path.Combine(gameTestRoot, "CreatedGames"));
        var commandRouter = new CommandRouter(monitor, new SystemInfoService(), localAi, memory, tasks: tasks,
            modelGenerator: modelGenerator, gameProjectGenerator: gameGenerator);
        var router = new IntentRouter(toolbox, new FileWorkspaceService(root, root, history), commandRouter, new ReadOnlyCommandService(monitor, history));

        // --- decision preview: what Sentinel would do, without doing it ---
        string tool = DecisionPreview.TryExplain("jak to rozumiem: kwota slownie: 1234,56") ?? throw new InvalidOperationException("The preview trigger was not recognised.");
        Check(tool.Contains("Narzędzie „Kwota słownie”") && tool.Contains("To tylko podgląd"), "A tool command must be described as that tool: " + tool);
        Check(!tool.Contains("złote 56 groszy"), "The preview must not run the tool (no result in the explanation): " + tool);
        string abbreviation = DecisionPreview.TryExplain("Jak to rozumiem: dk")!;
        Check(abbreviation.Contains("diagnostyka komputera") && abbreviation.Contains("Literówka lub skrót"), "An abbreviation must be shown as its expansion: " + abbreviation);
        string missing = DecisionPreview.TryExplain("jak to rozumiem: unix:")!;
        Check(missing.Contains("Brakuje argumentu") && missing.Contains("unix: 1700000000"), "A tool without its argument must show an example: " + missing);
        string risky = DecisionPreview.TryExplain("jak to rozumiem: usun duplikaty c:\\dane")!;
        Check(risky.Contains("zgody") && risky.Contains("niczego nie wykonałem"), "A destructive command must be described as needing consent: " + risky);
        string question = DecisionPreview.TryExplain("jak to rozumiem: opowiedz mi proszę coś ciekawego o historii Krakowa")!;
        Check(question.Contains("zwykłe pytanie") && question.Contains("modelu AI"), "Free text must be described as a model question: " + question);
        string nested = DecisionPreview.TryExplain("jak to rozumiem: jak to rozumiem: lotto")!;
        Check(nested.Contains("nie zagnieżdżam"), "Previews must not nest: " + nested);
        Check(DecisionPreview.TryExplain("jak to rozumiem")!.Contains("Napisz"), "A bare trigger must show how to use it.");
        Check(DecisionPreview.TryExplain("Jak rozumiesz słowo ambicja?") == null, "An ordinary question must not be hijacked by the preview.");
        Check(DecisionPreview.TryExplain("ile mam ramu") == null, "A normal command must not be taken for a preview request.");

        // The catalogue and the preview agree: every entry resolves to itself (no tool shadows another one).
        foreach (var entry in ToolCatalog.Entries)
        {
            string command = ConversationMemoryService.Normalize(ToolCatalog.BuildCommand(entry, entry.Example)).TrimEnd('?', '!', '.', ' ');
            string? resolved = DecisionPreview.FindTool(command)?.Id;
            Check(resolved == entry.Id, "Catalogue entry „" + entry.Id + "” resolved to „" + resolved + "” for: " + command);
        }
        // Every new Forge tool is really handled by the toolbox (the page may never offer what the router cannot do).
        foreach (var entry in ToolCatalog.Entries.Where(x => x.Category == "Kuźnia 0.96"))
        {
            string command = ToolCatalog.BuildCommand(entry, entry.Example);
            string? answer = UtilityToolbox.Process(command, ConversationMemoryService.Normalize(command).TrimEnd('?', '!', '.', ' '));
            Check(answer != null && answer.Length > 0, "The Forge tool „" + entry.Id + "” is in the catalogue but the router does not handle: " + command);
        }
        foreach (var entry in ToolCatalog.Entries.Where(x => x.Category == "Roblox i modele 3D" && x.Id != "obj-model" && x.Id != "roblox-game-project"))
        {
            string command = ToolCatalog.BuildCommand(entry, entry.Example);
            string? answer = UtilityToolbox.Process(command, ConversationMemoryService.Normalize(command).TrimEnd('?', '!', '.', ' '));
            Check(answer != null && answer.Length > 0, "The Roblox/Luau tool „" + entry.Id + "” is catalogued but not routed: " + command);
        }
        string unsafeLuau = RobloxLuauTools.Audit("loadstring(payload)\nremote.OnServerEvent:Connect(function(player, amount) end)");
        Check(unsafeLuau.Contains("WYSOKIE RYZYKO") && unsafeLuau.Contains("GRANICA ZAUFANIA"), "The Luau audit must flag dynamic execution and untrusted remote arguments: " + unsafeLuau);
        string yieldingLoop = RobloxLuauTools.Audit("while true do task.wait(1) end");
        Check(!yieldingLoop.Contains("MOŻLIWA BLOKADA"), "A yielding task.wait loop must not be reported as a tight loop: " + yieldingLoop);
        string literalOnly = RobloxLuauTools.Audit("local example = [[loadstring(payload)]] -- remote.OnServerEvent:Connect()\n--[=[ while true do DataStoreService end ]=]");
        Check(!literalOnly.Contains("WYSOKIE RYZYKO") && !literalOnly.Contains("GRANICA ZAUFANIA") && !literalOnly.Contains("MOŻLIWA BLOKADA") && !literalOnly.Contains("ODPORNOŚĆ"),
            "The Luau audit must ignore patterns found only in comments and long-bracket string literals: " + literalOnly);
        string actualAfterComment = RobloxLuauTools.Audit("--[=[\nloadstring(payload)\n]=]\nloadstring(payload)");
        Check(actualAfterComment.Contains("WYSOKIE RYZYKO (linia 4)"), "Masking comments must preserve original finding line numbers: " + actualAfterComment);
        string templateInterpolation = RobloxLuauTools.Audit("local result = `value {loadstring(payload)}`");
        Check(templateInterpolation.Contains("WYSOKIE RYZYKO"), "Template-string interpolations must be scanned as code: " + templateInterpolation);
        string templateRawComment = RobloxLuauTools.Audit("local result = `-- raw loadstring(hidden) {safeValue}`");
        Check(!templateRawComment.Contains("WYSOKIE RYZYKO"), "Comment-like text in template literals must not hide or invent code findings: " + templateRawComment);
        string interpolationAfterRawComment = RobloxLuauTools.Audit("local result = `-- raw text {loadstring(payload)}`");
        Check(interpolationAfterRawComment.Contains("WYSOKIE RYZYKO"), "Raw template text must not consume a later executable interpolation: " + interpolationAfterRawComment);
        Check(RobloxLuauTools.GenerateTemplate("checkpoint").Contains("local debounce = {}"), "The checkpoint sample must demonstrate a Touched debounce.");
        string remoteTemplate = RobloxLuauTools.GenerateTemplate("remote");
        Check(remoteTemplate.Contains("typeof(itemKey)") && remoteTemplate.Contains("product.Price") && remoteTemplate.Contains("FireServer"),
            "The RemoteEvent example must validate a key and keep prices on the server: " + remoteTemplate);
        string modelPreview = DecisionPreview.TryExplain("jak to rozumiem: model 3d: cube 2 2 2")!;
        Check(modelPreview.Contains("Generuj siatkę 3D OBJ") && modelPreview.Contains("podgląd niczego nie zapisuje"), "An OBJ decision preview must describe the file side effect without generating it: " + modelPreview);
        string bareModelHelp = await commandRouter.ProcessAsync("zbuduj model 3d", default);
        Check(bareModelHelp.Contains("model 3d: cube") && !Directory.Exists(modelGenerator.OutputDirectory), "A bare 3D request must show usage and have no side effect: " + bareModelHelp);

        // Deterministic OBJ generation for every supported primitive; verify indices, finite vertices, file hash and safe parameter bounds.
        foreach (string command in new[]
        {
            "model 3d: cube 2 1 3", "model 3d: plane 4 6", "model 3d: sphere 1 12 6",
            "model 3d: cylinder 1 2 12", "model 3d: cone 1 2 12"
        })
        {
            ObjModelResult generated = await modelGenerator.GenerateFromCommandAsync(command);
            Check(generated.Success && File.Exists(generated.Path), "Supported 3D command did not produce a verified OBJ: " + generated.Message);
            string source = File.ReadAllText(generated.Path, Encoding.UTF8);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            Check(hash == generated.Sha256 && generated.Sha256.Length == 64, "OBJ read-back SHA-256 must match.");
            string[] lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int vertexCount = lines.Count(line => line.StartsWith("v ", StringComparison.Ordinal));
            int faceCount = lines.Count(line => line.StartsWith("f ", StringComparison.Ordinal));
            Check(vertexCount == generated.Vertices && faceCount == generated.Faces, "OBJ rows must agree with reported mesh counts.");
            foreach (string line in lines.Where(line => line.StartsWith("v ", StringComparison.Ordinal)))
            {
                double[] coordinates = line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                Check(coordinates.Length == 3 && coordinates.All(double.IsFinite), "OBJ vertices must be finite triples: " + line);
            }
            foreach (string line in lines.Where(line => line.StartsWith("f ", StringComparison.Ordinal)))
                foreach (string indexText in line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int index = int.Parse(indexText, System.Globalization.CultureInfo.InvariantCulture);
                    Check(index >= 1 && index <= vertexCount, "OBJ face index is outside the vertex table: " + line);
                }
        }
        string routedModel;
        ActionHistoryEntry modelProof;
        using (var modelCapture = ActionEvidenceCapture.Begin("model-routing-test"))
        {
            routedModel = await router.ProcessAsync("model 3d: cube 1", default);
            modelProof = modelCapture.Snapshot().SingleOrDefault() ?? throw new InvalidOperationException("OBJ generation must publish action evidence.");
        }
        Check(routedModel.Contains("VERIFIED") && routedModel.Contains("CreatedModels"), "A model command must reach the generator through IntentRouter: " + routedModel);
        Check(modelProof.Status == "VERIFIED" && modelProof.ActionType == "MODEL_OBJ" && modelProof.Evidence.Contains("SHA-256"), "The OBJ action proof must report read-back evidence and a digest.");
        ObjModelResult rejectedModel = await modelGenerator.GenerateFromCommandAsync("model 3d: cube 2 2 2 ../../escape");
        Check(!rejectedModel.Success && !rejectedModel.Path.Contains("escape", StringComparison.Ordinal), "OBJ arguments must not accept an output path or path traversal.");

        // --- actionable abuse is stopped before toolbox/router side effects ---
        string refusedRoute = await router.ProcessAsync("Stwórz ransomware", default);
        ToolboxCommandResult refusedToolbox = await toolbox.ProcessAsync("Stwórz ransomware", default);
        Check(refusedRoute.Contains("Nie pomogę") && refusedToolbox.Handled && refusedToolbox.Response.Contains("Nie pomogę") &&
            !Directory.Exists(gameGenerator.OutputDirectory),
            "The front-door policy must refuse an illegal tool request before either router or toolbox side effects: " + refusedRoute);

        // --- Roblox game starter: route natural-language briefs into a safe Rojo/Studio/Blender package ---
        string bareGameHelp = await commandRouter.ProcessAsync("roblox gra", default);
        Check(bareGameHelp.Contains("Nie musisz znać żadnej składni") && !Directory.Exists(gameGenerator.OutputDirectory),
            "A bare Roblox request must show natural-language help without creating files: " + bareGameHelp);
        string naturalPreview = DecisionPreview.TryExplain("jak to rozumiem: stwórz grę na Robloxie: wyścig przez neonowy kosmos")!;
        Check(naturalPreview.Contains("Twórz prototyp gry Roblox") && naturalPreview.Contains("podgląd niczego nie zapisuje") &&
            !Directory.Exists(gameGenerator.OutputDirectory), "The natural-language game preview must describe, but never create, project files: " + naturalPreview);
        Check(!RobloxGameRequestAnalyzer.TryAnalyze("Jak stworzyć grę na Robloxie?", out _) &&
            !RobloxGameRequestAnalyzer.TryAnalyze("Opowiedz mi o popularnych grach Roblox", out _) &&
            !RobloxGameRequestAnalyzer.TryAnalyze("I don't want to create a Roblox game yet; explain how the editor works.", out _) &&
            !RobloxGameRequestAnalyzer.TryAnalyze("Nie chcę, żebyś mi stworzył grę na Robloxie; tylko wyjaśnij, jak działa Studio.", out _),
            "Questions, discussions and negated requests must not be mistaken for a command to generate a project.");
        const string naturalGameRequest = "Stwórz mi grę na Robloxie: symulator kryształów w neonowym kosmosie, bardzo płynny na telefonie i PC, z onboardingiem i krótkim celem. Nazwij ją Crystal Miner i zaprojektuj tak, by można było uczciwie testować jej popularność.";
        Check(RobloxGameRequestAnalyzer.TryAnalyze("Stwórz mi grę na Robloxie", out var minimalGameRequest) &&
            minimalGameRequest.Genre == "custom" && minimalGameRequest.DisplayName.Length > 1,
            "The user's plain example must trigger creation without a colon, title, or command prefix: " + minimalGameRequest);
        Check(RobloxGameRequestAnalyzer.TryAnalyze(naturalGameRequest, out var analyzedGame) &&
            analyzedGame.Genre == "simulator" && analyzedGame.Theme == "space" && analyzedGame.DisplayName == "Crystal Miner" &&
            analyzedGame.TargetDevice.Contains("mobile-first", StringComparison.Ordinal) && analyzedGame.OriginalPrompt == naturalGameRequest,
            "Ordinary Polish game briefs must be classified and preserved without a magic command: " + analyzedGame);
        Check(RobloxGameRequestAnalyzer.TryAnalyze("Create a Roblox racing game, mobile first, name it Starfall Rally", out var englishGame) &&
            englishGame.Genre == "racing" && englishGame.DisplayName == "Starfall Rally",
            "English Roblox game requests must use the same natural-intent pipeline: " + englishGame);

        Check(AuthorizedUsePolicy.TryRefuse("Dodaj możliwość robienia nie legalnych rzeczy.", out string illegalCapabilityRefusal) &&
            AuthorizedUsePolicy.TryRefuse("Ulepsz Sentinela i dodaj żeby mógł robić nie legalne rzeczy.", out _) &&
            illegalCapabilityRefusal.Contains("legalnym audycie"),
            "An explicit request to enable illegal capabilities must be refused with lawful alternatives.");
        Check(AuthorizedUsePolicy.TryRefuse("Napisz skrypt, który wykradnie hasła z cudzego komputera.", out _) &&
            AuthorizedUsePolicy.TryRefuse("Zatrzyj logi po włamaniu.", out _) &&
            AuthorizedUsePolicy.TryRefuse("Stwórz ransomware.", out _),
            "Credential theft, concealment and malware creation must be blocked before tools run.");
        Check(!AuthorizedUsePolicy.TryRefuse("Wyjaśnij, jak ransomware działa i jak chronić przed nim własny komputer.", out _) &&
            !AuthorizedUsePolicy.TryRefuse("Wygeneruj detektor ransomware do testów w moim środowisku.", out _),
            "Educational and defensive security requests must remain available.");

        const string xmlSensitiveLua = "print(\"XML & text ]]> survives\")\n";
        string xmlPlace = RobloxPlaceFileBuilder.Build("Safe Place", xmlSensitiveLua);
        Check(RobloxPlaceFileBuilder.TryReadServerSource(xmlPlace, out string escapedSource, out string xmlError) && escapedSource == xmlSensitiveLua,
            "The Roblox XML builder must round-trip XML-sensitive script text, including the CDATA terminator: " + xmlError);
        Check(!RobloxPlaceFileBuilder.TryReadServerSource("<roblox version=\"5\" />", out _, out _),
            "The place reader must reject unsupported XML document versions.");
        var gameCommands = new (string Command, string Genre, string Slug)[]
        {
            ("roblox gra: simulator Crystal Miner", "simulator", "crystal-miner"),
            ("roblox gra: obby Neon Skyline", "obby", "neon-skyline"),
            ("roblox gra: tycoon Cozy Workshop", "tycoon", "cozy-workshop"),
            ("roblox gra: rounds Night Shift", "rounds", "night-shift"),
            ("roblox gra: custom Moon Garden", "custom", "moon-garden"),
            ("roblox gra: racing Velocity Trial", "racing", "velocity-trial")
        };
        foreach (var item in gameCommands)
        {
            Check(RobloxGameProjectGenerator.TryParseCommand(item.Command, out var parsed, out string parseError) &&
                parsed.Genre == item.Genre && parsed.Slug == item.Slug,
                "The game command parser must accept the " + item.Genre + " profile: " + parseError);
        }
        Check(RobloxGameProjectGenerator.TryParseCommand("roblox gra: simulator ../Escape", out var sanitizedName, out _) &&
            sanitizedName.Slug == "escape", "Project names must be sanitized into a slug, never used as a filesystem path.");
        Check(RobloxGameProjectGenerator.TryParseCommand("roblox gra: simulator Safe\nTitle", out var newlineSafeName, out _) &&
            newlineSafeName.DisplayName == "Safe Title", "Control characters and newlines must be normalized before they enter project files.");
        var invalidGame = await gameGenerator.GenerateAsync(new RobloxGameProjectSpec("custom", "Moon Garden", "../escape"));
        Check(!invalidGame.Success && !Directory.Exists(gameGenerator.OutputDirectory), "Invalid project slugs must fail before creating files.");

        string routedGame;
        ActionHistoryEntry gameProof;
        using (var gameCapture = ActionEvidenceCapture.Begin("roblox-game-routing-test"))
        {
            routedGame = await router.ProcessAsync(naturalGameRequest, default);
            gameProof = gameCapture.Snapshot().SingleOrDefault() ?? throw new InvalidOperationException("Natural-language Roblox generation must record action evidence.");
        }
        Check(routedGame.Contains("VERIFIED") && routedGame.Contains(".zip") && routedGame.Contains(".rbxlx") && routedGame.Contains("Nie publikuję gry"),
            "The real IntentRouter must create local source, Lua and place files from an ordinary sentence: " + routedGame);
        Check(gameProof.Status == "VERIFIED" && gameProof.ActionType == "ROBLOX_PROJECT_ZIP" && gameProof.Evidence.Contains("SHA-256"),
            "Roblox game output evidence must contain a read-back result and digest.");
        string[] gameArchives = Directory.GetFiles(gameGenerator.OutputDirectory, "*.zip");
        Check(gameArchives.Length == 1, "Only the explicitly requested Roblox archive should exist at this point.");
        var simulatorFiles = ReadProjectArchive(gameArchives[0]);
        Check(simulatorFiles.Count == 12 && simulatorFiles.ContainsKey("default.project.json") &&
            simulatorFiles.ContainsKey("src/server/Main.server.lua") && simulatorFiles.ContainsKey("src/client/Main.client.lua") &&
            simulatorFiles.ContainsKey("SentinelGame.server.lua") && simulatorFiles.ContainsKey("place/crystal-miner.rbxlx") &&
            simulatorFiles.ContainsKey("GAME_DESIGN.md") && simulatorFiles.ContainsKey("PLAYTEST_PLAN.md") &&
            simulatorFiles.ContainsKey("MONETIZATION.md") && simulatorFiles.ContainsKey("tools/blender/create_blockout.py") &&
            simulatorFiles.ContainsKey("tools/blender/build_scene.py"),
            "The ZIP must contain a standalone game Script, a text Roblox place, modular Rojo sources, design/test/monetization briefs and two Blender workflows.");
        Check(simulatorFiles.Values.All(content => content.Length <= 250_000) &&
            simulatorFiles.Keys.All(name => !name.StartsWith("/", StringComparison.Ordinal) && !name.Contains("..", StringComparison.Ordinal) && !name.Contains('\\')),
            "Every generated ZIP entry must be bounded and traversal-safe.");
        using (JsonDocument manifest = JsonDocument.Parse(simulatorFiles["default.project.json"]))
        {
            JsonElement tree = manifest.RootElement.GetProperty("tree");
            Check(tree.GetProperty("$className").GetString() == "DataModel" &&
                tree.GetProperty("ReplicatedStorage").GetProperty("Shared").GetProperty("$path").GetString() == "src/shared" &&
                tree.GetProperty("ServerScriptService").GetProperty("$path").GetString() == "src/server",
                "The Rojo manifest must be valid JSON with the expected source mappings.");
        }
        string serverSource = simulatorFiles["src/server/Main.server.lua"];
        Check(serverSource.Contains("not allowedActions[action]") && serverSource.Contains("action == \"RequestState\"") &&
            serverSource.Contains("UpdateAsync") && serverSource.Contains("safeNumber") && serverSource.Contains("TimeTrialTrack"),
            "Generated Luau must validate remote actions, persisted values and the racing profile.");
        string standalone = simulatorFiles["SentinelGame.server.lua"];
        Check(standalone.Contains("ProximityPrompt") && standalone.Contains("UpdateAsync") && standalone.Contains("SentinelGeneratedWorld") &&
            standalone.Contains("roundSign.Text = roundState") && !standalone.Contains("loadstring") && !standalone.Contains("require(123"),
            "The one-file ServerScript must create a playable server-owned prototype without dynamic remote code.");
        Check(RobloxPlaceFileBuilder.TryReadServerSource(simulatorFiles["place/crystal-miner.rbxlx"], out string placeServerSource, out string placeError) &&
            placeServerSource == standalone, "The .rbxlx XML place must contain the exact standalone server Script: " + placeError);
        Check(simulatorFiles["tools/blender/build_scene.py"].Contains("bpy.ops.export_scene.fbx") &&
            simulatorFiles["tools/blender/build_scene.py"].Contains("bpy.ops.wm.save_as_mainfile") &&
            !simulatorFiles["tools/blender/build_scene.py"].Contains("bpy.ops.wm.read_factory_settings"),
            "Blender automation must save a new .blend and export FBX without clearing an existing scene.");
        Check(simulatorFiles["src/client/Main.client.lua"].Contains("actionRemote:FireServer(\"RequestState\")") &&
            simulatorFiles["src/shared/Config.lua"].Contains("Genre = \"simulator\"") &&
            simulatorFiles["src/shared/Config.lua"].Contains("Name = \"Crystal Miner\""),
            "The generated client, theme config and simulator name must be wired together.");
        Check(simulatorFiles["tools/blender/create_blockout.py"].Contains("bpy.data.collections.new") &&
            !simulatorFiles["tools/blender/create_blockout.py"].Contains("bpy.data.collections.remove"),
            "The optional Blender script must keep existing named collections instead of deleting them.");
        string archiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gameArchives[0]))).ToLowerInvariant();
        Check(archiveHash.Length == 64 && routedGame.Contains(archiveHash), "The reported ZIP SHA-256 must match the saved archive bytes.");
        string[] generatedPlacePaths = Directory.GetFiles(gameGenerator.OutputDirectory, "crystal-miner.rbxlx", SearchOption.AllDirectories);
        string[] generatedLuaPaths = Directory.GetFiles(gameGenerator.OutputDirectory, "SentinelGame.server.lua", SearchOption.AllDirectories);
        Check(generatedPlacePaths.Length == 1 && generatedLuaPaths.Length == 1 &&
            File.ReadAllText(generatedPlacePaths[0]).Contains("SentinelGameBootstrap") &&
            File.ReadAllText(generatedLuaPaths[0]) == standalone,
            "Direct .lua/.rbxlx files must exist outside the ZIP and be byte-identical to the packaged project sources.");

        foreach (var item in gameCommands.Skip(1))
        {
            RobloxGameProjectResult generatedGame = await gameGenerator.GenerateFromCommandAsync(item.Command);
            Check(generatedGame.Success && generatedGame.Files == 12 && File.Exists(generatedGame.Path) &&
                File.Exists(generatedGame.LuaPath) && File.Exists(generatedGame.PlacePath) && File.Exists(generatedGame.BlenderScriptPath),
                "The " + item.Genre + " starter must generate and verify a complete local ZIP: " + generatedGame.Message);
            var profileFiles = ReadProjectArchive(generatedGame.Path);
            Check(profileFiles["src/shared/Config.lua"].Contains("Genre = \"" + item.Genre + "\"") &&
                profileFiles.ContainsKey("place/" + item.Slug + ".rbxlx") &&
                profileFiles["SentinelGame.server.lua"].Contains("GAME_GENRE = \"" + item.Genre + "\"") &&
                profileFiles["GAME_DESIGN.md"].Length > 100 && profileFiles["PLAYTEST_PLAN.md"].Length > 100,
                "The " + item.Genre + " ZIP must include its configured Lua/place and design/playtest files.");
        }
        Check(Directory.GetFiles(gameGenerator.OutputDirectory, "*.zip").Length == gameCommands.Length,
            "Each requested genre must produce one new archive without overwriting another.");

        var noAppGenerator = new RobloxGameProjectGenerator(Path.Combine(gameTestRoot, "NoApplications", "CreatedGames"));
        var noAppWorkflow = new RobloxCreativeWorkflowService(noAppGenerator, new BlenderAutomationService(), new RobloxStudioLauncher(), openApplications: false);
        string noAppResult = await noAppWorkflow.CreateAsync(new RobloxGameProjectSpec("racing", "Offline Trial", "offline-trial")
        {
            OriginalPrompt = "Create a Roblox racing prototype for offline workflow testing."
        });
        Check(noAppResult.Contains("Automatyczne uruchomienie aplikacji zewnętrznych wyłączono") &&
            Directory.GetFiles(noAppGenerator.OutputDirectory, "*.zip").Length == 1 &&
            Directory.GetFiles(noAppGenerator.OutputDirectory, "offline-trial.rbxlx", SearchOption.AllDirectories).Length == 1,
            "The project workflow must still save Lua/place/ZIP files when app launching is disabled, without needing Blender or Studio.");

        var recallMemory = new ConversationMemoryService(Path.Combine(root, "recall-memory"));
        recallMemory.AddUserMessage("Which icon did we choose for the launch?", "test");
        recallMemory.AddAssistantMessage("Decision recorded: Project Nebula uses the blue launch icon.");
        for (int i = 0; i < 6; i++)
        {
            recallMemory.AddUserMessage($"Unrelated discussion {i}: weather in another city.", "test");
            recallMemory.AddAssistantMessage($"The forecast for city {i} is mild.");
        }
        recallMemory.AddUserMessage("Co ustaliliśmy o projekcie Nebula?", "test");
        var aiHandler = new CapturingAiHandler();
        using (var testAi = new LocalAiService(new GamingModeService(), aiHandler, Path.Combine(root, "recall-ai-settings")))
        {
            var recallRouter = new CommandRouter(monitor, new SystemInfoService(), testAi, recallMemory, modelGenerator: modelGenerator);
            string answer = await recallRouter.ProcessAsync("Co ustaliliśmy o projekcie Nebula?");
            Check(answer == "Odpowiedź testowa.", "The local model integration test must complete successfully.");
            Check(aiHandler.UserPrompt.Contains("blue launch icon") && aiHandler.UserPrompt.Contains("starszy trafiony fragment"),
                "A natural-language recall follow-up must deliver its older matching exchange to the model: " + aiHandler.UserPrompt);
        }

        // --- the preview through the real pipeline: no side effects, a QR preview writes no PNG ---
        int pngBefore = QrFiles();
        string qrPreview = await router.ProcessAsync("jak to rozumiem: qr: https://example.com", default);
        Check(qrPreview.Contains("podgląd niczego nie zapisuje") && !qrPreview.Contains("Kod QR zapisany"), "The QR preview must only describe: " + qrPreview);
        Check(QrFiles() == pngBefore, "The decision preview must not create a PNG file.");
        string wifiPreview = await router.ProcessAsync("jak to rozumiem: qr wifi: Siec|haslo", default);
        Check(wifiPreview.Contains("Kod QR do sieci Wi-Fi") && QrFiles() == pngBefore, "The Wi-Fi QR preview must be a description too: " + wifiPreview);
        string viaRouter = await commandRouter.ProcessAsync("jak to rozumiem: lotto", default);
        Check(viaRouter.Contains("Narzędzie „Lotto”") && !viaRouter.Contains("Lotto (6 z 49)"), "The command router must preview without running: " + viaRouter);

        // --- search in tasks: local, read-only, diacritics-insensitive, includes done tasks and reminders ---
        var open = tasks.AddTask("Wysłać raport kwartalny", TaskRecord.PriorityNormal, null, "");
        Check(open != null, "test task 1 was not stored");
        Check(tasks.AddTask("Kupić chleb", TaskRecord.PriorityNormal, null, "") != null, "test task 2 was not stored");
        var finished = tasks.AddTask("Stary raport roczny", TaskRecord.PriorityNormal, null, "");
        Check(finished != null && tasks.SetTaskStatus(finished.Id, TaskRecord.StatusDone), "test task 3 was not closed");
        Check(tasks.AddReminder("Zadzwonić w sprawie raportu", DateTime.Now.AddHours(2), "") != null, "test reminder was not stored");
        int taskCount = tasks.GetTasks(includeDone: true).Count;

        string found = await router.ProcessAsync("szukaj w zadaniach: raport", default);
        Check(found.Contains("Wysłać raport kwartalny") && found.Contains("Stary raport roczny") && found.Contains("[zrobione]") && found.Contains("Zadzwonić w sprawie raportu"),
            "Task search must list open tasks, done tasks and reminders: " + found);
        Check(!found.Contains("chleb"), "Task search must only return matches: " + found);
        string accents = await router.ProcessAsync("szukaj w zadaniach: wysłać kwartalny", default);
        Check(accents.Contains("Wysłać raport kwartalny") && !accents.Contains("Stary raport"), "Every word of the phrase must match, diacritics included: " + accents);
        string plain = await router.ProcessAsync("szukaj w zadaniach: WYSLAC", default);
        Check(plain.Contains("Wysłać raport kwartalny"), "Task search must ignore case and diacritics: " + plain);
        string none = await router.ProcessAsync("szukaj w zadaniach: kosmos", default);
        Check(none.Contains("nie ma nic z frazą „kosmos”"), "No match must be said honestly: " + none);
        string bare = await router.ProcessAsync("szukaj w zadaniach", default);
        Check(bare.Contains("Podaj frazę"), "A bare task search must show usage, not a web search: " + bare);
        Check(tasks.GetTasks(includeDone: true).Count == taskCount, "Task search is read-only and must not change the tasks.");
        Check(tasks.Search("   ").Tasks.Count == 0 && tasks.Search("raport", limit: 1).Tasks.Count == 1, "TaskService.Search must ignore an empty phrase and respect the limit.");
        await Task.CompletedTask;
    }
}
