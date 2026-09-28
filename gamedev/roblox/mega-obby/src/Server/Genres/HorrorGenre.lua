--!strict
-- ============================================================================
-- GAMEFORGE — gatunek HORROR (Server/Genres/HorrorGenre)
-- Ciemny budynek (siatka ścian z losowym seedem), latarka z BATERIĄ (drenuje
-- się, doładowuje na zielonych padach), 3 BEZPIECZNIKI do zebrania, drzwi
-- wyjściowe otwierają się po zebraniu wszystkich, a POTWÓR goni najbliższego
-- gracza (łap → teleport na start + strata połowy baterii). Po ucieczce
-- bezpieczniki wracają, potwór przyspiesza — pętla rozgrywki jest nieskończona.
-- Cykl dnia WYŁĄCZONY (SkipAmbient) — ciemność jest częścią rozgrywki.
-- ============================================================================

local Lighting = game:GetService("Lighting")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "horror"
Pack.DisplayName = "Horror"
Pack.SkipAmbient = true

local FUSES_TOTAL = 3
local FUSE_SPOTS = { Vector3.new(-58, 2, -58), Vector3.new(58, 2, -58), Vector3.new(58, 2, 58) }
local monsterState = { Speed = 9, Part = nil }
local rng = Random.new(20090926)

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function addWall(parent, position, size)
	local wall = Instance.new("Part")
	wall.Name = "Wall"
	wall.Size = size
	wall.Position = position
	wall.Anchored = true
	wall.Color = Color3.fromRGB(38, 40, 50)
	wall.Material = Enum.Material.Concrete
	wall.Parent = parent
	return wall
end

local function spawnFuse(parent, index, position)
	local fuse = Instance.new("Part")
	fuse.Name = "Fuse" .. index
	fuse.Size = Vector3.new(1, 2, 1)
	fuse.Position = position
	fuse.Anchored = true
	fuse.Color = Color3.fromRGB(255, 220, 60)
	fuse.Material = Enum.Material.Neon
	fuse:SetAttribute("FuseId", index)
	fuse.Parent = parent
	return fuse
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "HorrorWorld"
	world.Parent = folders.World

	local floor = Instance.new("Part")
	floor.Name = "Floor"
	floor.Size = Vector3.new(140, 2, 140)
	floor.Position = Vector3.new(0, -0.5, 0)
	floor.Anchored = true
	floor.Color = Color3.fromRGB(30, 30, 38)
	floor.Material = Enum.Material.Slate
	floor.Parent = world

	-- obwód + losowe wnętrze (deterministyczny seed — ten sam labirynt dla wszystkich)
	for _, wall in ipairs({
		{ Vector3.new(0, 5, -70), Vector3.new(140, 10, 2) },
		{ Vector3.new(0, 5, 70), Vector3.new(140, 10, 2) },
		{ Vector3.new(-70, 5, 0), Vector3.new(2, 10, 140) },
		{ Vector3.new(70, 5, 0), Vector3.new(2, 10, 140) },
	}) do
		addWall(world, wall[1], wall[2])
	end
	for index = 1, 14 do
		local horizontal = rng:NextNumber() < 0.5
		local x = rng:NextInteger(-55, 55)
		local z = rng:NextInteger(-55, 55)
		local size = horizontal and Vector3.new(rng:NextInteger(12, 24), 8, 1.5) or Vector3.new(1.5, 8, rng:NextInteger(12, 24))
		addWall(world, Vector3.new(x, 4, z), size)
		if index % 4 == 0 then
			local lamp = Instance.new("Part")
			lamp.Name = "Lamp"
			lamp.Size = Vector3.new(1.5, 0.4, 1.5)
			lamp.Position = Vector3.new(x, 9.2, z)
			lamp.Anchored = true
			lamp.Color = Color3.fromRGB(255, 220, 160)
			lamp.Material = Enum.Material.Neon
			lamp.Parent = world
			local lampLight = Instance.new("PointLight")
			lampLight.Range = 18
			lampLight.Brightness = 0.9
			lampLight.Color = Color3.fromRGB(255, 210, 150)
			lampLight.Parent = lamp
		end
	end

	for index, spot in ipairs(FUSE_SPOTS) do
		spawnFuse(world, index, spot)
	end

	local door = Instance.new("Part")
	door.Name = "ExitDoor"
	door.Size = Vector3.new(12, 9, 1.5)
	door.Position = Vector3.new(0, 4.5, -69)
	door.Anchored = true
	door.Color = Color3.fromRGB(160, 40, 40)
	door.Material = Enum.Material.Neon
	door:SetAttribute("Open", false)
	door.Parent = world

	for _, spot in ipairs({ Vector3.new(-30, 1, 30), Vector3.new(30, 1, -30) }) do
		local charger = Instance.new("Part")
		charger.Name = "BatteryCharger"
		charger.Size = Vector3.new(5, 0.8, 5)
		charger.Position = spot
		charger.Anchored = true
		charger.Color = Color3.fromRGB(120, 255, 120)
		charger.Material = Enum.Material.Neon
		charger.Parent = world
	end

	local monster = Instance.new("Part")
	monster.Name = "Monster"
	monster.Shape = Enum.PartType.Ball
	monster.Size = Vector3.new(3.4, 3.4, 3.4)
	monster.Position = Vector3.new(0, 3, 40)
	monster.Anchored = true
	monster.Color = Color3.fromRGB(200, 30, 30)
	monster.Material = Enum.Material.Neon
	monster.Parent = world
	local monsterLight = Instance.new("PointLight")
	monsterLight.Color = Color3.fromRGB(255, 40, 40)
	monsterLight.Range = 14
	monsterLight.Brightness = 1.4
	monsterLight.Parent = monster
	monsterState.Part = monster

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "HorrorSpawn"
	spawnPad.Size = Vector3.new(16, 1, 16)
	spawnPad.Position = Vector3.new(0, 1, 60)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(230, 230, 240)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world
	local safeLight = Instance.new("PointLight")
	safeLight.Range = 24
	safeLight.Brightness = 1.2
	safeLight.Parent = spawnPad

	return { SpawnPad = spawnPad, Root = world, Door = door, Monster = monster }
end

function Pack.Setup(deps, world)
	local config = knobs(deps)
	monsterState.Speed = config.MonsterSpeed or 9

	-- ciemność (zamiast cyklu dnia)
	Lighting.ClockTime = 0
	Lighting.Brightness = 0.4
	Lighting.FogEnd = 90
	Lighting.FogColor = Color3.fromRGB(8, 8, 12)

	local function batteryOf(data)
		-- bateria żyje w danych gracza (widoczna w HUD i zapisywana)
		data.Stats.Battery = data.Stats.Battery or 100
		return data.Stats.Battery
	end

	local function applyFlashlight(player, character)
		local head = character:WaitForChild("Head", 10)
		if not head then
			return
		end
		local existing = head:FindFirstChild("Flashlight")
		if existing then
			existing:Destroy()
		end
		local spot = Instance.new("SpotLight")
		spot.Name = "Flashlight"
		spot.Angle = 55
		spot.Range = 34
		spot.Brightness = 2.2
		spot.Face = Enum.NormalId.Front
		spot.Color = Color3.fromRGB(255, 240, 200)
		spot.Parent = head
	end

	local function onCharacter(character)
		local player = Players:GetPlayerFromCharacter(character)
		if player then
			applyFlashlight(player, character)
		end
	end
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(onCharacter)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		if player.Character then
			applyFlashlight(player, player.Character)
		end
		player.CharacterAdded:Connect(onCharacter)
	end

	-- bezpieczniki: zebranie (raz na grę na daną rundę — stat FuseN)
	local function armFuse(fuse)
		fuse.Touched:Connect(function(hit)
			local player = hit.Parent and Players:GetPlayerFromCharacter(hit.Parent)
			if not player or not fuse.Parent then
				return
			end
			local data = deps.PlayerDataService.Get(player)
			if not data then
				return
			end
			local key = "Fuse" .. fuse:GetAttribute("FuseId")
			if (data.Stats[key] or 0) == 0 then
				data.Stats[key] = 1
				fuse:Destroy()
				deps.EffectsService.BurstAt(fuse.Position, { 255, 220, 60 })
				local collected = 0
				for index = 1, FUSES_TOTAL do
					collected += (data.Stats["Fuse" .. index] or 0)
				end
				if collected >= FUSES_TOTAL then
					world.Door:SetAttribute("Open", true)
					world.Door.Color = Color3.fromRGB(90, 255, 120)
					deps.Net:SendTo(player, "Notify", "success", "Wszystkie bezpieczniki! DRZWI OTWARTE — UCIEKAJ!")
				else
					deps.Net:SendTo(player, "Notify", "info", "Bezpiecznik " .. collected .. "/" .. FUSES_TOTAL .. " twój.")
				end
			end
		end)
	end
	for _, child in ipairs(world.Root:GetChildren()) do
		if child.Name:sub(1, 4) == "Fuse" then
			armFuse(child)
		end
	end

	-- doładowywarki
	for _, part in ipairs(world.Root:GetChildren()) do
		if part.Name == "BatteryCharger" then
			part.Touched:Connect(function(hit)
				local player = hit.Parent and Players:GetPlayerFromCharacter(hit.Parent)
				local data = player and deps.PlayerDataService.Get(player)
				if data then
					data.Stats.Battery = 100
					deps.Net:SendTo(player, "Notify", "info", "🔋 Bateria pełna!")
				end
			end)
		end
	end

	-- drzwi wyjściowe: ucieczka = monety + reset rundy (bezpieczniki wracają)
	world.Door.Touched:Connect(function(hit)
		local player = hit.Parent and Players:GetPlayerFromCharacter(hit.Parent)
		if not player then
			return
		end
		local data = deps.PlayerDataService.Get(player)
		if not (data and world.Door:GetAttribute("Open")) then
			return
		end
		local now = os.clock()
		local last = world.Door:GetAttribute("Cooldown")
		if last and now - last < 3 then
			return
		end
		world.Door:SetAttribute("Cooldown", now)

		deps.PlayerDataService.IncrementStat(player, "Escapes", 1)
		deps.PlayerDataService.AddCoins(player, 250, "ucieczka!")
		monsterState.Speed += 1.5 -- potwór się uczy
		-- reset rundy: bezpieczniki wracają, staty fuse do zera, drzwi się zamykają
		for _, child in ipairs(world.Root:GetChildren()) do
			if child.Name:sub(1, 4) == "Fuse" then
				child:Destroy()
			end
		end
		for index, spot in ipairs(FUSE_SPOTS) do
			armFuse(spawnFuse(world.Root, index, spot))
		end
		for index = 1, FUSES_TOTAL do
			data.Stats["Fuse" .. index] = 0
		end
		world.Door:SetAttribute("Open", false)
		world.Door.Color = Color3.fromRGB(160, 40, 40)
		deps.Net:SendAll("Notify", "success", "🏃 " .. player.DisplayName
			.. " UCIEKŁ! Bezpieczniki wróciły, potwór przyspiesza ("
			.. string.format("%.0f", monsterState.Speed) .. ").")
		local character = player.Character
		if character then
			character:PivotTo(world.SpawnPad.CFrame + Vector3.new(0, 3, 0))
			character:SetAttribute("LastTeleport", os.clock())
		end
	end)

	-- pętla: bateria drenuje się, potwór goni najbliższego (poza strefą startową)
	task.spawn(function()
		local accumulator = 0
		RunService.Heartbeat:Connect(function(dt)
			accumulator += dt
			if accumulator >= 1 then
				accumulator = 0
				local drain = 1.4
				for _, player in ipairs(Players:GetPlayers()) do
					local data = deps.PlayerDataService.Get(player)
					if data then
						local maxLevel = data.Stats.BatteryMax or 0
						local current = batteryOf(data)
						data.Stats.Battery = math.max(0, current - drain * (1 - 0.2 * maxLevel))
						local character = player.Character
						local head = character and character:FindFirstChild("Head")
						local spot = head and head:FindFirstChild("Flashlight")
						if spot then
							spot.Enabled = data.Stats.Battery > 0
						end
					end
				end
			end
			local monster = monsterState.Part
			if monster and monster.Parent then
				local nearest, nearestDistance = nil, math.huge
				for _, player in ipairs(Players:GetPlayers()) do
					local character = player.Character
					local root = character and character:FindFirstChild("HumanoidRootPart")
					if root then
						local spawnDistance = (root.Position - world.SpawnPad.Position).Magnitude
						if spawnDistance > 18 then
							local distance = (root.Position - monster.Position).Magnitude
							if distance < nearestDistance then
								nearest, nearestDistance = root, distance
							end
						end
					end
				end
				if nearest then
					local direction = nearest.Position - monster.Position
					direction = Vector3.new(direction.X, 0, direction.Z)
					if direction.Magnitude > 0.1 then
						monster.Position += direction.Unit * math.min(monsterState.Speed * dt, direction.Magnitude)
					end
					if nearestDistance < 5 then
						local player = Players:GetPlayerFromCharacter(nearest.Parent)
						if player then
							local data = deps.PlayerDataService.Get(player)
							if data then
								data.Stats.Battery = (data.Stats.Battery or 100) / 2
							end
							local character = player.Character
							if character then
								character:PivotTo(world.SpawnPad.CFrame + Vector3.new(0, 3, 0))
								character:SetAttribute("LastTeleport", os.clock())
							end
							deps.Net:SendTo(player, "Notify", "error", "👹 DOPADŁ CIĘ! Bateria w połowie — spróbuj jeszcze raz.")
						end
					end
				end
			end
		end)
	end)

	print("[Forge] Horror: ciemność, latarki, bezpieczniki i potwór działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local collected = 0
	for index = 1, FUSES_TOTAL do
		collected += (data.Stats["Fuse" .. index] or 0)
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Bezpieczniki", Value = collected .. "/" .. FUSES_TOTAL },
			{ Label = "Bateria", Value = math.floor(data.Stats.Battery or 100) .. "%" },
			{ Label = "Ucieczki", Value = tostring(data.Stats.Escapes or 0) },
			{ Label = "Potwór", Value = string.format("%.0f", monsterState.Speed) .. " prędkości" },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local level = data.Stats.BatteryMax or 0
	return {
		{ Id = "battery_max", Name = "Większa bateria", Description = "drenuje się wolniej o 20% (lvl " .. level .. "/5)", Kind = "stat", Stat = "BatteryMax", Amount = 1, Price = math.floor(600 * 1.6 ^ level), Owned = level >= 5 },
		{ Id = "monster_slow", Name = "Zamrożenie potwora", Description = "potwór o 1 prędkości wolniejszy (na zawsze)", Kind = "special", Price = 2500, Owned = (data.Stats.MonsterSlowed or 0) > 0 },
	}
end

function Pack.OnBuy(deps, player, item)
	if item.Id == "monster_slow" then
		local data = deps.PlayerDataService.Get(player)
		if data then
			data.Stats.MonsterSlowed = 1
		end
		monsterState.Speed = math.max(4, monsterState.Speed - 1)
		return true, "Potwór zwolnił!"
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "h_escape_1", type = "stat:Escapes", target = 1, reward = 400, desc = "Uciekaj z budynku" },
	{ id = "h_escape_3", type = "stat:Escapes", target = 3, reward = 1200, desc = "Ucieknij 3 razy" },
	{ id = "h_coins_300", type = "coins", target = 300, reward = 400, desc = "Zbierz 300 monet" },
	{ id = "h_time_20", type = "playtime", target = 20, reward = 350, desc = "Przetrwaj 20 minut" },
}

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (20 + 5 * math.min(6, data.Stats.Escapes or 0)) or 20
end

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Escapes or 0) or 0
end

return Pack
