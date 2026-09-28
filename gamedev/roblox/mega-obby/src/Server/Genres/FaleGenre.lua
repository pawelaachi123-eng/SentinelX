--!strict
-- ============================================================================
-- GAMEFORGE — gatunek FALE / PRZETRWANIE (Server/Genres/FaleGenre)
-- Arena z KRYSZTAŁEM w centrum: wrogów przybywa w falach (coraz szybsi i
-- twardsi), gracze ich ZABLIJAJĄ dotykiem (obrażenia z ulepszeń, cooldown na
-- wrogu), wrogi docierające do kryształu odbierają mu HP. Upadek kryształu =
-- koniec rundy (bonus za falę, powrót do 1, kryształ odrasta). Sklep:
-- obrażenia, pancerz kryształu, spowolnienie wrogów. Leaderboard: najlepsza fala.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "fale"
Pack.DisplayName = "Przetrwanie Fal"

local arenaState = {
	Wave = 0,
	CrystalHP = 20,
	MaxCrystalHP = 20,
	BreakUntil = 0,
	Pending = 0,
	Running = false,
}

local enemies = {} -- [enemyPart] = true

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function damageOf(data)
	return 1 + (data.Stats.DamageLevel or 0)
end

local function enemySpeedOf(data)
	local slow = 0.05 * (data.Stats.SlowLevel or 0)
	return (4 + arenaState.Wave * 0.4) * (1 - slow)
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "WaveWorld"
	world.Parent = folders.World

	local arena = Instance.new("Part")
	arena.Name = "ArenaFloor"
	arena.Shape = Enum.PartType.Cylinder
	arena.Size = Vector3.new(2, 140, 140)
	arena.CFrame = CFrame.new(0, 0, 0) * CFrame.Angles(0, 0, math.rad(90))
	arena.Anchored = true
	arena.Color = Color3.fromRGB(52, 56, 68)
	arena.Material = Enum.Material.Slate
	arena.Parent = world

	local enemiesFolder = Instance.new("Folder")
	enemiesFolder.Name = "Enemies"
	enemiesFolder.Parent = world

	local crystal = Instance.new("Part")
	crystal.Name = "Crystal"
	crystal.Size = Vector3.new(5, 8, 5)
	crystal.Position = Vector3.new(0, 5, 0)
	crystal.Anchored = true
	crystal.Color = Color3.fromRGB(120, 240, 230)
	crystal.Material = Enum.Material.ForceField
	crystal.Parent = world
	local crystalLight = Instance.new("PointLight")
	crystalLight.Color = Color3.fromRGB(120, 240, 230)
	crystalLight.Range = 26
	crystalLight.Brightness = 1.4
	crystalLight.Parent = crystal

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "WaveSpawn"
	spawnPad.Size = Vector3.new(14, 1, 14)
	spawnPad.Position = Vector3.new(0, 1.5, 48)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(34, 211, 238)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	return { SpawnPad = spawnPad, Root = world, Crystal = crystal, Enemies = enemiesFolder }
end

local function damageEnemy(deps, world, enemy, amount)
	local hp = (enemy:GetAttribute("HP") or 1) - amount
	if hp <= 0 then
		local player = enemy:GetAttribute("LastHitter")
		local hitter = player and Players:GetPlayerByUserId(player)
		if hitter then
			deps.PlayerDataService.AddCoins(hitter, 8 + arenaState.Wave, "pokonany wróg")
			deps.PlayerDataService.IncrementStat(hitter, "Kills", 1)
			deps.EffectsService.BurstAt(enemy.Position, { 255, 80, 80 })
		end
		enemies[enemy] = nil
		enemy:Destroy()
	else
		enemy:SetAttribute("HP", hp)
		enemy.Size = Vector3.new(2 + hp * 0.3, 2 + hp * 0.3, 2 + hp * 0.3)
	end
end

local function spawnEnemy(deps, world)
	local angle = math.random() * math.pi * 2
	local radius = 58
	local enemy = Instance.new("Part")
	enemy.Name = "Enemy"
	enemy.Shape = Enum.PartType.Ball
	enemy.Size = Vector3.new(2.6, 2.6, 2.6)
	enemy.Position = Vector3.new(math.cos(angle) * radius, 3, math.sin(angle) * radius)
	enemy.Anchored = true
	enemy.Color = Color3.fromRGB(255, 60 + math.min(120, arenaState.Wave * 6), 60)
	enemy.Material = Enum.Material.Neon
	enemy:SetAttribute("HP", 3 + arenaState.Wave)
	enemy:SetAttribute("LastHit", 0)
	enemy.Parent = world.Enemies
	enemies[enemy] = true
	arenaState.Pending += 1

	enemy.Touched:Connect(function(hit)
		local character = hit.Parent
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local now = os.clock()
		if now - (enemy:GetAttribute("LastHit") or 0) < 0.4 then
			return
		end
		enemy:SetAttribute("LastHit", now)
		enemy:SetAttribute("LastHitter", player.UserId)
		local data = deps.PlayerDataService.Get(player)
		if data then
			damageEnemy(deps, world, enemy, damageOf(data))
		end
	end)
	return enemy
end

local function startWave(deps, world)
	arenaState.Wave += 1
	arenaState.Running = true
	local count = 2 + arenaState.Wave
	deps.Net:SendAll("Notify", "info", "⚔️ FALA " .. arenaState.Wave .. "! Wrogów: " .. count .. ". Broń kryształu!")
	for index = 1, count do
		task.delay(index * 0.8, function()
			if arenaState.Running then
				spawnEnemy(deps, world)
				arenaState.Pending -= 1
			end
		end)
	end
end

local function endRun(deps, world)
	arenaState.Running = false
	for _, player in ipairs(Players:GetPlayers()) do
		local data = deps.PlayerDataService.Get(player)
		if data then
			local best = math.max(arenaState.Wave - 1, 0)
			if best > (data.Stats.BestWave or 0) then
				data.Stats.BestWave = best
			end
			if best > 0 then
				deps.PlayerDataService.AddCoins(player, best * 50, "obrona: fala " .. best)
			end
		end
	end
	deps.Net:SendAll("Notify", "error", "💀 Kryształ upadł na fali " .. arenaState.Wave .. "! Bonus wypłacony — nowa runda za chwilę.")
	for enemy in pairs(enemies) do
		enemy:Destroy()
	end
	enemies = {}
	arenaState.Wave = 0
	arenaState.CrystalHP = arenaState.MaxCrystalHP
	arenaState.BreakUntil = os.time() + 8
end

function Pack.Setup(deps, world)
	local config = knobs(deps)
	arenaState.MaxCrystalHP = config.CrystalHP or 20
	arenaState.CrystalHP = arenaState.MaxCrystalHP

	-- ruch wrogów + dotarcie do kryształu + zarządzanie falami
	task.spawn(function()
		RunService.Heartbeat:Connect(function(dt)
			local crystal = world.Crystal
			for enemy in pairs(enemies) do
				if enemy.Parent then
					local direction = crystal.Position - enemy.Position
					direction = Vector3.new(direction.X, 0, direction.Z)
					if direction.Magnitude > 6 then
						enemy.Position += direction.Unit * enemySpeed() * dt
					else
						enemies[enemy] = nil
						enemy:Destroy()
						arenaState.CrystalHP -= 1
						deps.EffectsService.BurstAt(crystal.Position, { 255, 60, 60 })
						if arenaState.CrystalHP <= 0 then
							endRun(deps, world)
							return
						end
					end
				end
			end
			-- start/przerwa między falami
			if not arenaState.Running then
				if os.time() >= arenaState.BreakUntil then
					startWave(deps, world)
				end
			elseif arenaState.Pending <= 0 and next(enemies) == nil then
				-- fala oczyszczona
				local bonus = 30 + arenaState.Wave * 15
				for _, player in ipairs(Players:GetPlayers()) do
					deps.PlayerDataService.AddCoins(player, bonus, "fala " .. arenaState.Wave .. " oczyszczona")
				end
				deps.Net:SendAll("Notify", "success", "✅ FALA " .. arenaState.Wave .. " OCZYSZCZONA! +"
					.. bonus .. " monet każdemu. Następna za 6 s…")
				arenaState.Running = false
				arenaState.BreakUntil = os.time() + 6
			end
		end)
	end)

	print("[Forge] Przetrwanie Fal: kryształ (" .. arenaState.MaxCrystalHP .. " HP) i fale wrogów działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Fala", Value = tostring(arenaState.Wave) },
			{ Label = "Kryształ", Value = arenaState.CrystalHP .. "/" .. arenaState.MaxCrystalHP .. " HP" },
			{ Label = "Wrogów żywych", Value = tostring(0) },
			{ Label = "Twoje obrażenia", Value = tostring(damageOf(data)) },
			{ Label = "Rekord fali", Value = tostring(data.Stats.BestWave or 0) },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	local damage = data.Stats.DamageLevel or 0
	local armor = data.Stats.ArmorLevel or 0
	local slow = data.Stats.SlowLevel or 0
	return {
		{ Id = "damage_up", Name = "Obrażenia +1", Description = "szybciej ubijasz wrogów (lvl " .. damage .. "/12)", Kind = "stat", Stat = "DamageLevel", Amount = 1, Price = math.floor(300 * 1.5 ^ damage), Owned = damage >= 12 },
		{ Id = "armor_up", Name = "Pancerz kryształu +5 HP", Description = "i pełne leczenie kryształu (lvl " .. armor .. "/6)", Kind = "special", Price = math.floor(900 * 1.6 ^ armor), Owned = armor >= 6 },
		{ Id = "slow_up", Name = "Wrogowie -5% prędkości", Description = "wolniej docierają (lvl " .. slow .. "/6)", Kind = "stat", Stat = "SlowLevel", Amount = 1, Price = math.floor(500 * 1.5 ^ slow), Owned = slow >= 6 },
	}
end

function Pack.OnBuy(deps, player, item)
	if item.Id == "armor_up" then
		arenaState.MaxCrystalHP += 5
		arenaState.CrystalHP = arenaState.MaxCrystalHP
		return true, "Kryształ odrasta z pancerzem!"
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "w_wave_5", type = "stat:BestWave", target = 5, reward = 500, desc = "Dotrzyj do fali 5" },
	{ id = "w_wave_10", type = "stat:BestWave", target = 10, reward = 1500, desc = "Dotrzyj do fali 10" },
	{ id = "w_kills_50", type = "stat:Kills", target = 50, reward = 600, desc = "Pokonaj 50 wrogów" },
	{ id = "w_time_20", type = "playtime", target = 20, reward = 300, desc = "Bronij kryształu 20 minut" },
}

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.BestWave or 0) or 0
end

function Pack.OfflineRatePerHour(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (20 + 3 * (data.Stats.BestWave or 0)) or 20
end

return Pack
