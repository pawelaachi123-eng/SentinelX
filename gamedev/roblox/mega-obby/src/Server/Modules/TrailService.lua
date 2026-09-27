--!strict
-- ============================================================================
-- MEGA OBBY — TrailService (Server/Modules/TrailService)
-- Sklep ze śladami za monety: kupno (walidacja ceny na serwerze), zakładanie
-- i zdejmowanie. Efekt dokłada EffectsService.ApplyTrail.
-- ============================================================================

local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local TrailService = {}

local deps = nil
local config = nil

local function findTrail(trailId)
	for _, trail in ipairs(config.Trails) do
		if trail.id == trailId then
			return trail
		end
	end
	return nil
end

local function handleBuyTrail(player, trailId)
	if type(trailId) ~= "string" then
		return false, "Podaj ślad."
	end
	local trail = findTrail(trailId)
	if not trail then
		return false, "Nie znam takiego śladu."
	end
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if trail.vipOnly and not deps.PlayerDataService.HasGamepass(player, "vip") then
		return false, "„" .. trail.name .. "” jest tylko dla VIP — sprawdź zakładkę Przepustki."
	end
	for _, owned in ipairs(data.Trails) do
		if owned == trailId then
			return false, "Ten ślad już masz."
		end
	end
	if not deps.PlayerDataService.TrySpend(player, trail.price) then
		return false, "Za mało monet — „" .. trail.name .. "” kosztuje " .. deps.Util.Format.Number(trail.price) .. "."
	end
	table.insert(data.Trails, trailId)
	deps.Net:SendTo(player, "Notify", "success", "Kupiono ślad: " .. trail.name .. "! Zakładam ci go od razu.")
	data.EquippedTrail = trailId
	deps.EffectsService.ApplyTrail(player)
	deps.PlayerDataService.PushSnapshot(player)
	return true, "Kupiono " .. trail.name
end

local function handleEquipTrail(player, trailId)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	if trailId == nil or trailId == "" then
		data.EquippedTrail = nil
		deps.EffectsService.ApplyTrail(player)
		deps.PlayerDataService.PushSnapshot(player)
		return true, "Ślad zdjęty."
	end
	if type(trailId) ~= "string" then
		return false, "Nieprawidłowy ślad."
	end
	local owned = false
	for _, candidate in ipairs(data.Trails) do
		if candidate == trailId then
			owned = true
			break
		end
	end
	if not owned then
		return false, "Najpierw kup ten ślad."
	end
	data.EquippedTrail = trailId
	deps.EffectsService.ApplyTrail(player)
	deps.PlayerDataService.PushSnapshot(player)
	local trail = findTrail(trailId)
	return true, trail and ("Założono: " .. trail.name) or "Założono."
end

function TrailService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.Net:OnServer("BuyTrail", handleBuyTrail)
	deps.Net:OnServer("EquipTrail", handleEquipTrail)

	print("[MegaObby] TrailService gotowy — śladów w sklepie: " .. #config.Trails .. ".")
end

return TrailService
