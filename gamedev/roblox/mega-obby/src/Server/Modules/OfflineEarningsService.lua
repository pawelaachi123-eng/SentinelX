--!strict
-- ============================================================================
-- MEGA OBBY — OfflineEarningsService (Server/Modules/OfflineEarningsService)
-- „Podczas twojej nieobecności” — klasyczny mechanizm retencji i przychodu:
--  • stawka godzinowa z gatunku (Pack.OfflineRatePerHour) albo bazowa,
--  • uczciwy limit MaxHours (nie liczymy tygodni nieobecności),
--  • wypłata automatyczna przy wejściu + komunikat z kwotą,
--  • produkt „podwój zarobek offline” — raz na dobę podwaja (idempotentnie).
-- Data.UpdatedAt (UpdateAsync) jest punktem odniesienia czasu.
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local OfflineEarningsService = {}

local deps = nil
local config = nil

local function rateOf(player): number
	local pack = deps.Pack
	if pack and pack.OfflineRatePerHour then
		local ok, rate = pcall(pack.OfflineRatePerHour, deps, player)
		if ok and type(rate) == "number" and rate > 0 then
			return rate
		end
	end
	return config.BasePerHour
end

local function grantOffline(player, data)
	local updatedAt = data.UpdatedAt
	if not updatedAt or updatedAt <= 0 then
		return -- pierwsza gra: nie ma z czego liczyć
	end
	local awaySeconds = math.clamp(os.time() - updatedAt, 0, config.MaxHours * 3600)
	if awaySeconds < 300 then
		return -- mniej niż 5 minut: bez fanfar
	end
	local hours = awaySeconds / 3600
	local amount = math.floor(rateOf(player) * hours)
	if amount <= 0 then
		return
	end
	-- kwota do ewentualnego podwojenia trzymana do końca doby
	data.Stats.OfflinePending = amount
	deps.PlayerDataService.AddCoins(player, amount, "zarobek offline (" .. math.floor(hours * 10) / 10 .. " h)")
	deps.Net:SendTo(player, "Notify", "success",
		"💤 Podczas nieobecności zarobiłeś " .. deps.Util.Format.Number(amount)
		.. " monet (limit " .. config.MaxHours .. " h). Możliwe podwojenie w oknie SPIN/OFERTY.")
end

local function handleDouble(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return false, "Dane jeszcze się ładują."
	end
	local amount = data.Stats.OfflinePending or 0
	if amount <= 0 then
		return false, "Nie ma czego podwajać (albo już to zrobiłeś dziś)."
	end
	local today = deps.Util.UtcDayNumber()
	if data.Stats.OfflineDoubleDay == today then
		return false, "Podwojenie offline dziś już użyte — wróć jutro."
	end
	data.Stats.OfflineDoubleDay = today
	data.Stats.OfflinePending = 0
	deps.PlayerDataService.AddCoins(player, amount, "podwojenie offline")
	return true, "x2: +" .. deps.Util.Format.Number(amount) .. " monet!"
end

OfflineEarningsService.ProductGrants = function(_deps)
	return { offline_double = function(player) return handleDouble(player) end }
end

function OfflineEarningsService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.PlayerDataService.OnLoaded:Connect(function(player, data)
		task.delay(2, function()
			if player.Parent then
				grantOffline(player, data)
			end
		end)
	end)

	print("[MegaObby] OfflineEarningsService gotowy — limit " .. config.MaxHours .. " h, baza "
		.. config.BasePerHour .. "/h (gatunek może nadpisać).")
end

return OfflineEarningsService
