# 🧠 MÓZG SENTINELA — jak myśli, sprawdza, naprawia i przyznaje się do błędów

> Ten dokument to specyfikacja rozumowania Sentinela. Jeśli zmieniasz cokolwiek
> w `Core/Verification/`, `Services/Actions/ActionEngine.cs` albo w regułach
> weryfikacji — najpierw przeczytaj, potem zmień, a na końcu DOPISZ tutaj.
> No update = no pass.

---

## 1. CYKL ROZUMOWANIA — PLAN → PRÓBA → CHECK → NAPRAWA → WERDYKT

Każde polecenie przechodzi przez jeden tor (`ActionEngine.ExecuteAsync`):

```
PLAN      normalizacja polecenia, bramki (STOP, prywatność, głos nie zatwierdza)
PRÓBA 1   wykonanie przez router/narzędzia; każde narzędzie zostawia DOWÓD
NAMYSŁ    jeśli PRÓBA 1 padła i przyczyna wygląda na CHWIŁOWĄ → jedna PRÓBA 2
          (RetryAdvisor.TransientCause); trwałe błędy NIE są ponawiane
CHECK     VerificationCenter: niezależny sąd nad dowodami (post-kondycje)
NAPRAWA   jeśli sąd obalił VERIFIED → status spada do FAILED + RADA NAPRAWY
WERDYKT   VERIFIED (udowodnione) · UNVERIFIED (szczerze: nie potwierdzam) ·
          FAILED · CANCELLED — zawsze ze śladem w dowodach i księdze zdrowia
```

Ślad rozumowania trafia do `ActionRecord.ReasoningTrace` i do dowodów:
`PLAN: <polecenie> → CHECK: N reguł · ✔ wszystkie przeszły` (albo `✗ …`),
plus `PRÓBA 1 · niepowodzenie: …` gdy był namysł, plus `NAPRAWA: …` przy obaleniu.

---

## 2. NO SUCCESS = NO PASS — reguły sądu (VerificationCenter)

Reguły rejestrują się wg prefiksu typu akcji (`"*"` = wszystkie). Wyjątek
w regule liczy się PRZECIWKO sukcesowi. Domowe reguły:

| # | Reguła | Co obala |
|---|---|---|
| 1 | WERDYKT BEZ DOWODU | VERIFIED z pustym Evidence |
| 2 | LICZBA BEZ POMIARU | jednostka (%, GB, MB) bez żadnej cyfry |
| 3 | PRZEKONYWAJĄCY WERDYKT | VERIFIED z „błąd/nie udało się” w treści |
| 4 | SEKWENCJA NIEDOKOŃCZONA | WORKFLOW z < 2 narzędziami |
| 5 | CZĘŚCIOWA PRAWDA | WORKFLOW łączący VERIFIED i FAILED |
| 6 | POMIAR BEZ LICZBY W DOWODZIE | `MEASURE_*` z liczbą tylko w komunikacie, nie w dowodzie |
| 7 | DOWÓD ZBYT UBOGI | `OPEN_*`/`CLOSE_*` z dowodem krótszym niż 8 znaków |
| 8 | SPRZĄTANIE BEZ LICZBY | `CLEANUP` bez liczby uwolnionych danych |

**Jak dodać regułę:** `VerificationCenter.Register("PREFIKS_", (request, type, entries) => powód_lub_null);`
— najlepiej z własnego modułu rodzin narzędzi, NIGDY nie skracając reguł domowych.

---

## 3. NAMYSŁ — kiedy ponawiamy (i kiedy NIE)

`RetryAdvisor.TransientCause(błąd)` klasyfikuje przyczynę:

- **Chwilowa** (ponawiamy RAZ, z backoffem `BackoffMilliseconds(attempt)`):
  limit czasu · zajęty zasób · sieć/połączenie · usługa chwilowo niedostępna.
- **Trwała** (NIE ponawiamy): brak pliku · odmowa dostępu · zła składnia · nieznana.

Dodatkowy hamulec: ponawiamy tylko, gdy PRÓBA 1 **nic nie wykonała**
(zero dowodów = zero ryzyka podwójnego wykonania). Anulowanie w trakcie
namysłu = uczciwe CANCELLED, nigdy „ponowiłem po Tobie”.

---

## 4. RADA NAPRAWY — co dalej po porażce

`RecoveryAdvisor.Advise(typ, błąd)` zwraca WYKONYWALNĄ poradę per rodzina
(MEASURE_/OPEN_/CLOSE_/CLEANUP/NETWORK_/FILE_/SMARTHOME_/MEMORY_), a gdy
przyczyna chwilowa — radę „odczekaj i ponów”. Rada ląduje w
`ActionRecord.RecoveryAdvice` i w toaście błędu w UI. Zero ogólników.

---

## 5. ZAUFANIE — jak liczymy, ile sukcesów jest prawdziwych

Dwa źródła, ta sama matematyka (VERIFIED / zakończone):

- **EngineLedger** (sesja, na żywo): liczniki executed/verified/unverified/
  failed/cancelled + retried + downgraded + `TopUnverifiedTypes()` —
  „typy poleceń najczęściej kończące bez dowodu”.
- **TrustSummary** (historia z dysku, ostatnie 50): procent VERIFIED.

Panel gotowości (karta „Zaufanie do akcji”): ≥80% GOTOWE; detal pokazuje
źródło procentu i TOP typy bez dowodu. **Pusta historia/księga NIE daje 100%**
— daje „brak danych / nie oceniam na zapasie”. Pasek statusu pokazuje `%`
na żywo z księgi.

---

## 6. SZCZEROŚĆ — co Sentinel mówi wprost

- UNVERIFIED na polecenie-systemowe: *„nie mam dowodu z narzędzia, więc NIE
  potwierdzam wykonania”* + podpowiedź, jak sformułować mierzalne polecenie.
- Faza UNVERIFIED: *„NO SUCCESS = NO PASS · brak dowodu — sukcesu nie potwierdzam”*.
- Porażka po namyśle: *„także po ponowieniu — przyczyna nie jest chwilowa”*.
- Obalenie: *„Nie potwierdzam sukcesu (no success = no pass): <powody>”*.

---

## 7. JAK ROZSZERZAĆ MÓZG — przepisy

**Nowa rodzina narzędzi:** 1) dodaj regułę post-kondycyjną (sekcja 2),
2) dopisz rodzinę w `RecoveryAdvisor.ByFamily`, 3) jeśli błędy bywają chwilowe —
sygnały do `RetryAdvisor`, 4) kotwice w `tests/BrainRoundsRegression.cs`, 5) update tego pliku.

**Nowa rada naprawy:** jeden wiersz w `ByFamily` — konkretna, wykonywalna, z „przyczyna: …”.

**Nowa statystyka zaufania:** najpierw `EngineLedger` (pola + Changed), potem
konsumenci (Readiness/status bar), nigdy odwrotnie.

---

## 8. BRAMKI (no success = no pass dotyczy też nas)

- Regresja 45: `tests/BrainRoundsRegression.cs` — kotwice całej sekcji 1–7.
- Lokalne: arch PASS + grep kotwic ZANIM commitniesz.
- CI: Build + Smoke ~215 s; czerwone = adnotacje → napraw → push.
- Ten plik aktualizowany PRZY KAŻDEJ zmianie mózgu. Dokument bez zgodności z kodem = kłamstwo.

*Wersja: 0.99 · 10 rund ulepszeń mózgu (PLAN→CHECK→NAPRAWA, namysł, księga, rada, zaufanie na żywo).*
