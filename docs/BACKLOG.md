# Trwały backlog rozwoju SentinelX

Stan na 2026-09-25 (0.92). Zasada: każda pojedyncza pozycja znika z listy dopiero, gdy jest **zbudowana i przetestowana na Windows (CI)**, nie „gdy wygląda na zrobioną".

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

## Gotowe w 0.90 (ten przyrost — patrz git)

- **Rozumienie literówek** (`CommandUnderstanding`): Damerau-Levenshtein, progi 0,80 / margines 0,08, naprawa frazy i pojedynczych słów, jawny komunikat „Zrozumiałem jako: …”. Katalog napraw (`Core/IntentCatalog`) **nie zawiera poleceń niszczących** — pilnuje tego test.
- **Skróty** (18, jawne, komenda `skróty`): cs, dc, st, ch, nt, kl, yt, mz, dk, sp, sn, sns, zs, tp, ti, zd, pr, pm.
- **Uruchamianie programów z dowodem**: 7 tytułów Steam (CS2, Dota 2, Cyberpunk 2077, Wiedźmin 3, Elden Ring, Terraria, Rust), aplikacje po ścieżce instalacji (VS Code, VLC, OBS, Firefox, Edge, Telegram), systemowe (Paint, Terminal), reszta przez skróty menu Start.
- **24 narzędzia offline** (`UtilityToolbox`, czyste funkcje): kalkulator z własnym parserem, procenty, VAT, przelicznik jednostek i temperatur, daty i dni tygodnia, hasło (stock crypto), UUID, licznik słów, Base64 w obie strony, SHA-256, walidacja i formatowanie JSON, slug, transliteracja, wielkie/małe litery, odwracanie tekstu, losowanie, kostki, wybór losowy, BMI, liczby rzymskie w dwie strony, kolor (RGB/HSL/kontrast WCAG).
- **5 funkcji przekrojowych** (`WorkspaceInsightsService`): `plan dnia`, `szukaj wszystkiego: fraza`, `statystyki`, `backup` z manifestem i odczytem zwrotnym, `pomoc` / `co umiesz`.
- **Własny folder i comiesięczne archiwum pamięci** (`MemoryArchiveService`): `Memory/Archives/RRRR-MM/` (JSON + Markdown + SHA-256), usunięcie z magazynu dopiero po zgodnym odczycie zwrotnym, ustawienie „Archiwum rozmów (miesiące)” (domyślnie 1), komendy `archiwizuj rozmowy` / `archiwa` / `usuń archiwum RRRR-MM`, odmowa w trybie prywatnym, wspomnienia i profil nietknięte.
- **Pliki do pobrania z GitHub**: `.github/workflows/release.yml` (tag `v*.*.*` i ręczny dispatch) publikuje portable ZIP, instalator EXE, `SHA256SUMS.txt`, `BUILD.txt`.
- Zestawy `tests/UnderstandingRegression.cs`, `tests/UtilityRegression.cs`, `tests/MemoryArchiveRegression.cs` w `--ui-smoke` (CI) + 31 dodatkowych sprawdzeń end-to-end przez prawdziwy silnik czatu.
- Dokument `docs/ARCHITECTURE.md`: warstwy, ścieżka polecenia, reguły dowodów, granice.

## Gotowe w 0.91 (ten przyrost — patrz git)

- **GUI „CENTRUM”**: sidebar 4 pozycje (Centrum/Pamięć/Projekty/Ustawienia), w Centrum 9 zakładek-ikon bez podpisów (💬📓🕘🎤🖥🎮✨⚡🩺), rename „Command Center”→„Centrum”, poświata aurora za treścią, przełącznik mikrofonu w nagłówku. Smoke renderuje każdą zakładkę.
- **Paleta `//`** w czacie (`Core/SlashCatalog` + `Utilities/SlashKeys`): Tab/Shift+Tab/Enter/Esc, ~40 wpisów, dokładne skróty działają też jako wysłane polecenia (`//diag` → diagnostyka komputera).
- **Głos domyślnie WŁ** przy starcie (świadoma decyzja użytkownika) + wskaźnik i szybkie wyłączenie.
- **Szara strefa rozumienia** (`CommandUnderstanding.Suggest`): podobieństwo 0,62–0,80 → pytanie „Czy chodziło Ci o: …”, „tak” wykonuje; bez propozycji dla poleceń niszczących.
- **Dziennik lekcji** (`Services/Intent/UnderstandingJournal`, `Memory/Lessons.jsonl`, limit 500) + komenda `lekcje`.
- **`zrob zadanie:`** tworzy zadanie w zakładce 📓; `notatka:` alias zapamiętaj; `szukaj w zadaniach:`.
- **`samokontrola`** i **`propozycje`** (`WorkspaceInsightsService`) — tylko odczyt i lista „wpisz: …”, nic bez zgody.
- **~50 narzędzi offline** w `UtilityToolbox`: matematyka (pierwiastek/silnia/nwd/nww/pierwsze/dzielniki/fibonacci/statystyki/zaokrąglenia/zmiana %), tekst (znaki/zdania/palindrom/anagram/rot13/tytuł), kodowania (morse/binarnie/hex w obie strony), PL identyfikatory (PESEL/NIP/IBAN — walidacja lokalna), rgb→hex, kalendarz (tydzień/dzień roku/koniec roku/wiek/dni robocze/wielkanoc), zegary świata (8 miast), fakty systemu (nazwa/rdzenie/architektura/lokalne IP), losowe (moneta/lotto/PIN).
- **Uczciwe odmowy wbudowane**: modele 3D, samomodyfikacja kodu, automatyczne skanowanie dysku.
- 5 nowych skrótów (razem 23), katalog fraz rozszerzony, `tests/UtilityRegression` + `tests/UnderstandingRegression` + `UiSmokeTestRunner` rozszerzone.

## Gotowe w 0.91.1 (ten przyrost — patrz git)

- **Słowo-klucz w dowolnym miejscu zdania** (żądanie użytkownika): `CommandText.ContainsWakeWord/StripWakeWord` rozpoznaje i wycina „sentinel” (z wariantami pisowni) z dowolnego miejsca wypowiedzi; `VoiceService` wykonuje polecenie tylko ze słowem-kluczem — bez niego nic nie robi i dalej nasłuchuje. Zdania zaczynające się inaczej niż „Sentinel…” (np. „ile mam ramu, sentinel”) wreszcie działają. Pokryte nowymi asercjami w `UiSmokeTestRunner`.
- **Naprawa limitywnika `Suggest`**: podpowiedź szarej strefy działa tylko dla krótkich poleceń (≤60 znaków i ≤4 wyrazy) — dłuższe pytania konwersacyjne („Czy moje obecne użycie CPU i RAM wygląda dobrze do grania?”) idą prosto do modelu zamiast utknąć w podpowiedzi (regresja `--self-test`).
- **Wydanie rolling**: `release.yml` na `workflow_dispatch` publikuje każdy upgrade pod stabilnym tagiem `rolling` (ZIP portable + instalator EXE + sumy SHA-256 + BUILD.txt) po pełnej walidacji; tagi `v*.*.*` pozostają wydaniami milowymi. Link: `releases/tag/rolling` w README i `docs/RELEASE-0.91.md`.

## Gotowe w 0.91.2 (ten przyrost — patrz git)

- **Bezwzględna bramka słowa-klucza** (żądanie użytkownika): każde polecenie głosowe wymaga „sentinel” w zdaniu (dowolne miejsce); bez niego Sentinel niczego nie wykonuje i nasłuchuje dalej. Usunięty wyjątek „okna rozmowy”, który wykonywał zdania bez wybudzenia przez kilka minut po ostatnim poleceniu — to była luka względem zasady użytkownika.
- Pierwsze automatyczne wydanie rolling opublikowane i zweryfikowane: `releases/tag/rolling` (SentinelX-0.91.1-win-x64-portable.zip 87,7 MB, SentinelX-0.91.1-win-x64-setup.exe 74,6 MB, SHA256SUMS.txt, BUILD.txt), publikacja po pełnej walidacji w CI. Tag milowy `v0.91.2` opublikowany tą samą ścieżką.

## Gotowe w 0.92 (ten przyrost — patrz git)

- **Bezpieczna praca na plikach (P0.1 — część)**: `Services/Files/FileCleanupService` — `duplikaty: <folder>` (SHA-256, grupy + odzyskiwalne bajty, tylko odczyt), `porzadki: <folder>` (największe/puste/najstarsze/rozmiar, tylko odczyt), `usuń do kosza: <ścieżka>` z dwustopniowym potwierdzeniem i jednorazowym pending (późniejsze „tak” nie usuwa). Limity: 50 000 plików, ≤256 MB hashowane, junction/symlinki nieprzechodzone, dowiązania nieusuwalne. Audyt: FILE_SCAN_DUPLICATES/FILE_SCAN_TIDY/FILE_RECYCLE.
- `tests/FileCleanupRegression.cs` (w tym test junction przez `mklink /J`) + asercje end-to-end w smoke UI; skróty `//duplikaty`, `//porzadki` w palecie.

## Gotowe w 0.93 (ten przyrost — patrz git)

- **Wykonywalne porządki (dokończenie P0.1)**: `FileCleanupService` rozszerzony o:
  - `zmien nazwy: <folder> z <old> na <new>` oraz `zmien nazwy: <folder>: <old> -> <new>` — skan top-level (max 200), podgląd old→new (max 20), walidacja kolizji i nieprawidłowych znaków, wykonanie dopiero po „potwierdz”, jednorazowy pending, audyt FILE_BATCH_RENAME / FILE_BATCH_RENAME_PREVIEW.
  - `uporzadkuj: <folder>` (aliasy: `posprzątaj`, `wykonaj porzadki`) — znajduje puste pliki 0 B (rekurencyjnie, limit 50k), proponuje przeniesienie do Kosza (max 100 na raz) z podglądem i potwierdzeniem, audyt FILE_TIDY_EXEC.
  - `usun duplikaty: <folder>` — skan duplikatów jak w 0.92, proponuje zachowanie pierwszego pliku z grupy (alfabetycznie) i przeniesienie reszty do Kosza (max 100), podgląd grup (max 10), potwierdzenie, audyt FILE_DUPLICATES_CLEANUP.
- Wszystkie operacje mutujące: dwustopniowe, jednorazowe (późniejsze „tak” nie odpala starego pending), tylko do Kosza (przywracalne) lub rename bez nadpisywania, junction/symlinki nieprzechodzone i nieusuwalne, limity twarde.
- `tests/FileCleanupRegression.cs` rozszerzony o pełny cykl rename (podgląd → brak zmian → stale tak → ponowna propozycja → potwierdz → zweryfikowane przemianowanie), arrow syntax, kolizje, bare commands, tidy exec (puste pliki), duplicate batch cleanup (zachowanie jednego). Smoke UI: nowe polecenia przez prawdziwy silnik + skróty `//zmien-nazwy`, `//uporzadkuj`, `//usun-duplikaty` w palecie.

## Priorytet P0 — kolejny przyrost

1. **Pliki — domknięte w 0.93**: P0.1 w całości zrobione (duplikaty SHA-256, raport porządkowy, kosz pojedynczy, zbiorcze zmiany nazw z podglądem, wykonywalny plan porządkowy dla pustych plików, usuwanie wielu duplikatów z listą i potwierdzeniem). Zostały rozszerzenia P1 (diff, filtry).
2. **Sekwencje działań**: nazwane kroki, plan z podglądem, statusy kroków, zatrzymanie na błędzie, wznowienie tam, gdzie to sensowne.
2. **Sekwencje działań**: nazwane kroki, plan z podglądem, statusy kroków, zatrzymanie na błędzie, wznowienie tam, gdzie to sensowne.
3. **Panel archiwum w UI**: strona/panel z listą archiwów (miesiąc, rozmowy, wypowiedzi, rozmiar, hash), podglądem Markdowna i usuwaniem — dziś archiwum działa z czatu i z ustawień, ale nie ma własnego widoku.
4. **Podpisywanie buildów**: certyfikat (płatny) albo jawnie opisana ścieżka bez podpisu z instrukcją odblokowania SmartScreen; dziś pliki są niepodpisane i tak jest opisane.
5. **Integracje odłożone z 0.91** (decyzja użytkownika): GitHub i Gmail — dopiero jako osobny przyrost, wyłącznie za jawną zgodą i z lokalnym przechowywaniem poświadczeń stockowym crypto Windows.
6. **Motywy**: pełny motyw „szkło/Jarvis” (dziś aurora to warstwa dekoracyjna; okno nieprzezroczyste z powodów wydajności/kompatybilności).

## Priorytet P1

- Pliki — rozszerzenia poza P0: podgląd diff przed zmianą treści, filtrowanie duplikatów po rozmiarze przed hashowaniem.
- Tryb gry: zawieszanie indeksowania/ciężkich zadań, profil modeli z histerezą przełączania (progi + opóźnienie), zwalnianie modelu opcjonalne.
- Sekwencje działań: nazwane kroki, plan z podglądem, statusy kroków, stop na błędzie, wznowienie tam gdzie sensowne.
- Rozmowa: edycja wypowiedzi i ponowne wysłanie z oznaczeniem alternatywy; zakładanie odgałęzień rozmowy.
- Wyszukiwanie (wspomnienia, rozmowa, `szukaj wszystkiego`) z tolerancją literówek — dziś naprawa dotyczy poleceń, nie treści wyszukiwania.
- Narzędzia: przelicznik walut (wymaga kursu — offline nieuczciwe), własne stawki VAT (8/5/0%), przywracanie archiwum do nowej rozmowy, skróty konfigurowalne przez użytkownika.
- Rozbicie `CommandRouter` (~640 linii po 0.91) na osobne moduły: pamięć, projekty/zadania, snapshoty, narzędzia, meta — dopiero przy zielonym CI.
- Z 0.91 odłożone: dalsze ~40 pozycji z listy „100 funkcji” (m.in. edycja plików konfiguracyjnych z podglądem diff, rozszerzony zegar świata, profile skrótów użytkownika, `czas w` dla dowolnej strefy, święta w `dni robocze`) — bez udawania, że istnieją.
- Modele 3D i generowanie modeli — poza zakresem produktu (wbudowana uczciwa odmowa); gdyby wróciły, to jako integracja z zewnętrznym narzędziem za zgodą.
- Samomodyfikacja kodu — odrzucona na stałe decyzją użytkownika; odpowiednikiem są `samokontrola` i `propozycje`.

## Priorytet P2

- Wyszukiwanie semantyczne lokalne: tylko jako opcjonalny indeks (bez wpływu na tryb tekstowy); najpierw benchmark jakości na scenariuszach PL.
- Integracja z autostartem/Harmonogramem zadań dla przypomnień — wyłącznie za jawną zgodą, z opcją cofnięcia.
- Alerty progów diagnostyki z historią krótkich pomiarów (z limitem wzrostu plików).

## Świadome ograniczenia obecnego stanu

- Brak lokalnego kompilatora .NET w środowisku deweloperskim — weryfikacja wyłącznie przez CI na `windows-latest` (opis w README).
- Build niepodpisany: SmartScreen może ostrzegać (zgodnie z README; nie wyłączamy zabezpieczeń).
- Ollama, mikrofon i gry wymagają testów na komputerze docelowym; CI nie ma mikrofonu ani gier.
- Wyszukiwanie **treści** (wspomnienia, rozmowa, `szukaj wszystkiego`) jest tekstowe (normalizacja, bez fuzzy) — literówki mogą nie trafić. Tolerancja literówek dotyczy **poleceń**, nie wyników wyszukiwania.
- Naprawa poleceń działa na stałym katalogu ~240 fraz (0.91): nie rozumie odmiany ani kontekstu, a przy niejednoznaczności celowo nic nie poprawia.
- `przelicz` nie przelicza walut, brak zrzutów ekranu i sterowania głośnością — świadomie odłożone, nie „zrobione częściowo”.
- VAT liczy tylko stawkę 23%.
- Archiwum rozmów jest kopią do odczytu: nie ma przywracania do aktywnej rozmowy.
- Limit magazynu artefaktów GitHub Actions został osiągnięty 2026-09-24: przebieg `36049020743` miał **13/13 kroków merytorycznych `success`**, a czerwony znacznik pochodzi wyłącznie z `upload-artifact` („Artifact storage quota has been hit”). Retencja artefaktów skrócona do 2 dni; trwałą dystrybucją są assety wydania. Gdyby limit dalej blokował, kolejne kroki: `retention-days: 1` albo rezygnacja z uploadu binariów w buildzie gałęzi.
- Streaming AI nie jest weryfikowany z żywym modelem w CI (brak Ollama w środowisku testowym) — transport i logika fragmentów są testowane na wstrzykniętym HTTP.
