--!strict
-- ============================================================================
-- MEGA OBBY — SettingsUI (Client/Controllers/SettingsUI)
-- Panel ustawień LOKALNYCH (nic nie idzie na serwer): muzyka wł/wył, efekty
-- dźwiękowe wł/wył, pole widzenia (FOV) kamery 70–100. Przycisk na dolnym
-- pasku, panel z przełącznikami.
-- ============================================================================

local Players = game:GetService("Players")
local Workspace = game:GetService("Workspace")

local SettingsUI = {}

local player = Players.LocalPlayer

local state, config, net, music
local panel = nil

local function color3From(parts)
	return Color3.fromRGB(parts[1], parts[2], parts[3])
end

local function corner(radius)
	local c = Instance.new("UICorner")
	c.CornerRadius = UDim.new(0, radius)
	return c
end

local function applyFov()
	local camera = Workspace.CurrentCamera
	if camera then
		camera.FieldOfView = state.Settings.Fov
	end
end

local function toggleRow(parent, offsetY, label, getValue, setValue)
	local row = Instance.new("Frame")
	row.Size = UDim2.new(1, -24, 0, 44)
	row.Position = UDim2.new(0, 12, 0, offsetY)
	row.BackgroundColor3 = color3From(config.Ui.PanelColor)
	row.BackgroundTransparency = 0.1
	row.BorderSizePixel = 0
	row.Parent = parent
	corner(10).Parent = row

	local text = Instance.new("TextLabel")
	text.Size = UDim2.new(1, -90, 1, 0)
	text.Position = UDim2.new(0, 12, 0, 0)
	text.BackgroundTransparency = 1
	text.Font = Enum.Font.GothamBold
	text.TextSize = 15
	text.TextXAlignment = Enum.TextXAlignment.Left
	text.TextColor3 = Color3.fromRGB(240, 244, 255)
	text.Text = label
	text.Parent = row

	local button = Instance.new("TextButton")
	button.Size = UDim2.new(0, 70, 0.64, 0)
	button.Position = UDim2.new(1, -82, 0.18, 0)
	button.Font = Enum.Font.GothamBold
	button.TextSize = 14
	button.TextColor3 = Color3.fromRGB(15, 18, 25)
	button.Text = getValue() and "WŁĄCZONO" or "WYŁĄCZONO"
	button.BackgroundColor3 = getValue() and color3From(config.Ui.SuccessColor) or Color3.fromRGB(120, 130, 150)
	button.Parent = row
	corner(8).Parent = button

	button.Activated:Connect(function()
		setValue(not getValue())
		button.Text = getValue() and "WŁĄCZONO" or "WYŁĄCZONO"
		button.BackgroundColor3 = getValue() and color3From(config.Ui.SuccessColor) or Color3.fromRGB(120, 130, 150)
	end)
	return row
end

local function fovRow(parent, offsetY)
	local row = Instance.new("Frame")
	row.Size = UDim2.new(1, -24, 0, 44)
	row.Position = UDim2.new(0, 12, 0, offsetY)
	row.BackgroundColor3 = color3From(config.Ui.PanelColor)
	row.BackgroundTransparency = 0.1
	row.BorderSizePixel = 0
	row.Parent = parent
	corner(10).Parent = row

	local text = Instance.new("TextLabel")
	text.Size = UDim2.new(1, -180, 1, 0)
	text.Position = UDim2.new(0, 12, 0, 0)
	text.BackgroundTransparency = 1
	text.Font = Enum.Font.GothamBold
	text.TextSize = 15
	text.TextXAlignment = Enum.TextXAlignment.Left
	text.TextColor3 = Color3.fromRGB(240, 244, 255)
	text.Text = "Pole widzenia (FOV)"
	text.Parent = row

	local input = Instance.new("TextBox")
	input.Size = UDim2.new(0, 70, 0.64, 0)
	input.Position = UDim2.new(1, -82, 0.18, 0)
	input.BackgroundColor3 = Color3.fromRGB(35, 42, 56)
	input.Font = Enum.Font.GothamBold
	input.TextSize = 14
	input.TextColor3 = Color3.fromRGB(240, 244, 255)
	input.Text = tostring(state.Settings.Fov)
	input.ClearTextOnFocus = false
	input.Parent = row
	corner(8).Parent = input

	input.FocusLost:Connect(function(enterPressed)
		if not enterPressed then
			return
		end
		local value = tonumber(input.Text)
		if value then
			value = math.clamp(math.floor(value), 70, 100)
			state.Settings.Fov = value
			applyFov()
		end
		input.Text = tostring(state.Settings.Fov)
	end)
	return row
end

function SettingsUI.Init(stateIn, configIn, _netIn, musicIn)
	state, config, music = stateIn, configIn, musicIn

	local screen = Instance.new("ScreenGui")
	screen.Name = "MegaObbySettings"
	screen.ResetOnSpawn = false
	screen.DisplayOrder = 6
	screen.Parent = player:WaitForChild("PlayerGui")

	panel = Instance.new("Frame")
	panel.Size = UDim2.fromOffset(360, 260)
	panel.Position = UDim2.fromScale(0.5, 0.5)
	panel.AnchorPoint = Vector2.new(0.5, 0.5)
	panel.BackgroundColor3 = Color3.fromRGB(18, 22, 30)
	panel.BorderSizePixel = 0
	panel.Visible = false
	panel.Parent = screen
	corner(16).Parent = panel

	local title = Instance.new("TextLabel")
	title.Size = UDim2.new(1, -60, 0, 40)
	title.Position = UDim2.new(0, 16, 0, 8)
	title.BackgroundTransparency = 1
	title.Font = Enum.Font.GothamBlack
	title.TextSize = 20
	title.TextXAlignment = Enum.TextXAlignment.Left
	title.TextColor3 = Color3.fromRGB(240, 244, 255)
	title.Text = "⚙ USTAWIENIA (lokalne)"
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

	toggleRow(panel, 64, "🎵 Muzyka",
		function() return state.Settings.Music end,
		function(value) music.SetEnabled(value) end)
	toggleRow(panel, 116, "💥 Efekty dźwiękowe",
		function() return state.Settings.Effects end,
		function(value) state.Settings.Effects = value end)
	fovRow(panel, 168)

	local open = Instance.new("TextButton")
	open.Size = UDim2.fromOffset(140, 40)
	open.Position = UDim2.new(0.5, 85, 1, -54)
	open.BackgroundColor3 = color3From(config.Ui.PanelColor)
	open.Font = Enum.Font.GothamBold
	open.TextSize = 14
	open.TextColor3 = Color3.fromRGB(240, 244, 255)
	open.Text = "⚙ USTAWIENIA"
	open.Parent = screen
	corner(10).Parent = open
	local stroke = Instance.new("UIStroke")
	stroke.Color = color3From(config.Ui.AccentColor)
	stroke.Transparency = 0.35
	stroke.Parent = open
	open.Activated:Connect(function()
		panel.Visible = not panel.Visible
	end)

	applyFov()
	print("[MegaObby] Ustawienia gotowe (muzyka, efekty, FOV).")
end

return SettingsUI
