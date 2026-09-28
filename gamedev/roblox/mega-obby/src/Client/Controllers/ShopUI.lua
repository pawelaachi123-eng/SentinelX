--!strict
-- ============================================================================
-- MEGA OBBY — ShopUI (Client/Controllers/ShopUI)
-- Sklep zbudowany z kodu: 4 zakładki (Ślady · Zwierzaki · Przepustki · Monety).
-- Monety → RemoteFunction (serwer waliduje); Robux → MarketplaceService prompt.
-- ID = 0 (przed publikacją) → uczciwy komunikat, że twórca musi podpiąć ID
-- z kreatora przepustek (README-GRA.md). Żadnych zmyślonych zakupów.
-- ============================================================================

local MarketplaceService = game:GetService("MarketplaceService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local ShopUI = {}

local player = Players.LocalPlayer

local state, config, net, hud
local refs = {}
local activeTab = "trails"

local function color3From(parts)
	return Color3.fromRGB(parts[1], parts[2], parts[3])
end

local function corner(radius)
	local c = Instance.new("UICorner")
	c.CornerRadius = UDim.new(0, radius)
	return c
end

local function fmt(n)
	return (tostring(math.floor(n)):reverse():gsub("(%d%d%d)", "%1 "):reverse():gsub("^%s+", ""))
end

-- ————————————————————————————————————————————————
-- Wiersz oferty: [ikona] nazwa + opis  [cena] [PRZYCISK]
-- ————————————————————————————————————————————————
local function offerRow(parent, offsetY, titleText, subtitleText, buttonText, buttonColor, onActivate)
	local row = Instance.new("Frame")
	row.Size = UDim2.new(1, -12, 0, 54)
	row.Position = UDim2.new(0, 6, 0, offsetY)
	row.BackgroundColor3 = color3From(config.Ui.PanelColor)
	row.BackgroundTransparency = 0.1
	row.BorderSizePixel = 0
	row.Parent = parent
	corner(10).Parent = row

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -190, 0.52, 0)
	title.Position = UDim2.new(0, 12, 0.06, 0)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.GothamBold
	title.TextSize = 15
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextTruncate = Enum.TextTruncate.AtEnd
	title.TextColor3 = Color3.fromRGB(240, 244, 255)
	title.Text = titleText
	title.Parent = row

	local subtitle = Instance.new("TextLabel")
	subtitle.Size = UDim2.new(1, -190, 0.34, 0)
	subtitle.Position = UDim2.new(0, 12, 0.56, 0)
	subtitle.BackgroundTransparency = 1
	subtitle.Font = Enum.Font.Gotham
	subtitle.TextSize = 12
	subtitle.TextXAlignment = Enum.TextXAlignment.Left
	subtitle.TextTruncate = Enum.TextTruncate.AtEnd
	subtitle.TextColor3 = Color3.fromRGB(160, 170, 195)
	subtitle.Text = subtitleText
	subtitle.Parent = row

	local button = Instance.new("TextButton")
	button.Size = UDim2.new(0, 150, 0.66, 0)
	button.Position = UDim2.new(1, -162, 0.17, 0)
	button.BackgroundColor3 = buttonColor
	button.Font = Enum.Font.GothamBold
	button.TextSize = 13
	button.TextColor3 = Color3.fromRGB(15, 18, 25)
	button.Text = buttonText
	button.Parent = row
	corner(8).Parent = button
	button.Activated:Connect(onActivate)

	return row, button
end

-- ————————————————————————————————————————————————
-- Zakładki
-- ————————————————————————————————————————————————
local function clearList()
	refs.List:ClearAllChildren()
end

local function refreshButtons()
	local snapshot = state.Snapshot
	if not snapshot or not refs.TabBar then
		return
	end
	-- przebudowa listy aktywnej zakładki
	clearList()
	local offsetY = 6

	if activeTab == "trails" then
		local owned = {}
		for _, id in ipairs(snapshot.Trails or {}) do
			owned[id] = true
		end
		for _, trail in ipairs(config.Trails) do
			local isOwned = owned[trail.id] or (trail.vipOnly and snapshot.Gamepasses and snapshot.Gamepasses.vip)
			local equipped = snapshot.EquippedTrail == trail.id
			local buttonText = equipped and "ZDJMIJ" or (isOwned and "ZAŁÓŻ" or (fmt(trail.price) .. " 🪙"))
			local buttonColor = equipped and Color3.fromRGB(120, 130, 150) or color3From(config.Ui.AccentColor)
			offerRow(refs.List, offsetY, trail.name .. (trail.vipOnly and "  (VIP)" or ""),
				isOwned and "posiadany" or ("cena: " .. fmt(trail.price) .. " monet"), buttonText, buttonColor, function()
					task.spawn(function()
						local ok, message
						if isOwned then
							ok, message = net:Invoke("EquipTrail", equipped and "" or trail.id)
						else
							ok, message = net:Invoke("BuyTrail", trail.id)
						end
						if hud and message ~= "" then
							hud.Notify(ok and "success" or "error", message)
						end
					end)
				end)
			offsetY += 60
		end
	elseif activeTab == "forge" then
		-- uniwersalny sklep GATUNKU (pozycje definiuje moduł trybu — ForgeBuy)
		local items = snapshot.ShopItems or {}
		for _, item in ipairs(items) do
			local buttonText = item.Owned and "MASZ ✔" or ((item.Price or 0) .. " 🪙 — KUP")
			local buttonColor = item.Owned and Color3.fromRGB(90, 200, 120) or color3From(config.Ui.SuccessColor)
			offerRow(refs.List, offsetY, item.Name or item.Id, item.Description or "", buttonText, buttonColor, function()
				if item.Owned then
					return
				end
				task.spawn(function()
					local ok, message = net:Invoke("ForgeBuy", item.Id)
					if hud and message ~= "" then
						hud.Notify(ok and "success" or "error", message)
					end
				end)
			end)
			offsetY += 60
		end
		if #items == 0 then
			offerRow(refs.List, offsetY, "Ten tryb nie ma własnego sklepu",
				"kupuj ślady i przepustki na pozostałych zakładkach", "—", Color3.fromRGB(70, 78, 95), function() end)
		end
	elseif activeTab == "pets" then
		local equippedSet = {}
		for _, uid in ipairs(snapshot.EquippedPets or {}) do
			equippedSet[uid] = true
		end
		-- jajka
		for _, egg in ipairs(config.Eggs) do
			offerRow(refs.List, offsetY, "🥚 " .. egg.name, "losowanie jednego z " .. #egg.pets .. " zwierzaków", fmt(egg.price) .. " 🪙 — LOSUJ",
				Color3.fromRGB(255, 200, 90), function()
					task.spawn(function()
						local ok, message = net:Invoke("BuyEgg", egg.id)
						if hud and message ~= "" then
							hud.Notify(ok and "success" or "error", message)
						end
					end)
				end)
			offsetY += 60
		end
		-- ekwipunek
		for _, pet in ipairs(snapshot.Pets or {}) do
			local equipped = equippedSet[pet.Uid]
			offerRow(refs.List, offsetY, pet.Name .. "  (" .. pet.Rarity .. ")",
				"bonus monet ×" .. pet.Bonus .. (pet.MagnetRadius > 0 and (" · magnes " .. pet.MagnetRadius .. " studów") or ""),
				equipped and "ZDEJMIJ" or "WYPOSAŻ",
				equipped and Color3.fromRGB(120, 130, 150) or color3From(config.Ui.SuccessColor), function()
					task.spawn(function()
						local ok, message = net:Invoke("EquipPet", equipped and "" or pet.Uid)
						if hud and message ~= "" then
							hud.Notify(ok and "success" or "error", message)
						end
					end)
				end)
			offsetY += 60
		end
		if #snapshot.Pets == 0 then
			offerRow(refs.List, offsetY, "Brak zwierzaków", "wylosuj pierwsze z jajka powyżej", "—", Color3.fromRGB(70, 78, 95), function() end)
		end
	elseif activeTab == "passes" then
		for _, pass in ipairs(config.Gamepasses) do
			local owned = snapshot.Gamepasses and snapshot.Gamepasses[pass.id]
			local buttonText = owned and "MASZ ✔" or "KUP ZA ROBUX"
			offerRow(refs.List, offsetY, pass.name, pass.description
				.. ((not owned and pass.robuxProductId == 0) and "  ·  ⚠️ twórca musi podpiąć ID (README-GRA)" or ""),
				buttonText,
				owned and Color3.fromRGB(90, 200, 120) or Color3.fromRGB(120, 130, 150), function()
					if owned then
						return
					end
					if pass.robuxProductId == 0 then
						if hud then
							hud.Notify("error", "Twórca gry musi jeszcze podpiąć ID tej przepustki (README-GRA.md, sekcja monetyzacja).")
						end
						return
					end
					MarketplaceService:PromptGamePassPurchase(player, pass.robuxProductId)
				end)
			offsetY += 60
		end
	elseif activeTab == "coins" then
		for _, product in ipairs(config.DevProducts) do
			offerRow(refs.List, offsetY, product.name,
				product.robuxProductId == 0 and "⚠️ twórca musi podpiąć ID (README-GRA)" or "zakup za Robux natychmiast na konto",
				"KUP ZA ROBUX", Color3.fromRGB(120, 130, 150), function()
					if product.robuxProductId == 0 then
						if hud then
							hud.Notify("error", "Twórca gry musi jeszcze podpiąć ID tego produktu (README-GRA.md).")
						end
						return
					end
					MarketplaceService:PromptProductPurchase(player, product.robuxProductId)
				end)
			offsetY += 60
		end
	end
end

local function selectTab(tabId)
	activeTab = tabId
	for id, button in pairs(refs.Tabs or {}) do
		button.BackgroundColor3 = id == activeTab and color3From(config.Ui.AccentColor) or color3From(config.Ui.PanelColor)
		button.TextColor3 = id == activeTab and Color3.fromRGB(15, 18, 25) or Color3.fromRGB(200, 210, 230)
	end
	refreshButtons()
end

-- ————————————————————————————————————————————————
-- INIT: okno sklepu + przycisk na dolnym pasku
-- ————————————————————————————————————————————————
function ShopUI.Init(stateIn, configIn, netIn)
	state, config, net = stateIn, configIn, netIn
	local ok, HudModule = pcall(function()
		return require(script.Parent:WaitForChild("HudController"))
	end)
	hud = ok and HudModule or nil

	local screen = Instance.new("ScreenGui")
	screen.Name = "MegaObbyShop"
	screen.ResetOnSpawn = false
	screen.DisplayOrder = 5
	screen.Parent = player:WaitForChild("PlayerGui")
	refs.Screen = screen

	-- okno
	local panel = Instance.new("Frame")
	panel.Size = UDim2.fromOffset(680, 440)
	panel.Position = UDim2.fromScale(0.5, 0.5)
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	panel.BackgroundColor3 = Color3.fromRGB(18, 22, 30)
	panel.BorderSizePixel = 0
	panel.Visible = false
	panel.Parent = screen
	corner(16).Parent = panel
	refs.Panel = panel

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -60, 0, 40)
	title.Position = UDim2.new(0, 16, 0, 8)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.GothamBlack
	title.TextSize = 20
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextColor3 = Color3.fromRGB(240, 244, 255)
	title.Text = "🛒 SKLEP — " .. config.GameName
	title.Parent = panel

	local close = Instance.new("TextButton")
	close.Size = UDim2.new(0, 32, 0, 32)
	close.Position = UDim2.new(1, -42, 0, 10)
	close.BackgroundTransparency = 1
	close.Font = Enum.Font.GothamBold
	close.TextSize = 20
	close.TextColor3 = Color3.fromRGB(160, 170, 195)
	close.Text = "✕"
	close.Parent = panel
	close.Activated:Connect(function()
		panel.Visible = false
	end)

	-- zakładki
	refs.TabBar = Instance.new("Frame")
	refs.TabBar.Size = UDim2.new(1, -24, 0, 34)
	refs.TabBar.Position = UDim2.new(0, 12, 0, 52)
	refs.TabBar.BackgroundTransparency = 1
	refs.TabBar.Parent = panel
	local tabLayout = Instance.new("UIListLayout")
	tabLayout.FillDirection = Enum.FillDirection.Horizontal
	tabLayout.Padding = UDim.new(0, 8)
	tabLayout.Parent = refs.TabBar

	refs.Tabs = {}
	local tabDefs = {
		{ id = "trails", name = "ŚLADY" },
		{ id = "forge", name = "SKLEP TRYBU" },
		{ id = "pets", name = "ZWIERZAKI" },
		{ id = "passes", name = "PRZEPUSTKI" },
		{ id = "coins", name = "MONETY" },
	}
	for _, tabDef in ipairs(tabDefs) do
		local button = Instance.new("TextButton")
		button.Size = UDim2.new(0, 150, 1, 0)
		button.BackgroundColor3 = color3From(config.Ui.PanelColor)
		button.Font = Enum.Font.GothamBold
		button.TextSize = 14
		button.TextColor3 = Color3.fromRGB(200, 210, 230)
		button.Text = tabDef.name
		button.Parent = refs.TabBar
		corner(10).Parent = button
		refs.Tabs[tabDef.id] = button
		button.Activated:Connect(function()
			selectTab(tabDef.id)
		end)
	end

	-- przewijana lista ofert
	local scroll = Instance.new("ScrollingFrame")
	scroll.Size = UDim2.new(1, -24, 1, -108)
	scroll.Position = UDim2.new(0, 12, 0, 96)
	scroll.BackgroundTransparency = 1
	scroll.BorderSizePixel = 0
	scroll.ScrollBarThickness = 6
	scroll.CanvasSize = UDim2.new(0, 0, 0, 0)
	scroll.AutomaticCanvasSize = Enum.AutomaticSize.Y
	scroll.Parent = panel
	refs.List = scroll

	-- przycisk otwierający (dolny pasek)
	local open = Instance.new("TextButton")
	open.Size = UDim2.fromOffset(130, 40)
	open.Position = UDim2.new(0.5, -75, 1, -54)
	open.BackgroundColor3 = color3From(config.Ui.PanelColor)
	open.Font = Enum.Font.GothamBold
	open.TextSize = 14
	open.TextColor3 = Color3.fromRGB(240, 244, 255)
	open.Text = "🛒 SKLEP"
	open.Parent = screen
	corner(10).Parent = open
	local openStroke = Instance.new("UIStroke")
	openStroke.Color = Color3.fromRGB(255, 200, 40)
	openStroke.Transparency = 0.35
	openStroke.Parent = open
	open.Activated:Connect(function()
		panel.Visible = not panel.Visible
		if panel.Visible then
			selectTab(activeTab)
		end
	end)

	selectTab("trails")
	print("[MegaObby] Sklep zbudowany (4 zakładki).")
end

function ShopUI.OnSnapshot(_snapshot)
	if refs.Panel and refs.Panel.Visible then
		refreshButtons()
	end
end

return ShopUI
