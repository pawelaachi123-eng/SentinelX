# MEGA OBBY: Wyspy Przygód — kompletna gra (kod robi wszystko)

**120 etapów na 6 wyspach, generowanych proceduralnie z kodu serwera.** Nie musisz
stawiać niczego w Studio ręcznie: wklejasz skrypty (albo podpinasz Rojo),
wciaskasz **Play** — lobby, portal, checkpointy, killbricki, platformy ruchome,
wiry, sprężyny, monety, deszcz monet, tablice wyników, sklep, zwierzaki, questy,
nagrody dzienne, anty-cheat i monetyzacja — wszystko działa od razu.

```
mega-obby/
├── default.project.json      ← layout Rojo 7 (opcjonalnie)
├── README-GRA.md             ← ten plik
└── src/
    ├── Shared/               ← ReplicatedStorage.Shared
    │   ├── GameConfig.lua    ← WSZYSTKIE stałe gry (zmieniasz tylko tu)
    │   ├── Util.lua          ← Signal, Trove, losowanie ważone, limiter, formaty PL
    │   └── Net.lua           ← wszystkie RemoteEvent/Function + limity częstości
    ├── Server/               ← ServerScriptService.Server
    │   ├── Main.server.lua   ← bootstrap (kolejność startu usług)
    │   └── Modules/          ← 14 usług (patrz tabela niżej)
    └── Client/               ← StarterPlayerScripts.Client
        ├── Main.client.lua   ← bootstrap klienta (klawisz R = checkpoint)
        └── Controllers/      ← HUD, Sklep, Ustawienia, Muzyka
```

## Dwie drogi wgrania

### A) Rojo (polecam — pliki zostają na dysku, Studio je synchronizuje)
1. Zainstaluj Rojo 7 (`winget install rojo` albo wtyczka do Studio z releases).
2. W tym folderze: `rojo serve` → w Studio wtyczka **Connect**.
3. Wciśnij **Play**. Gotowe.

### B) Ręcznie (bez narzędzi)
Utwórz w Studio dokładnie taką strukturę i wklej zawartość plików:

| Plik | Gdzie w Studio | Jaki obiekt |
|---|---|---|
| `src/Shared/*.lua` | ReplicatedStorage → Folder `Shared` | 3× ModuleScript (nazwa = nazwa pliku) |
| `src/Server/Main.server.lua` | ServerScriptService → Folder `Server` | Script (RunContext: Legacy/Server) |
| `src/Server/Modules/*.lua` | `Server` → Folder `Modules` | 14× ModuleScript |
| `src/Client/Main.client.lua` | StarterPlayer → StarterPlayerScripts → Folder `Client` | LocalScript |
| `src/Client/Controllers/*.lua` | `Client` → Folder `Controllers` | 4× ModuleScript |

W Workspace włącz **StreamingEnabled** (zalecane), FilteringEnabled i tak jest
zawsze włączone. Działa w trybie Play od razu; DataStore ruszy po **publikacji**
(Settings → Security → „Enable Studio Access to API Services” do testów).

## 14 usług serwera — co robi każda

| Moduł | Odpowiedzialność |
|---|---|
| `PlayerDataService` | DataStore + retry, autosave 2 min, BindToClose, migracja schematu, leaderstats, mnożniki monet w JEDNYM miejscu, tryb offline w Studio |
| `EffectsService` | ślady (Trail), błyski checkpointów, konfetti na finiszu |
| `WorldBuilder` | buduje lobby + 120 etapów z seeda (deterministycznie!), pętle ruchu platform/wirów, przenoszenie graczy na platformach, killbricki, sprężyny |
| `StageService` | checkpointy, zasada „tylko +1” (anty-skip), portal, finisz, skip z produktu, respawn na padach |
| `CoinService` | monety z toru, respawn, magnes zwierzaków, deszcz monet w lobby |
| `PetService` | jajka (losowanie ważone), duplikaty → monety, modele z części, podążanie (lerp serwera), bonusy: mnożnik + magnes |
| `TrailService` | sklep ze śladami (cena walidowana na serwerze) |
| `MonetizationService` | przepustki + produkty; ProcessReceipt **dokładnie jeden**, paragony idempotentne, natychmiastowy zapis po grancie |
| `QuestService` | 3 dzienne zadania (te same dla wszystkich — seed dnia UTC), postęp z EventBusa, nagrody |
| `DailyRewardService` | streak 7-dniowy po UTC, przerwa = reset do 1 |
| `LeaderboardService` | 2 tablice TOP-10 (etapy, monety) na OrderedDataStore, cache nazw |
| `AntiCheatService` | prędkość/teleport → ostrzeżenia → kick; legalne teleporty wyłączone z testu; admini zwolnieni |
| `AdminService` | `!help !coins N !stage N !rain !announce … !kick gracz` |
| `AmbientService` | cykl dnia i nocy + strefy muzyki (atrybut gracza → klient) |

## Najlepsze praktyki, których pilnuje kod (to różni go od „wygenerowanych” skryptów)

- **Serwer jest autorytatywny**: klient może tylko PROSIĆ; ceny, etapy, nagrody,
  dropy i pozycja respawnu liczone wyłącznie na serwerze.
- **Jeden `ProcessReceipt`** na grę, **idempotentny** (PurchaseId w danych),
  nieznany produkt → `NotProcessedYet` (paragon wróci — nic nie przepada).
- **pcall na każdym wywołaniu DataStore** + retry z backoffem + `UpdateAsync`
  (nie nadpisze nowszego zapisu) + `BindToClose` (nikt nie straci postępu).
- **Limity częstości na każdym RemoteFunction** (token bucket na gracza) i
  handler w `pcall` — złośliwy klient nie położy wątku.
- **task.wait / task.spawn** (zero deprecated `wait`), CollectionService zamiast
  skanowania workspace, attributes zamiast Values tam, gdzie to sensowne.
- **Tarcza anty-skip**: checkpoint działa tylko `aktualny+1` — telepatyczne
  „teleporty” cheatów po prostu nie dają nagrody.
- Uczciwość wobec gracza: brak wyników = komunikat, duplikat zwierzaka = monety,
  ID=0 w configu = jasny komunikat dla twórcy, nie udawany zakup.

## Monetyzacja krok po kroku (PO publikacji gry)

1. Creator Dashboard → Twoje doświadczenie → **Monetization**.
2. Stwórz 3 Passes: *x2 Monety*, *+25% Szybkości*, *VIP* — skopiuj ich **ID**.
3. Stwórz 5 Developer Products: *500 / 2 000 / 6 000 Monet*, *Rzut Premium
   Jajkiem*, *Pomiń Etap* — skopiuj **ID**.
4. Wklej ID do `src/Shared/GameConfig.lua` → sekcje `Gamepasses` i
   `DevProducts` (zamień `0`). Zapisz. To wszystko — reszta działa.

Do czasu podpięcia ID klienci dostaną jasny komunikat „twórca musi podpiąć ID”
— nie ma udawanych zakupów ani błędów.

## Muzyka i dźwięki

`GameConfig.Ambient.Music` i FX-y klienta mają `assetId = 0` = **cisza**.
Wgraj własne audio (Creator → Audio, prawa do utworów MUSISZ mieć), wpisz ID
— klient sam zacznie grać strefowo (lobby/wyspy). Nie pakuję cudzych assetów.

## Anty-cheat — jak działa

- próbki pozycji co 0,5 s; prędkość > ~2,2× WalkSpeed przez **2 próbki z rzędu**
  (bez fałszywych alarmów) → strike; skok > 130 studów → strike (chyba że to
  legalny teleport: portal/skip/spawn ustawia `LastTeleport`);
- 3 strike (gasną po 60 s) → kick z uczciwym komunikatem; admini zwolnieni;
- nagrody i tak idą wyłącznie przez serwer, więc „trainer” i tak nic nie da.

## Modele i animacje z Blendera

Obok tej gry: `gamedev/blender/` — generator postaci R6 z rigiem, 7 animacji
(Idle/Walk/Run/Jump/Fall/Victory/Dance) i 11 propów (moneta, gem, flaga,
killbrick, platforma, portal, skrzynia…) + eksport FBX/GLB. Instrukcja:
`gamedev/blender/README-BLENDER.md`.

## Uczciwa uwaga techniczna

Kod Luau pisany pod Roblox Luau i sprawdzony ręcznie (struktura, API zgodne z
dokumentacją 2026), ale **repo SentinelX nie ma kompilatora Luau** — pierwsze
wklejenie do Studio pokaże ewentualną literówkę w sekundę (Studio podświetla).
Jeśli coś nie zagra: konsola (F9) pokaże dokładnie plik i linię. Struktura i
wzorce są zgodne z oficjalnymi rekomendacjami Roblox (docs.roblox.com).
