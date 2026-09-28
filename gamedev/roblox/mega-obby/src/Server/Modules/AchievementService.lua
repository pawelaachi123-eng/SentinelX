--!strict
-- ============================================================================
-- OSIĄGNIĘCIA (Server/Modules/AchievementService)
-- Progi statystyk nagradzane RAZ (idempotentnie): złów pierwszą rybę,
-- przebiegnij 1500 studów, zbierz 100 plonów, dotrzyj do fali 10… Definicje:
-- GameConfig.Achievements. Zapis: data.Achievements. Pętla serwera co 20 s
-- sprawdza wszystkich graczy — osiągnięcia działają w KAŻDYM gatunku.
-- ============================================================================

local Players = game:GetService("Players")

local AchievementService = {}

function AchievementService.Start(deps)
	task.spawn(function()
		while true do
			task.wait(20)
			local config = deps.GameConfig.Achievements or {}
			for _, player in ipairs(Players:GetPlayers()) do
				local data = deps.PlayerDataService.Get(player)
				if data then
					data.Achievements = data.Achievements or {}
					for _, achievement in ipairs(config) do
						if not data.Achievements[achievement.id]
							and (data.Stats[achievement.stat] or 0) >= achievement.threshold then
							data.Achievements[achievement.id] = true
							deps.PlayerDataService.AddCoins(player, achievement.reward, "osiągnięcie: " .. achievement.desc)
							deps.Net:SendTo(player, "Notify", "success",
								"🏅 OSIĄGNIĘCIE: " .. achievement.desc .. " — +" .. achievement.reward .. " 🪙!")
						end
					end
				end
			end
		end
	end)
	print("[Forge] ✔ AchievementService — osiągnięcia działają ("
		.. #(deps.GameConfig.Achievements or {}) .. " progów).")
end

-- Count(deps, player) → "2/8" (do HUD lub popupów)
function AchievementService.Count(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return "0/0"
	end
	local config = deps.GameConfig.Achievements or {}
	local done = 0
	for _, achievement in ipairs(config) do
		if (data.Achievements or {})[achievement.id] then
			done += 1
		end
	end
	return done .. "/" .. #config
end

return AchievementService
