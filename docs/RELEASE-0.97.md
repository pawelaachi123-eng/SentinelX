# RELEASE 0.97 · RDZEŃ — .NET 10, runtime rdzenia i narzędzia offline

Data: 2026-09-27. Gałąź: `arena/01a0e28e-sentinelx`. Wersja: `0.97 · RDZEŃ`, pakiet `0.97.0`.
Bez pakowania EXE — zgodnie z poleceniem użytkownika ten przyrost to **funkcje i narzędzia**,
a weryfikacją jest build i testy na Windows w CI.

## Zamówienie użytkownika

„dodałem teraz net 10 do githuba zrob to i jezeli cos juz jest to ulepsz to” oraz „dodaj te funkcje
i toolsy jezeli juz jakies mamy z tych to ulepsz je o wiele bardziej” + lista 1550 pozycji
(20 sekcji + bonus). Zasada z tej listy: **co już istnieje — ulepszać, nie dublować**.

Zakres zrealizowany w tym przyroście: **.NET 10** oraz **sekcja 1 (rdzeń systemu)**, a także
**sekcje 4–7** (narzędzia deweloperskie), **13 i 16** (produktywność i finanse osobiste) oraz
**poz. 186–220 z sekcji 4** (analiza kodu źródłowego). Reszta listy (m.in. Ollama/local models,
głos, wizja, scraping, komunikacja, health, IoT, GUI, agenci) jest w `docs/BACKLOG.md` jako
kolejne przyrosty — bez udawania, że istnieje.

## 1. .NET 10 (GA) + C# 14

| Element | Zmiana |
|---|---|
| `SENTINEL-X.csproj` | `net10.0-windows`, `LangVersion 14.0`, `EnableWindowsTargeting=true` (build WPF także poza Windows), `NoWarn WPF0001` |
| Pakiety Microsoft/System | `Microsoft.Extensions.DependencyInjection`, `System.Speech`, `System.Diagnostics.PerformanceCounter` → **10.0.0** |
| `.github/workflows/windows-build.yml`, `release.yml` | SDK **10.0.x**, ścieżki `bin/Release/net10.0-windows/SentinelX.exe` |
| `scripts/check-architecture.py` | bramka pilnuje `net10.0-windows` i `LangVersion 14.0` — powrót do .NET 9 zapala CI |
| Wersja | `0.97.0`, `AppConstants.Version = "0.97 · RDZEŃ"`, instalator `0.97.0` |

Uczciwa uwaga: w środowisku deweloperskim nie ma lokalnego SDK .NET, więc **jedynym dowodem
kompilacji jest CI na `windows-latest`** — tak samo jak w poprzednich przyrostach.

## 2. Rdzeń systemu — `Core/Runtime/` (18 modułów, ~3400 linii)

| Moduł | Co gwarantuje |
|---|---|
| `EventBus` | pub/sub w procesie; wzorce `*` i `?` bez regexów; historia ograniczona (200); błąd subskrybenta wraca w raporcie publikacji, nigdy nie rzuca |
| `Resilience` | `RetryPolicy` (wykładniczy backoff, limit 20 prób), `CircuitBreaker` (zamknięty/otwarty/półotwarty, zegar wstrzykiwany), `DeadLetterQueue` (limit 100, wskrzeszanie) |
| `TaskQueue` | 4 priorytety, kolejność zgłoszenia przy remisie, opóźnienia, ponowienia, kolejka zwrotów, anulowanie; **bez wykonawcy kolejka odmawia pracy**, zamiast udawać, że wykonała |
| `Caches` | cache LRU/LFU z TTL, liczniki trafień/pudeł/wyparć/wygaśnięć, uczciwe „brak danych” zamiast wymyślonej skuteczności |
| `CronSchedule` | 5 pól, zakresy, kroki, listy, nazwy dni i miesięcy, `@daily`…; `Next`/`Matches` czysto matematycznie (horyzont 4 lat), opis po polsku |
| `StateMachine` | przejścia deklarowane z góry, odmowa z powodem i listą dostępnych, historia w limitcie, wspólny cykl życia Sentinela |
| `WorkflowEngine` | graf zadań (Kahn): kolejność topologiczna, poziomy równoległości, cykle i brakujące zależności jako błędy; wykonawca ponawia kroki i **pomija** kroki z nieudaną zależnością |
| `FeatureFlags` | flaga = stan + procentowy zasięg + opis; przydział deterministyczny (FNV-1a), zapis atomowy z odczytem zwrotnym; brak pliku = brak flag |
| `HealthChecks` | sprawdzenia na żądanie (bez wątku w tle); wyjątek czujnika = problem z treścią wyjątku, nigdy „OK” |
| `Metrics` | liczniki, wskaźniki, histogramy z p50/p95 (interpolacja), pomiar bloku `Time()` |
| `StructuredLog` | JSONL, rotacja rozmiarowa (`structured.jsonl.1…`), poziom minimalny, błąd zapisu jawnie raportowany |
| `IntegrityManifest` | manifest SHA-256 katalogu, porównanie: zmienione / brakujące / nowe / nieczytelne |
| `BackupManager` | kopia ZIP z manifestem w środku i plikiem towarzyszącym (rozmiar + SHA-256), weryfikacja przez rozpakowanie i ponowne liczenie skrótów, rotacja „keep N” |
| `SecretVault` | AES-256-GCM, klucz z PBKDF2 (210 000 iteracji), hasło **tylko** z pola w panelu; manipulacja plikiem jest wykrywana i nie zwraca treści |
| `BinarySerializer` | format `SXB1` (varint/zigzag, posortowane klucze = deterministyczne bajty), `ToHex`/`TryFromHex`, uczciwe błędy dla uciętych danych |
| `RuntimeJobs` | prawdziwy wykonawca zadań: `log:`, `cache:`, `cache-usun:`, `metryka:`, `flaga:` — nieznany rodzaj kończy się błędem zadania |
| `RuntimeCommands` | warstwa poleceń rdzenia (tabela niżej) z jawnymi ograniczeniami |
| `SentinelRuntime` | spięcie modułów w jeden obiekt, 7 sprawdzeń zdrowia, jawne katalogi (testy nie dotykają danych użytkownika), bez pracy w tle |

## 3. Polecenia rdzenia

| Polecenie | Odpowiedź |
|---|---|
| `rdzen` / `stan rdzenia` | raport: event bus, kolejka, bezpieczniki, cache, flagi, sejf, kopie, dziennik, metryki, cykl życia, polityka ponowień |
| `zdrowie`, `metryki`, `zdarzenia`, `dziennik json`, `bezpieczniki`, `maszyna` | raporty modułów (tylko odczyt) |
| `kolejka`, `kolejka dodaj: log: info test`, `kolejka przetworz [N]`, `kolejka anuluj: T0001` | kolejka zadań i prawdziwe wykonanie (`log:` naprawdę zapisuje wpis JSONL) |
| `zwroty`, `zwrot ponow: T0001` | kolejka zwrotów i ręczne wskrzeszenie zadania |
| `cron opis: */15 * * * *`, `cron nastepne: 0 8 * * 1-5` | opis po polsku + najbliższe terminy |
| `flagi`, `ustaw flage: demo on|off|30`, `flaga demo [podmiot]` | flagi funkcji z deterministycznym zasięgiem |
| `cache zapisz: klucz = wartość`, `cache pokaz: klucz`, `cache usun: klucz` | cache z TTL 10 minut (przez polecenie) |
| `integralnosc zbuduj: <katalog>`, `integralnosc sprawdz: <katalog>`, `integralnosc` | manifest i porównanie zmian |
| `kopia danych`, `kopie danych`, `weryfikuj kopie: <nazwa>` | kopia, lista i weryfikacja od zera |
| `sejf`, `sejf dodaj: nazwa = wartość`, `sejf pokaz: nazwa`, `sejf usun: nazwa`, `sejf zablokuj` | sejf; **`sejf odblokuj` i utworzenie sejfu odmawiają hasła z czatu** (historia poleceń = ujawnienie) |
| `serializuj: tekst`, `deserializuj: <hex>` | format SXB1 w obie strony |
| `maszyna: startuje`, `workflow: pobierz > sprawdz > zapisz` | maszyna stanów; workflow tylko **planuje** graf (z cyklami jako błędami), nie uruchamia kroków |

## 4. Narzędzia deweloperskie — `DeveloperToolbox.cs` (sekcje 4–7)

`diff:` (LCS + podsumowanie zmian), `regex:` (z limitem 1 s i grupami), `semver:`/`semver podbij`,
`ip:` (klasa, sieć, rozgłoszenie, liczba hostów, zakres prywatny/publiczny), `podsiec:` (podział na
potęgi dwójki), `jwt:` (nagłówek + ładunek + ważność; **podpisu nie weryfikuję i tak jest napisane**),
`uuid7`, `ulid`, `json csharp:` (rekordy C# z JSON), `sql tabela:` (z walidacją nazw i jawnym
pominięciem kolumn bez typu), `sql z json:`, `mock json:` (dane przykładowe, jawnie nieprawdziwe),
`base32`/`base58` w obie strony, `md5|sha1|sha512|crc32`, `kody znakow`/`z ascii`,
`csv markdown`/`csv json`/`json csv`, `spis tresci:`, `czy …` (e-mail, URL, IPv4, UUID, semver —
z limitami: mówię, że e-mail to kontrola składni, nie istnienie skrzynki), `camel|snake|kebab|pascal|stala:`,
`commit:` (konwencja + typ z czasownika), `szablony`/`szablon:` (13 szablonów plików, w tym
Dockerfile .NET 10 i GitHub Actions), `tokeny:` (heurystyka ~3,6 znaku/token z zastrzeżeniem),
`kontekst:` (budżet okna z rezerwą 25%).

## 5. Produktywność i finanse — `ProductivityToolbox.cs` (sekcje 13 i 16)

`liczba slownie:` (z poprawną odmianą tysięcy/milionów i groszami), `lorem N` (deterministyczny),
`statystyki tekstu:` (znaki, słowa, unikalne, zdania, akapity, najczęstsze, czas czytania/mówienia),
`roi:` (z werdyktem), `break even:` (marża ≤ 0 → „próg nie istnieje, każda sprzedaż powiększa stratę”),
`amortyzacja:` (liniowa, 10 pierwszych lat), `inflacja:` (założenie stałej inflacji wypisane),
`oszczednosci:` (składanie miesięczne, podatek/inflacja jawnie pominięte), `cel:` (miesiące + rata
końcowa), `budzet:` (50/30/20), `macierz:` (Eisenhower — klasyfikacja wyłącznie z Twoich słów),
`slajdy:` (Markdown zgodny z Marp/Reveal.js).

## 6. Analiza kodu źródłowego — `CodeInsightsService.cs` (poz. 186–220)

Tylko odczyt, twarde limity (2000 plików / 1 MiB / 12 s / głębokość 8) i pomijane katalogi
techniczne (`node_modules`, `.git`, `bin`, `obj`, `dist`, `target`, `.venv`…): `skan kodu:`,
`statystyki kodu:`, `zaleznosci kodu:` (importy C#/Python/JS/TS/Go/Rust + cykle),
`bezpieczenstwo kodu:` (10 wzorców ryzyka z wagami — wzorce tekstowe, **nie** analiza przepływu
danych), `duplikaty kodu:` (bloki 6 znaczących linii), `licencje:` (LICENSE/COPYING + nagłówki SPDX),
`funkcje kodu:` (długość + przybliżona złożoność), `drzewo kodu:`.

## 7. Testy (dowód, nie deklaracja)

- `tests/CoreRuntimeRegression.cs` — 18 modułów rdzenia: event bus (wzorce, historia, izolacja
  błędów), bezpieczniki (progi, schłodzenie, półotwarty), kolejka (priorytety, ponowienia, kolejka
  zwrotów, anulowanie), cache (LFU, TTL), cron (`*/15`, dni robocze, `@daily`, 31 lutego),
  maszyna stanów, graf workflow (cykl, brakująca zależność, pominięty krok), flagi (determinizm
  i zapis), zdrowie, metryki (p50/p95), dziennik (rotacja), manifest (zmienione/brakujące/nowe),
  kopie (filtr rozszerzeń, rotacja, weryfikacja), sejf (krótkie hasło, złe hasło, manipulacja
  plikiem), serializacja binarna (determinizm, ucięte dane) oraz polecenia rdzenia end-to-end.
- `tests/DeveloperToolboxRegression.cs` — stałe skrótów i kodowań (MD5/SHA-1/SHA-512/CRC32,
  Base32/Base58 — sprawdzone niezależnym rachunkiem), walidatory, generatory, dispatch poleceń
  (18 deweloperskich + 6 produktywnościowych) i przypadek negatywny (obcy tekst nie jest przechwytywany).
- Oba zestawy są wpięte w `--ui-smoke` (`.github/workflows/windows-build.yml`) obok 15 istniejących
  regresji; wynik CI na Windows jest jedynym dowodem, że kompilacja i testy przechodzą.

## 8. Czego 0.97 nie robi (świadomie)

- Analiza kodu nie jest kompilatorem ani analizą semantyczną — mówi to każda odpowiedź.
- `workflow:` planuje graf; nie ma jeszcze wykonania kroków przez rdzeń (kolejka zadań wykonuje
  tylko zadania tekstowe `log:`/`cache:`/`metryka:`/`flaga:`).
- Sejf nie przyjmuje hasła z czatu; nie ma jeszcze panelu UI do sejfu (polecenia + pole hasła hosta).
- Z listy 1550 pozycji nie zrealizowano sekcji 2, 3, 8–12, 14–15, 17–20 — to kolejne przyrosty
  w `docs/BACKLOG.md`, nie „prawie zrobione”.
- Nic z 0.97 nie sięga do sieci i nic nie zmienia kodu aplikacji (samomodyfikacja pozostaje
  odrzucona).
