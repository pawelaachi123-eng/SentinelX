--!strict
-- ============================================================================
-- MEGA OBBY — Net (ReplicatedStorage.Shared.Net)
-- Wszystkie RemoteEvent/RemoteFunction tworzone w jednym miejscu, z limitem
-- częstości po stronie serwera (token bucket na gracza i na remote).
-- Zasada najlepszych praktyk: klient NIGDY nie ufa serwerowi, serwer NIGDY
-- nie ufa klientowi — każdy handler waliduje argumenty sam.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local Util = require(Shared:WaitForChild("Util"))

local Net = {}

-- Zdarzenia serwer → klient
Net.EVENTS = {
	"StateSync",   -- pełna migawka stanu gracza (throttled ~0,5 s)
	"StatChanged", -- (key, value) — szybkie zmiany pojedynczych statystyk
	"Notify",      -- (kind, message) — kind: "info" | "success" | "error"
	"WorldFX",     -- (fxName, cframe, extra) — efekty świata (checkpoint, win…)
}

-- Funkcje klient → serwer (z limitami: {wołań/sek, seria})
Net.FUNCTIONS = {
	"BuyTrail",    -- (trailId) -> ok: boolean, message: string
	"EquipTrail",  -- (trailId | nil) -> ok, message
	"BuyEgg",      -- (eggId) -> ok, message (roluje zwierzaka)
	"EquipPet",    -- (petUid | nil) -> ok, message
	"ClaimDaily",  -- () -> ok, message
	"ClaimQuest",  -- (questId) -> ok, message
	"Respawn",     -- () -> ok, message (klawisz R)
	"Train",       -- (symulator) -> ok, message
	"Shoot",       -- (shooter, Vector3 celownika) -> ok, message
	"ForgeBuy",    -- (itemId) -> ok, message — uniwersalny sklep gatunku
}

Net.LIMITS = {
	BuyTrail = { 0.5, 3 },
	EquipTrail = { 1, 3 },
	BuyEgg = { 0.5, 3 },
	EquipPet = { 1, 4 },
	ClaimDaily = { 0.2, 2 },
	ClaimQuest = { 0.5, 4 },
	Respawn = { 0.5, 2 },
	ForgeBuy = { 2, 6 },
}

local remoteFolder: Folder? = nil

local function ensureFolder(): Folder
	if not remoteFolder then
		local found = ReplicatedStorage:FindFirstChild("Remotes")
		if found and found:IsA("Folder") then
			remoteFolder = found
		else
			local created = Instance.new("Folder")
			created.Name = "Remotes"
			created.Parent = ReplicatedStorage
			remoteFolder = created
		end
	end
	return remoteFolder :: Folder
end

function Net:Get(name: string): Instance
	if not (table.find(Net.EVENTS, name) or table.find(Net.FUNCTIONS, name)) then
		error("[Net] Nieznany remote: " .. tostring(name))
	end
	-- Serwer tworzy remote'y; klient CZEKA (replikacja nie zawsze zdąży przed
	-- pierwszym require — FindFirstChild by tu tworzył lokalną atrapę).
	local folder
	if RunService:IsServer() then
		folder = ensureFolder()
	else
		folder = ReplicatedStorage:WaitForChild("Remotes", 10)
		if not folder then
			error("[Net] Serwer nie utworzył folderu Remotes w 10 s — czy Main.server.lua działa?")
		end
	end
	local remote = folder:FindFirstChild(name)
	if not remote and RunService:IsServer() then
		if table.find(Net.EVENTS, name) then
			remote = Instance.new("RemoteEvent")
		else
			remote = Instance.new("RemoteFunction")
		end
		remote.Name = name
		remote.Parent = folder
	end
	if not remote then
		remote = (folder :: Folder):WaitForChild(name, 10)
	end
	if not remote then
		error("[Net] Remote „" .. name .. "” nie dotarł z replikacją w 10 s")
	end
	return remote
end

-- ————————————————————————————————————————————————
-- SERWER
-- ————————————————————————————————————————————————
if RunService:IsServer() then
	local limiters = {} -- [remoteName][userId] = RateLimiter

	-- Net:OnServer(name, handler, limit?) — handler zwraca (ok, message, ...)
	-- albo sam rzuca błąd (pcall i tak ochroni wątek).
	function Net:OnServer(name: string, handler, limit)
		local remote = Net:Get(name)
		assert(remote:IsA("RemoteFunction"), name .. " musi być RemoteFunction")
		local perPlayer = {}
		limiters[name] = perPlayer
		remote.OnServerInvoke = function(player, ...)
			-- limit częstości
			local limiter = perPlayer[player.UserId]
			if not limiter then
				local rate, burst
				if limit then
					rate, burst = limit[1], limit[2]
				else
					local configured = Net.LIMITS[name]
					rate = configured and configured[1] or 1
					burst = configured and configured[2] or 3
				end
				limiter = Util.RateLimiter(rate, burst)
				perPlayer[player.UserId] = limiter
			end
			if not limiter:Allow() then
				return false, "Zwolnij trochę — za dużo poleceń naraz."
			end
			-- handler nie może położyć wątku
			local results = table.pack(pcall(handler, player, ...))
			if not results[1] then
				warn("[Net] Błąd handlera " .. name .. ": " .. tostring(results[2]))
				return false, "Coś się wysypało po naszej stronie — spróbuj ponownie."
			end
			return results[2], results[3], results[4]
		end
	end

	-- Net:SendTo(player, name, ...) — zdarzenie do jednego gracza
	function Net:SendTo(player, name, ...)
		local remote = Net:Get(name)
		assert(remote:IsA("RemoteEvent"), name .. " musi być RemoteEvent")
		remote:FireClient(player, ...)
	end

	-- Net:SendAll(name, ...) — do wszystkich
	function Net:SendAll(name, ...)
		local remote = Net:Get(name)
		assert(remote:IsA("RemoteEvent"), name .. " musi być RemoteEvent")
		remote:FireAllClients(...)
	end

	return Net
end

-- ————————————————————————————————————————————————
-- KLIENT
-- ————————————————————————————————————————————————
function Net:On(name: string, callback)
	local remote = Net:Get(name)
	assert(remote:IsA("RemoteEvent"), name .. " musi być RemoteEvent")
	return remote.OnClientEvent:Connect(callback)
end

-- Bezpieczne wywołanie: false + czytelny komunikat zamiast wiecznego wisienia.
function Net:Invoke(name: string, ...): (boolean, string)
	local remote = Net:Get(name)
	assert(remote:IsA("RemoteFunction"), name .. " musi być RemoteFunction")
	local ok, a, b = pcall(function()
		return remote:InvokeServer(...)
	end)
	if not ok then
		return false, "Połączenie z serwerem przerwane — spróbuj ponownie."
	end
	if a == nil then
		return false, "Serwer nie odpowiedział na „" .. name .. "”."
	end
	return a, b or ""
end

return Net
