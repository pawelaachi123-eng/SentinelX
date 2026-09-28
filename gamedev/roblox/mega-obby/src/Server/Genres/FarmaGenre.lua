--!strict
-- ============================================================================
-- GAMEFORGE — gatunek FARMA (Server/Genres/FarmaGenre)
-- Ziemia z działkami: bierzesz nasiona ze skrzyni, sadzisz na wolnej działce,
-- uprawa rośnie w czasie serwera, zbierasz gotowy plon → monety + nasiono
-- wraca (pętla się kręci). Sklep: szybszy wzrost, wartość plonu, więcej
-- działek. Leaderboard: zebrane plony.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "farma"
Pack.DisplayName = "Farma"

local plots = {} -- [plotId] = { Part, State = "empty"/"growing"/"ready", ReadyAt, Crop }

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function growSecondsOf(data)
	return math.max(8, 30 * (0.9 ^ (data.Stats.GrowthLevel or 0)))
end

local function cropValueOf(data)
	return 12 + (data.Stats.ValueLevel or 0)
end

local function refreshPlotVisual(state)
	local part = state.Part
	local crop = state.Crop
	if state.State == "empty" then
		part.Color = Color3.fromRGB(96, 70, 48)
		part.Material = Enum.Material.Ground
		if crop then
			crop:Destroy()
			state.Crop = nil
		end
	elseif state.State == "growing" then
		part.Color = Color3.fromRGB(70, 90, 50)
		if crop then
			crop:Destroy()
		end
		local sprout = Instance.new("Part")
		sprout.Name = "Sprout"
		sprout.Size = Vector3.new(0.8, 0.8, 0.8)
		sprout.Position = part.Position + Vector3.new(0, 1, 0)
		sprout.Anchored = true
		sprout.CanCollide = false
		sprout.Color = Color3.fromRGB(120, 180, 80)
		sprout.Material = Enum.Material.Grass
		sprout.Parent = part.Parent
		state.Crop = sprout
	elseif state.State == "ready" then
		part.Color = Color3.fromRGB(60, 140, 60)
		if crop then
			crop:Destroy()
		end
		local ripe = Instance.new("Part")
		ripe.Name = "RipeCrop"
		ripe.Size = Vector3.new(1.6, 1.6, 1.6)
		ripe.Position = part.Position + Vector3.new(0, 1.4, 0)
		ripe.Anchored = true
		ripe.CanCollide = false
		ripe.Color = Color3.fromRGB(255, 170, 60)
		ripe.Material = Enum.Material.Neon
		ripe.Parent = part.Parent
		state.Crop = ripe
	end
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "FarmWorld"
	world.Parent = folders.World

	local ground = Instance.new("Part")
	ground.Name = "FarmGround"
	ground.Size = Vector3.new(120, 2, 120)
	ground.Position = Vector3.new(0, -0.5, 0)
	ground.Anchored = true
	ground.Color = Color3.fromRGB(74, 110, 60)
	ground.Material = Enum.Material.Grass
	ground.Parent = world

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "FarmSpawn"
	spawnPad.Size = Vector3.new(12, 1, 12)
	spawnPad.Position = Vector3.new(0, 1.5, 42)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(200, 160, 90)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	-- skrzynia z nasionami
	local crate = Instance.new("Part")
	crate.Name = "SeedCrate"
	crate.Size = Vector3.new(5, 4, 5)
	crate.Position = Vector3.new(-14, 2, 36)
	crate.Anchored = true
	crate.Color = Color3.fromRGB(150, 110, 60)
	crate.Material = Enum.Material.WoodPlanks
	crate.Parent = world
	local crateSign = Instance.new("BillboardGui")
	crateSign.Size = UDim2.fromScale(8, 2)
	crateSign.StudsOffset = Vector3.new(0, 4, 0)
	crateSign.MaxDistance = 80
	local crateLabel = Instance.new("TextLabel")
	crateLabel.Size = UDim2.fromScale(1, 1)
	crateLabel.BackgroundTransparency = 1
	crateLabel.Font = Enum.Font.GothamBold
	crateLabel.TextScaled = true
	crateLabel.TextColor3 = Color3.fromRGB(255, 240, 200)
	crateLabel.TextStrokeTransparency = 0.4
	crateLabel.Text = "🌱 SKRZYNIA NASION\ndotknij, gdy masz mniej niż 10"
	crateLabel.Parent = crateSign
	crateSign.Parent = crate

	-- 12 działek (4 odblokowywane w sklepie: atrybut Premium)
	local plotId = 0
	for row = 0, 2 do
		for column = 0, 3 do
			plotId += 1
			local plot = Instance.new("Part")
			plot.Name = "Plot" .. plotId
			plot.Size = Vector3.new(10, 0.6, 10)
			plot.Position = Vector3.new(-18 + column * 12, 0.5, 6 + row * 12)
			plot.Anchored = true
			plot.Color = Color3.fromRGB(96, 70, 48)
			plot.Material = Enum.Material.Ground
			plot:SetAttribute("PlotId", plotId)
			plot:SetAttribute("Premium", plotId > 8)
			plot.Parent = world
			plots[plotId] = { Part = plot, State = "empty", ReadyAt = 0, Crop = nil }
		end
	end

	return { SpawnPad = spawnPad, Root = world }
end

function Pack.Setup(deps, world)
	local config = knobs(deps)
	local plotCost = config.PlotCost or 800

	-- skrzynia: dokup nasiona do 10
	world.Root.SeedCrate.Touched:Connect(function(hit)
		local player = hit.Parent and Players:GetPlayerFromCharacter(hit.Parent)
		local data = player and deps.PlayerDataService.Get(player)
		if data and (data.Stats.Seeds or 0) < 10 then
			data.Stats.Seeds = math.min(10, (data.Stats.Seeds or 0) + 5)
			deps.Net:SendTo(player, "Notify", "info", "🌱 Masz " .. data.Stats.Seeds .. "/10 nasion — sadź na działkach!")
		end
	end)

	-- działki: sadzenie i zbiór
	for _, state in pairs(plots) do
		state.Part.Touched:Connect(function(hit)
			local character = hit.Parent
			local player = character and Players:GetPlayerFromCharacter(character)
			local data = player and deps.PlayerDataService.Get(player)
			if not data then
				return
			end
			if state.State == "empty" then
				if state.Part:GetAttribute("Premium") and (data.Stats.ExtraPlots or 0) == 0 then
					return -- działka premium: odblokowanie w sklepie
				end
				if (data.Stats.Seeds or 0) <= 0 then
					deps.Net:SendTo(player, "Notify", "error", "Nie masz nasion — dotknij skrzyni po lewej.")
					return
				end
				data.Stats.Seeds -= 1
				state.State = "growing"
				state.ReadyAt = os.time() + math.floor(growSecondsOf(data))
				refreshPlotVisual(state)
			elseif state.State == "ready" then
				state.State = "empty"
				refreshPlotVisual(state)
				data.Stats.Seeds = (data.Stats.Seeds or 0) + 1 -- nasiono wraca
				data.Stats.Harvested = (data.Stats.Harvested or 0) + 1
				deps.PlayerDataService.AddCoins(player, cropValueOf(data), "plon")
				deps.EffectsService.BurstAt(state.Part.Position + Vector3.new(0, 2, 0), { 255, 170, 60 })
			end
		end)
	end

	-- wzrost: pętla serwera odhacza gotowe uprawy
	task.spawn(function()
		while true do
			task.wait(1)
			local now = os.time()
			for _, state in pairs(plots) do
				if state.State == "growing" and now >= state.ReadyAt then
					state.State = "ready"
					refreshPlotVisual(state)
				end
			end
		end
	end)

	print("[Forge] Farma: 12 działek (8 startowych), skrzynia nasion i wzrost działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local ready = 0
	for _, state in pairs(plots) do
		if state.State == "ready" then
			ready += 1
		end
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Nasiona", Value = (data.Stats.Seeds or 0) .. "/10" },
			{ Label = "Gotowe do zbioru", Value = tostring(ready) },
			{ Label = "Wzrost", Value = growSecondsOf(data) .. " s" },
			{ Label = "Wartość plonu", Value = cropValueOf(data) .. " 🪙" },
			{ Label = "Zebrane plony", Value = tostring(data.Stats.Harvested or 0) },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local growth = data.Stats.GrowthLevel or 0
	local value = data.Stats.ValueLevel or 0
	return {
		{ Id = "growth_up", Name = "Szybszy wzrost -10%", Description = "plon gotowy szybciej (lvl " .. growth .. "/8)", Kind = "stat", Stat = "GrowthLevel", Amount = 1, Price = math.floor(350 * 1.5 ^ growth), Owned = growth >= 8 },
		{ Id = "value_up", Name = "Wartość plonu +1", Description = "więcej monet za zbiór (lvl " .. value .. "/15)", Kind = "stat", Stat = "ValueLevel", Amount = 1, Price = math.floor(450 * 1.45 ^ value), Owned = value >= 15 },
		{ Id = "extra_plots", Name = "4 działki premium", Description = "odblokowuje górny rząd działek", Kind = "special", Price = plotCost, Owned = (data.Stats.ExtraPlots or 0) > 0 },
	}
end

function Pack.OnBuy(deps, player, item)
	if item.Id == "extra_plots" then
		local data = deps.PlayerDataService.Get(player)
		if data then
			data.Stats.ExtraPlots = 1
		end
		return true, "Działki premium odblokowane!"
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "f_harvest_15", type = "stat:Harvested", target = 15, reward = 350, desc = "Zbierz 15 plonów" },
	{ id = "f_harvest_60", type = "stat:Harvested", target = 60, reward = 1100, desc = "Zbierz 60 plonów" },
	{ id = "f_coins_400", type = "coins", target = 400, reward = 450, desc = "Zarób 400 monet z upraw" },
	{ id = "f_time_25", type = "playtime", target = 25, reward = 300, desc = "Prowadź farmę 25 minut" },
}

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Harvested or 0) or 0
end

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (15 + (data.Stats.Harvested or 0) / 2) or 15
end

return Pack
