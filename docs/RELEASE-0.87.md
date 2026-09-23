# Wydanie 0.87 — projekty i izolacja kontekstu (2026-09-23)

## Pochodzenie buildu

- **Wersja:** 0.87.0 (`Version` w `SENTINEL-X.csproj`, `AppConstants`, `installer/SentinelX.iss`).
- **Źródła:** gałąź `arena/01a0ca99-sentinelx`, commit `967d7f37177f8de843c9f06a36e3f0a22696ac9b`.
- **CI:** workflow „Windows build and WPF smoke", run `35901867808` — wszystkie kroki zakończone sukcesem (build, regresje, UI smoke 10 stron, portable, instalator, instalacja+smoke).
- Build **nie jest podpisany** certyfikatem wydawcy — SmartScreen może ostrzegać; nie wyłączamy zabezpieczeń Windows.

## Artefakty (do pobrania z zielonego przebiegu CI → „Artifacts")

| Artefakt | Zawartość | SHA-256 (z adnotacji kroku CI) |
|---|---|---|
| `SentinelX-Setup-win-x64` | `SentinelX-Setup-0.87.0-win-x64.exe` + `SHA256SUMS.txt` | `21C19FCE53E5A70DCD469C359EC2A4D8AFFCFCE0BE8E1981E3B48A8507B5A74D` (instalator) |
| `SentinelX-Portable-win-x64` | wypakuj i uruchom `SentinelX.exe`, self-contained | `6352E6BA7FF1AA4A477C839B50E0A8FF27341217897DEF70B49B9D742FB0684D` (`SentinelX.exe`) |
| `SentinelX-windows-x64` | build framework-dependent | suma w `SHA256SUMS.txt` artefaktu |
| `sentinel-validation` | wyniki testów | zrzuty stron (w tym `projects.png`), logi smoke, raporty regresji |

Weryfikacja po pobraniu: `Get-FileHash -Algorithm SHA256 <plik>` i porównanie z tabelą lub z `SHA256SUMS.txt` dołączonym do artefaktów. Sumowy sandbox deweloperski nie pobiera blobów artefaktów; sumy pochodzą z adnotacji zielonego przebiegu CI.

## Co faktycznie wykonano na Windows (nie „powinno działać")

1. `dotnet restore` + `dotnet build -c Release` — OK.
2. Zestawy regresji w procesie: `BackendRegression`, `ProductRegression`, `ReleaseRegression`, `MemoryRegression` i **nowy `ProjectRegression`** (tworzenie/duplikaty nazw, stemplowanie notatek i rozmów `ProjectId`, izolacja kontekstu AI między projektami przy zachowanej widoczności wpisów globalnych, odmowa wznowienia rozmowy z innego projektu i rozmowy globalnej pod aktywnym projektem, przypisanie i odłączenie rozmowy, restart persystencji obu magazynów, eksport JSON z treścią notatki, cykl statusów, archiwizacja/przywracanie bez utraty danych, odczyt zwrotny `projects.json`) — OK.
3. `--ui-smoke`: renderowanie wszystkich 10 stron (nowa `projects.png` w artefakcie walidacyjnym), walidacja bindingów, roundtrip projektowy przez DI: utworzenie dwóch projektów, notatka z innego projektu **nie** wchodzi do kontekstu AI aktywnego projektu, notatka globalna — wchodzi, baner aktywnego projektu na karcie — OK.
4. Istniejący zestaw `--self-test` (legacy UI) — OK.
5. `dotnet publish` portable + `--ui-smoke` z paczki — OK.
6. Kompilacja Inno, instalacja, `--ui-smoke` zainstalowanej aplikacji — OK.

Suma kontrolna kroku „Install EXE and smoke-test installed application" potwierdza, że zainstalowany EXE to nie symulacja: krok uruchamia faktyczną aplikację z `%LOCALAPPDATA%\Programs\SentinelX`.

## Nowe funkcje 0.87

- Strona **Projekty** (10. w shellu), `ProjectService` z własnym magazynem `projects.json` (zapis atomowy, kopia uszkodzonego pliku, odczyt zwrotny z SHA-256, limit 200 projektów, strażnik duplikatów nazw).
- Izolacja kontekstu AI po projekcie; wyszukiwanie w panelu Pamięć świadomie globalne.
- „Gdzie skończyliśmy?" z zapisanych danych, eksport projektu do JSON, komendy: `nowy projekt: X`, `projekty`, `użyj projektu N`, `aktywny projekt`, `wyłącz projekt`.

## Historia iteracji (szczerze)

Do zielonego przebiegu potrzebne były 3 poprawki po pierwszym pushu, wszystkie złapane przez CI, nie „na ślepo":
1. cudzysłów prosty w środku interpolowanego napisu w `ProjectViewModel` (CS, banalne),
2. target-typed `new(string[])` z dostępem do składowej (CS — wymagany jawny `new string(...)`),
3. **funkcjonalna:** `Clone` wpisu pamięci nie przenosiło `ProjectId` — panel i testy widziały pusty stempel. Złapane przez własny test `ProjectRegression` zanim trafiło do użytkownika; naprawione w jednym miejscu.

## Pierwsze użycie modułu Projektów

1. Strona **Projekty → Nowy projekt: nazwa → Utwórz i aktywuj**. Od tego momentu nowe notatki i rozmowy są stemplowane projektem, a kontekst AI obejmuje wyłącznie wpisy globalne i tego projektu.
2. Albo w centrum poleceń: `nowy projekt: Sentinel X`, następnie `zapamiętaj ...`, `pokaz rozmowy`, `gdzie skończyliśmy?` na karcie projektu.
3. `wyłącz projekt` zwraca kontekst globalny. Archiwizacja nic nie usuwa.

## Ograniczenia i rzeczy niezweryfikowane

- Nie istnieje jeszcze widok „dziś" ani przypomnienia związane z projektami — to P0 poprowadzone w `docs/BACKLOG.md`.
- Wznawianie rozmowy respektuje aktywny projekt pomocniczo: `ResumeSession` rozmowy globalnej pod aktywnym projektem jest odmawiane (świadome, z polskim wyjaśnieniem) — jeśli ktoś chce przenieść rozmowę globalną do projektu, robi to przyciskiem „Przypisz aktywną rozmowę" lub komendą z paska.
- Eksport projektu zawiera notatki i **metadane** rozmów (tytuł, daty), nie szkice ani audyt akcji — świadome; treść rozmów zostaje w magazynie pamięci i w eksporcie pamięci.
- Import projektu z JSON nie istnieje (eksport = zapis/backup, nie migracja) — backlog.
- CI nie weryfikuje Ollama ani głosu — jak w 0.86, te kawałki wymagają komputera docelowego.
