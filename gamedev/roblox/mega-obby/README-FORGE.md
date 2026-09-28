# GAMEFORGE — wpisujesz JAKĄ grę chcesz, silnik ją generuje

To nie jest „gra obby". To **generator gier**: jedna linijka w `GameSpec.lua`
wybiera gatunek, a serwer buduje CAŁĄ grę od zera — świat, pętlę rozgrywki,
sklep, questa i ekonomię. Obby to tylko jeden z sześciu gatunków.

## Jak wygrać inną grę (60 sekund)

Otwórz `src/Shared/GameSpec.lua` i zmień dwie linijki:

```lua
GameSpec.Name  = "Pizzeria Imperium"  -- twoja nazwa
GameSpec.Genre = "tycoon"             -- ← TO zmienia całą grę
```

Wgraj do Studio (README-GRA.md: Rojo albo ręcznie) → **Play**. Gotowe:
generuje się działka z dozownikami kropel, przyciski ulepszeń, sklep trybu,
questa dnia, tablice TOP-10 liczące krople. Zmieniasz `Genre` na `"horror"`
i ta sama gra staje się ciemnym budynkiem z latarką, bezpiecznikami i
goniącym potworem.

## Gatunki (i co dokładnie się generuje)

| `GameSpec.Genre` | Alias | Świat i pętla generowana z kodu |
|---|---|---|
| `obby` | parkour, przeszkody | lobby + **120 etapów** na 6 wyspach, checkpointy, killbricki, platformy ruchome, wiry, sprężyny, portal, finisz, jajka ze zwierzakami |
| `symulator` | simulator, clicker | plac + **3 strefy treningowe** (progi siły, aury monet), klik z limitem, **automatyczne treningi**, NARODZINY (reset siły za ×1,5 mnożnika) |
| `tycoon` | biznes, fabryka | **4 działki** do przejęcia, dozowniki kropel → kolektory = monety, **przyciski ulepszeń** (szybkość/wartość/drugi dozownik), sklep trybu |
| `horror` | przetrwanie | **ciemny budynek** z losowym labiryntem (ten sam seed dla wszystkich), latarka z baterią, **3 bezpieczniki**, otwierane drzwi, **goniący potwór** (przyspiesza po każdej ucieczce) |
| `shooter` | strzelanka, fps | arena z osłonami, **6 ruchomych celów**, strzał = **raycast na serwerze** (cooldown/zasięg z ulepszeń), statystyki |
| `wyscigi` | racing, samochody | **tor owalny**, 8 checkpointów w kolejności, okrążenia z nagrodami, doładowania prędkości, stałe ulepszenie |

W SentinelX nie musisz nawet znać pliku: wpisz w czacie
**`roblox wygeneruj: tycoon pizzeria`** — rozpozna gatunek i poda gotowy
`GameSpec` do wklejenia.

## Dostrajanie (opcjonalne)

```lua
GameSpec.Knobs = {
	horror    = { MonsterSpeed = 14 },   -- szybszy potwór
	symulator = { ClickPower = 5 },      -- mocniejszy start
	wyscigi   = { LapReward = 250 },     -- hojniejsze okrążenia
}
```

## Co WSPÓLNE dla każdego gatunku (silnik)

- monety, zakupy i ceny liczone **wyłącznie na serwerze**,
- idempotentny `ProcessReceipt` (jeden na grę), przepustki + produkty,
- questa dnia (w tym **statystyczne**: „zdobądź 10 000 siły" — liczone na żywo),
- nagrody 7-dniowe, tablice TOP-10 (wartość lidera wg gatunku: siła / krople / ucieczki / zestrzelenia / okrążenia),
- ślady z lobby, anty-cheat (prędkość/teleport), komendy admina, tryb offline w Studio.

## Architektura generatora

```
src/Shared/GameSpec.lua          ← TY wpisujesz tu (nazwa + gatunek)
src/Server/Modules/Forge.lua     ← dystrybutor: czyta Genre, wybiera moduł gatunku
src/Server/Genres/*.lua          ← 6 modułów gatunków (świat + pętla + sklep + questa)
src/Server/Modules/ForgeShopService.lua ← uniwersalny sklep gatunku (ForgeBuy)
src/Client/Controllers/HudController.lua ← panel statystyk gatunku (snapshot.Hud)
src/Client/Controllers/ShopUI.lua        ← zakładka „SKLEP TRYBU” (snapshot.ShopItems)
```

Kontrakt modułu gatunku: `BuildWorld(folders) → { SpawnPad }`, `Setup(deps, world)`,
`Hud`, `Shop`, `QuestPool`, `LeaderValue`, opcjonalnie `OnBuy`, `OnCharacter`,
`MonetizationGrants`, `SkipAmbient`. Dodanie WŁASNEGO gatunku = jeden nowy plik
w `Genres/` + wpis w rejestrze `Forge.lua`.
