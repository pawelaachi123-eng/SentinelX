# Trwały backlog rozwoju SentinelX

Stan na 2026-09-22. Zasada: każda pojedyncza pozycja znika z listy dopiero, gdy jest **zbudowana i przetestowana na Windows (CI)**, nie „gdy wygląda na zrobioną".

## Gotowe i zweryfikowane (0.85 i wcześniejsze)

- MVVM shell + 8 stron, DI z walidacją, legacy UI za `--legacy`.
- Deterministyczne odczyty narzędzi (RAM/CPU/dyski/czas), dowody na żądanie, audyt JSONL, historia z filtrami i eksportem JSON/CSV.
- Centrum zgód LOW/MEDIUM/HIGH/CRITICAL: jednorazowe zgody z wygaśnięciem, głos nie zatwierdza, STOP/anulowanie usuwa oczekiwanie.
- STOP awaryjny z zatrzaskiem, single-lane ActionEngine, korelacja dowodów z requestId.
- Self-contained portable + instalator per-user (Inno), build/test/smoke CI na Windows.
- Pamięć v2: rozmowa z limitem 720 wpisów, profil, wspomnienia, format json z atomowym zapisem i ochroną przed uszkodzeniem.

## Gotowe w 0.86 (ten przyrost — patrz git)

- Pamięć v3: stabilne ID, kategorie, przypinanie, edycja/usuwanie po ID, oznaczanie nieaktualne, dziennik zmian, wykrywanie duplikatów i konfliktów (jako wskazówka do decyzji).
- Nazwane, wznawiane rozmowy z auto-tytułem i izolacją kontekstu; przełączenie przeładowuje widok rozmowy.
- Cztery niezależne przełączniki prywatności + retencja rozmów + tryb prywatny (bez trwałego śladu, także w audycie).
- Podgląd „co trafiło do modelu i dlaczego" (etykiety + powody, bez pełnych promptów), komenda `co poszło do modelu`.
- Nowa strona Pamięć w shellu (łącznie 9 stron), szkic rozmowy przeżywa restart poza trybem prywatnym.
- Eksport istniał; dodany import z podglądem, walidacją i ochroną przed duplikatami (bez importu historii).
- Migracja v1/v2→v3 z kopią zapasową przed pierwszym zapisem.
- Zestaw regresji `tests/MemoryRegression.cs` (odpalany w `--ui-smoke`).

## Gotowe w 0.87 (ten przyrost — patrz git)

- Moduł **Projekty**: tworzenie (auto-aktywacja), zmiana nazwy/opisu, statusy (aktywny/wstrzymany/zakończony), archiwizacja i przywracanie bez utraty danych.
- Izolacja kontekstu AI: aktywny projekt widzi wpisy projektu + globalne; wpisy innych projektów nie trafiają do modelu. Nowe notatki/rozmowy stemplowane `ProjectId`. Wyszukiwanie w panelu Pamięć celowo globalne.
- Rozmowy powiązane z projektem (dziedziczenie, przypisanie, odmowa wznowienia między projektami z wyjaśnieniem), „gdzie skończyliśmy?" z zapisanych danych, eksport projektu (JSON).
- Strona Projekty w shellu (łącznie 10 stron), komendy `nowy projekt: X` / `projekty` / `użyj projektu N` / `aktywny projekt` / `wyłącz projekt`.
- Własny magazyn `projects.json`: zapis atomowy, kopia uszkodzonego pliku, odczyt zwrotny z SHA-256, limit 200 projektów, strażnik duplikatów nazw.
- Zestaw regresji `tests/ProjectRegression.cs` + sekcja projektowa w `--ui-smoke`, uruchamiane w CI.

## Gotowe w 0.88 (ten przyrost — patrz git)

- Moduł **Zadania i przypomnienia**: priorytety, terminy, wiązanie z projektem, statusy otwarte/w toku/zrobione, filtry (Dzisiaj/Wszystkie/Przeterminowane/Zrobione), 11. strona shella.
- Polski parser terminów offline z testowalnym zegarzem; wszystkie wyniki to konkretna widoczna data+godzina przed zapisem.
- Przypomnienia z czatu accept-only („tak”/„nie”, wygaśnięcie 5 min); przegapione przy starcie oznaczane jawnie; brak obietnicy przypomnień przy zamkniętej aplikacji.
- Komendy: „dodaj zadanie:”, „zadania”, „zadanie N zrobione”, „przypomnienia”; usuwanie tylko przyciskiem w panelu.
- Własny magazyn `tasks.json`: zapis atomowy, kopia uszkodzonego pliku, odczyt zwrotny z SHA-256. Zestaw `tests/TaskRegression.cs` + sekcje w `--ui-smoke`.

## Gotowe w 0.89 (ten przyrost — patrz git)

- **Streaming odpowiedzi AI**: `/api/chat` z `stream: true`, NDJSON linia po linii, przyrostowe filtrowanie `<think>` (także przeciętego między fragmentami). Tylko pierwsza próba modelu strumieniuje, więc podgląd = odpowiedź końcowa.
- **Przycisk „Zatrzymaj generowanie” widoczny stale**; zatrzymanie zachowuje częściowy tekst z jawnym oznaczeniem i nie oznacza odpowiedzi jako udanej.
- **Ponowienie odpowiedzi** (`ponów` + przycisk) z oznaczeniem ponowienia.
- **Snapshoty diagnostyczne** (12. strona): zapis odczytu, porównanie dwóch odczytów linia po linii, eksport Markdown + JSON z odczytem zwrotnym i SHA-256, usuwanie pojedynczego odczytu, limit 20, `snapshots.json` z kopią uszkodzonego pliku.
- **Narzędzia rozmowy**: `szukaj w rozmowie: fraza` (tylko aktywna rozmowa) i `eksportuj rozmowę markdown` (odmowa w trybie prywatnym).
- Paleta poleceń: 13 nowych wpisów, w tym brakujące strony Pamięć/Projekty/Zadania/Diagnostyka.
- Zestawy `tests/AiStreamRegression.cs` + `tests/DiagnosticSnapshotRegression.cs` w `--ui-smoke` (CI).

## Priorytet P0 — kolejny przyrost

1. **Pliki**: plan porządkowania folderu z podglądem przed wykonaniem, zbiorcze zmiany nazw z podglądem, duplikaty po treści (SHA-256), usuwanie do Kosza jako domyślne.
2. **Sekwencje działań**: nazwane kroki, plan z podglądem, statusy kroków, zatrzymanie na błędzie, wznowienie tam, gdzie to sensowne.

## Priorytet P1

- Pliki — rozszerzenia poza P0: podgląd diff przed zmianą treści, filtrowanie duplikatów po rozmiarze przed hashowaniem.
- Tryb gry: zawieszanie indeksowania/ciężkich zadań, profil modeli z histerezą przełączania (progi + opóźnienie), zwalnianie modelu opcjonalne.
- Sekwencje działań: nazwane kroki, plan z podglądem, statusy kroków, stop na błędzie, wznowienie tam gdzie sensowne.
- Rozmowa: edycja wypowiedzi i ponowne wysłanie z oznaczeniem alternatywy; zakładanie odgałęzień rozmowy.

## Priorytet P2

- Wyszukiwanie semantyczne lokalne: tylko jako opcjonalny indeks (bez wpływu na tryb tekstowy); najpierw benchmark jakości na scenariuszach PL.
- Integracja z autostartem/Harmonogramem zadań dla przypomnień — wyłącznie za jawną zgodą, z opcją cofnięcia.
- Alerty progów diagnostyki z historią krótkich pomiarów (z limitem wzrostu plików).

## Świadome ograniczenia obecnego stanu

- Brak lokalnego kompilatora .NET w środowisku deweloperskim — weryfikacja wyłącznie przez CI na `windows-latest` (opis w README).
- Build niepodpisany: SmartScreen może ostrzegać (zgodnie z README; nie wyłączamy zabezpieczeń).
- Ollama, mikrofon i gry wymagają testów na komputerze docelowym; CI nie ma mikrofonu ani gier.
- Wyszukiwanie we wspomnieniach i w rozmowie jest tekstowe (normalizacja, bez fuzzy/typo-tolerance) — literówki mogą nie trafić.
- Streaming AI nie jest weryfikowany z żywym modelem w CI (brak Ollama w środowisku testowym) — transport i logika fragmentów są testowane na wstrzykniętym HTTP.
