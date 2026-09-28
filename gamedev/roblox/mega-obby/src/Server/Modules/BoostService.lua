--!strict
-- ============================================================================
-- MEGA OBBY — BoostService (Server/Modules/BoostService)
-- Dwa mnożniki monet nakładane w PlayerDataService.AddCoins:
--  1. osobisty boost (spin/quest/oferta): x2 przez N sekund, liczony od czasu
--     serwera (data.Boosts.Coin.Until) — przetrwa rejoin, nie da się oszukać,
--  2. GLOBALNY EVENT serwerowy: co IntervalSeconds wszyscy dostaja x3 na
--     DurationSeconds — social pressure (gracze wzywają znajomych "lecimy!").
-- InfoOf/GlobalInfo zasila HUD (chip boosta) i snapshot.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local BoostService = {}

local deps = nil
local config = nil
local globalUntil = 0

function BoostService.Apply(player, data, kind: string, durationSeconds: number, multiplier: number)
	data.Boosts = data.Boosts or {}
	data.Boosts[kind] = { Multiplier = multiplier, Until = os.time() + durationSeconds }
	deps.Net:SendTo(player, "Notify", "success",
		"BOOST x" .. multiplier .. " aktywny przez " .. math.floor(durationSeconds / 60) .. " min!")
	deps.PlayerDataService.PushSnapshot(player)
end

-- aktywny mnożnik osobisty gracza (1 = brak)
function BoostService.PersonalMultiplier(data): number
	if not (data and data.Boosts) then
		return 1
	end
	local best = 1
	local now = os.time()
	for _, boost in pairs(data.Boosts) do
		if type(boost) == "table" and type(boost.Until) == "number" and boost.Until > now then
			best = math.max(best, boost.Multiplier or 1)
		end
	end
	return best
end

function BoostService.GlobalMultiplier(): number
	if os.time() < globalUntil then
		return config.GlobalBoost.Multiplier
	end
	return 1
end

function BoostService.GlobalInfo()
	local now = os.time()
	return { Active = now < globalUntil, Multiplier = config.GlobalBoost.Multiplier, EndsIn = math.max(0, globalUntil - now) }
end

function BoostService.InfoOf(player)
	local data = deps.PlayerDataService.Get(player)
	local globalInfo = BoostService.GlobalInfo()
	local personal, personalEndsIn = 1, 0
	if data and data.Boosts then
		local now = os.time()
		for _, boost in pairs(data.Boosts) do
			if type(boost) == "table" and type(boost.Until) == "number" and boost.Until > now then
				personal = boost.Multiplier or 1
				personalEndsIn = boost.Until - now
			end
		end
	end
	return {
		Personal = personal,
		PersonalEndsIn = personalEndsIn,
		Global = globalInfo.Active and globalInfo.Multiplier or 1,
		GlobalEndsIn = globalInfo.EndsIn,
	}
end

-- granty produktu "boost_x2" (opcjonalny produkt deweloperski)
BoostService.ProductGrants = nil

function BoostService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	-- pętla globalnych eventów: każdy jest LOSOWANY z Rotations (x3 monety, x5 krótko, dar dla wszystkich)
	task.spawn(function()
		while true do
			task.wait(config.GlobalBoost.IntervalSeconds)
			local picked = deps.Util.WeightedPick(config.GlobalBoost.Rotations, Random.new(os.time()))
			if picked.kind == "coins" then
				activeMultiplier = picked.multiplier
				globalUntil = os.time() + picked.duration
				deps.Net:SendAll("Notify", "success",
					"🔥 GLOBALNY EVENT: WSZYSTKIE MONETY x" .. picked.multiplier
					.. " przez " .. math.floor(picked.duration / 60) .. " min! WOŁAJ ZNAJOMYCH!")
			elseif picked.kind == "gift" then
				for _, player in ipairs(Players:GetPlayers()) do
					deps.PlayerDataService.AddCoins(player, picked.amount, "dar eventu")
				end
				deps.Net:SendAll("Notify", "success",
					"🎁 DAR EVENTU: +" .. picked.amount .. " monet dla KAŻDEGO online!")
			end
		end
	end)

	print("[MegaObby] BoostService gotowy — eventy losowane co "
		.. config.GlobalBoost.IntervalSeconds .. " s (rodzajów: " .. #config.GlobalBoost.Rotations .. ").")
end

return BoostService
