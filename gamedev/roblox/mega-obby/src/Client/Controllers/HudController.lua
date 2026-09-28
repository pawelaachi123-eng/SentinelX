--!strict
-- ============================================================================
-- MEGA OBBY — HudController (Client/Controllers/HudController)
-- Cały HUD budowany z KODU (zero klikania w Studio): chip etapu z nazwą wyspy,
-- licznik monet z animacją naliczania, tracker 3 dziennych zadań z przyciskiem
-- odbioru, popup nagrody dziennej ze streakiem, stos powiadomień z tweenami.
-- ============================================================================

local Players = game:GetService("Players")
local RunService = game:GetService("RunService")
local TweenService = game:GetService("TweenService")

local PlayersService = Players

local Hud = {}

local player = PlayersService.LocalPlayer

local state, config, net, formatNumber
local refs = {} -- instancje UI
local notificationContainer = nil

-- ————————————————————————————————————————————————
-- Pomocnicze bloki UI
-- ————————————————————————————————————————————————
local function color3From(parts)
	return Color3.fromRGB(parts[1], parts[2], parts[3])
end

local function corner(radius: number)
	local c = Instance.new("UICorner")
	c.CornerRadius = UDim.new(0, radius)
	return c
end

local function stroke(color: Color3, thickness: number?)
	local s = Instance.new("UIStroke")
	s.Color = color
	s.Thickness = thickness or 1.5
	s.Transparency = 0.35
	return s
end

-- ————————————————————————————————————————————————
-- Powiadomienia (stos u góry, max 3, tween wjazd + znikanie)
-- ————————————————————————————————————————————————
function Hud.Notify(kind: string, message: string)
	if not notificationContainer then
		return
	end
	local colors = {
		info = Color3.fromRGB(120, 170, 255),
		success = color3From(config.Ui.SuccessColor),
		error = color3From(config.Ui.ErrorColor),
	}
	local icon = kind == "success" and "✅" or kind == "error" and "⚠️" or "ℹ️"
	local entry = Instance.new("TextLabel")
	entry.Size = UDim2.new(1, 0, 0, 34)
	entry.BackgroundColor3 = color3From(config.Ui.PanelColor)
	entry.BackgroundTransparency = 0.15
	entry.BorderSizePixel = 0
	entry.Font = Enum.Font.Gotham
	entry.Text = "  " .. icon .. "  " .. message
	entry.TextColor3 = colors[kind] or colors.info
	entry.TextSize = 16
	entry.TextXAlignment = Enum.TextXAlignment.Left
	entry.Parent = notificationContainer
	corner(8).Parent = entry
	local entryStroke = Instance.new("UIStroke")
	entryStroke.Color = colors[kind] or colors.info
	entryStroke.Thickness = 1.5
	entryStroke.Transparency = 0.35
	entryStroke.Parent = entry

	entry.Position = UDim2.new(0, 0, 0, -40)
	TweenService:Create(entry, TweenInfo.new(0.25, Enum.EasingStyle.Quart, Enum.EasingDirection.Out), {
		Position = UDim2.new(0, 0, 0, 0),
	}):Play()

	task.delay(4.5, function()
		local fade = TweenService:Create(entry, TweenInfo.new(0.4), {
			BackgroundTransparency = 1,
			TextTransparency = 1,
		})
		fade.Completed:Connect(function()
			entry:Destroy()
		end)
		fade:Play()
	end)

	-- max 3: starsze usuwamy
	local count = 0
	for _, child in ipairs(notificationContainer:GetChildren()) do
		if child:IsA("TextLabel") then
			count += 1
			if count > 3 then
				child:Destroy()
			end
		end
	end
end

-- ————————————————————————————————————————————————
-- Monety: animacja naliczania (licznik „goni” wartość docelową)
-- ————————————————————————————————————————————————
local coinsShown = 0
local coinsTarget = 0

local function coinsLoop()
	RunService.RenderStepped:Connect(function(dt)
		if coinsShown ~= coinsTarget then
			local diff = coinsTarget - coinsShown
			local step = math.ceil(math.abs(diff) * math.clamp(dt * 6, 0.08, 1))
			if math.abs(diff) <= step then
				coinsShown = coinsTarget
			elseif diff > 0 then
				coinsShown += step
			else
				coinsShown -= step
			end
			refs.CoinsLabel.Text = "🪙 " .. formatNumber(coinsShown)
		end
	end)
end

-- ————————————————————————————————————————————————
-- Snapshot z serwera → odświeżenie chipów i listy zadań
-- ————————————————————————————————————————————————
function Hud.OnSnapshot(snapshot)
	if not snapshot or not refs.StageLabel then
		return
	end
	refs.StageLabel.Text = "ETAP " .. snapshot.Stage .. " / " .. snapshot.TotalStages
	refs.IslandLabel.Text = snapshot.Island .. " · " .. (snapshot.GameName or config.GameName)
	refs.IslandUnderline.BackgroundColor3 = color3From(snapshot.IslandColor or { 34, 211, 238 })
	coinsTarget = snapshot.Coins or 0

	-- zadania dnia
	refs.QuestList:ClearAllChildren()
	local offsetY = 0
	for _, quest in ipairs(snapshot.Quests or {}) do
		local row = Instance.new("Frame")
		row.Size = UDim2.new(1, 0, 0, 44)
		row.Position = UDim2.new(0, 0, 0, offsetY)
		row.BackgroundColor3 = color3From(config.Ui.PanelColor)
		row.BackgroundTransparency = 0.18
		row.BorderSizePixel = 0
		row.Parent = refs.QuestList
		corner(8).Parent = row
		offsetY += 50

		local desc = Instance.new("TextLabel")
		desc.Size = UDim2.new(1, -16, 0.5, 0)
		desc.Position = UDim2.new(0, 8, 0, 3)
		desc.BackgroundTransparency = 1
		desc.Font = Enum.Font.GothamBold
		desc.TextSize = 13
		desc.TextXAlignment = Enum.TextXAlignment.Left
		desc.TextColor3 = Color3.fromRGB(235, 240, 250)
		desc.TextClipsDescendants = true
		desc.Text = quest.Desc
		desc.Parent = row

		local progress = Instance.new("TextLabel")
		progress.Size = UDim2.new(1, -16, 0.35, 0)
		progress.Position = UDim2.new(0, 8, 0.55, 0)
		progress.BackgroundTransparency = 1
		progress.Font = Enum.Font.Gotham
		progress.TextSize = 12
		progress.TextXAlignment = Enum.TextXAlignment.Left
		progress.TextColor3 = Color3.fromRGB(160, 170, 195)
		progress.Text = math.floor(quest.Progress) .. " / " .. quest.Target .. "  ·  +" .. quest.Reward
		progress.Parent = row

		if quest.Done and not quest.Claimed then
			local claim = Instance.new("TextButton")
			claim.Size = UDim2.new(0.34, 0, 0.62, 0)
			claim.Position = UDim2.new(0.63, 0, 0.19, 0)
			claim.BackgroundColor3 = color3From(config.Ui.SuccessColor)
			claim.Font = Enum.Font.GothamBold
			claim.TextSize = 13
			claim.TextColor3 = Color3.fromRGB(15, 20, 15)
			claim.Text = "ODBIERZ"
			claim.Parent = row
			corner(8).Parent = claim
			claim.Activated:Connect(function()
				task.spawn(function()
					local ok, message = net:Invoke("ClaimQuest", quest.Id)
					if message ~= "" then
						Hud.Notify(ok and "success" or "error", message)
					end
				end)
			end)
		elseif quest.Claimed then
			local done = Instance.new("TextLabel")
			done.Size = UDim2.new(0.34, 0, 0.62, 0)
			done.Position = UDim2.new(0.63, 0, 0.19, 0)
			done.BackgroundTransparency = 1
			done.Font = Enum.Font.GothamBold
			done.TextSize = 13
			done.TextColor3 = color3From(config.Ui.SuccessColor)
			done.Text = "✔ ODEBRANE"
			done.Parent = row
		end
	end

	-- nagroda dzienna: przycisk świeci, gdy można odebrać
	local daily = snapshot.Daily or {}
	refs.DailyButton.Text = daily.CanClaim and ("🎁 NAGRODA (+" .. (daily.NextReward or 0) .. ")") or "🎁 NAGRODA"
	refs.DailyButton.BackgroundColor3 = daily.CanClaim
		and color3From(config.Ui.SuccessColor)
		or color3From(config.Ui.PanelColor)
end

function Hud.OnStatChanged(key, value)
	if key == "Coins" then
		coinsTarget = tonumber(value) or coinsTarget
	end
end

-- ————————————————————————————————————————————————
-- Popup nagrody dziennej
-- ————————————————————————————————————————————————
local function showDailyPopup()
	local snapshot = state.Snapshot
	local daily = snapshot and snapshot.Daily or { Streak = 0, CanClaim = false, NextReward = 0, Day = 1 }
	local overlay = Instance.new("Frame")
	overlay.Size = UDim2.fromScale(1, 1)
	overlay.BackgroundColor3 = Color3.new(0, 0, 0)
	overlay.BackgroundTransparency = 0.45
	overlay.Parent = refs.Screen
	local panel = Instance.new("Frame")
	panel.Size = UDim2.fromOffset(340, 240)
	panel.Position = UDim2.fromScale(0.5, 0.5)
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	panel.BackgroundColor3 = color3From(config.Ui.PanelColor)
	panel.BorderSizePixel = 0
	panel.Parent = overlay
	corner(14).Parent = panel
	stroke(color3From(config.Ui.AccentColor)).Parent = panel

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -20, 0, 44)
	title.Position = UDim2.new(0, 10, 0, 12)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.GothamBold
	title.TextSize = 22
	title.TextColor3 = Color3.fromRGB(240, 244, 255)
	title.Text = "🎁 NAGRODA DNIA"
	title.Parent = panel

	local info = Instance.new("TextLabel")
	info.Size = UDim2.new(1, -40, 0, 70)
	info.Position = UDim2.new(0, 20, 0, 60)
	info.BackgroundTransparency = 1
	info.Font = Enum.Font.Gotham
	info.TextSize = 16
	info.TextWrapped = true
	info.TextColor3 = Color3.fromRGB(200, 210, 230)
	info.Text = "Seria: " .. (daily.Streak or 0) .. " dni · dzień " .. (daily.Day or 1) .. "/7\nNagroda: " .. (daily.NextReward or 0) .. " monet"
	info.Parent = panel

	local claim = Instance.new("TextButton")
	claim.Size = UDim2.new(1, -40, 0, 42)
	claim.Position = UDim2.new(0, 20, 0, 140)
	claim.BackgroundColor3 = daily.CanClaim and color3From(config.Ui.SuccessColor) or Color3.fromRGB(70, 78, 95)
	claim.Font = Enum.Font.GothamBold
	claim.TextSize = 16
	claim.TextColor3 = Color3.fromRGB(255, 255, 255)
	claim.Text = daily.CanClaim and ("ODBIERZ +" .. (daily.NextReward or 0)) or "JUŻ DZIŚ ODEBRANE"
	claim.Parent = panel
	corner(10).Parent = claim

	claim.Activated:Connect(function()
		task.spawn(function()
			local ok, message = net:Invoke("ClaimDaily")
			Hud.Notify(ok and "success" or "error", message)
			if ok then
				overlay:Destroy()
			end
		end)
	end)

	local close = Instance.new("TextButton")
	close.Size = UDim2.new(0, 28, 0, 28)
	close.Position = UDim2.new(1, -36, 0, 8)
	close.BackgroundTransparency = 1
	close.Font = Enum.Font.GothamBold
	close.TextSize = 18
	close.TextColor3 = Color3.fromRGB(160, 170, 195)
	close.Text = "✕"
	close.Parent = panel
	close.Activated:Connect(function()
		overlay:Destroy()
	end)
end

-- ————————————————————————————————————————————————
-- INIT: budowa całego HUD
-- ————————————————————————————————————————————————
function Hud.Init(stateIn, configIn, netIn)
	state, config, net = stateIn, configIn, netIn

	local Shared = game:GetService("ReplicatedStorage"):WaitForChild("Shared")
	formatNumber = require(Shared:WaitForChild("Util")).Format.Number

	local screen = Instance.new("ScreenGui")
	screen.Name = "MegaObbyHud"
	screen.ResetOnSpawn = false
	screen.IgnoreGuiInset = true
	screen.Parent = player:WaitForChild("PlayerGui")
	refs.Screen = screen

	-- chip etapu (lewy górny)
	local stageChip = Instance.new("Frame")
	stageChip.Size = UDim2.fromOffset(230, 58)
	stageChip.Position = UDim2.fromOffset(12, 12)
	stageChip.BackgroundColor3 = color3From(config.Ui.PanelColor)
	stageChip.BackgroundTransparency = 0.12
	stageChip.BorderSizePixel = 0
	stageChip.Parent = screen
	corner(12).Parent = stageChip
	stroke(color3From(config.Ui.AccentColor)).Parent = stageChip

	refs.StageLabel = Instance.new("TextLabel")
	refs.StageLabel.Size = UDim2.new(1, -16, 0.52, 0)
	refs.StageLabel.Position = UDim2.new(0, 8, 0, 4)
	refs.StageLabel.BackgroundTransparency = 1
	refs.StageLabel.Font = Enum.Font.GothamBlack
	refs.StageLabel.TextSize = 20
	refs.StageLabel.TextXAlignment = Enum.TextXAlignment.Left
	refs.StageLabel.TextColor3 = Color3.fromRGB(240, 244, 255)
	refs.StageLabel.Text = "ETAP …"
	refs.StageLabel.Parent = stageChip

	refs.IslandLabel = Instance.new("TextLabel")
	refs.IslandLabel.Size = UDim2.new(1, -16, 0.34, 0)
	refs.IslandLabel.Position = UDim2.new(0, 8, 0.56, 0)
	refs.IslandLabel.BackgroundTransparency = 1
	refs.IslandLabel.Font = Enum.Font.Gotham
	refs.IslandLabel.TextSize = 12
	refs.IslandLabel.TextXAlignment = Enum.TextXAlignment.Left
	refs.IslandLabel.TextColor3 = Color3.fromRGB(170, 180, 200)
	refs.IslandLabel.Text = "wczytywanie…"
	refs.IslandLabel.Parent = stageChip

	refs.IslandUnderline = Instance.new("Frame")
	refs.IslandUnderline.Size = UDim2.new(1, -16, 0, 2)
	refs.IslandUnderline.Position = UDim2.new(0, 8, 1, -4)
	refs.IslandUnderline.BorderSizePixel = 0
	refs.IslandUnderline.BackgroundColor3 = color3From(config.Ui.AccentColor)
	refs.IslandUnderline.Parent = stageChip

	-- panel statystyk gatunku (Forge: symulator/horror/tycoon/shooter/wyścigi)
	refs.PackPanel = Instance.new("Frame")
	refs.PackPanel.Size = UDim2.fromOffset(250, 120)
	refs.PackPanel.Position = UDim2.new(0, 12, 0, 78)
	refs.PackPanel.BackgroundColor3 = color3From(config.Ui.PanelColor)
	refs.PackPanel.BackgroundTransparency = 0.12
	refs.PackPanel.BorderSizePixel = 0
	refs.PackPanel.Visible = false
	refs.PackPanel.Parent = screen
	corner(12).Parent = refs.PackPanel
	local packStroke = Instance.new("UIStroke")
	packStroke.Color = color3From(config.Ui.AccentColor)
	packStroke.Transparency = 0.5
	packStroke.Parent = refs.PackPanel
	refs.PackTitle = Instance.new("TextLabel")
	refs.PackTitle.Size = UDim2.new(1, -16, 0, 26)
	refs.PackTitle.Position = UDim2.new(0, 8, 0, 4)
	refs.PackTitle.BackgroundTransparency = 1
	refs.PackTitle.Font = Enum.Font.GothamBlack
	refs.PackTitle.TextSize = 14
	refs.PackTitle.TextXAlignment = Enum.TextXAlignment.Left
	refs.PackTitle.TextColor3 = color3From(config.Ui.AccentColor)
	refs.PackTitle.Text = ""
	refs.PackTitle.Parent = refs.PackPanel
	refs.PackRows = Instance.new("Frame")
	refs.PackRows.Size = UDim2.new(1, 0, 1, -30)
	refs.PackRows.Position = UDim2.new(0, 0, 0, 30)
	refs.PackRows.BackgroundTransparency = 1
	refs.PackRows.Parent = refs.PackPanel

	-- chip monet (prawy górny)
	local coinsChip = Instance.new("Frame")
	coinsChip.Size = UDim2.fromOffset(170, 46)
	coinsChip.Position = UDim2.new(1, -182, 0, 12)
	coinsChip.BackgroundColor3 = color3From(config.Ui.PanelColor)
	coinsChip.BackgroundTransparency = 0.12
	coinsChip.BorderSizePixel = 0
	coinsChip.Parent = screen
	corner(12).Parent = coinsChip
	stroke(Color3.fromRGB(255, 200, 40)).Parent = coinsChip

	refs.CoinsLabel = Instance.new("TextLabel")
	refs.CoinsLabel.Size = UDim2.fromScale(1, 1)
	refs.CoinsLabel.BackgroundTransparency = 1
	refs.CoinsLabel.Font = Enum.Font.GothamBold
	refs.CoinsLabel.TextSize = 18
	refs.CoinsLabel.TextColor3 = Color3.fromRGB(255, 220, 100)
	refs.CoinsLabel.Text = "🪙 0"
	refs.CoinsLabel.Parent = coinsChip

	-- lista zadań (lewy środek)
	local questPanel = Instance.new("Frame")
	questPanel.Size = UDim2.fromOffset(250, 200)
	questPanel.Position = UDim2.new(0, 12, 0.5, -100)
	questPanel.BackgroundTransparency = 1
	questPanel.Parent = screen
	local questTitle = Instance.new("TextLabel")
	questTitle.Size = UDim2.new(1, 0, 0, 22)
	questTitle.BackgroundTransparency = 1
	questTitle.Font = Enum.Font.GothamBold
	questTitle.TextSize = 13
	questTitle.TextXAlignment = Enum.TextXAlignment.Left
	questTitle.TextColor3 = Color3.fromRGB(160, 170, 195)
	questTitle.Text = "ZADANIA DNIA"
	questTitle.Parent = questPanel
	refs.QuestList = Instance.new("Frame")
	refs.QuestList.Size = UDim2.new(1, 0, 1, -26)
	refs.QuestList.Position = UDim2.new(0, 0, 0, 26)
	refs.QuestList.BackgroundTransparency = 1
	refs.QuestList.Parent = questPanel

	-- powiadomienia (góra-środek)
	notificationContainer = Instance.new("Frame")
	notificationContainer.Size = UDim2.new(0, 420, 0, 140)
	notificationContainer.Position = UDim2.new(0.5, -210, 0, 12)
	notificationContainer.BackgroundTransparency = 1
	notificationContainer.Parent = screen
	local layout = Instance.new("UIListLayout")
	layout.Padding = UDim.new(0, 6)
	layout.HorizontalAlignment = Enum.HorizontalAlignment.Center
	layout.SortOrder = Enum.SortOrder.LayoutOrder
	layout.Parent = notificationContainer

	-- dolny pasek: NAGRODA (sklep i ustawienia dokleją własne przyciski)
	refs.DailyButton = Instance.new("TextButton")
	refs.DailyButton.Size = UDim2.fromOffset(150, 40)
	refs.DailyButton.Position = UDim2.new(0.5, -235, 1, -54)
	refs.DailyButton.BackgroundColor3 = color3From(config.Ui.PanelColor)
	refs.DailyButton.Font = Enum.Font.GothamBold
	refs.DailyButton.TextSize = 14
	refs.DailyButton.TextColor3 = Color3.fromRGB(240, 244, 255)
	refs.DailyButton.Text = "🎁 NAGRODA"
	refs.DailyButton.Parent = screen
	corner(10).Parent = refs.DailyButton
	stroke(color3From(config.Ui.AccentColor)).Parent = refs.DailyButton
	refs.DailyButton.Activated:Connect(showDailyPopup)

	coinsLoop()
	print("[MegaObby] HUD zbudowany.")
end

return Hud
