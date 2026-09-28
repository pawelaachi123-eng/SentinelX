-- ============================================================================
-- MEGA OBBY — UiKit (Client/UiKit)
-- Wspólny system designu DLA CAŁEGO UI gry (HUD, sklep, ustawienia).
-- Styl: „szklane panele” (glassmorphism) na ciemnym tle — półprzezroczyste
-- karty, cienkie jasne obwódki, zaokrąglenia, poświata akcentu i animacje
-- sprężynowe. Wszystko budowane z KODU, zero klikania w Studio.
-- Główne elementy:
--   · UiKit.Glass      — szklana karta (tło + obwódka + narożnik)
--   · UiKit.Button     — przycisk z hoverem (unoszenie) i wciśnięciem
--   · UiKit.Chip       — mała etykieta (cena, rzadkość, %)
--   · UiKit.Bar        — pasek postępu z animowanym wypełnieniem
--   · UiKit.Toast      — powiadomienie wpadające z góry (stos w kontenerze)
--   · UiKit.Confetti   — konfetti przy nagrodach (kolorowe kwadraciki)
--   · UiKit.CountUp    — licznik, który „goni” wartość docelową
--   · UiKit.Shine      — przebiegający połysk po otwarciu karty
--   · UiKit.Spring     — TweenInfo ze sprężyną (Back/Quart)
-- Kolory: jedno miejsce w grze, w którym definiujemy paletę.
-- ============================================================================

local TweenService = game:GetService("TweenService")
local RunService = game:GetService("RunService")

local UiKit = {}

-- ————————————————————————————————————————————————
-- TOKENY: paleta całego UI (ciemne szkło + akcenty)
-- ————————————————————————————————————————————————
UiKit.Tokens = {
	Background = Color3.fromRGB(11, 13, 20),
	Glass = Color3.fromRGB(21, 26, 38),
	GlassLight = Color3.fromRGB(31, 37, 52),
	GlassBorder = Color3.fromRGB(255, 255, 255),
	BorderOpacity = 0.14,
	FillOpacity = 0.86,
	Text = Color3.fromRGB(237, 241, 250),
	TextSoft = Color3.fromRGB(168, 178, 198),
	TextMuted = Color3.fromRGB(120, 130, 152),
	Accent = Color3.fromRGB(34, 211, 238),
	AccentViolet = Color3.fromRGB(167, 139, 250),
	AccentPink = Color3.fromRGB(244, 114, 182),
	Gold = Color3.fromRGB(255, 208, 84),
	Success = Color3.fromRGB(74, 222, 128),
	Warning = Color3.fromRGB(251, 191, 36),
	Error = Color3.fromRGB(251, 113, 133),
	OnAccent = Color3.fromRGB(9, 14, 22),
	Radius = 14,
	RadiusSm = 9,
	Font = Enum.Font.Gotham,
	FontBold = Enum.Font.GothamBold,
	FontBlack = Enum.Font.GothamBlack,
}

-- Mapy rzadkości: kolor + emoji dla zwierzaków i ryb (spójne w całym UI)
UiKit.Rarity = {
	["Wspólny"] = Color3.fromRGB(190, 190, 200),
	["Wspólna"] = Color3.fromRGB(190, 190, 200),
	["Niezwykły"] = Color3.fromRGB(120, 200, 90),
	["Niezwykła"] = Color3.fromRGB(120, 200, 90),
	["Rzadki"] = Color3.fromRGB(90, 150, 255),
	["Rzadka"] = Color3.fromRGB(90, 150, 255),
	["Epicki"] = Color3.fromRGB(190, 110, 255),
	["Epicka"] = Color3.fromRGB(190, 110, 255),
	["Legendarny"] = UiKit.Tokens.Gold,
	["Legendarna"] = UiKit.Tokens.Gold,
}

-- ————————————————————————————————————————————————
-- BAZOWE BLOKI
-- ————————————————————————————————————————————————
function UiKit.Corner(radius)
	local c = Instance.new("UICorner")
	c.CornerRadius = UDim.new(0, radius or UiKit.Tokens.Radius)
	return c
end

function UiKit.Stroke(color, thickness, opacity)
	local s = Instance.new("UIStroke")
	s.Color = color or UiKit.Tokens.GlassBorder
	s.Thickness = thickness or 1
	s.Transparency = opacity or UiKit.Tokens.BorderOpacity
	s.ApplyStrokeMode = Enum.ApplyStrokeMode.Border
	return s
end

function UiKit.Gradient(colorA, colorB, rotation)
	local g = Instance.new("UIGradient")
	g.Color = ColorSequence.new({
		ColorSequenceKeypoint.new(0, colorA),
		ColorSequenceKeypoint.new(1, colorB),
	})
	g.Rotation = rotation or 90
	return g
end

-- Szybki konstruktor: UiKit.New("Frame", { Size = …, Parent = … }, { dziecko, … })
function UiKit.New(className, props, children)
	local inst = Instance.new(className)
	if props then
		for key, value in pairs(props) do
			inst[key] = value
		end
	end
	if children then
		for _, child in ipairs(children) do
			child.Parent = inst
		end
	end
	return inst
end

function UiKit.Spring(time)
	return TweenInfo.new(time or 0.35, Enum.EasingStyle.Back, Enum.EasingDirection.Out)
end

function UiKit.Ease(time)
	return TweenInfo.new(time or 0.25, Enum.EasingStyle.Quart, Enum.EasingDirection.Out)
end

function UiKit.Tween(inst, info, props)
	TweenService:Create(inst, info, props):Play()
end

-- ————————————————————————————————————————————————
-- GLASS: szklana karta — fundament każdego panelu
-- ————————————————————————————————————————————————
function UiKit.Glass(parent, size, position, radius, opts)
	opts = opts or {}
	local frame = Instance.new("Frame")
	frame.Size = size
	frame.Position = position or UDim2.new()
	frame.BackgroundColor3 = opts.Color or UiKit.Tokens.Glass
	frame.BackgroundTransparency = opts.Transparency or UiKit.Tokens.FillOpacity
	frame.BorderSizePixel = 0
	frame.Parent = parent
	UiKit.Corner(radius or UiKit.Tokens.Radius).Parent = frame
	UiKit.Stroke(UiKit.Tokens.GlassBorder, 1, opts.BorderOpacity or UiKit.Tokens.BorderOpacity).Parent = frame
	if opts.Shadow then
		local shadow = Instance.new("ImageLabel")
		shadow.Name = "Shadow"
		shadow.BackgroundTransparency = 1
		shadow.Image = "rbxassetid://1316045217"
		shadow.ImageColor3 = Color3.new(0, 0, 0)
		shadow.ImageTransparency = 0.55
		shadow.ScaleType = Enum.ScaleType.Slice
		shadow.SliceCenter = Rect.new(10, 10, 118, 118)
		shadow.Size = UDim2.new(1, 36, 1, 36)
		shadow.Position = UDim2.new(0, -18, 0, -18)
		shadow.ZIndex = frame.ZIndex - 1
		shadow.Parent = frame
	end
	return frame
end

-- Przebiegający połysk po otwarciu („shine sweep”)
function UiKit.Shine(frame)
	local shine = Instance.new("Frame")
	shine.Size = UDim2.new(0.35, 0, 1, 0)
	shine.Position = UDim2.new(-0.4, 0, 0, 0)
	shine.BackgroundColor3 = Color3.fromRGB(255, 255, 255)
	shine.BackgroundTransparency = 0.88
	shine.BorderSizePixel = 0
	shine.ZIndex = frame.ZIndex + 1
	shine.Parent = frame
	local rotation = Instance.new("UIGradient")
	rotation.Color = ColorSequence.new(Color3.new(1, 1, 1), Color3.new(1, 1, 1))
	rotation.Transparency = NumberSequence.new({
		NumberSequenceKeypoint.new(0, 1),
		NumberSequenceKeypoint.new(0.5, 0.2),
		NumberSequenceKeypoint.new(1, 1),
	})
	rotation.Rotation = 25
	rotation.Parent = shine
	UiKit.Corner(UiKit.Tokens.Radius).Parent = shine
	local tween = TweenService:Create(shine, TweenInfo.new(0.8, Enum.EasingStyle.Quart, Enum.EasingDirection.Out), {
		Position = UDim2.new(1.1, 0, 0, 0),
	})
	tween.Completed:Connect(function()
		shine:Destroy()
	end)
	tween:Play()
end

-- ————————————————————————————————————————————————
-- PRZYCISK: hover = unoszenie, wciśnięcie = ściśnięcie
-- ————————————————————————————————————————————————
function UiKit.Button(parent, text, color, size, position, onClick, opts)
	opts = opts or {}
	local button = Instance.new("TextButton")
	button.Size = size
	button.Position = position or UDim2.new()
	button.BackgroundColor3 = color or UiKit.Tokens.Accent
	button.Font = opts.Font or UiKit.Tokens.FontBold
	button.TextSize = opts.TextSize or 14
	button.TextColor3 = opts.TextColor or UiKit.Tokens.OnAccent
	button.Text = text
	button.AutoButtonColor = false
	button.BorderSizePixel = 0
	button.Parent = parent
	UiKit.Corner(opts.Radius or UiKit.Tokens.RadiusSm).Parent = button
	if opts.Ghost then
		button.BackgroundTransparency = 0.25
	end
	UiKit.Stroke(Color3.fromRGB(255, 255, 255), 1, 0.75).Parent = button

	local scale = Instance.new("UIScale")
	scale.Parent = button
	scale.Scale = 1

	button.MouseEnter:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.16), { Scale = 1.04 })
		UiKit.Tween(button, UiKit.Ease(0.16), { BackgroundTransparency = (opts.Ghost and 0.05) or 0 })
	end)
	button.MouseLeave:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.2), { Scale = 1 })
		UiKit.Tween(button, UiKit.Ease(0.2), { BackgroundTransparency = (opts.Ghost and 0.25) or 0 })
	end)
	button.MouseButton1Down:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.08), { Scale = 0.96 })
	end)
	button.MouseButton1Up:Connect(function()
		UiKit.Tween(scale, UiKit.Ease(0.14), { Scale = 1.02 })
	end)
	if onClick then
		button.Activated:Connect(onClick)
	end
	return button
end

-- ————————————————————————————————————————————————
-- CHIP: mała zaokrąglona etykieta (cena, %, rzadkość)
-- ————————————————————————————————————————————————
function UiKit.Chip(parent, text, color, position, opts)
	opts = opts or {}
	local chip = Instance.new("TextLabel")
	chip.Size = opts.Size or UDim2.fromOffset(74, 24)
	chip.Position = position or UDim2.new()
	chip.BackgroundColor3 = color or UiKit.Tokens.GlassLight
	chip.BackgroundTransparency = opts.Transparency or 0.25
	chip.Font = UiKit.Tokens.FontBold
	chip.TextSize = opts.TextSize or 12
	chip.TextColor3 = opts.TextColor or UiKit.Tokens.Text
	chip.Text = text
	chip.BorderSizePixel = 0
	chip.Parent = parent
	UiKit.Corner(12).Parent = chip
	UiKit.Stroke(color or UiKit.Tokens.GlassBorder, 1, 0.5).Parent = chip
	return chip
end

-- ————————————————————————————————————————————————
-- BAR: pasek postępu (tło + animowane wypełnienie)
-- ————————————————————————————————————————————————
function UiKit.Bar(parent, size, position, color)
	local back = Instance.new("Frame")
	back.Size = size
	back.Position = position or UDim2.new()
	back.BackgroundColor3 = Color3.fromRGB(9, 11, 16)
	back.BackgroundTransparency = 0.25
	back.BorderSizePixel = 0
	back.Parent = parent
	UiKit.Corner(6).Parent = back

	local fill = Instance.new("Frame")
	fill.Size = UDim2.new(0, 0, 1, 0)
	fill.BackgroundColor3 = color or UiKit.Tokens.Accent
	fill.BorderSizePixel = 0
	fill.Parent = back
	UiKit.Corner(6).Parent = fill
	local gloss = Instance.new("Frame")
	gloss.Size = UDim2.new(1, 0, 0.45, 0)
	gloss.BackgroundColor3 = Color3.fromRGB(255, 255, 255)
	gloss.BackgroundTransparency = 0.82
	gloss.BorderSizePixel = 0
	gloss.Parent = fill
	UiKit.Corner(6).Parent = gloss
	return back, fill
end

-- ————————————————————————————————————————————————
-- TOAST: powiadomienie wpadające z góry do kontenera-stosu
-- ————————————————————————————————————————————————
local toastIcons = {
	success = "✅",
	error = "⚠️",
	info = "ℹ️",
}
local toastColors = {
	success = function(tokens) return tokens.Success end,
	error = function(tokens) return tokens.Error end,
	info = function(tokens) return tokens.Accent end,
}

function UiKit.Toast(container, kind, message)
	local tokens = UiKit.Tokens
	local accent = (toastColors[kind] or toastColors.info)(tokens)
	local card = Instance.new("Frame")
	card.Size = UDim2.new(1, -8, 0, 44)
	card.BackgroundColor3 = tokens.Glass
	card.BackgroundTransparency = 0.12
	card.BorderSizePixel = 0
	card.Parent = container
	UiKit.Corner(12).Parent = card
	UiKit.Stroke(accent, 1.4, 0.25).Parent = card

	local dot = Instance.new("Frame")
	dot.Size = UDim2.fromOffset(8, 8)
	dot.Position = UDim2.new(0, 14, 0.5, -4)
	dot.BackgroundColor3 = accent
	dot.BorderSizePixel = 0
	dot.Parent = card
	UiKit.Corner(4).Parent = dot

	local label = Instance.new("TextLabel")
	label.Size = UDim2.new(1, -52, 1, -8)
	label.Position = UDim2.new(0, 32, 0, 4)
	label.BackgroundTransparency = 1
	label.Font = UiKit.Tokens.FontBold
	label.TextSize = 13
	label.TextColor3 = tokens.Text
	label.TextXAlignment = Enum.TextXAlignment.Left
	label.TextTruncate = Enum.TextTruncate.AtEnd
	label.Text = (toastIcons[kind] or toastIcons.info) .. "  " .. message
	label.Parent = card

	card.Position = UDim2.new(0, -24, 0, 0)
	card.BackgroundTransparency = 1
	local stroke = card:FindFirstChildOfClass("UIStroke")
	if stroke then
		stroke.Transparency = 1
	end
	UiKit.Tween(card, UiKit.Spring(0.4), { Position = UDim2.new(0, 4, 0, 0), BackgroundTransparency = 0.12 })
	if stroke then
		UiKit.Tween(stroke, UiKit.Ease(0.4), { Transparency = 0.25 })
	end
	UiKit.Tween(dot, UiKit.Spring(0.5), { Position = UDim2.new(0, 14, 0.5, -4) })

	task.delay(4.2, function()
		local fade = TweenService:Create(card, TweenInfo.new(0.35, Enum.EasingStyle.Quart, Enum.EasingDirection.In), {
			Position = UDim2.new(0, 24, 0, 0),
			BackgroundTransparency = 1,
			TextTransparency = 1,
		})
		fade.Completed:Connect(function()
			card:Destroy()
		end)
		fade:Play()
	end)

	-- stos: maksymalnie 4, starsze usuwamy
	local count = 0
	for _, child in ipairs(container:GetChildren()) do
		if child:IsA("Frame") then
			count += 1
			if count > 4 then
				child:Destroy()
			end
		end
	end
	return card
end

-- ————————————————————————————————————————————————
-- CONFETTI: pęknięcie kolorowych kwadracików (nagrody, komplet indeksu)
-- ————————————————————————————————————————————————
function UiKit.Confetti(parent, center, colors)
	colors = colors or { UiKit.Tokens.Gold, UiKit.Tokens.Accent, UiKit.Tokens.AccentPink, UiKit.Tokens.Success }
	for index = 1, 14 do
		local piece = Instance.new("Frame")
		piece.Size = UDim2.fromOffset(math.random(5, 9), math.random(5, 9))
		piece.Position = center
		piece.BackgroundColor3 = colors[(index % #colors) + 1]
		piece.BorderSizePixel = 0
		piece.Rotation = math.random(0, 180)
		piece.ZIndex = parent.ZIndex + 2
		piece.Parent = parent
		UiKit.Corner(2).Parent = piece
		local direction = (index % 2 == 0) and 1 or -1
		UiKit.Tween(piece, TweenInfo.new(math.random(55, 95) / 100, Enum.EasingStyle.Quart, Enum.EasingDirection.Out), {
			Position = center + UDim2.fromOffset(math.random(40, 130) * direction, math.random(-90, 40)),
			Rotation = math.random(90, 360) * direction,
			BackgroundTransparency = 1,
		})
		task.delay(1.1, function()
			piece:Destroy()
		end)
	end
end

-- ————————————————————————————————————————————————
-- COUNTUP: licznik „goni” wartość (monety, rekordy)
-- ————————————————————————————————————————————————
function UiKit.CountUp(render)
	local shown, target = 0, 0
	RunService.RenderStepped:Connect(function(dt)
		if shown ~= target then
			local diff = target - shown
			local step = math.ceil(math.abs(diff) * math.clamp(dt * 6, 0.08, 1))
			if math.abs(diff) <= step then
				shown = target
			elseif diff > 0 then
				shown += step
			else
				shown -= step
			end
			render(shown)
		end
	end)
	return {
		SetTarget = function(value)
			target = value or 0
		end,
		Punch = function(label)
			UiKit.Tween(label, UiKit.Ease(0.1), { TextSize = 21 })
			task.delay(0.12, function()
				if label.Parent then
					UiKit.Tween(label, UiKit.Spring(0.35), { TextSize = 18 })
				end
			end)
		end,
	}
end

-- ————————————————————————————————————————————————
-- OPEN/CLOSE: modal wjeżdża sprężyną + połysk; wychodzi fadeem
-- ————————————————————————————————————————————————
function UiKit.OpenModal(panel, scaleFrom)
	panel.Visible = true
	local scale = panel:FindFirstChildOfClass("UIScale")
	if scale then
		scale.Scale = scaleFrom or 0.9
		UiKit.Tween(scale, UiKit.Spring(0.4), { Scale = 1 })
	end
	UiKit.Shine(panel)
end

function UiKit.CloseModal(panel, onDestroy)
	local scale = panel:FindFirstChildOfClass("UIScale")
	if scale then
		UiKit.Tween(scale, UiKit.Ease(0.16), { Scale = 0.92 })
	end
	local tween = TweenService:Create(panel, UiKit.Ease(0.18), { BackgroundTransparency = 1 })
	tween.Completed:Connect(function()
		if onDestroy then
			onDestroy()
		else
			panel.Visible = false
			panel.BackgroundTransparency = 0
		end
	end)
	tween:Play()
end

return UiKit
