# SENTINEL X — MASTER PROMPT: idealne GUI (warstwa widoku, bez ruszania backendu)

> **Paweł, to jest plik z promptem, nie implementacja.** Nic w kodzie nie zostało zmienione.
> Prompt jest napisany pod konkretne fakty z repo `pawelaachi123-eng/SentinelX` @ 0.93 — każde odwołanie
> do pliku, stylu i właściwości jest prawdziwe i sprawdzalne `grep`-em. Wrzuć całość (albo sekcję „WERSJA
> KRÓTKA” na model z małym kontekstem) do agenta kodowego, który ma dostęp do repozytorium.
>
> Przed wysłaniem: uzupełnij 3 pola w sekcji **S0** (co boli, co zostaje, ile czasu/equipu).
> Bez tego model będzie zgadywał priorytety.

---

## WERSJA KRÓTKA (gdy okno kontekstowe jest małe)

```
Pracujesz w repo SentinelX: .NET 9 WPF, net9.0-windows, C# 13, CommunityToolkit.Mvvm 8.4.2, DI z
Microsoft.Extensions.DependencyInjection, RootNamespace=SentinelX. Aktualne GUI to shell MVVM:
Views/MainWindow.xaml + Views/Pages/*Page.xaml + Themes/*.xaml (klucze Sx*). Legacy MainWindow.xaml
z katalogu głównego zostaje nietknięty (obsługuje --legacy i --self-test).
Zadanie: przebuduj WYŁĄCZNIE warstwę widoku na poziom produktu komercyjnego, bez zmian w Services/,
ViewModelach (poza czysto prezentacyjnymi polami), Models/Settings i formacie JSON.
Przed kodem: (1) przeczytaj scripts/check-architecture.py i podporządkuj mu każdy XAML,
(2) przeczytaj UiSmokeTestRunner.cs sekcję UI i zachowaj wszystkie asercje, (3) zadaj mi pytania o
kryteria, których nie da się wywnioskować z repo, (4) daj plan fazowy do akceptacji.
Zakaz: nowe pakiety NuGet bez mojej zgody, WinForms, AllowsTransparency na oknie głównym,
hardkodowane kolory hex w Views/**, code-behind > 20 linii, wymyślone metryki (VRAM, FPS),
pisanie czegokolwiek w Services/*.
```

---

# PEŁNY PROMPT — kopiuj od tej linii

## S0. KONTEKST, KTÓREGO MUSZĘ UZUPEŁNIĆ PRZED WYSYŁKĄ

```
BOLI MNIE NAJBARDZIEJ:  <np. „Centrum z 9 ikonami bez podpisów jest nieczytelne", „strony wyglądają
                        jak tabele debugowania", „okno nie zapamiętuje rozmiaru">
ZOSTAJE BEZ DYSKUSJI:  <np. ciemny motyw jako domyślny, sidebar po lewej, czat jako ser aplikacji>
MAM CZAS NA:           <1 sesję / 1 dzień / tydzień iteracji>
MAM DOSTĘP DO:         <tylko CI Windows / mam lokalny Visual Studio / mam tylko laptopa bez .NET>
```

Jeśli pól nie uzupełnię — **zapytaj o nie przed napisaniem kodu**, nie zakładaj.

---

## S1. ROLA I CEL

Jesteś starszym inżynierem UI specjalizującym się w WPF (.NET 9) i projektowaniem systemów wizualnych dla
narzędzi systemowych klasy „pro" (jak Raycast, Linear, Windows Terminal, PowerToys). Zadanie: zaprojektować
i zaimplementować **idealną warstwę GUI** dla SENTINEL X — lokalnego asystenta Windows z pomiarami sprzętu,
głosem, pamięcią, zadaniami, projektami i diagnostyką.

Cel produktu: użytkownik w 3 sekundy po otwarciu okna ma wiedzieć **czy Sentinel jest gotowy, co właśnie
robi, czy mu wolno wydać polecenie i jaki był skutek ostatniego**. Wszystko inne ma zniknąć.

Nie jest celem: „ładniej", „nowocześniejszo", „jak w Dribblinie". Jest celem: **czytelność stanu,
przewidywalność i zero kłamstw wizualnych**.

Zasada nadrzędna: **GUI jest warstwą, nie źródłem prawdy.** Widok pokazuje wyłącznie to, co już zwracają
usługi i ViewModele. Jeśli ViewModel nie ma danego pola — **nie wymyślaj pola, nie wymyślaj wartości, nie
dekoruj**. Zgłoś brak jako osobny punkt raportu.

---

## S2. STAN FAKTYCZNY REPO (nie założenia — sprawdzaj `grep`-em przed każdym zdaniem o kodzie)

Stack:
- `SENTINEL-X.csproj`: `OutputType=WinExe`, `TargetFramework=net9.0-windows`, `UseWPF=true`,
  `LangVersion=13.0`, `Nullable=enable`, `ImplicitUsings=enable`, `PlatformTarget=x64`,
  `RootNamespace/AssemblyName=SentinelX`, `Version=0.93.0`, `EnableWindowsTargeting=true`.
- Pakiety (używaj tylko tych): `CommunityToolkit.Mvvm 8.4.2`,
  `Microsoft.Extensions.DependencyInjection 9.0.0`, `OllamaSharp 5.4.18`, `System.Speech 9.0.0`,
  `NAudio 2.2.1`, `Whisper.net 1.9.1` (+`Whisper.net.Runtime`), `org.k2fsa.sherpa.onnx 1.13.5`,
  `SharpCompress 0.50.4`, `System.Diagnostics.PerformanceCounter 9.0.0`.
- Brak Prism, brak MaterialDesign, brak WPF-UI, brak WinForms. `UseWindowsForms` **nie może** pojawić się
  w csproj (testowane w CI). Fluent to wbudowany motyw .NET 9 (`PresentationFramework.Fluent`) — już
  podłączony w `App.xaml`, bez pakietu.
- `app.manifest`: `asInvoker`, `PerMonitorV2, PerMonitor` DPI, supportedOS Windows 10/11. Nie zmieniaj.

Dwa współistniejące interfejsy — **nie mylić**:
1. **Nowy shell (domyślny, cel zadania):** `Views/MainWindow.xaml` (`x:Class="SentinelX.Views.MainWindow"`,
   `Width=1400 Height=900 MinWidth=1100 MinHeight=700`, `ThemeMode="Dark"`, `Icon=/Assets/sentinel.ico`,
   `u:UiInteraction.LoadedCommand="{Binding InitializeCommand}"`, `KeyBinding Ctrl+K → OpenPaletteCommand`),
   nawigacja `ListBox` na `MainViewModel.NavItems`, treść przez `ContentControl Content="{Binding CurrentPage}"`
   + `DataTemplate` per ViewModel → 12 plików `Views/Pages/*Page.xaml`.
2. **Legacy (zostaje, nietknięte):** `MainWindow.xaml` + `MainWindow.xaml.cs/.Commands/.Settings/.Voice`
   w katalogu głównym, `Theme.xaml`, sterowanie `Click=`/`x:Name=`, wersja „0.85" w tytule. Uruchamiane przez
   `App.xaml.cs` przy `--legacy` i `--self-test`. **Zakaz edycji**, zakaz „portowania", zakaz usuwania.

Warstwa widoku opiera się na:
- `App.xaml` — słowniki: Fluent, `Themes/Colors.xaml`, `Themes/Brushes.xaml`, `Themes/Typography.xaml`,
  `Themes/Controls.xaml`, `Themes/Animations.xaml`; konwertery `ReadinessBrush`, `StatusBrush`, `VoiceBrush`,
  `RiskBrush` (z `Converters/StatusConverters.cs`) + wbudowany `BoolToVisibility`. Brak `StartupUri`
  (okno wybiera `App.xaml.cs`).
- Motywy są dokładane **w trakcie działania** przez `Services/Desktop/DesktopService.cs`: słownik
  `Themes/DarkTheme.xaml` | `Themes/DeepDarkTheme.xaml` | `Themes/LightTheme.xaml` zastępuje klucze
  `Sx*Color`, `window.ThemeMode` na `Light`/`Dark`, a akcent nadpisuje
  `Application.Current.Resources["SxAccentCyanColor"]` z `Ui.AccentColor`.
  Animacje są wyłączane runtime przez `Application.Current.Resources["SxAnimationsEnabled"]`
  (wyłączone też, gdy wykryto grę).
- Istniejące zachowania widokowe bez code-behind: `u:UiInteraction.LoadedCommand`,
  `u:UiInteraction.FocusOnVisible`, `u:UiInteraction.FocusVersion`, `u:InputHint.Text` (renderowany w
  szablonie `SxTextBox`), `u:PageTransitions.Enabled`, `u:ChatScroll.Follow`, `u:SlashKeys.Enabled`.

System tokenów — **kontrakt, którego nie wolno złamać**: w `Themes/Colors.xaml` każdy token istnieje jako
para `<Color x:Key="SxFooColor"/>` + `<SolidColorBrush x:Key="SxFoo" Color="{DynamicResource SxFooColor}"/>`.
Słowniki motywu nadpisują **tylko** klucze `*Color`. Nowe tokeny dokładaj dokładnie tym wzorcem, w tym pliku.

`MainViewModel` (kontrakt widoku): `Version`, `NavItems` (rekord `NavItem(Key, Icon, Label, ViewModel)`),
`SelectedItem`, `CurrentPage`, `IsStopped`, `DesktopStatus`, `Voice`, `Palette`, `Readiness`, komendy
`OpenPaletteCommand`, `OpenReadinessCommand`, `InitializeCommand`, `EmergencyStopCommand`, `ResumeCommand`,
`ExitCommand`; mapa `CenterTabByLegacyKey` kieruje stare klucze (`system`, `voice`, `ai`, `actions`,
`history`, `gaming`, `tasks`, `diagnostics`, `command`) do zakładek Centrum przez
`commandCenter.SelectTabCommand`.

`CommandCenterViewModel`: `Sections` (`IReadOnlyList<CenterTab>` z `Key/Icon/Label/ViewModel`;
klucze `rozmowa, zadania, historia, glos, system, gry, ai, akcje, diagnostyka`), `SelectedTab`,
`IsChatTab`, `SubTabContent`, `Messages`, `UserInput`, `Status`, `IsBusy`, `IsStreaming`, `StreamingText`,
`IsPrivateMode`, `ConversationTitle`, `VoiceActive`, `Voice`, `SlashOpen/SlashItems/SlashIndex`,
`HasPermission/PermissionSummary/ApproveCommand/CancelCommand`, `SendMessageCommand`, `StopGenerationCommand`,
`RetryCommand`, `QuickCommandCommand`, `TogglePrivateModeCommand`, `ToggleVoiceCommand`, `InputFocusVersion`,
`CurrentAction`.

`UiSettings` (już istnieje, nie rozszerzaj bez potrzeby): `AnimationsEnabled`, `CloseToTray`, `MinimizeToTray`,
`Theme` (`Dark`|`Deep Dark`|`System`), `AccentColor` (`#RRGGBB`), `OverlayOpacityPercent` (20–100),
`OverlayScalePercent` (60–180), `OverlayPosition` (`Lewy górny`|`Prawy górny`|`Lewy dolny`|`Prawy dolny`),
`WindowWidth`, `WindowHeight`, `SelectedPage`.

**Znaleziona luka do domknięcia przy okazji (zweryfikuj przed zmianą):** `Ui.WindowWidth`, `Ui.WindowHeight`
i `Ui.SelectedPage` są stosowane **tylko w legacy** (`MainWindow.xaml.cs:61,70,228`). Nowy shell ma stałe
1400×900 i zawsze startuje w Centrum. Dodatkowo legacy klamping w `Models/Settings/SentinelSettings.cs`
to `WindowWidth∈[1000,3840]`, `WindowHeight∈[720,2160]`, a `MinHeight` shellu to `700` — niech liczby się
nie rozjadą. Podłącz zapamiętywanie rozmiaru/pozycji i ostatniej strony w shellu (przez istniejące pola
`UiSettings`; zapis jak w legacy — przez `AppSettingsService`), albo udokumentuj w raporcie, dlaczego nie.

---

## S3. BEZWZGLĘDNE BRAMKI — każde złamanie = PR odrzucony przez CI

`scripts/check-architecture.py` (uruchamiasz **lokalnie**: `python3 scripts/check-architecture.py`) sprawdza
dosłownie to; dostosuj każdy wygenerowany plik:

| # | Reguła (fakt z pliku) | Co to znaczy dla Ciebie |
|---|---|---|
| R1 | brak duplikatów `x:Key` w jednym pliku `Themes/*.xaml` | nowe style/tokens unikalne w pliku |
| R2 | `ProgressBar` z `Value="{Binding …}"` musi mieć `Mode=OneWay` | pomiary tylko jednokierunkowo |
| R3 | w `Views/**/*.xaml` wolno odwoływać się wyłącznie do `{Static/DynamicResource Sx\w+}` oraz `BoolToVisibility`, `StatusBrush`, `VoiceBrush`, `RiskBrush`, `ReadinessBrush` | każdy nowy zasób = `Sx`+prefiks i definicja w `Themes/*.xaml`/`App.xaml`; brak `StaticResource` do klucza, którego nie ma na liście |
| R4 | `assert not re.search(r'="#[0-9a-fA-F]{6,8}"', source)` dla `Views/**/*.xaml` | **zero literałów hex w widokach** — także `Fill="#00D4FF"`, `Color=`, `Opacity`-gradienty; zawsze token |
| R5 | do każdego `Views/**/*.xaml` musi istnieć `.xaml.cs` z **< 20 liniami** | bez logiki w code-behind; zachowanie → `Utilities/*` jako attached property |
| R6 | w `ViewModels/*.cs` zakazane: `Process.Start(`, `File.Read`, `File.Write`, `Registry.Current`, `ManagementObjectSearcher` | ViewModel nie dotyka systemu |
| R7 | `TargetFramework == net9.0-windows`, `UseWindowsForms != true` | nie ruszaj csproj |
| R8 | `Views/Pages/*Page.xaml` jest dokładnie **12** | nowa strona = zmiana tej asercji + `UiSmokeTestRunner` + `MainViewModel` + `docs/`; bez zgody nie dodawaj |

Dodatkowo (z `docs/ARCHITECTURE.md`, pilnowane przez ten sam plik i code review): widok nie sięga do logiki,
ViewModel nie czyta plików, serwis nie zna WPF.

Reguły miękkie, ale traktuj jako twarde (wynikają z `UiSmokeTestRunner.cs`):
- `MainWindow.xaml` legacy i `Theme.xaml` **nie są usuwane ani przenoszone**.
- nazwa `x:Name="SearchInput"` w `Views/Controls/CommandPalette.xaml` musi zostać (test sprawdza fokus na
  `TextBox` o tej nazwie i przywrócenie fokusa po zamknięciu).
- `Readiness.Checks.Count == 4`; nawigacja palety musi trafić w `vm.SelectedItem?.Key == "settings"`;
  paleta **nie wykonuje** polecenia, tylko podstawia (`chat.UserInput`), STOP zachowuje draft;
  po `EmergencyStop` nowe akcje są blokowane, `Resume` odblokowuje; głos nigdy nie zatwierdza.
- smoke renderuje **każdy** `NavItems` i **każdą** `chat.Sections` → każda zakładka musi działać i
  nie może rzucać błędu wiązania (`RenderTargetBitmap` na żywym oknie).
- smoke przełącza motywy `"Deep Dark"`, `"System"`, `"Dark"` — UI nie może się rozsypać w żadnym z trzech,
  w tym w jasnym (kontrast! patrz S7).

**Kompilacji nie sprawdzisz lokalnie** (w tym środowisku nie ma .NET — `which dotnet` jest pusty). Źródłem
prawdy jest CI `windows-build.yml`. Dlatego: małe kroki, każdy krok osobno przez CI, a nie jeden giant commit.

---

## S4. ZAKRES — CO WOLNO, A CZEGO NIE

WOLNO (cel zadania):
- wszystkie pliki w `Views/**`, `Themes/**`, `App.xaml`, `Utilities/**` (nowe attached properties),
  `Converters/**` (nowe, czyste konwertery prezentacyjne), `Assets/**` (ikony/krzywe),
  rozszerzenia `ViewModels/**` **wyłącznie** o pola/`RelayCommand` czysto widokowe (np. `IsSidebarCollapsed`,
  `VisibleMetricTiles`), `UiSmokeTestRunner.cs` (dodawanie asercji), `docs/**`, skróty klawiszowe widoku.

NIE WOLNO (bez wyraźnej zgody w odpowiedzi):
- `Services/**`, `Core/**`, `Models/**`, `*.cs` w katalogu głównym poza `UiSmokeTestRunner.cs`,
  format `settings.json`/`memory.json`/`tasks.json`/`projects.json`/`snapshots.json`,
  `SENTINEL-X.csproj` (w tym nowe NuGet), `.github/workflows/**`, `app.manifest`, katalog legacy.
- nic, co zmienia kolejność `IntentRouter` albo pozwala modelowi wykonać polecenie;
- żaden przycisk nie może udawać funkcji, której nie ma (patrz S8).

Jeśli uważasz, że coś z „NIE WOLNO" jest konieczne dla UI (np. potrzeba pola w ViewModelu serwisowym) —
**opisz to w sekcji „Potrzebuję decyzji" i czekaj**. Nie robić „na chwilę".

---

## S5. SYSTEM PROJEKTOWY DO ZBUDOWANIA (dokładna specyfikacja)

Pliki: `Themes/Colors.xaml` (tokeny), `Themes/Typography.xaml` (skala typu), `Themes/Spacing.xaml` **nowy**
(jeśli trzeba — ale pamiętaj o R3/R1), `Themes/Controls.xaml` (szablony), `Themes/Icons.xaml` (Path geometry).
Wszystkie nowe klucze z przedrostkiem `Sx` i definicją tylko w `Themes/*.xaml` (patrz R3).

1. **Kolory.** Zachowaj istniejące tokeny i ich pary `Color`/`Brush`. Dodaj komplet dla: warstwy
   `SxSurfaceRaised`, obramowania aktywnego `SxBorderActive`, cienia `SxShadow`, kontenera kodu `SxCodeBg`,
   paska wirtualnego fokusowania `SxFocusRing`, oraz stanów metryk: `SxMetricGood`, `SxMetricWarn`,
   `SxMetricBad` (mapowane z progów, które **już** istnieją w `SystemSnapshot`/`WatchSettings` — nic nowego
   nie wymyślasz). Dla tokena z alpha: wzorzec jak `SxUserBubbleColor` (`#RRGGBBAA`).
   Zdefiniuj **te same klucze we wszystkich trzech motywach** tam, gdzie jasny motyw wymaga innej wartości —
   dziś `Themes/LightTheme.xaml` i `DeepDarkTheme.xaml` nadpisują tylko podzbiór; uzupełnij luki, żeby żaden
   stan nie wyglądał jak błąd w Light.
2. **Typografia.** `Themes/Typography.xaml` już podaje skalę: `SxFontHeading1=28 … SxFontMicro=10`.
   **Użyj tych kluczy w stylach zamiast literałów `28`/`18`/`14`**, tak, by zmiana skali była jedną linią.
   Dodaj style `SxMono` (rodzina `Cascadia Mono, Consolas`) dla danych technicznych i `SxNumber` dla metryk
   (`FontVariantNumeric` nie istnieje w WPF → ustaw `TextOptions.TextRenderingMode="Grayscale"` i
   stałą szerokość przez `FontFamily` monospace, nie przez „na oko").
3. **Rytm i odległości.** Skala 4/8/12/16/24/32. Margines strony = `28` (jak dziś), odstępy kart `12`,
   padding kart `16`/`20`. Nic mniejszego niż 4, nic „13,5".
4. **Narożniki i granice.** `CornerRadius` 10 (wnętrza), 12 (kontrole), 16 (karty), 4/16/16/16 w dymkach
   czatu (zostaw — to już jest wzorzec). Obrys 1px, stan focus = 2px `SxFocusRing`.
5. **Głębia.** Bez `DropShadowEffect` na elementach wewnątrz `ItemsControl`/`ScrollViewer` (koszt renderowania
   w WPF). Cieniowanie: 1 warstwa `Border` + gradient `SxShadow` na kartach statycznych, `SxCardElevated`
   dla warstw modalnych. Efekt „szkła" z backlogu P0#6: **bez `AllowsTransparency=True` na oknie głównym**
   (wydajność/compat) — realizuj przez `Mica`/gradient + aurora warstwę dekoracyjną jak dziś (`Ellipse` +
   `RadialGradientBrush`, `IsHitTestVisible="False"`, `Panel.ZIndex` pod treścią) i/lub przez
   `SystemParameters.ClientAreaAnimation`. Jeśli uznasz, że trzeba `AllowsTransparency`, to **najpierw**
   zaproponuj i opisz koszt — nie wdrażaj w ciemno.
6. **Ruch.** Animacje wyłącznie: fade 120–180 ms, ewentualny 4–8 px translate. Wszystko za bramką
   `SxAnimationsEnabled` (mechanizm już istnieje w `PageTransitions`), plus respektuj
   `SystemParameters.ClientAreaAnimation`. Brak animacji nieskończonych (ciężar CPU kłóci się z trybem gry).
   Brak `BeginStoryboard` w code-behind.
7. **Ikony.** W projekcie są ikony znakowe (⌘ ▤ ▣ ⚙ 🎤 💬 🕘 🖥 ✨ ⚡ 🩺). Emotikony = ryzyko braku glifu i
   „tanie" wrażenie. Zrób `Themes/Icons.xaml` z `Geometry` (Path data) dla każdego znaku używanego w UI i
   podmień znaki na `Path` **tylko w interfejsie** — ale zachowaj pola `NavItem.Icon` / `CenterTab.Icon`,
   bo `MainViewModel`, paleta i smoke ich używają: niech `Icon` nadal istnieje i służy jako klucz/tekst dla
   czytnika ekranu, a `DataTemplate` niech renderuje z `Geometry` (mapa konwerter `SxIconKeyToGeometry` albo
   `DataTrigger`-y — bez logiki w code-behind).
8. **Skalowalność okna.** Zachowaj `MinWidth/MinHeight`, dodaj: tryb wąski (< 1180 px → sidebar zwija się do
   szyny 64 px z ikonami+`ToolTip`), tryb szeroki (> 1600 px → dwie kolumny metryk albo panel szczegółów).
   Reaguj na `WindowSizeChanged` **bez** kodu w code-behind (styler/`DataTrigger` na `ActualWidth`,
   albo jedno pole `IsRail` w `MainViewModel`). Przetestuj 125%/150%/200% DPI (manifest PerMonitorV2).

---

## S6. SPECYFIKACJA EKRANÓW

### S6.1 Shell (`Views/MainWindow.xaml`)
- Górna belka: tytuł + wersja z `Version`, status `DesktopStatus`, wskaźnik AI (gotowy/sprawdzanie/błąd),
  wskaźnik głosu (`Voice.State/Status` + `VoiceBrush`), wskaźnik STOP, przycisk palety (Ctrl+K),
  COLLAPSIBLE sidebar toggle, min/max/close. Okno **bez** własnego caption-bar re-engineeringu:
  użyj `WindowChrome` tylko, jeśli naprawdę potrzebujesz integracji paska tytułu; w raporcie opisz koszt
  (Test UI musi nadal przechodzić).
- Sidebar: 4 pozycje `NavItems` (Centrum, Pamięć, Projekty, Ustawienia) — to jest kontrakt `MainViewModel`,
  **nie zmieniaj liczby ani kluczy**. Dodaj: aktywny wskaźnik 2px po lewej, hover/focus ring,
  licznik „co jest do zrobienia" jako badge (tylko dane, które już istnieją: np. liczba zadań otwartych z
  `TaskViewModel`, liczba niearchiwizowanych rozmów) — **jeśli VM tego nie wystawia, nie kłam i nie dodawaj
  pobierania danych z widoku**.
- Dół sidebaru: „LOCAL INTELLIGENCE", badge stanu głosu, `ϟ EMERGENCY STOP` (`SxDangerButton`),
  „Wznów Sentinel" (widoczny gdy `IsStopped`), „Wyjdź z aplikacji". Zostaje logika `BoolToVisibility`.
- Nakładka aurora: zostaje, ale przenieś do `Themes` jako styl `SxAuroraLayer`, żeby strony mogły ją włączać.
- `ContentControl` dla `CurrentPage`: dodaj przejście `u:PageTransitions.Enabled="True"` (na stronach
  już jest — nie dubluj), zachowaj stan VM (DI trzyma instancje — nie wprowadzaj nawigacji, która rebuilduje
  ViewModel).

### S6.2 Centrum (`Views/Pages/CommandCenterPage.xaml`) — najważniejszy ekran
- Pasek 9 zakładek-ikon dziś: ikony bez podpisów, `WrapPanel`, `Focusable="False"`. **To jest główny problem
  czytelności.** Rozwiązanie: ikona + etykieta (skrót do 1 słowa), tooltip z `Label`, widoczny selected-state,
  nawigacja klawiaturą (strzałki) i `AutomationProperties.Name="{Binding Label}"`. Zachowaj
  `ItemsSource={Binding Sections}` i `SelectedTab TwoWay` (smoke tego dotyka).
- Nagłówek: tytuł „Centrum", `ConversationTitle`, „Tryb prywatny", badge `VOICE · {State}`,
  przełącznik nasłuchu z tekstem WŁ/WYŁ (zostaje `DataTrigger` na `VoiceActive`), badge `ROZMOWA PRYWATNA`.
- Karta z dymkami: zachowaj `MaxWidth=740`, zaokrąglenia kierunkowe, `u:ChatScroll.Follow="True"`, kartę
  `ActionRecord` (Status/ActionId/Phase/Elapsed/StorageWarning/Evidence) z konwerterem `StatusBrush`,
  blok `IsStreaming` z `StreamingText`.
- **Ulepszenia obowiązkowe:**
  - akcje na dymku widoczne po najechaniu (kopiuj treść, pokaż surowe dane, skok do historii) — tylko
    komendy, które istnieją; brak — nie dodawaj przycisku;
  - znaczniki czasu grupowane po dniu + separator „wczoraj / data", bez zaglądania do plików;
  - pasek postępu generowania oparty o `IsBusy`/`IsStreaming`/`CurrentAction.Phase` (żadnych
    „przypuszczalnych %");
  - karty zgody (`HasPermission`) w wersji wyróżnionej: kolor wg ryzyka LOW/MEDIUM/HIGH/CRITICAL przez
    `RiskBrush`, jawny napis „to jest jednorazowa zgoda”, anulowanie `Esc`, timer wygaśnięcia pokazany tylko
    jeśli ViewModel zna czas.
- Tło/cienie/typografia wg S5; żadnego hexa w pliku (R4).

### S6.3 Pozostałe strony — ta sama zasada „stan > dekoracja"
Dla każdej strony wymagam: (a) nagłówek + 1 zdanie co strona robi, (b) stan pusty z konkretną akcją,
(c) stan ładowania bez fałszywego licznika, (d) stan błędu z tym, co zrobić, (e) wirtualizacja przy listach,
(f) wszystkie liczby z istniejących VM, (g) kopiowalna treść techniczna (`TextBox IsReadOnly` + selekcja,
`IsReadOnlyCaretVisible=True`).

- `SystemPage` — kafle CPU/RAM/GPU/VRAM + top procesy + dyski; VRAM **musi** wyświetlać informację o braku
  wiarygodnego licznika (patrz S8); paski postępu `Mode=OneWay`; sortowalna tabela procesów przez
  `ICollectionView` bez dotykania serwisu.
- `DiagnosticsPage` — snapshoty: dwa odczyty obok siebie (różnica linia po linia, tak jak liczy serwis),
  eksport Markdown/JSON z SHA-256 jako tekst do skopiowania, limit 20 pokazany uczciwie „20/20”.
- `MemoryPage` — lista wspomnień z filtrami kategorii, przypięte na górze, edycja/usuwanie po ID, statusy
  „nieaktualne”, wyszukiwarka z **uczciwym komunikatem**, że wyszukiwanie treści jest dosłowne (bez fuzzy) —
  to znane ograniczenie z README; nie sugeruj „inteligentnego" dopasowania.
- `ProjectsPage` — karty projektów + badge „aktywny”, izolacja kontekstu wyjaśniona jednym zdaniem,
  eksport JSON.
- `TasksPage` — filtry Dzisiaj/Wszystkie/Przeterminowane/Zrobione, usuwanie wyłącznie przyciskiem z
  potwierdzeniem, daty w formacie czytelnym z godziną, stan „przegapione przypomnienia” wyróżniony.
- `HistoryPage` — filtry statusu (`VERIFIED` itd.), eksport JSON/CSV z widocznym SHA-256 (`ExportSummary`).
- `VoicePage` — urządzenia, miernik (RAW i PROCESSED jako dwa paski, dziś legacy tak ma — przenieś wzorzec),
  kalibracja szumu, postęp pobierania modelu (tylko to, co serwis raportuje), lista modeli lokalnych.
- `AiPage` — wybór modelu, status, zwalnianie z pamięci, podgląd „co trafiło do modelu i dlaczego” (bez
  pełnych promptów).
- `GamingPage` — lista gier, overlay (pozycja/skala/przezroczystość z `UiSettings`), Watch z progami;
  wyraźny brak pomiaru FPS/latencji (nie ma go).
- `ActionsPage` — kolejka i dowody: VERIFIED/UNVERIFIED/FAILURE jako stan kolorystyczny, ale **zawsze**
  ze słowem opisującym, nie samym kolorem (dostępność).
- `SettingsPage` — generator pól (`SettingsCatalog`) zostaw, ale podnieś jakość: grupy przyklejone
  (`StickyGroupStyle` przez `ItemsControl.AlternationCount`/`GroupBox` — bez code-behind), walidacja inline
  w miejscu błędu, „przywróć sekcję” potwierdzone, skróty klawiszowe, pole wyszukiwania z `InputHint`.
  Sekcje `Wygląd` są tu najważniejsze: motyw, akcent, animacje, przezroczystość/skala/pozycja nakładki,
  close/minimize to tray — **każde ustawienie z `UiSettings` musi mieć tu sterowanie**; braki zgłaszaj.
- `Controls/CommandPalette.xaml` — okno 780 px jak dziś; dodaj: grupowanie wyników (strony / komendy /
  narzędzia), podświetlanie dopasowania, licznik wyników, nawigacja ↑↓/Enter/Esc, `SearchInput` **zachowane**,
  przewijanie do wybranego (`ScrollIntoView` — bez code-behind, przez `AttachedBehavior` w `Utilities/`).
- `Controls/ReadinessPanel.xaml` — 4 karty `Checks` (asercja smoke: `Count == 4`), stan `StateLabel` z
  `ReadinessBrush`, `NextStep` jako `OpenSectionCommand` z `PageKey`; dodaj pasek postępu odświeżania i
  `Esc`/scrim-klik do zamknięcia (już `CloseCommand` istnieje).
- `Views/Overlay/OverlayWindow.xaml` — zachowaj `WindowStyle=None AllowsTransparency=True Topmost
  ShowInTaskbar=False SizeToContent` + `DragOverlay` (to jedyny wyjątek od reguły „nie ruszać overlaya", jeśli
  chcesz poprawić: zmiana musi utrzymać działanie nad grami okienkowymi, patrz README). Dodaj
  CPU/RAM/GPU/VRAM w wersji kompakt, przełączany skrótami z `GlobalHotkeyService` **tylko** jeśli API już to
  wystawia.

---

## S7. DOSTĘPNOŚĆ, JASNOŚĆ TEKSTU, POLSKI

- Wszystkie etykiety kontrolki przez `AutomationProperties.Name` po polsku (wzorzec już w repo — trzymaj się).
- Kontrast: ≥ 4,5:1 dla tekstu normalnego, ≥ 3:1 dla dużego i elementów nietekstowych, **w każdym motywie**
  (w Light też — sprawdź `SxTextMuted` na `SxSurface`). Jeśli token nie spełnia, skoryguj wartość `*Color`
  w słownikach motywu, nie w widoku.
- Stan nigdy nie jest wyrażony tylko kolorem: kolor + tekst + kształt/ikona.
- Skupienie: widoczny ring 2px (`SxFocusRing`), brak `Focusable="False"` na czymkolwiek interaktywnym
  (wyjątek: dzisiejszy `ListBox` zakładek Centrum — jeśli zostawiasz, musi być działająca alternatywa
  klawiaturowa).
- Mapa klawiszy (dokumentuj w UI jako `ToolTips` + wpis w „pomocy”): `Ctrl+K` paleta, `Ctrl+Shift+X` STOP,
  `Ctrl+Shift+S` pokaż okno, `Esc` zamknij nakładkę/paletę/anuluj, `Tab`/`Shift+Tab` w palecie `//`,
  `Enter` wyślij, `Alt+1..4` nawigacja (jeśli dodajesz — przez `KeyBinding` w XAML, bez code-behind).
- Kopiowalność: każde dane techniczne w elemencie readOnly-selectable; przycisk „Kopiuj” tam, gdzie tekst ma
  być przeniesiony do schowka.
- Polski: przecinek dziesiętny, spacje przed jednostkami, bez anglicyzmów w UI („settings”, „toggle”) — nazwy
  typów/kodów zostają angielskie. Daty: `HH:mm`, `yyyy-MM-dd`. Nie tłumacz nazw własnych (CS2, Discord).
- Nie używaj sformułowań, które obiecują więcej niż kod: „inteligentne planowanie”, „AI wie”, „w pełni
  bezpieczne”. Język: rzeczowy, techniczny, z dowodem („Zrobione: X. Potwierdzenie: Y. Niepotwierdzone: Z.”).

---

## S8. UCZCIWOŚĆ UI — reguła produktowa, nie kosmetyka

`ActionExecutionResult` rozróżnia `VerifiedSuccess` / `UnverifiedSuccess` / `Failure`; status `Verified`
w historii tylko dla niezależnego odczytu zwrotnego. UI musi to oddawać dosłownie.

- Zakaz: zera dla braku pomiaru (VRAM = „brak wiarygodnego licznika”, nie `0%`), fałszywe „gotowe”,
  „AI myśli…” przy braku modelu, animacja „ładowania” przy operacji, która się nie wykonuje, zielony kolor
  dla `UnverifiedSuccess`, zaokrąglanie w górę, statyczne demo-dane.
- Każde nieudostępnione źródło danych = jawny napis z powodem i co zrobić (np. „Ollama nie odpowiada —
  uruchom `ollama serve`”).
- Każde pole, które przyjmuje wejście, waliduj przy pomocy istniejącej walidacji (`Error` w ustawieniach) —
  bez własnej logiki w widoku.
- Nie dodawaj przycisków „w budowie”, nie zostawaj `TODO` w XAML, nie komentuj funkcji zamiast jej usunąć.

---

## S9. WYDAJNOŚĆ (budżet; mierzalne i uzasadnione w raporcie)

- Lista rozmów: `ItemsControl` dziś. Jeśli liczba urośnie — `VirtualizingStackPanel.IsVirtualizing=True`
  + `ScrollViewer.CanContentScroll=True` **albo** pozostaw `StackPanel` (płynne przewijanie) i wyjaśnij wybór.
  Nie zmieniaj na `ListBox` bez powodu (zmienia fokus/keyboard nav).
- Brak `DropShadowEffect`/`BlurEffect` w szablonach elementów powtarzalnych; brak `Opacity` na kontenerach z
  animacją (tworzy warstwę offscreen); gradienty statyczne, nie przeliczane w `DataTrigger`-ach co tick.
- Timerów w widoku zero — odświeżanie metryk przychodzi z serwisów (`SystemMonitor`, `WatchSettings`); jeśli
  potrzebujesz odświeżania, użyj istniejącego `StateChanged`. Nie wprowadzaj `DispatcherTimer` w code-behind
  (R5 + logika w widoku).
- Bindings: `Mode=OneWay` wszędzie, gdzie ViewModel nie przyjmuje zwrotnie (patrz R2), `UpdateSourceTrigger`
  jawne tam, gdzie pole ma reagować na znak (jak `Search`). Nie dubluj konwerterów — istniejące 4 wystarczą;
  jeśli potrzebujesz 5., zrób `IMultiValueConverter` w `Converters/` i zarejestruj w `App.xaml` z `Sx`-
  przyjazną nazwą... **uwaga**: R3 dopuszcza w widokach tylko `Sx\w+` i 4 istniejące klucze — więc nowy
  konwerter też musi mieć klucz zaczynający się od `Sx` (np. `SxIconGeometry`), żeby przeszedł bramkę.
- Cel: otwarcie okna < 1 s na typowym desktopie, przewijanie 60 fps bez janków na liście 720 wpisów rozmowy
  (limit magazynu). Zmierz i podaj w raporcie metodę (np. `Stopwatch` w `--ui-smoke` / profilowanie manualne),
  nie podawaj liczb, których nie masz.
- Tryb gry: nic nowego w tle, animacje i tak wyłączane przez `SxAnimationsEnabled`.

---

## S10. CZEGO NIE WOLNO ZROBIĆ (antywzorce; każde złamanie = odrzucone)

1. Nowy pakiet NuGet do GUI (WPF-UI, ModernWpf, MaterialDesignThemes, HandyControl, MahApps, SkiaSharp) —
   **zakaz**, dopóki nie dostanę Twojego uzasadnienia + wpływu na rozmiar instalatora i offline-build, i nie
   napiszę „ok”.
2. Przenoszenie logiki do code-behind, `x:Name` + manipulacja w C# (R5), eventy `Click=` w nowym shellu.
3. `WinForms`, `System.Windows.Forms.NotifyIcon`, `UseWindowsForms=true` (R7).
4. `AllowsTransparency=True` lub `WindowChrome` na oknie głównym bez mojej akceptacji kosztu.
5. Hardkodowane hexy / rozmiary czcionek w `Views/**` (R4, R5) i style bez przedrostka `Sx` (R3).
6. Zmiana lub usunięcie `NavItems`, kluczy zakładek `CenterTabByLegacyKey`, `Sections`, `SearchInput`,
   liczby 12 stron, `Count == 4` w `Readiness`, formatu JSON, kolejności `IntentRouter`.
7. „Ujednolicenie" przez skasowanie `MainWindow.xaml`/`Theme.xaml` (legacy) albo przeniesienie ich do `Views/`.
8. Wymyślone pola w ViewModelach, które nic nie zasilają; puste `ICommand`; dwustronne bindowanie stanu
   pomiarowego; podmiana `RenderTargetBitmap`-ów w smoke na coś, co nie renderuje.
9. Zmiana czegokolwiek w `.github/workflows/**` po to, żeby CI „przeszło”.
10. Publikowanie wydań/EXE — w repo jest decyzja użytkownika: `release.yml` publikuje tylko tagi
    `v*.*.*` lub ręczny dispatch; **nie publikuj nic bez wyraźnej prośby**.
11. Angielskie teksty UI, lorem ipsum, „Feature coming soon”.
12. Opisywanie w raporcie czegoś jako „zrobione i przetestowane”, jeśli nie ma na to asercji w smoke lub
    nie było uruchomione w CI.

---

## S11. PLAN PRACY — etap po etapie, z bramkami

Wykonaj i **zatrzymaj się po fazie 2 i po fazie 4** na mój komentarz (chyba że w S0 powiem inaczej).
Każda faza = jeden commit/PR, przechodzący `python3 scripts/check-architecture.py` + zielone CI.

- **F0 AUDYT (bez zmian w kodzie).** Zrób i pokaż: tabelę `Views/*.xaml` → użyte style/klucze → których brakuje;
  listę złamań R1–R8 istniejących dziś; listę ustawień z `UiSettings` bez sterowania w UI; listę pól VM
  nieużywanych w widoku i widocznych dziur; 3 warianty kierunku wizualnego w zdaniach (nie kod) + co każdy
  kosztuje. **Zadaj pytania, jeśli czegoś nie da się ustalić z repo.**
- **F1 TOKENY.** Przebudowa/uzupełnienie `Themes/` (kolory we wszystkich motywach, typografia wg skali,
  `Icons.xaml`, warstwy głębi). Zero zmian stron. Cel: `Light` nie wygląda jak zepsuty `Dark`.
- **F2 SHELL.** `Views/MainWindow.xaml` + zachowania w `Utilities/` (rail/expand, zapamiętywanie rozmiaru
  i ostatniej strony przez `UiSettings`), nagłówek stanu, licznik STOP, skróty. Smoke nadal zielony.
- **F3 CENTRUM.** Zakładki z etykietami, dymki, karta dowodów, zgody, streaming, pasek `//`.
- **F4 STRONY.** Kolejność wg ryzyka nieczytelności: `System` → `Diagnostics` → `Tasks` → `Memory` →
  `History` → `Projects` → `Voice` → `AI` → `Gaming` → `Actions` → `Settings`.
- **F5 NAKŁADKI + OVERLAY.** Paleta (grupowanie, focus, `ScrollIntoView`), panel gotowości, overlay.
- **F6 TESTY.** Rozszerz `UiSmokeTestRunner.cs` o asercje nowego UI (patrz S12). **Nie zdejmuj ani jednej
  istniejącej asercji** — tylko dokładaj.
- **F7 DOKUMENTACJA.** Sekcja „GUI” w `README.md`, wpis `docs/RELEASE-<kolejny>.md`, aktualizacja
  `docs/BACKLOG.md` (P0#6 motywy, P0#3 panel archiwum, P1) + `docs/ARCHITECTURE.md` jeśli doszły pliki.
- **F8 OKRUSZKI.** Skróty klawiszowe, stany błędów, mikrotypografia, 3 przebiegi manualne (Dark/Deep Dark/Light).

Jeśli faza wymaga ruszenia zakazanego pliku — **stop, raportuj, czekaj**. Nie przeskakuj do następnej fazy
z „drobnym wyjątkiem”.

---

## S12. TESTY: co musisz zrobić w warstwie testów widoku

W `UiSmokeTestRunner.cs` (uruchamiane `SentinelX.exe --ui-smoke <katalog>` na Windows, renderuje każdą stronę
i zbiera błędy wiązań z `PresentationTraceSources`) dodaj asercje dla nowego UI, np.:
- render przy `ActualWidth` 1100 i 1800 px nie rzuca wyjątku; rail/expand faktycznie zmienia `IsHitTestVisible`
  obszar nawigacji;
- każda zakładka Centrum ma widoczny tekst etykiety (`AutomationProperties.Name` niepuste) i da się ją
  wybrać klawiaturą;
- przełączenie motywu na `Light` nie pozostawia elementów z tokenem niezdefiniowanym (sprawdź przez
  `TryFindResource` na znanej liście kluczy — bez czytania plików, bez `File.*` w VM);
- brak błędu `Sx*` w outputcie trace'ów (reguła: lista oczekiwanych braków musi być pusta);
- kod-behind plików widoku nadal < 20 linii (to już jest R5 — asercja w skrypcie architektonicznym,
  uruchom `python3 scripts/check-architecture.py` lokalnie i wklej wynik `PASS: …`);
- `SaveWindowGeometry` → restart symulowany (utwórz drugi shell i sprawdź rozmiar), jeśli dodałeś
  zapamiętywanie okna; pilnuj, by `SENTINEL_UI_SMOKE` nadal pomijał zapis (jak dziś w legacy).
Zachowaj fakt, że smoke **nie** włącza mikrofonu, nie instaluje modeli, nie rusza autostartu — nie dodawaj
takich akcji do testu.

---

## S13. JAK MA ODPOWIEDZIEĆ (format, każdy etap)

1. **Pytania / Potrzebuję decyzji** — na początku, maks. 7, każde z Twoją rekomendacją i kosztem. Jeśli nie ma
   pytań — napisz „Brak pytań; zakładam: …”.
2. **Kontekst zweryfikowany** — które pliki przeczytałeś (nazwy, nie ogólniki) i 3 fakty, które zmieniły Twój
   plan.
3. **Plan** — fazy z punktu S11, z przewidywanym ryzykiem na fazę.
4. **Diff** — plik po pliku, z komentarzem „dlaczego ten plik”; bez zmian, których nie ma w zakresie S4.
5. **Mapa: stan → źródło danych → widżet** — tabela dla każdego nowego elementu UI; wiersz, w którym nie ma
   źródła, musi zniknąć z implementacji.
6. **Wyniki bramek** — dokładny output `python3 scripts/check-architecture.py`, lista asercji smoke
   dodanych/usuniętych (usuniętych ma być 0), status CI, które przeoczyłeś i dlaczego.
7. **Czego świadomie nie zrobiłem** — lista + konsekwencja (nie „potem”, a „wymaga X, bo Y”).
8. **Potencjalne regresje do sprawdzenia na moim komputerze** — mikrofon, Ollama, gry, DPI, tray, hotkey.

Nie skarż się na ograniczenia — wykorzystaj je. Nie tłumacz się stylem, tłumacz liczbami i nazwami plików.
Nie zaczynaj od „Świetne pytanie”. Nie powtarzaj mi tego, co już wiem z S2 bez dodania wartości.
Jeśli któraś z moich instrukcji jest wewnętrznie sprzeczna (np. R4 vs „gradienty”) — wskaż sprzeczność
i zaproponuj rozwiązanie **przed** kodem.

---

## S14. DEFINICJA UKOŃCZENIA (wszystko tak/nie, bez „w zasadzie”)

- [ ] `python3 scripts/check-architecture.py` → `PASS` (R1–R8).
- [ ] CI `windows-build.yml`: restore, build, `--ui-smoke`, regresje, portable, instalator — zielone
      (czerwony `upload-artifact` z limitu kwoty artefaktów to osobny, znany problem — udokumentuj, nie naprawiaj workflow).
- [ ] Zero literałów hex i zero rozmiarów czcionek w `Views/**/*.xaml`; każdy nowy zasób to `Sx*` zdefiniowany w `Themes/`.
- [ ] Dark / Deep Dark / Light: wszystkie 12 stron + 9 zakładek Centrum + paleta + panel gotowości + overlay
      renderują się bez błędu wiązania i bez tekstu poniżej kontrastu.
- [ ] Zmiana rozmiaru okna: 1100×700 i 1400×900 i 2560×1440; 100/125/150/200% DPI bez obcięcia; rozmiar i
      ostatnia strona zapamiętane (jeśli było w zakresie F2).
- [ ] Klawiatura: paleta (Ctrl+K, ↑↓/Enter/Esc, fokus wraca), `//` w czacie (Tab/Shift+Tab), nawigacja
      zakładek Centrum, `Esc` zamyka nakładki, Enter wysyła, Ctrl+Shift+X zatrzymuje.
- [ ] Każdy stan: pusty / ładowanie / błąd / brak danych / STOP / tryb prywatny / brak zgody — widoczny,
      opisany słowem, z jedną akcją.
- [ ] Code-behind każdego widoku < 20 linii; brak `Click=` w nowym shellu; zachowania w `Utilities/`.
- [ ] Legacy UI (`--legacy`) nadal działa i nie zmieniło ani jednego znaku.
- [ ] `UiSmokeTestRunner.cs`: 0 usuniętych asercji, ≥ 4 nowe, wszystkie dot. GUI przechodzą.
- [ ] README + `docs/RELEASE-*.md` + `docs/BACKLOG.md` zaktualizowane; żaden wpis nie twierdzi, że funkcja
      istnieje, jeśli nie istnieje.
- [ ] Raport w formacie S13 dostarczony, z sekcją „czego nie zrobiłem”.

---

## ZAŁĄCZNIK A — inwentarz zasobów do użycia (R3 dozwala tylko te)

Style kontrolek (`Themes/Controls.xaml`): `SxSecondaryButton` (domyślny `TargetType=Button`),
`SxPrimaryButton`, `SxDangerButton`, `SxGhostButton`, `SxIconButton`, `SxPillButton`, `SxSidebarItem`,
`SxCard`, `SxCardElevated`, `SxMetricCard`, `SxBadge`, `SxTextBox`, `SxToggle`, `SxProgressBar`,
`SxComboBox`, `SxScrollBarThumb`, `SxScrollBar`, `SxTooltip`, `SxSlider`.

Typografia (`Themes/Typography.xaml`): `SxFontFamily`, `SxFontHeading1..3`, `SxFontSubheading`, `SxFontBody`,
`SxFontCaption`, `SxFontSmall`, `SxFontMicro`, style `SxHeading`, `SxCaption`, `SxSectionTitle`.

Tokeny kolorów (`Themes/Colors.xaml`): `SxBackground`, `SxSurface(+Hover,+Active)`, `SxSidebar(+Hover,+Active)`,
`SxBorder`, `SxBorderSubtle`, `SxTextPrimary/Secondary/Muted/Disabled`, `SxAccentCyan(+Dim)`,
`SxAccentViolet`, `SxAccentBlue`, `SxAccentGradStart/End`, `SxSuccess(+Dim)`, `SxWarning(+Dim)`,
`SxError(+Dim)`, `SxVoiceActive/Standby/Off`, `SxOverlayBg`, `SxScrim`, `SxUserBubble`, `SxWhite`,
`SxTransparent` oraz pary `*Color`. Gradient: `SxAccentGradient` (`Themes/Brushes.xaml`).
Animacje: `SxFadeIn`, `SxFadeOut` (`Themes/Animations.xaml`).
Konwertery: `ReadinessBrush`, `StatusBrush`, `VoiceBrush`, `RiskBrush`, `BoolToVisibility`.
Motywy nadpisujące `*Color`: `Themes/DarkTheme.xaml`, `Themes/DeepDarkTheme.xaml`, `Themes/LightTheme.xaml`.

## ZAŁĄCZNIK B — typowe potknięcia LLM w WPF (sprawdź się przed oddaniem)

- `StaticResource` do klucza z `App.xaml` w `UserControl.Resources` — działa; `StaticResource` do klucza
  definiowanego **poniżej** w tym samym słowniku — wyjątek. Używaj `DynamicResource` dla wszystkiego, co
  nadpisuje motyw (`*Color`, akcent z `UiSettings`).
- `{Binding RelativeSource={RelativeSource AncestorType=…}}` w `DataTemplate` wewnątrz `ListBoxItem` —
  działa; `ElementName` nie działa, bo template ma własną nazwę. Patrz istniejący wzorzec
  `DataContext.EmergencyStopCommand` w `ReadinessPanel`.
- `ListBox` z `Focusable="False"` = brak obsługi klawiatury; świadoma decyzja musi być w raporcie.
- `ProgressBar` bez `Mode=OneWay` = czerwone CI (R2).
- `ItemsControl` bez `ScrollViewer.CanContentScroll` = brak wirtualizacji; `StackPanel` jako `ItemsPanel`
  w `ListBox` = pełna wirtualizacja znika.
- `TextWrapping=Wrap` + `MaxWidth` w dymku — bez `MaxWidth` dymek rozpycha kolumnę i łamie layout.
- `CornerRadius="16"` na `Border` z `Background` gradient i `ClipToBounds` — gradacja tła a narożniki
  wymagają jednego `Border` (nie `Rectangle`+maski).
- `ThemeMode` na `Window` to WPF .NET 9 (Fluent). Nie wprowadzaj `Application.Current.ThemeMode` ani API
  z win32 `DwmSetWindowAttribute` — jest już obsłużone przez `DesktopService`.
- `BitmapEffects`/`ShaderEffect` — nie.
- `x:Uid`, `x:Static` dla stringów — zbędne, UI i tak ma być w XAML (bez nowego systemu lokalizacji).
- Nowy `x:Key` bez przedrostka `Sx` = `Unknown resource` w CI (R3).
- `AutomationProperties.Name="{Binding Label}"` na kontrolce w `DataTemplate` — OK; na `ListBoxItem`
  przez `ItemContainerStyle` wymaga `TemplateBinding` — użyj istniejącego wzorca z `SxSidebarItem`.

## ZAŁĄCZNIK C — komendy, których używasz

```bash
python3 scripts/check-architecture.py            # bramka architektury (działa na Linuxie)
grep -rn '="#' Views/ Themes/                     # polowanie na hardkodowane hexy (R4)
grep -rno 'x:Key="[^"]*"' Themes/*.xaml | sort -u  # czy klucz istnieje i nie jest duplikatem
grep -rn "SearchInput\|NavItems\|Sections\|Checks.Count" UiSmokeTestRunner.cs   # kontrakty testów
```
```powershell
# Tylko na Windows z .NET 9 SDK — buduje i odpala prawdziwy render WPF:
dotnet build -c Release
.\bin\Release\net9.0-windows\SentinelX.exe --ui-smoke "$PWD\test-results\ui"
.\bin\Release\net9.0-windows\SentinelX.exe --self-test "$PWD\test-results\regression"
.\bin\Release\net9.0-windows\SentinelX.exe --legacy    # porównanie ze starym UI
```

---

**Zacznij od F0 (audyt). Nie pisz kodu przed moją akceptacją planu, jeśli w audycie znajdziesz sprzeczność
w tym prompcie z rzeczywistością repo.**
