# Wydanie 0.86 — pamięć v3 i prywatność (2026-09-23)

## Pochodzenie buildu

- **Wersja:** 0.86.0 (`Version` w `SENTINEL-X.csproj`, `AppConstants`).
- **Źródła:** gałąź `arena/01a0ca99-sentinelx`, commit `5b4e99db8bfb6547e05068c134c59c8881e2e587` (+ późniejsze commity dokumentacyjne w tym samym wydaniu gałęzi).
- **CI:** workflow „Windows build and WPF smoke", run `35896868514` — wszystkie kroki zakończone sukcesem.
- Build **nie jest podpisany** certyfikatem wydawcy — SmartScreen może ostrzegać; nie wyłączamy zabezpieczeń Windows.

## Artefakty (do pobrania z zielonego przebiegu CI → „Artifacts")

| Artefakt | Zawartość | Uwagi |
|---|---|---|
| `SentinelX-Setup-win-x64` | `SentinelX-Setup-0.86.0-win-x64.exe` + `SHA256SUMS.txt` | Instalator per-user (Inno), bez uprawnień administratora. Runtime w zestawie. |
| `SentinelX-Portable-win-x64` | wypakuj i uruchom `SentinelX.exe` | Self-contained: nie wymaga .NET na komputerze. Zawiera `SHA256SUMS.txt` plików. |
| `SentinelX-windows-x64` | build framework-dependent | Wymaga .NET 9 Desktop Runtime. |
| `sentinel-validation` | wyniki testów | Zrzuty ekranu stron, logi smoke, raporty regresji. |

Sumy SHA-256 są drukowane także jako adnotacje w logu kroku CI — sam sandbox deweloperski nie pobiera blobów artefaktów, więc weryfikację wykonaj po pobraniu: `Get-FileHash -Algorithm SHA256 <plik>` i porównaj z dołączonym plikiem `SHA256SUMS.txt`.

## Co faktycznie wykonano na Windows (nie „powinno działać")

1. `dotnet restore` + `dotnet build -c Release` na windows-latest — OK.
2. `--ui-smoke`: renderowanie wszystkich 9 stron, walidacja bindingów WPF, szybka ścieżka „ile mam RAM?" z dowodem VERIFIED, zapamiętanie wspomnienia przez silnik i odczyt na stronie Pamięć, przypięcie, tryb prywatny włącz/wyłącz — OK.
3. Zestawy regresji w pamięci procesu: `BackendRegression`, `ProductRegression`, `ReleaseRegression`, **nowy `MemoryRegression`** (restart danych, izolacja rozmów, 4 przełączniki prywatności, tryb prywatny bez śladu, redakcja audytu, migracja v2→v3 z kopią, retencja, import z deduplikacją, konflikty, szkic, wyszukiwanie bez diakrytyk) — OK.
4. Istniejący zestaw `--self-test` (legacy UI) — OK.
5. `dotnet publish` portable (self-contained, win-x64) + uruchomienie `--ui-smoke` z paczki — OK.
6. Kompilacja Inno, instalacja w katalogu testowym, `--ui-smoke` zainstalowanej aplikacji — OK.

## Pierwsze uruchomienie (skrót)

1. Instalator: pobierz `SentinelX-Setup-win-x64`, uruchom EXE. Albo paczka portable: wypakuj całość i uruchom `SentinelX.exe`.
2. Bez Ollama działają narzędzia lokalne (RAM/CPU/dyski/aplikacje/pliki/pamięć). Dla rozmowy z AI: zainstaluj [Ollama](https://ollama.com/), następnie `ollama pull qwen3:4b` (lub dowolny inny), w aplikacji: strona **AI → Sprawdź połączenie → Użyj wybranego modelu**.
3. Głos (opcjonalnie): strona **Voice → Pobierz modele i włącz głos** — pobieranie jest jawne.
4. Pamięć: strona **Pamięć** — dodawanie wspomnień, rozmowy, prywatność. Cztery przełączniki: **Settings → Pamięć**.
5. `Ctrl+Shift+X` — STOP awaryjny; `Ctrl+K` — paleta poleceń.

## Lista nowych funkcji 0.86

Zobacz sekcję „Nowe w 0.86" w README.md oraz `docs/MEMORY.md` (format pamięci v3, zasady prywatności).

## Ograniczenia i rzeczy niezweryfikowane

- CI nie ma mikrofonu, Ollama ani gier — głos, prawdziwe modele i tryb gry wymagają testu na komputerze docelowym.
- Sandbox deweloperski nie skompilował lokalnie (Linux, bez .NET, bez dostępu do NuGet): każda weryfikacja odbyła się wyłącznie w CI na Windows.
- Funkcje sekcji 5–17 z planu (projekty, zadania, przypomnienia, snapshoty itd.) są w `docs/BACKLOG.md`, nie w tym wydaniu.
- Usuwanie danych z pamięci nie kasuje eksportów ani audytu akcji (świadome; opisane w UI i docs/MEMORY.md).
