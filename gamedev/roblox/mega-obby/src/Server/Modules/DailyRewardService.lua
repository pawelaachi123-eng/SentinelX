--!strict
-- ============================================================================
-- MEGA OBBY — DailyRewardService (Server/Modules/DailyRewardService)
-- Nagroda dzienna ze streakiem 7-dniowym (cykl się powtarza). Dzień liczony
-- po UTC (Util.UtcDayNumber), więc wszyscy gracze mają ten sam „dzień gry”.
-- Przerwany streak wraca do 1 — uczciwie i bez bugów stref czasowych.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local DailyRewardService = {}

local deps = nil
local config = nil

function DailyRewardService.InfoOf(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return { Streak = 0, CanClaim = false, NextReward = config.DailyRewards[1].coins, Day = 1 }
	end
	local today = deps.Util.UtcDayNumber()
	local canClaim = data.Daily.LastClaimDay ~= today
	local nextIndex
	if canClaim then
		local continues = data.Daily.LastClaimDay == today - 1
		local nextStreak = continues and data.Daily.Streak + 1 or 1
		nextIndex = ((nextStreak - 1) % #config.DailyRewards) + 1
	else
		nextIndex = ((data.Daily.Streak - 1) % #config.DailyRewards) + 1
	end
	return {
		Streak = data.Daily.Streak,
		CanClaim = canClaim,
		NextReward = config.DailyRewards[nextIndex].coins,
		Day = nextIndex,
	}
end

local function handleClaimDaily(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	local today = deps.Util.UtcDayNumber()
	if data.Daily.LastClaimDay == today then
		return false, "Dzisiejsza nagroda już odebrana — wróć jutro!"
	end
	if data.Daily.LastClaimDay == today - 1 then
		data.Daily.Streak = data.Daily.Streak + 1
	else
		data.Daily.Streak = 1
	end
	data.Daily.LastClaimDay = today
	local index = ((data.Daily.Streak - 1) % #config.DailyRewards) + 1
	local reward = config.DailyRewards[index]
	deps.PlayerDataService.AddCoins(player, reward.coins, "nagroda dnia " .. reward.day)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Dzień " .. reward.day .. "/7: +" .. deps.Util.Format.Number(reward.coins)
		.. " monet! Streak: " .. data.Daily.Streak .. "."
end

function DailyRewardService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("ClaimDaily", handleClaimDaily)

	print("[MegaObby] DailyRewardService gotowy — cykl 7 dni.")
end

return DailyRewardService
