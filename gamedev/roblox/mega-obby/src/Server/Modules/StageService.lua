--!strict
-- ============================================================================
-- MEGA OBBY — StageService (Server/Modules/StageService)
-- Logika etapów: checkpointy (tag Checkpoint), portal lobby → aktualny etap,
-- nagrody za NOWE etapy, twarda zasada anty-skip (tylko +1!), finisz gry,
-- skip z produktu, respawn na właściwym padzie (player.RespawnLocation).
-- ============================================================================

local CollectionService = game:GetService("CollectionService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local StageService = {}

local deps = nil
local world = nil
local config = nil

local function notify(player, kind, message)
	deps.Net:SendTo(player, "Notify", kind, message)
end

local function markTeleport(character)
	if character then
		character:SetAttribute("LastTeleport", os.clock())
	end
end

-- Teleport gracza na pad etapu (portal, skip, po finiszu)
function StageService.TeleportToStage(player, stage: number)
	local character = player.Character
	local root = character and character:FindFirstChild("HumanoidRootPart")
	local spawn = world.Result and world.Result.StageSpawns[stage]
	if root and spawn then
		character:PivotTo(spawn)
		markTeleport(character)
	end
end

-- Używane przez MonetizationService (produkt „Pomiń Etap”)
function StageService.SkipStage(player): (boolean, string)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują — chwilka."
	end
	local current = data.Stage
	if current >= config.FinishStage then
		return false, "Cała gra już zaliczona — skip niepotrzebny. 🏆"
	end
	local nextStage = current + 1
	deps.PlayerDataService.SetStage(player, nextStage)
	local pad = CollectionService:GetTagged("Checkpoint")
	for _, candidate in ipairs(pad) do
		if candidate:GetAttribute("StageNumber") == nextStage then
			player.RespawnLocation = candidate
			break
		end
	end
	StageService.TeleportToStage(player, nextStage)
	return true, "Etap " .. nextStage .. " odblokowany skipem."
end

local function onWin(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return
	end
	if (data.Stats.Wins or 0) > 0 then
		-- powtórny finisz: mniejsza nagroda, wciąż przyjemność
		deps.PlayerDataService.AddCoins(player, 100, "powtórny finisz")
		notify(player, "success", "Znowu całość! +100 monet (powtórka).")
		return
	end
	deps.PlayerDataService.IncrementStat(player, "Wins", 1)
	deps.PlayerDataService.AddCoins(player, config.Coins.FinishBonus, "FINISZ")
	deps.EffectsService.ConfettiAt(Vector3.new(0, 8, 0))
	deps.Net:SendAll("WorldFX", "win", CFrame.new(0, 8, 0), nil)
	deps.Net:SendAll("Notify", "success",
		"🏆 " .. player.DisplayName .. " PRZESZEDŁ CAŁĄ GRĘ! Gratulacje!")
	StageService.TeleportToStage(player, math.min(data.Stage, config.TotalStages))
	task.delay(1.5, function()
		local character = player.Character
		if character then
			character:PivotTo(world.Result.LobbySpawn)
			markTeleport(character)
		end
	end)
end

local function handleCheckpoint(player, pad)
	local stageNumber = pad:GetAttribute("StageNumber")
	if type(stageNumber) ~= "number" then
		return
	end
	deps.PlayerDataService.WhenReady(player, function(data)
		local current = data.Stage
		if stageNumber <= current then
			return -- etap już zaliczony: bez nagrody, bez karania (ktoś wrócił zwiedzać)
		end
		if stageNumber > current + 1 then
			-- ANTY-SKIP: wolno tylko +1. Teleporty z cheatów po prostu nie działają.
			notify(player, "error", "Najpierw etap " .. (current + 1) .. " — nie da się przeskoczyć dalej.")
			return
		end
		-- NOWY etap: nagroda + respawn tu + efekty
		deps.PlayerDataService.SetStage(player, stageNumber)
		player.RespawnLocation = pad
		if stageNumber >= config.FinishStage then
			onWin(player)
			return
		end
		local island = config.IslandOf(stageNumber)
		local islandIndex = 1
		for index, candidate in ipairs(config.Islands) do
			if candidate.id == island.id then
				islandIndex = index
			end
		end
		local reward = config.Coins.StageRewardBase
			+ config.Coins.StageRewardPerIslandStep * (islandIndex - 1)
		deps.PlayerDataService.AddCoins(player, reward, "etap " .. stageNumber)
		deps.EffectsService.BurstAt(pad.Position + Vector3.new(0, 2, 0), island.color)
		deps.Net:SendTo(player, "WorldFX", "checkpoint", CFrame.new(pad.Position + Vector3.new(0, 2, 0)), stageNumber)
		deps.EventBus.StageReached:Fire(player, stageNumber, true)
	end)
end

local function armCheckpoint(pad)
	if pad:GetAttribute("Armed") then
		return
	end
	pad:SetAttribute("Armed", true)
	pad.Touched:Connect(function(hit)
		local character = hit.Parent
		if not character then
			return
		end
		local player = Players:GetPlayerFromCharacter(character)
		if player then
			handleCheckpoint(player, pad)
		end
	end)
end

local function armPortal(portal)
	if portal:GetAttribute("Armed") then
		return
	end
	portal:SetAttribute("Armed", true)
	portal.Touched:Connect(function(hit)
		local character = hit.Parent
		local player = character and Players:GetPlayerFromCharacter(character)
		if not player then
			return
		end
		local data = deps.PlayerDataService.Get(player)
		if not data then
			return
		end
		local debounce = character:GetAttribute("PortalDebounce")
		local now = os.clock()
		if debounce and now - debounce < 2 then
			return
		end
		character:SetAttribute("PortalDebounce", now)
		StageService.TeleportToStage(player, data.Stage)
		notify(player, "info", "Teleport na etap " .. data.Stage .. " — powodzenia!")
	end)
end

function StageService.Start(depsIn)
	deps = depsIn
	config = deps.Config
	world = deps.WorldBuilder

	-- klawisz R u klienta: szybki powrót na checkpoint (śmierć = respawn na padzie)
	deps.Net:OnServer("Respawn", function(player)
		local character = player.Character
		local humanoid = character and character:FindFirstChildOfClass("Humanoid")
		if humanoid and humanoid.Health > 0 then
			humanoid.Health = 0
			return true, "Wracasz na ostatni checkpoint."
		end
		return false, ""
	end)

	for _, pad in ipairs(CollectionService:GetTagged("Checkpoint")) do
		armCheckpoint(pad)
	end
	CollectionService:GetInstanceAddedSignal("Checkpoint"):Connect(armCheckpoint)

	for _, portal in ipairs(CollectionService:GetTagged("Portal")) do
		armPortal(portal)
	end
	CollectionService:GetInstanceAddedSignal("Portal"):Connect(armPortal)

	-- respawn ustala pad: po wejściu w checkpoint Roblox sam wrzuci gracza tu
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function(character)
			markTeleport(character) -- spawn ≠ cheat
			deps.PlayerDataService.WhenReady(player, function(data)
				-- poaduj aktualny pad jako respawn (najnowszy zaliczony etap)
				for _, pad in ipairs(CollectionService:GetTagged("Checkpoint")) do
					if pad:GetAttribute("StageNumber") == data.Stage then
						player.RespawnLocation = pad
						break
					end
				end
			end)
		end)
	end)

	print("[MegaObby] StageService gotowy — checkpointy i portal uzbrojone.")
end

return StageService
