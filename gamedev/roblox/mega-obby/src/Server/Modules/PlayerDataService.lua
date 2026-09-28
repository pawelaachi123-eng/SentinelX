--!strict
-- ============================================================================
-- MEGA OBBY — PlayerDataService (Server/Modules/PlayerDataService)
-- Dane gracza: DataStore + retry z backoffem, sesyjny cache, autosave co 2 min,
-- UpdateAsync (bez utraty zapisu równoległego), migracja brakujących kluczy,
-- BindToClose (nikt nie straci postępu przy zamykaniu serwera), leaderstats.
--
-- Najlepsze praktyki, które tu stosuję:
--  • nigdy nie ufam wartościom z klienta (ta usługa nie czyta żadnych remote'ów),
--  • pcall na KAŻDYM wywołaniu DataStore (błąd sieci ≠ wywalona gra),
--  • tryb offline w Studio bez dostępu API (dane tylko na sesję, uczciwy log),
--  • nagroda przechodzi przez AddCoins → mnożniki (x2, zwierzaki) w jednym miejscu.
-- ============================================================================

local DataStoreService = game:GetService("DataStoreService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Util = require(Shared:WaitForChild("Util"))

local PlayerDataService = {}

local STORE_NAME = "MegaObby_v1"
local AUTOSAVE_SECONDS = 120
local LOAD_RETRIES = 3

local DEFAULT_DATA = {
	Coins = 0,
	Stage = 1,
	Trails = { "trail_basic" },
	EquippedTrail = "trail_basic",
	Pets = {},          -- { { Uid = string, Id = string, Obtained = number } }
	EquippedPets = {},  -- { uid, ... }
	Gamepasses = {},    -- { [passId] = true }
	Quests = {},        -- [dayKey] = { [questId] = { Progress = n, Claimed = bool } }
	Daily = { Streak = 0, LastClaimDay = 0 },
	Receipts = {},      -- [purchaseId] = true (idempotentność ProcessReceipt)
	Stats = { Jumps = 0, Deaths = 0, PlaytimeSeconds = 0, Wins = 0 },
	UpdatedAt = 0,
}

PlayerDataService.OnLoaded = Util.Signal() -- (player, data)

local profiles = {}      -- [userId] = { Data, Player }
local waiting = {}       -- [userId] = { fn, ... }
local offlineMode = false
local store = nil

local function keyFor(userId: number): string
	return "u_" .. userId
end

local function ensureLeaderstats(player, data)
	local existing = player:FindFirstChild("leaderstats")
	if existing then
		return existing
	end
	local folder = Instance.new("Folder")
	folder.Name = "leaderstats"
	local coins = Instance.new("IntValue")
	coins.Name = "Monety"
	coins.Value = data.Coins
	coins.Parent = folder
	local stage = Instance.new("IntValue")
	stage.Name = "Etap"
	stage.Value = data.Stage
	stage.Parent = folder
	folder.Parent = player
	return folder
end

local function migrate(loaded)
	-- dokłada brakujące klucze z DEFAULT (bezpieczna migracja starych zapisów)
	local merged = Util.TableUtil.DeepCopy(DEFAULT_DATA)
	if type(loaded) == "table" then
		for key, value in pairs(loaded) do
			if key == "Stats" and type(value) == "table" then
				for statKey, statValue in pairs(value) do
					merged.Stats[statKey] = statValue
				end
			elseif key == "Daily" and type(value) == "table" then
				for dailyKey, dailyValue in pairs(value) do
					merged.Daily[dailyKey] = dailyValue
				end
			else
				merged[key] = value
			end
		end
	end
	return merged
end

local function loadDataAsync(key: string)
	for attempt = 1, LOAD_RETRIES do
		local ok, result = pcall(function()
			return store:GetAsync(key)
		end)
		if ok then
			return true, result
		end
		warn("[MegaObby] GetAsync nie powiódł się (próba " .. attempt .. "/" .. LOAD_RETRIES .. "): " .. tostring(result))
		task.wait(2 ^ attempt)
	end
	return false, nil
end

local function saveDataAsync(player, data, quiet: boolean?): boolean
	if offlineMode or store == nil then
		return false
	end
	data.UpdatedAt = os.time()
	local payload = Util.TableUtil.DeepCopy(data)
	local key = keyFor(player.UserId)
	for attempt = 1, LOAD_RETRIES do
		local ok, err = pcall(function()
			store:UpdateAsync(key, function(old)
				-- nie nadpisuj nowszego zapisu z innej sesji
				if type(old) == "table" and type(old.UpdatedAt) == "number" and old.UpdatedAt > payload.UpdatedAt then
					return old
				end
				return payload
			end)
		end)
		if ok then
			return true
		end
		warn("[MegaObby] UpdateAsync nie powiódł się (próba " .. attempt .. "): " .. tostring(err))
		task.wait(2 ^ attempt)
	end
	if not quiet then
		local net = PlayerDataService._deps.Net
		net:SendTo(player, "Notify", "error", "Nie udało się zapisać postępu — spróbuję znowu przy wyjściu.")
	end
	return false
end

local function resolveWaiting(player, data)
	local queue = waiting[player.UserId]
	if queue then
		waiting[player.UserId] = nil
		for _, fn in ipairs(queue) do
			task.spawn(fn, data)
		end
	end
end

-- ————————————————————————————————————————————————
-- API dla innych usług
-- ————————————————————————————————————————————————
function PlayerDataService.Get(player)
	local profile = profiles[player.UserId]
	return profile and profile.Data or nil
end

function PlayerDataService.WhenReady(player, fn)
	local profile = profiles[player.UserId]
	if profile then
		task.spawn(fn, profile.Data)
		return
	end
	local queue = waiting[player.UserId]
	if not queue then
		queue = {}
		waiting[player.UserId] = queue
	end
	table.insert(queue, fn)
end

local function scheduleSnapshot(player)
	local times = PlayerDataService._snapshotTimes
	local now = os.clock()
	if times[player.UserId] and now - times[player.UserId] < 0.5 then
		return
	end
	times[player.UserId] = now
	local data = PlayerDataService.Get(player)
	if not data then
		return
	end
	local snapshot = PlayerDataService.Snapshot(player)
	PlayerDataService._deps.Net:SendTo(player, "StateSync", snapshot)
end

function PlayerDataService.AddCoins(player, amount: number, reason: string?): number
	local data = PlayerDataService.Get(player)
	if not data or amount <= 0 then
		return data and data.Coins or 0
	end
	local deps = PlayerDataService._deps
	local multiplier = 1
	if data.Gamepasses["x2coins"] then
		multiplier = multiplier * 2
	end
	if deps.PetService then
		multiplier = multiplier * deps.PetService.GetCoinMultiplier(player)
	end
	local granted = math.floor(amount * multiplier + 0.5)
	data.Coins = data.Coins + granted
	local stats = player:FindFirstChild("leaderstats")
	local coinsValue = stats and stats:FindFirstChild("Monety")
	if coinsValue then
		coinsValue.Value = data.Coins
	end
	deps.Net:SendTo(player, "StatChanged", "Coins", data.Coins)
	deps.EventBus.CoinsAdded:Fire(player, granted, data.Coins, reason or "nieznany")
	scheduleSnapshot(player)
	return data.Coins
end

function PlayerDataService.TrySpend(player, amount: number): boolean
	local data = PlayerDataService.Get(player)
	if not data then
		return false
	end
	if data.Coins < amount then
		PlayerDataService._deps.Net:SendTo(player, "Notify", "error",
			"Za mało monet (potrzeba " .. Util.Format.Number(amount) .. "). Zbieraj monety na torze i w questach!")
		return false
	end
	data.Coins = data.Coins - amount
	local stats = player:FindFirstChild("leaderstats")
	local coinsValue = stats and stats:FindFirstChild("Monety")
	if coinsValue then
		coinsValue.Value = data.Coins
	end
	PlayerDataService._deps.Net:SendTo(player, "StatChanged", "Coins", data.Coins)
	scheduleSnapshot(player)
	return true
end

function PlayerDataService.SetStage(player, stage: number)
	local data = PlayerDataService.Get(player)
	if not data then
		return
	end
	data.Stage = stage
	local stats = player:FindFirstChild("leaderstats")
	local stageValue = stats and stats:FindFirstChild("Etap")
	if stageValue then
		stageValue.Value = stage
	end
	PlayerDataService._deps.Net:SendTo(player, "StatChanged", "Stage", stage)
	scheduleSnapshot(player)
end

function PlayerDataService.IncrementStat(player, statKey: string, delta: number)
	local data = PlayerDataService.Get(player)
	if not data then
		return
	end
	data.Stats[statKey] = (data.Stats[statKey] or 0) + delta
end

function PlayerDataService.GrantGamepass(player, passId: string)
	local data = PlayerDataService.Get(player)
	if not data then
		return
	end
	if data.Gamepasses[passId] then
		return
	end
	data.Gamepasses[passId] = true
	PlayerDataService._deps.EventBus.GamepassGranted:Fire(player, passId)
	scheduleSnapshot(player)
end

function PlayerDataService.HasGamepass(player, passId: string): boolean
	local data = PlayerDataService.Get(player)
	return data ~= nil and data.Gamepasses[passId] == true
end

-- Zwraca true, gdy ten paragon jeszcze nie był obsłużony (idempotentność).
function PlayerDataService.ClaimReceipt(player, purchaseId: string): boolean
	local data = PlayerDataService.Get(player)
	if not data then
		return false
	end
	if data.Receipts[purchaseId] then
		return false
	end
	data.Receipts[purchaseId] = true
	return true
end

function PlayerDataService.Snapshot(player)
	local deps = PlayerDataService._deps
	local data = PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local island = deps.Config.IslandOf(data.Stage)
	return {
		GameName = deps.Config.GameName,
		Coins = data.Coins,
		Stage = data.Stage,
		TotalStages = deps.Config.TotalStages,
		Island = island.name,
		IslandColor = island.color,
		Trails = data.Trails,
		EquippedTrail = data.EquippedTrail,
		Pets = deps.PetService and deps.PetService.OwnedOf(player) or {},
		EquippedPets = data.EquippedPets or {},
		Gamepasses = data.Gamepasses,
		Quests = deps.QuestService and deps.QuestService.QuestsOf(player) or {},
		Hud = deps.HudProvider and deps.HudProvider(player) or nil,
		ShopItems = deps.ShopProvider and deps.ShopProvider(player) or nil,
		Daily = deps.DailyRewardService and deps.DailyRewardService.InfoOf(player) or { Streak = 0, CanClaim = false, NextReward = 0 },
		Stats = data.Stats,
	}
end

function PlayerDataService.PushSnapshot(player)
	local times = PlayerDataService._snapshotTimes
	times[player.UserId] = 0 -- wymuś natychmiast
	scheduleSnapshot(player)
end

function PlayerDataService.SavePlayer(player)
	local profile = profiles[player.UserId]
	if profile then
		saveDataAsync(player, profile.Data, true)
	end
end

-- ————————————————————————————————————————————————
-- START
-- ————————————————————————————————————————————————
function PlayerDataService.Start(deps)
	PlayerDataService._deps = deps
	PlayerDataService._snapshotTimes = {}

	local ok, result = pcall(function()
		return DataStoreService:GetDataStore(STORE_NAME)
	end)
	if ok then
		store = result
	else
		offlineMode = true
		warn("[MegaObby] DataStore niedostępny (Studio bez publikacji?) — tryb offline: postęp tylko na sesję.")
	end

	local function onPlayerAdded(player: Player)
		local key = keyFor(player.UserId)
		local loaded: any = nil
		if not offlineMode then
			local okLoad, result = loadDataAsync(key)
			if okLoad then
				loaded = result
			else
				offlineMode = true
				warn("[MegaObby] Odczyt padł 3× — przechodzę w tryb offline (postęp na sesję).")
			end
		end
		local data = migrate(loaded)
		profiles[player.UserId] = { Data = data, Player = player }
		ensureLeaderstats(player, data)
		PlayerDataService.OnLoaded:Fire(player, data)
		resolveWaiting(player, data)
		deps.Net:SendTo(player, "Notify", "info",
			"Witaj w " .. deps.Config.GameName .. "! Do lobby należy portal — teleportuje na Twój aktualny etap.")
		scheduleSnapshot(player)
	end

	Players.PlayerAdded:Connect(onPlayerAdded)
	for _, player in ipairs(Players:GetPlayers()) do
		task.spawn(onPlayerAdded, player)
	end

	-- czasy gry (dla questów i statystyk): 1 s tykanie
	local accumulator = 0
	game:GetService("RunService").Heartbeat:Connect(function(dt)
		accumulator += dt
		if accumulator >= 1 then
			local delta = math.floor(accumulator)
			accumulator -= delta
			for userId, profile in pairs(profiles) do
				if profile.Player.Parent then
					profile.Data.Stats.PlaytimeSeconds += delta
					deps.EventBus.PlaytimeTick:Fire(profile.Player, delta)
				end
			end
		end
	end)

	-- śmierć → statystyka
	local function onCharacter(player, character)
		local humanoid = character:WaitForChild("Humanoid", 10)
		if humanoid then
			humanoid.Died:Connect(function()
				PlayerDataService.IncrementStat(player, "Deaths", 1)
			end)
			humanoid.StateChanged:Connect(function(_, newState)
				if newState == Enum.HumanoidStateType.Jumping then
					PlayerDataService.IncrementStat(player, "Jumps", 1)
				end
			end)
		end
	end
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function(character)
			onCharacter(player, character)
		end)
	end)

	-- autosave + wyjście + zamknięcie serwera
	task.spawn(function()
		while true do
			task.wait(AUTOSAVE_SECONDS)
			for userId, profile in pairs(profiles) do
				if profile.Player.Parent then
					saveDataAsync(profile.Player, profile.Data, true)
				end
			end
		end
	end)

	Players.PlayerRemoving:Connect(function(player)
		local profile = profiles[player.UserId]
		if profile then
			saveDataAsync(player, profile.Data, true)
			profiles[player.UserId] = nil
			waiting[player.UserId] = nil
		end
	end)

	game:BindToClose(function()
		if offlineMode then
			return
		end
		local pending = 0
		for userId, profile in pairs(profiles) do
			pending += 1
			task.spawn(function()
				saveDataAsync(profile.Player, profile.Data, true)
				pending -= 1
			end)
		end
		local deadline = os.clock() + 25
		while pending > 0 and os.clock() < deadline do
			task.wait(0.25)
		end
	end)

	print("[MegaObby] PlayerDataService gotowy" .. (offlineMode and " (TRYB OFFLINE — bez DataStore)" or "") .. ".")
end

return PlayerDataService
