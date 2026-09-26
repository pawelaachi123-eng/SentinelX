# RELEASE 0.95 · JARVIS — narzędzia asystenta i samo-naprawa

Data: 2026-09-26. Gałąź: `arena/01a0dda5-sentinelx`. Wersja: `0.95 · JARVIS`, pakiet `0.95.0`.
Bez wydania EXE — publikacja wstrzymana do jawnego polecenia użytkownika.

## Zamówienie użytkownika

„dodaj więcej mega-przydatnych, takich żeby lepiej się z niego korzystało i takie co ma każdy
Jarvis; zrób też self repair i self check i żeby sam się ulepszał, jeżeli jeszcze nie ma”.

## 1. Narzędzia, które ma każdy Jarvis (wszystko offline)

| Polecenie | Co robi |
|---|---|
| `timer 5 minut herbata` | odliczanie; powiadomienie ⏰ o wskazanej godzinie (kanał przypomnień) |
| `budzik 7:00` / `budzik 730` | budzik na najbliższe wystąpienie godziny |
| `stoper start` · `stoper` · `stoper stop` | stoper sesji (w pamięci procesu, po restarcie od zera) |
| `głośność` · `głośność 40` | odczyt i ustawienie głośności master (Windows Core Audio COM) |
| `wycisz` · `przywróć dźwięk` | wyciszanie/włączanie dźwięku |
| `zrzut ekranu` | PNG całego wirtualnego pulpitu w `Screenshots/` w folderze danych |
| `kopiuj: tekst` · `co w schowku` | schowek systemowy (podgląd z limitem 200 znaków) |

Zasady: nazwy plików z datą i godziną (niczego nie nadpisuję), każdy odczyt ma fallback
„niedostępne na tym systemie” zamiast udawania, timer/budzik armują się od razu (samo polecenie
jest żądaniem — inaczej niż przypomnienia z opisem, które nadal wymagają „tak”).

## 2. Self-check, self-repair, self-improve

- **Self-check** (`samokontrola`) — rozszerzony: magazyny (pamięć, zadania, projekty, snapshoty,
  ustawienia, nauczone wzorce), dziennik lekcji, archiwa, wolne miejsce. Nadal **tylko do odczytu**.
- **Self-repair** (`napraw sie`, `self repair`, `napraw dane`) — naprawia **dane, nigdy kod**:
  - magazyn JSON nieczytelny → przywrócenie z najnowszej czytelnej kopii (`*.bak`, `*backup*`);
    bez kopii → przeniesienie uszkodzonego pliku na bok (`.corrupt-…`), magazyn odtworzy się
    przy następnym zapisie; **niczego nie kasuje**,
  - dzienniki JSONL (lekcje) → usunięcie nieczytelnych linii z kopią oryginału na bok,
  - nauczone wzorce → odrzucenie wpisów destrukcyjnych (plik jest edytowalny ręcznie).
- **Self-improve** (`ulepsz sie`, `self improve`, `naucz sie`) — bezpieczna pętla uczenia:
  1. każda naprawiona literówka („ile mam ramuu” → „ile mam ramu”) trafia do
     `Memory/LearnedPatterns.json`,
  2. każda zaakceptowana propozycja „Czy chodziło Ci o…” („tak”) też,
  3. przy powtórce tego samego wejścia odpowiadam natychmiast z notą „nauczone z Twojej poprawki”,
  4. twarde reguły: cele bez słów destrukcyjnych (walidacja przy zapisie **i** odczycie),
     limit 200 wzorców, wszystko w danych — **kod pozostaje nietknięty** (samomodyfikacja źródeł
     odrzucona na stałe; zmiany kodu to wersje w CI z testami).

## 3. Testy (dowód, nie deklaracja)

- `tests/SelfMaintenanceRegression.cs` (NOWY) — LearnedPatterns (nauka, odrzucanie destrukcyjnych,
  trwałość, samooczyszczenie po ręcznej edycji), SelfRepair.RepairJsonl/RepairJsonStore
  (przywracanie z kopii, parkowanie bez kasowania), TryParseDuration, StopwatchRegistry.
- `tests/UtilityRegression.cs` — głośność/schowek/zrzut: wynik albo uczciwy fallback.
- `UiSmokeTestRunner` — `napraw sie` → „SELF-REPAIR”, `ulepsz sie` → „Nie modyfikuję własnego
  kodu” + opis pętli uczenia, `timer 5 minut test` → „Timer ustawiony”, stoper start/stop.
- Zamrożone kontrakty 0.91–0.94 nietknięte (m.in. `vat 100` → „brutto 123,00”, „LEKCJE”+„ramuu”,
  „ulepsz sie” zawiera „Nie modyfikuję własnego kodu”).

## 4. Czego 0.95 nie robi (świadomie)

- Nie modyfikuje własnego kodu (użytkownik odrzucił to na stałe) — self-improve dotyczy
  zachowania i danych.
- Timer/budzik działają tylko przy uruchomionej aplikacji (jak przypomnienia) — bez udawania.
- Zrzut ekranu nie przechwytuje poszczególnych okien ani DRM-owego wideo.
- Brak sterowania jasnością ekranu (sprzętowe API są zawodne) — odłożone.

## Weryfikacja

- `scripts/check-architecture.py` — do uruchomienia przed pushem.
- Pełna weryfikacja: CI `windows-latest` (`build`, `test`, `--ui-smoke`). Brak lokalnego
  kompilatora .NET — patrz README.
