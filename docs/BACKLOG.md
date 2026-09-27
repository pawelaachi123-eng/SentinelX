# Trwały backlog rozwoju SentinelX

Stan na 2026-09-27 (0.97). Zasada: każda pojedyncza pozycja znika z listy dopiero, gdy jest **zbudowana i przetestowana na Windows (CI)**, nie „gdy wygląda na zrobioną".

**Bramki CI są zielone (2026-09-27):** `Windows build and WPF smoke` — success (restore, build, `--ui-smoke` z 17 zestawami regresji, `--self-test`), `Build Windows app` — success. Kroki publikacji EXE/instalatora nadal uruchamiają się wyłącznie przy `workflow_dispatch` (sekcja 8 `docs/RELEASE-0.97.md`). Kolejny przyrost pracuje na zielonej bazie.

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
- Pierwsze automatyczne wydanie rolling opublikowane i zweryfikowane: `releases/tag/rolling` (SentinelX-0.91.1-win-x64-portable.zip 87,7 MB, SentinelX-0.91.1-win-x64-setup.exe 74,6 MB, SHA256SUMS.txt, BUILD.txt), publikacja po pełnej walidacji w CI. Tag milowy `v0.91.2` opublikowany tą samą ścieżką. Ostatnie rolling: `0.92.0`.
- **Decyzja użytkownika 2026-09-25**: automatyczne publikowanie EXE przy każdym upgrade WYŁĄCZONE — `release.yml` publikuje tylko dla tagów `v*.*.*` lub na ręczny dispatch; zwykły push nic nie publikuje.

## Gotowe w 0.92 (ten przyrost — patrz git)

- **Bezpieczna praca na plikach (P0.1 — część)**: `Services/Files/FileCleanupService` — `duplikaty: <folder>` (SHA-256, grupy + odzyskiwalne bajty, tylko odczyt), `porzadki: <folder>` (największe/puste/najstarsze/rozmiar, tylko odczyt), `usuń do kosza: <ścieżka>` z dwustopniowym potwierdzeniem i jednorazowym pending (późniejsze „tak” nie usuwa). Limity: 50 000 plików, ≤256 MB hashowane, junction/symlinki nieprzechodzone, dowiązania nieusuwalne. Audyt: FILE_SCAN_DUPLICATES/FILE_SCAN_TIDY/FILE_RECYCLE.
- `tests/FileCleanupRegression.cs` (w tym test junction przez `mklink /J`) + asercje end-to-end w smoke UI; skróty `//duplikaty`, `//porzadki` w palecie.
- P0.1 domknięte w 0.93: usuwanie duplikatów z listy, sprzątanie pustych plików, zbiorcze zmiany nazw z podglądem — wszystko za jawną zgodą.

## Gotowe w 0.93 (ten przyrost — patrz git)

- **`usuń duplikaty: <folder>`**: z każdej grupy identycznych treści zostawia 1 plik (najkrótsza ścieżka), resztę po jawnym `potwierdz` przenosi do Kosza; wynik per plik (VERIFIED/PARTIAL/FAILED) w audycie `FILE_RECYCLE`. Propozycja zawsze pokazuje pełną listę przed zgodą; `anuluj`/inne polecenie porzuca operację.
- **`usuń puste pliki: <folder>`**: pliki 0 B po jawnym `potwierdz` do Kosza (ten sam mechanizm jednorazowej zgody).
- **`zmien nazwy: <folder> zamien X na Y`**: zbiorcza zmiana nazw plików bezpośrednio w folderze — podgląd `stara → nowa`, kolizje i niebezpieczne nazwy pomijane i ogłoszone, wykonanie po `potwierdz` (`File.Move` bez nadpisywania), audyt `FILE_RENAME`.
- **Decyzja użytkownika**: automatyczne wydanie rolling WYŁĄCZONE — `release.yml` publikuje tylko tagi `v*.*.*` i ręczny dispatch; zwykły push tylko buduje i testuje. **Nie publikować EXE bez wyraźnej prośby.**
- Skróty `//sprzatanie`, `//nazwy` w palecie; `co nowego` zaktualizowane.
- **P0.1 „plików” domknięte** — wszystkie pozycje zbudowane i testowane w CI.
- **GUI: pełna warstwa wizualna 0.93 (nowoczesny design bez nowych pakietów NuGet)** — żądanie użytkownika: „o wiele bardziej nowoczesne, nie tylko 2 kolory i zero animacji”.
  - System tokenów `Sx*` w `Themes/`: 71 tokenów koloru (tła, powierzchnie, 3 poziomy tekstu, 5 akcentów + warianty Dim/Soft, semantyka z końcami gradientów, obramowania, fokus, cienie, shimmer, reveal, dymki czatu, aurora, overlay, scrim), tokeny promieni i odstępów (`CornerRadius`/`Thickness`), typografia (3 rodziny, 12 rozmiarów, 12 styli), gradienty i efekty (akcent, fiolet–róż, mięta–limonka, błękit–cyjan, success, danger, warning, shimmer, pasek postępu, reveal, aurora, 3 cienie, poświata).
  - `scripts/generate-themes.py` generuje `DarkTheme/DeepDarkTheme/LightTheme.xaml` i liczy kontrast WCAG dla par krytycznych (tekst/tło, biały na końcach gradientów akcentu i semantyki, tekst stanu na jego tle) — `--check` jest bramką: `PASS: 71 tokenów × 3 motywy, kontrast OK`.
  - `Themes/Animations.xaml`: 4 krzywe łatwości i 35 storyboardów (wejście strony, wejścia modalne, scrim, hover, press, glow, sheen, lift, pasek nawigacji, nudge, linia, suwak, chevron, puls twardy i miękki, ripple, oddech, busy, shimmer, pasy postępu, dryf aurory).
  - `Utilities/Motion.cs` (attached: `Entrance`, `BubbleIn`, `Hover`, `Pulse`, `Shimmer`, `Stripes`, `Aurora`, `ModalIn`) i `Utilities/Reveal.cs` — animacje pętlące są bramkowane przez `ClientAreaAnimation` Windows **i** `SxAnimationsEnabled` (ustawienie „Animacje” + automatyczne wyłączenie podczas gry); animacje jednorazowe hover/press zostają w triggerach szablonów i szanują ustawienie systemu.
  - Królewski herb marki (prośba użytkownika): styl `SxCrest` (tarcza + korona + cyjanowy klejnot, Viewbox), złoty wordmark, tokeny `SxRoyalGold*` w trzech motywach, `SxRoyalGradient`/`SxRoyalGlow`; nowa ikona `Assets/sentinel.ico` z tym samym herbem (wpisy PNG 16–256) dla tytułu okna, EXE i instalatora.
  - Shell: sidebar 268 px z gradientem, herb `SxCrest`, pill `Ctrl+K`, badge głosu z pulsującą kropką, aurora w tle treści, przejścia stron, modalne paleta i panel gotowości ze scrimem; zakładki Centrum mają **widoczne podpisy** (nie tylko ikony).
  - 12 stron + overlay zbudowane od nowa na tokenach: nagłówek (pasek akcentu + eyebrow + tytuł + opis), kafelki metryk z gradientowym paskiem, karty z reveal i wejściem, paski stanu z kolorem **i** etykietą tekstową, stany zajętości z shimmerem, stany puste, uczciwe „Niedostępne” zamiast zmyślonych procentów (VRAM/RAM bez fabrykowanych wartości).
  - `UiSmokeTestRunner.cs`: zero usuniętych asercji, ~120 nowych sprawdzeń tokenów (kolory po przełączeniu motywu, promienie/odstępy, style, storyboardy, gradienty, efekty) + dotychczasowa bramka „zero błędów wiązań”.
  - `scripts/check-architecture.py`: nowa bramka „XAML pitfalls” (6 reguł) — `BeginStoryboard` bez `TargetName`, `Setter TargetName` tylko w szablonie, `Style` ustawiony raz, `StringFormat` z ucieczką `{}`, brak duplikatów `x:Name`, właściwości dołączone `u:*` muszą istnieć. Reguły odpowiadają błędom, które realnie zatrzymały build w CI (MC3072, MC1000); każda sprawdzona wstrzyknięciem błędu.
  - Paski pomiarów CPU/GPU są odporne na `NaN` z `SystemSnapshot.Empty`: `SafePercent` + `FiniteToVisibility` (pasek znika zamiast pokazywać 0%, tekst mówi „Niedostępne”) — `RangeBase` odrzuca wartości niekończone, więc bez konwertera pierwsze malowanie okna kończyłoby się błędem wiązania.
  - Animacje części szablonu (poświata i błysk przycisku, pasek nawigacji, podkreślenie zakładki i pola, gałka przełącznika, strzałka listy) wyzwalają `DataTrigger`y w stylu samej części z `RelativeSource TemplatedParent` — storyboard działa wtedy na ten element i nie potrzebuje nieistniejącego `BeginStoryboard.TargetName`. Scrim palety i panelu gotowości wchodzi `DataTrigger`em na `IsOpen`, nie `EventTrigger Loaded` (ten przy stałej obecności w drzewie zagrałby raz, na starcie).
  - `App.xaml` scala `Themes/Animations.xaml` **przed** `Themes/Controls.xaml` (odstępstwo od opisu w §2.3 pkt 1 master promptu, opisane w `docs/RELEASE-0.93.md`): szablony sięgają po storyboardy przez `StaticResource`, a ten widzi tylko słowniki scalone wcześniej. Przy starej kolejności aplikacja nie startowała wcale — `XamlParseException` przed `OnStartup`, więc krok smoke w CI kończył się bez katalogu wyników i bez jednego logu (przebieg `36235263621`). W `Themes/Controls.xaml` został też jeden styl domyślny `ListBoxItem` zamiast dwóch (`BasedOn SxListItem` i `BasedOn SxSidebarItem`): dwa style bez `x:Key` o tym samym `TargetType` to duplikat klucza `typeof(ListBoxItem)` = `ArgumentException` przy ładowaniu słownika. Nawigacja i tak ma `ItemContainerStyle="{StaticResource SxSidebarItem}"`.
  - `scripts/check-architecture.py` +2 bramki: **kolejność zasobów** (symulacja ładowania `App.xaml` — każdy `StaticResource` musi istnieć w chwili użycia, także styl domyślny przez `{x:Type}`, oraz brak duplikatu stylu domyślnego w jednym słowniku) i **ścieżki animacji** (element animowany storyboardem musi mieć `RenderTransform` z żądanym dzieckiem `TransformGroup` pod żądanym indeksem; storyboardy grane z `Utilities/` mieszczą się w konwencji `[Scale, Translate]` z `Motion.EnsureTransform`). Każda sprawdzona wstrzyknięciem błędu.
  - `Utilities/Reveal.cs` trzyma warstwę poświaty we właściwości dołączonej zamiast `FindName("PART_RevealLayer")` — element z kodu nie rejestruje się w namescope strony, więc karta zawijałaby się w kolejną siatkę przy każdym ruchu myszy. `Utilities/CrashLogger.cs` dostał inicjalizator modułu, który dla `--ui-smoke` rejestruje `AppDomain.UnhandledException` jeszcze przed `Main` i zostawia `FAILED.txt` w katalogu wyników: awaria ładowania zasobów przestaje być w CI niewidoczna (`.github/workflows/**` bez zmian).
  - Bez zmian: wersja 0.93, etykiety i listy wyborów w `SettingsCatalog`, `Ui.SelectedPage`, legacy UI (`MainWindow.xaml`, `Theme.xaml`) — ani jednego znaku.

## Gotowe w 0.94 (ten przyrost — patrz git)

- **Rozumienie zdań** (`CommandUnderstanding.Understand`): skróty, naprawa literówek, dekodowanie poleceń ukrytych w uprzejmych zdaniach („sprawdź proszę ile mam ramu”) z zachowaniem argumentów i notą „Zrozumiałem jako: …”. Katalog fraz rósł do ~310 bezpiecznych pozycji; ogierki odmiany (`CommandLexicon.Stem`) i fold synonimów („odpal” → „wlacz”).
- **Bezpieczne granice rozumienia**: negacja, czasowniki rozmowy („napisz wiersz o tym ile mam ramu”), słowa zakresu („dziś”/„teraz”) i cokolwiek destrukcyjnego nigdy nie trafiają do automatycznego wykonania — lądują w „Czy chodziło Ci o…”. Ekstrakcja sięga wyłącznie katalogu bezpiecznych fraz.
- **`zrozum: <zdanie>`** — suchy pokaz kroków rozumienia, zero wykonania. Wywoływany przed całą resztą routingu.
- **Pamięć i kontekst**: `SessionFactBook` (ostatnie odczyty narzędzi do dopytań, ring 8, tylko RAM aplikacji) + komendy `fakty`, `co wiesz o mnie`, `podsumuj rozmowę`; data ISO + dzień tygodnia + aktywny projekt + „Wątek rozmowy” w kontekście modelu; `AiContextFilter` z punktacją (istotność + świeżość) zamiast „zostaw ogon”.
- **Wyszukiwanie nieostre**: wspomnienia i rozmowa tolerują odmianę i jedną literówkę (≥5 znaków: wspólny ogier albo edycja ≤1); dokładne zliczenia bez zmian (regresja pilnuje „ustalenie” == 3).
- **Zadania**: priorytet z tekstu („pilne/na juz/niski priorytet”) i `zadanie N priorytet wysoki|niski|normalny`.
- **~30 nowych narzędzi offline** (VAT wielostawkowy, finanse, matematyka, kalendarz, walidatory EAN/ISBN/LUHN/REGON, tekst, system, losowość) + `UtilityRegression` o ~60 asercjach; `tests/ContextRegression.cs` (fakty, wyszukiwanie, kontekst) w `--ui-smoke`.

## Gotowe w 0.95 (ten przyrost — patrz git)

- **Warstwa Jarvisa**: `timer`/`budzik` (na kanale przypomnień ⏰), `stoper start|stop`, głośność
  (Core Audio COM: odczyt/ustawienie/wyciszenie), `zrzut ekranu` (GDI BitBlt + PNG przez WPF),
  schowek (`kopiuj:`, `co w schowku`) — wszystko offline, z uczciwymi fallbackami.
- **Self-repair** (`napraw sie`): przywracanie magazynów z kopii, parkowanie uszkodzonych plików
  (`.corrupt-…`, nigdy kasowanie), czyszczenie JSONL, walidacja nauczonych wzorców.
- **Self-improve** (`ulepsz sie`): `Core/LearnedPatterns` — trwałe wzorce z literówek i
  akceptowanych propozycji; walidacja destrukcyjna przy zapisie i odczucie; nota
  „nauczone z Twojej poprawki”. Kod pozostaje nietknięty (decyzja użytkownika podtrzymana).
- **Self-check** rozszerzony o magazyn nauczonych wzorców; `tests/SelfMaintenanceRegression.cs`
  + asercje w `--ui-smoke` (SELF-REPAIR, timer, stoper).

## Gotowe w 0.96 (ten przyrost — patrz git)

- **Sterowanie pulpitem**: `okna` (lista z tytułami), `minimalizuj wszystko` (Windows+D),
  `minimalizuj/maksymalizuj/przywróć/zamknij okno`, `przełącz okno` (Alt+Tab),
  `przełącz na: fragment`, `okno w lewo/prawo`, `pełny ekran` — `Core/WindowManager.cs`
  + `Core/InputSender.cs` (user32, bez WinForms i bez nowych pakietów).
- **Multimedia**: `pauza`, `wznów odtwarzanie`, `następny/poprzedni utwór`,
  `zatrzymaj odtwarzanie`, `głośniej`, `ciszej` (±10%).
- **Ekran i zasilanie**: `zablokuj ekran`, `wygasz ekran`, `uspij komputer`, `zamknij komputer`,
  `restart komputera`, `anuluj zamknięcie`. Polecenie jest zgodą (decyzja użytkownika), a akcje
  nieodwracalne mają okno do odwołania (`Core/PowerManager.cs`, systemowy `shutdown.exe`).
- **Briefing**: `dzień dobry` i `dobranoc` — czysta funkcja `Core/JarvisBriefing.cs` nad danymi
  z lokalnych odczytów (zadania, przypomnienia, CPU/RAM/dyski/bateria, przypięte notatki).
- **Rutyny (sceny)** — domknięcie części P0 „sekwencje działań” w wariancie bezpiecznym:
  `rutyny` / `uruchom rutynę: X` / `dodaj rutynę: X = krok | krok` / `usuń rutynę: X`,
  magazyn `Memory/routines.json` (zapis atomowy, kopia uszkodzonego pliku, odczyt zwrotny
  SHA-256, wpięty w `samokontrola`), odrzucanie kroków niszczących dane, limit zagnieżdżenia 3.
- **`pomodoro N` / `przerwa N`**, **historia schowka** (pierścień sesji, bez zapisu na dysk),
  **`znajdź plik: fragment`** (Pulpit/Dokumenty/Pobrane, tylko odczyt, twarde limity).
- `tests/JarvisRegression.cs` + asercje w `--ui-smoke` (okna, rutyny, odmowa kroku niszczącego,
  briefing, pomodoro, historia schowka, `//rutyny`).

## Gotowe w 0.97 (ten przyrost — patrz git)

- **.NET 10 (GA) + C# 14**: `net10.0-windows`, `LangVersion 14.0`, pakiety Microsoft/System 10.0.0,
  SDK 10.0.x w workflow, bramka `check-architecture.py` pilnuje wersji (cofnięcie zapala CI).
- **Rdzeń `Core/Runtime/` (18 modułów)**: event bus, kolejka zadań (priorytety, ponowienia,
  kolejka zwrotów, anulowanie), bezpieczniki obwodu, cache LRU/LFU z TTL, cron 5-polowy,
  maszyna stanów cyklu życia, graf workflow (Kahn + pomijanie zależności po błędzie), flagi
  funkcji (FNV-1a), sprawdzenia zdrowia, metryki p50/p95, dziennik JSONL z rotacją, manifest
  integralności SHA-256, kopie ZIP z manifestem i weryfikacją, sejf AES-256-GCM (PBKDF2 210k),
  serializator binarny SXB1, wykonawca zadań, warstwa poleceń, `SentinelRuntime`.
- **Polecenia rdzenia** (patrz `docs/RELEASE-0.97.md`, sekcja 3): m.in. `kolejka …`, `cron …`,
  `flagi`/`ustaw flage:`, `cache …`, `integralnosc …`, `kopia danych`, `sejf …`, `serializuj:`,
  `workflow:` (plan), `maszyna:`. Hasło sejfu nie przechodzi przez czat.
- **Narzędzia deweloperskie** (`DeveloperToolbox.cs`): diff LCS, regex z limitem 1 s, semver,
  IP/podsieci, JWT (bez weryfikacji podpisu — jawnie), UUID v7/ULID, generatory C#/SQL/mock,
  Base32/Base58, skróty (MD5/SHA-1/SHA-512/CRC32), CSV/JSON/Markdown, walidatory, konwencje nazw,
  konwencja commitów, 13 szablonów plików, budżet kontekstu.
- **Produktywność i finanse** (`ProductivityToolbox.cs`): kwota słownie, lorem, statystyki tekstu,
  ROI, próg rentowności, amortyzacja, inflacja, plan oszczędzania, cel, budżet 50/30/20,
  macierz Eisenhowera, generator slajdów.
- **Analiza kodu źródłowego** (`CodeInsightsService.cs`, poz. 186–220): skan, statystyki,
  zależności z cyklami, heurystyczny SAST (10 wzorców), duplikaty, licencje, funkcje/złożoność,
  drzewo — tylko odczyt, z limitami i jawnym zastrzeżeniem „to nie kompilator”.
- **Regresje**: `tests/CoreRuntimeRegression.cs` i `tests/DeveloperToolboxRegression.cs`
  wpięte w `--ui-smoke`; paleta `//` i `pomoc` rozszerzone o nowe rodziny poleceń.

## Priorytet P0 — kolejny przyrost

1. **Pliki — DOMKNIĘTE w 0.92/0.93**: duplikaty po treści (SHA-256), raport porządkowy, usuwanie pojedynczych plików do Kosza z potwierdzeniem, usuwanie duplikatów (`usuń duplikaty:`) z pozostawieniem 1 kopii na grupę, sprzątanie pustych plików (`usuń puste pliki:`), zbiorcze zmiany nazw z podglądem (`zmien nazwy: … zamien … na …`). Ewentualne rozszerzenia (filtry rozmiaru, podfoldery w zmianach nazw) to P1.
2. **Sekwencje działań — CZĘŚCIOWO w 0.96**: rutyny (`rutyna X`) dają nazwane kroki, wykonanie po kolei z wynikiem każdego kroku i zatrzymanie przy przerwaniu. Zostaje: plan z podglądem przed uruchomieniem, statusy kroków w historii akcji i wznowienie od kroku, który nie zdążył się wykonać.
3. **Panel archiwum w UI**: strona/panel z listą archiwów (miesiąc, rozmowy, wypowiedzi, rozmiar, hash), podglądem Markdowna i usuwaniem — dziś archiwum działa z czatu i z ustawień, ale nie ma własnego widoku.
4. **Podpisywanie buildów**: certyfikat (płatny) albo jawnie opisana ścieżka bez podpisu z instrukcją odblokowania SmartScreen; dziś pliki są niepodpisane i tak jest opisane.
5. **Integracje odłożone z 0.91** (decyzja użytkownika): GitHub i Gmail — dopiero jako osobny przyrost, wyłącznie za jawną zgodą i z lokalnym przechowywaniem poświadczeń stockowym crypto Windows.
6. **Motywy**: system tokenów `Sx*` z generatorem i kontrolą kontrastu jest domknięty w 0.93 (Dark/Deep Dark/Light + akcent w trakcie działania). Zostaje: pełny motyw „szkło/Jarvis” z przezroczystym oknem głównym (aurora jest dziś warstwą dekoracyjną wewnątrz okna; `AllowsTransparency` na oknie głównym to świadomie niezaakceptowany koszt wydajności/kompatybilności).

## Priorytet P1

- Pliki — rozszerzenia poza P0: podgląd diff przed zmianą treści, filtrowanie duplikatów po rozmiarze przed hashowaniem.
- Tryb gry: zawieszanie indeksowania/ciężkich zadań, profil modeli z histerezą przełączania (progi + opóźnienie), zwalnianie modelu opcjonalne.
- Sekwencje działań (po 0.96): plan z podglądem, statusy kroków w historii akcji, wznowienie od niewykonanego kroku.
- Rozmowa: edycja wypowiedzi i ponowne wysłanie z oznaczeniem alternatywy; zakładanie odgałęzień rozmowy.
- Narzędzia: przelicznik walut (wymaga kursu — offline nieuczciwe), przywracanie archiwum do nowej rozmowy, skróty konfigurowalne przez użytkownika.
- Rozbicie `CommandRouter` (~700 linii po 0.94) na osobne moduły: pamięć, projekty/zadania, snapshoty, narzędzia, meta — dopiero przy zielonym CI.
- Z 0.91 odłożone: dalsze pozycje z listy „100 funkcji” (m.in. edycja plików konfiguracyjnych z podglądem diff, profile skrótów użytkownika, święta w `dni robocze`, wykrywanie świąt w kalendarzu) — bez udawania, że istnieją.
- Modele 3D i generowanie modeli — poza zakresem produktu (wbudowana uczciwa odmowa); gdyby wróciły, to jako integracja z zewnętrznym narzędziem za zgodą.
- Samomodyfikacja kodu — odrzucona na stałe decyzją użytkownika; odpowiednikami są `napraw sie` (naprawa danych), `ulepsz sie` (pętla uczenia z poprawek — dane, nie kod), `samokontrola` i `propozycje`.

## Priorytet P2

- Wyszukiwanie semantyczne lokalne: tylko jako opcjonalny indeks (bez wpływu na tryb tekstowy); najpierw benchmark jakości na scenariuszach PL.
- Integracja z autostartem/Harmonogramem zadań dla przypomnień — wyłącznie za jawną zgodą, z opcją cofnięcia.
- Alerty progów diagnostyki z historią krótkich pomiarów (z limitem wzrostu plików).

## Świadome ograniczenia obecnego stanu

- Brak lokalnego kompilatora .NET w środowisku deweloperskim — weryfikacja wyłącznie przez CI na `windows-latest` (opis w README).
- Build niepodpisany: SmartScreen może ostrzegać (zgodnie z README; nie wyłączamy zabezpieczeń).
- Ollama, mikrofon i gry wymagają testów na komputerze docelowym; CI nie ma mikrofonu ani gier.
- Wyszukiwanie **treści** (wspomnienia, rozmowa, `szukaj wszystkiego`) jest tekstowe z tolerancją odmiany i jednej literówki (≥5 znaków: wspólny ogier albo edycja ≤1) — bez semantyki; dalej może nie trafić na synonimy.
- Naprawa poleceń działa na katalogu ~371 fraz (0.97) z ogierkami odmiany: nie rozumie pełnej polszczyzny ani kontekstu, a przy niejednoznaczności celowo nic nie poprawia.
- `przelicz` nie przelicza walut (wymaga kursu — offline nieuczciwe). Zrzuty ekranu i sterowanie
  głośnością są od 0.95.
- Sterowanie oknami i multimediami (0.96) używa syntetycznych klawiszy: system może je odrzucić,
  gdy aktywne okno ma wyższe uprawnienia (UIPI) — wtedy pada uczciwe „nie udało się”.
- Rutyny (0.96) nie mają podglądu planu przed uruchomieniem ani statusów kroków w historii akcji.
- VAT liczy stawki 23/8/5/0% (wybór w poleceniu, domyślnie 23%); inne stawki są odrzucane z wyjaśnieniem.
- Archiwum rozmów jest kopią do odczytu: nie ma przywracania do aktywnej rozmowy.
- Limit magazynu artefaktów GitHub Actions został osiągnięty 2026-09-24: przebieg `36049020743` miał **13/13 kroków merytorycznych `success`**, a czerwony znacznik pochodzi wyłącznie z `upload-artifact` („Artifact storage quota has been hit”). Retencja artefaktów skrócona do 2 dni; trwałą dystrybucją są assety wydania. Gdyby limit dalej blokował, kolejne kroki: `retention-days: 1` albo rezygnacja z uploadu binariów w buildzie gałęzi.
- Streaming AI nie jest weryfikowany z żywym modelem w CI (brak Ollama w środowisku testowym) — transport i logika fragmentów są testowane na wstrzykniętym HTTP.
- Warstwa wizualna 0.93 nie była oglądana na żywo w środowisku deweloperskim (brak lokalnego kompilatora .NET): dowodem są bramki statyczne (`check-architecture.py`, `generate-themes.py --check`) oraz `--ui-smoke` w CI na Windows (render każdej strony i zakładki, zero błędów wiązań, asercje tokenów). Ocena „czy to wygląda dobrze” wymaga uruchomienia na komputerze użytkownika.
- Kontrola kontrastu w generatorze obejmuje zdefiniowane pary tokenów (tekst/tło, biały na końcach gradientów, tekst stanu na jego tle), nie każdą kombinację użytą w XAML; gradienty pod etykietami mają celowo przyciemnione końce.
- Animacje pętlące (aurora, shimmer, pasy postępu, puls) są bramkowane ustawieniem Windows i ustawieniem „Animacje”, ale ich realny koszt GPU/CPU na słabszych maszynach nie został zmierzony — gdyby okazał się zauważalny, pierwszym krokiem jest wyłączenie aurory (`Motion.Aurora`) na stronie Centrum.
- Panel „Motywy” nie ma podglądu motywu na żywo przed zapisem (zmiana działa natychmiast, ale nie ma miniatur).
- Rdzeń 0.97 nie ma jeszcze panelu UI (sejf, kolejka, flagi, kopie działają z czatu i hosta);
  `workflow:` tylko planuje graf — wykonanie kroków to kolejny przyrost.
- Analiza kodu (0.97) jest tekstowa: bez drzewa składni i bez analizy przepływu danych; trafienia
  SAST wymagają obejrzenia, a brak trafień niczego nie gwarantuje.
- Pakowanie (EXE/instalator) jest w 0.97 wyłączone na zwykłym pushu — decyzja użytkownika
  („nie buduj mi EXE jeszcze”); kroki publikacji w `windows-build.yml` działają tylko przy
  ręcznym uruchomieniu workflow, a `release.yml` bez zmian tylko na tagach.
- Z listy 1550 pozycji zrealizowano dotąd: rdzeń (sekcja 1) oraz sekcje 4–7, 13 i 16; pozycje
  z sekcji 2, 3, 8–12, 14, 15 i 17–20 są nadal otwarte (kolejne przyrosty, nie „prawie gotowe”).
