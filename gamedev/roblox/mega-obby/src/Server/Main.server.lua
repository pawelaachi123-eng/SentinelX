--!strict
-- ============================================================================
-- MEGA OBBY: Wyspy Przygód — bootstrap serwera (ServerScriptService.Server.Main)
-- Kolejność jest ważna: najpierw dane gracza i efekty, potem świat, potem
-- usługi, które na nich pracują. Monetyzacja rejestruje produkty na końcu
-- (ProcessReceipt może być ustawiony TYLKO RAZ w całej grze).
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local ServerScriptService = game:GetService("ServerScriptService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Config = require(Shared:WaitForChild("GameConfig"))
local Util = require(Shared:WaitForChild("Util"))
local Net = require(Shared:WaitForChild("Net"))

local Modules = script.Parent:WaitForChild("Modules")

-- Wspólna magistrala zdarzeń serwera (Bindable-like Signals, bez Instance)
local EventBus = {
	CoinsAdded = Util.Signal(),    -- (player, amount, total, reason)
	StageReached = Util.Signal(),  -- (player, stageNumber, isFirstTime)
	PetEquipped = Util.Signal(),   -- (player, petDefOrNil)
	PlaytimeTick = Util.Signal(),  -- (player, deltaSeconds)
	GamepassGranted = Util.Signal(), -- (player, passId)
	CoinsSpent = Util.Signal(),    -- (player, amount) — onboarding/statystyki
}

local deps = {
	Config = Config,
	Util = Util,
	Net = Net,
	EventBus = EventBus,
}

-- GAMEFORGE: dystrybutor gatunków — czyta GameSpec (TO wybrana przez ciebie gra)
-- i buduje ją: obby = klasyczny Mega Obby, inne gatunki = generowany świat.
deps.GameSpec = require(Shared:WaitForChild("GameSpec"))
deps.GenresFolder = script.Parent:WaitForChild("Genres")

print("[MegaObby] " .. Config.GameName .. " v" .. Config.Version .. " — wczytuję moduły…")

local Forge = require(Modules:WaitForChild("Forge"))
local ok, err = pcall(Forge.Run, deps)
if not ok then
	warn("[MegaObby] BŁĄD KRYTYCZNY FORGE: " .. tostring(err))
end

-- Produkty i przepustki podpięte dopiero teraz — wszystkie granty zdążyły się
-- zarejestrować (patrz MonetizationService.Finish).
if deps.MonetizationService then
	deps.MonetizationService.Finish(deps)
end

print("[MegaObby] START ZAKOŃCZONY — „" .. deps.GameSpec.Name .. "” (" .. tostring(deps.Forge and deps.Forge.Genre or "obby") .. ") czeka na graczy! 🎮")
