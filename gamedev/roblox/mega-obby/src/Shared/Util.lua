--!strict
-- ============================================================================
-- MEGA OBBY — Util (ReplicatedStorage.Shared.Util)
-- Małe, sprawdzone narzędzia: Signal, Trove (sprzątanie), losowanie ważone,
-- limiter częstości, formaty liczb po polsku. Zero zależności poza standardem.
-- ============================================================================

local Util = {}

-- ————————————————————————————————————————————————
-- Signal — lekki odpowiednik RBXScriptSignal dla logiki serwera
-- ————————————————————————————————————————————————
export type SignalConnection = { Disconnect: (self: SignalConnection) -> () }

export type Signal = {
	Connect: (self: Signal, fn: (...any) -> ()) -> SignalConnection,
	Fire: (self: Signal, ...any) -> (),
}

local Signal = {}
Signal.__index = Signal

function Util.Signal(): Signal
	return setmetatable({ _handlers = {} }, Signal)
end

function Signal:Connect(fn)
	assert(type(fn) == "function", "Signal:Connect oczekuje funkcji")
	local handlers = self._handlers
	table.insert(handlers, fn)
	local connection = {}
	function connection.Disconnect()
		for i, handler in ipairs(handlers) do
			if handler == fn then
				table.remove(handlers, i)
				break
			end
		end
	end
	return connection :: SignalConnection
end

function Signal:Fire(...)
	-- task.spawn = handler nie zawiesi Fire, a wyjątek jednego słuchacza
	-- nie położy pozostałych (odpowiednik zachowania RBXScriptSignal).
	for _, handler in ipairs(table.clone(self._handlers)) do
		task.spawn(handler, ...)
	end
end

-- ————————————————————————————————————————————————
-- Trove — wsadzasz połączenia/instancje/funkcje, Clean() sprząta wszystko
-- (jedno wywołanie zamiast dziesięciu disconnectów).
-- ————————————————————————————————————————————————
export type Trove = {
	Add: (self: Trove, item: any, cleanupName: string?) -> any,
	Connect: (self: Trove, signal: any, fn: (...any) -> ()) -> SignalConnection,
	Clean: (self: Trove) -> (),
}

local Trove = {}
Trove.__index = Trove

function Util.Trove(): Trove
	return setmetatable({ _items = {} }, Trove)
end

function Trove:Add(item, cleanupName)
	table.insert(self._items, { item = item, name = cleanupName })
	return item
end

function Trove:Connect(signal, fn)
	local connection = signal:Connect(fn)
	self:Add(connection, "Disconnect")
	return connection
end

function Trove:Clean()
	for i = #self._items, 1, -1 do
		local entry = self._items[i]
		self._items[i] = nil
		local item, name = entry.item, entry.name
		local kind = typeof(item)
		if kind == "RBXScriptConnection" or name == "Disconnect" then
			pcall(function() item:Disconnect() end)
		elseif kind == "Instance" then
			item:Destroy()
		elseif kind == "table" and type(item.Destroy) == "function" then
			pcall(function() item:Destroy() end)
		elseif kind == "function" then
			pcall(item)
		end
	end
end

Util.TroveClass = Trove

-- ————————————————————————————————————————————————
-- Losowanie ważone: lista elementów z polem Weight (domyślnie 1).
-- rng = obiekt Random.new(seed) — wtedy deterministycznie; bez rng = math.random.
-- ————————————————————————————————————————————————
function Util.WeightedPick(items, rng)
	local total = 0
	for _, item in ipairs(items) do
		total = total + (item.Weight or 1)
	end
	local roll = nil
	if rng ~= nil and type(rng) ~= "number" and rng.NextNumber ~= nil then
		roll = rng:NextNumber(0, total)
	else
		roll = math.random() * total
	end
	local acc = 0
	for _, item in ipairs(items) do
		acc = acc + (item.Weight or 1)
		if roll <= acc then
			return item
		end
	end
	return items[#items]
end

-- ————————————————————————————————————————————————
-- RateLimiter — token bucket; Allow() = true, gdy wolno wykonać akcję.
-- Używany przez Net do ochrony RemoteFunction przed spamem.
-- ————————————————————————————————————————————————
export type RateLimiter = { Allow: (self: RateLimiter) -> boolean }

local RateLimiter = {}
RateLimiter.__index = RateLimiter

function Util.RateLimiter(ratePerSecond: number, burst: number): RateLimiter
	return setmetatable({
		_rate = ratePerSecond,
		_burst = math.max(burst, 1),
		_tokens = math.max(burst, 1),
		_last = os.clock(),
	}, RateLimiter)
end

function RateLimiter:Allow()
	local now = os.clock()
	local elapsed = now - self._last
	self._last = now
	self._tokens = math.min(self._burst, self._tokens + elapsed * self._rate)
	if self._tokens >= 1 then
		self._tokens = self._tokens - 1
		return true
	end
	return false
end

Util.RateLimiterClass = RateLimiter

-- ————————————————————————————————————————————————
-- Formaty liczb (polskie): 1234567 → „1 234 567”, „1,2 mln”
-- ————————————————————————————————————————————————
local Format = {}

function Format.Number(value: number): string
	local n = math.floor(tonumber(value) or 0)
	local s = tostring(n)
	local formatted = s:reverse():gsub("(%d%d%d)", "%1 ")
	formatted = (formatted :: string):reverse()
	formatted = (formatted :: string):gsub("^%s+", "")
	return formatted
end

function Format.Short(value: number): string
	local n = tonumber(value) or 0
	if n >= 1e9 then
		return (string.format("%.1f mld", n / 1e9):gsub("%.", ","))
	elseif n >= 1e6 then
		return (string.format("%.1f mln", n / 1e6):gsub("%.", ","))
	elseif n >= 1e4 then
		return (string.format("%.1f tys.", n / 1e3):gsub("%.", ","))
	end
	return Format.Number(n)
end

function Format.Time(totalSeconds: number): string
	local minutes = math.floor(totalSeconds / 60)
	local seconds = totalSeconds % 60
	return string.format("%d:%02d", minutes, seconds)
end

Util.Format = Format

-- ————————————————————————————————————————————————
-- TableUtil — minimum, którego naprawdę używam
-- ————————————————————————————————————————————————
local TableUtil = {}

function TableUtil.ShallowCopy(source)
	local copy = {}
	for key, value in pairs(source) do
		copy[key] = value
	end
	return copy
end

function TableUtil.DeepCopy(source)
	if type(source) ~= "table" then
		return source
	end
	local copy = {}
	for key, value in pairs(source) do
		copy[key] = TableUtil.DeepCopy(value)
	end
	return copy
end

function TableUtil.Count(source): number
	local n = 0
	for _ in pairs(source) do
		n = n + 1
	end
	return n
end

function TableUtil.FindWhere(list, predicate)
	for _, item in ipairs(list) do
		if predicate(item) then
			return item
		end
	end
	return nil
end

Util.TableUtil = TableUtil

-- ————————————————————————————————————————————————
-- Kolory z configu {r,g,b} → Color3
-- ————————————————————————————————————————————————
function Util.Color3FromRGB(parts): Color3
	return Color3.fromRGB(parts[1] or 255, parts[2] or 255, parts[3] or 255)
end

-- ————————————————————————————————————————————————
-- Dzień UTC jako „YYYY-MM-DD” — spójne nagrody/questy na całym świecie
-- ————————————————————————————————————————————————
function Util.UtcDayKey(): string
	return os.date("!%Y-%m-%d")
end

function Util.UtcDayNumber(): number
	return math.floor(os.time() / 86400)
end

return Util
