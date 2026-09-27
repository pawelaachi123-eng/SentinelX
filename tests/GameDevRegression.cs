using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.98 · GAME DEV (Roblox) — offline: szablony Luau z najlepszymi praktykami (pcall,
/// BindToClose, walidacja serwera, idempotentny ProcessReceipt, task.wait), matematyka DevEx
/// (0,0035 USD/R$), słownik, nauka, projektowanie, modelowanie. Online: przez fejkową sieć
/// i wyłącznik WiFi (wyniki tylko po włączeniu).</summary>
internal static class GameDevRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        GameDevToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ————— szablony Luau: najlepsze praktyki dosłownie w kodzie —————
        string leaderstats = Handle("roblox skrypt: leaderstats");
        Check(leaderstats.Contains("pcall") && leaderstats.Contains("BindToClose") && leaderstats.Contains("GetAsync") && leaderstats.Contains("task.wait"),
            "leaderstats: pcall + BindToClose + task.wait (nie deprecated wait)");
        string remote = Handle("roblox skrypt: zdalne");
        Check(remote.Contains("OnServerEvent") && remote.Contains("walidacja") && remote.Contains("rate limit"),
            "zdalne: walidacja typu i rate-limit na serwerze");
        string shop = Handle("roblox skrypt: sklep");
        Check(shop.Contains("ProcessReceipt") && shop.Contains("PurchaseGranted") && shop.Contains("NotProcessedYet"),
            "sklep: idempotentny ProcessReceipt");
        string kill = Handle("roblox skrypt: killbrick");
        Check(kill.Contains("debounce") && kill.Contains("Touched"), "killbrick z debounce");
        Check(Handle("roblox skrypt: tween").Contains("TweenService"), "tween przez TweenService");
        Check(Handle("roblox skrypt: narzedzie").Contains("cooldown"), "narzędzie z cooldownem");
        Check(Handle("roblox skrypt: checkpoint").Contains("CharacterAdded"), "checkpoint na CharacterAdded");
        Check(Handle("roblox skrypt: cokolwiek").Contains("Znam typy"), "nieznany typ → lista, nie zmyślony kod");

        // ————— matematyka DevEx (kurs 0,0035 USD/R$) —————
        string small = Handle("roblox monetyzacja: 10000");
        Check(small.Contains("35,00 USD") && small.Contains("30 000"), "10 000 R$ = 35,00 USD + ostrzeżenie o minimum: " + small.Split('\n')[1]);
        string big = Handle("roblox monetyzacja: 35000");
        Check(big.Contains("122,50 USD") && !big.Contains("jeszcze się nie wypłaca"), "35 000 R$ = 122,50 USD, minimum spełnione");
        Check(Handle("roblox monetyzacja: 100").Contains("70 R$"), "prowizja 30%: gracz płaci 100, twórca dostaje 70");

        // ————— wiedza projektowa —————
        Check(Handle("roblox nauka").Contains("Etap") || Handle("roblox nauka").Contains("etap"), "plan nauki ma etapy");
        Check(Handle("roblox nauka").Contains("Luau"), "plan nauki zaczyna od Luau");
        Check(Handle("roblox projektowanie").Contains("60 SEKUND"), "projektowanie: pierwsze 60 sekund");
        Check(Handle("roblox modelowanie").Contains("Anchored"), "modelowanie: Anchored jako podstawa");
        Check(Handle("roblox optymalizacja").Contains("StreamingEnabled"), "optymalizacja: StreamingEnabled");
        Check(Handle("roblox checklist").Contains("Ikona") || Handle("roblox checklist").Contains("ikon"), "checklista: ikona");
        string sketch = Handle("roblox szkic: Horrorowy obby");
        Check(sketch.Contains("SZKIC GDD") && sketch.Contains("HORROR"), "szkic rozpoznaje gatunek (horror)");
        string concept = Handle("roblox pojecie: remoteevent");
        Check(concept.Contains("NIE zwraca wartości"), "słownik: RemoteEvent nie zwraca wartości");
        Check(Handle("roblox pojecie: nieznane-pojecie").Contains("nie zgaduję"), "nieznane pojęcie — uczciwe");
        Check(Handle("roblox struktura").Contains("ReplicatedStorage"), "struktura: ReplicatedStorage wyjaśnione");

        // ————— ONLINE przez fejkową sieć + wyłącznik —————
        var web = new WebAccessService(new WebAccessRegression.FakeWebHandler(), settingsDirectory: Path.Combine(directory, "settings"));
        string offline = GameDevToolbox.TryHandle("roblox najlepsze: datastore", CommandText.Normalize("roblox najlepsze: datastore"), web)!;
        Check(offline.Contains("WYŁĄCZONE"), "roblox najlepsze bez WiFi odmawia: " + offline.Split('\n')[0]);
        web.Toggle(true);
        string online = await Routerize(web, "roblox najlepsze: datastore");
        Check(online.Contains("WYNIKI SZUKANIA") && online.Contains("create.roblox.com"),
            "roblox najlepsze celuje w dokumentację Robloxa: " + online.Split('\n')[0]);
        Check((await Routerize(web, "roblox nowosci")).Contains("WYNIKI SZUKANIA"), "roblox nowosci szuka release notes");

        foreach (string sentence in new[] { "roblox kupiłem kiedyś", "nauka jazdy na rowerze", "skrypt na moją stronę" })
            Check(GameDevToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem game dev: " + sentence);

        File.WriteAllText(Path.Combine(directory, "gamedev.txt"),
            "PASS\nLuau templates (pcall/BindToClose/validation/idempotency), DevEx math, glossary, curriculum, design, web-search with wifi gate verified\n");
    }

    private static async Task<string> Routerize(WebAccessService web, string command)
    {
        var router = new CommandRouter(new SystemMonitor(), new SystemInfoService(),
            new LocalAiService(new GamingModeService(), settingsDirectory: Path.Combine(Path.GetTempPath(), "gamedev-ai")),
            new ConversationMemoryService(Path.Combine(Path.GetTempPath(), "gamedev-mem")), web: web);
        return await router.ProcessAsync(command);
    }
}
