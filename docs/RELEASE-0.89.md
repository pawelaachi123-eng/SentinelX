# Wydanie 0.89 — strumieniowanie AI, odczyty diagnostyczne, narzędzia rozmowy (2026-09-24)

## Pochodzenie buildu

- **Wersja:** 0.89.0 (`Version` w `SENTINEL-X.csproj`, `AppConstants` = „0.89 · WIĘCEJ", `installer/SentinelX.iss`).
- **Źródła:** gałąź `arena/01a0ca99-sentinelx`, commit `9ef657f65b3faa4f80204f0e65b2d0b721bb1d69`.
- **CI:** workflow „Windows build and WPF smoke", run `35918339573` (oraz `35918335279` z PR) — **wszystkie 13 kroków `success`**: checks architektoniczne, restore, build, UI smoke (12 stron + walidacja bindingów), istniejący zestaw regresji, publish portable, sumy kontrolne, weryfikacja paczki, kompilacja Inno, instalacja i smoke zainstalowanej aplikacji.
- Build **nie jest podpisany** certyfikatem wydawcy — SmartScreen może ostrzegać. Nie wyłączamy zabezpieczeń Windows.

## Artefakty (zielony przebieg CI → „Artifacts")

| Artefakt | Zawartość | SHA-256 (z adnotacji kroku CI) |
|---|---|---|
| `SentinelX-Setup-win-x64` | `SentinelX-Setup-0.89.0-win-x64.exe` + `SHA256SUMS.txt` | `A3EDFDDA3EA96476AF48AA585FE707CE89A9AA2678DCDAF9E1744CC59D67689D` (instalator) |
| `SentinelX-Portable-win-x64` | wypakuj i uruchom `SentinelX.exe`, self-contained | `E79200C9ED4C17D48DA6A2BCF87A56BC53B0FA67BA3F2B0C0EC89185E66B0C31` (`SentinelX.exe`) |
| `SentinelX-windows-x64` | build framework-dependent | suma w `SHA256SUMS.txt` artefaktu |
| `sentinel-validation` | wyniki testów | zrzuty 12 stron (w tym `diagnostics.png`), logi bindingów, raporty regresji |

Weryfikacja po pobraniu: `Get-FileHash -Algorithm SHA256 <plik>` i porównanie z tabelą lub z `SHA256SUMS.txt`. Sumy pochodzą z adnotacji zielonego przebiegu CI — sandbox deweloperski nie pobiera blobów artefaktów.

## Co faktycznie wykonało się na Windows (nie „powinno działać")

1. `dotnet build -c Release` — OK.
2. `tests/AiStreamRegression.cs` (nowy, w `--ui-smoke`): parser linii NDJSON (`message.content`, `response`, `done`, linia `error`, śmieci), przyrostowe filtrowanie `<think>` w pięciu krokach (w tym znacznik przecięty między fragmentami), pełny przepływ strumienia przez wstrzyknięty transport (3 fragmenty w kolejności = odpowiedź końcowa), ścieżka niestrumieniowa bez zmian, **zatrzymanie w połowie** (opublikowany tylko pierwszy fragment, `LastPartialAnswer` zachowany, odpowiedź nieoznaczona jako udana), awaria strumienia HTTP 500 → odpowiedź z zapasowego modelu bez doklejania drugiej odpowiedzi do podglądu, linia błędu w strumieniu nieudająca odpowiedzi — OK.
3. `tests/DiagnosticSnapshotRegression.cs` (nowy): porównanie na syntetycznych odczytach (zmiana wartości, stan sekcji, linie wolnotekstowe, sekcja brakująca, sekcja nowa, brak różnic dla identycznych), realny zapis dwóch odczytów, trwałość po restarcie, dowód z odczytu zwrotnego i SHA-256, odmowa porównania odczytu z samym sobą i nieznanego id, eksport Markdown+JSON zweryfikowany odczytem, usuwanie pojedynczego odczytu z trwałością, przycięcie magazynu do 20 odczytów, uszkodzony plik → jawny błąd + kopia — OK.
4. Pozostałe zestawy (`BackendRegression`, `ProductRegression`, `ReleaseRegression`, `MemoryRegression`, `ProjectRegression`, `TaskRegression`) — OK.
5. `--ui-smoke`: renderowanie **12 stron** (nowy `diagnostics.png`), zero błędów bindingów WPF, roundtrip odczytów przez czat (`snapshot` → `snapshoty` → strona → porównanie z raportem), wyszukiwanie w rozmowie, eksport rozmowy do Markdown z SHA-256, przycisk stop generacji odpowiada uczciwie, gdy nic nie jest generowane, ponowienie dodaje oznaczone polecenie i nową odpowiedź — OK.
6. `dotnet publish` portable + `--ui-smoke` z paczki, kompilacja Inno, instalacja i smoke zainstalowanej aplikacji — OK.

## Nowe funkcje 0.89

- **Strumieniowanie odpowiedzi AI** (`/api/chat`, `stream: true`, NDJSON linia po linii) z przyrostowym filtrem ukrytego rozumowania (`StreamThinkFilter`) — brudnopis modelu nie trafia do czatu nawet wtedy, gdy znacznik zostanie przecięty między fragmentami. Strumieniuje wyłącznie pierwsza próba modelu, więc podgląd jest tym samym tekstem, który wraca jako odpowiedź.
- **„Zatrzymaj generowanie" widoczne stale** (nie tylko w trakcie pracy). Zatrzymanie zachowuje wygenerowany tekst z jawnym oznaczeniem urwania i nie oznacza odpowiedzi jako udanej.
- **Ponowienie** (`ponów` + przycisk) — powtarza ostatnie polecenie dosłownie, odpowiedź jest oznaczona jako ponowiona.
- **Strona „Diagnostyka" (12. w shellu)**: zapis odczytu, porównanie dwóch odczytów linia po linii, eksport Markdown+JSON z odczytem zwrotnym i SHA-256, usuwanie pojedynczego odczytu, limit 20, magazyn `snapshots.json` (zapis atomowy, kopia uszkodzonego pliku).
- **Narzędzia rozmowy**: `szukaj w rozmowie: fraza` (także `znajdź w rozmowie:`, `przeszukaj rozmowę:`) oraz `eksportuj rozmowę markdown` (odmowa w trybie prywatnym, bo nie ma czego zapisywać).
- **Paleta poleceń**: 13 nowych wpisów, w tym brakujące strony Pamięć/Projekty/Zadania/Diagnostyka.

## Historia iteracji (szczerze — co złapało CI zanim trafiło do użytkownika)

1. **Kompilacja:** dwa przeciążenia `LocalAiService.AskAsync` (3- i 4-parametrowe, oba z wartościami domyślnymi) okazały się niejednoznaczne dla wywołań 1–2 argumentowych — 10 błędów w istniejącym zestawie AI. Zostało jedno przeciążenie z opcjonalnym `onDelta`.
2. **Test:** `VerifyPersistedState` zwracał `false`, bo `File.WriteAllText(..., Encoding.UTF8)` zapisuje BOM, a dowód czytał plik przez `Encoding.UTF8.GetString(bytes)`, który BOM zachowuje → `JsonException`. `Load()` używał `File.ReadAllText` i dlatego działał. Naprawa: odczyt tolerujący BOM + zapis bez BOM + magazyn w zwykłych DTO (konwencja reszty repozytorium) + asercja pokazująca dowód.
3. **Test:** moje oczekiwanie `HeldBackLength("abc<thi", "<think>") == 5` było błędne — `<thi` to 4-znakowy przedrostek znacznika. Poprawione oczekiwanie, dopisany przypadek 5-znakowy.
4. **Funkcjonalna kolizja:** `SentinelToolboxService` ma prefiks `"szukaj "` (wyszukiwanie w sieci), więc `szukaj w rozmowie: …` było przechwytywane jako wyszukiwanie internetowe i nigdy nie docierało do routera — złapane przez własny smoke. Dodana jawna osłona: szukanie w rozmowie to funkcja pamięci, nie sieć; router obsługuje trzy naturalne sformułowania.

## Ograniczenia i rzeczy niezweryfikowane

- **Strumieniowanie nie jest testowane z żywym modelem** — CI nie ma Ollama. Transport, kolejność fragmentów, filtrowanie i zatrzymanie są testowane na wstrzykniętym HTTP; zachowanie z prawdziwym modelem wymaga komputera docelowego.
- Porównanie odczytów **nie wyjaśnia przyczyny** i nie jest diagnozą kondycji sprzętu — ten komunikat jest wbudowany w raport.
- Wyszukiwanie w rozmowie jest dopasowaniem tekstu po normalizacji: bez literówek, odmiany i synonimów (mówi o tym sama odpowiedź).
- Snapshoty nie mają alertów progowych ani harmonogramu — patrz `docs/BACKLOG.md` (P0: pliki, sekwencje działań).
- Brak podpisu certyfikatem; brak weryfikacji mikrofonu i gier w CI.
