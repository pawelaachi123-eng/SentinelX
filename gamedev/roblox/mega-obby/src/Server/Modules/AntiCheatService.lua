--!strict
-- ============================================================================
-- MEGA OBBY — AntiCheatService (Server/Modules/AntiCheatService)
-- Heurystyki serwera (klient nigdy nie jest zaufany):
--   • prędkość pozioma ~2.2× bieżącej WalkSpeed przez próbkę → strike,
--   • teleport (skok pozycji > 130 studów między próbkami, bez znamion
--     legalnego teleportu — attribute LastTeleport ustawiany przez StageService),
--   • strikes gasną po DecaySeconds; po StrikeLimit → wyrzucenie z komunikatem.
-- Admini (AdminService.IsAdmin) są zwolnieni. Rozjemca flagingu: 1 sample
-- = 1 kandydat, ale strike wymaga 2 próbek z rzędu (eliminuje mikroszarpnięcia).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local AntiCheatService = {}

local deps = nil
local config = nil

local trackers = {} -- [userId] = { LastPos, Strikes, LastStrike, PendingSpeed }

local function strike(player, tracker, reason: string)
	local now = os.clock()
	if tracker.LastStrike and now - tracker.LastStrike > config.AntiCheat.DecaySeconds then
		tracker.Strikes = 0
	end
	tracker.Strikes += 1
	tracker.LastStrike = now
	if tracker.Strikes >= config.AntiCheat.StrikeLimit then
		deps.Net:SendTo(player, "Notify", "error", "Wykryto niemożliwy ruch (" .. reason .. "). Wyrzucam — gra uczciwie!")
		task.delay(1, function()
			player:Kick("MEGA OBBY: niemożliwy ruch (" .. reason .. "). Jeśli to błąd — wróć, wystarczy że grasz uczciwie.")
		end)
	else
		deps.Net:SendTo(player, "Notify", "error",
			"Uwaga: niemożliwy ruch (" .. reason .. ") — ostrzeżenie " .. tracker.Strikes .. "/" .. config.AntiCheat.StrikeLimit .. ".")
	end
end

local function watchCharacter(player: Player)
	local function onCharacter(character)
		local root = character:WaitForChild("HumanoidRootPart", 10)
		local humanoid = character:WaitForChild("Humanoid", 10)
		if not (root and humanoid) then
			return
		end
		trackers[player.UserId] = { LastPos = root.Position, Strikes = 0, LastStrike = nil, PendingSpeed = 0 }
		local tracker = trackers[player.UserId]
		local accumulator = 0
		local connection
		connection = RunService.Heartbeat:Connect(function(dt)
			accumulator += dt
			if accumulator < config.AntiCheat.SampleSeconds then
				return
			end
			accumulator = 0
			if not root.Parent then
				connection:Disconnect()
				return
			end
			local now = root.Position
			local previous = tracker.LastPos
			tracker.LastPos = now
			local delta = now - previous
			local horizontal = Vector3.new(delta.X, 0, delta.Z).Magnitude
			local seconds = config.AntiCheat.SampleSeconds
			local speed = horizontal / seconds

			-- legalny teleport (portal/skip/win/spawn) wyłącza oba testy na 2 s
			local lastTeleport = character:GetAttribute("LastTeleport")
			if type(lastTeleport) == "number" and os.clock() - lastTeleport < 2 then
				tracker.PendingSpeed = 0
				return
			end

			-- 1) teleport
			if horizontal > config.AntiCheat.TeleportStuds then
				strike(player, tracker, "teleport")
				return
			end

			-- 2) prędkość: strike dopiero po 2 próbkach z rzędu (bez fałszywych alarmów)
			local threshold = math.max(config.AntiCheat.MinSpeedStuds, humanoid.WalkSpeed * config.AntiCheat.SpeedFactor)
			if speed > threshold then
				tracker.PendingSpeed += 1
				if tracker.PendingSpeed >= 2 then
					tracker.PendingSpeed = 0
					strike(player, tracker, string.format("prędkość %.0f/st", speed))
				end
			else
				tracker.PendingSpeed = 0
			end
		end)
	end
	player.CharacterAdded:Connect(onCharacter)
	if player.Character then
		task.spawn(onCharacter, player.Character)
	end
end

function AntiCheatService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	Players.PlayerAdded:Connect(function(player)
		if deps.AdminService and deps.AdminService.IsAdmin(player) then
			return -- admini zwolnieni (testują komendy typu !stage)
		end
		watchCharacter(player)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		if not (deps.AdminService and deps.AdminService.IsAdmin(player)) then
			watchCharacter(player)
		end
	end

	Players.PlayerRemoving:Connect(function(player)
		trackers[player.UserId] = nil
	end)

	print("[MegaObby] AntiCheatService gotowy (limit: "
		.. config.AntiCheat.StrikeLimit .. " uderzeń, decay " .. config.AntiCheat.DecaySeconds .. " s).")
end

return AntiCheatService
