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

- **EXE i instalator nie są budowane przy zwykłym pushu** (polecenie użytkownika: „nie buduj mi EXE jeszcze”).
  Workflow `windows-build.yml` nadal robi restore, build, `--ui-smoke` i `--self-test` (to jest dowód
  kompilacji i testów), ale kroki publikacji paczki portable, instalatora Inno i artefaktów binarnych
  mają teraz bramkę `if: github.event_name == 'workflow_dispatch'` — czyli uruchamiają się wyłącznie
  po ręcznym wywołaniu workflow (albo w `release.yml` na tagu), nigdy przy pushu gałęzi.

- Analiza kodu nie jest kompilatorem ani analizą semantyczną — mówi to każda odpowiedź.
- `workflow:` planuje graf; nie ma jeszcze wykonania kroków przez rdzeń (kolejka zadań wykonuje
  tylko zadania tekstowe `log:`/`cache:`/`metryka:`/`flaga:`).
- Sejf nie przyjmuje hasła z czatu; nie ma jeszcze panelu UI do sejfu (polecenia + pole hasła hosta).
- Z listy 1550 pozycji nie zrealizowano sekcji 2, 3, 8–12, 14–15, 17–20 — to kolejne przyrosty
  w `docs/BACKLOG.md`, nie „prawie zrobione”.
- Nic z 0.97 nie sięga do sieci i nic nie zmienia kodu aplikacji (samomodyfikacja pozostaje
  odrzucona).

## 9. Domknięcie bramki jakości (0.97, pierwsze zielone CI)

Pierwsze pełne uruchomienie 0.97 na Windows odsłoniło defekty, które nie mogły ujawnić się
wcześniej — dopóki poprzednia asercja nie przeszła, następna nigdy się nie wykonywała. Każda
poprawka poniżej ma regresję w `tests/` albo w `--ui-smoke`, żeby nie wróciła:

- **Rozumienie nie podmienia znanego polecenia.** `CommandUnderstanding.Extract` nie przepisuje
  wpisu, który sam jest znaną frazą. Wcześniej odmiana przez ogierki skracała „snapshoty” do
  „snapshot” (krótszy kandydat wygrywał kolejność) i użytkownik zapisywał nowy odczyt zamiast
  dostać listę odczytów. Regresja: `tests/UnderstandingRegression.cs`.
- **Walidator i generator hasła nie zabierają zwykłych pytań.** „czy” + dowolny tekst wpadało do
  walidatora, a „hasło …” do generatora, więc pytania nigdy nie docierały do modelu. Teraz
  walidacja wymaga dwukropka (`waliduj: 10.0.0.1`, `czy email: ala@example.com`), a hasło —
  podanej długości (`haslo 20`). Regresje: `tests/DeveloperToolboxRegression.cs`,
  `tests/UtilityRegression.cs`.
- **Cron liczy miesiąc.** „0 0 31 2 *” zwracał 31 stycznia, bo miesiąc nie był sprawdzany przy
  szukaniu terminu; teraz uczciwie odpowiada „brak terminu”.
- **Manifest integralności normalizuje ścieżki do „/”.** Ten sam plik bywał raz zapisany jako
  `podkatalog\notatka.md`, a raz `podkatalog/notatka.md`, co dawało fałszywe „zmienione”
  w weryfikacji kopii.
- **„ip:” wymaga pełnego zapisu kropkowanego.** `IPAddress.TryParse` przyjmuje skróty („1.2.3”,
  „0x7f.1”) i opisywał inny adres, niż wpisał użytkownik.
- **Weryfikacja magazynu rutyn** patrzy na awarię zapisu (`storageFault`), a nie na komunikaty
  walidacji — „Nie mam rutyny o nazwie …” nie jest już traktowane jako problem z plikiem.
- **Wersja ma jedno źródło.** `Core.AppConstants.Version` zasila „wersja”, nagłówek „co nowego”
  i asercje testów, więc dziennik zmian nie zostaje w tyle za wydaniem (był jeszcze na 0.93).
- **Budżet kontekstu** — asercja sprawdza dokładne brzmienie komunikatu („…się NIE zmieści…”).

Stan bramek po tych poprawkach (gałąź `arena/01a0e28e-sentinelx`):

| Bieg | Wynik |
| --- | --- |
| `windows-build.yml` → „Windows build and WPF smoke” | **success** (build, `--ui-smoke`, `--self-test`) |
| `dotnet-desktop.yml` → „Build Windows app” | **success** |

Kroki publikacji EXE/instalatora i wysyłki artefaktów pozostają wyłączone przy pushu
(sekcja 8) — zielone CI nie oznacza, że powstał instalator.

## 10. Modele lokalne — `ModelToolbox.cs` (sekcja 2, pierwszy przyrost)

Sekcja 2 listy (poz. 46–120) mówi o modelach lokalnych. Ten przyrost bierze z niej to, co da się
zrobić uczciwie **bez sieci i bez uruchamiania modelu**: katalog orientacyjny, rachunek pamięci
i gotowe materiały do pracy z Ollamą. Każda odpowiedź podaje wzór, z którego liczy, i mówi wprost,
że to szacunek — żadna liczba nie udaje pomiaru z Twojej maszyny.

| Polecenie | Co daje |
| --- | --- |
| `modele lokalne` | katalog 17 modeli: rola, rozmiar parametrów, notka (m.in. `qwen3:1.7b`, `qwen3:4b-instruct`, `gemma3:4b` — te same, których Sentinel używa domyślnie) |
| `model karta: qwen2.5:7b` | warstwy, głowy KV, wymiar głowy, maksymalny kontekst, wagi w q4_K_M, KV cache przy 8 192 tokenach, suma z narzutem 0,8 GB |
| `model dopasuj: 8` | podział katalogu na „zmieści się spokojnie / na styk / nie zmieści się” dla podanej pamięci |
| `model audyt` | to samo, ale od realnego RAM-u komputera (2 GB zostają dla systemu) |
| `model rola: kod` · `…: wizja` · `…: embeddingi` | propozycje modeli w tej roli z uzasadnieniem i szacunkiem pamięci |
| `kwantyzacje` · `kwantyzacja: q4_K_M` | tabela 8 wariantów: bity na wagę (2,6–16) → rozmiar wag dla 7B |
| `presety modelu` · `preset modelu: szybki` | trzy zestawy parametrów (temperatura, top_p, num_ctx, num_predict, keep_alive) — do wykorzystania, nie wymuszane |
| `prompt szablony` · `prompt szablon: kod` | 10 gotowych promptów systemowych (kod, testy, dokumentacja, tłumaczenie, streszczenie, ekstrakcja, SQL, refaktor, błąd, e-mail) |
| `model kv: qwen2.5:7b 8192` | KV cache z wypisanym wzorem: `2 × warstwy × głowy KV × wymiar × tokeny × 2 B` |
| `model pamiec: 8192` | ile pamięci zjada kontekst dla trzech rozmiarów modelu |
| `model porownaj: a vs b` | dwa modele obok siebie + który jest lżejszy i o ile |
| `model kolejka` | stan faktyczny: jedna ścieżka wykonania, limit 180 s, „przerwij”, model zapasowy (maks. dwie próby), `keep_alive` 10m / 0 w trybie gry |
| `model polityka` | domyślne role i liczby z `AiSettings` (temperatura 0,22 · kontekst 4096 · odpowiedź 700 · progi 78/85/70) |
| `model offline` | co działa bez modelu, a czego bez niego nie ma |

Regresja: `tests/ModelToolboxRegression.cs` (18. zestaw w `--ui-smoke`) sprawdza liczby
(7B w q4_K_M = 4,24375 GB, f16 = 14 GB, KV cache 28×4×128×8192 = 0,46976 GB, podwojenie kontekstu
= podwojenie KV), odrzucanie śmieci (nieznany model, rola, preset, szablon, kwantyzacja,
absurdalna pamięć) oraz to, że **zwykłe zdanie o modelach nie jest przechwytywane** — ta sama
pułapka, która wcześniej zabierała pytania zaczynające się od „czy”.

Czego w sekcji 2 **nadal nie ma** (żeby nie było wątpliwości): indeksu embeddingów i wyszukiwania
wektorowego, benchmarku tokenów na Twoim sprzęcie i panelu zarządzania modelami w GUI (pobieranie,
usuwanie, dobór do zadania i licencje doszły w drugim przyroście — sekcja 15).

## 11. Analiza danych — `AnalysisToolbox.cs` (sekcja 15)

- **Wejście liczbowe**: jedna konwencja dla wszystkich poleceń — liczby rozdzielone spacjami albo
  średnikami, przecinek jest częścią ułamka (`3 4; 4 5,5 -2`), a `|` dzieli dwie serie.
  Nic nie jest zgadywane: gdy danych brakuje, odpowiedź podaje gotową składnię.
- **Statystyki opisowe**: `statystyki liczb:` (suma, min/maks, rozstęp, średnia, mediana, dominanta,
  kwartyle, IQR, wariancja próby, odchylenie, odstające poza płotkami 1,5 × IQR) ·
  `kwartyle:` (interpolacja liniowa jak `PERCENTILE.INC`, płotki Tukeya, percentyle 5/95) ·
  `odchylenie:` / `wariancja:` (próba i populacja, zakres średnia ± 1 SD) ·
  `skosnosc:` / `kurtoza:` · `wspolczynnik zmiennosci:` (z progiem jednorodności) ·
  `przedzial ufnosci:` (90/95/99% i jawne „nie mam tablic t-Studenta”) ·
  `percentyl: … | 90` · `rangi:` (remisy uśredniane).
- **Związki**: `korelacja:` (Pearson + kowariancja + Spearman na rangach, ocena siły i jawny brak
  wnioskowania o przyczynie) · `regresja:` (y = a·x + b, R², przykład predykcji) ·
  `prognoza:` (3 kolejne wartości) · `trend:` (rosnący/malejący/płaski).
- **Kształt i przygotowanie danych**: `histogram: … | 4` (2–20 przedziałów, słupki z `#`) ·
  `normalizuj:` (min–max 0…1 oraz z-score) · `odleglosc:` (euklides, manhattan, Czebyszew,
  podobieństwo kosinusowe) · `wygladzanie: … | 0,5` (wykładnicze, alpha ∈ (0, 1>) ·
  `outliery:` (płotki IQR, pozycje, jawny komunikat „brak odstających”).
- **Klasyfikacja i nieporządek**: `macierz pomylek: TP FP FN TN` (dokładność, zbalansowana
  dokładność, precyzja, czułość, swoistość, F1, MCC + ostrzeżenie o niezbalansowanych klasach) ·
  `entropia:` (Shannona w bitach, ile z maksimum) · `gini:` (nieczystość klasyfikacyjna **i**
  współczynnik nierównomierności — dwie różne miary, wyraźnie rozdzielone) ·
  `test t:` (Welcha; **bez p-wartości** — nie mam tablic rozkładu t i mówię to wprost).
- **Regresja**: `tests/AnalysisToolboxRegression.cs` — liczby ręcznie policzone (mediana 4,5;
  Q1 4, IQR 4; percentyl 0,25 z 1–10 = 3,25; percentyl 90 z 1–5 = 4,6; Pearson ±1; y = 2x → R² = 1;
  √50 = 7,071; macierz 50/10/5/35 → 85%, 83,33%, 90,91%, F1 0,87; entropia 8/1/1 = 0,922 bita;
  Gini 2/3/5 = 0,62) oraz dziewięć zdań, które **nie mogą** wpadać do narzędzia.

## 12. Zdrowie i komunikacja — `LifeToolbox.cs` (sekcje 17 i 14)

- **Zdrowie — arytmetyka, nie porada**: `bmr:` (Mifflin-St Jeor: 10·waga + 6,25·wzrost − 5·wiek
  ± 5/−161) · `tdee:` (współczynnik 1,2–1,9) · `makro:` (procenty → gramy z kontrolą bilansu) ·
  `hrmax:` / `tetno maksymalne:` (220 − wiek i Tanaka 208 − 0,7 · wiek, 5 stref) ·
  `whtr:` (próg 0,5) · `whr:` (WHO 0,90/0,85; bez płci pokazuje oba progi) ·
  `1rm:` (Epley, Brzycki, Lombardi, zakres + limit sensowności powtórzeń) · `tempo:` (min/km,
  km/h, czas na 5 km i półmaraton) · `kroki:` (krok = 0,415 × wzrost, dystans i orientacyjny
  wydatek na 70 kg) · `woda:` (30 i 35 ml/kg) · `sen:` (cykle 90 min + 15 min na zaśnięcie,
  pobudki od 3 do 6 cykli) · `deficyt:` (7700 kcal/kg, tempo 0,2–1 kg/tydzień).
  Każda odpowiedź kończy się jawnym zastrzeżeniem, że to nie porada medyczna.
- **Komunikacja — limity i szkice**: `sms:` (GSM-7 160/153 znaki vs UCS-2 70/67 przy polskich
  znakach lub emoji) · `post:` (limit 280, pozostałe znaki, linki liczone po 23) ·
  `skroc do: 120 | tekst` (cięcie na granicy słowa + wielokropek, bez cichego kasowania) ·
  `ton:` (wykrzykniki, słowa WERSALIKAMI, słowa nacisku → neutralny / lekko naciskający /
  nagląco) · `czytelnosc:` (długość zdań, trudne słowa, bez angielskich wskaźników dla polskiego) ·
  `mail:` (szkielet wiadomości) · `follow up:` (przypomnienie w trzech zdaniach) ·
  `agenda:` (bloki 30 minut) · `protokol:` (punkty rozdzielone `|`, znacznik czasu).
  Nic nie jest wysyłane: narzędzia tylko liczą i piszą szkic na ekranie.
- **Regresja**: `tests/LifeToolboxRegression.cs` — BMR 1780/1614 kcal, makro 2400 kcal → 180/67/270 g
  z bilansem, Tanaka 183,5 dla 35 lat, WHtR 0,444, 1RM 93,3 kg, tempo 4:12 i 14,29 km/h,
  kroki 5,81 km, woda 2,4 l, sen 07:15 przy 5 cyklach, deficyt 20 tygodni, SMS GSM-7 vs UCS-2,
  skrót „Ala ma kota…”, ton naglący vs neutralny, agenda 0:26–0:30 — plus dziesięć zdań,
  które pozostają rozmową („sen o 23 był dobry”, „tempo życia mnie zaskakuje”).

## 13. Prywatność — `PrivacyToolbox.cs` (sekcja 3)

- **Odczyty, nie deklaracje**: `prywatnosc` (raport stanu: brak telemetrii, brak konta, brak chmury,
  gdzie leżą dane, co jest zapisywane, a co nie) · `gdzie sa moje dane` / `mapa danych`
  (per podkatalog: rozmiar, liczba plików, razem — prawdziwy odczyt z dysku) ·
  `duze pliki danych: 5` (próg w MB, 15 największych, **bez czytania treści**) ·
  `wiek danych` (najstarszy i najnowszy plik w każdym katalogu).
- **Retencja to podgląd**: `retencja: 90` pokazuje, co byłoby starsze niż 90 dni — i **nic nie usuwa**
  (test sprawdza, że plik nadal istnieje); `retencja` opisuje realne limity aplikacji.
- **Szyfrowanie i uprawnienia bez upiększania**: `szyfrowanie` mówi wprost, że AES-256-GCM ma tylko
  sejf, a rozmowa, wspomnienia i ZIP-y **nie są** zaszyfrowane · `uprawnienia` wylicza użyte
  uprawnienia (pliki w katalogu danych, klucz autostartu HKCU, WM_CLOSE, lokalne API) ·
  `co wysylam` (nic nie wychodzi; jedyne możliwe połączenie to 127.0.0.1:11434) ·
  `eksport danych` (realne polecenia eksportu i kopia danych) · `minimalizacja` (co wyłączyć,
  żeby zapisywać mniej).
- **Regresja**: `tests/PrivacyToolboxRegression.cs` pracuje na własnym katalogu tymczasowym:
  tworzy pliki, sprawdza rozmiary, wiek, próg dużych plików, podgląd retencji (łącznie z tym,
  że **plik nadal istnieje**), treść deklaracji i siedem zdań, które nie są poleceniami.

## 14. Studio 0.97 — nowa strona interfejsu (najlepsze UI na .NET 10)

- **Po co**: narzędzia rosły szybciej niż interfejs — 70 poleceń z sekcji 2, 3, 14, 15, 17 i rdzenia
  nie miało miejsca, w którym da się je zobaczyć i uruchomić bez pamiętania składni.
  Studio to jedna strona (menu → „Studio 0.97”, ikona ◈), która robi dokładnie to.
- **Kategorie i przykłady**: sześć kategorii (Analiza danych · Zdrowie · Komunikacja · Prywatność ·
  Modele lokalne · Rdzeń i narzędzia) i 70 gotowych poleceń. Kliknięcie polecenia wstawia je do pola
  i pokazuje jedno zdanie wyjaśnienia (co zwróci, czego nie zrobi). Każde polecenie w katalogu ma opis
  — pilnuje tego `--ui-smoke`.
- **Prawdziwe uruchomienie, nie podgląd**: „Policz ⏎” (albo Enter w polu) idzie tą samą drogą co czat
  (`IActionEngine` → router poleceń), więc narzędzie samo waliduje składnię, a akcje systemowe nadal
  pytają o zgodę i zostawiają dowód wykonania. Wynik trafia do pola monospace (zawijanie wyłączone,
  bo raporty mają kolumny), a ostatnie 6 uruchomień pojawia się na liście — **wyłącznie w pamięci
  procesu**, bez zapisu na dysk (wynik może zawierać dane użytkownika).
- **Integracja z paletą `//`**: sześć wpisów palety prowadzi wprost do Studia, łącznie z wyborem
  kategorii (`PageKey: "studio:analiza"` itd.) — klucz nieznany nie psuje wyboru, tylko go zostawia.
- **Bez nowych zależności**: strona korzysta wyłącznie z motywu 0.93+ (`SxCard`, `SxChipButton`,
  `SxTextBox`, `SxFontMono`, gradienty i animacje wejścia `u:Reveal.Enabled`), więc działa w jasnym
  i ciemnym motywie oraz respektuje wyłączone animacje. UI na .NET 10 bez zmian w pakietach.
- **Dowód w CI**: `--ui-smoke` renderuje wszystkie strony (nowa też) i wywala bieg na najmniejszym
  błędzie wiązania; osobny blok testuje Studium jak funkcję: katalog ≥ 60 poleceń z opisami, wybór
  kategorii, staging polecenia, realne uruchomienie BMR („1780 kcal”) i statystyk („mediana: 4,5”),
  historia, kopiowanie (schowek może odmówić w sesji CI — VM ma to obsłużyć) i czyszczenie.

## 15. Modele lokalne — zarządzanie (drugi przyrost sekcji 2)

- **Co dochodzi**: dobór modelu do opisu zadania („model do zadania: pisanie kodu” → `qwen2.5-coder:7b`,
  ze względu, szacunkiem pamięci, alternatywami i wskazaniem, że to podpowiedź z katalogu, nie benchmark),
  licencje rodzin z katalogu („model licencje” — Apache 2.0 / MIT / Llama Community / Gemma Terms,
  z jawnym „Sprawdź u źródła; Sentinel nie jest prawnikiem”), karta zainstalowanej wersji z Ollamy
  („model info: qwen2.5:7b” — rodzina, parametry, kwantyzacja, kontekst), lista załadowanych modeli
  („model uruchomione” z rozmiarem w pamięci) i status pobierania („model status pobierania” — bez sieci).
- **Pobieranie i usuwanie tylko za zgodą dwuetapową**: „model pobierz: qwen3:1.7b” pokazuje **plan**
  (szacunek rozmiaru z katalogu albo uczciwe „nie znam”, źródło pobierania, dokładną formę potwierdzenia),
  a dopiero „model pobierz: qwen3:1.7b potwierdzam” startuje. Zgoda jest jednorazowa, związana z nazwą
  i wygasa po 10 minutach. Usuwanie („model usun: …”) dodatkowo odmówi, gdy model jest aktualnie
  ustawiony albo gdy trwa pobieranie, a plan mówi wprost, że operacja jest nieodwracalna. Kopiowanie
  („model kopiuj: a do b”) jest dodatnie, więc idzie od razu — ale po ścisłej walidacji nazw.
- **Bezpieczeństwo kanału**: wszystkie operacje idą wyłącznie do loopbackowej Ollamy (127.0.0.1:11434).
  Nazwy modeli przechodzą ścisłą normalizację (małe litery, opcjonalnie jeden „:tag”, zero spacji
  i ukośników — ścieżka nie przejdzie jako nazwa). Bez Ollamy każda operacja kończy się jawnym
  komunikatem z adresem, nie wyciszoną awarią. Postęp pobierania jest parsowany linia po linii (NDJSON),
  a śmieciowa linia strumienia jest ignorowana, nie przerywa pobierania.
- **Testy bez sieci**: `tests/ModelManagementRegression.cs` symuluje martwą sieć (własny handler),
  więc regresja deterministycznie przechodzi też na maszynie z uruchomioną Ollamą — **niczego naprawdę
  nie pobiera i niczego nie usuwa**. Łącznie 22 zestawy w `--ui-smoke`.

## 16. Pozostałe sekcje naraz — architektura, full-stack, wiedza, research, smart home, cele, media

- **Architektura (sekcja 5, `ArchitectureToolbox.cs`)**: `moduly:` (kolejność budowy Kahnem, cykle),
  `sprzezenie:` (fan-in/fan-out), `warstwy:` (przeskoki i wywołania w górę), `dlug techniczny:`
  (rejestr 0–10 z priorytetem), `adr:`, `styl:`, `c4:`, `kapacyt:` (instancje z rezerwą),
  `pojemnosc kolejki:`, `latencja:` (budżet z największym kawałkiem), `migracja bazy:`
  (expand–migrate–contract), `wdrozenie kanary:`, `karta modulu:`. Nic nie uruchamia — analizuje dane z polecenia.
- **Full-stack (sekcja 6, `FullStackToolbox.cs`)**: szkielet REST z encji, OpenAPI YAML,
  `encja ts:` / `encja csharp:`, `migracja sql:`, `sql indeks:`, `compose:`, `cors:`, `env:`,
  `dostep:` (macierz ról), `status http:` (22 kody; nieznany → uczciwe „nie znam”), `rest tabela`,
  `walidacja:`, `relacja:` (1:n / m:n / 1:1 z SQL), `paginacja:` (offsety, keyset, stabilne sortowanie).
- **Wiedza (sekcja 8, `KnowledgeToolbox.cs`)**: `fiszki:`, `anki:` (TSV do importu), `powtorki:`
  (odstawy 1/3/7/16/35 dnia, z datami), `slownik pojec:`, `podobienstwo:` (kosinus na workach słów —
  czysta arytmetyka, wypisane ograniczenie „to nie ta sama myśl”), `wspolne tematy:`, `mapa wiedzy:`,
  `indeks pojec:`, `pytania kontrolne:`.
- **Research (sekcja 12, `ResearchToolbox.cs`)**: `cytuj apa|ieee:`, `bibliografia:`,
  `wiarygodnosc:` (checklisty wg typu źródła), `plan badan:`, `slowa kluczowe:` (stopwordy PL/EN),
  `zapytanie:` (4 warianty z operatorami — do wklejenia, **nic nie pobieram**), `macierz porownania:`,
  `podsumuj notatki:` (wycinkowo, tylko zdania użytkownika), `fakt zapisz:`, `pytania badawcze:`.
- **Smart home (sekcja 18, `SmartHomeToolbox.cs`)**: `energia:` (kWh i zł, rok), `koszt urzadzen:`,
  `termostat:` (reguła 5–7% na 1 K, z zastrzeżeniem „sprawdź na liczniku”), `scena dom:`,
  `yaml automatyzacji:` (do wklejenia; Sentinel niczego nie łączy), `mqtt:` (tematy i symbole),
  `prad:`, `luminy:`, `czujnik baterii:`, `tarif:`.
- **Cele agentic (sekcja 20, `GoalToolbox.cs`)**: `cel rozloz:`, `plan krokow:` (wymusza krok
  weryfikujący), `czas na zadanie:` (+25% bufor), `plan wycofania:`, `polityka autonomii` (trzy poziomy
  zgód — dokładnie tak, jak działa Sentinel), `ryzyko:` (klasyfikacja po czasownikach),
  `samoocena:`, `definicja sukcesu:` (SMART).
- **Media i obraz (sekcje 9–10, `MediaVisionToolbox.cs`)**: `kontrast:` (WCAG z klasyfikacją AA/AAA),
  `ppi:`, `proporcje:` (GCD), `bitrate wideo:`, `audio czas|rozmiar:`, `tempo mowy:`, `db:`.
- **Dokładki (sekcje 7, 11, 13)**: `jezyk:` (7 języków po stopwordach; „nie zgaduję” gdy brak trafień),
  `i18n:` (braki kluczy między wersjami), `webhook szablon:` (HMAC + idempotencja),
  `token bucket:`, `retry plan:` (backoff ×2 + jitter; 4xx nie ponawiane), `sesje:`, `koszt spotkania:`,
  `godziny pracy:`.
- **Dokładki po pierwszej zieleni pakietu**: „wykres: 3 5 8 4 | Sty Lut Mar Kwi” renderuje
  słupkowy **PNG** (`ChartCommands.cs`, WPF DrawingVisual → RenderTargetBitmap) i zapisuje go w danych
  aplikacji (ścieżka i statystyki w odpowiedzi); „indeks zbuduj: folder” / „indeks szukaj: fraza” /
  „indeks status” (`KnowledgeIndexService.cs`) skanują .txt/.md do mapy słów **tylko w RAM** (200 plików,
  głębokość 3, 2 MB/plik — nic na dysk); „plan tygodnia: pn=…; wt=…” daje siatkę tygodnia.
- **RAG bez chmury (sekcja 8, `KnowledgeRagService.cs` + `LocalAiService.GetEmbeddingAsync`)**:
  „rag zbuduj: folder” (fragmenty ≤ 800 znaków z zakładką 120, wektory z nomic-embed-text przez
  loopback, limit 200 plików / 400 fragmentów), „rag szukaj: fraza” (kosinus, top 5 z procentem
  i nazwą pliku), „rag prompt: pytanie” (gotowiec z obowiązkowymi źródłami), „rag model/status/reset”.
  Baza tylko w RAM; bez Ollamy jawny komunikat; nieudana budowa nie czyści bazy. Regresja:
  `tests/RagRegression.cs` na deterministycznych fejkowych wektorach (worki słów, 16 kubełków),
  przejście przez realny router — 33 zestawy. Dla maszyny użytkownika: `scripts/setup-rag.ps1`
  (jedno polecenie: sprawdza dotnet/ollama, pociąga modele, NIE buduje EXE) i `docs/START-RAG.md`.
- **Router ma teraz jawną regresję dispatchu**: `ModelManagementRegression` przechodzi przez realny
  `CommandRouter` także dla architektury, researchu, indeksu i wykresu — dziura w dispatchu (moduły bez
  wpięcia po inkydencie z force-pushe) nie może się już powtórzyć niezauważona.
- Wszystko offline, bez sieci i bez modelu; każdy moduł ma własny zestaw regresji z liczbami
  do sprawdzenia na kalkulatorze — łącznie **32 zestawy** w `--ui-smoke`.

## 17. WiFi 📶 i Game Dev (Roblox) — 0.98, najważniejsza funkcja

- **WiFi — jedyny wyłącznik internetu**: domyślnie **WYŁĄCZONE**; włączenie to jawnie Twoja decyzja
  (przycisk 📶 w pasku bocznym — niebieska ikona = włączone, przygaszona = wszystko lokalnie; albo
  `wifi on` / `wifi off` w czacie; stan przeżywa restart). Za WiFi: `szukaj w sieci: fraza` (DuckDuckGo
  Lite, tytuły + adresy + fragmenty), `strona: https://…` (HTML zdejmowany do tekstu, limit 512 KB).
- **Tarcza**: wyłącznie https; localhost, sieci prywatne (RFC1918, pętla zwrotna, łącza lokalne, ULA IPv6)
  **zawsze zablokowane — nawet przy włączonym WiFi** (SSRF). Limity czasu (12 s) i rozmiaru. Zero cookies,
  zero telemetrii; blokada anty-botowa = uczciwy komunikat, nie zmyślone wyniki.
- **Game Dev (Roblox)** — offline: plan nauki w 8 etapach; generatory **skryptów Luau według oficjalnych
  najlepszych praktyk**: `roblox skrypt: leaderstats` (DataStore + pcall + BindToClose), `killbrick`
  (debounce), `checkpoint` (CharacterAdded + PivotTo), `sklep` (ProcessReceipt idempotentny), `zdalne`
  (walidacja typu + rate-limit na serwerze), `tween`, `narzedzie` (cooldown). Dalej: `roblox struktura`,
  `projektowanie` (core loop, pierwsze 60 sekund, MDA), `modelowanie` (Anchored, skala 1 stud ≈ 28 cm,
  Future/ShadowMap), `optymalizacja` (StreamingEnabled, pomiary MicroProfilerem), `checklist` wydania,
  `pojecie: …` (15 pojęć), `szkic: nazwa` (GDD z rozpoznaniem gatunku), `monetyzacja: N` — **prawdziwe
  przeliczniki**: DevEx 0,0035 USD/R$, minimum 30 000 R$, prowizja 30% (gracz płaci 100 → twórca 70).
- **Game Dev za WiFi**: `roblox najlepsze: temat` i `roblox przyklad: temat` celują zapytaniem w
  create.roblox.com i devforum.roblox.com; `roblox nowosci` w release notes. Bez WiFi odmowa ze wskazaniem
  `wifi on` — decyzja zawsze po stronie użytkownika.
- **Regresja**: `tests/WebAccessRegression.cs` (fejkowa sieć: parsowanie DDG Lite, dekodowanie uddg,
  tarcza SSRF, https-only, trwałość wyłącznika, routing) i `tests/GameDevRegression.cs` (szablony
  dosłownie z pcall/BindToClose/idempotencją, matematyka DevEx 10 000 R$ = 35,00 USD i 35 000 = 122,50 USD,
  słownik, plan nauki, szukanie za fejkowym WiFi).
- **GameForge — generator gier (0.98, dalej)**: wpisujesz JAKĄ grę chcesz —
  `roblox wygeneruj: <opis>` w SentinelX rozpoznaje gatunek i podaje gotowy `GameSpec.lua`;
  w grze `GameSpec.Genre` = jedno słowo zmienia CAŁĄ grę: `obby` (pełny Mega Obby, bez regresji),
  `symulator` (klik → siła → narodziny), `tycoon` (działki z dozownikami), `horror` (latarka,
  bezpieczniki, goniący potwór), `shooter` (raycast na serwerze), `wyscigi` (checkpointy, okrążenia).
  Wspólne: monetyzacja idempotentna, questa (w tym statystyczne), tablice wg gatunku, anty-cheat.
  Razem **41 zestawów** (doszły GameAssetsRegression, GameForgeRegression i MoneySystemsRegression).
- **Monetyzacja PRO (0.99)**: zestawy limitowane (STARTER 72 h z REALNYM licznikiem od
  pierwszego wejścia), koło fortuny z jawnymi szansami (wymóg Roblox dla płatnych losowań),
  SEZON — battle pass 30 poziomów z torem premium, zarobki offline z podwojeniem raz/dobę,
  globalne eventy x3 co 15 minut, bonus grupy +10%, onboarding 5 kroków. Wszystko przez
  ten sam idempotentny ProcessReceipt; playbook: `gamedev/roblox/mega-obby/README-MONEY.md`.
- **Różnorodność (0.99)**: generator gier ma teraz **9 gatunków** — doszły `biegacz`
  (endless runner z rekordem dystansu), `farma` (uprawy rosnące w czasie serwera,
  działki premium) i `fale` (obrona kryształu przed rosnącymi falami wrogów). Do tego
  trzecie jajko (legendarny Protopan Feniks ×1,55), ślady toxic/cyberpunk, **losowane**
  globalne eventy (x3 długie / x5 krótkie / dar dla wszystkich online) i nagrody
  dzienne ze spinami (dzień 3/5) oraz zwierzakiem (dzień 7). Razem **42 zestawy**
  regresji (doszła VarietyRegression).
- **UI 2.0 (0.99)**: aplikacja dostała **powiadomienia toast** (UiToastHost — sprężyna,
  stos, auto-fade, kolory z motywu) wpinane w stop awaryjny, wznowienie i przełącznik
  WiFi, oraz **szklany pasek statusu** na dole. UI gry Roblox zbudowano **od zera** na
  wspólnym `UiKit.lua` (glassmorphism: półprzezroczyste karty, sprężynowe animacje,
  toasty, konfetti, licznik „goniący” monety) — HUD i sklep mają teraz lewy rail
  zakładek, karty z uniesieniem na hover i paski postępu. **UiSmoke v2**: test UI
  uczy się głębiej — pilnuje cyklu życia powiadomień, paska statusu i dostępności
  (przycisk bez etykiety dla czytnika ekranu = czerwone CI).
- **Redesign „Claude” (0.99)**: interfejs w duchu najlepszych GUI — ciepłe tony
  (ivory/węgiel), terakotowy akcent, **szeryfowe nagłówki**, płaskie cienie,
  spokojniejsze animacje; pełny audyt kontrastu WCAG w generatorze motywów.
- **Duża gra (0.99, Fisch-style)**: **10. gatunek generatora — `ryby`**: brania
  losowane wagami rzadkości (wspólne → legendarna, wagi jawne w configu), mutacje
  ×2/×3, łódka na GŁĘBINY, **pogoda** rotująca na serwerze ze „szczęściem” do ×2,
  **ZBIORY/INDEKS** z nagrodą za komplet i **OSIĄGNIĘCIA** (progi wspólne dla
  wszystkich gatunków). **43 zestawy** regresji (doszła BigGameRegression).

## 18. Zestawy regresji po tym przyroście


`--ui-smoke` uruchamia teraz **30 zestawów**: dotychczasowe 22 plus `architecture`, `research`,
`knowledge`, `goals`, `fullstack`, `smarthome`, `media-vision` i `everyday` (pozostałe sekcje naraz).
Zestawy działają na jawnych danych i nie zależą od sieci ani od modelu.
