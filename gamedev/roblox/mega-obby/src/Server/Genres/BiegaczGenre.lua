--!strict
-- ============================================================================
-- GAMEFORGE — gatunek BIEGACZ (Server/Genres/BiegaczGenre)
-- Endless runner: długi tor generowany z seeda (ściany z lukami, przyspieszacze,
-- monety), dystans liczony na serwerze co 0,5 s, kamienie milowe co 100 studów
-- = monety. Śmierć = powrót na start, rekord zostaje w danych. Sklep: mnożnik
-- dystansu i stała prędkość. Leaderboard: rekordowy dystans.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "biegacz"
Pack.DisplayName = "Biegacz"

local TRACK_LENGTH = 1500 -- studów w jedną stronę

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function multiplierOf(data)
	return 1 + 0.1 * (data.Stats.DistanceLevel or 0)
end

local function speedOf(data)
	return 16 + (data.Stats.SpeedLevel or 0)
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "RunnerWorld"
	world.Parent = folders.World

	local rng = Random.new(424242)

	local startPad = Instance.new("SpawnLocation")
	startPad.Name = "RunnerSpawn"
	startPad.Size = Vector3.new(16, 1, 16)
	startPad.Position = Vector3.new(0, 0.5, 0)
	startPad.Anchored = true
	startPad.Neutral = true
	startPad.Duration = 0
	startPad.Color = Color3.fromRGB(34, 211, 238)
	startPad.Material = Enum.Material.Neon
	startPad.Parent = world

	-- tor: segmenty podłogi + przeszkody (ściany z luką) + przyspieszacze + monety
	local segmentLength = 20
	local segments = math.floor(TRACK_LENGTH / segmentLength)
	for index = 1, segments do
		local z = index * segmentLength
		local floor = Instance.new("Part")
		floor.Name = "TrackFloor"
		floor.Size = Vector3.new(24, 1, segmentLength)
		floor.Position = Vector3.new(0, 0, z)
		floor.Anchored = true
		floor.Color = Color3.fromRGB(56, 60, 74)
		floor.Material = Enum.Material.SmoothPlastic
		floor.Parent = world

		if index % 3 == 0 and index > 2 then
			-- ściana z luką (lewo/prawo losowo)
			local gapLeft = rng:NextNumber() < 0.5
			local wallX = gapLeft and -6.5 or 6.5
			local wall = Instance.new("Part")
			wall.Name = "Obstacle"
			wall.Size = Vector3.new(11, 7, 1.5)
			wall.Position = Vector3.new(wallX, 4.5, z + segmentLength / 2)
			wall.Anchored = true
			wall.Color = Color3.fromRGB(255, 70, 70)
			wall.Material = Enum.Material.Neon
			wall.Parent = world
		end
		if index % 5 == 0 then
			local pad = Instance.new("Part")
			pad.Name = "SpeedPad"
			pad.Size = Vector3.new(6, 0.4, 6)
			pad.Position = Vector3.new(0, 0.7, z + segmentLength / 2)
			pad.Anchored = true
			pad.Color = Color3.fromRGB(120, 255, 170)
			pad.Material = Enum.Material.Neon
			pad.Parent = world
		end
		if index % 2 == 0 then
			local coin = Instance.new("Part")
			coin.Name = "TrackCoin"
			coin.Shape = Enum.PartType.Cylinder
			coin.Size = Vector3.new(0.3, 1.8, 1.8)
			coin.CFrame = CFrame.new(rng:NextInteger(-8, 8), 2.5, z + segmentLength / 2) * CFrame.Angles(0, 0, math.rad(90))
			coin.Anchored = true
			coin.CanCollide = false
			coin.Color = Color3.fromRGB(255, 200, 40)
			coin.Material = Enum.Material.Neon
			coin:SetAttribute("Cool", false)
			coin.Parent = world
		end
	end

	return { SpawnPad = startPad, Root = world }
end

local function collectCoin(deps, player, coin)
	if coin:GetAttribute("Cool") then
		return
	end
	coin:SetAttribute("Cool", true)
	deps.PlayerDataService.AddCoins(player, 5, "moneta z trasy")
	coin.Transparency = 1
	task.delay(25, function()
		if coin.Parent then
			coin.Transparency = 0
			coin:SetAttribute("Cool", false)
		end
	end)
end

function Pack.Setup(deps, world)
	-- monety i przyspieszacze
	for _, part in ipairs(world.Root:GetChildren()) do
		if part.Name == "TrackCoin" then
			part.Touched:Connect(function(hit)
				local player = hit.Parent and Players:GetPlayerFromCharacter(hit.Parent)
				if player then
					collectCoin(deps, player, part)
				end
			end)
		elseif part.Name == "SpeedPad" then
			part.Touched:Connect(function(hit)
				local character = hit.Parent
				local player = character and Players:GetPlayerFromCharacter(character)
				local humanoid = character and character:FindFirstChildOfClass("Humanoid")
				if not (player and humanoid) then
					return
				end
				local now = os.clock()
				local last = character:GetAttribute("LastPad")
				if last and now - last < 4 then
					return
				end
				character:SetAttribute("LastPad", now)
				local data = deps.PlayerDataService.Get(player) or { Stats = {} }
				humanoid.WalkSpeed = speedOf(data) + 10
				task.delay(3, function()
					if humanoid.Parent then
						humanoid.WalkSpeed = speedOf(data)
					end
				end)
			end)
		elseif part.Name == "Obstacle" then
			part.Touched:Connect(function(hit)
				local character = hit.Parent
				local humanoid = character and character:FindFirstChildOfClass("Humanoid")
				if humanoid and humanoid.Health > 0 then
					humanoid.Health = 0 -- koniec biegu; respawn wraca na start
				end
			end)
		end
	end

	-- dystans: próbki co 0,5 s; kamień milowy co 100 studów = monety
	task.spawn(function()
		local accumulator = 0
		RunService.Heartbeat:Connect(function(dt)
			accumulator += dt
			if accumulator < 0.5 then
				return
			end
			accumulator = 0
			for _, player in ipairs(Players:GetPlayers()) do
				local data = deps.PlayerDataService.Get(player)
				local character = player.Character
				local root = character and character:FindFirstChild("HumanoidRootPart")
				if data and root and root.Position.Z > 0 then
					local distance = math.floor(root.Position.Z)
					data.Stats.CurrentDistance = distance
					if distance > (data.Stats.BestDistance or 0) then
						data.Stats.BestDistance = distance
					end
					local milestone = math.floor(distance / 100)
					if milestone > (data.Stats.LastMilestoneRun or 0) then
						data.Stats.LastMilestoneRun = milestone
						deps.PlayerDataService.AddCoins(player, math.floor(10 * multiplierOf(data)), "kamień milowy biegu")
					end
				end
			end
		end)
	end)

	-- respawn na starcie i reset licznika kamieni milowych po śmierci
	local function onCharacter(player, character)
		task.wait(0.2)
		local data = deps.PlayerDataService.Get(player)
		if data then
			data.Stats.LastMilestoneRun = 0
			data.Stats.CurrentDistance = 0
			local humanoid = character:FindFirstChildOfClass("Humanoid")
			if humanoid then
				humanoid.WalkSpeed = speedOf(data)
			end
		end
	end
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function(character)
			onCharacter(player, character)
		end)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		if player.Character then
			task.spawn(onCharacter, player, player.Character)
		end
		player.CharacterAdded:Connect(function(character)
			onCharacter(player, character)
		end)
	end

	print("[Forge] Biegacz: tor " .. TRACK_LENGTH .. " studów, przeszkody i kamienie milowe działają.")
end

function Pack.OnCharacter(deps, player, character)
	local humanoid = character:WaitForChild("Humanoid", 10)
	local data = deps.PlayerDataService.Get(player)
	if humanoid and data then
		humanoid.WalkSpeed = speedOf(data)
	end
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Dystans (ten bieg)", Value = tostring(data.Stats.CurrentDistance or 0) .. " studów" },
			{ Label = "Rekord", Value = tostring(data.Stats.BestDistance or 0) .. " studów" },
			{ Label = "Mnożnik dystansu", Value = "x" .. multiplierOf(data) },
			{ Label = "Prędkość", Value = tostring(speedOf(data)) },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local dist = data.Stats.DistanceLevel or 0
	local speed = data.Stats.SpeedLevel or 0
	return {
		{ Id = "distance_up", Name = "Mnożnik dystansu +10%", Description = "więcej z kamieni milowych (lvl " .. dist .. "/10)", Kind = "stat", Stat = "DistanceLevel", Amount = 1, Price = math.floor(400 * 1.5 ^ dist), Owned = dist >= 10 },
		{ Id = "speed_up", Name = "Stała prędkość +1", Description = "szybszy start biegu (lvl " .. speed .. "/8)", Kind = "stat", Stat = "SpeedLevel", Amount = 1, Price = math.floor(500 * 1.5 ^ speed), Owned = speed >= 8 },
	}
end

Pack.QuestPool = {
	{ id = "b_dist_1000", type = "stat:BestDistance", target = 1000, reward = 400, desc = "Pobiegnij 1000 studów w jednym biegu" },
	{ id = "b_dist_5000", type = "stat:BestDistance", target = 5000, reward = 1500, desc = "Rekord: 5000 studów" },
	{ id = "b_coins_300", type = "coins", target = 300, reward = 350, desc = "Zbierz 300 monet" },
	{ id = "b_time_20", type = "playtime", target = 20, reward = 300, desc = "Biegaj 20 minut" },
}

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.BestDistance or 0) or 0
end

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (20 + math.min(30, (data.Stats.BestDistance or 0) / 100)) or 20
end

return Pack
