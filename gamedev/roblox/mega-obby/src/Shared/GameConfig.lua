--!strict
-- ============================================================================
-- MEGA OBBY: Wyspy Przygód — wspólna konfiguracja (ReplicatedStorage.Shared.GameConfig)
-- Wszystkie stałe gry w JEDNYM miejscu: zmieniasz tutaj, działa na serwerze i kliencie.
-- ID przepustek/produktów (0 = placeholder) ustawiasz PO publikacji w Roblox —
-- patrz README-GRA.md, sekcja „Monetyzacja krok po kroku”.
-- ============================================================================

local GameConfig = {}

GameConfig.GameName = "MEGA OBBY: Wyspy Przygód"
GameConfig.Version = "1.0.0"

-- ————————————————————————————————————————————————
-- Statystyki widoczne w tabelce gracza (leaderstats)
-- ————————————————————————————————————————————————
GameConfig.Leaderstats = {
	Coins = "Monety",
	Stage = "Etap",
}

-- ————————————————————————————————————————————————
-- Wyspy: 6 wysp × 20 etapów = 120 etapów + meta (finish).
-- difficulty = mnożnik trudności geometrii (odstępy, wielkość platform, pułapki).
-- ————————————————————————————————————————————————
GameConfig.Islands = {
	{ id = "meadow", name = "Łąki Startowe",    from = 1,   to = 20,  color = { 106, 190, 82 },  difficulty = 1.00 },
	{ id = "desert", name = "Pustynia Skał",    from = 21,  to = 40,  color = { 222, 184, 105 }, difficulty = 1.15 },
	{ id = "ice",    name = "Zamarznięte Groty", from = 41,  to = 60,  color = { 168, 220, 240 }, difficulty = 1.30 },
	{ id = "lava",   name = "Wulkaniczne Kręgi", from = 61,  to = 80,  color = { 220, 90, 60 },  difficulty = 1.50 },
	{ id = "sky",    name = "Wyspy Nieba",       from = 81,  to = 100, color = { 150, 190, 255 }, difficulty = 1.70 },
	{ id = "cyber",  name = "Kodowa Strefa",     from = 101, to = 120, color = { 120, 240, 230 }, difficulty = 1.90 },
}
GameConfig.TotalStages = 120
GameConfig.FinishStage = 121 -- pad FINISZ (po 120. etapie)

-- Nazwy etapów — losowane z puli danej wyspy (siato: seed = numer etapu, więc
-- każdy gracz widzi TE SAME nazwy — spójny świat, zero „zmyślania”).
GameConfig.StageNames = {
	meadow = { "Pierwsze Kroki", "Ścieżka Marzeń", "Most Kucyków", "Zielone Skoki", "Rowerowa Aleja",
		"KWIATKOWY ZAKĄTEK", "Dębowy Most", "Staw Żabki", "Wiatraczki", "Przeskocz Strumyk",
		"Las Szyszek", "Grzybkowa Polana", "Kolorowe Kółka", "Skok w Trawę", "Wesoły Tor",
		"Przystanek Motyl", "Dolina Mchu", "Przystań Balonów", "Zielony Finisz", "Wielki Meadow" },
	desert = { "Gorący Piasek", "Kaktusowy Zakątek", "Sfinks Laduje", "Wydmy", "Oaza Skoków",
		"SKALNY LABIRYNT", "Karawana", "Zagubiona Piramida", "Piaskowy Mistrz", "Jaskinia Skorpionów",
		"Przepaść Piasku", "Złote Kości", "Pustynny Wiatr", "Zatoka Krokodyla", "Mirage",
		"Skarbiec Faraona", "Płonące Słońce", "Dolina Mumii", "Ostatnia Oaza", "Wielki Desert" },
	ice = { "Pierwszy Lodowiec", "Ślizgawka", "Bałwanki", "Zamarznięty Most", "Pingwinia Ścieżka",
		"KRYSTALOWA JASKINIA", "Zamieć", "Sople Grozy", "Lodowy Szpikulec", "Śnieżna Burza",
		"Góra Lodowa", "Biały Wilk", "Zorza", "Igloo Przystanek", "Szron",
		"Lodowa Przepaść", "Zimne Płuca", "Kryształowy Finisz", "Wieczne Zimy", "Biało-Czarny Lód" },
	lava = { "Pęknięcie", "Kamienne Kroki", "Lawowe Fontanny", "Wulkaniczny Most", "Popiół",
		"OGNISTA KULA", "Krater", "Skoki po Skałach", "Cień Smoka", "Rozgrzana Ziemia",
		"Magmowa Rzeka", "Żar", "Dymne Korytarze", "Obsydian", "Pierścień Ognia",
		"Strumień Lavy", "Gorący Szczyt", "Wybuch", "Serduszko Wulkanu", "Wielka Erukcja" },
	sky = { "Pierwszy Obłok", "Skrzydła", "Podniebny Most", "Wiatr", "Balonowa Alejka",
		"CHMURA W GĘSTEJ MGLE", "Deszcz Gwiazd", "Podniebne Wyspy", "Orli Lot", "Nimbostratus",
		"Tęczowa Łąka", "Zefirek", "Latarnie Nieba", "Chmurny Zakątek", "Wysoki Wzlot",
		"Skok do Burzy", "Piorunowa Aleja", "Anielskie Schody", "Krawędź Nieba", "Panie Nieba" },
	cyber = { "Boot", "Pierwszy Skrypt", "Neonowy Most", "Dane", "Serwery",
		"FIREWALL", "Kompilacja", "Procesor", "Pamięć RAM", "Kod Błędu 404",
		"Szkielet Programu", "Algorytm", "Bufor", "Karta Graficzna", "Zasilanie",
		"Ostatni Binarny", "Root", "Kernel", "Wielka Konsola", "MegaOBBY.exe" },
}

-- ————————————————————————————————————————————————
-- Ekonomia: monety
-- ————————————————————————————————————————————————
GameConfig.Coins = {
	PickupValue = 5,                          -- pojedyncza moneta z toru
	StageRewardBase = 10,                     -- nagroda za NOWY etap
	StageRewardPerIslandStep = 2,             -- +2 za każdą wyspę (im dalej, tym więcej)
	FinishBonus = 1000,                       -- przejście całości
	CoinRespawnSeconds = 20,                  -- moneta wraca po zebraniu
	RainIntervalSeconds = 600,                -- deszcz monet w lobby co 10 minut
	RainCount = 40,
	RainLifetimeSeconds = 90,
	DuplicatePetRefund = 150,                 -- powtórzony zwierzak → monety
}

-- ————————————————————————————————————————————————
-- Sklep: ślady (trail) za monety. price = 0 i StartingOwned = własność startowa.
-- VIPId = wymaga przepustki VIP. Colors = RGB listy dla sekwencji koloru Trail.
-- ————————————————————————————————————————————————
GameConfig.Trails = {
	{ id = "trail_basic",   name = "Klasyczny",      price = 0,    colors = { { 255, 255, 255 } } },
	{ id = "trail_red",     name = "Czerwony błysk", price = 300,  colors = { { 255, 80, 80 } } },
	{ id = "trail_blue",    name = "Błękit fali",    price = 300,  colors = { { 80, 160, 255 } } },
	{ id = "trail_neon",    name = "Neon",           price = 900,  colors = { { 0, 255, 170 }, { 0, 120, 255 } } },
	{ id = "trail_rainbow", name = "Tęcza",          price = 1200, colors = { { 255, 60, 60 }, { 255, 220, 60 }, { 80, 255, 120 }, { 80, 160, 255 }, { 200, 80, 255 } } },
	{ id = "trail_fire",    name = "Ogień",          price = 1500, colors = { { 255, 200, 0 }, { 255, 80, 0 } } },
	{ id = "trail_ice",     name = "Lód",            price = 1500, colors = { { 180, 240, 255 }, { 90, 180, 255 } } },
	{ id = "trail_shadow",  name = "Cień",           price = 2000, colors = { { 30, 30, 40 }, { 80, 40, 120 } } },
	{ id = "trail_stardust",name = "Gwiezdny pył",   price = 2500, colors = { { 255, 255, 200 }, { 200, 160, 255 }, { 255, 255, 255 } } },
	{ id = "trail_gold",    name = "Złoty (VIP)",    price = 0,    vipOnly = true, colors = { { 255, 215, 0 }, { 255, 240, 150 } } },
	{ id = "trail_toxic",   name = "Toksyczny",      price = 2200, colors = { { 120, 255, 60 }, { 40, 160, 30 } } },
	{ id = "trail_cyber",   name = "Cyberpunk",      price = 3000, colors = { { 255, 40, 180 }, { 40, 220, 255 }, { 120, 40, 255 } } },
}

-- ————————————————————————————————————————————————
-- Jajka i zwierzaki. Chance = waga losowania (nie %, proporcje).
-- Bonus = mnożnik monet; MagnetRadius = auto-zbieranie monet w promieniu (stud).
-- Rarity wpływa tylko na kolor/cenę duplikatu i rary w UI.
-- ————————————————————————————————————————————————
GameConfig.Eggs = {
	{
		id = "egg_basic", name = "Zwykłe Jajko", price = 500, currency = "Coins",
		pets = {
			{ id = "pet_krab",   name = "Krab",       rarity = "Częsty",      weight = 30, bonus = 1.05, magnetRadius = 0,  color = { 255, 120, 90 } },
			{ id = "pet_panda",  name = "Panda",      rarity = "Częsty",      weight = 26, bonus = 1.05, magnetRadius = 0,  color = { 240, 240, 240 } },
			{ id = "pet_lis",    name = "Lis",        rarity = "Rzadki",      weight = 20, bonus = 1.10, magnetRadius = 6,  color = { 255, 140, 40 } },
			{ id = "pet_wilk",   name = "Wilk",       rarity = "Rzadki",      weight = 14, bonus = 1.12, magnetRadius = 6,  color = { 130, 130, 140 } },
			{ id = "pet_duch",   name = "Duch",       rarity = "Epicki",      weight = 7,  bonus = 1.20, magnetRadius = 8,  color = { 200, 220, 255 } },
			{ id = "pet_smok",   name = "Smok",       rarity = "Legendarny",  weight = 3,  bonus = 1.35, magnetRadius = 12, color = { 180, 40, 220 } },
		},
	},
	{
		id = "egg_sky", name = "Niebiańskie Jajko", price = 2500, currency = "Coins",
		pets = {
			{ id = "pet_kot",     name = "Latający Kot",  rarity = "Częsty",     weight = 28, bonus = 1.10, magnetRadius = 0,  color = { 255, 220, 120 } },
			{ id = "pet_orzel",   name = "Orzeł",         rarity = "Rzadki",     weight = 22, bonus = 1.15, magnetRadius = 8,  color = { 160, 120, 70 } },
			{ id = "pet_robot",   name = "Robot",         rarity = "Rzadki",     weight = 20, bonus = 1.18, magnetRadius = 8,  color = { 120, 240, 230 } },
			{ id = "pet_jednorożec", name = "Jednorożec", rarity = "Epicki",     weight = 12, bonus = 1.25, magnetRadius = 10, color = { 255, 170, 230 } },
			{ id = "pet_feniks",  name = "Feniks",        rarity = "Epicki",     weight = 10, bonus = 1.28, magnetRadius = 10, color = { 255, 120, 0 } },
			{ id = "pet_golem",   name = "Golem Kodu",    rarity = "Legendarny", weight = 4,  bonus = 1.45, magnetRadius = 14, color = { 40, 200, 120 } },
		},
	},
	{
		id = "egg_wulkan", name = "Wulkaniczne Jajko", price = 6000, currency = "Coins",
		pets = {
			{ id = "pet_osiemnastka", name = "Płonący Krab",  rarity = "Częsty",     weight = 30, bonus = 1.12, magnetRadius = 4,  color = { 255, 100, 40 } },
			{ id = "pet_popiol",  name = "Duch Popiołu",      rarity = "Częsty",     weight = 26, bonus = 1.14, magnetRadius = 6,  color = { 130, 120, 120 } },
			{ id = "pet_bazalt",  name = "Bazaltowy Wilk",    rarity = "Rzadki",     weight = 20, bonus = 1.20, magnetRadius = 8,  color = { 60, 55, 60 } },
			{ id = "pet_lawa",    name = "Salamandra Lawy",   rarity = "Rzadki",     weight = 14, bonus = 1.22, magnetRadius = 9,  color = { 255, 150, 0 } },
			{ id = "pet_smokognia", name = "Smok Ognia",      rarity = "Epicki",     weight = 7,  bonus = 1.32, magnetRadius = 12, color = { 255, 60, 0 } },
			{ id = "pet_fenikspro", name = "Protopan Feniks", rarity = "Legendarny", weight = 3,  bonus = 1.55, magnetRadius = 16, color = { 255, 220, 60 } },
		},
	},
	{ id = "egg_lodowe", name = "Lodowe Jajko", price = 9000, currency = "Coins",
		pets = {
			{ id = "pet_pingwin",   name = "Lodowy Pingwin",     rarity = "Częsty",     weight = 30, bonus = 1.12, magnetRadius = 4,  color = { 170, 220, 255 } },
			{ id = "pet_foka",      name = "Morska Foka",        rarity = "Częsty",     weight = 26, bonus = 1.14, magnetRadius = 5,  color = { 200, 230, 245 } },
			{ id = "pet_lodowlis",  name = "Lodowy Lis",         rarity = "Rzadki",     weight = 20, bonus = 1.20, magnetRadius = 8,  color = { 140, 200, 240 } },
			{ id = "pet_niedzwiedz", name = "Niedźwiedź Polarny", rarity = "Rzadki",    weight = 14, bonus = 1.22, magnetRadius = 9,  color = { 225, 235, 245 } },
			{ id = "pet_kraken",    name = "Kraken Lodu",        rarity = "Epicki",     weight = 7,  bonus = 1.34, magnetRadius = 12, color = { 90, 160, 220 } },
			{ id = "pet_krolowazimy", name = "Królowa Zimy",     rarity = "Legendarny", weight = 3,  bonus = 1.60, magnetRadius = 16, color = { 240, 250, 255 } },
		},
	},
}

-- ————————————————————————————————————————————————
-- Monetyzacja — ID USTAWIASZ PO PUBLIKACJI (0 = placeholder, UI wtedy tłumaczy,
-- że twórca musi jeszcze podpiąć ID — patrz README-GRA.md).
-- ————————————————————————————————————————————————
GameConfig.Gamepasses = {
	{ id = "x2coins", name = "x2 Monety",          robuxProductId = 0, description = "Zawsze podwójne monety." },
	{ id = "speed25", name = "+25% Szybkości",     robuxProductId = 0, description = "Stałe szybsze bieganie." },
	{ id = "vip",     name = "VIP",                robuxProductId = 0, description = "Złoty ślad + 2 zwierzaki naraz + ranga VIP." },
}
GameConfig.DevProducts = {
	{ id = "coins_small", name = "500 Monet",        robuxProductId = 0 },
	{ id = "coins_medium", name = "2 000 Monet",     robuxProductId = 0 },
	{ id = "coins_big", name = "6 000 Monet",        robuxProductId = 0 },
	{ id = "egg_premium", name = "Rzut Premium Jajkiem", robuxProductId = 0 },
	{ id = "skip_stage", name = "Pomiń Etap",        robuxProductId = 0 },
}

-- ————————————————————————————————————————————————
-- Zadania (dzienne): 3 losowane dziennie z puli (seed = userId + dzień UTC).
-- Type: "coins" (zbierz monet), "stages" (nowe etapy), "playtime" (minuty gry).
-- ————————————————————————————————————————————————
GameConfig.QuestPool = {
	{ id = "q_coins_100",  type = "coins",    target = 100, reward = 250,  desc = "Zbierz 100 monet" },
	{ id = "q_coins_300",  type = "coins",    target = 300, reward = 600,  desc = "Zbierz 300 monet" },
	{ id = "q_coins_800",  type = "coins",    target = 800, reward = 1500, desc = "Zbierz 800 monet" },
	{ id = "q_stages_3",   type = "stages",   target = 3,   reward = 300,  desc = "Zalicz 3 nowe etapy" },
	{ id = "q_stages_8",   type = "stages",   target = 8,   reward = 800,  desc = "Zalicz 8 nowych etapów" },
	{ id = "q_stages_15",  type = "stages",   target = 15,  reward = 1800, desc = "Zalicz 15 nowych etapów" },
	{ id = "q_time_15",    type = "playtime", target = 15,  reward = 200,  desc = "Zagraj 15 minut" },
	{ id = "q_time_40",    type = "playtime", target = 40,  reward = 500,  desc = "Zagraj 40 minut" },
	{ id = "q_coins_1500", type = "coins",    target = 1500, reward = 3000, desc = "Zbierz 1 500 monet" },
	{ id = "q_time_90",    type = "playtime", target = 90,  reward = 1100, desc = "Zagraj 90 minut" },
	{ id = "q_stages_30",  type = "stages",   target = 30,  reward = 3600, desc = "Zalicz 30 nowych etapów" },
}
GameConfig.DailyQuestCount = 3

-- Nagrody dzienne (streak cykliczny 7 dni)
GameConfig.DailyRewards = {
	{ day = 1, coins = 150 },
	{ day = 2, coins = 250 },
	{ day = 3, coins = 400, spins = 1 },
	{ day = 4, coins = 600 },
	{ day = 5, coins = 850, spins = 1 },
	{ day = 6, coins = 1200 },
	{ day = 7, coins = 2000, spins = 2, pet = true },
	{ day = 8, coins = 2400 },
	{ day = 9, coins = 2700 },
	{ day = 10, coins = 3000, spins = 1 },
	{ day = 11, coins = 3300 },
	{ day = 12, coins = 3700 },
	{ day = 13, coins = 4300, spins = 1 },
	{ day = 14, coins = 5000, spins = 2, pet = true },
}

-- ————————————————————————————————————————————————
-- Anty-cheat (heurystyki serwera — patrz AntiCheatService)
-- ————————————————————————————————————————————————
GameConfig.AntiCheat = {
	SampleSeconds = 0.5,        -- co ile sekund próbkowanie pozycji
	SpeedFactor = 2.2,          -- dozwolone ~2.2× bieżącej prędkości chodu
	MinSpeedStuds = 34,         -- poniżej tej prędkości nie karzemy (spadanie itd.)
	TeleportStuds = 130,        -- skok pozycji między próbkami = podejrzenie teleportu
	StrikeLimit = 3,            -- tyle uderzeń → wyrzucenie
	DecaySeconds = 60,          -- pojedyncze „strike” gasi się po minucie
}

-- ————————————————————————————————————————————————
-- Ambiens: cykl dnia i strefy muzyki (assetId 0 = cisza — brak wgranego dźwięku)
-- ————————————————————————————————————————————————
GameConfig.Ambient = {
	DayLengthSeconds = 720,     -- pełna doba w grze
	StartClockTime = 9.5,
	Music = {
		lobby = { name = "Lobby", assetId = 0 },
		meadow = { name = "Łąki", assetId = 0 },
		desert = { name = "Pustynia", assetId = 0 },
		ice = { name = "Lody", assetId = 0 },
		lava = { name = "Wulkan", assetId = 0 },
		sky = { name = "Niebo", assetId = 0 },
		cyber = { name = "Cyber", assetId = 0 },
	},
}

-- ————————————————————————————————————————————————
-- Admini: UserIds z whitelisty. Grupa opcjonalna (rank >= MinRank też admin).
-- Komendy na czacie: !coins n | !stage n | !rain | !announce tekst | !kick gracz | !help
-- ————————————————————————————————————————————————
GameConfig.Admins = {
	UserIds = {},          -- np. { 123456789 }
	Group = { Id = 0, MinRank = 254 }, -- 0 = wyłączone
	ChatPrefix = "!",
}

-- ————————————————————————————————————————————————
-- MONETYZACJA PRO (0.99): oferty limitowane, spin dobowy, battle pass,
-- zarobki offline, bonus grupy, globalne eventy x3, onboarding.
-- ID = 0 do czasu publikacji (UI uczciwie tłumaczy — README-MONEY.md).
-- ————————————————————————————————————————————————
GameConfig.Offers = {
	{ id = "starter", name = "ZESTAW STARTOWY", robuxProductId = 0,
		coins = 1500, spins = 3, label = "NAJLEPSZA WARTOŚĆ",
		expireHoursFromJoin = 72, -- realny licznik: 3 doby od pierwszego wejścia
		description = "1500 monet + 3 spiny. Widoczny tylko pierwsze 72 h gry." },
	{ id = "mega", name = "MEGA ZESTAW", robuxProductId = 0,
		coins = 6000, spins = 8, label = "DLA SZYBKICH",
		expireHoursFromJoin = 168,
		description = "6000 monet + 8 spinów. Znika po tygodniu gry." },
	{ id = "weteran", name = "POWROT WETERANA", robuxProductId = 0,
		coins = 9000, spins = 10, label = "DLO WIELKICH",
		expireHoursFromJoin = 120, -- widać go od 7. doby gry, znika 5 h po otwarciu
		description = "9000 monet + 10 spinów. Nagroda za wierność — tylko dla graczy z stażem 7+ dni." },
	{ id = "sezonowy", name = "PAKIET SEZONOWY", robuxProductId = 0,
		coins = 12000, spins = 14, label = "PELNY SEZON",
		expireHoursFromJoin = 96, -- od 14. doby, okno 4 dni
		description = "12000 monet + 14 spinów. Ostatnie wielkie okno sezonu — od 14. dnia gry." },
}

GameConfig.Spin = {
	FreePerDay = 1,
	PaidProductId = 0, -- produkt deweloperski: dodatkowy spin
	-- wagi = widoczne szanse (Roblox wymaga pokazywania szans dla PŁATNYCH losowań)
	Rewards = {
		{ type = "coins",  amount = 150,  weight = 40, label = "150 monet" },
		{ type = "coins",  amount = 400,  weight = 25, label = "400 monet" },
		{ type = "boost",  amount = 600,  weight = 20, label = "BOOST x2 (10 min)" },
		{ type = "coins",  amount = 1000, weight = 10, label = "JACKPOT 1000!" },
		{ type = "pet",    weight = 5,    label = "LOSOWANIE ZWIERZAKA" },
	},
}

GameConfig.BattlePass = {
	Tiers = 40, -- dłuższy sezon: więcej celu dla graczy
	XpPerTier = 250,
	PremiumProductId = 0,   -- produkt: premium track na sezon
	PremiumGamepassId = 0,  -- alternatywnie przepustka
	XpFor = { Coins = 0.05, Stage = 40, PlayMinute = 2 }, -- xp za monetę / etap / minutę
}

GameConfig.Offline = {
	MaxHours = 10,       -- tyle maksymalnie liczymy (uczciwy limit)
	BasePerHour = 40,    -- gatunek może nadpisać (Pack.OfflineRatePerHour)
	DoubleProductId = 0, -- produkt: „podwój zarobek offline” (raz na dobę)
}

GameConfig.Group = { Id = 0, BonusPercent = 10 } -- 0 = wyłączone

GameConfig.GlobalBoost = {
	IntervalSeconds = 900, -- co 15 min losowany event serwerowy
	Rotations = {
		{ kind = "coins", multiplier = 3, duration = 180, weight = 35 },
		{ kind = "coins", multiplier = 5, duration = 60, weight = 20 },
		{ kind = "gift", amount = 250, weight = 20 },
		{ kind = "coins", multiplier = 2, duration = 480, weight = 15 },
		{ kind = "gift", amount = 500, weight = 10 },
	},
}

GameConfig.Onboarding = {
	{ type = "playtime", amount = 60,  reward = 100, label = "Pobaw się minutę w swojej grze" },
	{ type = "coins",    amount = 50,  reward = 150, label = "Zarób 50 monet" },
	{ type = "spent",    amount = 200, reward = 250, label = "Wydaj 200 monet (ulepszenia!)" },
	{ type = "coins",    amount = 300, reward = 400, label = "Zarób łącznie 300 monet" },
	{ type = "playtime", amount = 600, reward = 500, label = "Zagraj 10 minut" },
}

-- ————————————————————————————————————————————————
-- Różne
-- ————————————————————————————————————————————————
GameConfig.Player = {
	BaseWalkSpeed = 16,
	SpeedPassMultiplier = 1.25,
	MaxPetsWithoutVip = 1,
	MaxPetsWithVip = 2,
	RespawnKey = Enum.KeyCode.R, -- klient: R = wróć na checkpoint
}

GameConfig.Ui = {
	AccentColor = { 34, 211, 238 },  -- cyjan (spójny z SentinelX SxAccentCyan)
	SuccessColor = { 90, 220, 120 },
	ErrorColor = { 255, 90, 90 },
	PanelColor = { 24, 28, 38 },
	StrokeColor = { 60, 70, 95 },
}

-- Pomocne: wyspa dla numeru etapu
function GameConfig.IslandOf(stage: number)
	for _, island in ipairs(GameConfig.Islands) do
		if stage >= island.from and stage <= island.to then
			return island
		end
	end
	return GameConfig.Islands[#GameConfig.Islands]
end

-- ============================================================================
-- RYBY (gatunek „ryby”): wagi rzadkości, gatunki ryb, mutacje, głębiny
-- ============================================================================
GameConfig.Fish = {
	WaitSeconds = { 3, 8 },
	RarityWeights = {
		{ name = "Wspólna", weight = 58 },
		{ name = "Niezwykła", weight = 25 },
		{ name = "Rzadka", weight = 11 },
		{ name = "Epicka", weight = 4.5 },
		{ name = "Legendarna", weight = 1.5 },
	},
	Species = {
		{ name = "Płoć", rarity = "Wspólna", value = 6 },
		{ name = "Karp Lustrzany", rarity = "Wspólna", value = 9 },
		{ name = "Leszcz", rarity = "Wspólna", value = 12 },
		{ name = "Szczupak", rarity = "Niezwykła", value = 22 },
		{ name = "Węgorz", rarity = "Niezwykła", value = 30 },
		{ name = "Sieja Królewska", rarity = "Rzadka", value = 65 },
		{ name = "Sum Olbrzymi", rarity = "Rzadka", value = 85 },
		{ name = "Marlin Szafirowy", rarity = "Epicka", value = 180 },
		{ name = "Rekin Głębin", rarity = "Epicka", value = 240 },
		{ name = "Złoty Koi", rarity = "Legendarna", value = 520 },
		{ name = "Prastary Król Stawu", rarity = "Legendarna", value = 800 },
		{ name = "Upiór Odległych Wód", rarity = "Legendarna", value = 1200, deep = true },
	},
	DeepBonus = 1.6,
	Mutations = {
		{ name = "Błyszczący", multiplier = 2, weight = 5 },
		{ name = "Ogromny", multiplier = 3, weight = 2 },
	},
}

-- POGODA: rotuje na serwerze; szczęście wzmacnia rzadkie ryby
GameConfig.Weather = {
	RotationSeconds = 180,
	Types = {
		{ id = "slonko", name = "☀️ Słonecznie", luck = 1, weight = 45 },
		{ id = "deszcz", name = "🌧️ Deszcz", luck = 1.25, weight = 30 },
		{ id = "burza", name = "⛈️ Burza", luck = 1.5, weight = 20 },
		{ id = "tecza", name = "🌈 Tęcza", luck = 2, weight = 5 },
	},
}

-- ZBIORY (indeks): odkrycie wpisu + nagroda za KOMPLET zestawu
GameConfig.Collections = {
	{ id = "ryby_staw", name = "Indeks: Staw", reward = 900,
		entries = { "Płoć", "Karp Lustrzany", "Leszcz", "Szczupak", "Węgorz", "Sieja Królewska", "Sum Olbrzymi" } },
	{ id = "ryby_glebiny", name = "Indeks: Głębiny", reward = 2500,
		entries = { "Marlin Szafirowy", "Rekin Głębin", "Złoty Koi", "Prastary Król Stawu", "Upiór Odległych Wód" } },
}

-- OSIĄGNIĘCIA: progi statystyk nagradzane raz — działają w KAŻDYM gatunku
GameConfig.Achievements = {
	{ id = "first_catch", stat = "Caught", threshold = 1, reward = 120, desc = "Złów pierwszą rybę" },
	{ id = "angler_50", stat = "Caught", threshold = 50, reward = 800, desc = "Złów 50 ryb" },
	{ id = "record_500", stat = "BestCatch", threshold = 500, reward = 1000, desc = "Złów rybę wartą 500+" },
	{ id = "runner_1500", stat = "BestDistance", threshold = 1500, reward = 900, desc = "Przebiegnij 1500 studów" },
	{ id = "farmer_100", stat = "Harvested", threshold = 100, reward = 850, desc = "Zbierz 100 plonów" },
	{ id = "defender_10", stat = "BestWave", threshold = 10, reward = 900, desc = "Dotrzyj do fali 10" },
	{ id = "stages_30", stat = "Stages", threshold = 30, reward = 500, desc = "Ukończ 30 etapów" },
}

return GameConfig
