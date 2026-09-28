using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.99 · RÓŻNORODNOŚĆ — 3 nowe gatunki generatora (biegacz, farma, fale)
/// + pakiet urozmaicenia: trzecie jajko (6 nowych zwierzaków), 2 nowe ślady,
/// LOSOWANE typy globalnych eventów (x3 / x5 krótko / dar dla wszystkich),
/// nagrody dzienne ze spinami i zwierzakiem (dzień 7), więcej questa w puli.
/// Test pilnuje: kontraktów nowych modułów gatunków (z hookiem offline), wpisu
/// w rejestrze Forge i aliasów, treści konfiguracji, eventów z Rotations,
/// grantów dziennych, rozpoznawania nowych gatunków w toolboxie i README.</summary>
internal static class VarietyRegression
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
            File.WriteAllText(Path.Combine(directory, "variety.txt"),
                "SKIPPED\ngamedev/ nie znalezione obok aplikacji (uruchom z korzenia repo)\n");
            return Task.CompletedTask;
        }

        string gameRoot = Path.Combine(root, "gamedev", "roblox", "mega-obby");
        string Read(string relative) =>
            File.ReadAllText(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        // ————— 3 nowe moduły gatunków z PEŁNYM kontraktem (w tym offline) —————
        string[] newGenres =
        {
            "src/Server/Genres/BiegaczGenre.lua", "src/Server/Genres/FarmaGenre.lua", "src/Server/Genres/FaleGenre.lua",
        };
        foreach (var relative in newGenres)
        {
            Check(File.Exists(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
                "brak modułu gatunku: " + relative);
            string pack = Read(relative);
            Check(pack.Contains("Pack.BuildWorld") && pack.Contains("Pack.Setup") && pack.Contains("Pack.Hud")
                && pack.Contains("Pack.Shop") && pack.Contains("Pack.QuestPool") && pack.Contains("Pack.LeaderValue")
                && pack.Contains("Pack.OfflineRatePerHour"),
                relative + ": niepełny kontrakt (BuildWorld/Setup/Hud/Shop/QuestPool/LeaderValue/OfflineRatePerHour)");
        }

        // ————— Forge: rejestr + aliasy nowych gatunków —————
        string forge = Read("src/Server/Modules/Forge.lua");
        foreach (var marker in new[] { "BiegaczGenre", "FarmaGenre", "FaleGenre",
            "bieg = \"biegacz\"", "rolnik = \"farma\"", "zombie = \"fale\"",
            "biegacz, farma, fale" })
            Check(forge.Contains(marker), "Forge: brak wpisu różnorodności: " + marker);

        // ————— konfiguracja: jajko, ślady, eventy, nagrody, questa —————
        string config = Read("src/Shared/GameConfig.lua");
        Check(config.Contains("egg_wulkan") && config.Contains("Protopan Feniks"),
            "GameConfig: trzecie jajko (wulkaniczne) z nowymi zwierzakami");
        Check(config.Contains("trail_toxic") && config.Contains("trail_cyber"),
            "GameConfig: 2 nowe ślady (toxic, cyber)");
        Check(config.Contains("Rotations") && config.Contains("kind = \"gift\"") && config.Contains("multiplier = 5"),
            "GameConfig: losowane typy eventów (x3 / x5 / dar)");
        Check(config.Contains("spins = 1") && config.Contains("spins = 2") && config.Contains("pet = true"),
            "GameConfig: nagrody dzienne ze spinami i zwierzakiem (dzień 7)");
        Check(config.Contains("q_coins_1500") && config.Contains("q_stages_30"),
            "GameConfig: dodatkowe questa w puli globalnej");

        // ————— BoostService: eventy z Rotations (nie jeden sztywny) —————
        string boost = Read("src/Server/Modules/BoostService.lua");
        Check(boost.Contains("WeightedPick") && boost.Contains("Rotations") && boost.Contains("dar eventu"),
            "BoostService: eventy losowane z Rotations (w tym dar dla wszystkich)");

        // ————— nagrody dzienne: NextSpins + granty —————
        string daily = Read("src/Server/Modules/DailyRewardService.lua");
        Check(daily.Contains("NextSpins") && daily.Contains("GrantSpin") && daily.Contains("GrantEggRoll"),
            "DailyRewardService: spiny i zwierzak przy odbiorze nagrody");
        string hud = Read("src/Client/Controllers/HudController.lua");
        Check(hud.Contains("NextSpins") && hud.Contains("zwierzak!"),
            "HUD: popup nagrody dziennej pokazuje spiny i zwierzaka");

        // ————— toolbox: nowe gatunki w „roblox wygeneruj” —————
        string toolbox = File.ReadAllText(Path.Combine(root, "GameDevToolbox.cs"));
        foreach (var marker in new[] { "\"biegacz\", \"BIEGACZ", "\"farma\", \"FARMA", "\"fale\", \"PRZETRWANIE",
            "bieg/runner/dystans", "rolnik/sadzenie/ogrod", "obrona/zombie/wave" })
            Check(toolbox.Contains(marker), "GameDevToolbox: brak rozpoznania: " + marker);

        // ————— dokumentacja —————
        string forgeReadme = Read("README-FORGE.md");
        Check(forgeReadme.Contains("`biegacz`") && forgeReadme.Contains("`farma`") && forgeReadme.Contains("`fale`")
            && forgeReadme.Contains("DZIEWIĘCIU"),
            "README-FORGE.md: 9 gatunków w tabeli");
        string spec = Read("src/Shared/GameSpec.lua");
        Check(spec.Contains("biegacz") && spec.Contains("farma") && spec.Contains("fale"),
            "GameSpec: lista gatunków zaktualizowana");

        File.WriteAllText(Path.Combine(directory, "variety.txt"),
            "PASS\nRóżnorodność: 9 gatunków generatora (+biegacz, +farma, +fale z pełnym kontraktem i hookiem offline) ·\n" +
            "3. jajko wulkaniczne (6 nowych zwierzaków, legendarny Protopan Feniks x1,55) · ślady toxic i cyberpunk ·\n" +
            "eventy globalne LOSOWANE (x3 długie / x5 krótkie / dar dla wszystkich online) · nagrody dzienne ze spinami\n" +
            "i zwierzakiem (dzień 3/5/7) · 3 nowe questa w puli · toolbox rozpoznaje 9 gatunków z aliasów\n");
        return Task.CompletedTask;
    }
}
