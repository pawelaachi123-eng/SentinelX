namespace SentinelX.Services.Creative;

/// <summary>Produces a self-contained, server-authoritative Luau Script that can be placed in ServerScriptService.
/// The generated place script owns only its tagged world folder and never downloads or executes remote code.</summary>
public static class RobloxOneFileGameBuilder
{
    public static string Build(RobloxGameProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string quotedName = RobloxGameProjectGenerator.LuaString(spec.DisplayName);
        string slug = spec.Slug;
        (int r, int g, int b) accent = spec.Theme switch
        {
            "space" => (92, 125, 255),
            "underwater" => (53, 202, 220),
            "fantasy" => (179, 112, 255),
            "cyberpunk" => (39, 244, 198),
            "cozy" => (255, 184, 114),
            "nature" => (117, 212, 112),
            "city" => (255, 177, 88),
            "horror" => (235, 63, 94),
            "winter" => (138, 211, 255),
            _ => spec.Genre switch
            {
                "obby" => (255, 130, 94), "tycoon" => (255, 196, 76), "rounds" => (109, 157, 255),
                "racing" => (71, 236, 188), _ => (62, 211, 174)
            }
        };
        return $$"""
            -- Sentinel X one-file Roblox starter for {{spec.DisplayName}}.
            -- Put this Script in ServerScriptService in a new test place, then press Play.
            -- Gameplay state and rewards are server-owned. This prototype uses built-in prompts, not client trust.
            -- It creates a tagged folder; on rerun it replaces ONLY its own previous generated folder.
            local Players = game:GetService("Players")
            local DataStoreService = game:GetService("DataStoreService")
            local Workspace = game:GetService("Workspace")

            local GAME_NAME = {{quotedName}}
            local GAME_GENRE = "{{spec.Genre}}"
            local DATASTORE_NAME = "SX_{{slug}}_v2"
            local MAX_COINS = 1000000000
            local MAX_ENERGY = 1000000
            local MAX_UPGRADE = 20
            local ACCENT = Color3.fromRGB({{accent.r}}, {{accent.g}}, {{accent.b}})
            local ACCENT_2 = Color3.fromRGB(116, 135, 255)
            local INK = Color3.fromRGB(14, 21, 37)
            local PALETTE = {
                Base = Color3.fromRGB(23, 33, 53),
                Surface = Color3.fromRGB(38, 52, 76),
                Light = Color3.fromRGB(229, 239, 255),
                Danger = Color3.fromRGB(255, 76, 103),
                Gold = Color3.fromRGB(255, 210, 94),
            }

            local previousWorld = Workspace:FindFirstChild("SentinelGeneratedWorld")
            if previousWorld then
                if previousWorld:GetAttribute("SentinelGenerated") ~= true then
                    error("Sentinel refused to replace an unowned workspace.SentinelGeneratedWorld folder")
                end
                previousWorld:Destroy()
            end

            local world = Instance.new("Folder")
            world.Name = "SentinelGeneratedWorld"
            world:SetAttribute("SentinelGenerated", true)
            world:SetAttribute("Project", GAME_NAME)
            world.Parent = Workspace

            local profiles = {}
            local leaderstats = {}
            local actionTimes = {}
            local checkpoints = {}
            local raceGates = {}
            local roundState = "Lobby"
            local dataStore = nil
            pcall(function() dataStore = DataStoreService:GetDataStore(DATASTORE_NAME) end)

            local function safeInteger(value, fallback, maximum)
                if typeof(value) ~= "number" or value ~= value or value == math.huge or value == -math.huge then return fallback end
                return math.clamp(math.floor(value), 0, maximum)
            end

            local function defaultProfile()
                return {
                    Coins = 0, Energy = 0, Power = 1, UpgradeLevel = 0,
                    Bank = 0, DropperLevel = 1, Stage = 0, Wins = 0,
                    BestRaceMilliseconds = 0,
                }
            end

            local function updateStats(player)
                local profile = profiles[player.UserId]
                local values = leaderstats[player.UserId]
                if not profile or not values then return end
                values.Coins.Value = safeInteger(profile.Coins, 0, MAX_COINS)
                values.Wins.Value = safeInteger(profile.Wins, 0, 1000000)
                if values:FindFirstChild("Stage") then values.Stage.Value = safeInteger(profile.Stage, 0, 1000) end
                if values:FindFirstChild("Energy") then values.Energy.Value = safeInteger(profile.Energy, 0, MAX_ENERGY) end
                if values:FindFirstChild("Power") then values.Power.Value = safeInteger(profile.Power, 1, MAX_ENERGY) end
                if values:FindFirstChild("Bank") then values.Bank.Value = safeInteger(profile.Bank, 0, MAX_COINS) end
                if values:FindFirstChild("BestTime") then values.BestTime.Value = safeInteger(profile.BestRaceMilliseconds, 0, 3600000) end
            end

            local function addStat(player, name, initialValue, className)
                local folder = leaderstats[player.UserId]
                if not folder:FindFirstChild(name) then
                    local value = Instance.new(className or "IntValue")
                    value.Name = name
                    value.Value = initialValue
                    value.Parent = folder
                end
            end

            local function loadProfile(player)
                local profile = defaultProfile()
                if dataStore then
                    local ok, saved = pcall(function() return dataStore:GetAsync("u_" .. player.UserId) end)
                    if ok and typeof(saved) == "table" then
                        profile.Coins = safeInteger(saved.Coins, 0, MAX_COINS)
                        profile.Energy = safeInteger(saved.Energy, 0, MAX_ENERGY)
                        profile.Power = math.max(1, safeInteger(saved.Power, 1, MAX_ENERGY))
                        profile.UpgradeLevel = safeInteger(saved.UpgradeLevel, 0, MAX_UPGRADE)
                        profile.Bank = safeInteger(saved.Bank, 0, MAX_COINS)
                        profile.DropperLevel = math.max(1, safeInteger(saved.DropperLevel, 1, 1000))
                        profile.Stage = safeInteger(saved.Stage, 0, 1000)
                        profile.Wins = safeInteger(saved.Wins, 0, 1000000)
                        profile.BestRaceMilliseconds = safeInteger(saved.BestRaceMilliseconds, 0, 3600000)
                    elseif not ok then
                        warn("Sentinel: DataStore read failed; using session defaults")
                    end
                end
                if player.Parent ~= Players then return nil end
                profiles[player.UserId] = profile
                local stats = Instance.new("Folder")
                stats.Name = "leaderstats"
                stats.Parent = player
                leaderstats[player.UserId] = stats
                addStat(player, "Coins", 0)
                addStat(player, "Wins", 0)
                if GAME_GENRE == "obby" or GAME_GENRE == "racing" then addStat(player, "Stage", 0) end
                if GAME_GENRE == "simulator" or GAME_GENRE == "custom" then
                    addStat(player, "Energy", 0)
                    addStat(player, "Power", 1)
                elseif GAME_GENRE == "tycoon" then
                    addStat(player, "Bank", 0)
                elseif GAME_GENRE == "racing" then
                    addStat(player, "BestTime", 0)
                end
                updateStats(player)
                return profile
            end

            local function saveProfile(player)
                local profile = profiles[player.UserId]
                if not profile or not dataStore then return end
                local payload = {
                    Version = 2,
                    Coins = safeInteger(profile.Coins, 0, MAX_COINS),
                    Energy = safeInteger(profile.Energy, 0, MAX_ENERGY),
                    Power = math.max(1, safeInteger(profile.Power, 1, MAX_ENERGY)),
                    UpgradeLevel = safeInteger(profile.UpgradeLevel, 0, MAX_UPGRADE),
                    Bank = safeInteger(profile.Bank, 0, MAX_COINS),
                    DropperLevel = math.max(1, safeInteger(profile.DropperLevel, 1, 1000)),
                    Stage = safeInteger(profile.Stage, 0, 1000),
                    Wins = safeInteger(profile.Wins, 0, 1000000),
                    BestRaceMilliseconds = safeInteger(profile.BestRaceMilliseconds, 0, 3600000),
                }
                local ok, err = pcall(function()
                    dataStore:UpdateAsync("u_" .. player.UserId, function(oldValue)
                        if typeof(oldValue) ~= "table" then return payload end
                        -- Per-player session ownership is handled by Roblox's single server process for this key.
                        return payload
                    end)
                end)
                if not ok then warn("Sentinel: DataStore save failed: " .. tostring(err)) end
            end

            local function allowAction(player, key, cooldown)
                local userId = player.UserId
                local now = os.clock()
                actionTimes[userId] = actionTimes[userId] or {}
                local last = actionTimes[userId][key] or 0
                if now - last < (cooldown or 0.35) then return false end
                actionTimes[userId][key] = now
                return true
            end

            local function grant(player, amount)
                local profile = profiles[player.UserId]
                if not profile then return end
                profile.Coins = math.clamp(profile.Coins + safeInteger(amount, 0, 100000), 0, MAX_COINS)
                updateStats(player)
            end

            local function makePart(name, size, position, color, material, shape)
                local part = Instance.new("Part")
                part.Name = name
                part.Size = size
                part.Position = position
                part.Anchored = true
                part.CanTouch = true
                part.Color = color
                part.Material = material or Enum.Material.SmoothPlastic
                part.TopSurface = Enum.SurfaceType.Smooth
                part.BottomSurface = Enum.SurfaceType.Smooth
                if shape then part.Shape = shape end
                part.Parent = world
                return part
            end

            local function addLight(part, color, brightness, range)
                local light = Instance.new("PointLight")
                light.Color = color
                light.Brightness = brightness or 1
                light.Range = range or 16
                light.Shadows = false
                light.Parent = part
                return light
            end

            local function addSign(part, text, color)
                local gui = Instance.new("BillboardGui")
                gui.Name = "SentinelWorldLabel"
                gui.Adornee = part
                gui.AlwaysOnTop = true
                gui.LightInfluence = 0
                gui.Size = UDim2.fromOffset(320, 64)
                gui.StudsOffset = Vector3.new(0, 4, 0)
                gui.Parent = part
                local label = Instance.new("TextLabel")
                label.Size = UDim2.fromScale(1, 1)
                label.BackgroundColor3 = INK
                label.BackgroundTransparency = 0.12
                label.Text = text
                label.TextColor3 = color or PALETTE.Light
                label.TextScaled = true
                label.Font = Enum.Font.GothamBold
                label.Parent = gui
                local corner = Instance.new("UICorner")
                corner.CornerRadius = UDim.new(0, 12)
                corner.Parent = label
                return label
            end

            local function prompt(part, actionText, objectText, callback, holdDuration)
                local value = Instance.new("ProximityPrompt")
                value.ActionText = actionText
                value.ObjectText = objectText
                value.HoldDuration = holdDuration or 0.15
                value.MaxActivationDistance = 12
                value.RequiresLineOfSight = false
                value.Exclusivity = Enum.ProximityPromptExclusivity.OnePerButton
                value.Parent = part
                value.Triggered:Connect(function(player)
                    if player.Parent ~= Players or not profiles[player.UserId] then return end
                    if not allowAction(player, "prompt:" .. objectText, 0.45) then return end
                    callback(player)
                end)
                return value
            end

            local base = makePart("WorldFoundation", Vector3.new(240, 2, 160), Vector3.new(0, -1, 0), PALETTE.Base, Enum.Material.Slate)
            base.CanTouch = false
            makePart("NorthRim", Vector3.new(244, 2, 3), Vector3.new(0, 0, -81), ACCENT, Enum.Material.Neon).CanCollide = false
            makePart("SouthRim", Vector3.new(244, 2, 3), Vector3.new(0, 0, 81), ACCENT_2, Enum.Material.Neon).CanCollide = false
            for index = 1, 8 do
                local x = (index % 2 == 0) and -108 or 108
                local z = -60 + math.floor((index - 1) / 2) * 38
                local pillar = makePart("AmbientPillar" .. index, Vector3.new(3, 14 + (index % 3) * 2, 3), Vector3.new(x, 6, z), PALETTE.Surface, Enum.Material.Metal)
                local cap = makePart("PillarLight" .. index, Vector3.new(4, 1, 4), pillar.Position + Vector3.new(0, pillar.Size.Y / 2, 0), ACCENT, Enum.Material.Neon)
                cap.CanCollide = false
                addLight(cap, ACCENT, 0.8, 14)
            end

            local spawn = Instance.new("SpawnLocation")
            spawn.Name = "SentinelGeneratedSpawn"
            spawn.Size = Vector3.new(16, 1, 16)
            spawn.Position = Vector3.new(0, 1, 58)
            spawn.Anchored = true
            spawn.Neutral = true
            spawn.Duration = 0
            spawn.Color = ACCENT
            spawn.Material = Enum.Material.Neon
            spawn.Parent = world
            addLight(spawn, ACCENT, 0.7, 18)

            local titleSign = makePart("ExperienceTitle", Vector3.new(3, 3, 3), Vector3.new(0, 9, 43), ACCENT, Enum.Material.Neon, Enum.PartType.Ball)
            titleSign.CanCollide = false
            addSign(titleSign, GAME_NAME, PALETTE.Light)
            addLight(titleSign, ACCENT, 1.4, 24)

            if GAME_GENRE == "obby" then
                local startSpawn = spawn
                startSpawn.Size = Vector3.new(18, 1, 18)
                startSpawn.Position = Vector3.new(-102, 4, 0)
                local fallPlane = makePart("FallReset", Vector3.new(260, 2, 180), Vector3.new(0, -24, 0), PALETTE.Danger, Enum.Material.Neon)
                fallPlane.Transparency = 1
                fallPlane.CanCollide = false
                fallPlane.Touched:Connect(function(hit)
                    local character = hit:FindFirstAncestorOfClass("Model")
                    local humanoid = character and character:FindFirstChildOfClass("Humanoid")
                    if humanoid and humanoid.Health > 0 then humanoid.Health = 0 end
                end)
                for index = 1, 10 do
                    local position = Vector3.new(-88 + (index - 1) * 18, 5 + ((index % 3) * 1.1), index % 2 == 0 and 6 or -6)
                    local pad = makePart("Checkpoint" .. index, Vector3.new(13, 1.5, 13), position,
                        index % 2 == 0 and ACCENT or ACCENT_2, Enum.Material.Neon)
                    checkpoints[index] = pad
                    pad.Touched:Connect(function(hit)
                        local character = hit:FindFirstAncestorOfClass("Model")
                        local player = character and Players:GetPlayerFromCharacter(character)
                        local profile = player and profiles[player.UserId]
                        local root = character and character:FindFirstChild("HumanoidRootPart")
                        if not profile or not root or (root.Position - pad.Position).Magnitude > 24 then return end
                        if index ~= profile.Stage + 1 or not allowAction(player, "checkpoint", 0.7) then return end
                        profile.Stage = index
                        grant(player, index == 10 and 25 or 5)
                        if index == 10 then profile.Wins = math.min(profile.Wins + 1, 1000000) end
                        updateStats(player)
                    end)
                end
                local finish = makePart("FinishPortal", Vector3.new(18, 8, 3), Vector3.new(101, 8, 0), PALETTE.Gold, Enum.Material.Neon)
                finish.CanCollide = false
                addSign(finish, "FINISH", INK)
            elseif GAME_GENRE == "tycoon" then
                makePart("TycoonPlot", Vector3.new(76, 1, 62), Vector3.new(0, 1, 0), PALETTE.Surface, Enum.Material.Metal)
                local machine = makePart("StarterMachine", Vector3.new(12, 14, 12), Vector3.new(-20, 9, 0), ACCENT, Enum.Material.Metal)
                local machineTop = makePart("MachineCore", Vector3.new(6, 4, 6), Vector3.new(-20, 18, 0), ACCENT_2, Enum.Material.Neon)
                machineTop.CanCollide = false
                addLight(machineTop, ACCENT_2, 1.1, 18)
                local collector = makePart("BankCollector", Vector3.new(18, 1, 18), Vector3.new(18, 2, 0), PALETTE.Gold, Enum.Material.Neon)
                addSign(collector, "COLLECT BANK", INK)
                prompt(collector, "Collect", "Tycoon Bank", function(player)
                    local profile = profiles[player.UserId]
                    if profile and profile.Bank > 0 then
                        local amount = safeInteger(profile.Bank, 0, MAX_COINS)
                        profile.Bank = 0
                        grant(player, amount)
                    end
                end)
                local upgrade = makePart("MachineUpgrade", Vector3.new(14, 1, 14), Vector3.new(-20, 2, 23), ACCENT_2, Enum.Material.Neon)
                addSign(upgrade, "UPGRADE MACHINE", PALETTE.Light)
                prompt(upgrade, "Upgrade", "Tycoon Machine", function(player)
                    local profile = profiles[player.UserId]
                    if not profile or profile.DropperLevel >= 1000 then return end
                    local cost = 25 * (2 ^ math.min(profile.DropperLevel - 1, 20))
                    if profile.Coins >= cost then
                        profile.Coins -= cost
                        profile.DropperLevel += 1
                        updateStats(player)
                    end
                end)
                task.spawn(function()
                    while world.Parent and task.wait(10) do
                        for userId, profile in pairs(profiles) do
                            profile.Bank = math.min(profile.Bank + profile.DropperLevel * 5, MAX_COINS)
                            local player = Players:GetPlayerByUserId(userId)
                            if player then updateStats(player) end
                        end
                    end
                end)
            elseif GAME_GENRE == "rounds" then
                spawn.Position = Vector3.new(0, 1, 65)
                local arena = makePart("RoundArena", Vector3.new(110, 1, 92), Vector3.new(0, 0, 0), PALETTE.Surface, Enum.Material.Slate)
                local hazard = makePart("RoundHazard", Vector3.new(88, 1.4, 2.5), Vector3.new(0, 4, 0), PALETTE.Danger, Enum.Material.Neon)
                hazard.Transparency = 1
                hazard.CanCollide = false
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
                local roundSign = addSign(arena, "SURVIVE THE ROUND", PALETTE.Light)
                task.spawn(function()
                    while world.Parent do
                        for seconds = 10, 1, -1 do
                            roundState = "Intermission · " .. seconds
                            roundSign.Text = roundState
                            task.wait(1)
                        end
                        local participants = {}
                        eliminated = {}
                        for _, player in ipairs(Players:GetPlayers()) do participants[player.UserId] = true end
                        hazard.Transparency = 0.12
                        for tick = 1, 120 do
                            hazard.CFrame = CFrame.new(0, 4, 0) * CFrame.Angles(0, tick * math.rad(3), 0)
                            roundState = "Survive · " .. math.ceil((120 - tick + 1) / 10)
                            roundSign.Text = roundState
                            if #Players:GetPlayers() == 0 then break end
                            task.wait(0.1)
                        end
                        hazard.Transparency = 1
                        for _, player in ipairs(Players:GetPlayers()) do
                            local profile = profiles[player.UserId]
                            local character = player.Character
                            local humanoid = character and character:FindFirstChildOfClass("Humanoid")
                            if participants[player.UserId] and not eliminated[player.UserId] and profile and humanoid and humanoid.Health > 0 then
                                grant(player, 10)
                                profile.Wins = math.min(profile.Wins + 1, 1000000)
                            end
                        end
                        roundState = "Round complete"
                        roundSign.Text = roundState
                        task.wait(3)
                    end
                end)
            elseif GAME_GENRE == "racing" then
                spawn.Size = Vector3.new(18, 1, 18)
                spawn.Position = Vector3.new(-105, 2, 0)
                local startPad = makePart("RaceStart", Vector3.new(18, 1, 18), Vector3.new(-100, 1, 0), ACCENT, Enum.Material.Neon)
                addSign(startPad, "TIME TRIAL", PALETTE.Light)
                prompt(startPad, "Start", "Time Trial", function(player)
                    local profile = profiles[player.UserId]
                    if profile then profile.RaceStage = 0; profile.RaceStartedAt = os.clock(); profile.Stage = 0; updateStats(player) end
                end)
                for index = 1, 7 do
                    local position = Vector3.new(-72 + index * 21, 4 + ((index % 3) * 2), index % 2 == 0 and 12 or -12)
                    local gate = makePart("RaceGate" .. index, Vector3.new(3, 10, 24), position,
                        index % 2 == 0 and ACCENT_2 or ACCENT, Enum.Material.Neon)
                    gate.Transparency = 0.35
                    gate.CanCollide = false
                    raceGates[index] = gate
                    gate.Touched:Connect(function(hit)
                        local character = hit:FindFirstAncestorOfClass("Model")
                        local player = character and Players:GetPlayerFromCharacter(character)
                        local profile = player and profiles[player.UserId]
                        local root = character and character:FindFirstChild("HumanoidRootPart")
                        if not profile or not root or not profile.RaceStartedAt or (root.Position - gate.Position).Magnitude > 30 then return end
                        if profile.RaceStage ~= index - 1 or not allowAction(player, "race-gate", 0.25) then return end
                        profile.RaceStage = index
                        profile.Stage = index
                        updateStats(player)
                    end)
                end
                local finish = makePart("RaceFinish", Vector3.new(5, 12, 28), Vector3.new(93, 6, 0), PALETTE.Gold, Enum.Material.Neon)
                finish.CanCollide = false
                finish.Touched:Connect(function(hit)
                    local character = hit:FindFirstAncestorOfClass("Model")
                    local player = character and Players:GetPlayerFromCharacter(character)
                    local profile = player and profiles[player.UserId]
                    local root = character and character:FindFirstChild("HumanoidRootPart")
                    if not profile or not root or not profile.RaceStartedAt or profile.RaceStage ~= #raceGates or (root.Position - finish.Position).Magnitude > 30 then return end
                    if not allowAction(player, "race-finish", 1) then return end
                    local elapsed = math.floor((os.clock() - profile.RaceStartedAt) * 1000)
                    profile.RaceStartedAt = nil
                    profile.RaceStage = 0
                    profile.Stage = #raceGates
                    if elapsed > 0 and (profile.BestRaceMilliseconds == 0 or elapsed < profile.BestRaceMilliseconds) then
                        profile.BestRaceMilliseconds = elapsed
                    end
                    profile.Wins = math.min(profile.Wins + 1, 1000000)
                    grant(player, math.clamp(10000 - elapsed, 25, 500))
                    updateStats(player)
                end)
            else
                -- Generic simulator loop: a safe playable core for custom briefs that need a different first mechanic.
                local resource = makePart("ResourceNode", Vector3.new(10, 10, 10), Vector3.new(0, 8, 0), ACCENT, Enum.Material.Neon, Enum.PartType.Ball)
                resource.CanCollide = false
                addLight(resource, ACCENT, 1.2, 20)
                addSign(resource, "GATHER · SELL · UPGRADE", PALETTE.Light)
                prompt(resource, "Gather", "Resource Node", function(player)
                    local profile = profiles[player.UserId]
                    if profile then profile.Energy = math.min(profile.Energy + profile.Power, MAX_ENERGY); updateStats(player) end
                end)
                local seller = makePart("SellStation", Vector3.new(16, 1, 14), Vector3.new(-22, 1, 0), PALETTE.Gold, Enum.Material.Neon)
                addSign(seller, "SELL ENERGY", INK)
                prompt(seller, "Sell", "Exchange", function(player)
                    local profile = profiles[player.UserId]
                    if profile and profile.Energy > 0 then
                        local amount = profile.Energy
                        profile.Energy = 0
                        grant(player, amount)
                    end
                end)
                local upgrade = makePart("UpgradeStation", Vector3.new(16, 1, 14), Vector3.new(22, 1, 0), ACCENT_2, Enum.Material.Neon)
                addSign(upgrade, "POWER UP", PALETTE.Light)
                prompt(upgrade, "Upgrade", "Workshop", function(player)
                    local profile = profiles[player.UserId]
                    if not profile or profile.UpgradeLevel >= MAX_UPGRADE then return end
                    local cost = 25 * (2 ^ math.min(profile.UpgradeLevel, MAX_UPGRADE))
                    if profile.Coins >= cost then
                        profile.Coins -= cost
                        profile.UpgradeLevel += 1
                        profile.Power = math.min(profile.Power + 1, MAX_ENERGY)
                        updateStats(player)
                    end
                end)
                for index = 1, 5 do
                    local x = -48 + index * 20
                    local shard = makePart("AmbientShard" .. index, Vector3.new(2.6, 7 + index % 3, 2.6), Vector3.new(x, 4, -30), index % 2 == 0 and ACCENT_2 or ACCENT, Enum.Material.Neon)
                    shard.CanCollide = false
                    shard.CFrame = shard.CFrame * CFrame.Angles(0, math.rad(45), math.rad(18))
                end
            end

            local function placeAtCheckpoint(player, character)
                if GAME_GENRE ~= "obby" then return end
                task.defer(function()
                    for _ = 1, 50 do
                        if player.Parent ~= Players or not character.Parent then return end
                        local profile = profiles[player.UserId]
                        if profile then
                            local checkpoint = checkpoints[math.clamp(profile.Stage, 0, #checkpoints)]
                            if checkpoint then character:PivotTo(checkpoint.CFrame + Vector3.new(0, 5, 0)) end
                            return
                        end
                        task.wait(0.1)
                    end
                end)
            end

            local function bindPlayer(player)
                if profiles[player.UserId] then return end
                player.CharacterAdded:Connect(function(character) placeAtCheckpoint(player, character) end)
                local profile = loadProfile(player)
                if not profile or player.Parent ~= Players then return end
                if player.Character then placeAtCheckpoint(player, player.Character) end
            end

            Players.PlayerAdded:Connect(function(player) task.spawn(bindPlayer, player) end)
            for _, player in ipairs(Players:GetPlayers()) do task.spawn(bindPlayer, player) end
            Players.PlayerRemoving:Connect(function(player)
                saveProfile(player)
                profiles[player.UserId] = nil
                leaderstats[player.UserId] = nil
                actionTimes[player.UserId] = nil
            end)
            game:BindToClose(function()
                for _, player in ipairs(Players:GetPlayers()) do saveProfile(player) end
                task.wait(2)
            end)
            print("Sentinel prototype ready: " .. GAME_NAME .. " (" .. GAME_GENRE .. ")")
            """;
    }
}
