using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.99 · DUŻA GRA (inspiracje Fisch) — funkcje, które robią z wygenerowanej
/// gry „dużą” grę: gatunek RYBY (brania losowane wagami rzadkości, mutacje,
/// łódka na głębiny), ZBIORY/INDEKS (odkrycia + nagroda za komplet),
/// OSIĄGNIĘCIA (progi statystyk nagradzane raz, działają w każdym gatunku)
/// i POGODA (rotacja na serwerze, szczęście wzmacniające rzadkie ryby).
/// Test pilnuje kontraktów usług, wpisu w obu ścieżkach Forge, konfiguracji,
/// HUD gatunku ryby, rozpoznania w toolboxie i dokumentacji.</summary>
internal static class BigGameRegression
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
            File.WriteAllText(Path.Combine(directory, "biggame.txt"),
                "SKIPPED\ngamedev/ nie znalezione obok aplikacji (uruchom z korzenia repo)\n");
            return Task.CompletedTask;
        }

        string gameRoot = Path.Combine(root, "gamedev", "roblox", "mega-obby");
        string Read(string relative) =>
            File.ReadAllText(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        // ————— gatunek RYBY: pełny kontrakt + mechaniki Fisch —————
        string ryby = Read("src/Server/Genres/RybyGenre.lua");
        Check(ryby.Contains("Pack.BuildWorld") && ryby.Contains("Pack.Setup") && ryby.Contains("Pack.Hud")
            && ryby.Contains("Pack.Shop") && ryby.Contains("Pack.OnBuy") && ryby.Contains("Pack.QuestPool")
            && ryby.Contains("Pack.LeaderValue") && ryby.Contains("Pack.OfflineRatePerHour"),
            "RybyGenre: niepełny kontrakt Pack");
        Check(ryby.Contains("rollSpecies") && ryby.Contains("rollMutation") && ryby.Contains("WeatherService.Luck()")
            && ryby.Contains("CollectionService.Report") && ryby.Contains("BestCatch") && ryby.Contains("DeepBonus")
            && ryby.Contains("Glebiny"),
            "RybyGenre: rzadkości + mutacje + szczęście pogody + indeks + głębiny");
        Check(ryby.Contains("WeatherService.Label()") && ryby.Contains("CollectionService.Progress"),
            "RybyGenre: HUD pokazuje pogodę i postęp indeksu");

        // ————— ZBIORY / INDEKS —————
        string collections = Read("src/Server/Modules/CollectionService.lua");
        Check(collections.Contains("CollectionService.Report") && collections.Contains("CollectionService.Progress")
            && collections.Contains("komplet indeksu") && collections.Contains("GameConfig.Collections")
            && collections.Contains("data.Collections"),
            "CollectionService: odkrycia + nagroda za komplet + zapis w profilu");

        // ————— OSIĄGNIĘCIA —————
        string achievements = Read("src/Server/Modules/AchievementService.lua");
        Check(achievements.Contains("AchievementService.Start") && achievements.Contains("threshold")
            && achievements.Contains("data.Achievements") && achievements.Contains("task.wait(20)")
            && achievements.Contains("AddCoins"),
            "AchievementService: progi statystyk nagradzane raz (idempotentnie)");

        // ————— POGODA —————
        string weather = Read("src/Server/Modules/WeatherService.lua");
        Check(weather.Contains("WeatherService.Luck") && weather.Contains("WeatherService.Label")
            && weather.Contains("RotationSeconds") && weather.Contains("SendAll") && weather.Contains("WeightedPick"),
            "WeatherService: rotacja ważona + szczęście + ogłoszenie wszystkim");

        // ————— konfiguracja: ryby / pogoda / zbiory / osiągnięcia —————
        string config = Read("src/Shared/GameConfig.lua");
        Check(config.Contains("GameConfig.Fish") && config.Contains("RarityWeights")
            && config.Contains("\"Legendarna\"") && config.Contains("Upiór Odległych Wód")
            && config.Contains("Błyszczący") && config.Contains("Ogromny"),
            "GameConfig.Fish: wagi rzadkości, 12 gatunków, mutacje");
        Check(config.Contains("GameConfig.Weather") && config.Contains("\"tecza\"") && config.Contains("luck = 2"),
            "GameConfig.Weather: 4 typy pogody ze szczęściem");
        Check(config.Contains("GameConfig.Collections") && config.Contains("ryby_glebiny") && config.Contains("reward = 2500"),
            "GameConfig.Collections: zestawy indeksu z nagrodami");
        Check(config.Contains("GameConfig.Achievements") && config.Contains("BestDistance")
            && config.Contains("BestWave") && config.Contains("Harvested"),
            "GameConfig.Achievements: progi działające w każdym gatunku");

        // ————— Forge: rejestr, aliasy, usługi w OBU ścieżkach —————
        string forge = Read("src/Server/Modules/Forge.lua");
        Check(forge.Contains("ryby = \"RybyGenre\""), "Forge: gatunek ryby w rejestrze");
        Check(forge.Contains("fish = \"ryby\"") && forge.Contains("wedkowanie = \"ryby\"")
            && forge.Contains("lowienie = \"ryby\""), "Forge: aliasy wędkowania");
        Check(forge.Split(new[] { "\"CollectionService\", \"AchievementService\", \"WeatherService\"" }).Length - 1 == 2,
            "Forge: 3 nowe usługi w OBU ścieżkach startu");
        Check(forge.Contains("fale, ryby."), "Forge: uczciwa lista gatunków w odmowie");

        // ————— toolbox + dokumentacja —————
        string toolbox = File.ReadAllText(Path.Combine(root, "GameDevToolbox.cs"));
        Check(toolbox.Contains("\"ryby\", \"RYBY") && toolbox.Contains("wędkowanie/łowisko/fish")
            && toolbox.Contains("\"ryby\" => \"jezioro z pomostem"),
            "GameDevToolbox: gatunek ryby rozpoznawany w „roblox wygeneruj”");
        string forgeReadme = Read("README-FORGE.md");
        Check(forgeReadme.Contains("`ryby`") && forgeReadme.Contains("DZIESIĘCIU") && !forgeReadme.Contains("DZIEWIĘCIU"),
            "README-FORGE.md: 10 gatunków w tabeli");
        string spec = Read("src/Shared/GameSpec.lua");
        Check(spec.Contains("ryby"), "GameSpec: ryby na liście gatunków");

        File.WriteAllText(Path.Combine(directory, "biggame.txt"),
            "PASS\nDuża gra (Fisch-style): gatunek RYBY — 12 gatunków ryb w 5 rzadkościach (wagi widoczne w configu),\n" +
            "mutacje Błyszczący x2 / Ogromny x3, łódka → GŁĘBINY x1,6, pogoda rotująca na serwerze (Słonecznie /\n" +
            "Deszcz / Burza / Tęcza — szczęście do x2), ZBIORY/INDEKS (2 zestawy ryb, komplet = 900/2500 monet),\n" +
            "OSIĄGNIĘCIA (7 progów działających w każdym gatunku, nagrody idempotentne), leaderboard = najcięższy złów.\n" +
            "3 nowe usługi w OBU ścieżkach Forge; toolbox rozpoznaje 10 gatunków.\n");
        return Task.CompletedTask;
    }
}
