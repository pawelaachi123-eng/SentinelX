--!strict
-- ============================================================================
-- MEGA OBBY — LeaderboardService (Server/Modules/LeaderboardService)
-- Dwie tablice w lobby (tag Leaderboard + attribute BoardId: "stages"/"coins")
-- zasilane z OrderedDataStore (top 10). Wyniki wysyłane co 2 min, odczyt co
-- 60 s. Nazwy użytkowników cache'owane (GetNameFromUserIdAsync bywa drogie).
-- Błędy DataStore nie wywalają pętli — tylko cichy log i próba za 60 s.
-- ============================================================================

local DataStoreService = game:GetService("DataStoreService")
local Players = game:GetService("Players")
local CollectionService = game:GetService("CollectionService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local LeaderboardService = {}

local deps = nil
local config = nil
local boards = {} -- [boardId] = { store, surface: SurfaceGui }
local nameCache = {} -- [userId] = name

local PUSH_SECONDS = 120
local READ_SECONDS = 60

local function nameFor(userId: number): string
	if nameCache[userId] then
		return nameCache[userId]
	end
	local ok, name = pcall(function()
		return Players:GetNameFromUserIdAsync(userId)
	end)
	if ok and name then
		nameCache[userId] = name
		return name
	end
	return "gracz #" .. userId
end

local function buildSurface(stand)
	local gui = Instance.new("SurfaceGui")
	gui.Face = Enum.NormalId.Back
	gui.CanvasSize = Vector2.new(600, 460)
	gui.Parent = stand
	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, 0, 0.18, 0)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.GothamBold
	title.TextScaled = true
	title.TextColor3 = Color3.fromRGB(34, 211, 238)
	title.Text = "TOP 10"
	title.Parent = gui
	local rows = Instance.new("TextLabel")
	rows.Size = UDim2.new(0.94, 0, 0.78, 0)
	rows.Position = UDim2.new(0.03, 0, 0.2, 0)
	rows.BackgroundTransparency = 1
	rows.Font = Enum.Font.Gotham
	rows.TextScaled = true
	rows.TextWrapped = true
	rows.TextXAlignment = Enum.TextXAlignment.Left
	rows.TextColor3 = Color3.fromRGB(235, 240, 250)
	rows.Text = "Wczytywanie…"
	rows.Parent = gui
	return rows
end

local function watchStands()
	local function arm(stand)
		local boardId = stand:GetAttribute("BoardId")
		if type(boardId) ~= "string" or boards[boardId] then
			return
		end
		local ok, store = pcall(function()
			return DataStoreService:GetOrderedDataStore("MegaObby_Board_" .. boardId)
		end)
		if not ok then
			warn("[MegaObby] OrderedDataStore niedostępny dla " .. boardId .. " (Studio offline?)")
			return
		end
		boards[boardId] = { store = store, rows = buildSurface(stand) }
	end
	for _, stand in ipairs(CollectionService:GetTagged("Leaderboard")) do
		arm(stand)
	end
	CollectionService:GetInstanceAddedSignal("Leaderboard"):Connect(arm)
end

local function pushLoop()
	task.spawn(function()
		while true do
			task.wait(PUSH_SECONDS)
			if next(boards) then
				for _, player in ipairs(Players:GetPlayers()) do
					local data = deps.PlayerDataService.Get(player)
					if data then
						-- wartość lidera zależy od gatunku (obby: etapy, symulator: siła, …)
						local leaderValue = data.Stage
						if deps.LeaderValueProvider then
							local okValue, value = pcall(deps.LeaderValueProvider, player)
							if okValue and type(value) == "number" then
								leaderValue = value
							end
						end
						pcall(function()
							boards.stages.store:SetAsync(tostring(player.UserId), leaderValue)
						end)
						pcall(function()
							boards.coins.store:SetAsync(tostring(player.UserId), data.Coins)
						end)
					end
				end
			end
		end
	end)
end

local function readLoop()
	task.spawn(function()
		while true do
			task.wait(READ_SECONDS)
			for boardId, board in pairs(boards) do
				local ok, pages = pcall(function()
					return board.store:GetSortedAsync(false, 10):GetCurrentPage()
				end)
				if ok and pages then
					local lines = {}
					for rank, entry in ipairs(pages) do
						local label
						if boardId == "stages" then
							label = entry.value .. " et."
						else
							label = deps.Util.Format.Short(entry.value)
						end
						table.insert(lines, rank .. ". " .. nameFor(tonumber(entry.key) or 0) .. " — " .. label)
					end
					board.rows.Text = #lines > 0 and table.concat(lines, "\n") or "Jeszcze nikt — bądź pierwszy!"
				end
			end
		end
	end)
end

function LeaderboardService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	watchStands()
	pushLoop()
	readLoop()

	print("[MegaObby] LeaderboardService gotowy — tablic: " .. #CollectionService:GetTagged("Leaderboard") .. ".")
end

return LeaderboardService
