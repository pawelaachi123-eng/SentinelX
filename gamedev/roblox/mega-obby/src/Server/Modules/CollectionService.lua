--!strict
-- ============================================================================
-- ZBIORY / INDEKS (Server/Modules/CollectionService)
-- System kolekcji jak w dużych grach: pierwsze odkrycie rzeczy (ryby, pet,
-- coś z gatunku) wpada do indeksu gracza, a ZAKOMPLETOWANIE zestawu daje
-- nagrodę w monetach. Definicje zestawów: GameConfig.Collections.
-- Zapis: data.Collections (PlayerDataService migruje nowe klucze sam).
-- ============================================================================

local CollectionService = {}

function CollectionService.Start(deps)
	print("[Forge] ✔ CollectionService — zbiory/indeks gotowe ("
		.. #(deps.GameConfig.Collections or {}) .. " zestawów).")
end

-- Report(deps, player, "Złoty Koi") — odkrycie po nazwie wpisu z zestawów
function CollectionService.Report(deps, player, entryName)
	local data = deps.PlayerDataService.Get(player)
	if not data or type(entryName) ~= "string" then
		return
	end
	data.Collections = data.Collections or {}
	for _, set in ipairs(deps.GameConfig.Collections or {}) do
		for _, entry in ipairs(set.entries) do
			if entry == entryName then
				local discovered = data.Collections[set.id] or {}
				if not discovered[entryName] then
					discovered[entryName] = true
					data.Collections[set.id] = discovered
					local have = 0
					for _, e in ipairs(set.entries) do
						if discovered[e] then
							have += 1
						end
					end
					deps.Net:SendTo(player, "Notify", "info", "📖 INDEKS: "
						.. entryName .. " (" .. have .. "/" .. #set.entries .. " — " .. set.name .. ")")
					if have == #set.entries and not data.Collections["Done_" .. set.id] then
						data.Collections["Done_" .. set.id] = true
						deps.PlayerDataService.AddCoins(player, set.reward, "komplet indeksu: " .. set.name)
						deps.Net:SendTo(player, "Notify", "success",
							"🏆 KOMPLET INDEKSU „" .. set.name .. "” — +" .. set.reward .. " 🪙!")
					end
				end
				return
			end
		end
	end
end

-- Progress(deps, player, "ryby_staw") → "3/7" (do HUD)
function CollectionService.Progress(deps, player, setId)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return "0/0"
	end
	for _, set in ipairs(deps.GameConfig.Collections or {}) do
		if set.id == setId then
			local discovered = (data.Collections or {})[setId] or {}
			local have = 0
			for _, entry in ipairs(set.entries) do
				if discovered[entry] then
					have += 1
				end
			end
			return have .. "/" .. #set.entries
		end
	end
	return "0/0"
end

return CollectionService
