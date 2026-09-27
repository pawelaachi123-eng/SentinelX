--!strict
-- ============================================================================
-- MEGA OBBY — AdminService (Server/Modules/AdminService)
-- Komendy czatowe dla adminów (whitelist UserIds + opcjonalna grupa Roblox):
--   !help · !coins N · !stage N · !rain · !announce tekst · !kick gracz
-- Każda komenda z pcall i odpowiedzią zwrotną. IsAdmin cache'uje rangę grupy
-- (GetRankInGroup bywa wołany często przez złośliwe skrypty).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local AdminService = {}

local deps = nil
local config = nil
local rankCache = {} -- [userId] = { rank, at }

local function groupRank(player): number
	local group = config.Admins.Group
	if not group or not group.Id or group.Id == 0 then
		return 0
	end
	local cached = rankCache[player.UserId]
	if cached and os.clock() - cached.at < 60 then
		return cached.rank
	end
	local ok, rank = pcall(function()
		return player:GetRankInGroup(group.Id)
	end)
	local value = ok and rank or 0
	rankCache[player.UserId] = { rank = value, at = os.clock() }
	return value
end

function AdminService.IsAdmin(player): boolean
	if table.find(config.Admins.UserIds, player.UserId) then
		return true
	end
	return groupRank(player) >= config.Admins.Group.MinRank
end

local function reply(player, message)
	deps.Net:SendTo(player, "Notify", "info", "[ADMIN] " .. message)
end

local function handleChat(player: Player, message: string)
	if not AdminService.IsAdmin(player) then
		return
	end
	local prefix = config.Admins.ChatPrefix
	if string.sub(message, 1, #prefix) ~= prefix then
		return
	end
	local content = string.sub(message, #prefix + 1)
	local command, argument = content:match("^(%S+)%s*(.*)$")

	local ok, err = pcall(function()
		if command == "help" then
			reply(player, "!coins N · !stage N · !rain · !announce tekst · !kick gracz")
		elseif command == "coins" then
			local amount = tonumber(argument)
			if amount and amount > 0 and amount <= 1e9 then
				deps.PlayerDataService.AddCoins(player, math.floor(amount), "admin")
				reply(player, "Dodano " .. deps.Util.Format.Number(math.floor(amount)) .. " monet.")
			else
				reply(player, "Użycie: !coins 1000")
			end
		elseif command == "stage" then
			local stage = tonumber(argument)
			if stage and stage >= 1 and stage <= config.TotalStages then
				stage = math.floor(stage)
				deps.PlayerDataService.SetStage(player, stage)
				-- respawn + teleport na nowy pad
				local CollectionService = game:GetService("CollectionService")
				for _, pad in ipairs(CollectionService:GetTagged("Checkpoint")) do
					if pad:GetAttribute("StageNumber") == stage then
						player.RespawnLocation = pad
						break
					end
				end
				local character = player.Character
				local spawnCFrame = deps.WorldBuilder.Result.StageSpawns[stage]
				if character and spawnCFrame then
					character:PivotTo(spawnCFrame)
					character:SetAttribute("LastTeleport", os.clock())
				end
				reply(player, "Teleport na etap " .. stage .. ".")
			else
				reply(player, "Użycie: !stage 42")
			end
		elseif command == "rain" then
			deps.CoinService.TriggerRain()
			reply(player, "Deszcz monet odpalony w lobby.")
		elseif command == "announce" then
			if argument ~= "" then
				deps.Net:SendAll("Notify", "info", "📣 " .. player.DisplayName .. ": " .. argument)
			else
				reply(player, "Użycie: !announce Witajcie!")
			end
		elseif command == "kick" then
			local target = nil
			for _, candidate in ipairs(Players:GetPlayers()) do
				if candidate.Name:lower() == argument:lower() or candidate.DisplayName:lower() == argument:lower() then
					target = candidate
					break
				end
			end
			if target then
				target:Kick("Wyrzucony przez admina " .. player.DisplayName .. ".")
				reply(player, "Wyrzucono " .. target.Name .. ".")
			else
				reply(player, "Nie znaleziono gracza: " .. argument)
			end
		else
			reply(player, "Nieznana komenda. !help pokazuje listę.")
		end
	end)
	if not ok then
		warn("[MegaObby] Błąd komendy admina: " .. tostring(err))
		reply(player, "Błąd komendy — patrz konsola serwera.")
	end
end

function AdminService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	Players.PlayerAdded:Connect(function(player)
		player.Chatted:Connect(function(message)
			handleChat(player, message)
		end)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		player.Chatted:Connect(function(message)
			handleChat(player, message)
		end)
	end

	print("[MegaObby] AdminService gotowy — adminów na whiteliście: " .. #config.Admins.UserIds .. ".")
end

return AdminService
