--!strict
-- ============================================================================
-- MEGA OBBY — AmbientService (Server/Modules/AmbientService)
-- Cykl dnia i nocy (płynny ClockTime) oraz strefy muzyki: atrybut gracza
-- "AmbientZone" = id strefy wg aktualnej wyspy; klient (MusicController)
-- czyta atrybut i gra utwór z GameConfig.Ambient.Music (0 = cisza).
-- ============================================================================

local Lighting = game:GetService("Lighting")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local AmbientService = {}

local deps = nil
local config = nil

function AmbientService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	Lighting.ClockTime = config.Ambient.StartClockTime

	-- cykl doby
	local clockTime = config.Ambient.StartClockTime
	RunService.Heartbeat:Connect(function(dt)
		clockTime = (clockTime + dt * 24 / config.Ambient.DayLengthSeconds) % 24
		Lighting.ClockTime = clockTime
	end)

	-- strefy muzyki: etap → wyspa → atrybut gracza (replikuje się na klienta)
	task.spawn(function()
		while true do
			task.wait(2)
			for _, player in ipairs(Players:GetPlayers()) do
				local data = deps.PlayerDataService.Get(player)
				if data then
					local island = config.IslandOf(data.Stage)
					local zone = data.Stage < 1 and "lobby" or island.id
					if player:GetAttribute("AmbientZone") ~= zone then
						player:SetAttribute("AmbientZone", zone)
					end
				end
			end
		end
	end)

	print("[MegaObby] AmbientService gotowy — doba: " .. config.Ambient.DayLengthSeconds .. " s.")
end

return AmbientService
