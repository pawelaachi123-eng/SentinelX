using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Offline Luau source templates and heuristic safety checks. Sentinel never executes arbitrary Luau,
/// downloads assets, logs in to Roblox, or publishes; the separate game workflow may run a generated Blender build and open a local place in Studio.</summary>
public static class RobloxLuauTools
{
    private sealed record Template(string Name, string Location, string Description, string Source);

    private static readonly IReadOnlyDictionary<string, Template> Templates = new Dictionary<string, Template>(StringComparer.Ordinal)
    {
        ["leaderstats"] = new("leaderstats", "ServerScriptService/Leaderstats.server.lua", "Creates session-only Coins stats for joining players.", LeaderstatsSource),
        ["sprint"] = new("sprint", "StarterPlayer/StarterPlayerScripts/Sprint.client.lua", "Adds a Left Shift sprint LocalScript; client-side speed is not an anti-cheat or server security boundary.", SprintSource),
        ["checkpoint"] = new("checkpoint", "ServerScriptService/Checkpoints.server.lua", "Session-only obby checkpoints. Put numbered BaseParts in workspace.Checkpoints.", CheckpointSource),
        ["remote"] = new("remote", "ServerScriptService/Shop.server.lua", "Server-authoritative RemoteEvent example: only an allow-listed item key crosses the network; price and balance stay on the server.", RemoteSource)
    };

    public static bool IsCommand(string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        bool bareCommand = text is "roblox" or "roblox pomoc" or "roblox szablony" or "szablony roblox" or "luau pomoc" or "roblox kod" or "luau sprawdz" or "sprawdz luau";
        return bareCommand || text.StartsWith("roblox kod:", StringComparison.Ordinal) || text.StartsWith("roblox kod ", StringComparison.Ordinal) ||
            text.StartsWith("luau sprawdz:", StringComparison.Ordinal) || text.StartsWith("luau sprawdz ", StringComparison.Ordinal) ||
            text.StartsWith("sprawdz luau:", StringComparison.Ordinal) || text.StartsWith("sprawdz luau ", StringComparison.Ordinal);
    }

    public static string? Process(string command, string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        if (text is "roblox" or "roblox pomoc" or "roblox szablony" or "szablony roblox" or "luau pomoc")
            return Help;

        if (text == "roblox kod" || text.StartsWith("roblox kod:", StringComparison.Ordinal) || text.StartsWith("roblox kod ", StringComparison.Ordinal))
            return GenerateTemplate(Argument(command));

        if (text == "luau sprawdz" || text.StartsWith("luau sprawdz:", StringComparison.Ordinal) || text.StartsWith("luau sprawdz ", StringComparison.Ordinal) ||
            text == "sprawdz luau" || text.StartsWith("sprawdz luau:", StringComparison.Ordinal) || text.StartsWith("sprawdz luau ", StringComparison.Ordinal))
            return Audit(Argument(command));

        return null;
    }

    public static string GenerateTemplate(string name)
    {
        string key = ConversationMemoryService.Normalize(name ?? "").Trim().ToLowerInvariant();
        key = key switch
        {
            "statystyki" or "statystyki gracza" or "leaderboard" => "leaderstats",
            "bieganie" or "sprintowanie" => "sprint",
            "obby" or "punkt kontrolny" or "punkty kontrolne" => "checkpoint",
            "remoteevent" or "remote event" or "sklep" or "shop" => "remote",
            _ => key
        };

        if (key.Length == 0)
            return "Podaj szablon: „roblox kod: leaderstats”, „roblox kod: sprint”, „roblox kod: checkpoint” albo „roblox kod: remote”.";
        if (!Templates.TryGetValue(key, out Template? template))
            return "Nie mam wbudowanego szablonu „" + Shorten(key, 60) + "”. Dostępne: leaderstats, sprint, checkpoint, remote (bezpieczny wzorzec RemoteEvent). Do innego systemu opisz cel zwykłym pytaniem — lokalny model może przygotować kod, ale nie uruchomi go.";

        return "ROBLOX STUDIO / LUAU · „" + template.Name + "”\n" +
            "Miejsce w Explorerze: " + template.Location + "\n" +
            template.Description + "\n\n```lua\n" + template.Source.TrimEnd() + "\n```\n" +
            "To szablon tekstowy — Sentinel nie kompilował go ani nie uruchamiał. Przetestuj w Roblox Studio na kopii projektu.";
    }

    /// <summary>Heuristic safety/API scan only. This is not a Luau parser, type checker or compiler.</summary>
    public static string Audit(string source)
    {
        string code = source ?? "";
        if (string.IsNullOrWhiteSpace(code))
            return "Podaj kod, np. „luau sprawdz: while true do | task.wait(1) | end”. Sprawdzam wyłącznie typowe czerwone flagi — nie kompiluję Luau.";
        if (code.Length > 20000)
            return "Kod ma więcej niż 20 000 znaków. Podziel go na części; krótki audyt nie kompiluje Luau ani nie czyta plików.";

        // Replace comments and literal text with spaces, preserving newlines so findings point to the
        // source line rather than matching examples in documentation strings or commented-out code.
        string analyzable = MaskCommentsAndStrings(code);
        var findings = new List<string>();
        AddFinding(findings, analyzable, @"\bloadstring\s*\(", "WYSOKIE RYZYKO", "loadstring uruchamia tekst jako kod; Roblox zwykle go blokuje, a obchodzenie tego mechanizmu jest niebezpieczne.");
        AddFinding(findings, analyzable, @"\brequire\s*\(\s*\d+\s*\)", "SPRAWDŹ ŹRÓDŁO", "require z numerycznym Asset ID pobiera cudzy moduł. Zweryfikuj właściciela i cały kod przed użyciem.");

        bool tightLoop = IsMatch(analyzable, @"\bwhile\s+true\s+do\b");
        bool hasYield = IsMatch(analyzable, @"\btask\.wait\s*\(|\bRunService\.(?:Heartbeat|Stepped|RenderStepped)\s*:\s*Wait\s*\(");
        if (tightLoop && !hasYield)
            AddFinding(findings, analyzable, @"\bwhile\s+true\s+do\b", "MOŻLIWA BLOKADA", "Pętla while true może zablokować wątek. Dodaj kontrolowane task.wait albo użyj zdarzenia RunService.");

        AddFinding(findings, analyzable, @"(?<![\w.])(?:wait|spawn|delay)\s*\(", "STARSZE API", "Zamiast wait/spawn/delay używaj task.wait/task.spawn/task.delay; sprawdź zachowanie konkretnego kodu.");

        if (IsMatch(analyzable, @"\.Touched\s*:\s*Connect\s*\(") && !IsMatch(analyzable, @"\b(?:debounce|cooldown|lastHit|touchCooldown)\b"))
            AddFinding(findings, analyzable, @"\.Touched\s*:\s*Connect\s*\(", "MOŻLIWE POWTÓRZENIA", "Touched może uruchomić się wiele razy dla jednej postaci. Rozważ debounce lub idempotentny warunek po stronie serwera.");

        if (IsMatch(analyzable, @"\bOnServerEvent\b"))
            AddFinding(findings, analyzable, @"\bOnServerEvent\b", "GRANICA ZAUFANIA", "Argumenty RemoteEvent pochodzą od klienta: waliduj typ, zakres, uprawnienia i stan gry na serwerze.");

        if (IsMatch(analyzable, @"\bDataStoreService\b") && !IsMatch(analyzable, @"\bpcall\s*\("))
            AddFinding(findings, analyzable, @"\bDataStoreService\b", "ODPORNOŚĆ", "Wywołania DataStore mogą zawieść; dodaj obsługę błędów (pcall) i kontrolowane ponawianie.");

        var report = new StringBuilder("SZYBKI AUDYT LUAU · ").Append(findings.Count).AppendLine(" uwag");
        if (findings.Count == 0)
            report.AppendLine("Nie znalazłem typowych czerwonych flag w sprawdzonych wzorcach.");
        else
            foreach (string finding in findings) report.Append("· ").AppendLine(finding);
        report.Append("Komentarze, zwykłe łańcuchy i długie literały Luau są pomijane; zawartość template stringów jest skanowana konserwatywnie ze względu na interpolacje. To heurystyka, nie parser ani kompilator. Nie potwierdza poprawności składni, API, uprawnień ani działania w Roblox Studio; kod sprawdzaj na testowym projekcie.");
        return report.ToString();
    }

    /// <summary>Blanks comments and literal text while recursively scanning executable template interpolations, preserving offsets and line numbers.</summary>
    private static string MaskCommentsAndStrings(string source)
    {
        char[] masked = source.ToCharArray();
        int index = 0;
        MaskCode(source, masked, ref index, inTemplateInterpolation: false);
        return new string(masked);
    }

    private static void MaskCode(string source, char[] masked, ref int index, bool inTemplateInterpolation)
    {
        int nestedBraceDepth = 0;
        while (index < source.Length)
        {
            if (inTemplateInterpolation && source[index] == '}')
            {
                if (nestedBraceDepth == 0) { index++; return; }
                nestedBraceDepth--;
                index++;
                continue;
            }
            if (inTemplateInterpolation && source[index] == '{')
            {
                nestedBraceDepth++;
                index++;
                continue;
            }

            if (source[index] == '-' && index + 1 < source.Length && source[index + 1] == '-')
            {
                int commentStart = index + 2;
                int end;
                if (TryReadLongBracket(source, commentStart, out int commentContentStart, out string commentEnd))
                {
                    int closingIndex = source.IndexOf(commentEnd, commentContentStart, StringComparison.Ordinal);
                    end = closingIndex < 0 ? source.Length : closingIndex + commentEnd.Length;
                }
                else
                {
                    end = commentStart;
                    while (end < source.Length && source[end] is not ('\r' or '\n')) end++;
                }
                MaskRange(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] is '\'' or '"')
            {
                int start = index;
                char quote = source[index++];
                while (index < source.Length)
                {
                    if (source[index] == '\\') { index = Math.Min(source.Length, index + 2); continue; }
                    if (source[index++] == quote) break;
                }
                MaskRange(masked, start, index);
                continue;
            }

            if (source[index] == '[' && TryReadLongBracket(source, index, out int longContentStart, out string longEnd))
            {
                int closingIndex = source.IndexOf(longEnd, longContentStart, StringComparison.Ordinal);
                int end = closingIndex < 0 ? source.Length : closingIndex + longEnd.Length;
                MaskRange(masked, index, end);
                index = end;
                continue;
            }

            if (source[index] == '`')
            {
                MaskTemplateString(source, masked, ref index);
                continue;
            }
            index++;
        }
    }

    private static void MaskTemplateString(string source, char[] masked, ref int index)
    {
        MaskRange(masked, index, index + 1); // Opening backtick belongs to the template's literal text.
        index++;
        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                int end = Math.Min(source.Length, index + 2);
                MaskRange(masked, index, end);
                index = end;
                continue;
            }
            if (source[index] == '`')
            {
                MaskRange(masked, index, index + 1);
                index++;
                return;
            }
            if (source[index] == '{')
            {
                index++; // Keep and scan the expression, including nested table/function braces.
                MaskCode(source, masked, ref index, inTemplateInterpolation: true);
                continue;
            }
            MaskRange(masked, index, index + 1);
            index++;
        }
    }

    private static bool TryReadLongBracket(string source, int start, out int contentStart, out string closingDelimiter)
    {
        contentStart = 0;
        closingDelimiter = "";
        if (start < 0 || start >= source.Length || source[start] != '[') return false;
        int cursor = start + 1;
        while (cursor < source.Length && source[cursor] == '=') cursor++;
        if (cursor >= source.Length || source[cursor] != '[') return false;
        int equals = cursor - start - 1;
        contentStart = cursor + 1;
        closingDelimiter = "]" + new string('=', equals) + "]";
        return true;
    }

    private static void MaskRange(char[] destination, int start, int end)
    {
        for (int i = start; i < end; i++)
            if (destination[i] is not ('\r' or '\n')) destination[i] = ' ';
    }

    private static void AddFinding(List<string> findings, string code, string pattern, string title, string advice)
    {
        Match match;
        try { match = Regex.Match(code, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
        catch (RegexMatchTimeoutException) { return; }
        if (!match.Success) return;
        int line = 1;
        for (int i = 0; i < match.Index; i++) if (code[i] == '\n') line++;
        findings.Add(title + " (linia " + line + "): " + advice);
    }

    private static bool IsMatch(string code, string pattern)
    {
        try { return Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static string Argument(string command)
    {
        string raw = (command ?? "").Trim();
        int colon = raw.IndexOf(':');
        return colon >= 0 ? raw[(colon + 1)..].Trim() : "";
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private const string Help = "ROBLOX STUDIO / LUAU · NARZĘDZIA OFFLINE\n" +
        "· „roblox kod: leaderstats” — ServerScriptService/Leaderstats.server.lua.\n" +
        "· „roblox kod: sprint” — LocalScript w StarterPlayerScripts; klient nie jest granicą antycheat.\n" +
        "· „roblox kod: checkpoint” — ServerScriptService; wymaga folderu workspace.Checkpoints i ponumerowanych części.\n" +
        "· „roblox kod: remote” — przykład RemoteEvent z walidacją po stronie serwera.\n" +
        "· „Stwórz mi grę na Robloxie — opisz gatunek, pętlę, styl, telefon/PC i nazwę” — bez specjalnej komendy; Sentinel przygotuje .rbxlx, skrypt .lua, źródła Rojo i Blender build. Jeśli aplikacje są zainstalowane, workflow spróbuje uruchomić Blender i otworzyć place w Roblox Studio.\n" +
        "· „luau sprawdz: <kod>” — heurystyczny przegląd znanych ryzyk, bez kompilacji.\n" +
        "Nie wykonuję dowolnego kodu Luau, nie loguję się, nie publikuję, nie gwarantuję popularności ani zarobku. Studio musi potwierdzić place i zachowanie, a Blender wynik wizualny.";

    private const string LeaderstatsSource = """
        -- ServerScriptService/Leaderstats.server.lua
        -- Coins exist for this server session only; persistence needs a separate DataStore design.
        local Players = game:GetService("Players")

        local function addLeaderstats(player)
            if player:FindFirstChild("leaderstats") then return end
            local leaderstats = Instance.new("Folder")
            leaderstats.Name = "leaderstats"
            leaderstats.Parent = player

            local coins = Instance.new("IntValue")
            coins.Name = "Coins"
            coins.Value = 0
            coins.Parent = leaderstats
        end

        Players.PlayerAdded:Connect(addLeaderstats)

        -- Also cover players already present if this script starts after they join in Studio.
        for _, player in ipairs(Players:GetPlayers()) do
            task.spawn(addLeaderstats, player)
        end
        """;

    private const string SprintSource = """
        -- StarterPlayer/StarterPlayerScripts/Sprint.client.lua (LocalScript)
        local Players = game:GetService("Players")
        local UserInputService = game:GetService("UserInputService")

        local player = Players.LocalPlayer
        local DEFAULT_SPEED = 16 -- Match this to the movement system in your game.
        local SPRINT_SPEED = 24
        local sprinting = false

        local function setSprinting(enabled)
            sprinting = enabled
            local character = player.Character
            local humanoid = character and character:FindFirstChildOfClass("Humanoid")
            if humanoid then
                humanoid.WalkSpeed = sprinting and SPRINT_SPEED or DEFAULT_SPEED
            end
        end

        player.CharacterAdded:Connect(function(character)
            local humanoid = character:WaitForChild("Humanoid")
            humanoid.WalkSpeed = sprinting and SPRINT_SPEED or DEFAULT_SPEED
        end)

        UserInputService.InputBegan:Connect(function(input, gameProcessed)
            if gameProcessed or input.KeyCode ~= Enum.KeyCode.LeftShift then return end
            setSprinting(true)
        end)

        UserInputService.InputEnded:Connect(function(input)
            if input.KeyCode ~= Enum.KeyCode.LeftShift then return end
            setSprinting(false)
        end)
        """;

    private const string CheckpointSource = """
        -- ServerScriptService/Checkpoints.server.lua
        -- Create workspace.Checkpoints and place anchored BaseParts named 1, 2, 3, ... inside it.
        -- Progress is session-only; this example deliberately does not write to a DataStore.
        local Players = game:GetService("Players")
        local checkpoints = workspace:WaitForChild("Checkpoints")
        local debounce = {}

        local function getStageValue(player)
            local leaderstats = player:FindFirstChild("leaderstats")
            local stage = leaderstats and leaderstats:FindFirstChild("Stage")
            if stage and stage:IsA("IntValue") then return stage end

            if not leaderstats then
                leaderstats = Instance.new("Folder")
                leaderstats.Name = "leaderstats"
                leaderstats.Parent = player
            end
            stage = Instance.new("IntValue")
            stage.Name = "Stage"
            stage.Value = 0
            stage.Parent = leaderstats
            return stage
        end

        local boundPlayers = {}

        local function moveToCheckpoint(player, character)
            local stage = getStageValue(player)
            local checkpoint = checkpoints:FindFirstChild(tostring(stage.Value))
            local root = character:WaitForChild("HumanoidRootPart", 5)
            if checkpoint and checkpoint:IsA("BasePart") and root then
                character:PivotTo(checkpoint.CFrame + Vector3.new(0, 4, 0))
            end
        end

        local function bindPlayer(player)
            if boundPlayers[player] then return end
            boundPlayers[player] = true
            getStageValue(player)
            player.CharacterAdded:Connect(function(character)
                moveToCheckpoint(player, character)
            end)
        end

        Players.PlayerAdded:Connect(bindPlayer)
        for _, player in ipairs(Players:GetPlayers()) do bindPlayer(player) end

        for _, checkpoint in ipairs(checkpoints:GetChildren()) do
            if checkpoint:IsA("BasePart") then
                local checkpointNumber = tonumber(checkpoint.Name)
                if checkpointNumber then
                    checkpoint.Touched:Connect(function(hit)
                        local character = hit:FindFirstAncestorOfClass("Model")
                        local player = character and Players:GetPlayerFromCharacter(character)
                        if not player or debounce[player] then return end
                        debounce[player] = true

                        local stage = getStageValue(player)
                        if checkpointNumber > stage.Value then
                            stage.Value = checkpointNumber
                        end
                        task.delay(0.2, function() debounce[player] = nil end)
                    end)
                end
            end
        end

        Players.PlayerRemoving:Connect(function(player)
            debounce[player] = nil
            boundPlayers[player] = nil
        end)
        """;

    private const string RemoteSource = """
        -- ServerScriptService/Shop.server.lua
        -- Client requests only a product key. Prices, balance checks and rewards belong on the server.
        local Players = game:GetService("Players")
        local ReplicatedStorage = game:GetService("ReplicatedStorage")

        local remote = ReplicatedStorage:FindFirstChild("RequestPurchase")
        if remote and not remote:IsA("RemoteEvent") then
            error("ReplicatedStorage.RequestPurchase must be a RemoteEvent")
        end
        if not remote then
            remote = Instance.new("RemoteEvent")
            remote.Name = "RequestPurchase"
            remote.Parent = ReplicatedStorage
        end

        local CATALOG = {
            SmallPotion = { Price = 25 },
        }
        local REQUEST_COOLDOWN = 0.25
        local lastRequest = {}

        remote.OnServerEvent:Connect(function(player, itemKey)
            if typeof(itemKey) ~= "string" or #itemKey > 32 then return end

            local now = os.clock()
            if now - (lastRequest[player] or 0) < REQUEST_COOLDOWN then return end
            lastRequest[player] = now

            local product = CATALOG[itemKey]
            if not product then return end

            local leaderstats = player:FindFirstChild("leaderstats")
            local coins = leaderstats and leaderstats:FindFirstChild("Coins")
            if not coins or not coins:IsA("IntValue") or coins.Value < product.Price then return end

            coins.Value -= product.Price
            -- TODO: grant the item here on the server. Never accept a client-supplied price or balance.
        end)

        Players.PlayerRemoving:Connect(function(player)
            lastRequest[player] = nil
        end)

        -- Client example (put this in a LocalScript under a TextButton):
        -- local ReplicatedStorage = game:GetService("ReplicatedStorage")
        -- local requestPurchase = ReplicatedStorage:WaitForChild("RequestPurchase")
        -- script.Parent.Activated:Connect(function()
        --     requestPurchase:FireServer("SmallPotion")
        -- end)
        """;
}
