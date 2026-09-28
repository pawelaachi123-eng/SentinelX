--!strict
-- ============================================================================
-- GAMEFORGE — ForgeShopService (Server/Modules/ForgeShopService)
-- Uniwersalny sklep GATUNKU: pozycje definiuje moduł gatunku (Pack.Shop),
-- kupno przez remote ForgeBuy — cena walidowana na serwerze, pozycje "stat"
-- podnoszą poziom w data.Stats, "special" idą do Pack.OnBuy. Klient dostaje
-- listę w snapshotcie (ShopItems) — nigdy nie zgłasza zakupu po swojej stronie.
-- ============================================================================

local ForgeShopService = {}

function ForgeShopService.Start(deps)
	local pack = deps.Pack
	if not (pack and pack.Shop) then
		-- gatunek bez sklepu — pusta lista (klient pokaże informację)
		deps.ShopProvider = function()
			return {}
		end
		return
	end

	deps.ShopProvider = function(player)
		local ok, result = pcall(pack.Shop, deps, deps.PlayerDataService.Get(player))
		if ok and type(result) == "table" then
			return result
		end
		return {}
	end

	deps.Net:OnServer("ForgeBuy", function(player, itemId)
		if type(itemId) ~= "string" then
			return false, "Podaj przedmiot."
		end
		local data = deps.PlayerDataService.Get(player)
		if not data then
			return false, "Dane jeszcze się ładują."
		end
		for _, item in ipairs(deps.ShopProvider(player)) do
			if item.Id == itemId then
				if item.Owned then
					return false, "To już masz."
				end
				local price = tonumber(item.Price) or 0
				if price > 0 and not deps.PlayerDataService.TrySpend(player, price) then
					return false -- TrySpend samo tłumaczy brak monet
				end
				if item.Kind == "stat" then
					local key = tostring(item.Stat or item.Id)
					data.Stats[key] = (data.Stats[key] or 0) + (tonumber(item.Amount) or 1)
				end
				if pack.OnBuy then
					local ok, result = pcall(pack.OnBuy, deps, player, item)
					if not ok or result == false then
						-- zwrot: zakup nie doszedł do skutku (monety nie przepadają)
						if price > 0 then
							deps.PlayerDataService.AddCoins(player, price, "zwrot zakupu")
						end
						if not ok then
							warn("[ForgeShop] OnBuy padł (" .. itemId .. "): " .. tostring(result))
						end
						return false, "Zakup nie doszedł do skutku — monety wróciły."
					end
				end
				deps.PlayerDataService.PushSnapshot(player)
				return true, "Kupiono: " .. tostring(item.Name or itemId)
			end
		end
		return false, "Nie znam takiego przedmiotu."
	end)

	print("[ForgeShop] Sklep gatunku gotowy (ForgeBuy z walidacją serwera).")
end

return ForgeShopService
