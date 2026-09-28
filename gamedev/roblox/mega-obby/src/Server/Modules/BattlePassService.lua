--!strict
-- ============================================================================
-- MEGA OBBY — BattlePassService (Server/Modules/BattlePassService)
-- SEZON: 30 poziomów, XP za granie (monety, etapy/sukcesy gatunku, minuty).
-- Track darmowy (monety) + PREMIUM (monety x3 + spiny) — premium to produkt/
-- przepustka (data.Gamepasses.battlepass_premium). Odbiór claim per poziom
-- per track (idempotentny: data.PassClaimed["tier_track"]). Wyświetlanie
-- postępu w kliencie (zakładka SEZON).
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local BattlePassService = {}

local deps = nil
local config = nil

local function tierOf(xp: number): number
	return math.clamp(math.floor(xp / config.XpPerTier) + 1, 1, config.Tiers)
end

local function isPremium(player): boolean
	local data = deps.PlayerDataService.Get(player)
	return data ~= nil and (data.Gamepasses.battlepass_premium == true or data.Gamepasses.vip == true)
end

local function addXp(player, amount: number)
	local data = deps.PlayerDataService.Get(player)
	if not data or amount <= 0 then
		return
	end
	local before = tierOf(data.Stats.PassXp or 0)
	data.Stats.PassXp = math.min((data.Stats.PassXp or 0) + amount, config.Tiers * config.XpPerTier)
	local after = tierOf(data.Stats.PassXp)
	if after > before then
		deps.Net:SendTo(player, "Notify", "success",
			"🎟️ SEZON — poziom " .. after .. "/" .. config.Tiers .. "! Odbierz nagrody w zakładce SEZON.")
	end
end

function BattlePassService.InfoOf(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return { Xp = 0, Tier = 1, Tiers = config.Tiers, Premium = false }
	end
	return {
		Xp = data.Stats.PassXp or 0,
		Tier = tierOf(data.Stats.PassXp or 0),
		Tiers = config.Tiers,
		XpPerTier = config.XpPerTier,
		Premium = isPremium(player),
	}
end

-- nagrody proceduralne: darmowy track = 200 + 25/poziom monet; premium = x3 + spin co 5. poziom
local function rewardFor(tier: number, track: string)
	local coins = (200 + 25 * tier) * (track == "premium" and 3 or 1)
	local spins = (track == "premium" and tier % 5 == 0) and 1 or 0
	return coins, spins
end

local function handleClaim(player, tierRaw, trackRaw)
	if type(tierRaw) ~= "number" or type(trackRaw) ~= "string" then
		return false, "Podaj poziom i tor nagród."
	end
	local tier = math.floor(tierRaw)
	local track = trackRaw == "premium" and "premium" or "free"
	if tier < 1 or tier > config.Tiers then
		return false, "Takiego poziomu nie ma."
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if tierOf(data.Stats.PassXp or 0) < tier then
		return false, "Ten poziom jeszcze nieodblokowany — graj dalej!"
	end
	if track == "premium" and not isPremium(player) then
		return false, "Ten tor jest dla SEZON PREMIUM (widoczny w zakładce)."
	end
	data.PassClaimed = data.PassClaimed or {}
	local key = tier .. "_" .. track
	if data.PassClaimed[key] then
		return false, "Już odebrane."
	end
	data.PassClaimed[key] = true
	local coins, spins = rewardFor(tier, track)
	deps.PlayerDataService.AddCoins(player, coins, "sezon L" .. tier .. " (" .. track .. ")")
	if spins > 0 and deps.SpinService then
		for _ = 1, spins do
			deps.SpinService.GrantSpin(player)
		end
	end
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Sezon L" .. tier .. ": +" .. deps.Util.Format.Number(coins) .. " monet"
		.. (spins > 0 and (" +" .. spins .. " spin!") or "")
end

-- grant produktu premium
function BattlePassService.GrantPremium(player): (boolean, string)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if data.Gamepasses.battlepass_premium then
		return true, "Premium już aktywne."
	end
	data.Gamepasses.battlepass_premium = true
	deps.Net:SendTo(player, "Notify", "success", "🎟️ SEZON PREMIUM AKTYWNY! Odbieraj tor premium do L30.")
	deps.PlayerDataService.SavePlayer(player)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Premium aktywne"
end

BattlePassService.ProductGrants = function(_deps)
	return { battlepass_premium = function(player) return BattlePassService.GrantPremium(player) end }
end

function BattlePassService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("ClaimPass", handleClaim, { 1, 4 })

	-- XP z magistrali zdarzeń (wspólne dla wszystkich gatunków)
	deps.EventBus.CoinsAdded:Connect(function(player, amount)
		addXp(player, amount * config.XpFor.Coins)
	end)
	deps.EventBus.StageReached:Connect(function(player)
		addXp(player, config.XpFor.Stage)
	end)
	deps.EventBus.PlaytimeTick:Connect(function(player, deltaSeconds)
		addXp(player, deltaSeconds / 60 * config.XpFor.PlayMinute)
	end)

	print("[MegaObby] BattlePassService gotowy — sezon: " .. config.Tiers .. " poziomów po " .. config.XpPerTier .. " XP.")
end

return BattlePassService
