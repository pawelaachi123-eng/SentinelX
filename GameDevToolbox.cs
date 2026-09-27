using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

/// <summary>
/// GAME DEV (Roblox) — najważniejsza funkcja 0.98. Offline: pełna ścieżka nauki (Luau → budowanie →
/// UI → dane → sieć → optymalizacja → publikacja), generatory SKRYPTÓW LUAU pisanych według
/// oficjalnych najlepszych praktyk (pcall wokół DataStore, BindToClose, walidacja po stronie
/// serwera, rate-limit, task.wait zamiast wait, ProcessReceipt z idempotencją), słownik pojęć,
/// modelowanie i projektowanie (MDA, pierwsze 60 sekund), checklist wydania, monetyzacja z PRAWDZIWYMI
/// przelicznikami (DevEx 0,0035 USD/R$, minimum 30 000 R$, prowizja 30%). Online (tylko po włączeniu
/// WiFi): „roblox najlepsze: …” celuje w dokumentację i DevForum, „roblox nowosci” w release notes.
/// Sieć idzie wyłącznie przez WebAccessService — wyłącznik 📶 po stronie użytkownika.
/// </summary>
public static class GameDevToolbox
{
    public static string? TryHandle(string command, string text, WebAccessService? web = null)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        // ————— ONLINE (wymagają włączonego WiFi — decyzja użytkownika) —————
        var best = Regex.Match(text, @"^(?:roblox najlepsze|roblox szukaj)[:\s]+(.+)$", RegexOptions.Singleline);
        if (best.Success)
            return WebSearch(web, "Roblox " + Payload(raw, "roblox najlepsze", "roblox szukaj") +
                " best practices (site:create.roblox.com OR site:devforum.roblox.com)");
        var example = Regex.Match(text, @"^roblox przyklad[:\s]+(.+)$", RegexOptions.Singleline);
        if (example.Success)
            return WebSearch(web, "Roblox " + Payload(raw, "roblox przyklad") +
                " luau example tutorial (site:create.roblox.com OR site:devforum.roblox.com)");
        if (Is(norm, "roblox nowosci") || Is(norm, "roblox najnowsze"))
            return WebSearch(web, "Roblox Studio release notes changelog site:devforum.roblox.com");

        // ————— OFFLINE (działają zawsze) —————
        if (Is(norm, "roblox nauka") || Is(norm, "roblox plan nauki")) return Curriculum();
        if (Is(norm, "roblox struktura")) return Structure();
        if (Is(norm, "roblox projektowanie")) return Design();
        if (Is(norm, "roblox modelowanie") || Is(norm, "roblox budowanie")) return Modeling();
        if (Is(norm, "roblox optymalizacja") || Is(norm, "roblox wydajnosc")) return Optimization();
        if (Is(norm, "roblox checklist") || Is(norm, "roblox checklista")) return ReleaseChecklist();
        var money = Regex.Match(text, @"^roblox monetyzacja[:\s]+(\d{1,9})$");
        if (money.Success) return Monetization(money.Groups[1].Value);
        var script = Regex.Match(text, @"^roblox skrypt[:\s]+([a-z]+)$");
        if (script.Success) return Script(script.Groups[1].Value);
        var concept = Regex.Match(text, @"^roblox pojecie[:\s]+(.+)$");
        if (concept.Success) return Concept(Payload(raw, "roblox pojecie"));
        var sketch = Regex.Match(text, @"^(?:roblox szkic|roblox gdd)[:\s]+(.+)$", RegexOptions.Singleline);
        if (sketch.Success) return Sketch(Payload(raw, "roblox szkic", "roblox gdd"));
        return null;
    }

    // ————— ONLINE —————

    private static string WebSearch(WebAccessService? web, string query)
        => (web ?? WebAccessService.Shared).SearchAsync(query).GetAwaiter().GetResult();

    // ————— OFFLINE: plan nauki —————

    private static string Curriculum() =>
        "PLAN NAUKI ROBLOX DEVELOPMENTU (8 etapów; buduj projekt przy KAŻDYM etapie — sama teoria nie zostaje):" + Environment.NewLine +
        "· 1. LUAU PODSTAWY (1–2 tyg.): zmienne, typy, if/for/while, funkcje, tabele (tablica vs słownik), " +
        "scope. Wynik: skrypt wypisujący i prosty licznik. Pułapka: Luau to nie Python — bloki kończą się 'end'." + Environment.NewLine +
        "· 2. STUDIO I BUDOWANIE (1 tyg.): Explorer/Properties, Part/Model/Anchored, Weld, materiały, " +
        "oświetlenie (ShadowMap/Future), teren. Wynik: własna mapa obby z 10 przeszkodami." + Environment.NewLine +
        "· 3. SKRYPTY W GRZE (2 tyg.): Touched, Humanoid, debounce, TweenService, Instance.new. " +
        "Wynik: killbrick, checkpointy, drzwi na przycisk." + Environment.NewLine +
        "· 4. UI I PROGRESJA (1–2 tyg.): ScreenGui, Frames, leaderstats, sklepy z monety. Wynik: punkty za przejście + HUD." + Environment.NewLine +
        "· 5. TRWAŁE DANE (1 tyg.): DataStore + pcall + BindToClose (szablon: „roblox skrypt: leaderstats”). " +
        "Wynik: zapis punktów między sesjami. Pułapka: bez pcall i BindToClose tracisz dane przy restarcie serwera." + Environment.NewLine +
        "· 6. SIEĆ I BEZPIECZEŃSTWO (2 tyg.): RemoteEvent/RemoteFunction, walidacja na serwerze, rate-limit " +
        "(szablon: „roblox skrypt: zdalne”). Zasada żelazna: KLIENT NIGDY NIE MA PRAWDY." + Environment.NewLine +
        "· 7. MONETYZACJA I META (1 tyg.): Game Passy, Dev Producty (szablon: „roblox skrypt: sklep”), " +
        "DevEx i prowizje („roblox monetyzacja: 35000”)." + Environment.NewLine +
        "· 8. OPTYMALIZACJA I PUBLIKACJA (1 tyg.): StreamingEnabled, part-count, mikroprofiler " +
        "(„roblox optymalizacja”), checklist wydania („roblox checklist”)." + Environment.NewLine +
        "· Do każdego etapu: „roblox pojecie: <nazwa>” (słownik), „roblox skrypt: <typ>” (gotowce), " +
        "a po włączeniu WiFi 📶: „roblox najlepsze: <temat>” (oficjalna dokumentacja i DevForum).";

    private static string Structure() =>
        "STRUKTURA GRY (gdzie co leży i DLACZEGO):" + Environment.NewLine +
        "· ServerScriptService — logika serwera (leaderstats, sklep, checkpointy). Klient tego NIE widzi: tu prawda." + Environment.NewLine +
        "· ReplicatedStorage — to, co widzą obie strony: RemoteEvents, moduły wspólne, presety. " +
        "Nigdy sekretów — wszystko stąd klient może podejrzeć." + Environment.NewLine +
        "· ServerStorage — zasoby tylko serwera (nagrody, niewidoczne mapy do teleportów)." + Environment.NewLine +
        "· StarterPlayer/StarterPlayerScripts — skrypty klienta (HUD, efekty lokalne, kamera)." + Environment.NewLine +
        "· StarterGui — ScreenGui i UI; logika UI w LocalScriptach z StarterPlayerScripts." + Environment.NewLine +
        "· Workspace — świat: mapę anchoruj (Anchored = true dla statycznych), a dynamiczne obiekty przez spawner." + Environment.NewLine +
        "· Zasady: jeden moduł = jedna odpowiedzialność; nazwy po angielsku w kodzie (mniej bugów z拼音 zalem), " +
        "config w jednym ModuleScript (SPEED = 25), zero magicznych liczb w skryptach.";

    private static string Design() =>
        "PROJEKTOWANIE GRY — reguły, które decydują o retencji:" + Environment.NewLine +
        "· PIERWSZE 60 SEKUND: gracz ma dostać cel, jedną akcję i jedną nagrodę, zanim zdąży wyjść. " +
        "Zero cutscenek, zero tutoriali-ścian tekstu; nauka przez yszanie ( tutorial przez działanie)." + Environment.NewLine +
        "· CORE LOOP (pętla): akcja → nagroda → cel → mocniejsza akcja. Zapisz swoją pętlę jednym zdaniem; " +
        "jeśli się nie da — gra nie ma pętli." + Environment.NewLine +
        "· PROGRESJA: dwie osie — siła (liczby rosną) i status (rankings, tytuły, kosmetyki). Liczby łatwo, " +
        "status długo: kosmetyki trzymają weteranów, liczby łapią nowych." + Environment.NewLine +
        "· KRZYWA TRUDNOŚCI: łatwo 5 min → przypływ trudności co 10–15 min → odpoczynek. Szczyt frustracji " +
        "tuż przed nagrodą działa, ale tylko raz na sesję." + Environment.NewLine +
        "· SOCJALNA PĘTLA: widok innych graczy (leaderboard), wspólny cel, prosty czat/emocje — gry Roblox " +
        "żyją z tego, że gra się PRED INNYMI." + Environment.NewLine +
        "· MDA: mechanika → dynamika → estetyka. Projektuj od estetyki („chcę dreszczyku”), przez dynamikę " +
        "(„ciemne korytarze + dźwięki”), do mechaniki („latarka z baterią”). Odwrotna kolejność daje klon." + Environment.NewLine +
        "· SZKIC DO WYPEŁNIENIA: „roblox szkic: nazwa gry” · pred testem prawdziwymi graczami nie wydawaj niczego.";

    private static string Modeling() =>
        "MODELOWANIE I BUDOWANIE — praktyka, nie taste:" + Environment.NewLine +
        "· ANCHOR: każdą statyczną część Anchored = true — inaczej spadnie. To bug nr 1 początkujących." + Environment.NewLine +
        "· CZĘŚCI: Union sparingly (dzieci się psują przy eksporcie i ciężkie) — częściej MeshPart + " +
        "własne tekstury; collision fidelity: Box dla dekoracji (szybciej)." + Environment.NewLine +
        "· SKALA: 1 stud ≈ 28 cm. Drzwi 7 studów wysokie, schody max 1 stud stopień (bez skakania), " +
        "postać ≈ 5 studów — trzymaj wzorzec człowieka na scene, inaczej świat „nie czuje się” realny." + Environment.NewLine +
        "· MATERIAŁY I ŚWIATŁO: jedna paleta (3 kolory + akcent), Future dla idealnego światła (drogi — " +
        "dla małych map), ShadowMap domyślnie. Ambient w dół, kontrast robi klimat." + Environment.NewLine +
        "· TERRAIN: duże naturalne powierzchnie = Terrain (tanie), architektura = części. Nie rób gór z części." + Environment.NewLine +
        "· CLEANUP: nazwy części sensowne (nie „Part64”), foldery w Workspace (Map/Props/Effects), " +
        "zbędne właściwości domyślne zostaw — mniej do diffów i mniej bugów przy skryptowaniu." + Environment.NewLine +
        "· TEST: ustaw kamerę gracza na start mapy — jeśli nie wiesz, gdzie iść, gracz też nie będzie wiedział.";

    private static string Optimization() =>
        "OPTYMALIZACJA — od największego zysku do kosmetyki:" + Environment.NewLine +
        "· StreamingEnabled = true (Workspace): klient ładuje okolice gracza — najtańszy ogromny zysk na dużych mapach." + Environment.NewLine +
        "· LICZBA CZĘŚCI: łącz dekoracje (ale nie Unions na siłę), usuń niewidoczne ściany, " +
        "CollisionFidelity = Box dla rzeczy bez kolizji gameplayowej." + Environment.NewLine +
        "· PĘTLE: while task.wait(0.1) do zamiast while true do wait() — wait() jest deprecated; " +
        "i nie rób pętli co klatkę dla czegoś, co może się stać co 0.1 s." + Environment.NewLine +
        "· REMOTE: nie strzelaj RemoteEvent co klatkę (to koszty sieci i serwera); grupuj zmiany, " +
        "wysyłaj tylko przy zmianie stanu. Serwer waliduje wszystko (patrz „roblox skrypt: zdalne”). " +
        "UWAGA: HttpService w grze NIE może gadać z localhost ani prywatnymi adresami — i słusznie." + Environment.NewLine +
        "· PAMIĘĆ: rozłączaj połączenia (Connect zwracaconnection — :Disconnect() przy usuwaniu), " +
        "Destroy() na tym, co nieżyje, Instance.Destroying zamiast czekać na GC." + Environment.NewLine +
        "· POMIAR, NIE GŁOSOWANIE: View → Performance Stats + MicroProfiler. Poprawiaj to, co mikroprofiler " +
        "pokazuje jako najszerszy pasek, nie to, co „się wydaje”.";

    private static string ReleaseChecklist() =>
        "CHECKLISTA PRZED PUBLIKACJĄ (odhacz wszystko albo nie publikuj):" + Environment.NewLine +
        "· Ikona 512×512 czytelna w miniaturce (test: zmniejsz do 100 px — czy widać, o co chodzi?)" + Environment.NewLine +
        "· 3 miniatury z prawdziwego gameplayu (nie klikbajt, który nie ma z grą nic wspólnego)" + Environment.NewLine +
        "· Nazwa z słowami kluczowymi (co robisz + klimat), opis: pierwsze 2 zdania = dlaczego zagrać" + Environment.NewLine +
        "· ONBOARDING: nowy gracz w 60 s rozumie cel (testuj na kimś, kto gry nie zna)" + Environment.NewLine +
        "· MOBILE: sprawdź UI na telefonie (przyciski ≥ 44 px, nic pod palcem), kamera i skok działają" + Environment.NewLine +
        "· DANE: zapis działa, restart serwera nie kasuje postępu (BindToClose!), błędne dane nie wywalają gracza" + Environment.NewLine +
        "· SEKURITY: walidacja na serwerze wszędzie, rate-limity na RemoteEventach, żadnych sekretów w ReplicatedStorage" + Environment.NewLine +
        "· UMIARKOWANIE: czat filtrowany (standard), nazwy/grafiki zgodne z regulaminem, ankieta wieku wypełniona" + Environment.NewLine +
        "· MONETYZACJA: pasy/dev produkty przetestowane w trybie testowym (ProcessReceipt idempotentny)" + Environment.NewLine +
        "· PO WYDANIU: pierwsze 48 h czytaj feedback i poprawiaj top 1 frustrację — to decyduje o retencji.";

    private static string Monetization(string robuxText)
    {
        if (!double.TryParse(robuxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out double robux) || robux <= 0)
            return "Użycie: „roblox monetyzacja: 35000” (ilość Robuxów).";
        CultureInfo pl = CultureInfo.GetCultureInfo("pl-PL");
        double usd = robux * 0.0035;
        bool belowMin = robux < 30_000;
        return "MONETYZACJA — przelicznik dla " + robux.ToString("N0", pl) + " R$:" + Environment.NewLine +
            "· DevEx: ~" + usd.ToString("N2", pl) + " USD (kurs wymiany 0,0035 USD za 1 R$)" + Environment.NewLine +
            (belowMin
                ? "· UWAGA: DevEx wymaga minimum 30 000 R$ (~105 USD) i konta zweryfikowanego — ten wynik jeszcze się nie wypłaca" + Environment.NewLine
                : "· spełnia minimum DevEx (30 000 R$); wypłata wymaga zweryfikowanego konta i podatków po Twojej stronie" + Environment.NewLine) +
            "· PRAWDA O PASACH: gracz płaci 100 R$ → Ty dostajesz ~70 R$ (Roblox bierze 30% prowizji)" + Environment.NewLine +
            "· stawki pasów, które grają: 79 (drobiazg), 199 (popularny), 499 (specjalny), 999+ (cosmetyk dla weteranów)" + Environment.NewLine +
            "· cena kosmetyku rośnie z czasem gry: czym wcześniej w sesji, tym taniej; endgame może być drogi" + Environment.NewLine +
            "· nie obiecywuję zysków — to przeliczniki, nie prognoza popularności";
    }

    // ————— OFFLINE: generatory skryptów Luau —————

    private static string Script(string kind)
    {
        string what = Flat(kind);
        return what switch
        {
            "leaderstats" or "dane" or "datastore" =>
                "-- ServerScriptService/Leaderstats.luau · zapis punktów z pcall i BindToClose\n" +
                "local Players = game:GetService(\"Players\")\n" +
                "local DataStoreService = game:GetService(\"DataStoreService\")\n" +
                "local store = DataStoreService:GetDataStore(\"PlayerPoints_v1\")\n\n" +
                "local function onPlayerAdded(player: Player)\n" +
                "\tlocal leaderstats = Instance.new(\"Folder\")\n" +
                "\tleaderstats.Name = \"leaderstats\"\n" +
                "\tlocal points = Instance.new(\"IntValue\")\n" +
                "\tpoints.Name = \"Punkty\"\n" +
                "\tlocal ok, saved = pcall(function()\n\t\treturn store:GetAsync(\"u\" .. player.UserId)\n\tend)\n" +
                "\tpoints.Value = (ok and saved) or 0 -- awaria sieci nie zabija gracza\n" +
                "\tpoints.Parent = leaderstats\n\tleaderstats.Parent = player\nend\n\n" +
                "local function onPlayerRemoving(player: Player)\n" +
                "\tlocal stats = player:FindFirstChild(\"leaderstats\")\n" +
                "\tif not stats then return end\n" +
                "\tlocal points = stats:FindFirstChild(\"Punkty\")\n" +
                "\tif not points then return end\n" +
                "\tpcall(function()\n\t\tstore:SetAsync(\"u\" .. player.UserId, points.Value)\n\tend)\nend\n\n" +
                "Players.PlayerAdded:Connect(onPlayerAdded)\n" +
                "Players.PlayerRemoving:Connect(onPlayerRemoving)\n" +
                "game:BindToClose(function() -- restart serwera: ratuj dane WSZYSTKICH\n" +
                "\tfor _, player in Players:GetPlayers() do onPlayerRemoving(player) end\n" +
                "\ttask.wait(2)\nend)\n" +
                "-- Dobre praktyki tu: pcall wokół KAŻDEGO wywołania DataStore, klucz per UserId,\n" +
                "-- BindToClose na restart, task.wait zamiast deprecated wait().",

            "killbrick" =>
                "-- Script w części zabijającej · debounce per postać\n" +
                "local part = script.Parent\n" +
                "local debounce: {[Player]: true} = {}\n\n" +
                "part.Touched:Connect(function(hit: BasePart)\n" +
                "\tlocal character = hit.Parent\n" +
                "\tlocal humanoid = character and character:FindFirstChildOfClass(\"Humanoid\")\n" +
                "\tif not humanoid or humanoid.Health <= 0 then return end\n" +
                "\tif debounce[character] then return end\n" +
                "\tdebounce[character] = true\n" +
                "\thumanoid.Health = 0\n" +
                "\ttask.delay(1, function() debounce[character] = nil end)\nend)\n" +
                "-- Touched strzela wielokrotnie w jednej sekundzie — bez debaca HP leci w pętli.",

            "checkpoint" =>
                "-- ServerScriptService/Checkpoints.luau · części w workspace/Checkpoints o nazwach 1,2,3…\n" +
                "local Players = game:GetService(\"Players\")\n" +
                "local checkpoints = workspace:WaitForChild(\"Checkpoints\")\n\n" +
                "Players.PlayerAdded:Connect(function(player: Player)\n" +
                "\tlocal stats = Instance.new(\"Folder\")\n\tstats.Name = \"leaderstats\"\n" +
                "\tlocal stage = Instance.new(\"IntValue\")\n\tstage.Name = \"Etap\"\n\tstage.Value = 1\n" +
                "\tstage.Parent = stats\n\tstats.Parent = player\n" +
                "\tplayer.CharacterAdded:Connect(function(character: Model)\n" +
                "\t\ttask.wait(0.1)\n" +
                "\t\tlocal target = checkpoints:FindFirstChild(tostring(stage.Value))\n" +
                "\t\tif target and character.PrimaryPart then\n" +
                "\t\t\tcharacter:PivotTo(target.CFrame + Vector3.new(0, 4, 0))\n" +
                "\t\tend\n" +
                "\tend)\nend)\n\n" +
                "for _, trigger in checkpoints:GetChildren() do\n" +
                "\ttrigger.Touched:Connect(function(hit: BasePart)\n" +
                "\t\tlocal player = Players:GetPlayerFromCharacter(hit.Parent)\n" +
                "\t\tif not player then return end\n" +
                "\t\tlocal stats = player:FindFirstChild(\"leaderstats\")\n" +
                "\t\tlocal stage = stats and stats:FindFirstChild(\"Etap\")\n" +
                "\t\tif stage and tonumber(trigger.Name) == stage.Value + 1 then\n" +
                "\t\t\tstage.Value += 1 -- tylko kolejny etap; cofanie się nie psuje\n" +
                "\t\tend\n" +
                "\tend)\nend",

            "sklep" or "devproduct" or "receipt" =>
                "-- ServerScriptService/Receipts.luau · Dev Product z idempotencją (bez podwójnych zakupów)\n" +
                "local MarketplaceService = game:GetService(\"MarketplaceService\")\n" +
                "local DataStoreService = game:GetService(\"DataStoreService\")\n" +
                "local receipts = DataStoreService:GetDataStore(\"Receipts_v1\")\n\n" +
                "MarketplaceService.ProcessReceipt = function(info: MarketplaceService.ProcessReceiptInfo)\n" +
                "\tlocal player = game.Players:GetPlayerByUserId(info.PlayerId)\n" +
                "\tif not player then return Enum.ProductPurchaseDecision.NotProcessedYet end\n" +
                "\tlocal ok, already = pcall(function() return receipts:GetAsync(info.PurchaseId) end)\n" +
                "\tif ok and already then return Enum.ProductPurchaseDecision.PurchaseGranted end\n" +
                "\t-- TU przyznaj zakup (np. +100 monet) — najlepiej przez ten sam DataStore co dane gracza\n" +
                "\tlocal granted = pcall(function() receipts:SetAsync(info.PurchaseId, true) end)\n" +
                "\tif not granted then return Enum.ProductPurchaseDecision.NotProcessedYet end\n" +
                "\treturn Enum.ProductPurchaseDecision.PurchaseGranted\nend\n" +
                "-- NotProcessedYet = Roblox spróbuje ponownie; dlatego zakup MUSI być idempotentny.",

            "tween" =>
                "-- płynny ruch części (serwer albo klient)\n" +
                "local TweenService = game:GetService(\"TweenService\")\n" +
                "local part = script.Parent\n" +
                "local info = TweenInfo.new(1.2, Enum.EasingStyle.Quad, Enum.EasingDirection.InOut)\n" +
                "local tween = TweenService:Create(part, info, { Position = part.Position + Vector3.new(0, 8, 0) })\n" +
                "tween:Play()\n" +
                "-- Dobre praktyki: TweenService zamiast pętli z lerpem; one-shot → tween:Completed:Wait()",

            "zdalne" or "remote" =>
                "-- ReplicatedStorage/Events/ActionRequest (RemoteEvent) · serwer jest jedyną prawdą\n" +
                "-- SERWER:\n" +
                "local Events = game:GetService(\"ReplicatedStorage\"):WaitForChild(\"Events\")\n" +
                "local request = Events:WaitForChild(\"ActionRequest\")\n" +
                "local lastCall: {[number]: number} = {}\n\n" +
                "request.OnServerEvent:Connect(function(player: Player, action: unknown)\n" +
                "\tif typeof(action) ~= \"string\" then return end -- walidacja TYPU zanim cokolwiek zrobisz\n" +
                "\tlocal now = os.clock()\n" +
                "\tif lastCall[player.UserId] and now - lastCall[player.UserId] < 0.5 then return end -- rate limit\n" +
                "\tlastCall[player.UserId] = now\n" +
                "\tif action == \"otworzSkrzynie\" then\n" +
                "\t\t-- logika po stronie serwera: koszty, nagrody, zapis — klient NIGDY nie mówi serwerowi wynik\n" +
                "\tend\n" +
                "end)\n\n" +
                "game.Players.PlayerRemoving:Connect(function(player: Player)\n" +
                "\tlastCall[player.UserId] = nil -- sprzątaj, inaczej pamięć rośnie\n" +
                "end)\n" +
                "-- KLIENT: request:FireServer(\"otworzSkrzynie\")\n" +
                "-- RemoteFunction: unikaj (InvokeServer może zablokować klienta na timeout); wolisz RemoteEvent + odpowiedź.",

            "narzedzie" or "tool" =>
                "-- Tool z cooldownem (StarterPack)\n" +
                "local tool = script.Parent\n" +
                "local cooldown = false\n\n" +
                "tool.Activated:Connect(function()\n" +
                "\tif cooldown then return end\n" +
                "\tcooldown = true\n" +
                "\t-- animacja / efekt / obrażenia (walidacja dystansu po stronie serwera!)\n" +
                "\ttask.wait(0.6)\n" +
                "\tcooldown = false\n" +
                "end)\n" +
                "-- Dobre praktyki: obrażenia licz serwer (Damage humanoidalowi z walidacją odległości),\n" +
                "-- klient tylko animuje. Bez task.wait w pętli — Activated + cooldown wystarcza.",

            _ => "Znam typy skryptów: „roblox skrypt: leaderstats | killbrick | checkpoint | sklep | tween | zdalne | narzedzie”. " +
                 "Po włączeniu WiFi 📶: „roblox najlepsze: <temat>” — szukam oficjalnych przykładów w dokumentacji i DevForum.",
        };
    }

    // ————— OFFLINE: słownik pojęć —————

    private static string Concept(string input)
    {
        string what = Flat(input ?? "").Trim();
        string concept =
            what switch
            {
                "luau" => "Luau — język skryptów Roblox (wariant Lua z typami opcjonalnymi i szybszym runtime). " +
                    "Blok = end (nie klamry), komentarz --, tabele są wszystkim (tablica i słownik naraz).",
                "remoteevent" => "RemoteEvent — wiadomość klient↔serwer bez odpowiedzi zwrotnej (fire-and-forget). " +
                    "NIE zwraca wartości; serwer zawsze waliduje treść. Jeśli potrzebujesz odpowiedzi — drugi RemoteEvent z powrotem.",
                "remotefunction" => "RemoteFunction — wywołanie z odpowiedzią, ale InvokeServer może zawiesić klienta " +
                    "przy braku odpowiedzi. W praktyce: wolij RemoteEvent i komunikat zwrotny.",
                "datastore" => "DataStore — trwały zapis (baza Robloxa). Zawsze w pcall (sieć pada), klucz per UserId, " +
                    "limity krotkolet i BindToClose na restart serwera. Produkcja: ProfileService/ProfileStore.",
                "humanoid" => "Humanoid — „dusza” postaci: HP, chód, skok, stany. Odczyt: FindFirstChildOfClass(\"Humanoid\"). " +
                    "Zmiana MaxHealth/Health zamiast skryptowania śmierci ręcznie.",
                "anchored" => "Anchored — część nie podlega fizyce (nie spada). Statyczna mapa = wszystko anchored; " +
                    "poruszasz przez CFrame/Tween, nie siłą grawitacji.",
                "weld" => "Weld/WeldConstraint — skleja części sztywno. Model = PrimaryPart + WeldConstraints do niego; " +
                    "poruszasz całym modelem przez PivotTo.",
                "replication" => "Replikacja — serwer jest właścicielem prawdy; zmiany serwera idą do klientów. " +
                    "Klient może zmieniać tylko LOKALNE kopie (kamera, efekty, UI).",
                "filteringenabled" => "FilteringEnabled — dziś zawsze włączone: klient nie zmienia świata dla innych. " +
                    "Stąd RemoteEvent i walidacja serwera to podstawa.",
                "tween" => "Tween — płynna animacja właściwości (pozycja, przezroczystość, UI). TweenService:Create(instancja, TweenInfo, cel).",
                "module" => "ModuleScript — biblioteka: zwraca tabelę funkcji. Jedno źródło prawdy dla logiki używanej w wielu miejscach.",
                "leaderstats" => "leaderstats — folder w graczu; Roblox automatycznie pokazuje wartości na tablicy wyników.",
                "replicatedstorage" => "ReplicatedStorage — wspólne zasoby (obie strony widzą). Sekretów tam NIE wkładaj. " +
                    "ServerStorage = tylko serwer.",
                "streamingenabled" => "StreamingEnabled — klient ładuje mapę wokół gracza partiami. Duże mapy bez tego " +
                    "= telefony umierają. Włącza się we właściwościach Workspace.",
                "marketplaceservice" => "MarketplaceService — pasy gry i Dev Producty. ProcessReceipt na serwerze, " +
                    "idempotentnie (szablon: „roblox skrypt: sklep”).",
                _ => "",
            };
        if (concept.Length > 0)
            return "POJĘCIE: " + concept + Environment.NewLine +
                "· więcej: „roblox pojecie: luau | remoteevent | remotefunction | datastore | humanoid | anchored | weld | replication | filteringenabled | tween | module | leaderstats | replicatedstorage | streamingenabled | marketplaceservice”";
        return "Nie znam tego pojęcia w słowniku — nie zgaduję. Znane: luau, remoteevent, remotefunction, datastore, humanoid, " +
            "anchored, weld, replication, filteringenabled, tween, module, leaderstats, replicatedstorage, streamingenabled, marketplaceservice. " +
            "Po włączeniu WiFi 📶: „roblox najlepsze: " + (input ?? "").Trim() + "” — szukam w oficjalnej dokumentacji.";
    }

    // ————— OFFLINE: szkic GDD —————

    private static string Sketch(string input)
    {
        string name = (input ?? "").Trim();
        if (name.Length < 2)
            return "Użycie: „roblox szkic: Moja Gra” — dostaniesz szkielet projektu do wypełnienia.";
        string flat = Flat(name);
        // gatunki hybrydowe są realne („horrorowy obby”) — wykrywam wszystkie pasujące, nie tylko pierwszy
        var genres = new List<string>();
        if (flat.Contains("obby") || flat.Contains("parkour")) genres.Add("OBBY (parkour)");
        if (flat.Contains("symulator") || flat.Contains("simulator")) genres.Add("SYMULATOR (klikaj → zarabiaj → rośnij)");
        if (flat.Contains("tycoon")) genres.Add("TYCOON (buduj fabrykę)");
        if (flat.Contains("horror")) genres.Add("HORROR (zwiedzaj, uciekaj, przetrwaj)");
        if (flat.Contains("fps") || flat.Contains("strzel")) genres.Add("SHOOTER (PvP/PvE)");
        string genre = genres.Count > 0 ? string.Join(" + ", genres) : "RODZAJ DO WYBORU";
        return "SZKIC GDD: „" + name + "” (" + genre + ")" + Environment.NewLine +
            "· CORE LOOP (jedno zdanie!): gracz __________ → dostaje __________ → odblokowuje __________" + Environment.NewLine +
            "· PIERWSZE 60 SEKUND: co nowy gracz robi, czuje i dostaje w pierwszej minucie?" + Environment.NewLine +
            "· PROGRESJA: co rośnie (liczby)? co zostaje na pamiątkę (kosmetyki/rangi)?" + Environment.NewLine +
            "· SESJA: ile trwa jedna pętla? (2–5 min dla symulatorów, 10–20 min dla horrorów)" + Environment.NewLine +
            "· SOCJALNA PĘTLA: co gracze robią RAZEM albo PRZECZYP siebie?" + Environment.NewLine +
            "· MONETYZACJA: 1 pas za wygodę + 1–2 kosmetyki na start (nie pay-to-win — społeczność karze)" + Environment.NewLine +
            "· METRYKI SUKCESU: D1 retencja > 25%? średnia sesja > 8 min? (potem czytaj w analityce Robloxa)" + Environment.NewLine +
            "· RYZYKA: co zrobi konkurencja lepiej? co jest najtrudniejsze technicznie? (odpowiedz szczerze)" + Environment.NewLine +
            "· dalej: „roblox struktura” (kod), „roblox skrypt: …” (gotowce), „roblox checklist” (wydanie)";
    }

    // ————— pomocnicze —————

    private static string Flat(string input)
    {
        string s = (input ?? "").ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool Is(string norm, string trigger) => norm == trigger || norm.StartsWith(trigger + ":");

    private static string Payload(string raw, params string[] prefixes)
    {
        string t = (raw ?? "").Trim();
        foreach (string p in prefixes)
        {
            if (!t.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = t[p.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..].Trim();
            return rest;
        }
        return t;
    }
}
