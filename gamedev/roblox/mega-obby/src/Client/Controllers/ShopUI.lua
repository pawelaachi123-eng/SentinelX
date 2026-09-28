-- ============================================================================
-- MEGA OBBY — ShopUI (Client/Controllers/ShopUI)
-- Sklep na UiKit (szkło + sprężyny): LEWY RAIL zakładek z przesuwanym
-- zaznaczeniem, karty ofert z ikoną, ceną-chipem i przyciskiem (hover =
-- unoszenie, klik = wciśnięcie), żywy licznik monet w nagłówku, karty
-- wjeżdżają kaskadowo przy zmianie zakładki. Monety → RemoteFunction (serwer
-- waliduje), Robux → MarketplaceService prompt. ID = 0 → uczciwy komunikat,
-- że twórca musi podpiąć ID z kreatora (README-GRA.md). Zakładki te same co
-- wcześniej (OFERTY · SPIN · SEZON · ŚLADY · SKLEP TRYBU · ZWIERZAKI ·
-- PRZEPUSTKI · MONETY) — „SKLEP TRYBU” czyta pozycje z modułu trybu (ForgeBuy).
-- ============================================================================

local MarketplaceService = game:GetService("MarketplaceService")
local Players = game:GetService("Players")
local TweenService = game:GetService("TweenService")

local UiKit = require(script.Parent.Parent:WaitForChild("UiKit"))

local ShopUI = {}

local player = Players.LocalPlayer

local state, config, net, hud
local refs = {}
local activeTab = "trails"

-- ————————————————————————————————————————————————
-- Pomocnicze
-- ————————————————————————————————————————————————
local function fmt(n)
	return (tostring(math.floor(n)):reverse():gsub("(%d%d%d)", "%1 "):reverse():gsub("^%s+", ""))
end

local function notify(ok, message)
	if hud and message ~= "" then
		hud.Notify(ok and "success" or "error", message)
	end
end

-- ————————————————————————————————————————————————
-- KARTA OFERTY: [ikona] tytuł + opis [+ chip] [przycisk]
-- wjeżdża kaskadowo (opóźnienie wg indeksu), hover = uniesienie
-- ————————————————————————————————————————————————
local function itemCard(parent, index, spec)
	local card = Instance.new("Frame")
	card.Size = UDim2.new(1, -10, 0, 66)
	card.BackgroundColor3 = UiKit.Tokens.Glass
	card.BackgroundTransparency = 0.16
	card.BorderSizePixel = 0
	card.LayoutOrder = index
	card.Parent = parent
	UiKit.Corner(13).Parent = card
	local stroke = UiKit.Stroke(spec.Accent or UiKit.Tokens.GlassBorder, 1, 0.65)
	stroke.Parent = card

	local scale = Instance.new("UIScale")
	scale.Parent = card
	card.MouseEnter:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.15), { Scale = 1.015 })
		UiKit.Tween(stroke, UiKit.Ease(0.15), { Transparency = 0.2 })
		UiKit.Tween(card, UiKit.Ease(0.15), { BackgroundTransparency = 0.05 })
	end)
	card.MouseLeave:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.2), { Scale = 1 })
		UiKit.Tween(stroke, UiKit.Ease(0.2), { Transparency = 0.65 })
		UiKit.Tween(card, UiKit.Ease(0.2), { BackgroundTransparency = 0.16 })
	end)

	-- kwadrat ikony z gradientem akcentu
	local iconBox = Instance.new("Frame")
	iconBox.Size = UDim2.fromOffset(42, 42)
	iconBox.Position = UDim2.new(0, 12, 0.5, -21)
	iconBox.BackgroundColor3 = spec.Accent or UiKit.Tokens.Accent
	iconBox.BackgroundTransparency = 0.78
	iconBox.BorderSizePixel = 0
	iconBox.Parent = card
	UiKit.Corner(11).Parent = iconBox
	UiKit.Stroke(spec.Accent or UiKit.Tokens.Accent, 1.2, 0.3).Parent = iconBox
	local icon = Instance.new("TextLabel")
	icon.Size = UDim2.fromScale(1, 1)
	icon.BackgroundTransparency = 1
	icon.Font = UiKit.Tokens.FontBlack
	icon.TextSize = 20
	icon.TextColor3 = spec.Accent or UiKit.Tokens.Accent
	icon.Text = spec.Icon or "•"
	icon.Parent = iconBox

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -86, 0, 18)
	title.Position = UDim2.new(0, 66, 0, 10)
	title.BackgroundTransparency = 1
	title.Font = UiKit.Tokens.FontBold
	title.TextSize = 14
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextTruncate = Enum.TextTruncate.AtEnd
	title.TextColor3 = UiKit.Tokens.Text
	title.Text = spec.Title
	title.Parent = card

	local subtitle = Instance.new("TextLabel")
	subtitle.Size = UDim2.new(1, -86, 0, 26)
	subtitle.Position = UDim2.new(0, 66, 0, 30)
	subtitle.BackgroundTransparency = 1
	subtitle.Font = UiKit.Tokens.Font
	subtitle.TextSize = 12
	subtitle.TextXAlignment = Enum.TextXAlignment.Left
	subtitle.TextTruncate = Enum.TextTruncate.AtEnd
	subtitle.TextWrapped = true
	subtitle.TextColor3 = UiKit.Tokens.TextMuted
	subtitle.Text = spec.Subtitle or ""
	subtitle.Parent = card

	if spec.Chip then
		UiKit.Chip(card, spec.Chip, spec.ChipColor or UiKit.Tokens.Gold, UDim2.new(0, 66, 0, 30),
			{ Size = UDim2.fromOffset(64, 20), TextSize = 11 })
	end

	local button = UiKit.Button(card, spec.ButtonText or "KUP", spec.ButtonColor or UiKit.Tokens.Accent,
		UDim2.fromOffset(120, 36), UDim2.new(1, -130, 0.5, -18), spec.OnActivate, { TextSize = 12 })
	button.ZIndex = card.ZIndex + 1

	-- kaskadowy wjazd karty (tło wjeżdża z opóźnieniem wg indeksu)
	card.BackgroundTransparency = 1
	task.delay(math.min(index, 8) * 0.035, function()
		if card.Parent then
			UiKit.Tween(card, UiKit.Ease(0.3), { BackgroundTransparency = 0.16 })
		end
	end)
	return card
end

local function headerLabel(parent, text, subtitle)
	local header = Instance.new("TextLabel")
	header.Size = UDim2.new(1, -8, 0, 24)
	header.BackgroundTransparency = 1
	header.Font = UiKit.Tokens.FontBlack
	header.TextSize = 15
	header.TextXAlignment = Enum.TextXAlignment.Left
	header.TextColor3 = UiKit.Tokens.Text
	header.Text = text
	header.Parent = parent
	if subtitle then
		local sub = Instance.new("TextLabel")
		sub.Size = UDim2.new(1, -8, 0, 16)
		sub.Position = UDim2.new(0, 0, 0, 24)
		sub.BackgroundTransparency = 1
		sub.Font = UiKit.Tokens.Font
		sub.TextSize = 11
		sub.TextXAlignment = Enum.TextXAlignment.Left
		sub.TextColor3 = UiKit.Tokens.TextMuted
		sub.Text = subtitle
		sub.Parent = parent
	end
end

-- ————————————————————————————————————————————————
-- TREŚĆ ZAKŁADEK (te same zachowania co wcześniej, nowy wygląd)
-- ————————————————————————————————————————————————
local function renderTrails(list)
	local snapshot = state.Snapshot
	local owned = {}
	for _, id in ipairs(snapshot.Trails or {}) do
		owned[id] = true
	end
	for _, trail in ipairs(config.Trails) do
		local isOwned = owned[trail.id] or (trail.vipOnly and snapshot.Gamepasses and snapshot.Gamepasses.vip)
		local equipped = snapshot.EquippedTrail == trail.id
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "✨",
			Accent = UiKit.Rarity[trail.name] or UiKit.Tokens.Accent,
			Title = trail.name .. (trail.vipOnly and "  (VIP)" or ""),
			Subtitle = isOwned and "posiadany" or ("cena: " .. fmt(trail.price) .. " monet"),
			Chip = isOwned and nil or (fmt(trail.price) .. " 🪙"),
			ButtonText = equipped and "ZDJMIJ" or (isOwned and "ZAŁÓŻ" or "KUP"),
			ButtonColor = equipped and UiKit.Tokens.GlassLight or (isOwned and UiKit.Tokens.Success or UiKit.Tokens.Accent),
			OnActivate = function()
				task.spawn(function()
					local ok, message
					if isOwned then
						ok, message = net:Invoke("EquipTrail", equipped and "" or trail.id)
					else
						ok, message = net:Invoke("BuyTrail", trail.id)
					end
					notify(ok, message)
				end)
			end,
		})
	end
end

local function renderForge(list)
	local snapshot = state.Snapshot
	local items = snapshot.ShopItems or {}
	for _, item in ipairs(items) do
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🛠️",
			Accent = UiKit.Tokens.AccentViolet,
			Title = item.Name or item.Id,
			Subtitle = item.Description or "",
			Chip = item.Owned and nil or ((item.Price or 0) .. " 🪙"),
			ButtonText = item.Owned and "MASZ ✔" or "KUP",
			ButtonColor = item.Owned and UiKit.Tokens.GlassLight or UiKit.Tokens.Success,
			OnActivate = function()
				if item.Owned then
					return
				end
				task.spawn(function()
					local ok, message = net:Invoke("ForgeBuy", item.Id)
					notify(ok, message)
				end)
			end,
		})
	end
	if #items == 0 then
		itemCard(list, 1, {
			Icon = "🧭",
			Accent = UiKit.Tokens.TextMuted,
			Title = "Ten tryb nie ma własnego sklepu",
			Subtitle = "kupuj ślady i przepustki na pozostałych zakładkach",
			ButtonText = "—",
			ButtonColor = UiKit.Tokens.GlassLight,
			OnActivate = function() end,
		})
	end
end

local function renderOffers(list)
	local snapshot = state.Snapshot
	for _, offer in ipairs(snapshot.Offers or {}) do
		local hours = math.floor(offer.ExpiresIn / 3600)
		local minutes = math.floor((offer.ExpiresIn % 3600) / 60)
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "⏳",
			Accent = UiKit.Tokens.Warning,
			Title = offer.Name .. "  ·  " .. (offer.Label or ""),
			Subtitle = (offer.Description or "") .. "  ·  ZNIKA ZA " .. hours .. " h " .. minutes .. " min",
			ButtonText = (offer.RobuxProductId or 0) > 0 and "KUP ZA ROBUX" or "⚠ podpiąć ID",
			ButtonColor = (offer.RobuxProductId or 0) > 0 and UiKit.Tokens.Warning or UiKit.Tokens.GlassLight,
			OnActivate = function()
				if (offer.RobuxProductId or 0) > 0 then
					MarketplaceService:PromptProductPurchase(player, offer.RobuxProductId)
				elseif hud then
					hud.Notify("error", "Twórca gry musi podpiąć ID zestawu (README-MONEY.md).")
				end
			end,
		})
	end
	if #(snapshot.Offers or {}) == 0 then
		itemCard(list, 1, {
			Icon = "🌙",
			Accent = UiKit.Tokens.TextMuted,
			Title = "Brak aktywnych ofert",
			Subtitle = "zaglądaj tu czasem — zestawy się pojawiają",
			ButtonText = "—",
			ButtonColor = UiKit.Tokens.GlassLight,
			OnActivate = function() end,
		})
	end
end

local function renderSpin(list)
	local snapshot = state.Snapshot
	local spin = snapshot.Spins or {}
	local canSpin = (spin.Available or 0) > 0
	itemCard(list, 1, {
		Icon = "🎡",
		Accent = UiKit.Tokens.Gold,
		Title = "🎡 KOŁO FORTUNY",
		Subtitle = "darmowy spin co dzień · szanse widoczne poniżej",
		ButtonText = canSpin and "ZAKRĘĆ (darmowy)" or "WRÓĆ JUTRO",
		ButtonColor = canSpin and UiKit.Tokens.Gold or UiKit.Tokens.GlassLight,
		OnActivate = function()
			task.spawn(function()
				local ok, message = net:Invoke("Spin")
				notify(ok, message)
			end)
		end,
	})
	for _, reward in ipairs(spin.Rewards or {}) do
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🎟️",
			Accent = UiKit.Tokens.TextMuted,
			Title = reward.Label or "?",
			Subtitle = "jawna szansa z serwera",
			Chip = (reward.Percent or 0) .. "%",
			ChipColor = UiKit.Tokens.Accent,
			ButtonText = "—",
			ButtonColor = UiKit.Tokens.GlassLight,
			OnActivate = function() end,
		})
	end
	itemCard(list, #list:GetChildren() + 1, {
		Icon = "➕",
		Accent = UiKit.Tokens.AccentViolet,
		Title = "Extra spin",
		Subtitle = "dokup spin za Robux — do użycia od razu",
		ButtonText = (spin.PaidProductId or 0) > 0 and "KUP ZA ROBUX" or "⚠ podpiąć ID",
		ButtonColor = (spin.PaidProductId or 0) > 0 and UiKit.Tokens.AccentViolet or UiKit.Tokens.GlassLight,
		OnActivate = function()
			if (spin.PaidProductId or 0) > 0 then
				MarketplaceService:PromptProductPurchase(player, spin.PaidProductId)
			elseif hud then
				hud.Notify("error", "Twórca gry musi podpiąć ID produktu (README-MONEY.md).")
			end
		end,
	})
end

local function renderPass(list)
	local snapshot = state.Snapshot
	local pass = snapshot.Pass or { Tier = 1, Tiers = 30, XpPerTier = 250, Premium = false, Xp = 0 }
	headerLabel(list, "🎟️ SEZON — poziom " .. (pass.Tier or 1) .. "/" .. (pass.Tiers or 30),
		"XP: " .. (pass.Xp or 0) .. "/" .. (pass.XpPerTier or 250) .. " na kolejny poziom · XP leci samo za granie")
	local fraction = math.clamp((pass.Xp or 0) / math.max(1, pass.XpPerTier or 250), 0, 1)
	local barBack, barFill = UiKit.Bar(list, UDim2.new(1, -10, 0, 8), UDim2.new(0, 5, 0, 50), UiKit.Tokens.AccentViolet)
	UiKit.Tween(barFill, UiKit.Ease(0.6), { Size = UDim2.new(fraction, 0, 1, 0) })
	local spacer = Instance.new("Frame")
	spacer.Size = UDim2.new(1, -10, 0, 12)
	spacer.BackgroundTransparency = 1
	spacer.LayoutOrder = 999999
	spacer.Parent = list

	local startTier = math.max(1, (pass.Tier or 1) - 1)
	local endTier = math.min(pass.Tiers or 30, startTier + 7)
	for tier = startTier, endTier do
		local unlocked = tier <= (pass.Tier or 1)
		local rewardCoins = (200 + 25 * tier)
		itemCard(list, #list:GetChildren() + 1, {
			Icon = tier % 5 == 0 and "💎" or "🎁",
			Accent = UiKit.Tokens.Success,
			Title = "Poziom " .. tier .. " — DARMOWY",
			Subtitle = unlocked and ("nagroda: +" .. rewardCoins .. " monet") or ("za " .. ((tier - (pass.Tier or 1)) * (pass.XpPerTier or 250)) .. " XP"),
			ButtonText = unlocked and "ODBIERZ" or "🔒",
			ButtonColor = unlocked and UiKit.Tokens.Success or UiKit.Tokens.GlassLight,
			OnActivate = function()
				task.spawn(function()
					local ok, message = net:Invoke("ClaimPass", tier, "free")
					notify(ok, message)
				end)
			end,
		})
		if pass.Premium then
			itemCard(list, #list:GetChildren() + 1, {
				Icon = "👑",
				Accent = UiKit.Tokens.AccentViolet,
				Title = "Poziom " .. tier .. " — PREMIUM",
				Subtitle = "nagroda: +" .. (rewardCoins * 3) .. " monet" .. (tier % 5 == 0 and " + 1 spin" or ""),
				ButtonText = unlocked and "ODBIERZ" or "🔒",
				ButtonColor = unlocked and UiKit.Tokens.AccentViolet or UiKit.Tokens.GlassLight,
				OnActivate = function()
					task.spawn(function()
						local ok, message = net:Invoke("ClaimPass", tier, "premium")
						notify(ok, message)
					end)
				end,
			})
		end
	end
end

local function renderPets(list)
	local snapshot = state.Snapshot
	local equippedSet = {}
	for _, uid in ipairs(snapshot.EquippedPets or {}) do
		equippedSet[uid] = true
	end
	for _, egg in ipairs(config.Eggs) do
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🥚",
			Accent = UiKit.Tokens.Gold,
			Title = egg.name,
			Subtitle = "losowanie jednego z " .. #egg.pets .. " zwierzaków",
			Chip = fmt(egg.price) .. " 🪙",
			ButtonText = "LOSUJ",
			ButtonColor = UiKit.Tokens.Gold,
			OnActivate = function()
				task.spawn(function()
					local ok, message = net:Invoke("BuyEgg", egg.id)
					notify(ok, message)
				end)
			end,
		})
	end
	for _, pet in ipairs(snapshot.Pets or {}) do
		local equipped = equippedSet[pet.Uid]
		local rarityColor = UiKit.Rarity[pet.Rarity] or UiKit.Tokens.Accent
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🐾",
			Accent = rarityColor,
			Title = pet.Name .. "  (" .. pet.Rarity .. ")",
			Subtitle = "bonus monet ×" .. pet.Bonus .. (pet.MagnetRadius > 0 and (" · magnes " .. pet.MagnetRadius .. " studów") or ""),
			ButtonText = equipped and "ZDEJMIJ" or "WYPOSAŻ",
			ButtonColor = equipped and UiKit.Tokens.GlassLight or rarityColor,
			OnActivate = function()
				task.spawn(function()
					local ok, message = net:Invoke("EquipPet", equipped and "" or pet.Uid)
					notify(ok, message)
				end)
			end,
		})
	end
	if #snapshot.Pets == 0 then
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🪹",
			Accent = UiKit.Tokens.TextMuted,
			Title = "Brak zwierzaków",
			Subtitle = "wylosuj pierwsze z jajka powyżej",
			ButtonText = "—",
			ButtonColor = UiKit.Tokens.GlassLight,
			OnActivate = function() end,
		})
	end
end

local function renderPasses(list)
	local snapshot = state.Snapshot
	for _, pass in ipairs(config.Gamepasses) do
		local owned = snapshot.Gamepasses and snapshot.Gamepasses[pass.id]
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "🎫",
			Accent = owned and UiKit.Tokens.Success or UiKit.Tokens.AccentBlue or UiKit.Tokens.Accent,
			Title = pass.name,
			Subtitle = pass.description
				.. ((not owned and pass.robuxProductId == 0) and "  ·  ⚠️ twórca musi podpiąć ID (README-GRA)" or ""),
			ButtonText = owned and "MASZ ✔" or "KUP ZA ROBUX",
			ButtonColor = owned and UiKit.Tokens.GlassLight or UiKit.Tokens.Accent,
			OnActivate = function()
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
			end,
		})
	end
end

local function renderCoins(list)
	for _, product in ipairs(config.DevProducts) do
		itemCard(list, #list:GetChildren() + 1, {
			Icon = "💰",
			Accent = UiKit.Tokens.Gold,
			Title = product.name,
			Subtitle = product.robuxProductId == 0 and "⚠️ twórca musi podpiąć ID (README-GRA)" or "zakup za Robux natychmiast na konto",
			ButtonText = "KUP ZA ROBUX",
			ButtonColor = product.robuxProductId == 0 and UiKit.Tokens.GlassLight or UiKit.Tokens.Gold,
			OnActivate = function()
				if product.robuxProductId == 0 then
					if hud then
						hud.Notify("error", "Twórca gry musi jeszcze podpiąć ID tego produktu (README-GRA.md).")
					end
					return
				end
				MarketplaceService:PromptProductPurchase(player, product.robuxProductId)
			end,
		})
	end
end

local renderers = {
	trails = renderTrails,
	forge = renderForge,
	offers = renderOffers,
	spin = renderSpin,
	pass = renderPass,
	pets = renderPets,
	passes = renderPasses,
	coins = renderCoins,
}

local function refreshButtons()
	local snapshot = state.Snapshot
	if not snapshot or not refs.List then
		return
	end
	refs.List:ClearAllChildren()
	if renderers[activeTab] then
		renderers[activeTab](refs.List)
	end
	-- żywy licznik monet w nagłówku sklepu
	if refs.CoinsHeader and snapshot.Coins then
		refs.CoinsHeader.Text = "🪙 " .. fmt(snapshot.Coins)
	end
end

local function selectTab(tabId)
	activeTab = tabId
	for id, entry in pairs(refs.Tabs or {}) do
		local selected = id == activeTab
		entry.Button.BackgroundColor3 = selected and UiKit.Tokens.Accent or UiKit.Tokens.Glass
		entry.Button.BackgroundTransparency = selected and 0 or 0.25
		entry.Button.TextColor3 = selected and UiKit.Tokens.OnAccent or UiKit.Tokens.TextSoft
		entry.Pill.Visible = selected
		if selected then
			UiKit.Tween(entry.Pill, UiKit.Spring(0.4), { Size = UDim2.new(0, 3, 0.5, 0) })
		end
	end
	refreshButtons()
end

-- ————————————————————————————————————————————————
-- INIT: okno sklepu (rail + karty) + przycisk na dolnym pasku
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

	-- okno: szklane, otwiera się sprężyną
	local panel = Instance.new("Frame")
	panel.Size = UDim2.fromOffset(760, 500)
	panel.Position = UDim2.fromScale(0.5, 0.5)
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	panel.BackgroundColor3 = UiKit.Tokens.Glass
	panel.BackgroundTransparency = 0.06
	panel.BorderSizePixel = 0
	panel.Visible = false
	panel.Parent = screen
	UiKit.Corner(18).Parent = panel
	UiKit.Stroke(UiKit.Tokens.GlassBorder, 1, 0.2).Parent = panel
	local panelScale = Instance.new("UIScale")
	panelScale.Parent = panel
	refs.Panel = panel

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -220, 0, 30)
	title.Position = UDim2.new(0, 20, 0, 14)
	title.BackgroundTransparency = 1
	title.Font = UiKit.Tokens.FontBlack
	title.TextSize = 19
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextColor3 = UiKit.Tokens.Text
	title.Text = "🛒 SKLEP — " .. config.GameName
	title.Parent = panel

	-- żywy licznik monet w nagłówku
	local coinsChip = UiKit.Glass(panel, UDim2.fromOffset(120, 30), UDim2.new(1, -158, 0, 14), 12)
	coinsChip.BackgroundTransparency = 0.3
	refs.CoinsHeader = Instance.new("TextLabel")
	refs.CoinsHeader.Size = UDim2.fromScale(1, 1)
	refs.CoinsHeader.BackgroundTransparency = 1
	refs.CoinsHeader.Font = UiKit.Tokens.FontBold
	refs.CoinsHeader.TextSize = 13
	refs.CoinsHeader.TextColor3 = UiKit.Tokens.Gold
	refs.CoinsHeader.Text = "🪙 0"
	refs.CoinsHeader.Parent = coinsChip

	local close = Instance.new("TextButton")
	close.Size = UDim2.fromOffset(30, 30)
	close.Position = UDim2.new(1, -42, 0, 14)
	close.BackgroundTransparency = 1
	close.Font = UiKit.Tokens.FontBold
	close.TextSize = 18
	close.TextColor3 = UiKit.Tokens.TextMuted
	close.Text = "✕"
	close.Parent = panel
	close.Activated:Connect(function()
		UiKit.CloseModal(panel)
	end)

	-- LEWY RAIL zakładek: ikona + nazwa, przesuwane zaznaczenie
	local rail = Instance.new("Frame")
	rail.Size = UDim2.new(0, 176, 1, -70)
	rail.Position = UDim2.new(0, 12, 0, 58)
	rail.BackgroundTransparency = 1
	rail.Parent = panel
	local railLayout = Instance.new("UIListLayout")
	railLayout.Padding = UDim.new(0, 5)
	railLayout.SortOrder = Enum.SortOrder.LayoutOrder
	railLayout.Parent = rail

	refs.Tabs = {}
	local tabDefs = {
		{ id = "offers", name = "OFERTY", icon = "⏳" },
		{ id = "spin", name = "SPIN", icon = "🎡" },
		{ id = "pass", name = "SEZON", icon = "🎟️" },
		{ id = "trails", name = "ŚLADY", icon = "✨" },
		{ id = "forge", name = "SKLEP TRYBU", icon = "🛠️" },
		{ id = "pets", name = "ZWIERZAKI", icon = "🐾" },
		{ id = "passes", name = "PRZEPUSTKI", icon = "🎫" },
		{ id = "coins", name = "MONETY", icon = "💰" },
	}
	for order, tabDef in ipairs(tabDefs) do
		local button = Instance.new("TextButton")
		button.Size = UDim2.new(1, 0, 0, 38)
		button.BackgroundColor3 = UiKit.Tokens.Glass
		button.BackgroundTransparency = 0.25
		button.Font = UiKit.Tokens.FontBold
		button.TextSize = 13
		button.TextColor3 = UiKit.Tokens.TextSoft
		button.Text = ""
		button.AutoButtonColor = false
		button.BorderSizePixel = 0
		button.LayoutOrder = order
		button.Parent = rail
		UiKit.Corner(11).Parent = button

		local iconLabel = Instance.new("TextLabel")
		iconLabel.Size = UDim2.fromOffset(26, 26)
		iconLabel.Position = UDim2.new(0, 10, 0.5, -13)
		iconLabel.BackgroundTransparency = 1
		iconLabel.Font = UiKit.Tokens.FontBold
		iconLabel.TextSize = 16
		iconLabel.TextColor3 = UiKit.Tokens.Text
		iconLabel.Text = tabDef.icon
		iconLabel.Parent = button

		local nameLabel = Instance.new("TextLabel")
		nameLabel.Size = UDim2.new(1, -44, 1, 0)
		nameLabel.Position = UDim2.new(0, 42, 0, 0)
		nameLabel.BackgroundTransparency = 1
		nameLabel.Font = UiKit.Tokens.FontBold
		nameLabel.TextSize = 12
		nameLabel.TextXAlignment = Enum.TextXAlignment.Left
		nameLabel.TextTruncate = Enum.TextTruncate.AtEnd
		nameLabel.TextColor3 = UiKit.Tokens.TextSoft
		nameLabel.Text = tabDef.name
		nameLabel.Parent = button

		local pill = Instance.new("Frame")
		pill.Size = UDim2.new(0, 3, 0.5, 0)
		pill.Position = UDim2.new(0, 2, 0.25, 0)
		pill.BackgroundColor3 = UiKit.Tokens.Accent
		pill.BorderSizePixel = 0
		pill.Visible = false
		pill.Parent = button
		UiKit.Corner(2).Parent = pill

		button.MouseEnter:Connect(function()
			if id ~= activeTab then
				UiKit.Tween(button, UiKit.Ease(0.15), { BackgroundTransparency = 0.05 })
			end
		end)
		button.MouseLeave:Connect(function()
			if id ~= activeTab then
				UiKit.Tween(button, UiKit.Ease(0.2), { BackgroundTransparency = 0.25 })
			end
		end)
		button.Activated:Connect(function()
			selectTab(tabDef.id)
		end)
		refs.Tabs[tabDef.id] = { Button = button, Pill = pill }
	end

	-- przewijana lista kart (prawa strona)
	local scroll = Instance.new("ScrollingFrame")
	scroll.Size = UDim2.new(1, -214, 1, -76)
	scroll.Position = UDim2.new(0, 202, 0, 62)
	scroll.BackgroundTransparency = 1
	scroll.BorderSizePixel = 0
	scroll.ScrollBarThickness = 4
	scroll.ScrollBarImageColor3 = UiKit.Tokens.Accent
	scroll.CanvasSize = UDim2.new(0, 0, 0, 0)
	scroll.AutomaticCanvasSize = Enum.AutomaticSize.Y
	scroll.Parent = panel
	refs.List = scroll
	local listLayout = Instance.new("UIListLayout")
	listLayout.Padding = UDim.new(0, 7)
	listLayout.SortOrder = Enum.SortOrder.LayoutOrder
	listLayout.Parent = scroll

	-- przycisk otwierający (dolny pasek): szkło + złota obwódka
	local open = Instance.new("TextButton")
	open.Size = UDim2.fromOffset(136, 42)
	open.Position = UDim2.new(0.5, -65, 1, -56)
	open.BackgroundColor3 = UiKit.Tokens.Glass
	open.BackgroundTransparency = 0.1
	open.Font = UiKit.Tokens.FontBold
	open.TextSize = 14
	open.AutoButtonColor = false
	open.TextColor3 = UiKit.Tokens.Text
	open.Text = "🛒 SKLEP"
	open.Parent = screen
	UiKit.Corner(12).Parent = open
	UiKit.Stroke(UiKit.Tokens.Gold, 1.3, 0.25).Parent = open
	local openScale = Instance.new("UIScale")
	openScale.Parent = open
	open.MouseEnter:Connect(function()
		UiKit.Tween(openScale, UiKit.Ease(0.15), { Scale = 1.05 })
	end)
	open.MouseLeave:Connect(function()
		UiKit.Tween(openScale, UiKit.Ease(0.2), { Scale = 1 })
	end)
	open.Activated:Connect(function()
		panel.Visible = not panel.Visible
		if panel.Visible then
			UiKit.OpenModal(panel)
			selectTab(activeTab)
		end
	end)

	selectTab("trails")
	print("[MegaObby] Sklep zbudowany (UiKit: rail + karty + sprężyny).")
end

function ShopUI.OnSnapshot(_snapshot)
	if refs.Panel and refs.Panel.Visible then
		refreshButtons()
	end
end

return ShopUI
