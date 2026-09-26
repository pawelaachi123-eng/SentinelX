# Sentinel X 0.93 „PORZĄDKI” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.93 · PORZĄDKI`

## Co nowego

### `zmien nazwy: <folder> zamien <tekst> na <tekst>` — zbiorcza zmiana nazw z podglądem

- Działa na plikach **bezpośrednio w podanym folderze** (bez podfolderów — świadoma granica).
- Zamienia fragment nazwy (np. `IMG_` → `wakacje_`), niezależnie od wielkości liter.
- Najpierw pokazuje pełną listę `stara → nowa` oraz pominięte (kolizja z istniejącą nazwą,
  niebezpieczna nazwa wynikowa typu CON/NUL, dowiązania).
- Dopiero `potwierdz` zmienia nazwy (`File.Move` bez nadpisywania), wynik weryfikowany per plik
  (VERIFIED/PARTIAL/FAILED) i audytowany jako `FILE_RENAME`.
- Goła komenda pokazuje użycie zamiast zgadywać.

### `usuń puste pliki: <folder>` — sprzątanie plików 0 B

Znajduje pliki o rozmiarze 0 B (rekurencyjnie, z limitami skanu), pokazuje listę i po `potwierdz`
przenosi je do Kosza — tym samym jednorazowym mechanizmem zgody co pozostałe operacje.

### `usuń duplikaty: <folder>` — sprzątanie duplikatów za zgodą

Rozwinięcie raportu `duplikaty:` z 0.92 o krok wykonawczy:

1. Sentinel skanuje folder (te same granice co raport: maks. 50 000 plików, ≤256 MB hashowane,
   junction/symlinki nieprzechodzone).
2. Z każdej grupy identycznych treści (SHA-256) **zostawia 1 plik** — ten o najkrótszej ścieżce
   (zwykle oryginał), pozostałe kopie trafiają na listę „do Kosza”.
3. Pokazuje dokładną listę i pyta. Dopiero `potwierdz` przenosi pliki do Kosza Windows
   (przywracalne); `anuluj` albo jakiekolwiek inne polecenie porzuca operację.
4. Wynik jest weryfikowany per plik (VERIFIED/PARTIAL/FAILED) i zapisywany w audycie
   (`FILE_RECYCLE`) razem z listą usuniętych i pominiętych.

Bez duplikatów komenda uczciwie odpowiada „nie ma czego sprzątać”.

### Publikacja plików — zmiana procesu (decyzja użytkownika)

Automatyczne wydanie „rolling” po każdym pushu zostało **wyłączone** na prośbę użytkownika.
Workflow `release.yml` publikuje EXE/ZIP tylko:

- po pushu tagu milowego `v*.*.*` (np. `v0.91.2`), albo
- na ręczne żądanie (`workflow_dispatch`, tag domyślnie `rolling`).

Zwykły push na gałąź przechodzi pełny build i testy w „Windows build and WPF smoke”, ale nic nie
publikuje. Wydania milowe na GitHubie: `v0.90.0`, `v0.91.2`; pod tagiem `rolling` zostało ostatnie
automatyczne wydanie `0.92.0` — tag nie jest już przesuwany. Wersja 0.93.0 zostanie opublikowana
dopiero tagiem milowym (np. `v0.93.0`), kiedy użytkownik o to poprosi.

### GUI: nowoczesna warstwa wizualna (bez nowych pakietów NuGet)

Zakres: wyłącznie aktywny shell MVVM (`Views/**`, `Themes/**`, `Utilities/**`). Legacy UI za `--legacy`
nie zmieniło ani jednego znaku; wersja aplikacji zostaje `0.93`.

- **Tokeny designu `Sx*`** zamiast pojedynczych kolorów: 71 tokenów koloru (tła, powierzchnie, trzy
  poziomy tekstu, pięć akcentów z wariantami `Dim`/`Soft`, semantyka z końcami gradientów, obramowania,
  fokus, cienie, shimmer, reveal, dymki czatu, aurora, overlay, scrim) oraz tokeny promieni i odstępów
  (`CornerRadius`/`Thickness`). W `Views/**` nie ma dosłownych hexów, rodzin czcionek ani rozmiarów
  czcionek — sprawdza to `scripts/check-architecture.py`.
- **Trzy motywy z generatora**: `python3 scripts/generate-themes.py` tworzy `DarkTheme.xaml`,
  `DeepDarkTheme.xaml` i `LightTheme.xaml` (nadpisują wyłącznie tokeny `*Color`) i liczy kontrast WCAG
  dla par krytycznych; `--check` kończy się `PASS: 71 tokenów × 3 motywy, kontrast OK`. Kolor akcentu
  z ustawień nadpisuje `SxAccentCyanColor` w trakcie działania, więc każdy motyw działa z każdym
  akcentem.
- **Animacje**: 4 krzywe łatwości i 35 storyboardów (wejście strony, wejścia modalne, scrim, hover,
  press, glow, sheen, lift, pasek nawigacji, linia, chevron, puls, ripple, oddech, busy, shimmer, pasy
  postępu, dryf aurory). Animacje pętlące są bramkowane przez `ClientAreaAnimation` Windows **i**
  ustawienie „Animacje”, a podczas gry wyłączają się automatycznie.
- **Nowe zachowania w `Utilities/`, nie w code-behind**: `Motion` (attached `Entrance`, `BubbleIn`,
  `Hover`, `Pulse`, `Shimmer`, `Stripes`, `Aurora`, `ModalIn`), `Reveal` (poświata pod kursorem),
  `PageTransitions` (wejście strony). Code-behind każdego widoku ma mniej niż 20 linii i nie zawiera
  obsługi `Click=`.
- **Królewski herb zamiast „orba”** (prośba użytkownika po pierwszym wydaniu): styl `SxCrest`
  w `Themes/Controls.xaml` rysuje tarczę ze złotym obramowaniem, koroną i cyjanowym klejnotem
  (Viewbox, więc skaluje się do każdego rozmiaru), a wordmark „SENTINEL X” dostał złoty gradient.
  Do tego trzy nowe tokeny `SxRoyalGold*` (osobna rodzina niż semantyczny bursztyn `SxWarning`,
  wartości we wszystkich trzech motywach + pary kontrastu w generatorze) oraz `SxRoyalGradient`
  i `SxRoyalGlow` w `Themes/Brushes.xaml`.
- **Nowa ikona aplikacji** `Assets/sentinel.ico` (była 476 B): ten sam herb narysowany programowo
  i zapisany jako ICO z wpisami PNG 16/24/32/48/64/256 — tytuł okna, pasek zadań, EXE i instalator
  pokazują teraz tarczę z koroną zamiast dawnego kółka. Nazwa pliku bez zmian, więc `csproj`,
  `Icon=` okna i `SetupIconFile` instalatora nie wymagały dotyku.
- **Shell**: sidebar 268 px z gradientem i paskiem akcentu przy zaznaczeniu, herb `SxCrest`, pill `Ctrl+K`,
  badge głosu z pulsującą kropką i kolorem z konwertera stanu, aurora w tle treści, modalne paleta
  (`//`, `Esc`, `Enter`, strzałki) i panel gotowości ze scrimem. Zakładki Centrum mają widoczne podpisy.
- **12 stron i overlay** zbudowane od nowa na tokenach: spójny nagłówek (pasek akcentu + eyebrow +
  tytuł + opis), kafelki metryk z gradientowym paskiem, karty z reveal i animowanym wejściem, paski
  stanu, chipy, stany zajętości z shimmerem, stany puste. Stan jest zawsze opisany słowem — kolor go
  tylko wzmacnia, a brak odczytu pozostaje „Niedostępne” (VRAM nie jest zmyślany).
- **Trzy nowe konwertery** (`Converters/StatusConverters.cs`, klucze w `App.xaml`):
  `StringNotEmptyToVisibility` (komunikat pola widoczny tylko, gdy ma treść — błąd walidacji albo
  „Zapisano”), `SafePercent` i `FiniteToVisibility` (paski pomiarów CPU/GPU). Powód: `SystemSnapshot.Empty`
  ma `double.NaN` w CPU/GPU/RAM, a `RangeBase` odrzuca wartości niekończone
  (`ValidateValueCallback IsValidDoubleValue` w źródłach WPF) — pasek więc **znika**, gdy pomiaru nie ma,
  zamiast udawać 0%; tekst obok nadal mówi „Niedostępne”.
- **Kolejność scalania słowników w `App.xaml`** (świadome odstępstwo od opisu w §2.3 pkt 1 master
  promptu): `Themes/Animations.xaml` jest scalany **przed** `Themes/Controls.xaml`, nie po nim.
  Szablony z `Controls.xaml` sięgają po storyboardy przez `StaticResource` (poświata, błysk, hover,
  press, pasek nawigacji, linia akcentu, gałka przełącznika, strzałka listy), a `StaticResource`
  widzi tylko słowniki scalone wcześniej — przy starej kolejności `App.xaml` nie dało się w ogóle
  załadować (`XamlParseException` przed `OnStartup`, przebieg CI `36235263621`: krok smoke zakończony
  bez katalogu wyników). Zawartość słowników i wszystkie klucze zostały bez zmian.
- **Jeden styl domyślny `ListBoxItem`** w `Themes/Controls.xaml` (był zduplikowany: `BasedOn SxListItem`
  i `BasedOn SxSidebarItem`). Dwa style bez `x:Key` i z tym samym `TargetType` to dwa wpisy pod kluczem
  `typeof(ListBoxItem)` w jednym słowniku = `ArgumentException` „Item has already been added” przy
  ładowaniu `Controls.xaml`, czyli też przed `OnStartup`. Nawigacja i tak nadpisuje wygląd przez
  `ItemContainerStyle="{StaticResource SxSidebarItem}"` w `Views/MainWindow.xaml`, więc nic nie straciło.
- **`Utilities/Reveal.cs` bez `FindName`**: warstwa poświaty jest trzymana we właściwości dołączonej
  `Reveal.Layer`. Element tworzony w kodzie nie rejestruje się w namescope strony, więc poprzednie
  `grid.FindName("PART_RevealLayer")` zawsze zwracało `null` i karta zawijałaby się w kolejną siatkę
  przy każdym ruchu myszy (nieskończony przyrost drzewa zamiast jednego reveal).

## Weryfikacja

- `tests/FileCleanupRegression.cs` rozszerzony: cykl zgody na sprzątanie duplikatów, cykl zgody na
  puste pliki (tylko 0 B ruszone), pełny cykl zmiany nazw (podgląd, kolizja pominięta i ogłoszona,
  brak zmian przed zgodą, dokładna weryfikacja po zgodzie, powtórka bez par odpowiada „Brak plików
  do zmiany”, gołe komendy pokazują użycie). Plus wszystkie testy 0.92 (skany, raporty, pojedynczy
  Kosz, junction).
- Smoke UI + pełna regresja przez `--ui-smoke`, `--self-test` i smoke wersji portable w CI.
- `UiSmokeTestRunner.cs`: **zero usuniętych asercji**, ~120 nowych sprawdzeń warstwy wizualnej — po
  przełożeniu każdego motywu tokeny koloru muszą być osiągalne jako `Color`, tokeny promieni i odstępów
  jako `CornerRadius`/`Thickness`, style jako zasoby, storyboardy jako `Storyboard`, gradienty jako
  `Brush`, cienie i poświata jako `Effect`; do tego dotychczasowa bramka „zero błędów wiązań” po
  wyrenderowaniu każdej strony i zakładki.
- Bramki statyczne: `python3 scripts/check-architecture.py` → `PASS` oraz
  `python3 scripts/generate-themes.py --check` → `PASS: 68 tokenów × 3 motywy, kontrast OK`.
- **CI: przebieg `36236699412` (commit `47f92e9`) — zielony, wszystkie 17 kroków.** Cytaty z logu:

      Portable architecture checks:
      PASS: XML, resources, resource order, animation paths, 12 views, thin code-behind, VM boundaries, target framework, no WinForms flag, XAML pitfalls

      Build:
      Build succeeded.
          14 Warning(s)
          0 Error(s)

      MVVM UI smoke (all pages and binding validation) — zawartość ui-smoke.txt:
      PASS
      Pages: command, memory, projects, settings, centrum:rozmowa, centrum:zadania, centrum:historia, centrum:glos, centrum:system, centrum:gry, centrum:ai, centrum:akcje, centrum:diagnostyka
      Centrum tabs, // palette and voice default verified
      Dark/DeepDark/System themes rendered
      STOP/Resume/voice approval passed
      Palette, readiness, draft preservation and execution-scoped evidence passed
      Typo repair, grey-zone questions, lessons, self-check, offline tools, archives, insights and unified search passed

      Existing regression suite (--self-test): exit 0, test-results/regression z results.json
      i zrzutami legacy (Actions/AI/Chat/Gaming/Memory/Programs/Settings/System/Tools/Voice/minimum-size.png)

      Build per-user EXE installer:
      Successful compile (10.484 sec). Resulting Setup program filename is:
      D:\a\SentinelX\SentinelX\bin\installer\SentinelX-Setup-0.93.0-win-x64.exe
      ##[notice]B251998CE713172D6EDA7E2A18DB76C86D1A6C508D652223BD1A6E7796D15679  SentinelX-Setup-0.93.0-win-x64.exe

      Install EXE and smoke-test installed application: exit 0, ui-smoke.txt obecny

  14 ostrzeżeń buildu to wyłącznie pozycje zastane (`CS8604` w `WorkspaceInsightsService.cs:227`,
  `MVVMTK0034` ×6 w `ViewModels/TaskViewModel.cs:107,171` — liczone podwójnie dla `_wpftmp.csproj`
  i `SENTINEL-X.csproj`); ani jednej z plików tego przyrostu. Nie ruszam ich: to kod poza zakresem GUI.
- **Nieskładkowe:** `actions/upload-artifact@v4` nie wgrał artefaktów —
  `##[error]Failed to CreateArtifact: Artifact storage quota has been hit.` (limit magazynu konta,
  przeliczany co 6–12 h). Walidacja i tak się wykonała i przeszła; pliki `test-results/` zostały tylko
  na runnerze. `.github/workflows/**` nietknięte (R15), więc nic tu nie obejściem.
- Asercje smoke sprawdzają typ zasobu przez `Application.Current.TryFindResource(…) is
  Color/CornerRadius/Thickness/Style/Storyboard/Brush/Effect/IValueConverter`; pierwsza wersja użyła
  `ResourceDictionary.ContainsResource`, którego w .NET 9 nie ma (`CS1061` w przebiegu `36234911112`).
- `scripts/check-architecture.py` dostał nową bramkę „XAML pitfalls" (6 reguł), dopisaną po pierwszym
  przebiegu CI, który zatrzymał się na kompilacji: `BeginStoryboard` nie ma właściwości `TargetName`
  (MC3072), `Setter TargetName` tylko w `ControlTemplate`, `Style` nie może być ustawiony dwa razy
  (atrybut + element), `StringFormat` zaczynający się od `{` wymaga ucieczki `{}` (MC1000), duplikat
  `x:Name` w głównym zakresie nazw, właściwość dołączona `u:*` musi istnieć w `Utilities/`. Każda reguła
  została sprawdzona przez celowe wstrzyknięcie błędu (wszystkie 6 łapie, drzewo wraca do `PASS`).
- `scripts/check-architecture.py` dostał dwie kolejne bramki po trzecim przebiegu CI (`36235263621`,
  build już zielony, smoke zakończony bez żadnych artefaktów):
  **kolejność zasobów** (symulacja ładowania `App.xaml`: każdy `StaticResource` musi istnieć w chwili
  użycia — słowniki scalone później i klucze wpisane poniżej miejsca użycia są błędem; plus duplikat
  stylu domyślnego w jednym słowniku) oraz **ścieżki animacji** (każdy `BeginStoryboard` w stylu musi
  trafiać w element, którego `RenderTransform` ma żądane dziecko `TransformGroup` pod żądanym indeksem,
  a storyboardy grane z `Utilities/` mieszczą się w konwencji `[Scale, Translate]` z
  `Motion.EnsureTransform`). Obie sprawdzone przez celowe wstrzyknięcie błędu: zamiana kolejności
  scalania, dopisanie drugiego stylu domyślnego `ListBoxItem`, przesunięcie stylu domyślnego
  `TextBlock` na koniec pliku, usunięcie `RotateTransform` strzałki `ComboBox` i podstawienie
  `SxChevronUp` do `Motion` — wszystkie pięć bramka łapie, po przywróceniu drzewo wraca do `PASS`.
- Awaria sprzed `OnStartup` była w CI niewidoczna (katalog `test-results/ui` w ogóle nie powstawał,
  więc krok nie miał czego wydrukować). `Utilities/CrashLogger.cs` dostał inicjalizator modułu: dla
  `--ui-smoke` rejestruje `AppDomain.UnhandledException` **przed** `Main`, więc taki upadek zostawia
  `FAILED.txt` w katalogu wyników, a istniejący krok CI już ten plik czyta. Poza smoke zachowanie
  aplikacji się nie zmienia (handler nie jest rejestrowany). `.github/workflows/**` nietknięte.
- `Themes/Colors.xaml`: tokeny `CornerRadius`/`Thickness` używają `assembly=PresentationFramework`
  (`Thickness.cs` i `CornerRadius.cs` leżą w `PresentationFramework/System/Windows` — sprawdzone w
  źródłach `dotnet/wpf`, nie w `PresentationCore`).

## Uczciwie o ograniczeniach

- Zmiany nazw dotyczą tylko plików bezpośrednio w folderze (podfoldery zostają nietknięte); zmiany
  nazw podfolderów są poza zakresem.
- „Zostawiany” plik wybierany jest regułą najkrótszej ścieżki — jeśli oryginał leży głębiej niż
  kopia, to kopia zostanie. Lista w propozycji zawsze pozwala to zobaczyć i anulować.
- Usunięcie wielu plików ma JEDNO potwierdzenie dla całej listy (lista jest jawna w propozycji);
  trwałe usuwanie nadal nie istnieje.
- Kosz Windows testowany w CI na atrapie recyklera — rzeczywisty Kosz potwierdzi dopiero komputer
  użytkownika.
- P0.1 „plików” jest domknięte; dalsze pomysły (np. filtry rozmiaru w `porzadki`) wracają do P1.
- Warstwy wizualnej nie dało się obejrzeć na żywo w środowisku deweloperskim (brak lokalnego kompilatora
  .NET i brak dostępu do hostów Microsoft/NuGet): dowodem są bramki statyczne i `--ui-smoke` w CI na
  Windows. Ostateczna ocena wyglądu (DPI 125/150%, kilka ekranów, realny koszt aurory na słabszym GPU)
  należy do komputera użytkownika.
- Kontrola kontrastu obejmuje zdefiniowane pary tokenów, nie każdą kombinację użytą w XAML; końce
  gradientów pod białymi etykietami są celowo przyciemnione.
- Okno główne pozostaje nieprzezroczyste — motyw „szkło/Jarvis” z `AllowsTransparency` to świadomie
  niezaakceptowany koszt wydajności i kompatybilności (pozostaje w P0 backlogu).
- Rozbieżność zgłoszona, nie naprawiana „przy okazji”: stare `Views/Pages/SettingsPage.xaml` wiązało
  `AutomationProperties.Name="{Binding Name}"`, a `SettingViewModel` nie ma właściwości `Name` (ma
  `Label`) — każde takie wiązanie kończyło się błędem w logu wiązań. Nowa strona wiąże `Label`, więc
  błąd znika; nie zmieniano ani `SettingViewModel`, ani `SettingsCatalog`.
- Pole `Kolor akcentu` w `SettingsCatalog` ma jako wartości i etykiety dosłowne hexy
  (`#00D4FF`, `#66F2C2`, …). To kontrakt ustawień (Dopiski: etykiet nie zmieniamy), więc hexy zostają
  w katalogu; w `Views/**` nadal obowiązuje zakaz dosłownych hexów i jest on sprawdzany bramką.
