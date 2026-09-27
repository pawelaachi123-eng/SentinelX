--!strict
-- ============================================================================
-- MEGA OBBY — QuestService (Server/Modules/QuestService)
-- Dzienna trójka zadań, ta sama dla wszystkich (seed: dzień UTC — NIE userId,
-- dzięki temu społeczność ma wspólne cele), postęp liczy się z EventBusa:
-- monety (realnie zdobyte, po mnożnikach), nowe etapy, czas gry (minuty).
-- Nagroda: monety dodane przez AddCoins (mnożniki premium też działają —
-- celowo: przepustka x2 nagradza też questa).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local QuestService = {}

local deps = nil
local config = nil

-- Deterministyczny wybór 3 questów dla danego dnia (ten sam dla każdego gracza)
local function questsForDay(dayKey: string)
	local seed = 0
	for i = 1, #dayKey do
		seed = seed + string.byte(dayKey, i) * (i * 31)
	end
	local rng = Random.new(seed)
	local pool = {}
	for index, quest in ipairs(config.QuestPool) do
		table.insert(pool, index)
	end
	local chosen = {}
	while #chosen < config.DailyQuestCount and #pool > 0 do
		local pickIndex = rng:NextInteger(1, #pool)
		local questIndex = table.remove(pool, pickIndex)
		table.insert(chosen, config.QuestPool[questIndex])
	end
	return chosen
end

local function ensureToday(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local dayKey = deps.Util.UtcDayKey()
	if not data.Quests[dayKey] then
		data.Quests[dayKey] = {}
		for _, quest in ipairs(questsForDay(dayKey)) do
			data.Quests[dayKey][quest.id] = { Progress = 0, Claimed = false }
		end
		-- sprzątanie: questy są dzienne, stare dni wycinamy (dane nie puchną)
		for storedKey in pairs(data.Quests) do
			if storedKey ~= dayKey then
				data.Quests[storedKey] = nil
			end
		end
	end
	return data.Quests[dayKey]
end

function QuestService.QuestsOf(player)
	local today = ensureToday(player)
	if not today then
		return {}
	end
	local dayKey = deps.Util.UtcDayKey()
	local result = {}
	for _, quest in ipairs(questsForDay(dayKey)) do
		local state = today[quest.id]
		if state then
			table.insert(result, {
				Id = quest.id,
				Desc = quest.desc,
				Progress = math.min(state.Progress, quest.target),
				Target = quest.target,
				Reward = quest.reward,
				Claimed = state.Claimed,
				Done = state.Progress >= quest.target,
			})
		end
	end
	return result
end

local function addProgress(player, questType: string, amount: number)
	local today = ensureToday(player)
	if not today then
		return
	end
	local dayKey = deps.Util.UtcDayKey()
	for _, quest in ipairs(questsForDay(dayKey)) do
		if quest.type == questType then
			local state = today[quest.id]
			if state and not state.Claimed and state.Progress < quest.target then
				state.Progress = state.Progress + amount
				if state.Progress >= quest.target then
					deps.Net:SendTo(player, "Notify", "success",
						"✅ Zadanie zaliczone: " .. quest.desc .. " — odbierz nagrodę w panelu zadań!")
				end
			end
		end
	end
	deps.PlayerDataService.PushSnapshot(player)
end

local function handleClaimQuest(player, questId)
	if type(questId) ~= "string" then
		return false, "Podaj identyfikator zadania."
	end
	local today = ensureToday(player)
	if not today then
		return false, "Dane jeszcze się ładują."
	end
	local state = today[questId]
	local definition = nil
	for _, quest in ipairs(config.QuestPool) do
		if quest.id == questId then
			definition = quest
			break
		end
	end
	if not (state and definition) then
		return false, "Nie znam takiego zadania."
	end
	if state.Claimed then
		return false, "Nagroda już odebrana."
	end
	if state.Progress < definition.target then
		return false, "Zadanie jeszcze niewykonane (" .. math.floor(state.Progress) .. "/" .. definition.target .. ")."
	end
	state.Claimed = true
	deps.PlayerDataService.AddCoins(player, definition.reward, "quest: " .. definition.desc)
	return true, "+" .. deps.Util.Format.Number(definition.reward) .. " monet z questa!"
end

function QuestService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("ClaimQuest", handleClaimQuest)

	-- postęp z magistrali zdarzeń
	deps.EventBus.CoinsAdded:Connect(function(player, amount)
		addProgress(player, "coins", amount)
	end)
	deps.EventBus.StageReached:Connect(function(player)
		addProgress(player, "stages", 1)
	end)
	deps.EventBus.PlaytimeTick:Connect(function(player, deltaSeconds)
		-- questy liczą minuty: przy okazji zaokrąglam spam zdarzeń
		addProgress(player, "playtime", deltaSeconds / 60)
	end)

	print("[MegaObby] QuestService gotowy — puli zadań: " .. #config.QuestPool .. ".")
end

return QuestService
