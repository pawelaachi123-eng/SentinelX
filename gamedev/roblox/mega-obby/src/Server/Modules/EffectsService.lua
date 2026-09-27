--!strict
-- ============================================================================
-- MEGA OBBY — EffectsService (Server/Modules/EffectsService)
-- Ślady (Trail) za postacią, fajerwerkowe konfetti, błysk checkpointa.
-- Wszystko serwerowo (widoczne dla wszystkich), sprzątane przez Debris.
-- ============================================================================

local Debris = game:GetService("Debris")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local EffectsService = {}

local deps = nil
local config = nil

local TRAIL_NAME = "MegaTrail"

local function colorSequenceFrom(rgbList): ColorSequence
	local keypoints = {}
	local count = #rgbList
	for index, rgb in ipairs(rgbList) do
		local alpha = count == 1 and 0 or (index - 1) / (count - 1)
		table.insert(keypoints, ColorSequenceKeypoint.new(alpha, Color3.fromRGB(rgb[1], rgb[2], rgb[3])))
	end
	return ColorSequence.new(keypoints)
end

-- Podpina ślad do postaci wg EquippedTrail w danych
function EffectsService.ApplyTrail(player)
	local character = player.Character
	if not character then
		return
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return
	end
	local old = character:FindFirstChild(TRAIL_NAME)
	if old then
		old:Destroy()
	end
	local trailId = data.EquippedTrail
	if not trailId then
		return
	end
	local definition = nil
	for _, candidate in ipairs(config.Trails) do
		if candidate.id == trailId then
			definition = candidate
			break
		end
	end
	if not definition then
		return
	end
	local root = character:FindFirstChild("HumanoidRootPart")
	local head = character:FindFirstChild("Head")
	if not root or not head then
		return
	end
	local top = Instance.new("Attachment")
	top.Name = "TrailTop"
	top.Position = Vector3.new(0, 1, 0)
	top.Parent = root
	local bottom = Instance.new("Attachment")
	bottom.Name = "TrailBottom"
	bottom.Position = Vector3.new(0, -1, 0)
	bottom.Parent = root
	local trail = Instance.new("Trail")
	trail.Name = TRAIL_NAME
	trail.Attachment0 = top
	trail.Attachment1 = bottom
	trail.Color = colorSequenceFrom(definition.colors)
	trail.Transparency = NumberSequence.new({
		NumberSequenceKeypoint.new(0, 0.2),
		NumberSequenceKeypoint.new(1, 1),
	})
	trail.Lifetime = 0.55
	trail.MinLength = 0.05
	trail.FaceCamera = true
	trail.Parent = character
end

-- Jednorazowy „pyk” checkpointa (particle + światło)
function EffectsService.BurstAt(position: Vector3, rgbList: { number })
	local holder = Instance.new("Part")
	holder.Name = "FXBurst"
	holder.Size = Vector3.new(0.4, 0.4, 0.4)
	holder.Position = position
	holder.Anchored = true
	holder.CanCollide = false
	holder.CanQuery = false
	holder.Transparency = 1
	holder.Parent = deps.WorldBuilder.Result.Folders.Effects

	local emitter = Instance.new("ParticleEmitter")
	emitter.Color = colorSequenceFrom(rgbList)
	emitter.LightEmission = 1
	emitter.Lifetime = NumberRange.new(0.4, 0.9)
	emitter.Speed = NumberRange.new(8, 14)
	emitter.SpreadAngle = Vector2.new(180, 180)
	emitter.Rate = 0
	emitter.Size = NumberSequence.new({
		NumberSequenceKeypoint.new(0, 0.9),
		NumberSequenceKeypoint.new(1, 0),
	})
	emitter.Parent = holder
	emitter:Emit(40)

	local light = Instance.new("PointLight")
	light.Color = Color3.fromRGB(rgbList[1], rgbList[2], rgbList[3])
	light.Range = 14
	light.Brightness = 2
	light.Parent = holder

	Debris:AddItem(holder, 2.5)
end

-- Konfetti na finiszu: fizyczne karteczki, spadają i znikają
function EffectsService.ConfettiAt(position: Vector3)
	local folder = deps.WorldBuilder.Result.Folders.Effects
	local palette = {
		Color3.fromRGB(255, 80, 80), Color3.fromRGB(255, 220, 60),
		Color3.fromRGB(80, 255, 120), Color3.fromRGB(80, 160, 255),
		Color3.fromRGB(200, 80, 255), Color3.fromRGB(255, 255, 255),
	}
	local rng = Random.new(os.time() + math.random(0, 1e9))
	for i = 1, 60 do
		local piece = Instance.new("Part")
		piece.Name = "Confetti"
		piece.Size = Vector3.new(0.35, 0.05, 0.35)
		piece.Color = palette[rng:NextInteger(1, #palette)]
		piece.Material = Enum.Material.SmoothPlastic
		piece.CFrame = CFrame.new(position + Vector3.new(rng:NextNumber(-3, 3), rng:NextNumber(0, 3), rng:NextNumber(-3, 3)))
		piece.AssemblyLinearVelocity = Vector3.new(rng:NextNumber(-14, 14), rng:NextNumber(18, 30), rng:NextNumber(-14, 14))
		piece.AssemblyAngularVelocity = Vector3.new(rng:NextNumber(-12, 12), rng:NextNumber(-12, 12), rng:NextNumber(-12, 12))
		piece.Parent = folder
		Debris:AddItem(piece, 5)
	end
end

function EffectsService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	Players.PlayerAdded:Connect(function(player)
		player.CharacterAdded:Connect(function(character)
			character:WaitForChild("HumanoidRootPart", 10)
			task.wait(0.1)
			EffectsService.ApplyTrail(player)
		end)
	end)

	print("[MegaObby] EffectsService gotowy.")
end

return EffectsService
