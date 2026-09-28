--!strict
-- ============================================================================
-- GAMEFORGE — gatunek RYBY (Server/Genres/RybyGenre)
-- Wędkowanie jak w dużych grach rybackich: dotknij łowiska, poczekaj na
-- branie, złów rybę losowaną wagami rzadkości (wspólna → legendarna).
-- Mutacje (błyszczący/ogromny) mnożą wartość, każdy nowy gatunek ryby trafia
-- do INDEKSU (zbiory z nagrodą za komplet), pogoda na serwerze wzmacnia
-- szczęście. Sklep: wędzisko (rzadsze ryby), przynęta (szybsze brania),
-- łódka → GŁĘBINY (najelegantsze ryby). Leaderboard: najcięższy złów.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "ryby"
Pack.DisplayName = "Ryby"

local rarityColors = {
	["Wspólna"] = Color3.fromRGB(190, 190, 190),
	["Niezwykła"] = Color3.fromRGB(120, 200, 90),
	["Rzadka"] = Color3.fromRGB(90, 150, 255),
	["Epicka"] = Color3.fromRGB(190, 110, 255),
	["Legendarna"] = Color3.fromRGB(255, 200, 60),
}

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function fishConfig(deps)
	return deps.GameConfig.Fish
end

local function rodValueMultiplier(data)
	return 1 + 0.05 * (data.Stats.RodLevel or 0)
end

local function baitWaitSeconds(deps, data)
	local bounds = fishConfig(deps).WaitSeconds or { 3, 8 }
	local factor = math.max(0.4, 1 - 0.06 * (data.Stats.BaitLevel or 0))
	return math.random(bounds[1], bounds[2]) * factor
end

local function rollSpecies(deps, data, deepZone)
	local config = fishConfig(deps)
	local luck = deps.WeatherService and deps.WeatherService.Luck() or 1
	local rareBoost = (1 + 0.12 * (data.Stats.RodLevel or 0)) * (deepZone and (config.DeepBonus or 1.6) or 1)
	local pool = {}
	local total = 0
	for _, species in ipairs(config.Species) do
		local weight = 0
		for _, rarity in ipairs(config.RarityWeights) do
			if rarity.name == species.rarity then
				weight = rarity.weight
			end
		end
		if species.rarity ~= "Wspólna" then
			weight = weight * rareBoost * luck
		end
		if species.deep and (data.Stats.BoatLevel or 0) == 0 then
			weight = 0 -- głębinowe ryby tylko z łódką
		end
		if weight > 0 then
			table.insert(pool, { species = species, weight = weight })
			total += weight
		end
	end
	local roll = Random.new(os.time() + math.random(1, 100000)):NextNumber(0, total)
	for _, entry in ipairs(pool) do
		roll -= entry.weight
		if roll <= 0 then
			return entry.species
		end
	end
	return pool[#pool].species
end

local function rollMutation(deps)
	local config = fishConfig(deps)
	local pool, total = {}, 0
	for _, mutation in ipairs(config.Mutations) do
		table.insert(pool, mutation)
		total += mutation.weight
	end
	table.insert(pool, { name = "", multiplier = 1, weight = 100 - total }) -- bez mutacji
	local picked = deps.Util.WeightedPick(pool, Random.new(os.time() + math.random(1, 100000)))
	if picked.name == "" then
		return nil
	end
	return picked
end

local function catchFish(deps, player, part, deepZone)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return
	end
	deps.Net:SendTo(player, "Notify", "info", "🎣 Zarzucono wędkę… czekaj na branie!")
	task.wait(baitWaitSeconds(deps, data))
	if not player.Parent then
		return
	end
	local species = rollSpecies(deps, data, deepZone)
	local mutation = rollMutation(deps)
	local value = math.floor(species.value * (mutation and mutation.multiplier or 1)
		* (deepZone and (fishConfig(deps).DeepBonus or 1.6) or 1) * rodValueMultiplier(data))
	data.Stats.Caught = (data.Stats.Caught or 0) + 1
	data.Stats.BestCatch = math.max(data.Stats.BestCatch or 0, value)
	deps.PlayerDataService.AddCoins(player, value, "ryba")
	deps.CollectionService.Report(deps, player, species.name)
	deps.EffectsService.BurstAt(part.Position + Vector3.new(0, 2, 0),
		{ rarityColors[species.rarity].R * 255, rarityColors[species.rarity].G * 255, rarityColors[species.rarity].B * 255 })
	deps.Net:SendTo(player, "Notify", "success", "🎣 ZŁOWIONO: "
		.. (mutation and (mutation.name .. " ") or "") .. species.name .. " (" .. species.rarity .. ") — +" .. value .. " 🪙")
end

local function hookSpot(deps, part, deepZone)
	local lastCast = {}
	part.Touched:Connect(function(hit)
		local character = hit.Parent
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local now = os.clock()
		if lastCast[player] and now - lastCast[player] < 1.5 then
			return
		end
		lastCast[player] = now
		task.spawn(function()
			catchFish(deps, player, part, deepZone)
		end)
	end)
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Model")
	world.Name = "RybyWorld"

	-- pomost wędkarski
	local dock = Instance.new("Part")
	dock.Name = "Pomost"
	dock.Size = Vector3.new(24, 1, 12)
	dock.Position = Vector3.new(0, 1, 30)
	dock.Anchored = true
	dock.Color = Color3.fromRGB(150, 110, 60)
	dock.Material = Enum.Material.WoodPlanks
	dock.Parent = world

	local spawnPad = Instance.new("Part")
	spawnPad.Name = "SpawnPad"
	spawnPad.Size = Vector3.new(8, 1, 8)
	spawnPad.Position = Vector3.new(0, 1.5, 38)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(120, 200, 255)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	-- łowisko przy pomoście (wszyscy)
	local pond = Instance.new("Part")
	pond.Name = "Lowisko"
	pond.Size = Vector3.new(60, 2, 44)
	pond.Position = Vector3.new(0, 0, -6)
	pond.Anchored = true
	pond.Color = Color3.fromRGB(60, 130, 200)
	pond.Material = Enum.Material.Glass
	pond.Transparency = 0.25
	pond.Parent = world
	local pondSign = Instance.new("BillboardGui")
	pondSign.Size = UDim2.fromScale(10, 2.5)
	pondSign.StudsOffset = Vector3.new(0, 6, 0)
	pondSign.MaxDistance = 120
	local pondLabel = Instance.new("TextLabel")
	pondLabel.Size = UDim2.fromScale(1, 1)
	pondLabel.BackgroundTransparency = 1
	pondLabel.Font = Enum.Font.GothamBold
	pondLabel.TextScaled = true
	pondLabel.TextColor3 = Color3.fromRGB(190, 230, 255)
	pondLabel.TextStrokeTransparency = 0.4
	pondLabel.Text = "🎣 ŁOWISKO\ndotknij wody, żeby zarzucić wędkę"
	pondLabel.Parent = pondSign
	pondSign.Parent = pond

	-- GŁĘBINY za łódką (wymaga łódki ze sklepu)
	local deep = Instance.new("Part")
	deep.Name = "Glebiny"
	deep.Size = Vector3.new(44, 2, 34)
	deep.Position = Vector3.new(0, 0, -46)
	deep.Anchored = true
	deep.Color = Color3.fromRGB(25, 45, 110)
	deep.Material = Enum.Material.Glass
	deep.Transparency = 0.15
	deep.Parent = world
	local deepSign = Instance.new("BillboardGui")
	deepSign.Size = UDim2.fromScale(10, 2.5)
	deepSign.StudsOffset = Vector3.new(0, 6, 0)
	deepSign.MaxDistance = 120
	local deepLabel = Instance.new("TextLabel")
	deepLabel.Size = UDim2.fromScale(1, 1)
	deepLabel.BackgroundTransparency = 1
	deepLabel.Font = Enum.Font.GothamBold
	deepLabel.TextScaled = true
	deepLabel.TextColor3 = Color3.fromRGB(140, 170, 255)
	deepLabel.TextStrokeTransparency = 0.4
	deepLabel.Text = "🌊 GŁĘBINY\nwymagają łódki (sklep) — rzadsze ryby!"
	deepLabel.Parent = deepSign
	deepSign.Parent = deep

	world.Parent = folders.World
	return { SpawnPad = spawnPad, Root = world }
end

function Pack.Setup(deps, world)
	hookSpot(deps, world.Root.Lowisko, false)
	hookSpot(deps, world.Root.Glebiny, true)
	print("[Forge] Ryby: łowisko, głębiny, indeks i pogoda działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local weather = deps.WeatherService and (" " .. deps.WeatherService.Label()) or ""
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Złowione ryby", Value = tostring(data.Stats.Caught or 0) },
			{ Label = "Rekord (wartość)", Value = tostring(data.Stats.BestCatch or 0) .. " 🪙" },
			{ Label = "Wędzisko", Value = "lvl " .. tostring(data.Stats.RodLevel or 0) },
			{ Label = "Przynęta", Value = "lvl " .. tostring(data.Stats.BaitLevel or 0) },
			{ Label = "Głębiny", Value = (data.Stats.BoatLevel or 0) > 0 and "odblokowane 🌊" or "kup łódkę" },
			{ Label = "Indeks ryb", Value = deps.CollectionService and deps.CollectionService.Progress(deps, player, "ryby_staw")
				.. " + " .. deps.CollectionService.Progress(deps, player, "ryby_glebiny") or "—" },
			{ Label = "Pogoda", Value = weather },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local rod = data.Stats.RodLevel or 0
	local bait = data.Stats.BaitLevel or 0
	return {
		{ Id = "rod_up", Name = "Lepsze wędzisko +1", Description = "rzadsze ryby i +5% wartości (lvl " .. rod .. "/10)", Kind = "stat", Stat = "RodLevel", Amount = 1, Price = math.floor(500 * 1.5 ^ rod), Owned = rod >= 10 },
		{ Id = "bait_up", Name = "Lepsza przynęta +1", Description = "szybsze brania i mocniejsze szczęście (lvl " .. bait .. "/8)", Kind = "stat", Stat = "BaitLevel", Amount = 1, Price = math.floor(400 * 1.45 ^ bait), Owned = bait >= 8 },
		{ Id = "boat", Name = "🚤 Łódka na GŁĘBINY", Description = "odblokowuje głębiny: rzadsze i droższe ryby (x1,6)", Kind = "special", Price = 2500, Owned = (data.Stats.BoatLevel or 0) > 0 },
	}
end

function Pack.OnBuy(deps, player, item)
	if item.Id == "boat" then
		local data = deps.PlayerDataService.Get(player)
		if data then
			data.Stats.BoatLevel = 1
		end
		return true, "Płyniesz na GŁĘBINY! Rzadsze ryby czekają."
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "f_catch_10", type = "stat:Caught", target = 10, reward = 300, desc = "Złów 10 ryb" },
	{ id = "f_catch_50", type = "stat:Caught", target = 50, reward = 1200, desc = "Złów 50 ryb" },
	{ id = "f_coins_500", type = "coins", target = 500, reward = 500, desc = "Zarób 500 monet z ryb" },
	{ id = "f_time_25", type = "playtime", target = 25, reward = 300, desc = "Wędkuj 25 minut" },
}

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.BestCatch or 0) or 0
end

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (15 + math.min(40, (data.Stats.Caught or 0) / 2)) or 15
end

return Pack
