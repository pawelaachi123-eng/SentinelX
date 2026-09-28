--!strict
-- ============================================================================
-- MEGA OBBY — MonetizationService (Server/Modules/MonetizationService)
-- Przepustki (Game Passes) + produkty deweloperskie (Dev Products).
--
-- NAJWAŻNIEJSZE PRAKTYKI, których tu pilnuję:
--  • MarketplaceService.ProcessReceipt ustawiam DOKŁADNIE RAZ (w Finish(), po
--    zarejestrowaniu wszystkich grantów — inaczej Roblox wyrzuca błąd),
--  • paragony idempotentne: PurchaseId zapisywany w danych gracza, więc duplikat
--    paragonu NIE da podwójnej nagrody,
--  • grant wykonany → natychmiastowy zapis (awaria serwera nie odbierze zakupu),
--  • nieznany paragon → NotProcessedYet (Roblox wróci z nim później),
--  • ID w GameConfig są 0 do czasu publikacji — wtedy UI uczciwie tłumaczy,
--    że twórca musi podpiąć własne ID (README-GRA.md, sekcja monetyzacji).
-- ============================================================================

local MarketplaceService = game:GetService("MarketplaceService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local MonetizationService = {}

local deps = nil
local config = nil
local productGrants = {} -- [robuxProductId] = fn(player) -> ok, msg
local receiptHandlerSet = false

local function notify(player, kind, message)
	deps.Net:SendTo(player, "Notify", kind, message)
end

local function applyWalkSpeed(player)
	local character = player.Character
	local humanoid = character and character:FindFirstChildOfClass("Humanoid")
	if not humanoid then
		return
	end
	local multiplier = deps.PlayerDataService.HasGamepass(player, "speed25")
		and config.Player.SpeedPassMultiplier
		or 1
	humanoid.WalkSpeed = config.Player.BaseWalkSpeed * multiplier
end

-- ————————————————————————————————————————————————
-- Granty przepustek
-- ————————————————————————————————————————————————
local function grantGamepassEffects(player, passId)
	if passId == "speed25" then
		applyWalkSpeed(player)
	elseif passId == "vip" then
		local data = deps.PlayerDataService.Get(player)
		if data and not data.EquippedTrail then
			-- złoty ślad VIP: jeśli jeszcze kupiony w ramach passu, nadaj od razu
			local hasGold = false
			for _, trailId in ipairs(data.Trails) do
				if trailId == "trail_gold" then
					hasGold = true
				end
			end
			if not hasGold then
				table.insert(data.Trails, "trail_gold")
			end
			data.EquippedTrail = "trail_gold"
			deps.EffectsService.ApplyTrail(player)
		end
	end
	deps.EventBus.GamepassGranted:Fire(player, passId)
end

local function refreshOwnership(player)
	for _, pass in ipairs(config.Gamepasses) do
		if pass.robuxProductId and pass.robuxProductId > 0 and not deps.PlayerDataService.HasGamepass(player, pass.id) then
			local ok, owns = pcall(function()
				return MarketplaceService:UserOwnsGamePassAsync(player.UserId, pass.robuxProductId)
			end)
			if ok and owns then
				deps.PlayerDataService.GrantGamepass(player, pass.id)
				grantGamepassEffects(player, pass.id)
			end
		end
	end
end

-- ————————————————————————————————————————————————
-- Rejestracja grantów produktów (wywoływana w Start)
-- ————————————————————————————————————————————————
local function registerGrants()
	local coinPacks = {
		coins_small = 500,
		coins_medium = 2000,
		coins_big = 6000,
	}
	for productId, amount in pairs(coinPacks) do
		productGrants[productId] = function(player)
			deps.PlayerDataService.AddCoins(player, amount, "zakup: " .. productId)
			return true, "+" .. amount .. " monet!"
		end
	end

	productGrants["egg_premium"] = function(player)
		if deps.PetService and deps.PetService.GrantEggRoll then
			return deps.PetService.GrantEggRoll(player, "egg_sky")
		end
		return false, "Ten produkt działa w trybie obby."
	end

	productGrants["skip_stage"] = function(player)
		if deps.StageService then
			return deps.StageService.SkipStage(player)
		end
		return false, "Ten produkt działa w trybie obby."
	end

	-- gatunek (Forge) może dołożyć własne granty (np. darmowe narodziny w symulatorze)
	if deps.MonetizationGrants then
		for grantId, grant in pairs(deps.MonetizationGrants(deps)) do
			productGrants[grantId] = grant
		end
	end

	-- usługi monetyzacji PRO doklejają swoje granty produktów (spin/oferty/paszport/offline)
	for _, source in ipairs({ deps.SpinService, deps.OfferService, deps.BattlePassService, deps.OfflineEarningsService }) do
		if source and source.ProductGrants then
			local okGrants, grants = pcall(source.ProductGrants, deps)
			if okGrants and type(grants) == "table" then
				for grantId, grant in pairs(grants) do
					productGrants[grantId] = grant
				end
			end
		end
	end
end

-- ————————————————————————————————————————————————
-- START
-- ————————————————————————————————————————————————
function MonetizationService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	registerGrants()

	-- właściciela przepustki rozpoznajemy też po zakupie w trakcie gry
	MarketplaceService.PromptGamePassPurchaseFinished:Connect(function(player, gamePassId, wasPurchased)
		if not wasPurchased then
			return
		end
		for _, pass in ipairs(config.Gamepasses) do
			if pass.robuxProductId == gamePassId then
				deps.PlayerDataService.GrantGamepass(player, pass.id)
				grantGamepassEffects(player, pass.id)
				notify(player, "success", "Dziękujemy za „" .. pass.name .. "”! 🎉")
				deps.PlayerDataService.SavePlayer(player)
				deps.PlayerDataService.PushSnapshot(player)
				return
			end
		end
	end)

	-- nowe postaci: prędkość wg przepustki
	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function()
			task.wait(0.2)
			applyWalkSpeed(player)
		end)
		deps.PlayerDataService.WhenReady(player, function()
			refreshOwnership(player)
			applyWalkSpeed(player)
		end)
	end)

	print("[MegaObby] MonetizationService gotowy — przepustek: "
		.. #config.Gamepasses .. ", produktów: " .. #config.DevProducts .. ".")
end

-- ————————————————————————————————————————————————
-- FINISH: jedyny ProcessReceipt w grze (Main woła na końcu startu)
-- ————————————————————————————————————————————————
function MonetizationService.Finish(_deps)
	if receiptHandlerSet then
		return
	end
	receiptHandlerSet = true

	MarketplaceService.ProcessReceipt = function(receiptInfo)
		local player = Players:GetPlayerByUserId(receiptInfo.PlayerId)
		if not player then
			-- gracz wyszedł — paragon wróci (Roblox ponawia do PurchaseGranted)
			return Enum.ProductPurchaseDecision.NotProcessedYet
		end
		-- idempotentność najpierw (najtańsza obrona przed duplikatem)
		if not deps.PlayerDataService.ClaimReceipt(player, tostring(receiptInfo.PurchaseId)) then
			return Enum.ProductPurchaseDecision.PurchaseGranted
		end
		-- czy dane w ogóle zdążyły się wczytać?
		if not deps.PlayerDataService.Get(player) then
			deps.PlayerDataService.WhenReady(player, function()
				notify(player, "info", "Twój zakup zaraz zostanie doliczony (dane się wczytywały).")
			end)
			return Enum.ProductPurchaseDecision.NotProcessedYet
		end
		-- znajdź produkt po ID Roblox
		for _, product in ipairs(config.DevProducts) do
			if product.robuxProductId == receiptInfo.ProductId then
				local grant = productGrants[product.id]
				if not grant then
					warn("[MegaObby] Produkt bez grantu: " .. product.id)
					return Enum.ProductPurchaseDecision.NotProcessedYet
				end
				local ok, message = grant(player)
				if ok then
					notify(player, "success", message or "Dziękujemy za zakup!")
					deps.PlayerDataService.SavePlayer(player)
					deps.PlayerDataService.PushSnapshot(player)
					return Enum.ProductPurchaseDecision.PurchaseGranted
				end
				warn("[MegaObby] Grant nie powiódł się (" .. product.id .. "): " .. tostring(message))
				return Enum.ProductPurchaseDecision.NotProcessedYet
			end
		end
		-- oferty limitowane mają własne ID produktów (poza DevProducts)
		if deps.OfferService then
			for _, offer in ipairs(config.Offers) do
				if offer.robuxProductId == receiptInfo.ProductId then
					local ok, message = deps.OfferService.GrantOffer(player, offer)
					if ok then
						notify(player, "success", message)
						deps.PlayerDataService.SavePlayer(player)
						deps.PlayerDataService.PushSnapshot(player)
						return Enum.ProductPurchaseDecision.PurchaseGranted
					end
					return Enum.ProductPurchaseDecision.NotProcessedYet
				end
			end
		end
		warn("[MegaObby] Nieznany ProductId w paragonie: " .. tostring(receiptInfo.ProductId))
		return Enum.ProductPurchaseDecision.NotProcessedYet
	end

	print("[MegaObby] ProcessReceipt podpięty (jedyny w grze).")
end

return MonetizationService
