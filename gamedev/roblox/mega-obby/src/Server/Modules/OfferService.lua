--!strict
-- ============================================================================
-- MEGA OBBY — OfferService (Server/Modules/OfferService)
-- Oferty STARTER/MEGA z REALNYM licznikiem (znikają 72 h / 168 h od PIERWSZEGO
-- wejścia — pilnuje serwer po data.Stats.FirstJoinAt, nie fake countdown).
-- Jednorazowe (data.Offers kupione), grant idempotentny przez ProductGrants.
-- Najskuteczniejszy pojedynczy przychód w grach Roblox: pokaż na starcie,
-- daj realną wartość, nie przedłużaj po terminie.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local OfferService = {}

local deps = nil
local config = nil

function OfferService.GrantOffer(player, offer): (boolean, string)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	data.Offers = data.Offers or {}
	if data.Offers[offer.id] then
		return true, "Zestaw już odebrany (bez duplikatów)."
	end
	data.Offers[offer.id] = true
	if offer.coins and offer.coins > 0 then
		deps.PlayerDataService.AddCoins(player, offer.coins, "zestaw: " .. offer.id)
	end
	if offer.spins and offer.spins > 0 and deps.SpinService then
		for _ = 1, offer.spins do
			deps.SpinService.GrantSpin(player)
		end
	end
	deps.PlayerDataService.SavePlayer(player) -- zakup utrwalony natychmiast
	return true, offer.name .. " odblokowany!"
end

function OfferService.ProductGrants(_deps)
	local grants = {}
	for _, offer in ipairs(config.Offers) do
		grants[offer.id] = function(player)
			return OfferService.GrantOffer(player, offer)
		end
	end
	return grants
end

function OfferService.VisibleOf(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return {}
	end
	data.Stats.FirstJoinAt = data.Stats.FirstJoinAt or os.time()
	local now = os.time()
	local result = {}
	for _, offer in ipairs(config.Offers) do
		if not ((data.Offers or {})[offer.id]) then
			local expiresIn = (data.Stats.FirstJoinAt + offer.expireHoursFromJoin * 3600) - now
			if expiresIn > 0 then
				table.insert(result, {
					Id = offer.id,
					Name = offer.name,
					Description = offer.description,
					Label = offer.label,
					Coins = offer.coins,
					Spins = offer.spins,
					ExpiresIn = expiresIn,
					RobuxProductId = offer.robuxProductId,
				})
			end
		end
	end
	return result
end

function OfferService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	print("[MegaObby] OfferService gotowy — ofert limitowanych: " .. #config.Offers .. ".")
end

return OfferService
