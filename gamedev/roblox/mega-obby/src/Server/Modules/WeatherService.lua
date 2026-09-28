--!strict
-- ============================================================================
-- POGODA (Server/Modules/WeatherService)
-- Serwer rotuje pogodę (słonecznie / deszcz / burza / TĘCZA) co
-- GameConfig.Weather.RotationSeconds — losowanie ważone. Pogoda daje
-- „szczęście” (luck): mnoży szansę na rzadkie ryby w gatunku ryby
-- (WeatherService.Luck()), a zmiana jest ogłaszana WSZYSTKIM na chacie.
-- ============================================================================

local WeatherService = {}

local current = { id = "slonko", name = "☀️ Słonecznie", luck = 1, weight = 45 }

function WeatherService.Start(deps)
	local config = deps.GameConfig.Weather
	task.spawn(function()
		while true do
			task.wait(config.RotationSeconds)
			local picked = deps.Util.WeightedPick(config.Types, Random.new(os.time() + math.random(1, 100000)))
			if picked.id ~= current.id then
				current = picked
				deps.Net:SendAll("Notify", "info",
					picked.name .. " — szczęście x" .. picked.luck .. " (rzadsze ryby wkrótce!)")
			end
		end
	end)
	print("[Forge] ✔ WeatherService — pogoda rotuje co " .. config.RotationSeconds .. " s ("
		.. #config.Types .. " typów).")
end

-- Luck() → mnożnik szczęścia (1 / 1.25 / 1.5 / 2)
function WeatherService.Luck()
	return current.luck
end

-- Label() → "⛈️ Burza (x1.5)" (do HUD)
function WeatherService.Label()
	return current.name .. " (x" .. current.luck .. ")"
end

return WeatherService
