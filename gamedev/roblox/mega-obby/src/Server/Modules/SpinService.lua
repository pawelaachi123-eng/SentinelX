--!strict
-- ============================================================================
-- MEGA OBBY — SpinService (Server/Modules/SpinService)
-- Koło fortuny: 1 DARMOWY spin na dobę (UtcDay — uczciwie wszyscy w tym samym
-- czasie), kolejne za Robux (produkt spin_extra). Nagrody z wagami = JAWNE
-- szanse (config → klient pokazuje procenty; wymóg Roblox dla płatnych
-- losowań). Nagrody: monety (przez mnożniki AddCoins), boost (BoostService),
-- losowanie zwierzaka (tylko gdy gatunek ma PetService — obby).
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local SpinService = {}

local deps = nil
local config = nil

local function ensureToday(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local today = deps.Util.UtcDayNumber()
	if data.Stats.LastSpinDay ~= today then
		data.Stats.LastSpinDay = today
		data.Stats.SpinsAvailable = (data.Stats.SpinsAvailable or 0) + config.FreePerDay
	end
	return data
end

function SpinService.InfoOf(player)
	local data = ensureToday(player)
	if not data then
		return { Available = 0, Rewards = {} }
	end
	local rewards = {}
	local total = 0
	for _, reward in ipairs(config.Rewards) do
		total += reward.weight
	end
	for _, reward in ipairs(config.Rewards) do
		table.insert(rewards, { Label = reward.label, Percent = math.floor(reward.weight / total * 100 + 0.5) })
	end
	return {
		Available = data.Stats.SpinsAvailable or 0,
		UsedToday = data.Stats.LastSpinDay == deps.Util.UtcDayNumber() and (data.Stats.SpinsUsedToday or 0) or 0,
		Rewards = rewards,
		PaidProductId = config.PaidProductId,
	}
end

local function rollReward()
	local rng = Random.new(os.time() * 1000 + math.random(0, 999999))
	local picked = deps.Util.WeightedPick(config.Rewards, rng)
	return picked
end

local function applyReward(player, reward): string
	if reward.type == "coins" then
		deps.PlayerDataService.AddCoins(player, reward.amount, "spin")
		return reward.label
	end
	if reward.type == "boost" then
		if deps.BoostService then
			deps.BoostService.Apply(player, deps.PlayerDataService.Get(player), "Coin", 600, 2)
			return reward.label
		end
		deps.PlayerDataService.AddCoins(player, reward.amount, "spin (boost zamieniony)")
		return "+" .. reward.amount .. " monet (boost niedostępny)"
	end
	if reward.type == "pet" then
		if deps.PetService and deps.PetService.GrantEggRoll then
			local _, message = deps.PetService.GrantEggRoll(player, "egg_basic")
			return "ZWIERZAK: " .. message
		end
		deps.PlayerDataService.AddCoins(player, 500, "spin (pet zamieniony)")
		return "500 monet (zwierzaki nie w tym trybie)"
	end
	return "nagroda"
end

local function handleSpin(player)
	local data = ensureToday(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if (data.Stats.SpinsAvailable or 0) <= 0 then
		return false, "Darmowy spin już dziś wykorzystany — wróć jutro (albo kup extra w sklepie)."
	end
	data.Stats.SpinsAvailable = data.Stats.SpinsAvailable - 1
	data.Stats.SpinsUsedToday = (data.Stats.SpinsUsedToday or 0) + 1
	local reward = rollReward()
	local label = applyReward(player, reward)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "🎡 " .. label .. "!"
end

-- produkt "spin_extra": +1 spin (można kumulować)
function SpinService.GrantSpin(player): (boolean, string)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	data.Stats.SpinsAvailable = (data.Stats.SpinsAvailable or 0) + 1
	deps.Net:SendTo(player, "Notify", "success", "Dokupiono spin — zakręć w oknie SPIN!")
	deps.PlayerDataService.PushSnapshot(player)
	return true, "+1 spin"
end

SpinService.ProductGrants = function(_deps)
	return { spin_extra = function(player) return SpinService.GrantSpin(player) end }
end

function SpinService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("Spin", handleSpin, { 0.3, 2 })

	print("[MegaObby] SpinService gotowy — " .. config.FreePerDay .. " darmowy spin/dzień, "
		.. #config.Rewards .. " nagród z jawnymi szansami.")
end

return SpinService
