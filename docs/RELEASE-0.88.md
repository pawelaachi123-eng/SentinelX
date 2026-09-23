# Wydanie 0.88 — zadania i przypomnienia (2026-09-23)

## Pochodzenie buildu

- **Wersja:** 0.88.0 (`Version` w `SENTINEL-X.csproj`, `AppConstants`, `installer/SentinelX.iss`).
- **Źródła:** gałąź `arena/01a0ca99-sentinelx`, commit `e76740ba45f3bcaa77feb1138c9968ef64aec3c3`.
- **CI:** workflow „Windows build and WPF smoke", run `35908767793` — wszystkie kroki `success` (build, 6 zestawów regresji, UI smoke 11 stron, portable, sumy, Inno, instalacja+smoke).
- Build **nie jest podpisany** certyfikatem wydawcy — SmartScreen może ostrzegać; nie wyłączamy zabezpieczeń Windows.

## Artefakty (do pobrania z zielonego przebiegu CI → „Artifacts")

| Artefakt | Zawartość | SHA-256 (z adnotacji kroku CI) |
|---|---|---|
| `SentinelX-Setup-win-x64` | `SentinelX-Setup-0.88.0-win-x64.exe` + `SHA256SUMS.txt` | `0F7C97A95014EF3CE0C5A494CC87ADA7760574751F546A7F439012D2BC760BD8` (instalator) |
| `SentinelX-Portable-win-x64` | wypakuj i uruchom `SentinelX.exe`, self-contained | `140FBBEB7BFEEC2CEC2DD0EFB762E4E19647B7C73F0B3C10B836EA8BEC48D65A` (`SentinelX.exe`) |
| `SentinelX-windows-x64` | build framework-dependent | suma w `SHA256SUMS.txt` artefaktu |
| `sentinel-validation` | wyniki testów | zrzuty stron (w tym `tasks.png`), logi smoke, raporty regresji |

Weryfikacja po pobraniu: `Get-FileHash -Algorithm SHA256 <plik>` — porównanie z tabelą lub z `SHA256SUMS.txt` dołączonym do artefaktów. Sandbox deweloperski nie pobiera blobów artefaktów; sumy pochodzą z adnotacji zielonego przebiegu CI.

## Co faktycznie wykonano na Windows (nie „powinno działać")

1. `dotnet restore` + `dotnet build -c Release` — OK.
2. Zestawy regresji w procesie: `BackendRegression`, `ProductRegression`, `ReleaseRegression`, `MemoryRegression`, `ProjectRegression` i **nowy `TaskRegression`** — m.in. pełna tabela parsera terminów wokół środy 16:00 (względne, dziś/jutro/pojutrze, dni tygodnia z regułą „minęło → następny tydzień", daty jawne z kolizją „12.06 o 18" ≠ 12:06), cykl statusów, odmowa terminu w przeszłości, przypomnienie wywołane raz i tylko raz, oznaczenie przegapionych przy starcie, trwałość po restarcie magazynu, usuwanie pojedynczego wpisu — OK.
3. `--ui-smoke`: renderowanie wszystkich 11 stron (nowy `tasks.png` w artefakcie), roundtrip zadań przez DI + strona, czat `przypomnij mi jutro o 18 o sprawdzeniu ZXCVBNM` → brak zapisu przed „tak" → po „tak" przypomnienie istnieje i jest widoczne w panelu — OK.
4. Istniejący zestaw `--self-test` (legacy UI) — OK.
5. `dotnet publish` portable + `--ui-smoke` z paczki — OK.
6. Kompilacja Inno, instalacja, `--ui-smoke` zainstalowanej aplikacji — OK.

## Nowe funkcje 0.88

- Strona **Zadania** (11. w shellu): priorytety, terminy, projekty, statusy otwarte/w toku/zrobione (odwracalnie), filtry Dzisiaj/Wszystkie/Przeterminowane/Zrobione, edycja, kasowanie pojedynczego wpisu z uczciwym opisem zakresu.
- Polski parser terminów offline → zawsze konkretna data+godzina pokazana przed zapisem; testowalny zegar.
- Przypomnienia z czatu accept-only z wygaśnięciem 5 min; „tak”/`potwierdz` = zapis, „nie”/`anuluj` = porzucenie; inna wypowiedź usuwa oczekiwane i przechodzi normalnie.
- Przegapione przypomnienia przy starcie z jawnym oznaczeniem i bez udawania; baner i wiadomość w chacie działa, gdy aplikacja jest uruchomiona.
- Komendy: `dodaj zadanie:`, `zadania`, `zadanie N zrobione`, `przypomnienia`, `przypomnij …`. Usuwanie — wyłącznie przycisk w panelu.
- Magazyn `tasks.json` z zapisem atomowym, kopią uszkodzonego pliku, odczytem zwrotnym SHA-256.

## Historia iteracji (szczerze)

Do zielonego przebiegu potrzebne były 4 poprawki po pierwszym pushu, wszystkie złapane przez CI:
1. zagnieżdżona interpolacja stringów stringów w `TaskViewModel` (składnia) — rozpisane na jawne zmienne,
2. błąd w mojej własnej tabeli prawdy parsera (przypadek „w środę o 18" o 16:00 = dziś, nie za tydzień) — poprawione oczekiwanie + dopisany przypadek z rzeczywistym „minęło",
3. asercja `.Single()` na liście bez zrobionych zadań po oznaczeniu zadania jako zrobione,
4. **funkcjonalna:** naturalna polska kolejność „przypomnij mi jutro o 18 o…" (czas z przodu) nie była rozpoznawana — złapane przez własny smoke czatu; splitter obsługuje teraz oba układy (końcówka ma pierwszeństwo), a propozycje pokazują błąd parsera zamiast generycznej odmowy.

## Ograniczenia i rzeczy niezweryfikowane

- Przypomnienia cykliczne (codziennie o 8) **nie istnieją** — nie obiecuj funkcji, której nie ma; backlog P2.
- Przypominanie przy zamkniętej aplikacji wymagałoby harmonogramu systemowego lub autostartu — to wymaga jawnej zgody użytkownika i zostaje w backlogu P2; obecny stan jest uczciwie opisany w UI.
- Import/eksport zadań nie istnieje (eksport można zrobić kopią pliku `tasks.json`); backlog.
- CI nie weryfikuje Ollama ani głosu — jak w 0.86–0.87, te obszary wymagają komputera docelowego.
