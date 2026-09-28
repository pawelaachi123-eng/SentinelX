--!strict
-- ============================================================================
-- MEGA OBBY — OnboardingService (Server/Modules/OnboardingService)
-- Pierwsze kroki (FTUE) — 5 kroków z nagrodami: minuta gry → pierwsze monety →
-- pierwsze wydatki (uczy sklepu!) → kolejne monety → 10 minut gry. Krok liczony
-- serwerowo (data.Stats.Tutorial), komunikat przy wejściu i po każdym kroku.
-- To jednocześnie tutorialek i pierwszy łańcuch nagród (retencja D1).
-- ============================================================================

local Players = game:GetService("Players")
local ReplicatedStorage = game:GetService("ReplicatedStorage")

local Shared = ReplicatedStorage:WaitForChild("Shared")

local OnboardingService = {}

local deps = nil
local config = nil

local function stateOf(data)
	data.Stats.Tutorial = data.Stats.Tutorial or { Step = 1, Progress = 0, Done = false }
	return data.Stats.Tutorial
end

local function announce(player, data)
	local state = stateOf(data)
	if state.Done then
		return
	end
	local step = config.Onboarding[state.Step]
	if step then
		deps.Net:SendTo(player, "Notify", "info",
			"🎯 KROK " .. state.Step .. "/" .. #config.Onboarding .. ": " .. step.label
			.. " — nagroda " .. step.reward .. " monet.")
	end
end

local function completeStep(player, data)
	local state = stateOf(data)
	local step = config.Onboarding[state.Step]
	if not step then
		state.Done = true
		return
	end
	deps.PlayerDataService.AddCoins(player, step.reward, "krok " .. state.Step)
	deps.Net:SendTo(player, "Notify", "success",
		"✅ Krok " .. state.Step .. " zaliczony! +" .. step.reward .. " monet.")
	state.Step += 1
	state.Progress = 0
	if state.Step > #config.Onboarding then
		state.Done = true
		deps.Net:SendTo(player, "Notify", "success",
			"🎓 ONBOARDING UKOŃCZONY! Świat otwarty — questy, spiny i sezon czekają.")
	else
		announce(player, data)
	end
	deps.PlayerDataService.PushSnapshot(player)
end

local function progress(player, kind: string, amount: number)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return
	end
	local state = stateOf(data)
	if state.Done then
		return
	end
	local step = config.Onboarding[state.Step]
	if not (step and step.type == kind) then
		return
	end
	state.Progress += amount
	if state.Progress >= step.amount then
		completeStep(player, data)
	end
end

function OnboardingService.InfoOf(player)
	local data = deps.PlayerDataService.Get(player)
	if not data then
		return { Step = 1, Total = #config.Onboarding, Done = false, Label = "", Progress = 0, Target = 0, Reward = 0 }
	end
	local state = stateOf(data)
	local step = config.Onboarding[state.Step]
	return {
		Step = state.Step,
		Total = #config.Onboarding,
		Done = state.Done == true,
		Label = step and step.label or "",
		Progress = state.Progress or 0,
		Target = step and step.amount or 0,
		Reward = step and step.reward or 0,
	}
end

function OnboardingService.Start(depsIn)
	deps = depsIn
	config = deps.Config

	deps.PlayerDataService.OnLoaded:Connect(function(player, data)
		task.delay(4, function()
			if player.Parent and not stateOf(data).Done then
				announce(player, data)
			end
		end)
	end)

	deps.EventBus.PlaytimeTick:Connect(function(player, deltaSeconds)
		progress(player, "playtime", deltaSeconds)
	end)
	deps.EventBus.CoinsAdded:Connect(function(player, amount)
		progress(player, "coins", amount)
	end)
	deps.EventBus.CoinsSpent:Connect(function(player, amount)
		progress(player, "spent", amount)
	end)

	print("[MegaObby] OnboardingService gotowy — kroków: " .. #config.Onboarding .. ".")
end

return OnboardingService
