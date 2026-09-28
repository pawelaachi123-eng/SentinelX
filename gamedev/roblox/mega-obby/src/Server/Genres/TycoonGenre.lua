--!strict
-- ============================================================================
-- GAMEFORGE — gatunek TYCOON (Server/Genres/TycoonGenre)
-- Działki: wejdź na wolną, przejmij ją. Kropelka pada z dozownika na kolektor
-- → monety dla właściciela. Przyciski na działce: szybszy dozownik, większa
-- wartość, drugi dozownik. Zakup w specjalnym sklepie: podwójna wartość i
-- superszybkie kropelki. Wszystko serwerowe (dotyk = zakup, walidacja monet).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local Pack = {}

Pack.Id = "tycoon"
Pack.DisplayName = "Tycoon"

local plots = {} -- [plotId] = { Owner, Interval, Value, Droppers, Parts = {...} }

local function knobs(deps)
	return (deps.GameSpec.Knobs and deps.GameSpec.Knobs[Pack.Id]) or {}
end

local function updateBillboard(button, text, color)
	local billboard = button:FindFirstChildOfClass("BillboardGui")
	local label = billboard and billboard:FindFirstChildOfClass("TextLabel")
	if label then
		label.Text = text
		label.TextColor3 = color
	end
end

local function buildPlot(folders, index, center)
	local plot = Instance.new("Folder")
	plot.Name = "Plot" .. index
	plot.Parent = folders.World

	local floor = Instance.new("Part")
	floor.Name = "PlotFloor"
	floor.Size = Vector3.new(34, 1, 34)
	floor.Position = center + Vector3.new(0, 0.5, 0)
	floor.Anchored = true
	floor.Color = Color3.fromRGB(52, 58, 74)
	floor.Material = Enum.Material.SmoothPlastic
	floor.Parent = plot

	local claim = Instance.new("Part")
	claim.Name = "ClaimPad"
	claim.Size = Vector3.new(8, 0.5, 8)
	claim.Position = center + Vector3.new(0, 1.2, 12)
	claim.Anchored = true
	claim.Color = Color3.fromRGB(34, 211, 238)
	claim.Material = Enum.Material.Neon
	claim:SetAttribute("PlotId", index)
	claim.Parent = plot

	local dropper = Instance.new("Part")
	dropper.Name = "Dropper"
	dropper.Size = Vector3.new(3, 2, 3)
	dropper.Position = center + Vector3.new(-10, 8, -10)
	dropper.Anchored = true
	dropper.Color = Color3.fromRGB(255, 200, 90)
	dropper.Material = Enum.Material.SmoothPlastic
	dropper.Parent = plot

	local collector = Instance.new("Part")
	collector.Name = "Collector"
	collector.Size = Vector3.new(8, 1, 8)
	collector.Position = center + Vector3.new(-10, 1.5, -2)
	collector.Anchored = true
	collector.Color = Color3.fromRGB(90, 220, 120)
	collector.Material = Enum.Material.Neon
	collector:SetAttribute("PlotId", index)
	collector.Parent = plot

	local buttons = {}
	local defs = {
		{ Id = "speed", Name = "Szybszy dozownik", Base = 120 },
		{ Id = "value", Name = "Większa wartość", Base = 200 },
		{ Id = "dropper", Name = "Drugi dozownik", Base = 600 },
	}
	for buttonIndex, def in ipairs(defs) do
		local button = Instance.new("Part")
		button.Name = "Button_" .. def.Id
		button.Size = Vector3.new(4, 0.8, 4)
		button.Position = center + Vector3.new(4 + (buttonIndex - 1) * 7, 1.1, -12)
		button.Anchored = true
		button.Color = Color3.fromRGB(255, 170, 60)
		button.Material = Enum.Material.Neon
		button:SetAttribute("PlotId", index)
		button:SetAttribute("UpgradeId", def.Id)
		button.Parent = plot

		local billboard = Instance.new("BillboardGui")
		billboard.Size = UDim2.fromScale(8, 2)
		billboard.StudsOffset = Vector3.new(0, 3.5, 0)
		billboard.MaxDistance = 80
		local label = Instance.new("TextLabel")
		label.Name = "Label"
		label.Size = UDim2.fromScale(1, 1)
		label.BackgroundTransparency = 1
		label.Font = Enum.Font.GothamBold
		label.TextScaled = true
		label.TextColor3 = Color3.fromRGB(240, 244, 255)
		label.TextStrokeTransparency = 0.4
		label.Text = def.Name .. "\n" .. def.Base .. " 🪙"
		label.Parent = billboard
		billboard.Parent = button
		table.insert(buttons, { Part = button, Def = def, Level = 0 })
	end

	local state = {
		Id = index, Owner = nil, Claim = claim, Dropper = dropper, Collector = collector,
		Buttons = buttons, Interval = 2.0, Value = 1, Droppers = 1, Parts = plot,
	}
	plots[index] = state
	return state
end

function Pack.BuildWorld(folders, deps)
	local world = Instance.new("Folder")
	world.Name = "TycoonWorld"
	world.Parent = folders.World

	local ground = Instance.new("Part")
	ground.Name = "Ground"
	ground.Size = Vector3.new(220, 2, 220)
	ground.Position = Vector3.new(0, -0.5, 0)
	ground.Anchored = true
	ground.Color = Color3.fromRGB(46, 52, 66)
	ground.Material = Enum.Material.Grass
	ground.Parent = world

	local spawnPad = Instance.new("SpawnLocation")
	spawnPad.Name = "TycoonSpawn"
	spawnPad.Size = Vector3.new(14, 1, 14)
	spawnPad.Position = Vector3.new(0, 1.5, 0)
	spawnPad.Anchored = true
	spawnPad.Neutral = true
	spawnPad.Duration = 0
	spawnPad.Color = Color3.fromRGB(34, 211, 238)
	spawnPad.Material = Enum.Material.Neon
	spawnPad.Parent = world

	local centers = { Vector3.new(-60, 0, -60), Vector3.new(60, 0, -60), Vector3.new(-60, 0, 60), Vector3.new(60, 0, 60) }
	for index, center in ipairs(centers) do
		buildPlot(folders, index, center)
	end
	return { SpawnPad = spawnPad, Root = world }
end

local function priceOf(def, level)
	return math.floor(def.Base * 1.5 ^ level)
end

local function ownedBy(player, state)
	return state.Owner == player.UserId
end

function Pack.Setup(deps, world)
	-- przejmowanie działek
	local function armClaim(claim)
		claim.Touched:Connect(function(hit)
			local character = hit.Parent
			local player = character and Players:GetPlayerFromCharacter(character)
			if not player then
				return
			end
			local plotId = claim:GetAttribute("PlotId")
			local state = plots[plotId]
			if state and state.Owner == nil then
				state.Owner = player.UserId
				claim.Color = Color3.fromRGB(90, 220, 120)
				deps.Net:SendTo(player, "Notify", "success", "Przejąłeś działkę #" .. plotId .. " — kropelki płyną!")
				deps.EffectsService.BurstAt(claim.Position, { 90, 220, 120 })
			end
		end)
	end
	for _, state in pairs(plots) do
		armClaim(state.Claim)
	end

	-- przyciski ulepszeń (zakup dotykiem, monety waliduje serwer)
	local function armButton(button)
		button.Touched:Connect(function(hit)
			local character = hit.Parent
			local player = character and Players:GetPlayerFromCharacter(character)
			if not player then
				return
			end
			local state = plots[button:GetAttribute("PlotId")]
			if not (state and ownedBy(player, state)) then
				return
			end
			local now = os.clock()
			local last = button:GetAttribute("Cooldown")
			if last and now - last < 0.5 then
				return
			end
			button:SetAttribute("Cooldown", now)
			local upgradeId = button:GetAttribute("UpgradeId")
			for _, entry in ipairs(state.Buttons) do
				if entry.Def.Id == upgradeId then
					if entry.Level >= 10 then
						deps.Net:SendTo(player, "Notify", "info", "Ten ulepszenie jest na maksimum.")
						return
					end
					local price = priceOf(entry.Def, entry.Level)
					if not deps.PlayerDataService.TrySpend(player, price) then
						return
					end
					entry.Level += 1
					if upgradeId == "speed" then
						state.Interval = math.max(0.4, state.Interval * 0.85)
					elseif upgradeId == "value" then
						state.Value += 1
					elseif upgradeId == "dropper" then
						state.Droppers += 1
					end
					updateBillboard(button, entry.Def.Name .. "\n" .. priceOf(entry.Def, entry.Level) .. " 🪙 (lvl "
						.. entry.Level .. "/10)", Color3.fromRGB(160, 255, 180))
					deps.EffectsService.BurstAt(button.Position, { 255, 200, 90 })
					return
				end
			end
		end)
	end
	for _, state in pairs(plots) do
		for _, entry in ipairs(state.Buttons) do
			armButton(entry.Part)
		end
	end

	-- zbiór kropel: dotyk kolektora → monety właściciela (razem na part)
	local function armCollector(collector)
		collector.Touched:Connect(function(hit)
			local drop = hit.Parent
			local dropPart = drop and drop:IsA("Part") and drop or (hit:IsA("Part") and hit or nil)
			if not (dropPart and dropPart:GetAttribute("IsDrop")) then
				return
			end
			if dropPart:GetAttribute("Collected") then
				return
			end
			dropPart:SetAttribute("Collected", true)
			local state = plots[collector:GetAttribute("PlotId")]
			if state and state.Owner then
				local owner = Players:GetPlayerByUserId(state.Owner)
				if owner then
					local ownerData = deps.PlayerDataService.Get(owner)
					local multiplier = ownerData and (ownerData.Stats.DoubleValue or 0) > 0 and 2 or 1
					deps.PlayerDataService.AddCoins(owner, state.Value * multiplier, "kropelki")
					deps.PlayerDataService.IncrementStat(owner, "Drops", 1)
					deps.EffectsService.BurstAt(collector.Position, { 90, 220, 120 })
				end
			end
			dropPart:Destroy()
		end)
	end
	for _, state in pairs(plots) do
		armCollector(state.Collector)
	end

	-- dozowniki: pętla serwera, kropelki padają w kolektor
	for _, state in pairs(plots) do
		task.spawn(function()
			while true do
				task.wait(state.Interval)
				if state.Owner then
					for _ = 1, state.Droppers do
						local drop = Instance.new("Part")
						drop.Name = "Drop"
						drop.Size = Vector3.new(1.2, 1.2, 1.2)
						drop.Color = Color3.fromRGB(255, 220, 100)
						drop.Material = Enum.Material.Neon
						drop.Position = state.Dropper.Position
						drop:SetAttribute("IsDrop", true)
						drop.Parent = state.Parts
						game:GetService("Debris"):AddItem(drop, 8)
					end
				end
			end
		end)
	end

	-- zwolnienie działek po wyjściu
	Players.PlayerRemoving:Connect(function(player)
		for _, state in pairs(plots) do
			if state.Owner == player.UserId then
				state.Owner = nil
				state.Claim.Color = Color3.fromRGB(34, 211, 238)
			end
		end
	end)

	print("[Forge] Tycoon: 4 działki, dozowniki i przyciski działają.")
end

function Pack.Hud(deps, player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return nil
	end
	local mine = nil
	for _, state in pairs(plots) do
		if state.Owner == player.UserId then
			mine = state
		end
	end
	return {
		Title = deps.GameSpec.Name,
		Rows = {
			{ Label = "Kropelki razem", Value = deps.Util.Format.Short(data.Stats.Drops or 0) },
			{ Label = "Moja działka", Value = mine and ("#" .. mine.Id) or "brak — wejdź na wolną" },
			{ Label = "Interwał kropel", Value = mine and (string.format("%.1f", mine.Interval) .. " s") or "—" },
			{ Label = "Wartość kropelki", Value = mine and (mine.Value .. " 🪙") or "—" },
			{ Label = "Dozowniki", Value = mine and tostring(mine.Droppers) or "—" },
		},
	}
end

function Pack.Shop(deps, data)
	if not data then
		return {}
	end
	return {
		{ Id = "super_drops", Name = "Superszybkie kropelki", Description = "Wszystkie dozowniki ×2 szybsze (na zawsze)", Kind = "special", Price = 3000, Owned = (data.Stats.SuperDrops or 0) > 0 },
		{ Id = "double_value", Name = "Podwójna wartość", Description = "Każda kropelka daje ×2 monety (na zawsze)", Kind = "special", Price = 5000, Owned = (data.Stats.DoubleValue or 0) > 0 },
	}
end

function Pack.OnBuy(deps, player, item)
	local state = nil
	for _, candidate in pairs(plots) do
		if candidate.Owner == player.UserId then
			state = candidate
		end
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane się wczytują."
	end
	if item.Id == "super_drops" then
		data.Stats.SuperDrops = 1
		for _, candidate in pairs(plots) do
			if candidate.Owner == player.UserId then
				candidate.Interval = math.max(0.2, candidate.Interval / 2)
			end
		end
		return true, "Dozowniki przyspieszone!"
	end
	if item.Id == "double_value" then
		data.Stats.DoubleValue = 1
		return true, "Kropelki warte dwa razy więcej!"
	end
	return true, ""
end

Pack.QuestPool = {
	{ id = "t_drops_100", type = "stat:Drops", target = 100, reward = 400, desc = "Zbierz 100 kropel" },
	{ id = "t_drops_500", type = "stat:Drops", target = 500, reward = 1200, desc = "Zbierz 500 kropel" },
	{ id = "t_coins_400", type = "coins", target = 400, reward = 500, desc = "Zbierz 400 monet" },
	{ id = "t_coins_1500", type = "coins", target = 1500, reward = 1600, desc = "Zbierz 1 500 monet" },
	{ id = "t_time_25", type = "playtime", target = 25, reward = 300, desc = "Prowadź fabrykę 25 minut" },
}

function Pack.LeaderValue(deps, player)
	local data = deps.PlayerDataService.Get(player)
	return data and (data.Stats.Drops or 0) or 0
end

return Pack
