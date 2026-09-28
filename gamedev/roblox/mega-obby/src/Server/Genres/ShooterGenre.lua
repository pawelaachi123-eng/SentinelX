--!strict
-- ============================================================================
-- GAMEFORGE — gatunek SHOOTER (Server/Genres/ShooterGenre)
-- Arena z osłonami i 6 poruszającymi się celami-neonami. Klient strzela remote
-- (pozycja celownika), SERWER robi raycast od głowy (max zasięg wg ulepszeń),
-- cooldown wg szybkostrzelności — klient nigdy nie zgłasza trafienia sam.
-- Trafienie: cel gaśnie 3 s, gracz dostaje monety i licznik zestrzeleń.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")
local Workspace = game:GetService("Workspace")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "shooter"
Pack.DisplayName = "Strzelanka"

local bots = {} -- [botId] = { Part, Stand, Alive, BaseX, Phase }

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function cooldownOf(data)
	return math.clamp(0.45 - 0.03 * (data.Stats.FireRate or 0), 0.15, 0.45)
end

local function rangeOf(data)
	return 150 + 25 * (data.Stats.Range or 0)
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "ShooterWorld"
	world.Parent = folders.World

	local arena = Instance.new("Part")
	arena.Name = "Arena"
	arena.Size = Vector3.new(120, 2, 120)
	arena.Position = Vector3.new(0, -0.5, 0)
	arena.Anchored = true
	arena.Color = Color3.fromRGB(44, 50, 64)
	arena.Material = Enum.Material.Concrete
	arena.Parent = world

	for _, wall in ipairs({
		{ Vector3.new(0, 5, -60), Vector3.new(120, 10, 2) },
		{ Vector3.new(0, 5, 60), Vector3.new(120, 10, 2) },
		{ Vector3.new(-60, 5, 0), Vector3.new(2, 10, 120) },
		{ Vector3.new(60, 5, 0), Vector3.new(2, 10, 120) },
	}) do
		local wallPart = Instance.new("Part")
		wallPart.Name = "Wall"
		wallPart.Size = wall[2]
		wallPart.Position = wall[1]
		wallPart.Anchored = true
		wallPart.Color = Color3.fromRGB(56, 62, 78)
		wallPart.Material = Enum.Material.Concrete
		wallPart.Parent = world
	end

	-- osłony
	local coverSpots = { Vector3.new(-20, 3, -12), Vector3.new(20, 3, -12), Vector3.new(-20, 3, 14), Vector3.new(20, 3, 14), Vector3.new(0, 3, -28), Vector3.new(0, 3, 28) }
	for index, spot in ipairs(coverSpots) do
		local cover = Instance.new("Part")
		cover.Name = "Cover" .. index
		cover.Size = Vector3.new(8, 6, 2)
		cover.Position = spot
		cover.Anchored = true
		cover.Color = Color3.fromRGB(70, 78, 96)
		cover.Material = Enum.Material.Metal
		cover.Parent = world
	end

	-- cele: stojaki + ruchome tarcze
	for index = 1, 6 do
		local baseX = -50 + (index - 1) * 20
		local stand = Instance.new("Part")
		stand.Name = "BotStand" .. index
		stand.Size = Vector3.new(1, 6, 1)
		stand.Position = Vector3.new(baseX, 3, -45)
		stand.Anchored = true
		stand.Color = Color3.fromRGB(90, 96, 112)
		stand.Material = Enum.Material.Metal
		stand.Parent = world

		local target = Instance.new("Part")
		target.Name = "Bot" .. index
		target.Shape = Enum.PartType.Ball
		target.Size = Vector3.new(3, 3, 3)
		target.Position = Vector3.new(baseX, 8, -45)
		target.Anchored = true
		target.Color = Color3.fromRGB(255, 80, 80)
		target.Material = Enum.Material.Neon
		target:SetAttribute("BotId", index)
		target:SetAttribute("Alive", true)
		target.Parent = world
		bots[index] = { Part = target, Stand = stand, Alive = true, BaseX = baseX, Phase = index * 1.1 }
	end

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "ShooterSpawn"
	spawnPad.Size = Vector3.new(16, 1, 16)
	spawnPad.Position = Vector3.new(0, 1, 48)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(34, 211, 238)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	return { SpawnPad = spawnPad, Root = world }
end

function Pack.Setup(deps, world)
	-- cele wędrują na boki (serwer, sin czasu)
	task.spawn(function()
		RunService.Heartbeat:Connect(function()
			local t = Workspace:GetServerTimeNow()
			for _, bot in pairs(bots) do
				if bot.Alive then
					local part = bot.Part
					part.Position = Vector3.new(bot.BaseX + math.sin(t * 0.9 + bot.Phase) * 9, 8, -45)
				end
			end
		end)
	end)

	-- strzał: remote z pozycją celownika; walidacja po stronie serwera
	deps.Net:OnServer("Shoot", function(player, aimPosition)
		if typeof(aimPosition) ~= "Vector3" then
			return false, "Podaj cel."
		end
		local data = deps.PlayerDataService.Get(player)
		local character = player.Character
		local head = character and character:FindFirstChild("Head")
		if not (data and head) then
			return false, "Chwila — dane się wczytują."
		end
		-- cooldown = szybkostrzelność z ulepszeń
		local now = os.clock()
		local last = character:GetAttribute("LastShot") or 0
		if now - last < cooldownOf(data) then
			return false, ""
		end
		character:SetAttribute("LastShot", now)

		local origin = head.Position
		local direction = aimPosition - origin
		if direction.Magnitude < 0.5 then
			return false, ""
		end
		direction = direction.Unit * rangeOf(data)
		local rayParams = RaycastParams.new()
		rayParams.FilterType = Enum.RaycastFilterType.Exclude
		rayParams.FilterDescendantsInstances = { character }
		local result = Workspace:Raycast(origin, direction, rayParams)
		if result and result.Instance and result.Instance:GetAttribute("BotId") then
			local bot = bots[result.Instance:GetAttribute("BotId")]
			if bot and bot.Alive then
				bot.Alive = false
				bot.Part:SetAttribute("Alive", false)
				bot.Part.Transparency = 1
				deps.PlayerDataService.AddCoins(player, 15, "zestrzelenie")
				deps.PlayerDataService.IncrementStat(player, "Kills", 1)
				deps.EffectsService.BurstAt(result.Instance.Position, { 255, 80, 80 })
				task.delay(3, function()
					if bot.Part.Parent then
						bot.Part.Transparency = 0
						bot.Alive = true
						bot.Part:SetAttribute("Alive", true)
					end
				end)
				return true, "Zestrzelony! +15"
			end
		end
		return true, ""
	end, { 8, 12 })

	print("[Forge] Strzelanka: arena, cele i raycast serwera działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Zestrzelenia", Value = tostring(data.Stats.Kills or 0) },
			{ Label = "Szybkostrzelność", Value = (data.Stats.FireRate or 0) .. " lvl (" .. string.format("%.2f", cooldownOf(data)) .. " s)" },
			{ Label = "Zasięg", Value = (data.Stats.Range or 0) .. " lvl (" .. rangeOf(data) .. " studów)" },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local fire = data.Stats.FireRate or 0
	local range = data.Stats.Range or 0
	return {
		{ Id = "fire_rate", Name = "Szybkostrzelność +1", Description = "krótszy cooldown (lvl " .. fire .. "/10)", Kind = "stat", Stat = "FireRate", Amount = 1, Price = math.floor(300 * 1.5 ^ fire), Owned = fire >= 10 },
		{ Id = "range_up", Name = "Zasięg +25 studów", Description = "dalej sięgasz (lvl " .. range .. "/6)", Kind = "stat", Stat = "Range", Amount = 1, Price = math.floor(450 * 1.5 ^ range), Owned = range >= 6 },
	}
end

Pack.QuestPool = {
	{ id = "sh_kills_10", type = "stat:Kills", target = 10, reward = 300, desc = "Zestrzel 10 celów" },
	{ id = "sh_kills_50", type = "stat:Kills", target = 50, reward = 1000, desc = "Zestrzel 50 celów" },
	{ id = "sh_coins_250", type = "coins", target = 250, reward = 350, desc = "Zbierz 250 monet" },
	{ id = "sh_time_20", type = "playtime", target = 20, reward = 300, desc = "Walcz 20 minut" },
}

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (25 + math.min(25, data.Stats.Kills or 0)) or 25
end

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Kills or 0) or 0
end

return Pack
