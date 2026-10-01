using SentinelX.Models.Automations;

namespace SentinelX.Services.Automations;

/// <summary>
/// Szablony automatyzacji dostępne w UI „jeden klik".
/// </summary>
public static class AutomationTemplates
{
    public static IEnumerable<AutomationRule> All()
    {
        yield return new AutomationRule
        {
            Name = "Tryb gry",
            Description = "Po wykryciu gry: włącz profil gier, wyłącz indeksowanie, zostaw komendy głosowe.",
            IsTemplate = true,
            TemplateId = "gaming",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.ProcessEvent, Parameters = { ["event"] = "game-started" } },
            Then = { new AutomationAction { ActionId = "gaming.enable" } }
        };
        yield return new AutomationRule
        {
            Name = "Ostrzeżenie o małym miejscu na dysku",
            Description = "Powiadom gdy wolne miejsce spadnie poniżej 5 GB.",
            IsTemplate = true,
            TemplateId = "low-disk",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.Threshold, Parameters = { ["metric"] = "disk-free", ["op"] = "<", ["value"] = "5120" } },
            Then = { new AutomationAction { ActionId = "notify", Parameters = { ["title"] = "Mało miejsca na dysku", ["message"] = "Wolne miejsce < 5 GB." } } }
        };
        yield return new AutomationRule
        {
            Name = "Ostrzeżenie o wysokiej temperaturze",
            Description = "Powiadom przy temperaturze CPU/GPU powyżej 85°C.",
            IsTemplate = true,
            TemplateId = "high-temp",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.Threshold, Parameters = { ["metric"] = "temperature", ["op"] = ">", ["value"] = "85" } },
            Then = { new AutomationAction { ActionId = "notify", Parameters = { ["title"] = "Wysoka temperatura", ["message"] = "CPU/GPU przekracza 85°C." } } }
        };
        yield return new AutomationRule
        {
            Name = "Powiadomienie o starcie PC",
            Description = "Poinformuj telefon, że komputer się uruchomił.",
            IsTemplate = true,
            TemplateId = "pc-started",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.SystemEvent, Parameters = { ["event"] = "started" } },
            Then = { new AutomationAction { ActionId = "notify.phone", Parameters = { ["message"] = "Komputer się uruchomił." } } }
        };
        yield return new AutomationRule
        {
            Name = "Powiadomienie o końcu gry",
            Description = "Po zamknięciu gry przywróć profil normalny i wyślij krótkie podsumowanie.",
            IsTemplate = true,
            TemplateId = "game-finished",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.ProcessEvent, Parameters = { ["event"] = "game-exited" } },
            Then = { new AutomationAction { ActionId = "gaming.disable" }, new AutomationAction { ActionId = "notify", Parameters = { ["title"] = "Gra zamknięta" } } }
        };
        yield return new AutomationRule
        {
            Name = "Profil nocny",
            Description = "Wycisz dźwięki powiadomień i włącz ciemny motyw między 23:00 a 7:00.",
            IsTemplate = true,
            TemplateId = "night",
            When = new AutomationTrigger { Kind = AutomationTriggerKind.Schedule, Parameters = { ["cron"] = "0 23 * * *" } },
            Then = { new AutomationAction { ActionId = "profile.night" } }
        };
    }
}
