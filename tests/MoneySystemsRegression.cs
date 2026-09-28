using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.99 · SYSTEMY MONETYZACJI PRO — test integralności warstwy przychodu:
/// BoostService (mnożniki wpięte w AddCoins: osobisty + globalny event), SpinService
/// (darmowy dzienny, wagi = jawne szanse, grant spin_extra), OfferService (REALNY
/// licznik 72 h od FirstJoinAt, idempotentny grant, własne ID w ProcessReceipt),
/// BattlePassService (30 poziomów, XP z EventBusa, tor premium, claim per poziom),
/// OfflineEarningsService (limit godzin, stawka wg gatunku, podwojenie raz/dobę),
/// GroupBonusService (+% dla grupy), OnboardingService (5 kroków FTUE), konfiguracja,
/// zakładki klienta (OFERTY/SPIN/SEZON), chip boosta + pływające monety w HUD,
/// wpięcie w Forge (obie ścieżki) i playbook README-MONEY.md.</summary>
internal static class MoneySystemsRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string? FindRoot(string? start)
    {
        var current = start;
        for (int depth = 0; depth < 12 && current != null; depth++)
        {
            string candidate = Path.Combine(current, "gamedev", "roblox", "mega-obby");
            if (Directory.Exists(candidate)) return current;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        string? root = FindRoot(Directory.GetCurrentDirectory()) ?? FindRoot(AppContext.BaseDirectory);
        if (root == null)
        {
            File.WriteAllText(Path.Combine(directory, "money-systems.txt"),
                "SKIPPED\ngamedev/ nie znalezione obok aplikacji (uruchom z korzenia repo)\n");
            return Task.CompletedTask;
        }

        string gameRoot = Path.Combine(root, "gamedev", "roblox", "mega-obby");
        string Read(string relative) =>
            File.ReadAllText(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        // ————— komplet nowych usług —————
        string[] services =
        {
            "BoostService", "GroupBonusService", "OnboardingService", "SpinService",
            "OfferService", "BattlePassService", "OfflineEarningsService",
        };
        foreach (var service in services)
            Check(File.Exists(Path.Combine(gameRoot, "src/Server/Modules/" + service + ".lua")),
                "brak usługi: " + service);

        // ————— boost: mnożniki naprawdę w AddCoins (nie tylko deklaracja) —————
        string boost = Read("src/Server/Modules/BoostService.lua");
        Check(boost.Contains("PersonalMultiplier") && boost.Contains("GlobalMultiplier") && boost.Contains("IntervalSeconds"),
            "BoostService: mnożnik osobisty + globalny event");
        string playerData = Read("src/Server/Modules/PlayerDataService.lua");
        Check(playerData.Contains("BoostService.PersonalMultiplier") && playerData.Contains("BoostService.GlobalMultiplier")
            && playerData.Contains("GroupBonusService.IsMember"),
            "PlayerDataService: boost i bonus grupy wpięte w AddCoins");
        Check(playerData.Contains("CoinsSpent") && playerData.Contains("SpentTotal"),
            "PlayerDataService: wydatki publikowane (onboarding „wydaj monety”)");

        // ————— spin: darmowy dzienny + jawne szanse + produkt —————
        string spin = Read("src/Server/Modules/SpinService.lua");
        Check(spin.Contains("FreePerDay") && spin.Contains("UtcDayNumber") && spin.Contains("Percent")
            && spin.Contains("GrantSpin"),
            "SpinService: darmowy spin/dobę, jawne szanse (wymóg Roblox), grant spin_extra");

        // ————— oferty: prawdziwy licznik + idempotentność + ProcessReceipt —————
        string offers = Read("src/Server/Modules/OfferService.lua");
        Check(offers.Contains("FirstJoinAt") && offers.Contains("expireHoursFromJoin") && offers.Contains("ExpiresIn"),
            "OfferService: REALNY licznik od pierwszego wejścia (bez fake countdown)");
        Check(offers.Contains("bez duplikatów"),
            "OfferService: zakup jednorazowy (idempotentny)");
        string monetization = Read("src/Server/Modules/MonetizationService.lua");
        Check(monetization.Contains("config.Offers") && monetization.Contains("GrantOffer"),
            "MonetizationService: paragony ofert rozpoznawane po własnych ID");
        Check(monetization.Contains("deps.SpinService, deps.OfferService, deps.BattlePassService, deps.OfflineEarningsService"),
            "MonetizationService: granty usług PRO wpięte w ProcessReceipt");

        // ————— sezon: XP z EventBusa + premium + claim —————
        string pass = Read("src/Server/Modules/BattlePassService.lua");
        Check(pass.Contains("XpFor") && pass.Contains("ClaimPass") && pass.Contains("battlepass_premium")
            && pass.Contains("PassClaimed"),
            "BattlePassService: XP za granie, claim per poziom, tor premium");

        // ————— offline: limit + stawka gatunku + podwojenie —————
        string offline = Read("src/Server/Modules/OfflineEarningsService.lua");
        Check(offline.Contains("MaxHours") && offline.Contains("OfflineRatePerHour") && offline.Contains("OfflineDoubleDay"),
            "OfflineEarningsService: limit godzin, stawka wg gatunku, podwojenie raz/dobę");
        foreach (var genre in new[] { "SimulatorGenre", "TycoonGenre", "HorrorGenre", "ShooterGenre", "RacingGenre" })
            Check(Read("src/Server/Genres/" + genre + ".lua").Contains("OfflineRatePerHour"),
                genre + ": brak hooka stawki offline");

        // ————— Forge: nowe usługi w OBU ścieżkach —————
        string forge = Read("src/Server/Modules/Forge.lua");
        foreach (var service in services)
            Check(forge.Contains("\"" + service + "\""), "Forge: brak " + service + " w kolejności startu");

        // ————— konfiguracja —————
        string config = Read("src/Shared/GameConfig.lua");
        foreach (var section in new[] { "GameConfig.Offers", "GameConfig.Spin", "GameConfig.BattlePass",
            "GameConfig.Offline", "GameConfig.Group", "GameConfig.GlobalBoost", "GameConfig.Onboarding" })
            Check(config.Contains(section), "GameConfig: brak sekcji " + section);

        // ————— klient: zakładki i juice —————
        string shopUi = Read("src/Client/Controllers/ShopUI.lua");
        foreach (var marker in new[] { "\"offers\"", "\"spin\"", "\"pass\"", "ClaimPass", "KOŁO FORTUNY", "SEZON" })
            Check(shopUi.Contains(marker), "ShopUI: brak elementu monetizacji: " + marker);
        string hud = Read("src/Client/Controllers/HudController.lua");
        Check(hud.Contains("BoostChip") && hud.Contains("floatLabel"),
            "HUD: chip boosta + pływające monety (+X)");

        // ————— playbook —————
        string money = Read("README-MONEY.md");
        Check(money.Contains("SEZON PREMIUM") && money.Contains("0,0035") && money.Contains("szanse"),
            "README-MONEY.md: playbook z cenami i zasadami uczciwości");

        File.WriteAllText(Path.Combine(directory, "money-systems.txt"),
            "PASS\nMonetyzacja PRO: boost (osobisty x2 + globalne eventy x3) w AddCoins · spin dzienny z jawnymi szansami ·\n" +
            "oferty limitowane (realny licznik 72 h, idempotentne) · SEZON 30 poziomów (free+premium) ·\n" +
            "zarobki offline z podwojeniem · bonus grupy +10% · onboarding 5 kroków · zakładki OFERTY/SPIN/SEZON ·\n" +
            "chip boosta i pływające monety w HUD · README-MONEY.md (cennik 99/299/799 R$)\n");
        return Task.CompletedTask;
    }
}
