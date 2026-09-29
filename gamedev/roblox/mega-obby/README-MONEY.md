# MEGA OBBY / GAMEFORGE — PLAYBOOK MONETYZACJI („żeby z tego zarobić”)

Gra ma wbudowane **systemy przychodu i retencji**, które napędzają prawdziwe gry
z TOP zarabiających na Roblox. Wszystkie działają na wszystkich 6 gatunkach
GameForge. Żeby realnie zarabiać, potrzebujesz trzech rzeczy: **graczy (retencja),
powodu, by zostać (pętle), i uczciwych ofert (przychód)** — dokładnie w tej
kolejności. Zero sztuczek: żadnych fałszywych liczników, zmyślonych przecen ani
ukrytych szans (to i tak banują na Roblox).

## 1. Co jest wbudowane i po co (mapa przychodu)

| System | Plik | Co daje |
|---|---|---|
| **Zestawy limitowane** (STARTER 72 h / MEGA 7 dni) | `OfferService` + zakładka OFERTY | Najwyższa konwersja: jednorazowe pakiety z REALNYM licznikiem od pierwszego wejścia. Klasyczny „starter pack” to zwykle 30–50% przychodu gry. |
| **Koło fortuny** (1 darmowy/dzień + extra za Robux) | `SpinService` + zakładka SPIN | Codzienny powrót (retencja D1/D7) + **jawne szanse** w UI (wymóg Roblox dla płatnych losowań — masz to z głowy). |
| **SEZON (battle pass)** 40 poziomów, darmowy + premium tor | `BattlePassService` + zakładka SEZON | XP leci samo za granie; premium tor kupuje najwierniejsza grupa. Stabilny, przewidywalny przychód co sezon. |
| **Zarobki offline** + „podwój za Robux” (raz/dobę) | `OfflineEarningsService` | Powód, by wrócić („dopisało mi monety!”) + mikropłatność bez FOMO. Stawka rośnie z postępem gatunku (hook `OfflineRatePerHour`). |
| **Boosty ×2/×3/×5 i GLOBALNE eventy (5 typów: x2 długie, x3, x5 krótkie, DAR 250, MEGADAR 500)** | `BoostService` | Event serwerowy losowany z jawnych wag = gracze wzywają znajomych (organiczny zasięg). Boost osobisty ze spinów/questów/sezonu. |
| **Bonus grupy +10%** | `GroupBonusService` | Rosnąca grupa = darmowy marketing każdej aktualizacji (ogłoszenia do członków). |
| **Onboarding 5 kroków** z nagrodami | `OnboardingService` | Uczy sklepu w pierwszych 10 minutach — gracz, który kupi raz, kupi znów. |
| **Przepustki i produkty bazowe** (x2, VIP, pakiety monet) | `MonetizationService` | Fundament — opis w README-GRA.md (sekcja monetyzacja). |

## 2. Ustawienie (30 minut po publikacji)

1. Creator Dashboard → **Monetization**.
2. Stwórz produkty deweloperskie (Dev Products) i wpisz ID do
   `src/Shared/GameConfig.lua`:
   - `coins_small/medium/big`, `egg_premium`, `skip_stage` (już było),
   - `spin_extra` (koło), `offline_double` (podwojenie), `battlepass_premium` (sezon).
3. Stwórz **dwa produkty na zestawy** i wpisz ID do `GameConfig.Offers`
   (`starter`, `mega`) — to NIE są pozycje z DevProducts, mają własne ID.
4. (Opcjonalnie) przepustka SEZON PREMIUM jako Game Pass → `PremiumGamepassId`.
5. Wpisz `GameConfig.Group.Id` swojej grupy — bonus +10% włączy się sam.

## 3. Cennik, który działa na Roblox (punkty odniesienia)

| Pozycja | Sugerowana cena | Dlaczego |
|---|---|---|
| Zestaw STARTER | **99 R$** | Próg pierwszego zakupu; wartość 3–4× wyższa od equiv. paku monet — gracz to czuje. |
| MEGA ZESTAW | **299–499 R$** | Dla zaangażowanych; etykieta „DLA SZYBKICH”. |
| SEZON PREMIUM | **799 R$** | Standard rynkowy battle passów; tor premium zwraca część ceny w monetach/spinach (uczciwa pętla). |
| Extra spin | **49 R$** | Mikropłatność impulsowa przy dobrym wyniku darmowego spinu. |
| Podwojenie offline | **29–49 R$** | Raz na dobę — bez nachalności. |
| Pakiety monet | 99 / 199 / 499 R$ | Schody cenowe (wyższe = lepszy przelicznik). |

Robux → USD: **0,0035 USD/R$**, min. wypłata 30 000 R$ (DevEx), a prowizja
Roblox to 30% — `roblox monetyzacja: 35000` w SentinelX przeliczy to za ciebie.

## 4. Retencja = przychód (kolejność ma znaczenie)

1. **Minuta 0–10**: onboarding (5 kroków z nagrodami) → gracz rozumie grę i sklep.
2. **Dzień 1**: darmowy spin + questa + offline → 3 powody wrócić.
3. **Dzień 2–14**: nagroda dzienna (14-dniowy tor: dzień 10 +spin, dzień 14 +2 spiny i zwierzak), sezon leci samo, eventy globalne co 15 min.
4. **Tydzień 2+**: sezon premium kupują najmocniej zaangażowani — i to im dajesz
   najwięcej wartości (tor premium, spin w L5/L10/…).

## 5. Zasady uczciwości (chronią twoje konto)

- **Szanse losowań jawne** — wagi z `GameConfig.Spin.Rewards` renderują się w UI
  jako procenty. Płatne losowanie bez podanych szans = ryzyko bana.
- **Liczniki ofert prawdziwe** — STARTER naprawdę znika 72 h od pierwszego
  wejścia (serwer pilnuje po `FirstJoinAt`); nie resetuje się.
- **Zakupy nie przepadają** — paragon idempotentny, natychmiastowy zapis po
  grancie (`PurchaseGranted`/`NotProcessedYet` wg stanu).
- **Zero pay-to-win ekstremalnego** — kupujesz wygodę, kosmetykę i tempo;
  umiejętność (obby/horror/shooter) zostaje graczowi.
- ID = 0 → klient uczciwie mówi „twórca musi podpiąć ID”, nie udaje zakupu.

## 6. Jak mierzyć (bez zewnętrznych narzędzi)

- `!coins` — nie nadużywaj; do testów wpisz siebie w `GameConfig.Admins.UserIds`.
- Sezon/spiny/oferty piszą w konsoli serwera (`F9` w Studio) — patrz logi
  `[MegaObby]` przy starcie i grantach.
- W Creator Dashboard → Analytics obserwuj: **D1/D7 retention**, ARPDAU i
  konwersję — po włączeniu powyższych systemów retencja ma pójść w górę
  zanim przychód (to normalna kolejność).
