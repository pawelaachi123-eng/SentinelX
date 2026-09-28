-- ============================================================================
-- MEGA OBBY — HudController (Client/Controllers/HudController)
-- HUD zbudowany na UiKit (szklane panele + sprężyny): chip etapu z akcentem
-- wyspy, licznik monet z animacją „gonienia” i punch przy zysku, chip boosta,
-- panel statystyk gatunku, lista zadań z PASKAMI postępu, popup nagrody
-- dziennej z paskiem 7 dni i konfetti, powiadomienia jako toast w UiKit.
-- Cały interfejs powiadamiania zostaje ten sam: Init/OnSnapshot/Notify/Stat.
-- ============================================================================

local Players = game:GetService("Players")
local RunService = game:GetService("RunService")
local TweenService = game:GetService("TweenService")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")
local UiKit = require(script.Parent.Parent:WaitForChild("UiKit"))

local Hud = {}

local player = Players.LocalPlayer

local state, config, net, formatNumber
local refs = {}
local toastContainer = nil
local dailyPulse = true -- animowany halo przycisku nagrody

-- ————————————————————————————————————————————————
-- Powiadomienia: toast z UiKit (wjazd sprężyną, auto-fade, stos max 4)
-- ————————————————————————————————————————————————
function Hud.Notify(kind, message)
	if not toastContainer then
		return
	end
	UiKit.Toast(toastContainer, kind, message)
end

-- ————————————————————————————————————————————————
-- Monety: licznik UiKit (goni cel) + punch przy zysku
-- ————————————————————————————————————————————————
local coinsCounter
local lastCoins = 0

-- ————————————————————————————————————————————————
-- Snapshot z serwera → odświeżenie chipów, paneli i zadań
-- ————————————————————————————————————————————————
function Hud.OnSnapshot(snapshot)
	if not snapshot or not refs.StageLabel then
		return
	end
	refs.StageLabel.Text = "ETAP " .. snapshot.Stage .. " / " .. snapshot.TotalStages
	refs.IslandLabel.Text = snapshot.Island .. " · " .. (snapshot.GameName or config.GameName)
	refs.IslandUnderline.BackgroundColor3 = UiKit.Rarity[snapshot.Island] or UiKit.Tokens.Accent
	if snapshot.Coins and snapshot.Coins ~= lastCoins then
		if snapshot.Coins > lastCoins then
			coinsCounter.Punch(refs.CoinsLabel)
		end
		lastCoins = snapshot.Coins
	end
	coinsCounter.SetTarget(snapshot.Coins or 0)

	-- chip boosta: x2 osobisty / globalny event (szklany chip z poświatą)
	if refs.BoostChip then
		local boosts = snapshot.Boosts
		if boosts and (boosts.Personal or 1) > 1 then
			refs.BoostChip.Visible = true
			refs.BoostChip.Text = "⚡ BOOST x" .. boosts.Personal .. " (" .. math.ceil((boosts.PersonalEndsIn or 0) / 60) .. " min)"
			refs.BoostStroke.Color = UiKit.Tokens.Accent
		elseif boosts and (boosts.Global or 1) > 1 then
			refs.BoostChip.Visible = true
			refs.BoostChip.Text = "🔥 EVENT x" .. boosts.Global .. " (" .. math.ceil((boosts.GlobalEndsIn or 0) / 60) .. " min)"
			refs.BoostStroke.Color = UiKit.Tokens.AccentPink
		else
			refs.BoostChip.Visible = false
		end
	end

	-- panel statystyk GATUNKU (Forge): wiersze etykieta→wartość z kropką akcentu
	if snapshot.Hud then
		refs.PackPanel.Visible = true
		refs.PackTitle.Text = snapshot.Hud.Title or ""
		refs.PackRows:ClearAllChildren()
		for index, row in ipairs(snapshot.Hud.Rows or {}) do
			local rowFrame = Instance.new("Frame")
			rowFrame.Size = UDim2.new(1, -12, 0, 22)
			rowFrame.BackgroundTransparency = 1
			rowFrame.LayoutOrder = index
			rowFrame.Parent = refs.PackRows

			local dot = Instance.new("Frame")
			dot.Size = UDim2.fromOffset(5, 5)
			dot.Position = UDim2.new(0, 2, 0.5, -2)
			dot.BackgroundColor3 = UiKit.Tokens.Accent
			dot.BackgroundTransparency = 0.3
			dot.BorderSizePixel = 0
			dot.Parent = rowFrame
			UiKit.Corner(3).Parent = dot

			local label = Instance.new("TextLabel")
			label.Size = UDim2.new(0.62, -14, 1, 0)
			label.Position = UDim2.new(0, 14, 0, 0)
			label.BackgroundTransparency = 1
			label.Font = UiKit.Tokens.Font
			label.TextSize = 13
			label.TextXAlignment = Enum.TextXAlignment.Left
			label.TextTruncate = Enum.TextTruncate.AtEnd
			label.TextColor3 = UiKit.Tokens.TextSoft
			label.Text = row.Label or "?"
			label.Parent = rowFrame

			local value = Instance.new("TextLabel")
			value.Size = UDim2.new(0.38, -10, 1, 0)
			value.Position = UDim2.new(0.62, 0, 0, 0)
			value.BackgroundTransparency = 1
			value.Font = UiKit.Tokens.FontBold
			value.TextSize = 13
			value.TextXAlignment = Enum.TextXAlignment.Right
			value.TextTruncate = Enum.TextTruncate.AtEnd
			value.TextColor3 = UiKit.Tokens.Text
			value.Text = tostring(row.Value or "?")
			value.Parent = rowFrame
		end
		local rows = #((snapshot.Hud and snapshot.Hud.Rows) or {})
		refs.PackPanel.Size = UDim2.fromOffset(258, 40 + rows * 24)
	else
		refs.PackPanel.Visible = false
	end

	-- zadania dnia: szkło + pasek postępu + przycisk odbioru
	refs.QuestList:ClearAllChildren()
	for index, quest in ipairs(snapshot.Quests or {}) do
		local row = Instance.new("Frame")
		row.Size = UDim2.new(1, 0, 0, 52)
		row.BackgroundColor3 = UiKit.Tokens.Glass
		row.BackgroundTransparency = 0.18
		row.BorderSizePixel = 0
		row.LayoutOrder = index
		row.Parent = refs.QuestList
		UiKit.Corner(11).Parent = row
		UiKit.Stroke(quest.Done and not quest.Claimed and UiKit.Tokens.Success or UiKit.Tokens.GlassBorder, 1,
			quest.Done and not quest.Claimed and 0.35 or 0.8).Parent = row

		local desc = Instance.new("TextLabel")
		desc.Size = UDim2.new(1, -16, 0, 18)
		desc.Position = UDim2.new(0, 10, 0, 4)
		desc.BackgroundTransparency = 1
		desc.Font = UiKit.Tokens.FontBold
		desc.TextSize = 12
		desc.TextXAlignment = Enum.TextXAlignment.Left
		desc.TextTruncate = Enum.TextTruncate.AtEnd
		desc.TextColor3 = UiKit.Tokens.Text
		desc.Text = quest.Desc
		desc.Parent = row

		local fraction = math.clamp((quest.Progress or 0) / math.max(1, quest.Target or 1), 0, 1)
		local _, fill = UiKit.Bar(row, UDim2.new(1, -120, 0, 6), UDim2.new(0, 10, 0, 28),
			quest.Claimed and UiKit.Tokens.TextMuted or UiKit.Tokens.Accent)
		UiKit.Tween(fill, UiKit.Ease(0.5), { Size = UDim2.new(fraction, 0, 1, 0) })

		local progress = Instance.new("TextLabel")
		progress.Size = UDim2.new(1, -16, 0, 14)
		progress.Position = UDim2.new(0, 10, 0, 36)
		progress.BackgroundTransparency = 1
		progress.Font = UiKit.Tokens.Font
		progress.TextSize = 11
		progress.TextXAlignment = Enum.TextXAlignment.Left
		progress.TextColor3 = UiKit.Tokens.TextMuted
		progress.Text = math.floor(quest.Progress or 0) .. " / " .. quest.Target .. "  ·  +" .. quest.Reward .. " 🪙"
		progress.Parent = row

		if quest.Done and not quest.Claimed then
			local claim = UiKit.Button(row, "ODBIERZ", UiKit.Tokens.Success, UDim2.new(0, 86, 0, 30),
				UDim2.new(1, -94, 0, 11), function()
					task.spawn(function()
						local ok, message = net:Invoke("ClaimQuest", quest.Id)
						if message ~= "" then
							Hud.Notify(ok and "success" or "error", message)
						end
					end)
				end, { TextSize = 12 })
			claim.ZIndex = row.ZIndex + 1
		elseif quest.Claimed then
			local done = Instance.new("TextLabel")
			done.Size = UDim2.new(0, 86, 0, 30)
			done.Position = UDim2.new(1, -94, 0, 11)
			done.BackgroundTransparency = 1
			done.Font = UiKit.Tokens.FontBold
			done.TextSize = 12
			done.TextColor3 = UiKit.Tokens.Success
			done.Text = "✔ ODEBRANE"
			done.Parent = row
		end
	end

	-- nagroda dzienna: przycisk świeci (pulsująca obwódka), gdy można odebrać
	local daily = snapshot.Daily or {}
	refs.DailyButton.Text = daily.CanClaim and ("🎁 NAGRODA (+" .. (daily.NextReward or 0) .. ")") or "🎁 NAGRODA"
	refs.DailyButton.BackgroundColor3 = daily.CanClaim and UiKit.Tokens.Success or UiKit.Tokens.Glass
	refs.DailyButton.TextColor3 = daily.CanClaim and UiKit.Tokens.OnAccent or UiKit.Tokens.Text
	refs.DailyStroke.Color = daily.CanClaim and UiKit.Tokens.Success or UiKit.Tokens.Accent
	refs.DailyStroke.Transparency = daily.CanClaim and 0.1 or 0.5
	dailyPulse = daily.CanClaim == true
end

function Hud.OnStatChanged(key, value)
	if key == "Coins" then
		local newValue = tonumber(value) or 0
		local delta = newValue - lastCoins
		lastCoins = newValue
		coinsCounter.SetTarget(newValue)
		if delta > 0 and refs.Screen then
			-- floatLabel: pływający +X nad chipem monet (natychmiastowy feedback)
			local floatLabel = Instance.new("TextLabel")
			floatLabel.Size = UDim2.fromOffset(150, 24)
			floatLabel.Position = UDim2.new(1, -210, 0, 66)
			floatLabel.BackgroundTransparency = 1
			floatLabel.Font = UiKit.Tokens.FontBlack
			floatLabel.TextSize = 17
			floatLabel.TextColor3 = UiKit.Tokens.Gold
			floatLabel.Text = "+" .. formatNumber(delta) .. " 🪙"
			floatLabel.Parent = refs.Screen
			TweenService:Create(floatLabel, TweenInfo.new(1.05, Enum.EasingStyle.Quad, Enum.EasingDirection.Out), {
				Position = UDim2.new(1, -210, 0, 28),
				TextTransparency = 1,
			}):Play()
			task.delay(1.15, function()
				floatLabel:Destroy()
			end)
		end
	end
end

-- ————————————————————————————————————————————————
-- Popup nagrody dziennej: pasek 7 dni + konfetti przy odbiorze
-- ————————————————————————————————————————————————
local function showDailyPopup()
	local snapshot = state.Snapshot
	local daily = snapshot and snapshot.Daily or { Streak = 0, CanClaim = false, NextReward = 0, Day = 1 }
	local overlay = Instance.new("Frame")
	overlay.Size = UDim2.fromScale(1, 1)
	overlay.BackgroundColor3 = Color3.new(0, 0, 0)
	overlay.BackgroundTransparency = 1
	overlay.Parent = refs.Screen
	TweenService:Create(overlay, TweenInfo.new(0.25), { BackgroundTransparency = 0.45 }):Play()

	local panel = UiKit.Glass(overlay, UDim2.fromOffset(400, 318), UDim2.new(0.5, 0, 0.5, 0), 18, { Shadow = true })
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	local scale = Instance.new("UIScale")
	scale.Parent = panel

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -60, 0, 34)
	title.Position = UDim2.new(0, 20, 0, 16)
	title.BackgroundTransparency = 1
	title.Font = UiKit.Tokens.FontBlack
	title.TextSize = 21
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextColor3 = UiKit.Tokens.Text
	title.Text = "🎁 NAGRODA DNIA"
	title.Parent = panel

	local close = Instance.new("TextButton")
	close.Size = UDim2.fromOffset(30, 30)
	close.Position = UDim2.new(1, -40, 0, 14)
	close.BackgroundTransparency = 1
	close.Font = UiKit.Tokens.FontBold
	close.TextSize = 18
	close.TextColor3 = UiKit.Tokens.TextMuted
	close.Text = "✕"
	close.Parent = panel
	close.Activated:Connect(function()
		UiKit.CloseModal(overlay, function()
			overlay:Destroy()
		end)
	end)

	-- pasek 7 dni: minion dni = wypełnione, dziś = podświetlone
	for day = 1, 7 do
		local claimed = day < (daily.Day or 1)
		local today = day == (daily.Day or 1)
		local cell = Instance.new("Frame")
		cell.Size = UDim2.new(0, 44, 0, 54)
		cell.Position = UDim2.new(0, 20 + (day - 1) * 52, 0, 62)
		cell.BackgroundColor3 = claimed and UiKit.Tokens.Success or (today and UiKit.Tokens.AccentViolet or UiKit.Tokens.GlassLight)
		cell.BackgroundTransparency = claimed and 0.25 or (today and 0.05 or 0.35)
		cell.BorderSizePixel = 0
		cell.Parent = panel
		UiKit.Corner(10).Parent = cell
		UiKit.Stroke(today and UiKit.Tokens.Accent or UiKit.Tokens.GlassBorder, today and 1.6 or 1, today and 0.1 or 0.6).Parent = cell

		local dayLabel = Instance.new("TextLabel")
		dayLabel.Size = UDim2.new(1, 0, 0, 18)
		dayLabel.Position = UDim2.new(0, 0, 0, 6)
		dayLabel.BackgroundTransparency = 1
		dayLabel.Font = UiKit.Tokens.FontBold
		dayLabel.TextSize = 11
		dayLabel.TextColor3 = UiKit.Tokens.Text
		dayLabel.Text = "D" .. day
		dayLabel.Parent = cell

		local icon = Instance.new("TextLabel")
		icon.Size = UDim2.new(1, 0, 0, 22)
		icon.Position = UDim2.new(0, 0, 0, 26)
		icon.BackgroundTransparency = 1
		icon.Font = UiKit.Tokens.FontBold
		icon.TextSize = 16
		icon.Text = day == 7 and "🎉" or (claimed and "✔" or "🎁")
		icon.TextColor3 = UiKit.Tokens.Text
		icon.Parent = cell
	end

	local info = Instance.new("TextLabel")
	info.Size = UDim2.new(1, -40, 0, 64)
	info.Position = UDim2.new(0, 20, 0, 132)
	info.BackgroundTransparency = 1
	info.Font = UiKit.Tokens.Font
	info.TextSize = 15
	info.TextWrapped = true
	info.TextXAlignment = Enum.TextXAlignment.Left
	info.TextColor3 = UiKit.Tokens.TextSoft
	info.Text = "Seria: " .. (daily.Streak or 0) .. " dni · dzień " .. (daily.Day or 1) .. "/7\nNagroda: "
		.. (daily.NextReward or 0) .. " monet"
		.. (((daily.NextSpins or 0) > 0) and (" + " .. daily.NextSpins .. " spinów 🎟️") or "")
		.. (daily.NextPet == true and " + zwierzak! 🐾" or "")
	info.Parent = panel

	local claim = UiKit.Button(panel, daily.CanClaim and ("ODBIERZ +" .. (daily.NextReward or 0)) or "JUŻ DZIŚ ODEBRANE",
		daily.CanClaim and UiKit.Tokens.Success or UiKit.Tokens.GlassLight,
		UDim2.new(1, -40, 0, 46), UDim2.new(0, 20, 0, 210), function()
			task.spawn(function()
				local ok, message = net:Invoke("ClaimDaily")
				Hud.Notify(ok and "success" or "error", message)
				if ok then
					UiKit.Confetti(panel, UDim2.new(0.5, 0, 0.4, 0))
				end
				if ok then
					UiKit.CloseModal(overlay, function()
						overlay:Destroy()
					end)
				end
			end)
		end, { TextColor = daily.CanClaim and UiKit.Tokens.OnAccent or UiKit.Tokens.Text, TextSize = 15 })

	local hint = Instance.new("TextLabel")
	hint.Size = UDim2.new(1, -40, 0, 30)
	hint.Position = UDim2.new(0, 20, 0, 268)
	hint.BackgroundTransparency = 1
	hint.Font = UiKit.Tokens.Font
	hint.TextSize = 11
	hint.TextColor3 = UiKit.Tokens.TextMuted
	hint.Text = "Kolejne dni dają więcej — dzień 7: +2 spiny i zwierzak!"
	hint.Parent = panel

	UiKit.OpenModal(panel)
	return claim
end

-- ————————————————————————————————————————————————
-- INIT: budowa całego HUD z UiKit
-- ————————————————————————————————————————————————
function Hud.Init(stateIn, configIn, netIn)
	state, config, net = stateIn, configIn, netIn
	formatNumber = require(Shared:WaitForChild("Util")).Format.Number
	coinsCounter = UiKit.CountUp(function(value)
		if refs.CoinsLabel then
			refs.CoinsLabel.Text = "🪙 " .. formatNumber(value)
		end
	end)

	local screen = Instance.new("ScreenGui")
	screen.Name = "MegaObbyHud"
	screen.ResetOnSpawn = false
	screen.IgnoreGuiInset = true
	screen.Parent = player:WaitForChild("PlayerGui")
	refs.Screen = screen

	-- chip etapu (lewy górny): szklana karta z akcentowym podkreśleniem wyspy
	local stageChip = UiKit.Glass(screen, UDim2.fromOffset(238, 58), UDim2.fromOffset(12, 12), 14)
	stageChip.BackgroundTransparency = 0.12

	refs.StageLabel = Instance.new("TextLabel")
	refs.StageLabel.Size = UDim2.new(1, -18, 0.5, 0)
	refs.StageLabel.Position = UDim2.new(0, 10, 0, 4)
	refs.StageLabel.BackgroundTransparency = 1
	refs.StageLabel.Font = UiKit.Tokens.FontBlack
	refs.StageLabel.TextSize = 20
	refs.StageLabel.TextXAlignment = Enum.TextXAlignment.Left
	refs.StageLabel.TextColor3 = UiKit.Tokens.Text
	refs.StageLabel.Text = "ETAP …"
	refs.StageLabel.Parent = stageChip

	refs.IslandLabel = Instance.new("TextLabel")
	refs.IslandLabel.Size = UDim2.new(1, -18, 0.32, 0)
	refs.IslandLabel.Position = UDim2.new(0, 10, 0.55, 0)
	refs.IslandLabel.BackgroundTransparency = 1
	refs.IslandLabel.Font = UiKit.Tokens.Font
	refs.IslandLabel.TextSize = 12
	refs.IslandLabel.TextXAlignment = Enum.TextXAlignment.Left
	refs.IslandLabel.TextColor3 = UiKit.Tokens.TextSoft
	refs.IslandLabel.Text = "wczytywanie…"
	refs.IslandLabel.Parent = stageChip

	refs.IslandUnderline = Instance.new("Frame")
	refs.IslandUnderline.Size = UDim2.new(1, -20, 0, 3)
	refs.IslandUnderline.Position = UDim2.new(0, 10, 1, -6)
	refs.IslandUnderline.BorderSizePixel = 0
	refs.IslandUnderline.BackgroundColor3 = UiKit.Tokens.Accent
	refs.IslandUnderline.Parent = stageChip
	UiKit.Corner(2).Parent = refs.IslandUnderline

	-- panel statystyk gatunku (Forge): szklana karta z listą etykieta→wartość
	refs.PackPanel = Instance.new("Frame")
	refs.PackPanel.Size = UDim2.fromOffset(258, 120)
	refs.PackPanel.Position = UDim2.new(0, 12, 0, 78)
	refs.PackPanel.BackgroundColor3 = UiKit.Tokens.Glass
	refs.PackPanel.BackgroundTransparency = 0.1
	refs.PackPanel.BorderSizePixel = 0
	refs.PackPanel.Visible = false
	refs.PackPanel.Parent = screen
	UiKit.Corner(14).Parent = refs.PackPanel
	refs.PackStroke = UiKit.Stroke(UiKit.Tokens.Accent, 1, 0.4)
	refs.PackStroke.Parent = refs.PackPanel

	refs.PackTitle = Instance.new("TextLabel")
	refs.PackTitle.Size = UDim2.new(1, -20, 0, 26)
	refs.PackTitle.Position = UDim2.new(0, 12, 0, 6)
	refs.PackTitle.BackgroundTransparency = 1
	refs.PackTitle.Font = UiKit.Tokens.FontBlack
	refs.PackTitle.TextSize = 14
	refs.PackTitle.TextXAlignment = Enum.TextXAlignment.Left
	refs.PackTitle.TextColor3 = UiKit.Tokens.Accent
	refs.PackTitle.Text = ""
	refs.PackTitle.Parent = refs.PackPanel

	refs.PackRows = Instance.new("Frame")
	refs.PackRows.Size = UDim2.new(1, 0, 1, -34)
	refs.PackRows.Position = UDim2.new(0, 0, 0, 32)
	refs.PackRows.BackgroundTransparency = 1
	refs.PackRows.Parent = refs.PackPanel
	local packLayout = Instance.new("UIListLayout")
	packLayout.Padding = UDim.new(0, 2)
	packLayout.SortOrder = Enum.SortOrder.LayoutOrder
	packLayout.Parent = refs.PackRows

	-- chip monet (prawy górny): licznik „goni” + punch przy zysku
	local coinsChip = UiKit.Glass(screen, UDim2.fromOffset(178, 46), UDim2.new(1, -190, 0, 12), 14)
	coinsChip.BackgroundTransparency = 0.12
	UiKit.Stroke(UiKit.Tokens.Gold, 1.2, 0.3).Parent = coinsChip

	refs.CoinsLabel = Instance.new("TextLabel")
	refs.CoinsLabel.Size = UDim2.fromScale(1, 1)
	refs.CoinsLabel.BackgroundTransparency = 1
	refs.CoinsLabel.Font = UiKit.Tokens.FontBold
	refs.CoinsLabel.TextSize = 18
	refs.CoinsLabel.TextColor3 = UiKit.Tokens.Gold
	refs.CoinsLabel.Text = "🪙 0"
	refs.CoinsLabel.Parent = coinsChip

	-- chip boosta (pod monetami): pokazuje aktywny mnożnik z BoostService
	refs.BoostChip = Instance.new("TextLabel")
	refs.BoostChip.Size = UDim2.fromOffset(178, 30)
	refs.BoostChip.Position = UDim2.new(1, -190, 0, 64)
	refs.BoostChip.BackgroundColor3 = UiKit.Tokens.Glass
	refs.BoostChip.BackgroundTransparency = 0.1
	refs.BoostChip.BorderSizePixel = 0
	refs.BoostChip.Visible = false
	refs.BoostChip.Font = UiKit.Tokens.FontBold
	refs.BoostChip.TextSize = 13
	refs.BoostChip.TextColor3 = UiKit.Tokens.Text
	refs.BoostChip.Text = ""
	refs.BoostChip.Parent = screen
	UiKit.Corner(10).Parent = refs.BoostChip
	refs.BoostStroke = UiKit.Stroke(UiKit.Tokens.Accent, 1.2, 0.25)
	refs.BoostStroke.Parent = refs.BoostChip

	-- zadania dnia (lewy środek): szkło + paski postępu
	local questPanel = Instance.new("Frame")
	questPanel.Size = UDim2.fromOffset(262, 214)
	questPanel.Position = UDim2.new(0, 12, 0.5, -80)
	questPanel.BackgroundTransparency = 1
	questPanel.Parent = screen
	local questTitle = Instance.new("TextLabel")
	questTitle.Size = UDim2.new(1, 0, 0, 22)
	questTitle.BackgroundTransparency = 1
	questTitle.Font = UiKit.Tokens.FontBold
	questTitle.TextSize = 12
	questTitle.TextXAlignment = Enum.TextXAlignment.Left
	questTitle.TextColor3 = UiKit.Tokens.TextMuted
	questTitle.Text = "ZADANIA DNIA"
	questTitle.Parent = questPanel
	refs.QuestList = Instance.new("Frame")
	refs.QuestList.Size = UDim2.new(1, 0, 1, -26)
	refs.QuestList.Position = UDim2.new(0, 0, 0, 26)
	refs.QuestList.BackgroundTransparency = 1
	refs.QuestList.Parent = questPanel
	local questLayout = Instance.new("UIListLayout")
	questLayout.Padding = UDim.new(0, 6)
	questLayout.SortOrder = Enum.SortOrder.LayoutOrder
	questLayout.Parent = refs.QuestList

	-- powiadomienia (góra-środek): stos toastów UiKit
	toastContainer = Instance.new("Frame")
	toastContainer.Size = UDim2.new(0, 430, 0, 210)
	toastContainer.Position = UDim2.new(0.5, -215, 0, 12)
	toastContainer.BackgroundTransparency = 1
	toastContainer.Parent = screen
	local toastLayout = Instance.new("UIListLayout")
	toastLayout.Padding = UDim.new(0, 6)
	toastLayout.HorizontalAlignment = Enum.HorizontalAlignment.Center
	toastLayout.SortOrder = Enum.SortOrder.LayoutOrder
	toastLayout.Parent = toastContainer

	-- dolny pasek: NAGRODA (sklep i ustawienia dokleją własne przyciski)
	refs.DailyButton = Instance.new("TextButton")
	refs.DailyButton.Size = UDim2.fromOffset(158, 42)
	refs.DailyButton.Position = UDim2.new(0.5, -245, 1, -56)
	refs.DailyButton.BackgroundColor3 = UiKit.Tokens.Glass
	refs.DailyButton.BackgroundTransparency = 0.12
	refs.DailyButton.Font = UiKit.Tokens.FontBold
	refs.DailyButton.TextSize = 14
	refs.DailyButton.AutoButtonColor = false
	refs.DailyButton.TextColor3 = UiKit.Tokens.Text
	refs.DailyButton.Text = "🎁 NAGRODA"
	refs.DailyButton.Parent = screen
	UiKit.Corner(12).Parent = refs.DailyButton
	refs.DailyStroke = UiKit.Stroke(UiKit.Tokens.Accent, 1.3, 0.4)
	refs.DailyStroke.Parent = refs.DailyButton
	refs.DailyButton.Activated:Connect(showDailyPopup)

	-- pulsująca obwódka przycisku nagrody (tylko gdy można odebrać)
	task.spawn(function()
		while true do
			task.wait(0.9)
			if dailyPulse and refs.DailyStroke then
				UiKit.Tween(refs.DailyStroke, TweenInfo.new(0.8, Enum.EasingStyle.Sine, Enum.EasingDirection.InOut), {
					Transparency = 0.65,
				})
				task.wait(0.8)
				if refs.DailyStroke then
					UiKit.Tween(refs.DailyStroke, TweenInfo.new(0.8, Enum.EasingStyle.Sine, Enum.EasingDirection.InOut), {
						Transparency = 0.05,
					})
				end
			end
		end
	end)

	print("[MegaObby] HUD zbudowany (UiKit: szkło + sprężyny + toasty).")
end

return Hud
