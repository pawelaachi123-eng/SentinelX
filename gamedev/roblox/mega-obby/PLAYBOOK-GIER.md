# 🎮 PLAYBOOK GIER — jak GameForge buduje gry i jak robić to IDEALNIE

> **Ten plik to pamięć generatora.** Zanim cokolwiek zrobisz z `gamedev/roblox/mega-obby`,
> przeczytaj to. Tu jest zapisane: co potrafimy, co jest zbudowane, jak dodawać nowe
> rzeczy i jakich zasad NIGDY nie łamiemy. Aktualizuj go przy każdej większej zmianie.

---

## 1. CO POTRAFIMY — jedna linijka = pełna gra

`src/Shared/GameSpec.lua` ma jedno pole, które zmienia WSZYSTKO:

```lua
GameSpec.Genre = "obby" -- zmień na: symulator | tycoon | horror | shooter | wyscigi
                        -- biegacz | farma | fale | ryby
```

`Forge.lua` (`RunGenre`) dostarcza wspólny kręgosłup każdej grze:

| Warstwa | Skąd się bierze |
|---|---|
| Świat | `Pack.BuildWorld` (deterministyczny, z seeda) |
| Zapis gracza | `PlayerDataService` (nowe klucze w `Stats` utrwalają się same) |
| Monety | `AddCoins` z łańcuchem mnożników (pet × boost × event × grupa) |
| Sklep trybu | `Pack.Shop` + `ForgeShopService` (walidacja serwera + ZWROT monet) |
| Questy | `Pack.QuestPool` (statystyczne, `stat:`/`coins`/`playtime`) |
| Leaderboard | `Pack.LeaderValue` (jedna liczba „kto najlepszy”) |
| Offline | `Pack.OfflineRatePerHour` (zarobek za nieobecność, limit godzin) |
| HUD | `Pack.Hud` → wiersze na szklanym panelu |
| Monetyzacja | 7 usług PRO (oferty, spiny, sezon, boosty…) — w OBU ścieżkach Forge |
| Duża gra | Zbiory/Indeks · Osiągnięcia · Pogoda (3 usługi w `COMMON_ORDER`) |

**Złota reguła:** gatunek dostarcza TYLKO swoją unikalną rozrywkę. Wspólne usługi
(monety, questa, leaderboard, offline, monetyzacja) dostaje ZA DARMO.

---

## 2. DZIESIĘĆ GATUNKÓW — co każdy umie

| Gatunek | Rdzeń rozgrywki | Sklep | Leaderboard | Offline/h |
|---|---|---|---|---|
| `obby` | 120 proceduralnych etapów, 6 wysp, anty-skip | (klasyczny) | Etap | — |
| `symulator` | klikaj → siła → narodziny → mnożniki | siła/narodziny/klan | Narodziny | krótki |
| `tycoon` | taśma fabryki, 12 stanowisk, kontrakty | stanowiska/prędkość | Zarobek | średni |
| `horror` | 3 maszyny, potwór słyszy sprint, drzwi | bateria/maskowanie | Bezpieczne | — |
| `shooter` | arena fal, 4 bronie, tarcze | broń/pancerz | Zabójstwa | — |
| `wyscigi` | tor owalny, 8 checkpointów, okrążenia | silnik/opony | Okrążenia | — |
| `biegacz` | endless runner 1500 studów, kamienie milowe | mnożnik/prędkość | Dystans | 20+ |
| `farma` | 12 działek (4 premium), wzrost w czasie | wzrost/wartość/działki | Zbiory | 15+ |
| `fale` | kryształ 20 HP, fale rosnące, pancerz | HP/dmg/spowolnienia | Fala | 20+ |
| `ryby` | 12 ryb × 5 rzadkości, mutacje, GŁĘBINY | wędzisko/przynęta/łódka | Najcięższy złów | 15+ |

**Aliasy (fragment — pełna mapa w `Forge.lua`):**
`bieg/runner/dystans` · `rolnik/sadzenie/ogrod/plony` · `obrona/zombie/wave/krysztal` ·
`ryby/wedkowanie/lowienie/wedka/fish` · `f1/motory/przejazd` · `strach/duchy/piwnica` ·
`kasa/firma/restauracja` · `sila/trening/prestiz` · `wieze/platformy` · `karp/rekin/morze`.

Rozpoznawanie w aplikacji („roblox wygeneruj: …”) patrzy w `GameDevToolbox.cs`
i sprawdza gatunki w KOLEJNOŚCI — nowe dopisywane PRZED testem obby (bo „parkour”
wchodzi w skład opisów innych gier).

---

## 3. KONTRAKT MODUŁU GATUNKU — specyfikacja `Pack.*`

Nowy plik: `src/Server/Genres/<Nazwa>Genre.lua`. Musi zwrócić tabelę `Pack` z:

```lua
Pack.Id = "nazwa"                 -- klucz z GameSpec.Genre
Pack.DisplayName = "Nazwa"        -- do logów i nagłówków

function Pack.BuildWorld(folders, deps)   -- OBLIGATORYJNE
    -- buduj świat w folders.World, zwróć { SpawnPad = part, Root = model }
    -- DETERMINIZM: losowość TYLKO z seeda (Random.new(seed))
end

function Pack.Setup(deps, world)          -- OBLIGATORYJNE
    -- podepnij Touched, pętle serwera, logikę rund
    -- monety WYŁĄCZNIE przez deps.PlayerDataService.AddCoins(player, kwota, "powód")
end

function Pack.Hud(deps, player)           -- OBLIGATORYJNE
    -- return { Title = …, Rows = { { Label = …, Value = … }, … } }  (4–7 wierszy)
end

function Pack.Shop(deps, data)            -- OBLIGATORYJNE
    -- return { { Id, Name, Description, Kind="stat"|"special", Stat, Amount, Price, Owned }, … }
    -- ceny: baza * 1.45–1.6 ^ poziom (geometryczny wzrost!)
end

Pack.QuestPool = { … }                    -- OBLIGATORYJNE (min. 4 questa)
function Pack.LeaderValue(deps, player)   -- OBLIGATORYJNE (jedna liczba)
function Pack.OfflineRatePerHour(deps, player) -- OBLIGATORYJNE (uczciwa stawka)
function Pack.OnBuy(deps, player, item)   -- jeśli masz Kind="special"
```

**Wpis w Forge:** rejestr `nazwa = "NazwaGenre"` + aliasy w mapie synonym + lista
w `warn()` o nieznanym gatunku + wpisy w `COMMON_ORDER` jeśli gatunek potrzebuje
nowej usługi wspólnej. **Toolbox:** wzorzec rozpoznania w `GameDevToolbox.cs`
(PRZED obby-check) + opis + help-text. **Regresja:** kotwice w tests/*.cs.

---

## 4. JAK DODAĆ NOWY GATUNEK — 10 kroków, zero skrótów

1. **Koncept 3-zdaniowy:** rdzeń pętli rozgrywki, jedna liczba-sukces, jeden zasób.
2. `Genres/<Nazwa>Genre.lua` — pełny kontrakt (sekcja 3). Zero globali, wszystko z `deps`.
3. `GameConfig` — liczby dostrajania (jeśli potrzeba: Knobs w `GameSpec`).
4. `Forge.lua` — rejestr + aliasy + warn + (ewentualnie) usługa w COMMON_ORDER.
5. `GameSpec.lua` — dopisz do listy w komentarzu.
6. `GameDevToolbox.cs` — rozpoznanie (PRZED obby), opis generatora, help-text.
7. `README-FORGE.md` — wiersz w tabeli + aktualizacja liczby gatunków słownie.
8. **Weryfikacja lokalna:** luaparser 46/46 · arch PASS · grep inline `//` i em-dash w kodzie.
9. **Regresja:** kotwice (kontrakt + rejestr + config + toolbox + README) w tests/*.cs.
10. **Playbook:** zaktualizuj TĘ tabelę i sekcję 2. No update = no pass.

---

## 5. MONETYZACJA — uczciwa i zarabiająca (stan: ROZBUDOWANA)

**Oferty limitowane** (`GameConfig.Offers`): starter (72 h) · mega (168 h) ·
powrót weterana (dzień 7+) · pakiet sezonowy (dni 14+). Realny licznik od
`FirstJoinAt`, zakup jednorazowy, `robuxProductId = 0` → uczciwy komunikat.

**Koło fortuny**: darmowy spin/dobę, szanse JAWNE w configu i w UI, extra spin za Robux.
**Sezon**: 40 poziomów × 250 XP, tor darmowy + premium (×3, co 5. poziom +spin).
**Boosty**: osobisty ×2 · globalne eventy LOSOWANE z `Rotations`
(x2 długie / x3 / x5 krótkie / dar 250 / DAR 500 dla wszystkich online).
**Nagrody dzienne**: 14 dni (dzień 10: +spin · dzień 14: +2 spiny + zwierzak).
**Jajka**: Start (750) · Elitarne (2500) · Wulkaniczne (6000) · **Lodowe (9000,
Królowa Zimy ×1,60 w3)**. Szanse zawsze w configu.
**Offline**: limit 10 h, stawka wg gatunku, podwojenie raz/dobę za Robux.

**ŻELAZNE ZASADY UCZCIWOŚCI (wymóg Roblox + nasze własne):**
- każda szansa jawna (config + widoczna w UI),
- liczniki prawdziwe (FirstJoinAt, UtcDayNumber — zero „fałszywych liczników”),
- zakupy idempotentne (raz kupione = raz przyznane, `bez duplikatów`),
- zero pay-to-win wykluczającego: płaci się na WYGODĘ i WYGLĄD, nie na jedyną ścieżkę postępu,
- `robuxProductId = 0` nigdy nie udaje zakupu — mówi, że twórca musi podpiąć ID.

Cennik i DevEx-matematyka: `README-MONEY.md`.

---

## 6. USŁUGI „DUŻEJ GRY” — co robi „dużą” każdą grę

- **CollectionService** — indeks odkryć (ryby → zestawy, komplet = nagroda 900/2500).
  Nowa kolekcja = wpis w `GameConfig.Collections`, raport: `Report(deps, player, nazwa)`.
- **AchievementService** — progi statystyk (`GameConfig.Achievements`), nagroda RAZ,
  pętla co 20 s, działa w każdym gatunku. Nowe osiągnięcie = jeden wpis.
- **WeatherService** — pogoda rotuje ważonym losowaniem co 3 min, `Luck()` wzmacnia
  rzadkości (ryby). Nowy efekt pogody = podpięcie pod `Luck()` w swoim gatunku.

---

## 7. UI — UiKit (szkło + sprężyny), zasady

`src/Client/UiKit.lua`: `Glass` (półprzezroczysta karta, cienka obwódka, cień),
`Button` (hover = uniesienie, klik = wciśnięcie), `Chip`, `Bar`, `Toast` (stos max 4),
`Confetti`, `CountUp` (licznik „goni”), `Shine`, `OpenModal/CloseModal` (sprężyny).
Kolory WYŁĄCZNIE z `UiKit.Tokens`. HUD i sklep są zbudowane na UiKit — nowy ekran
robimy tak samo. Zero `Instance.new` bez `UiKit.Corner/Stroke` przy kartach.

---

## 8. JAK ROBIĆ TO IDEALNIE — bramki jakości

1. **Parser:** `luaparser` (pip, `--break-system-packages`) + konwerter Luau→Lua;
   cel: **X/X czystych**. Łapie: gołe wyrażenia, C-`//`, cyrylicę w kodzie, typy.
2. **Architektura:** `python3 scripts/check-architecture.py` → PASS zawsze.
3. **Kotwice:** każdy test C# (`tests/*.cs`) sprawdza ZAWARTOŚĆ plików — zmieniając
   plik, sprawdź kotwice grepn ZANIM commitniesz. Audyt kotwic uruchamiaj lokalnie.
4. **Commit+push jednym bashem:** fetch → reset --soft FETCH_HEAD → add -A →
   diff vs FETCH_HEAD (tylko Twoja praca!) → commit → push. Obcy commity: buduj NA nich.
5. **CI ~215 s:** Build + Smoke. Czerwone → `gh api …/check-runs/<id>/annotations` →
   napraw → push. Po zielonym: potwierdź, że packaging/EXE steps są `skipped`
   (chyba że wydano JAWNY rozkaz budowy EXE — wtedy użyj wydzielonego workflow).
6. **Dokumenty po każdej zmianie:** BACKLOG, RELEASE, ten plik, README-FORGE/MONEY.

---

## 9. PRZEPISY — szybkie zadania krok po kroku

**„Zrób nową grę X”:** sekcja 4 (10 kroków). Czas: jeden commit, 44+ zestawy regresji.
**„Dodaj przedmiot do sklepu”:** wpis w `Pack.Shop` (+`OnBuy` dla special) + kotwica.
**„Nowe zwierzaki”:** nowe jajko w `GameConfig.Eggs` (wagi sumują się do 100) + kotwica.
**„Nowy event globalny”:** wpis w `Rotations` (BoostService obsługuje `coins`/`gift`).
**„Nowy quest”:** wpis w `Pack.QuestPool` (`type = "stat:Klucz"|"coins"|"playtime"`).
**„Popraw UI”:** UiKit only; toasty/sprężyny/konfetti — nie wymyślaj nowego stylu.

---

## 10. CZEGO NIGDY NIE ROBIMY

- Fałszywych liczników, udawanych zakupów, zmyślonych procentów.
- `math.random()` bez seeda w budowaniu świata (determinizm!).
- Globali zamiast `deps` (moduł gatunku ma działać w izolacji testów).
- Inline `//`, em-dash i cyrylicy W KODZIE Lua (parser i CI to wyłapują).
- Pomijania hooka `OfflineRatePerHour` (usługa offline musi działać w każdym gatunku).
- Budowania EXE/instalatorów bez JAWNEGO rozkazu (bramka packaging nietknięta).
- Twierdzeń „wszystko działa” bez zielonego CI + lokalnych bramek z sekcji 8.

*Ostatnia aktualizacja: 0.99 (10 gatunków, 4 jajka, 14 dni nagród, 5 eventów globalnych,
4 oferty, sezon 40 poziomów, offline 10 h).*
