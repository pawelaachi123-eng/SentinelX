--!strict
-- ============================================================================
-- MEGA OBBY — PetService (Server/Modules/PetService)
-- Jajka (losowanie ważone), ekwipunek zwierzaków, wyposażanie (limit 1 / 2 z
-- VIP), modele zwierzaków budowane z części + płynne podążanie za graczem
-- (lerp pozycji na serwerze), bonusy: mnożnik monet i promień magnesu.
-- Powtórzony zwierzak → automatycznie zamieniany na monety (uczciwie, bez
-- „utylizacji” w ciszy — gracz dostaje komunikat).
-- ============================================================================

local HttpService = game:GetService("HttpService")
local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")
local RunService = game:GetService("RunService")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local PetService = {}

local deps = nil
local config = nil
local followers = {} -- [userId] = { { Model, Uid, Definition } }

-- ————————————————————————————————————————————————
-- Definicje z configu
-- ————————————————————————————————————————————————
local function petDefinitionById(petId)
	for _, egg in ipairs(config.Eggs) do
		for _, pet in ipairs(egg.pets) do
			if pet.id == petId then
				return pet
			end
		end
	end
	return nil
end

local function rarityColor(rarity: string): Color3
	if rarity == "Legendarny" then
		return Color3.fromRGB(255, 170, 40)
	elseif rarity == "Epicki" then
		return Color3.fromRGB(190, 90, 255)
	elseif rarity == "Rzadki" then
		return Color3.fromRGB(80, 170, 255)
	end
	return Color3.fromRGB(160, 160, 160)
end

-- ————————————————————————————————————————————————
-- Model zwierzaka z części (bez wglądów zewnętrznych — działa od razu)
-- ————————————————————————————————————————————————
local function buildPetModel(definition, uid): Model
	local model = Instance.new("Model")
	model.Name = definition.name
	local color = Color3.fromRGB(definition.color[1], definition.color[2], definition.color[3])

	local body = Instance.new("Part")
	body.Name = "Body"
	body.Shape = Enum.PartType.Ball
	body.Size = Vector3.new(1.7, 1.7, 1.7)
	body.Color = color
	body.Material = Enum.Material.SmoothPlastic
	body.TopSurface = Enum.SurfaceType.Smooth
	body.Parent = model

	local head = Instance.new("Part")
	head.Name = "Head"
	head.Shape = Enum.PartType.Ball
	head.Size = Vector3.new(1.1, 1.1, 1.1)
	head.Color = color
	head.Material = Enum.Material.SmoothPlastic
	head.Parent = model

	for side = -1, 1, 2 do
		local ear = Instance.new("Part")
		ear.Name = "Ear" .. side
		ear.Size = Vector3.new(0.3, 0.55, 0.2)
		ear.Color = color:Lerp(Color3.new(0, 0, 0), 0.15)
		ear.Material = Enum.Material.SmoothPlastic
		ear.Parent = model
	end

	local eye = Instance.new("Part")
	eye.Name = "Eyes"
	eye.Size = Vector3.new(0.7, 0.18, 0.1)
	eye.Color = Color3.fromRGB(20, 20, 25)
	eye.Material = Enum.Material.SmoothPlastic
	eye.Parent = model

	local light = Instance.new("PointLight")
	light.Color = color
	light.Range = 6
	light.Brightness = rarityColor(definition.rarity) == rarityColor("Legendarny") and 1.2 or 0.6
	light.Parent = body

	local billboard = Instance.new("BillboardGui")
	billboard.Size = UDim2.fromScale(6, 1.4)
	billboard.StudsOffset = Vector3.new(0, 1.6, 0)
	billboard.MaxDistance = 60
	local label = Instance.new("TextLabel")
	label.Size = UDim2.fromScale(1, 1)
	label.BackgroundTransparency = 1
	label.Font = Enum.Font.GothamBold
	label.TextScaled = true
	label.Text = definition.name .. " · " .. definition.rarity
	label.TextColor3 = rarityColor(definition.rarity)
	label.TextStrokeTransparency = 0.4
	label.Parent = billboard
	billboard.Parent = head

	model.PrimaryPart = body
	model:SetAttribute("Uid", uid)

	-- całość zgrana w jedną bryłę (Unanchor + Weld) — spadek kosztów fizyki
	for _, part in ipairs({ head, model:FindFirstChild("Ear-1"), model:FindFirstChild("Ear1"), eye }) do
		if part and part:IsA("BasePart") then
			local weld = Instance.new("WeldConstraint")
			weld.Part0 = body
			weld.Part1 = part
			weld.Parent = body
		end
	end

	return model
end

-- ————————————————————————————————————————————————
-- Pętla podążania: serwer lerpuje pozycję (stabilne, autorytatywne)
-- ————————————————————————————————————————————————
local function followLoop()
	RunService.Heartbeat:Connect(function(dt)
		local alpha = 1 - 0.0005 ^ dt -- płynne dojście ~z opóźnieniem
		for userId, entry in pairs(followers) do
			local player = Players:GetPlayerByUserId(userId)
			local character = player and player.Character
			local root = character and character:FindFirstChild("HumanoidRootPart")
			if root then
				for index, pet in ipairs(entry) do
					local model = pet.Model
					local body = model and model:FindFirstChild("Body")
					if body then
						-- lekkie ułożenie obok/za graczem, kilka zwierzaków w rzędzie
						local offset = CFrame.new((index - 1) * 2.6 - 1.3, 1.4 + math.sin(os.clock() * 2 + index) * 0.25, 3.2)
						local target = root.CFrame * offset
						local current = body.Position
						local desired = target.Position
						body.Position = current:Lerp(desired, alpha)
						local toPlayer = root.Position - body.Position
						if toPlayer.Magnitude > 0.05 then
							model:PivotTo(CFrame.new(body.Position, body.Position + toPlayer.Unit))
						end
						local head = model:FindFirstChild("Head")
						if head then
							head.Position = body.Position + Vector3.new(0, 0.55, -0.9)
							local earL = model:FindFirstChild("Ear-1")
							local earR = model:FindFirstChild("Ear1")
							if earL then
								earL.Position = head.Position + Vector3.new(-0.35, 0.55, 0)
							end
							if earR then
								earR.Position = head.Position + Vector3.new(0.35, 0.55, 0)
							end
							local eyes = model:FindFirstChild("Eyes")
							if eyes then
								eyes.Position = head.Position + Vector3.new(0, 0, -0.5)
								local forward = root.Position - eyes.Position
								if forward.Magnitude > 0.05 then
									eyes.CFrame = CFrame.lookAt(eyes.Position, eyes.Position + forward.Unit * 10)
								end
							end
						end
					end
				end
			end
		end
	end)
end

-- ————————————————————————————————————————————————
-- API
-- ————————————————————————————————————————————————
function PetService.OwnedOf(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return {}
	end
	local result = {}
	for _, owned in ipairs(data.Pets) do
		local definition = petDefinitionById(owned.Id)
		if definition then
			table.insert(result, {
				Uid = owned.Uid,
				Id = owned.Id,
				Name = definition.name,
				Rarity = definition.rarity,
				Bonus = definition.bonus,
				MagnetRadius = definition.magnetRadius,
			})
		end
	end
	return result
end

function PetService.GetCoinMultiplier(player): number
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return 1
	end
	local multiplier = 1
	for _, uid in ipairs(data.EquippedPets or {}) do
		for _, owned in ipairs(data.Pets) do
			if owned.Uid == uid then
				local definition = petDefinitionById(owned.Id)
				if definition then
					multiplier = multiplier * definition.bonus
				end
			end
		end
	end
	return multiplier
end

function PetService.GetMagnetRadius(player): number
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return 0
	end
	local radius = 0
	for _, uid in ipairs(data.EquippedPets or {}) do
		for _, owned in ipairs(data.Pets) do
			if owned.Uid == uid then
				local definition = petDefinitionById(owned.Id)
				if definition and definition.magnetRadius then
					radius = math.max(radius, definition.magnetRadius)
				end
			end
		end
	end
	return radius
end

-- ————————————————————————————————————————————————
-- Spawn/despawn modeli wg EquippedPets
-- ————————————————————————————————————————————————
local function refreshModels(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return
	end
	local entry = followers[player.UserId]
	if entry then
		for _, pet in ipairs(entry) do
			pet.Model:Destroy()
		end
	end
	followers[player.UserId] = {}
	local folder = deps.WorldBuilder.Result.Folders.Pets
	for _, uid in ipairs(data.EquippedPets or {}) do
		for _, owned in ipairs(data.Pets) do
			if owned.Uid == uid then
				local definition = petDefinitionById(owned.Id)
				if definition then
					local model = buildPetModel(definition, uid)
					local character = player.Character
					local root = character and character:FindFirstChild("HumanoidRootPart")
					model:PivotTo((root and root.CFrame or CFrame.new(0, 5, 0)) * CFrame.new(0, 2, 4))
					model.Parent = folder
					table.insert(followers[player.UserId], { Model = model, Uid = uid, Definition = definition })
				end
			end
		end
	end
end

-- ————————————————————————————————————————————————
-- HANDLERY SIECIOWE (wywoływane przez Net — rate limit w Net.OnServer)
-- ————————————————————————————————————————————————
local function handleBuyEgg(player, eggId)
	if type(eggId) ~= "string" then
		return false, "Podaj jajko."
	end
	local egg = nil
	for _, candidate in ipairs(config.Eggs) do
		if candidate.id == eggId then
			egg = candidate
			break
		end
	end
	if not egg then
		return false, "Nie znam takiego jajka."
	end
	if egg.currency ~= "Coins" then
		return false, "To jajko nie jest za monety."
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if not deps.PlayerDataService.TrySpend(player, egg.price) then
		return false, "Za mało monet — to jajko kosztuje " .. deps.Util.Format.Number(egg.price) .. "."
	end
	-- losowanie
	local picked = deps.Util.WeightedPick(egg.pets, Random.new(os.time() * 1000 + player.UserId))
	local uid = HttpService:GenerateGUID(false)
	-- powtórka?
	local duplicate = false
	for _, owned in ipairs(data.Pets) do
		if owned.Id == picked.id then
			duplicate = true
			break
		end
	end
	if duplicate then
		local refund = config.Coins.DuplicatePetRefund
		deps.PlayerDataService.AddCoins(player, refund, "duplikat zwierzaka")
		deps.Net:SendTo(player, "Notify", "info",
			"Powtórka: " .. picked.name .. "! Zamieniam na " .. refund .. " monet (i tak masz go w ekwipunku).")
		return true, "Powtórka → +" .. refund .. " monet"
	end
	table.insert(data.Pets, { Uid = uid, Id = picked.id, Obtained = os.time() })
	deps.Net:SendTo(player, "Notify", "success", "🥚 WYLOSOWANO: " .. picked.name .. " (" .. picked.rarity .. ")! Bonus monet ×" .. picked.bonus)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Wylosowano " .. picked.name
end

local function handleEquipPet(player, petUid)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if petUid == nil or petUid == "" then
		-- zdejmij wszystkie
		data.EquippedPets = {}
		refreshModels(player)
		deps.PlayerDataService.PushSnapshot(player)
		return true, "Zwierzaki schowane."
	end
	if type(petUid) ~= "string" then
		return false, "Nieprawidłowy identyfikator."
	end
	-- toggle
	for index, uid in ipairs(data.EquippedPets) do
		if uid == petUid then
			table.remove(data.EquippedPets, index)
			refreshModels(player)
			deps.PlayerDataService.PushSnapshot(player)
			return true, "Zwierzak odpoczywa."
		end
	end
	local ownedHere = nil
	for _, owned in ipairs(data.Pets) do
		if owned.Uid == petUid then
			ownedHere = owned
			break
		end
	end
	if not ownedHere then
		return false, "Ten zwierzak nie należy do ciebie."
	end
	local maxPets = deps.PlayerDataService.HasGamepass(player, "vip")
		and config.Player.MaxPetsWithVip
		or config.Player.MaxPetsWithoutVip
	if #data.EquippedPets >= maxPets then
		if not deps.PlayerDataService.HasGamepass(player, "vip") then
			return false, "Limit " .. maxPets .. " zwierzaka (VIP: " .. config.Player.MaxPetsWithVip .. ")."
		end
		return false, "Limit zwierzaków osiągnięty."
	end
	table.insert(data.EquippedPets, petUid)
	refreshModels(player)
	deps.PlayerDataService.PushSnapshot(player)
	local definition = petDefinitionById(ownedHere.Id)
	return true, definition and (definition.name .. " biegnie z tobą!") or "Wyposażono."
end

-- Darmowy rzut jajkiem (używany przez produkt deweloperski „Rzut Premium” —
-- bez płacenia monet; ta sama losowalność i duplikaty jak zwykły zakup).
function PetService.GrantEggRoll(player, eggId): (boolean, string)
	if type(eggId) ~= "string" then
		return false, "Podaj jajko."
	end
	local egg = nil
	for _, candidate in ipairs(config.Eggs) do
		if candidate.id == eggId then
			egg = candidate
			break
		end
	end
	if not egg then
		return false, "Nie znam takiego jajka."
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	local picked = deps.Util.WeightedPick(egg.pets, Random.new(os.time() * 1000 + player.UserId))
	local uid = HttpService:GenerateGUID(false)
	for _, owned in ipairs(data.Pets) do
		if owned.Id == picked.id then
			local refund = config.Coins.DuplicatePetRefund
			deps.PlayerDataService.AddCoins(player, refund, "duplikat (rzut premium)")
			return true, "Powtórka (" .. picked.name .. ") → +" .. refund .. " monet"
		end
	end
	table.insert(data.Pets, { Uid = uid, Id = picked.id, Obtained = os.time() })
	deps.Net:SendTo(player, "Notify", "success", "🥚 RZUT PREMIUM: " .. picked.name .. " (" .. picked.rarity .. ")! Bonus ×" .. picked.bonus)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Premium: " .. picked.name
end

function PetService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("BuyEgg", handleBuyEgg)
	deps.Net:OnServer("EquipPet", handleEquipPet)

	Players.PlayerRemoving:Connect(function(player)
		local entry = followers[player.UserId]
		if entry then
			for _, pet in ipairs(entry) do
				pet.Model:Destroy()
			end
			followers[player.UserId] = nil
		end
	end)

	followLoop()

	print("[MegaObby] PetService gotowy — jajek: " .. #config.Eggs .. ", limit zwierzaków: 1 (2 z VIP).")
end

return PetService
