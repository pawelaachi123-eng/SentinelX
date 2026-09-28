--!strict
-- ============================================================================
-- GAMEFORGE — Forge (Server/Modules/Forge)
-- Serce generatora: czyta GameSpec, wybiera moduł gatunku, buduje świat i startuje
-- wspólne usługi (dane, sklep kosmetyczny, monetyzacja, questy, tablice, admin).
-- Gatunek "obby" zachowuje DOKŁADNIE dotychczasową ścieżkę Mega Obby (zero regresji).
-- Moduł gatunku to kontrakt:
--   Pack.Id · Pack.BuildWorld(folders) -> { SpawnPad } · Pack.Setup(deps, world)
--   Pack.Hud(deps, player) · Pack.Shop(deps, data) · Pack.QuestPool
--   Pack.LeaderValue(deps, player) · Pack.MonetizationGrants(deps) [opcjonalnie]
--   Pack.OnCharacter(deps, player, character) [opcjonalnie] · Pack.SkipAmbient
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Workspace = game:GetService("Workspace")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Util = require(Shared:WaitForChild("Util"))

local Forge = {}

-- registry gatunków: klucz = wartość w GameSpec.Genre, wartość = moduł w Server/Genres
local GENRE_MODULES = {
	obby = nil, -- klasyczna ścieżka Mega Obby (WorldBuilder + StageService + CoinService + PetService)
	symulator = "SimulatorGenre",
	tycoon = "TycoonGenre",
	horror = "HorrorGenre",
	shooter = "ShooterGenre",
	wyscigi = "RacingGenre",
	biegacz = "BiegaczGenre",
	farma = "FarmaGenre",
	fale = "FaleGenre",
}

Forge.GenreAliases = {
	simulator = "symulator", clicker = "symulator", klikanie = "symulator",
	biznes = "tycoon", fabryka = "tycoon", empire = "tycoon",
	przetrwanie = "horror", strach = "horror",
	strzelanka = "shooter", fps = "shooter", strzelanie = "shooter",
	wyscig = "wyscigi", racing = "wyscigi", samochody = "wyscigi", tor = "wyscigi",
	parkour = "obby", przeszkody = "obby",
	bieg = "biegacz", runner = "biegacz", bieganie = "biegacz", dystans = "biegacz",
	farm = "farma", rolnik = "farma", sadzenie = "farma", ogrod = "farma", plony = "farma",
	wave = "fale", obrona = "fale", zombie = "fale", krysztal = "fale",
}

-- Klasyczna ścieżka Mega Obby — kolejność bez zmian + nowe usługi monetyzacji PRO
local OBBY_ORDER = {
	"PlayerDataService", "BoostService", "GroupBonusService", "OnboardingService",
	"SpinService", "OfferService", "BattlePassService", "OfflineEarningsService",
	"EffectsService", "WorldBuilder", "StageService", "CoinService",
	"PetService", "TrailService", "MonetizationService", "QuestService", "DailyRewardService",
	"LeaderboardService", "AntiCheatService", "AdminService", "AmbientService",
}

-- Wspólne usługi dla WSZYSTKICH wygenerowanych gatunków
local COMMON_ORDER = {
	"PlayerDataService", "BoostService", "GroupBonusService", "OnboardingService",
	"SpinService", "OfferService", "BattlePassService", "OfflineEarningsService",
	"EffectsService", "TrailService", "ForgeShopService",
	"MonetizationService", "QuestService", "DailyRewardService", "LeaderboardService",
	"AntiCheatService", "AdminService",
}

function Forge.RunOrder(deps, order)
	for _, name in ipairs(order) do
		local module = require(deps.Modules:WaitForChild(name))
		assert(type(module) == "table", name .. " musi zwracać tabelę (ModuleScript)")
		assert(type(module.Start) == "function", name .. " musi mieć .Start(deps)")
		deps[name] = module
	end
	for _, name in ipairs(order) do
		local ok, err = pcall(deps[name].Start, deps)
		if not ok then
			warn("[Forge] BŁĄD STARTU " .. name .. ": " .. tostring(err))
		else
			print("[Forge] ✔ " .. name)
		end
	end
end

local function runObby(deps)
	deps.Forge = { Genre = "obby" }
	print("[Forge] Gatunek: OBBY — klasyczny Mega Obby (" .. deps.GameSpec.Name .. ").")
	Forge.RunOrder(deps, OBBY_ORDER)
end

local function runGenre(deps, genreKey, moduleName)
	local pack = require(deps.GenresFolder:WaitForChild(moduleName))
	deps.Forge = { Genre = genreKey, Pack = pack }
	deps.GameSpec.Genre = genreKey

	print("[Forge] Gatunek: " .. string.upper(genreKey) .. " — generuję „" .. deps.GameSpec.Name .. "”…")

	-- świat z kodu (deterministycznie) + foldery, których sądzą wspólne usługi
	local root = Instance.new("Folder")
	root.Name = "ForgeWorld"
	root.Parent = Workspace
	local folders = {}
	for _, folderName in ipairs({ "World", "Effects", "Pickups" }) do
		local folder = Instance.new("Folder")
		folder.Name = folderName
		folder.Parent = root
		folders[folderName] = folder
	end
	-- shim dla EffectsService.BurstAt (używa deps.WorldBuilder.Result.Folders.Effects)
	deps.WorldBuilder = { Result = { Folders = { Effects = folders.Effects } } }

	local world = pack.BuildWorld(folders, deps) or {}
	world.Folders = folders

	-- dostawcy dla usług wspólnych (MUSZĄ być przed startem MonetizationService)
	deps.HudProvider = function(player)
		return pack.Hud and pack.Hud(deps, player) or nil
	end
	deps.QuestPoolProvider = function()
		return pack.QuestPool
	end
	deps.LeaderValueProvider = function(player)
		return pack.LeaderValue and pack.LeaderValue(deps, player) or 0
	end
	deps.MonetizationGrants = pack.MonetizationGrants

	if not pack.SkipAmbient then
		table.insert(COMMON_ORDER, "AmbientService") -- horror wyłącza cykl dnia (ciemność!)
	end

	Forge.RunOrder(deps, COMMON_ORDER)

	-- respawn zawsze na padzie startowym gatunku
	if world.SpawnPad then
		deps.PlayerDataService.OnLoaded:Connect(function(player)
			player.RespawnLocation = world.SpawnPad
		end)
	end

	-- hook postaci (np. stała prędkość z ulepszeń w wyścigach)
	if pack.OnCharacter then
		local function onCharacter(player, character)
			task.wait(0.3)
			pcall(pack.OnCharacter, deps, player, character)
		end
		Players.PlayerAdded:Connect(function(player)
			player.CharacterAdded:Connect(function(character)
				onCharacter(player, character)
			end)
		end)
		for _, player in ipairs(Players:GetPlayers()) do
			if player.Character then
				task.spawn(onCharacter, player, player.Character)
			end
			player.CharacterAdded:Connect(function(character)
				onCharacter(player, character)
			end)
		end
	end

	-- logika gatunku na końcu (remote'y, pętle rozgrywki)
	local ok, err = pcall(pack.Setup, deps, world)
	if not ok then
		warn("[Forge] BŁĄD LOGIKI GATUNKU " .. genreKey .. ": " .. tostring(err))
	else
		print("[Forge] ✔ Logika gatunku " .. genreKey .. " działa. Miłego grania!")
	end
end

function Forge.Run(deps)
	local spec = deps.GameSpec
	local genreKey = string.lower(tostring(spec.Genre or "obby"))
	genreKey = Forge.GenreAliases[genreKey] or genreKey
	local moduleName = GENRE_MODULES[genreKey]
	if genreKey == "obby" then
		return runObby(deps)
	end
	if not moduleName then
		warn("[Forge] Nie znam gatunku „" .. tostring(spec.Genre) .. "”. Dostępne: obby, symulator, tycoon, horror, shooter, wyscigi, biegacz, farma, fale. Wracam do obby.")
		return runObby(deps)
	end
	return runGenre(deps, genreKey, moduleName)
end

return Forge
