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
}

local deps = {
	Config = Config,
	Util = Util,
	Net = Net,
	EventBus = EventBus,
}

local ORDER = {
	"PlayerDataService",
	"EffectsService",
	"WorldBuilder",
	"StageService",
	"CoinService",
	"PetService",
	"TrailService",
	"MonetizationService",
	"QuestService",
	"DailyRewardService",
	"LeaderboardService",
	"AntiCheatService",
	"AdminService",
	"AmbientService",
}

for _, name in ipairs(ORDER) do
	local module = require(Modules:WaitForChild(name))
	assert(type(module) == "table", name .. " musi zwracać tabelę (ModuleScript)")
	assert(type(module.Start) == "function", name .. " musi mieć .Start(deps)")
	deps[name] = module
end

print("[MegaObby] " .. Config.GameName .. " v" .. Config.Version .. " — wczytano " .. #ORDER .. " modułów, startuję…")

for _, name in ipairs(ORDER) do
	local ok, err = pcall(deps[name].Start, deps)
	if not ok then
		warn("[MegaObby] BŁĄD STARTU " .. name .. ": " .. tostring(err))
	else
		print("[MegaObby] ✔ " .. name)
	end
end

-- Produkty i przepustki podpięte dopiero teraz — wszystkie granty zdążyły się
-- zarejestrować (patrz MonetizationService.Finish).
deps.MonetizationService.Finish(deps)

print("[MegaObby] START ZAKOŃCZONY — powodzenia na 120 etapach! 🎮")
