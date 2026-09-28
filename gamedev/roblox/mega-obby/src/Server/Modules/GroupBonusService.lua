--!strict
-- ============================================================================
-- MEGA OBBY — GroupBonusService (Server/Modules/GroupBonusService)
-- Członkowie grupy twórcy dostają +BonusPercent monet (klasyk retencji i
-- wzrostu grupy). Id > 0 w GameConfig.Group włącza bonus; ranga grupy jest
-- cache'owana (IsInGroup wywoływane raz na sesję). Bonus bez agresji: jeden
-- komunikat przy wejściu.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local GroupBonusService = {}

local deps = nil
local config = nil
local members = {} -- [userId] = true

function GroupBonusService.IsMember(player): boolean
	return members[player.UserId] == true
end

function GroupBonusService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	if not (config.Group and config.Group.Id and config.Group.Id > 0) then
		print("[MegaObby] GroupBonusService: grupa nie skonfigurowana (GameConfig.Group.Id = 0) — bonus wyłączony.")
		return
	end

	local function check(player)
		local ok, inGroup = pcall(function()
			return player:IsInGroup(config.Group.Id)
		end)
		if ok and inGroup then
			members[player.UserId] = true
			deps.Net:SendTo(player, "Notify", "success",
				"💚 Bonus grupy: +" .. config.Group.BonusPercent .. "% monet na zawsze. Dzięki, że jesteś z nami!")
		else
			deps.Net:SendTo(player, "Notify", "info",
				"Dołącz do naszej grupy Roblox i zgarnij +" .. config.Group.BonusPercent .. "% monet (bonus leci sam).")
		end
	end

	Players.PlayerAdded:Connect(function(player)
		task.delay(3, function()
			if player.Parent then
				check(player)
			end
		end)
	end)
	for _, player in ipairs(Players:GetPlayers()) do
		task.spawn(check, player)
	end

	Players.PlayerRemoving:Connect(function(player)
		members[player.UserId] = nil
	end)

	print("[MegaObby] GroupBonusService gotowy — grupa " .. config.Group.Id .. ", bonus +" .. config.Group.BonusPercent .. "%.")
end

return GroupBonusService
