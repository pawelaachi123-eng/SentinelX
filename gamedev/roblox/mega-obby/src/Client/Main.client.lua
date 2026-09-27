--!strict
-- ============================================================================
-- MEGA OBBY — Main (klient) (StarterPlayerScripts.Client.Main)
-- Spinamy kontrolery: HUD, sklep, ustawienia, muzyka. Wspólny stan w jednej
-- tabeli `state` (migawka z serwera + ustawienia lokalne). Klawisz R = respawn
-- na ostatnim checkpoincie.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared:WaitForChild("GameConfig"))
local Net = require(Shared:WaitForChild("Net"))

local Controllers = script.Parent:WaitForChild("Controllers")
local Hud = require(Controllers:WaitForChild("HudController"))
local ShopUI = require(Controllers:WaitForChild("ShopUI"))
local SettingsUI = require(Controllers:WaitForChild("SettingsUI"))
local Music = require(Controllers:WaitForChild("MusicController"))

local player = Players.LocalPlayer

-- ——— wspólny stan klienta ———
local state = {
	Snapshot = nil,
	Settings = { Music = true, Effects = true, Fov = 70 },
}

Hud.Init(state, Config, Net)
ShopUI.Init(state, Config, Net)
SettingsUI.Init(state, Config, Net, Music)
Music.Init(state, Config)

Net:On("StateSync", function(snapshot)
	state.Snapshot = snapshot
	Hud.OnSnapshot(snapshot)
	ShopUI.OnSnapshot(snapshot)
end)

Net:On("Notify", function(kind, message)
	Hud.Notify(kind, message)
end)

Net:On("StatChanged", function(key, value)
	Hud.OnStatChanged(key, value)
end)

Net:On("WorldFX", function(fxName, _cframe, _extra)
	Music.PlayFx(fxName)
end)

-- R = szybki powrót na checkpoint (gdy spadniesz w dziurę bez śmierci itd.)
UserInputService.InputBegan:Connect(function(input, processed)
	if processed then
		return
	end
	if input.KeyCode == Config.Player.RespawnKey then
		task.spawn(function()
			Net:Invoke("Respawn")
		end)
	end
end)

print("[MegaObby] Klient gotowy — powodzenia! (R = powrót na checkpoint)")
