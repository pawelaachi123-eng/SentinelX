--!strict
-- ============================================================================
-- MEGA OBBY — CoinService (Server/Modules/CoinService)
-- Monety na torze (z WorldBuilder.CoinSpots), zbiórka z dotykiem + debounce,
-- respawn po N sekundach, MAGNES zwierzaków (auto-zbieranie w promieniu),
-- deszcz monet w lobby co 10 minut.
-- ============================================================================

local CollectionService = game:GetService("CollectionService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local CoinService = {}

local deps = nil
local config = nil

local COIN_TAG = "Coin"

local function spawnCoin(position: Vector3, stage: number, parentFolder)
	local coin = Instance.new("Part")
	coin.Name = "Coin"
	coin.Shape = Enum.PartType.Cylinder
	coin.Size = Vector3.new(0.35, 2.2, 2.2)
	coin.CFrame = CFrame.new(position) * CFrame.Angles(0, 0, math.rad(90))
	coin.Color = Color3.fromRGB(255, 200, 40)
	coin.Material = Enum.Material.Neon
	coin.Anchored = true
	coin.CanCollide = false
	coin.CanTouch = true
	coin:SetAttribute("Value", config.Coins.PickupValue)
	coin:SetAttribute("Stage", stage)
	coin.Parent = parentFolder
	CollectionService:AddTag(coin, COIN_TAG)

	coin.Touched:Connect(function(hit)
		if coin:GetAttribute("Cool") then
			return
		end
		local character = hit.Parent
		local humanoid = character and character:FindFirstChildOfClass("Humanoid")
		local player = character and Players:GetPlayerFromCharacter(character)
		if not (humanoid and player and humanoid.Health > 0) then
			return
		end
		coin:SetAttribute("Cool", true)
		local value = coin:GetAttribute("Value") or config.Coins.PickupValue
		deps.PlayerDataService.AddCoins(player, value, "moneta")
		deps.EffectsService.BurstAt(coin.Position, { 255, 200, 40 })
		-- schowaj i przywróć
		coin.Transparency = 1
		coin.CanTouch = false
		task.delay(config.Coins.CoinRespawnSeconds, function()
			if coin.Parent then
				coin.Transparency = 0
				coin.CanTouch = true
				coin:SetAttribute("Cool", nil)
			end
		end)
	end)

	return coin
end

-- Magnes: co 0,4 s zbiera monety w promieniu zwierzaka
local function magnetLoop()
	local accumulator = 0
	game:GetService("RunService").Heartbeat:Connect(function(dt)
		accumulator += dt
		if accumulator < 0.4 then
			return
		end
		accumulator = 0
		local coinsFolder = deps.WorldBuilder.Result.Folders.Coins
		for _, player in ipairs(Players:GetPlayers()) do
			local radius = deps.PetService.GetMagnetRadius(player)
			if radius and radius > 0 then
				local character = player.Character
				local root = character and character:FindFirstChild("HumanoidRootPart")
				if root then
					for _, coin in ipairs(coinsFolder:GetChildren()) do
						if not coin:GetAttribute("Cool") and coin:IsA("BasePart") then
							if (coin.Position - root.Position).Magnitude <= radius then
								-- symuluj dotknięcie: ten sam kod co Touched
								local humanoid = character:FindFirstChildOfClass("Humanoid")
								if humanoid and humanoid.Health > 0 then
									coin:SetAttribute("Cool", true)
									local value = coin:GetAttribute("Value") or config.Coins.PickupValue
									deps.PlayerDataService.AddCoins(player, value, "magnes")
									coin.Transparency = 1
									coin.CanTouch = false
									task.delay(config.Coins.CoinRespawnSeconds, function()
										if coin.Parent then
											coin.Transparency = 0
											coin.CanTouch = true
											coin:SetAttribute("Cool", nil)
										end
									end)
								end
							end
						end
					end
				end
			end
		end
	end)
end

-- Deszcz monet w lobby
local function spawnRain()
	local world = deps.WorldBuilder.Result
	local center = world.LobbyCenter + Vector3.new(0, 3, 0)
	local ring = Random.new(os.time())
	for i = 1, config.Coins.RainCount do
		local angle = (i / config.Coins.RainCount) * math.pi * 2
		local radius = 12 + ring:NextNumber(0, 6)
		local coin = spawnCoin(center + Vector3.new(math.cos(angle) * radius, ring:NextNumber(0.5, 4), math.sin(angle) * radius), 0, world.Folders.Effects)
		task.delay(config.Coins.RainLifetimeSeconds, function()
			if coin.Parent then
				coin:Destroy()
			end
		end)
	end
	deps.Net:SendAll("Notify", "info", "🌧️ DESZCZ MONET w lobby! Biegnij — znika za " .. config.Coins.RainLifetimeSeconds .. " s.")
end

CoinService.TriggerRain = spawnRain -- używa też admin (!rain)

function CoinService.Start(depsIn)
	deps = depsIn
	config = deps.Config
	local world = deps.WorldBuilder.Result

	for _, spot in ipairs(world.CoinSpots) do
		spawnCoin(spot.Position, spot.Stage, world.Folders.Coins)
	end

	magnetLoop()
	task.spawn(function()
		while true do
			task.wait(config.Coins.RainIntervalSeconds)
			spawnRain()
		end
	end)

	print("[MegaObby] CoinService gotowy — monet na torze: " .. #world.CoinSpots .. ".")
end

return CoinService
