--!strict
-- ============================================================================
-- GAMEFORGE — gatunek SYMULATOR (Server/Genres/SimulatorGenre)
-- Klikaj → siła rośnie → odblokowujesz strefy treningowe (aura monet) →
-- kupujesz automatyczne treningi → NARODZINY resetują siłę za stały mnożnik.
-- Świat: plac + 3 strefy z progami siły. Wszystko serwerowe (klik = remote
-- z limitem częstości, strefy liczone na serwerze).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")
local Workspace = game:GetService("Workspace")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "symulator"
Pack.DisplayName = "Symulator"

local function knobs(deps)
	local spec = deps.GameSpec
	return (spec.Knobs and spec.Knobs[Pack.Id]) or {}
end

local ZONES = { { Need = 25, Label = "Strefa Nowicjusza", Offset = -34 }, { Need = 150, Label = "Strefa Wojownika", Offset = 0 }, { Need = 1000, Label = "Strefa Mistrza", Offset = 34 } }

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "SymulatorWorld"
	world.Parent = folders.World

	local plaza = Instance.new("Part")
	plaza.Name = "Plaza"
	plaza.Size = Vector3.new(90, 2, 60)
	plaza.Position = Vector3.new(0, 0, 0)
	plaza.Anchored = true
	plaza.Color = Color3.fromRGB(60, 66, 82)
	plaza.Material = Enum.Material.SmoothPlastic
	plaza.TopSurface = Enum.SurfaceType.Smooth
	plaza.Parent = world

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "SimSpawn"
	spawnPad.Size = Vector3.new(12, 1, 12)
	spawnPad.Position = Vector3.new(0, 1.5, 18)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(34, 211, 238)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	for index, zone in ipairs(ZONES) do
		local pad = Instance.new("Part")
		pad.Name = "Zone" .. index
		pad.Size = Vector3.new(20, 0.6, 20)
		pad.Position = Vector3.new(zone.Offset, 1.3, -12)
		pad.Anchored = true
		pad.CanCollide = false
		pad.Color = Color3.fromRGB(120, 255, 170)
		pad.Material = Enum.Material.Neon
		pad.Transparency = 0.35
		pad:SetAttribute("ZoneNeed", zone.Need)
		pad.Parent = world

		local billboard = Instance.new("BillboardGui")
		billboard.Size = UDim2.fromScale(10, 2.2)
		billboard.StudsOffset = Vector3.new(0, 5, 0)
		billboard.MaxDistance = 120
		local label = Instance.new("TextLabel")
		label.Size = UDim2.fromScale(1, 1)
		label.BackgroundTransparency = 1
		label.Font = Enum.Font.GothamBold
		label.TextScaled = true
		label.TextColor3 = Color3.fromRGB(240, 244, 255)
		label.TextStrokeTransparency = 0.4
		label.Text = zone.Label .. "\nWYMAGA " .. zone.Need .. " SIŁY · +2 monety/s"
		label.Parent = billboard
		billboard.Parent = pad
	end

	return { SpawnPad = spawnPad, Root = world }
end

local function clickPowerOf(data)
	return 1 + (data.Stats.ClickPower or 0)
end

local function multiplierOf(data)
	return 1 + 0.5 * (data.Stats.Rebirths or 0)
end

local function grantStrength(deps, player, data, amount)
	data.Stats.Strength = (data.Stats.Strength or 0) + amount
	deps.Net:SendTo(player, "StatChanged", "Strength", data.Stats.Strength)
	-- kamienie milowe siły → monety (co 100)
	local milestone = math.floor(data.Stats.Strength / 100)
	if milestone > (data.Stats.LastMilestone or 0) then
		data.Stats.LastMilestone = milestone
		deps.PlayerDataService.AddCoins(player, 10 * multiplierOf(data), "kamień milowy siły")
	end
end

function Pack.Setup(deps, world)
	local config = knobs(deps)
	local rebirthBase = config.RebirthBase or 1000

	-- klik / trening: remote z limitem (serwer liczy siłę, klient nigdy nie zgłasza wartości)
	deps.Net:OnServer("Train", function(player)
		local data = deps.PlayerDataService.Get(player)
		if not data then
			return false, "Dane się wczytują."
		end
		grantStrength(deps, player, data, clickPowerOf(data) * multiplierOf(data))
		return true, ""
	end, { 4, 8 })

	-- pętla: automatyczne treningi + aury stref (serwer, co 1 s)
	task.spawn(function()
		local accumulator = 0
		RunService.Heartbeat:Connect(function(dt)
			accumulator += dt
			if accumulator < 1 then
				return
			end
			accumulator = 0
			for _, player in ipairs(Players:GetPlayers()) do
				local data = deps.PlayerDataService.Get(player)
				local character = player.Character
				local root = character and character:FindFirstChild("HumanoidRootPart")
				if data and root then
					local auto = data.Stats.AutoLevel or 0
					if auto > 0 then
						grantStrength(deps, player, data, auto * 2 * multiplierOf(data))
					end
					for _, pad in ipairs(world.Root:GetChildren()) do
						local need = pad:GetAttribute("ZoneNeed")
						if type(need) == "number" and (data.Stats.Strength or 0) >= need then
							if (root.Position - pad.Position).Magnitude < 14 then
								deps.PlayerDataService.AddCoins(player, 2, "strefa treningowa")
							end
						end
					end
				end
			end
		end)
	end)

	print("[Forge] Symulator: klik, strefy i automatyczne treningi działają.")
end

function Pack.OnBuy(deps, player, item)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane się wczytują."
	end
	if item.Id == "rebirth" then
		data.Stats.Rebirths = (data.Stats.Rebirths or 0) + 1
		data.Stats.Strength = 0
		data.Stats.LastMilestone = 0
		deps.Net:SendTo(player, "Notify", "success",
			"🌟 NARODZINY #" .. data.Stats.Rebirths .. "! Mnożnik siły: ×" .. multiplierOf(data))
		return true, "Mnożnik ×" .. multiplierOf(data)
	end
	if item.Id == "rebirth_free" then -- produkt deweloperski
		return Pack.OnBuy(deps, player, { Id = "rebirth", Name = "Narodziny (premium)" })
	end
	return true, ""
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Siła", Value = deps.Util.Format.Short(data.Stats.Strength or 0) },
			{ Label = "Siła kliku", Value = clickPowerOf(data) * multiplierOf(data) },
			{ Label = "Auto-trening", Value = (data.Stats.AutoLevel or 0) .. " lvl (×" .. ((data.Stats.AutoLevel or 0) * 2) .. "/s)" },
			{ Label = "Narodziny", Value = (data.Stats.Rebirths or 0) .. " (×" .. multiplierOf(data) .. ")" },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local clickLevel = data.Stats.ClickPower or 0
	local autoLevel = data.Stats.AutoLevel or 0
	local rebirthCost = rebirthBase * (2 ^ (data.Stats.Rebirths or 0))
	return {
		{ Id = "click_power", Name = "Siła kliku +1", Description = "klikaj mocniej (lvl " .. clickLevel .. "/25)", Kind = "stat", Stat = "ClickPower", Amount = 1, Price = math.floor(150 * 1.5 ^ clickLevel), Owned = clickLevel >= 25 },
		{ Id = "auto_trainer", Name = "Automatyczny trener", Description = "+2 siły/s za poziom (lvl " .. autoLevel .. "/12)", Kind = "stat", Stat = "AutoLevel", Amount = 1, Price = math.floor(400 * 1.6 ^ autoLevel), Owned = autoLevel >= 12 },
		{ Id = "rebirth", Name = "🌟 NARODZINY", Description = "reset siły, PERMANENTNY mnożnik +50%", Kind = "special", Price = rebirthCost, Owned = false },
	}
end

Pack.QuestPool = {
	{ id = "s_str_500", type = "stat:Strength", target = 500, reward = 300, desc = "Zdobądź 500 siły" },
	{ id = "s_str_2500", type = "stat:Strength", target = 2500, reward = 900, desc = "Zdobądź 2 500 siły" },
	{ id = "s_str_10000", type = "stat:Strength", target = 10000, reward = 2500, desc = "Zdobądź 10 000 siły" },
	{ id = "s_coins_200", type = "coins", target = 200, reward = 350, desc = "Zbierz 200 monet" },
	{ id = "s_coins_600", type = "coins", target = 600, reward = 900, desc = "Zbierz 600 monet" },
	{ id = "s_time_20", type = "playtime", target = 20, reward = 250, desc = "Trenuj 20 minut" },
}

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	local multiplier = data and (1 + 0.25 * (data.Stats.AutoLevel or 0)) or 1
	return 25 * multiplier
end

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Strength or 0) or 0
end

function Pack.MonetizationGrants(deps)
	return {
		rebirth_free = function(player)
			return Pack.OnBuy(deps, player, { Id = "rebirth", Name = "Narodziny (premium)" })
		end,
	}
end

return Pack
