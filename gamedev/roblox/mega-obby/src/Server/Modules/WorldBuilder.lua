--!strict
-- ============================================================================
-- MEGA OBBY — WorldBuilder (Server/Modules/WorldBuilder)
-- SERWER BUDUJE CAŁY ŚWIAT Z KODU: lobby + 120 etapów na 6 wyspach + finisz.
-- Nie musisz nic stawiać w Studio — wklej skrypty i wciśnij Play.
-- Geometria jest DETERMINISTYCZNA (seed = numer etapu): każdy gracz widzi
-- dokładnie ten sam tor, a reguły trudności idą z GameConfig.Islands.
--
-- Tagi (CollectionService) — cała logika czyta tagi, nie nazwy:
--   Checkpoint (SpawnLocation, attribute StageNumber, IslandId)
--   KillBrick · MovingPlatform (MoveOffset:Vector3, MoveSeconds:number,
--   BaseCFrame:CFrame) · Spinner (SpinSpeedDeg:number, BaseCFrame:CFrame)
--   Spring (Power:number) · FinishPad · Leaderboard (BoardId:string)
-- ============================================================================

local CollectionService = game:GetService("CollectionService")
local RunService = game:GetService("RunService")
local Workspace = game:GetService("Workspace")

local WorldBuilder = {}

WorldBuilder.Result = nil

local ROOT_NAME = "MegaObbyWorld"

-- ————————————————————————————————————————————————
-- Pomocnicze: tworzenie części z domyślnymi, sensownymi wartościami
-- ————————————————————————————————————————————————
local function newPart(className: string, props: { [string]: any }, parent: Instance?): Instance
	local inst = Instance.new(className)
	inst.Anchored = true
	inst.TopSurface = Enum.SurfaceType.Smooth
	inst.BottomSurface = Enum.SurfaceType.Smooth
	for key, value in pairs(props) do
		(inst :: any)[key] = value
	end
	inst.Parent = parent
	return inst
end

local function colorFrom(parts: { number }): Color3
	return Color3.fromRGB(parts[1], parts[2], parts[3])
end

local function addBillboard(parent: Instance, text: string, studs: number, textColor: Color3?)
	local billboard = Instance.new("BillboardGui")
	billboard.Size = UDim2.fromScale(8, 2.2)
	billboard.StudsOffset = Vector3.new(0, studs, 0)
	billboard.AlwaysOnTop = false
	billboard.MaxDistance = 160
	local label = Instance.new("TextLabel")
	label.Size = UDim2.fromScale(1, 1)
	label.BackgroundTransparency = 1
	label.Text = text
	label.TextScaled = true
	label.Font = Enum.Font.GothamBold
	label.TextColor3 = textColor or Color3.fromRGB(255, 255, 255)
	label.TextStrokeTransparency = 0.4
	label.Parent = billboard
	billboard.Parent = parent
	return billboard
end

-- ————————————————————————————————————————————————
-- Lobby: plac startowy, portal, tablice wyników, szyld z instrukcją
-- ————————————————————————————————————————————————
local function buildLobby(folders): CFrame
	local lobby = folders.Lobby
	local floor = newPart("Part", {
		Name = "LobbyFloor",
		Size = Vector3.new(64, 2, 64),
		Position = Vector3.new(0, 0, 0),
		Color = Color3.fromRGB(60, 66, 82),
		Material = Enum.Material.SmoothPlastic,
	}, lobby)
	local trim = newPart("Part", {
		Name = "LobbyTrim",
		Size = Vector3.new(66, 0.6, 66),
		Position = Vector3.new(0, -0.8, 0),
		Color = Color3.fromRGB(34, 211, 238),
		Material = Enum.Material.Neon,
	}, lobby)

	-- Spawn gracza (każdy pierwszy join → lobby)
	newPart("SpawnLocation", {
		Name = "LobbySpawn",
		Size = Vector3.new(10, 1, 10),
		Position = Vector3.new(0, 1.5, 12),
		Color = Color3.fromRGB(34, 211, 238),
		Material = Enum.Material.Neon,
		Duration = 0,
		Neutral = true,
	}, lobby)
	CollectionService:AddTag(trim.Parent and floor or floor, "Lobby")

	-- Portal: dwa filary + belka; dotknięcie → teleport na aktualny etap
	-- (logika teleportu w StageService — tutaj tylko geometria + tag).
	newPart("Part", {
		Name = "PortalLeft",
		Size = Vector3.new(1.2, 10, 1.2),
		Position = Vector3.new(-4, 7, -24),
		Color = Color3.fromRGB(34, 211, 238),
		Material = Enum.Material.Neon,
	}, lobby)
	newPart("Part", {
		Name = "PortalRight",
		Size = Vector3.new(1.2, 10, 1.2),
		Position = Vector3.new(4, 7, -24),
		Color = Color3.fromRGB(34, 211, 238),
		Material = Enum.Material.Neon,
	}, lobby)
	local portalBeam = newPart("Part", {
		Name = "PortalBeam",
		Size = Vector3.new(9.2, 1.2, 1.2),
		Position = Vector3.new(0, 12.6, -24),
		Color = Color3.fromRGB(34, 211, 238),
		Material = Enum.Material.Neon,
	}, lobby)
	local portalField = newPart("Part", {
		Name = "PortalField",
		Size = Vector3.new(7.6, 11.4, 0.4),
		Position = Vector3.new(0, 6.7, -24),
		Color = Color3.fromRGB(120, 240, 230),
		Material = Enum.Material.ForceField,
		Transparency = 0.35,
		CanCollide = false,
	}, lobby)
	CollectionService:AddTag(portalField, "Portal")

	-- Szyld z instrukcją
	local sign = newPart("Part", {
		Name = "Sign",
		Size = Vector3.new(18, 7, 1),
		Position = Vector3.new(0, 9, -31.5),
		Color = Color3.fromRGB(24, 28, 38),
		Material = Enum.Material.SmoothPlastic,
	}, lobby)
	local gui = Instance.new("SurfaceGui")
	gui.Face = Enum.NormalId.Back
	gui.CanvasSize = Vector2.new(900, 350)
	gui.Parent = sign
	local text = Instance.new("TextLabel")
	text.Size = UDim2.fromScale(1, 1)
	text.BackgroundTransparency = 1
	text.Font = Enum.Font.GothamBold
	text.TextScaled = true
	text.TextColor3 = Color3.fromRGB(240, 244, 255)
	text.Text = "MEGA OBBY: WYSPLY PRZYGÓD\n120 etapów · 6 wysp · wejdź w portal →"
	text.Parent = gui

	-- Stojaki pod tablice wyników (SurfaceGui dokłada LeaderboardService)
	for _, info in ipairs({ { "stages", -18 }, { "coins", 18 } }) do
		local stand = newPart("Part", {
			Name = "Board_" .. info[1],
			Size = Vector3.new(9, 7, 1),
			Position = Vector3.new(info[2], 5.5, -20),
			Color = Color3.fromRGB(24, 28, 38),
			Material = Enum.Material.SmoothPlastic,
			Anchored = true,
		}, lobby)
		CollectionService:AddTag(stand, "Leaderboard")
		stand:SetAttribute("BoardId", info[1])
	end

	-- Dekoracja: kilka drzewek (pień + korona)
	local rng = Random.new(1337)
	for i = 1, 6 do
		local angle = rng:NextNumber(0, math.pi * 2)
		local radius = rng:NextNumber(22, 28)
		local base = Vector3.new(math.cos(angle) * radius, 0, math.sin(angle) * radius)
		newPart("Part", {
			Name = "TreeTrunk" .. i,
			Size = Vector3.new(1.4, 5, 1.4),
			Position = base + Vector3.new(0, 4.5, 0),
			Color = Color3.fromRGB(110, 74, 48),
			Material = Enum.Material.Wood,
		}, lobby)
		newPart("Ball", {
			Name = "TreeLeaves" .. i,
			Size = Vector3.new(5.5, 5.5, 5.5),
			Position = base + Vector3.new(0, 8.6, 0),
			Color = Color3.fromRGB(70, 160, 80),
			Material = Enum.Material.Grass,
		}, lobby)
	end

	return floor.CFrame
end

-- ————————————————————————————————————————————————
-- Jeden etap: pad startowy (checkpoint) + ciąg platform + pułapki + monety.
-- Zwraca CFrame wyjściowy (= miejsce pada następnego etapu).
-- ————————————————————————————————————————————————
local function buildStage(stage: number, entryCFrame: CFrame, folders): (CFrame, { Vector3 })
	local config = WorldBuilder._config
	local island = config.IslandOf(stage)
	local pool = config.StageNames[island.id]
	local stageName = pool[((stage - island.from) % #pool) + 1]
	local rng = Random.new(stage * 7919)
	local progress = (stage - island.from) / math.max(1, (island.to - island.from))
	local difficulty = island.difficulty * (0.85 + 0.5 * progress)
	local islandColor = colorFrom(island.color)

	local coinSpots = {}

	-- ——— PAD STARTOWY (checkpoint) ———
	local pad = Instance.new("SpawnLocation")
	pad.Name = "StagePad_" .. stage
	pad.Size = Vector3.new(10, 1, 10)
	pad.Anchored = true
	pad.TopSurface = Enum.SurfaceType.Smooth
	pad.BottomSurface = Enum.SurfaceType.Smooth
	pad.Neutral = true
	pad.Duration = 0
	pad.CFrame = entryCFrame
	pad.Color = islandColor
	pad.Material = Enum.Material.SmoothPlastic
	pad.Parent = folders.Stages
	CollectionService:AddTag(pad, "Checkpoint")
	pad:SetAttribute("StageNumber", stage)
	pad:SetAttribute("IslandId", island.id)
	pad:SetAttribute("StageName", stageName)
	addBillboard(pad, "ETAP " .. stage .. "\n" .. stageName, 4.5, islandColor)

	-- flaga na padzie
	newPart("Part", {
		Name = "FlagPole",
		Size = Vector3.new(0.3, 7, 0.3),
		Position = entryCFrame.Position + Vector3.new(-3.6, 4, -3.6),
		Color = Color3.fromRGB(200, 205, 215),
		Material = Enum.Material.Metal,
	}, folders.Stages)
	newPart("Part", {
		Name = "Flag",
		Size = Vector3.new(2.2, 1.4, 0.15),
		Position = entryCFrame.Position + Vector3.new(-2.4, 6.8, -3.6),
		Color = islandColor,
		Material = Enum.Material.Neon,
	}, folders.Stages)

	-- ——— CIĄG PLATFORM ———
	local cursor = entryCFrame
	local heading = 0 -- odchylenie od +Z w radianach (łagodny meander toru)
	local platformCount = 5 + math.floor(rng:NextNumber(0, 4.99)) -- 5..9
	local baseGap = math.min(7 + difficulty * 2.2, 11.5) -- zawsze w zasięgu skoku
	local baseWidth = math.clamp(9 - difficulty * 2.0, 4, 10)

	for i = 1, platformCount do
		heading = math.clamp(heading + rng:NextNumber(-0.3, 0.3), -0.7, 0.7)
		local gap = baseGap + rng:NextNumber(-1.5, 1.5)
		local rise = rng:NextNumber(-2.0, 1.5 + difficulty)
		local width = math.clamp(baseWidth + rng:NextNumber(-1.5, 1.5), 4, 11)
		local depth = math.clamp(width * rng:NextNumber(0.8, 1.2), 4, 12)

		local direction = Vector3.new(math.sin(heading), 0, math.cos(heading))
		local center = cursor.Position + direction * (gap + width / 2) + Vector3.new(0, rise, 0)
		local rotation = CFrame.Angles(0, heading, 0)

		-- rzadko: zamiast zwykłej platformy — RUCHOMA
		local isMoving = difficulty > 1.15 and rng:NextNumber() < 0.22
		if isMoving then
			local platform = newPart("Part", {
				Name = "MovingPlatform",
				Size = Vector3.new(width, 1, depth),
				CFrame = CFrame.new(center) * rotation,
				Color = Color3.fromRGB(255, 200, 90),
				Material = Enum.Material.SmoothPlastic,
			}, folders.Stages)
			local lateral = Vector3.new(math.cos(heading), 0, -math.sin(heading))
			local moveOffset = lateral * rng:NextNumber(5, 9) * (rng:NextNumber() < 0.5 and 1 or -1)
			platform:SetAttribute("MoveOffset", moveOffset)
			platform:SetAttribute("MoveSeconds", rng:NextNumber(2.2, 3.6))
			platform:SetAttribute("BaseCFrame", platform.CFrame)
			CollectionService:AddTag(platform, "MovingPlatform")
		else
			newPart("Part", {
				Name = "Platform",
				Size = Vector3.new(width, 1, depth),
				CFrame = CFrame.new(center) * rotation,
				Color = islandColor:Lerp(Color3.fromRGB(255, 255, 255), rng:NextNumber(0, 0.35)),
				Material = Enum.Material.SmoothPlastic,
			}, folders.Stages)
		end

		-- pułapki — tylko przy wystarczającej trudności
		if rng:NextNumber() < 0.10 + 0.06 * difficulty then
			-- killbrick lewitujący nad platformą
			newPart("Part", {
				Name = "KillBrick",
				Size = Vector3.new(2.2, 1.2, 2.2),
				CFrame = CFrame.new(center + Vector3.new(rng:NextNumber(-1.5, 1.5), 3.4, rng:NextNumber(-1, 1))),
				Color = Color3.fromRGB(255, 60, 60),
				Material = Enum.Material.Neon,
				CanCollide = true,
			}, folders.Stages)
		end

		if difficulty > 1.3 and rng:NextNumber() < 0.18 then
			-- obracająca się belka nad platformą
			local bar = newPart("Part", {
				Name = "SpinnerBar",
				Size = Vector3.new(width + 4, 0.8, 0.9),
				CFrame = CFrame.new(center + Vector3.new(0, 2.6, 0)) * rotation,
				Color = Color3.fromRGB(255, 120, 40),
				Material = Enum.Material.Neon,
			}, folders.Stages)
			bar:SetAttribute("SpinSpeedDeg", rng:NextNumber(70, 150) * (rng:NextNumber() < 0.5 and -1 or 1))
			bar:SetAttribute("BaseCFrame", bar.CFrame)
			CollectionService:AddTag(bar, "Spinner")
		end

		if rng:NextNumber() < 0.08 then
			-- sprężyna: wybija w górę (Power z configu trudności)
			local spring = newPart("Part", {
				Name = "Spring",
				Size = Vector3.new(2.4, 0.7, 2.4),
				CFrame = CFrame.new(center + Vector3.new(0, 0.85, 0)),
				Color = Color3.fromRGB(120, 255, 120),
				Material = Enum.Material.Neon,
				Shape = Enum.PartType.Cylinder,
			}, folders.Stages)
			spring.CFrame = spring.CFrame * CFrame.Angles(0, 0, math.rad(90))
			spring:SetAttribute("Power", 70 + difficulty * 15)
			CollectionService:AddTag(spring, "Spring")
		end

		-- monety nad platformą (3-5 na etap)
		if rng:NextNumber() < 0.8 then
			table.insert(coinSpots, center + Vector3.new(0, 2.6, 0))
		end

		cursor = CFrame.new(center) * rotation
		cursor = cursor + cursor.LookVector * (width / 2)
	end

	-- wyjście: miejsce pada następnego etapu (pad zbuduje kolejna iteracja)
	local exitGap = math.min(8 + difficulty * 1.5, 12)
	local exitCenter = cursor.Position + cursor.LookVector * (exitGap + 5)
	local exitCFrame = CFrame.new(exitCenter) * CFrame.Angles(0, heading, 0)

	-- monety nad przejściem między platformami
	if rng:NextNumber() < 0.5 then
		table.insert(coinSpots, (cursor.Position + cursor.LookVector * (exitGap / 2)) + Vector3.new(0, 3.2, 0))
	end

	return exitCFrame, coinSpots
end

-- ————————————————————————————————————————————————
-- Logika tagów: killbricki, platformy ruchome (z przenoszeniem gracza!),
-- wiry, sprężyny. Jedna pętla Heartbeat dla wszystkiego — tanio i stabilnie.
-- ————————————————————————————————————————————————
local function wireTagBehaviors(folders)
	-- KILLBRICK: dotknięcie = śmierć (respawn na checkpoint Roblox zrobi sam
	-- dzięki player.RespawnLocation ustawianemu przez StageService).
	local function armKillbrick(brick)
		brick.Touched:Connect(function(hit)
			local character = hit.Parent
			local humanoid = character and character:FindFirstChildOfClass("Humanoid")
			if humanoid and humanoid.Health > 0 then
				humanoid.Health = 0
			end
		end)
	end
	for _, brick in ipairs(CollectionService:GetTagged("KillBrick")) do
		armKillbrick(brick)
	end
	CollectionService:GetInstanceAddedSignal("KillBrick"):Connect(armKillbrick)

	-- SPRĘŻYNA: wybija w górę zachowując ruch poziomy
	local function armSpring(spring)
		spring.Touched:Connect(function(hit)
			local character = hit.Parent
			local humanoid = character and character:FindFirstChildOfClass("Humanoid")
			local root = character and character:FindFirstChild("HumanoidRootPart")
			if humanoid and root and humanoid.Health > 0 then
				local power = spring:GetAttribute("Power") or 80
				local velocity = root.AssemblyLinearVelocity
				root.AssemblyLinearVelocity = Vector3.new(velocity.X, power, velocity.Z)
			end
		end)
	end
	for _, spring in ipairs(CollectionService:GetTagged("Spring")) do
		armSpring(spring)
	end
	CollectionService:GetInstanceAddedSignal("Spring"):Connect(armSpring)

	-- PĘTLA RUCHU: platformy przesuwają się po sinusie, wiry się kręcą,
	-- a stojący na platformie gracz JEDZIE z nią (raycast pod stopy).
	local players = game:GetService("Players")
	local lastPositions = {} -- [part] = Vector3
	local carryParams = RaycastParams.new()
	carryParams.FilterType = Enum.RaycastFilterType.Include
	carryParams.FilterDescendantsInstances = { folders.Stages }

	RunService.Heartbeat:Connect(function(dt)
		-- platformy ruchome
		for _, platform in ipairs(CollectionService:GetTagged("MovingPlatform")) do
			local base = platform:GetAttribute("BaseCFrame")
			local offset = platform:GetAttribute("MoveOffset")
			local seconds = platform:GetAttribute("MoveSeconds")
			if typeof(base) == "CFrame" and typeof(offset) == "Vector3" and type(seconds) == "number" then
				local t = Workspace:GetServerTimeNow()
				local phase = 0.5 - 0.5 * math.cos(t * math.pi * 2 / seconds)
				local previous = lastPositions[platform] or base.Position
				local now = (base + offset * phase).Position
				platform.CFrame = CFrame.new(now) * (base - base.Position)
				lastPositions[platform] = now
				-- przenoszenie graczy
				local delta = now - previous
				if delta.Magnitude > 0.001 then
					for _, player in ipairs(players:GetPlayers()) do
						local character = player.Character
						local root = character and character:FindFirstChild("HumanoidRootPart")
						if root then
							local origin = root.Position + Vector3.new(0, 1, 0)
							local result = Workspace:Raycast(origin, Vector3.new(0, -6, 0), carryParams)
							if result and result.Instance == platform then
								root.CFrame = root.CFrame + delta
							end
						end
					end
				end
			end
		end
		-- wiry
		for _, bar in ipairs(CollectionService:GetTagged("Spinner")) do
			local base = bar:GetAttribute("BaseCFrame")
			local speed = bar:GetAttribute("SpinSpeedDeg")
			if typeof(base) == "CFrame" and type(speed) == "number" then
				local t = Workspace:GetServerTimeNow()
				bar.CFrame = (base - base.Position) + CFrame.new(base.Position) * CFrame.Angles(0, math.rad(speed) * t, 0)
			end
		end
	end)
end

-- ————————————————————————————————————————————————
-- START
-- ————————————————————————————————————————————————
function WorldBuilder.Start(deps)
	WorldBuilder._config = deps.Config
	local config = deps.Config

	local old = Workspace:FindFirstChild(ROOT_NAME)
	if old then
		old:Destroy()
	end

	local root = Instance.new("Folder")
	root.Name = ROOT_NAME
	root.Parent = Workspace

	local folders = {}
	for _, name in ipairs({ "Lobby", "Stages", "Coins", "Pets", "Effects" }) do
		local folder = Instance.new("Folder")
		folder.Name = name
		folder.Parent = root
		folders[name] = folder
	end
	WorldBuilder._folders = folders

	buildLobby(folders)

	local stageSpawns = {}
	local coinSpots = {}

	-- etap 1 startuje tuż za lobby
	local cursor = CFrame.new(0, 0, 40) * CFrame.Angles(0, 0, 0)
	for stage = 1, config.TotalStages do
		local exitCFrame, spots = buildStage(stage, cursor, folders)
		stageSpawns[stage] = cursor + Vector3.new(0, 4, 0) -- spawn 4 stud nad padem
		for _, spot in ipairs(spots) do
			table.insert(coinSpots, { Position = spot, Stage = stage })
		end
		cursor = exitCFrame
	end

	-- FINISZ (etap 121)
	local finish = newPart("SpawnLocation", {
		Name = "FinishPad",
		Size = Vector3.new(14, 1, 14),
		CFrame = cursor,
		Color = Color3.fromRGB(255, 215, 0),
		Material = Enum.Material.Neon,
		Duration = 0,
		Neutral = true,
	}, folders.Stages)
	CollectionService:AddTag(finish, "FinishPad")
	finish:SetAttribute("StageNumber", config.FinishStage)
	addBillboard(finish, "🏆 FINISZ!\n" .. config.GameName, 5, Color3.fromRGB(255, 215, 0))
	-- zwycięska brama
	newPart("Part", {
		Name = "FinishGate",
		Size = Vector3.new(14, 1.2, 1.2),
		CFrame = cursor * CFrame.new(0, 6, -5),
		Color = Color3.fromRGB(255, 215, 0),
		Material = Enum.Material.Neon,
	}, folders.Stages)

	WorldBuilder.Result = {
		StageSpawns = stageSpawns,
		CoinSpots = coinSpots,
		LobbySpawn = CFrame.new(0, 4, 12),
		LobbyCenter = Vector3.new(0, 2, 0),
		Root = root,
		Folders = folders,
	}

	wireTagBehaviors(folders)

	print("[MegaObby] ŚWIAT GOTOWY: " .. config.TotalStages .. " etapów, "
		.. #coinSpots .. " miejsc na monety, lobby + finisz.")
end

return WorldBuilder
