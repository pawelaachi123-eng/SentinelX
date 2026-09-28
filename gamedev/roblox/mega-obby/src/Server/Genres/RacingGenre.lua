--!strict
-- ============================================================================
-- GAMEFORGE — gatunek WYŚCIGI (Server/Genres/RacingGenre)
-- Tor owalny (segmenty na elipsie), 8 checkpointów w kolejności, okrążenie =
-- nagroda + licznik. Doładowania prędkości na prostych (tymczasowy boost
-- WalkSpeed — anty-cheat i tak skaluje próg z bieżącą prędkością), sklep:
-- stała prędkość (OnCharacter ustawia po respawnie). Wszystko serwerowe.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "wyscigi"
Pack.DisplayName = "Wyścigi"

local CHECKPOINT_COUNT = 8
local TRACK_A, TRACK_B = 62, 42 -- promienie elipsy (studs)
local lapState = {} -- [userId] = { Next = 1..8 }

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function speedLevelOf(data)
	return data.Stats.SpeedLevel or 0
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "RacingWorld"
	world.Parent = folders.World

	local ground = Instance.new("Part")
	ground.Name = "Ground"
	ground.Size = Vector3.new(200, 2, 160)
	ground.Position = Vector3.new(0, -0.5, 0)
	ground.Anchored = true
	ground.Color = Color3.fromRGB(50, 90, 56)
	ground.Material = Enum.Material.Grass
	ground.Parent = world

	-- tor: segmenty wzdłuż elipsy
	local segments = 56
	for index = 0, segments - 1 do
		local angle = (index / segments) * math.pi * 2
		local x = math.cos(angle) * TRACK_A
		local z = math.sin(angle) * TRACK_B
		local segment = Instance.new("Part")
		segment.Name = "Track"
		segment.Size = Vector3.new(9, 1, 7.2)
		segment.CFrame = CFrame.new(x, 0.5, z) * CFrame.Angles(0, -angle, 0)
		segment.Anchored = true
		segment.Color = Color3.fromRGB(56, 58, 68)
		segment.Material = Enum.Material.Asphalt
		segment.Parent = world
	end

	-- linia mety + checkpointy
	for index = 1, CHECKPOINT_COUNT do
		local angle = ((index - 1) / CHECKPOINT_COUNT) * math.pi * 2
		local x = math.cos(angle) * TRACK_A
		local z = math.sin(angle) * TRACK_B
		local checkpoint = Instance.new("Part")
		checkpoint.Name = "Checkpoint" .. index
		checkpoint.Size = Vector3.new(10, 8, 1.2)
		checkpoint.CFrame = CFrame.new(x, 4, z) * CFrame.Angles(0, -angle + math.pi / 2, 0)
		checkpoint.Anchored = true
		checkpoint.CanCollide = false
		checkpoint.Transparency = 0.7
		checkpoint.Color = index == 1 and Color3.fromRGB(255, 215, 0) or Color3.fromRGB(34, 211, 238)
		checkpoint.Material = Enum.Material.Neon
		checkpoint:SetAttribute("CheckpointIndex", index)
		checkpoint.Parent = world

		local billboard = Instance.new("BillboardGui")
		billboard.Size = UDim2.fromScale(6, 1.6)
		billboard.StudsOffset = Vector3.new(0, 6, 0)
		billboard.MaxDistance = 120
		local label = Instance.new("TextLabel")
		label.Size = UDim2.fromScale(1, 1)
		label.BackgroundTransparency = 1
		label.Font = Enum.Font.GothamBold
		label.TextScaled = true
		label.TextColor3 = Color3.fromRGB(240, 244, 255)
		label.TextStrokeTransparency = 0.4
		label.Text = index == 1 and "META / START" or ("CP " .. index)
		label.Parent = billboard
		billboard.Parent = checkpoint
	end

	-- doładowania prędkości na prostych
	for _, spot in ipairs({ Vector3.new(0, 1, TRACK_B), Vector3.new(0, 1, -TRACK_B) }) do
		local boost = Instance.new("Part")
		boost.Name = "SpeedPad"
		boost.Size = Vector3.new(10, 0.5, 6)
		boost.Position = spot
		boost.Anchored = true
		boost.Color = Color3.fromRGB(120, 255, 170)
		boost.Material = Enum.Material.Neon
		boost.Parent = world
	end

	-- dołek po środku (bezpieczna strefa)
	local infield = Instance.new("Part")
	infield.Name = "Infield"
	infield.Size = Vector3.new(24, 1, 14)
	infield.Position = Vector3.new(0, 0.5, 0)
	infield.Anchored = true
	infield.Color = Color3.fromRGB(46, 52, 66)
	infield.Material = Enum.Material.SmoothPlastic
	infield.Parent = world

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "RacingSpawn"
	spawnPad.Size = Vector3.new(12, 1, 12)
	spawnPad.Position = Vector3.new(0, 1.5, 0)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(255, 215, 0)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	return { SpawnPad = spawnPad, Root = world }
end

local function applySpeed(deps, player, character)
	local humanoid = character:WaitForChild("Humanoid", 10)
	local data = deps.PlayerDataService.Get(player)
	if humanoid and data then
		humanoid.WalkSpeed = 16 + 2 * speedLevelOf(data)
	end
end

function Pack.Setup(deps, world)
	local config = knobs(deps)
	local lapReward = config.LapReward or 100

	-- checkpointy w kolejności (serwer pilnuje sekwencji — nie da się „przeskoczyć”)
	for _, checkpoint in ipairs(world.Root:GetChildren()) do
		if checkpoint.Name:sub(1, 10) == "Checkpoint" then
			checkpoint.Touched:Connect(function(hit)
				local character = hit.Parent
				local player = character and Players:GetPlayerFromCharacter(character)
				if not player then
					return
				end
				local data = deps.PlayerDataService.Get(player)
				if not data then
					return
				end
				local state = lapState[player.UserId]
				if not state then
					state = { Next = 1 }
					lapState[player.UserId] = state
				end
				local index = checkpoint:GetAttribute("CheckpointIndex")
				if index == state.Next then
					if index == CHECKPOINT_COUNT then
						state.Next = 1
						deps.PlayerDataService.IncrementStat(player, "Laps", 1)
						deps.PlayerDataService.AddCoins(player, lapReward, "okrążenie!")
						deps.EffectsService.BurstAt(checkpoint.Position, { 255, 215, 0 })
						deps.Net:SendTo(player, "Notify", "success", "🏁 OKRĄŻENIE #"
							.. (data.Stats.Laps or 0) .. "! +" .. lapReward .. " monet.")
					else
						state.Next = index + 1
						deps.Net:SendTo(player, "Notify", "info", "CP " .. index .. "/" .. (CHECKPOINT_COUNT - 1) .. " — dalej!")
					end
				end
			end)
		end
	end

	-- doładowania: tymczasowy boost (4 s), spójny z anty-cheatem (skaluje z WalkSpeed)
	for _, pad in ipairs(world.Root:GetChildren()) do
		if pad.Name == "SpeedPad" then
			pad.Touched:Connect(function(hit)
				local character = hit.Parent
				local player = character and Players:GetPlayerFromCharacter(character)
				local humanoid = character and character:FindFirstChildOfClass("Humanoid")
				if not (player and humanoid) then
					return
				end
				local now = os.clock()
				local last = character:GetAttribute("LastBoost") or 0
				if now - last < 3 then
					return
				end
				character:SetAttribute("LastBoost", now)
				local base = 16 + 2 * speedLevelOf(deps.PlayerDataService.Get(player) or { Stats = {} })
				humanoid.WalkSpeed = base + 14
				task.delay(4, function()
					if humanoid.Parent then
						humanoid.WalkSpeed = base
					end
				end)
				deps.EffectsService.BurstAt(pad.Position, { 120, 255, 170 })
			end)
		end
	end

	-- stała prędkość po każdym respawnie (ulepszenie ze sklepu)
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function(character)
			task.wait(0.2)
			applySpeed(deps, player, character)
		end)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		if player.Character then
			applySpeed(deps, player, player.Character)
		end
		player.CharacterAdded:Connect(function(character)
			task.wait(0.2)
			applySpeed(deps, player, character)
		end)
	end

	Players.PlayerRemoving:Connect(function(player)
		lapState[player.UserId] = nil
	end)

	print("[Forge] Wyścigi: tor, checkpointy i doładowania działają.")
end

function Pack.OnCharacter(deps, player, character)
	applySpeed(deps, player, character)
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local state = lapState[player.UserId]
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Okrążenia", Value = tostring(data.Stats.Laps or 0) },
			{ Label = "Następny CP", Value = state and (state.Next .. "/" .. CHECKPOINT_COUNT) or "1/" .. CHECKPOINT_COUNT },
			{ Label = "Prędkość", Value = (16 + 2 * speedLevelOf(data)) .. " (lvl " .. speedLevelOf(data) .. ")" },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local level = speedLevelOf(data)
	return {
		{ Id = "speed_level", Name = "Stała prędkość +2", Description = "zawsze szybszy (lvl " .. level .. "/10)", Kind = "stat", Stat = "SpeedLevel", Amount = 1, Price = math.floor(500 * 1.55 ^ level), Owned = level >= 10 },
		{ Id = "turbo_pack", Name = "Dłuższe turbo", Description = "doładowanie działa 6 s zamiast 4 (na zawsze)", Kind = "special", Price = 2200, Owned = (data.Stats.LongBoost or 0) > 0 },
	}
end

function Pack.OnBuy(deps, player, item)
	if item.Id == "turbo_pack" then
		local data = deps.PlayerDataService.Get(player)
		if data then
			data.Stats.LongBoost = 1
		end
		return true, "Turbo wydłużone!"
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "r_laps_3", type = "stat:Laps", target = 3, reward = 400, desc = "Zalicz 3 okrążenia" },
	{ id = "r_laps_10", type = "stat:Laps", target = 10, reward = 1500, desc = "Zalicz 10 okrążeń" },
	{ id = "r_coins_300", type = "coins", target = 300, reward = 400, desc = "Zbierz 300 monet" },
	{ id = "r_time_20", type = "playtime", target = 20, reward = 300, desc = "Ścigaj się 20 minut" },
}

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (30 + 2 * math.min(20, data.Stats.Laps or 0)) or 30
end

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Laps or 0) or 0
end

return Pack
