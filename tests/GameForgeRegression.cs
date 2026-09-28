using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.98 · GAMEFORGE — generator gier wg wpisanego opisu. Test integralności:
/// dystrybutor Forge (rejestr 6 gatunków + aliasy + ścieżka obby bez regresji),
/// 5 modułów gatunków z pełnym kontraktem (BuildWorld/Setup/Hud/Shop/QuestPool/
/// LeaderValue), GameSpec z jednym miejscem wyboru gatunku, sklep gatunku
/// (ForgeBuy + zwrot monet), questa statystyczne, tablice wg gatunku, klient
/// (panel HUD gatunku + zakładka SKLEP TRYBU), toolbox „roblox wygeneruj: …”
/// rozpoznający gatunek z opisu i uczciwie odmawiający nieznanego.</summary>
internal static class GameForgeRegression
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
            File.WriteAllText(Path.Combine(directory, "game-forge.txt"),
                "SKIPPED\ngamedev/ nie znalezione obok aplikacji (uruchom z korzenia repo)\n");
            return Task.CompletedTask;
        }

        string gameRoot = Path.Combine(root, "gamedev", "roblox", "mega-obby");

        string Read(string relative) =>
            File.ReadAllText(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        // ————— pliki generatora —————
        string[] genrePacks =
        {
            "src/Server/Genres/SimulatorGenre.lua", "src/Server/Genres/TycoonGenre.lua",
            "src/Server/Genres/HorrorGenre.lua", "src/Server/Genres/ShooterGenre.lua",
            "src/Server/Genres/RacingGenre.lua",
        };
        foreach (var relative in genrePacks)
            Check(File.Exists(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
                "brak modułu gatunku: " + relative);
        foreach (var relative in new[] { "src/Shared/GameSpec.lua", "src/Server/Modules/Forge.lua",
            "src/Server/Modules/ForgeShopService.lua", "README-FORGE.md" })
            Check(File.Exists(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
                "brak pliku generatora: " + relative);

        // ————— GameSpec: jedno miejsce wyboru gry —————
        string spec = Read("src/Shared/GameSpec.lua");
        Check(spec.Contains("GameSpec.Genre = \"obby\"") && spec.Contains("GameSpec.Name"),
            "GameSpec: domyślny gatunek i nazwa do edycji");

        // ————— Forge: rejestr gatunków, aliasy, obby bez regresji —————
        string forge = Read("src/Server/Modules/Forge.lua");
        foreach (string genre in new[] { "symulator", "tycoon", "horror", "shooter", "wyscigi", "obby" })
            Check(forge.Contains(genre), "Forge: brak gatunku w rejestrze: " + genre);
        Check(forge.Contains("GenreAliases") && forge.Contains("OBBY_ORDER") && forge.Contains("COMMON_ORDER"),
            "Forge: aliasy + rozdzielone ścieżki (obby bez regresji / wspólne usługi)");
        Check(forge.Contains("Nie znam gatunku"), "Forge: uczciwa odmowa nieznanego gatunku (fallback do obby)");
        string main = Read("src/Server/Main.server.lua");
        Check(main.Contains("Forge.Run") && main.Contains("GameSpec"), "Main: start przez Forge z GameSpec");

        // ————— kontrakt modułów gatunków —————
        foreach (var relative in genrePacks)
        {
            string pack = Read(relative);
            Check(pack.Contains("Pack.BuildWorld") && pack.Contains("Pack.Setup") && pack.Contains("Pack.Hud")
                && pack.Contains("Pack.Shop") && pack.Contains("Pack.QuestPool") && pack.Contains("Pack.LeaderValue"),
                relative + ": niepełny kontrakt (BuildWorld/Setup/Hud/Shop/QuestPool/LeaderValue)");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(pack, @"^\s*//", System.Text.RegularExpressions.RegexOptions.Multiline),
                relative + ": komentarz C-style w Lua");
        }

        // ————— sklep gatunku: walidacja serwera + ZWROT monet przy błędzie —————
        string shop = Read("src/Server/Modules/ForgeShopService.lua");
        Check(shop.Contains("ForgeBuy") && shop.Contains("TrySpend") && shop.Contains("zwrot zakupu"),
            "ForgeShop: ForgeBuy + TrySpend + zwrot monet przy nieudanym OnBuy");

        // ————— questa statystyczne + tablice wg gatunku —————
        string quests = Read("src/Server/Modules/QuestService.lua");
        Check(quests.Contains("stat:") && quests.Contains("QuestPoolProvider"),
            "QuestService: questa statystyczne + pula wg gatunku");
        string boards = Read("src/Server/Modules/LeaderboardService.lua");
        Check(boards.Contains("LeaderValueProvider"), "LeaderboardService: wartość lidera wg gatunku");

        // ————— klient: panel HUD gatunku + zakładka SKLEP TRYBU —————
        string hud = Read("src/Client/Controllers/HudController.lua");
        Check(hud.Contains("snapshot.Hud") && hud.Contains("PackPanel"), "HUD: panel statystyk gatunku");
        string shopUi = Read("src/Client/Controllers/ShopUI.lua");
        Check(shopUi.Contains("SKLEP TRYBU") && shopUi.Contains("ForgeBuy"), "Sklep: zakładka trybu z ForgeBuy");

        // ————— toolbox: roblox wygeneruj — rozpoznaje gatunek, uczciwie odmawia —————
        string toolbox = File.ReadAllText(Path.Combine(root, "GameDevToolbox.cs"));
        Check(toolbox.Contains("roblox wygeneruj") && toolbox.Contains("DetectGenre") && toolbox.Contains("ForgeGuide"),
            "toolbox: polecenie roblox wygeneruj z rozpoznaniem gatunku");
        Check(toolbox.Contains("Nie zgaduję gatunku"), "toolbox: uczciwa odmowa nieznanego gatunku");

        int packLines = genrePacks.Sum(relative =>
            File.ReadAllLines(Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar))).Length);
        Check(packLines > 800, "moduły gatunków mają być pełnoprawnymi grami (mniej niż 800 linii łącznie?)");

        // ————— klasa błędów znaleziona na żywo: inline „//” i cyrylica w KODZIE (poza stringami) —————
        foreach (var luaFile in Directory.EnumerateFiles(Path.Combine(root, "gamedev"), "*.lua", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(luaFile);
            bool inBlockComment = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string withoutStrings = System.Text.RegularExpressions.Regex.Replace(
                    lines[i], "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
                if (withoutStrings.Contains("--[[")) inBlockComment = true;
                if (inBlockComment)
                {
                    if (withoutStrings.Contains("]]")) inBlockComment = false;
                    continue;
                }
                int commentIndex = withoutStrings.IndexOf("--", StringComparison.Ordinal);
                string codeOnly = commentIndex >= 0 ? withoutStrings[..commentIndex] : withoutStrings;
                Check(!System.Text.RegularExpressions.Regex.IsMatch(codeOnly, @"\s//"),
                    $"{Path.GetFileName(luaFile)}:{i + 1} komentarz „//” w kodzie Lua (to nie C#!)");
                Check(!codeOnly.Any(c => c >= '\u0400' && c <= '\u04FF'),
                    $"{Path.GetFileName(luaFile)}:{i + 1} cyrylica w kodzie Lua (literówka keyboardu)");
            }
        }

        File.WriteAllText(Path.Combine(directory, "game-forge.txt"),
            "PASS\nGameForge: 6 gatunków (obby/symulator/tycoon/horror/shooter/wyscigi) z jednej linijki GameSpec.Genre\n" +
            "Forge dispatch + aliasy · kontrakt 5 modułów gatunków (" + packLines + " linii) · ForgeBuy ze zwrotem ·\n" +
            "questa statystyczne · tablice wg gatunku · HUD panel + SKLEP TRYBU · toolbox roblox wygeneruj\n");
        return Task.CompletedTask;
    }
}
