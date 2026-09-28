--!strict
-- ============================================================================
-- GAMEFORGE — specyfikacja Twojej gry (ReplicatedStorage.Shared.GameSpec)
-- TO JEDYNY PLIK, KTÓRY EDYTUJESZ. Wpisz nazwę i gatunek — reszta dzieje się
-- sama: serwer zbuduje świat, pętlę rozgrywki, sklep i questy dla TEGO gatunku.
--
-- Genre (albo polskie/dowolne z aliasów):
--   "obby"       → tor przeszkód 120 etapów (klasyczny Mega Obby)
--   "symulator"  → klikaj → siła → automatyczne treningi → narodziny (rebirth)
--   "tycoon"     → działki z kropelkami, przyciski ulepszeń, fabryka monet
--   "horror"     → ciemny budynek, latarka z baterią, 3 bezpieczniki, potwór
--   "shooter"    → arena, strzelanie do celów, szybkostrzelność i zasięg
--   "wyscigi"    → tor owalny, checkpointy, okrążenia, doładowania prędkości
--   "biegacz"    → endless runner: tor z przeszkodami, rekord dystansu, kamienie milowe
--   "farma"      → nasiona, uprawy rosnące w czasie, zbiory = monety, działki premium
--   "fale"       → broń kryształu przed falami wrogów (fale rosną, bonus za obronę) · ryby
-- Aliasy też działają: "clicker", "biznes", "fabryka", "przetrwanie",
-- "strzelanka", "fps", "racing", "samochody", "parkour", "przeszkody",
-- "bieg", "runner", "rolnik", "sadzenie", "ogrod", "wave", "obrona", "zombie".
-- ============================================================================

local GameSpec = {}

-- ⬇⬇⬇ WPISZ TUTAJ SWOJĄ GRĘ ⬇⬇⬇
GameSpec.Name = "Mega Obby: Wyspy Przygód" -- nazwa Twojej gry
GameSpec.Genre = "obby"                     -- gatunek z listy powyżej
GameSpec.Theme = "wyspy"                    -- motyw (widoczny w HUD i nazwach)
-- ⬆⬆⬆ (domyślnie klasyczny obby — zmień Genre i masz INNĄ grę) ⬆⬆⬆

-- Opcjonalne dostrajanie gatunku (nadpisuje stałe z modułu gatunku):
-- GameSpec.Knobs = {
-- 	symulator = { ClickPower = 5 },
-- 	horror = { MonsterSpeed = 14 },
-- }

return GameSpec
