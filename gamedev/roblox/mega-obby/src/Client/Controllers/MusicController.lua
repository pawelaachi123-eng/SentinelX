--!strict
-- ============================================================================
-- MEGA OBBY — MusicController (Client/Controllers/MusicController)
-- Muzyka strefowa: serwer ustawia atrybut gracza "AmbientZone" (id wyspy),
-- klient gramy utwór z GameConfig.Ambient.Music. assetId = 0 → cisza (uczciwie:
-- brak wgranego dźwięku, żadnych cudzych assetów bez zgody). Dodatkowo krótkie
-- efekty dźwiękowe (WorldFX) — checkpoint/win — też z placeholderem 0.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local SoundService = game:GetService("SoundService")

local Music = {}

local player = Players.LocalPlayer

local state, config
local currentSound = nil
local currentZone = nil

local function stopCurrent()
	if currentSound then
		currentSound:Destroy()
		currentSound = nil
	end
	currentZone = nil
end

local function playZone(zoneId: string)
	if not state.Settings.Music then
		return
	end
	local definition = config.Ambient.Music[zoneId]
	if not definition then
		return
	end
	if definition.assetId == 0 then
		-- brak podpiętego dźwięku → cisza (bez błędu — to świadome)
		stopCurrent()
		return
	end
	if currentZone == zoneId and currentSound then
		return
	end
	stopCurrent()
	local sound = Instance.new("Sound")
	sound.Name = "MegaObbyMusic_" .. zoneId
	sound.SoundId = "rbxassetid://" .. definition.assetId
	sound.Looped = true
	sound.Volume = 0.35
	sound.Parent = SoundService
	sound:Play()
	currentSound = sound
	currentZone = zoneId
end

function Music.SetEnabled(enabled: boolean)
	state.Settings.Music = enabled
	if not enabled then
		stopCurrent()
	else
		local zone = player:GetAttribute("AmbientZone")
		if type(zone) == "string" then
			playZone(zone)
		end
	end
end

function Music.PlayFx(fxName: string)
	if not state.Settings.Effects then
		return
	end
	-- placeholder 0 = pomiń (brak wgranego dźwięku w tym repo — celowo,
	-- żeby nie używać cudzych assetów). Wpisz własne ID w tabeli poniżej.
	local fxIds = {
		checkpoint = 0,
		win = 0,
	}
	local assetId = fxIds[fxName]
	if assetId and assetId ~= 0 then
		local sound = Instance.new("Sound")
		sound.SoundId = "rbxassetid://" .. assetId
		sound.Volume = 0.5
		sound.Parent = SoundService
		sound.Ended:Connect(function()
			sound:Destroy()
		end)
		sound:Play()
	end
end

function Music.Init(stateIn, configIn)
	state, config = stateIn, configIn

	player:GetAttributeChangedSignal("AmbientZone"):Connect(function()
		local zone = player:GetAttribute("AmbientZone")
		if type(zone) == "string" then
			playZone(zone)
		end
	end)

	print("[MegaObby] MusicController gotowy (assetId 0 = cisza — wpisz własne dźwięki w GameConfig).")
end

return Music
