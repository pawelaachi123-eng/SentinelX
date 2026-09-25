# SENTINEL X — MASTER PROMPT GUI (wersja docelowa, repo-gruntowna)

> Do wklejenia całości agentowi kodowemu z dostępem do repo `pawelaachi123-eng/SentinelX`.
> Wszystkie nazwy typów, właściwości i komend są przepisane z kodu i opatrzone numerami linii,
> więc wykonawca może (i musi) je zweryfikować zamiast mi ufać.
> Wersje krótsze: `docs/GUI-MASTER-PROMPT-1500.md`, `docs/GUI-MASTER-PROMPT-DOPISKI.md`.
> Uzupełnij sekcję 0 przed wysłaniem. Reszta nie wymaga Twojej ręcznej edycji.

---

## 0. CO MUSZĘ UZUPEŁNIĆ (jeśli puste — zapytaj, nie zgaduj)

```
BOLI MNIE NAJBARDZIEJ: <np. „Centrum z 9 ikonami bez podpisów", „każda strona to lity blok tekstu">
ZOSTAJE BEZ DYSKUSJI: <np. „ciemny motyw domyślny", „czat jako ser", „sidebar po lewej">
MAM CZAS NA: <jedna sesja / dzień / tydzień iteracji z CI>
MAM .NET SDK LOKALNIE: <tak / nie>
WYBÓR KIERUNKU WIZUALNEGO (sekcja 6): <A kontrola / B laboratorium / C fluencik — albo „zaproponuj">
```

---

## 1. ROLA, CEL, KRYTERIA

**Rola:** starszy inżynier UI, WPF na .NET 9, autorskie systemy wizualne dla narzędzi systemowych klasy profesjonalnej. Nie dekorator, nie architekt backendu.

**Cel:** interfejs, w którym w 3 sekundy po otwarciu okna widać cztery rzeczy i żadnej więcej nie trzeba szukać:

1. czy Sentinel jest gotowy,
2. co właśnie robi,
3. czy mogę wydać polecenie (i dlaczego nie, jeśli nie mogę),
4. jaki był skutek ostatniego polecenia i czy jest potwierdzony.

**Kryteria sukcesu (mierzalne, nie smakowe):**

| # | Kryterium | Jak sprawdzasz |
|---|---|---|
| K1 | Każdy ekran startuje w ≤2 kliknięcia z poziomu okna (paleta `Ctrl+K` się nie liczy, bo jest skrótem) | tabela w raporcie: ekran → ścieżka kliknięć |
| K2 | Każdy stan (gotowość, praca, błąd, STOP, prywatność, brak danych) ma reprezentację tekstową, nie tylko kolor | grep po `Visibility`/`DataTrigger` i czytanie |
| K3 | Cały UI obsłużysz klawiaturą, bez myszy, w tym zakładki Centrum i listy | `--ui-smoke` + lista klawiszy z sekcji 9 |
| K4 | Zero literałów koloru w `Views/**` i zero literałów rozmiaru czcionki w stylach | `python3 scripts/check-architecture.py` |
| K5 | Żadna liczba w UI bez źródła: każdy binding wskazuje na istniejące pole ViewModelu | tabela „stan → pole → widget" (sekcja 14.5) |
| K6 | Trzy motywy (Dark, Deep Dark, Light/System) × 12 stron × 9 zakładek renderują się bez błędu wiązania i bez tekstu poniżej 4,5:1 | `--ui-smoke` + próbka kontrastu w raporcie |

**Celowo NIE jest celem:** „nowocześniejszy wygląd", animacje dla animacji, przezroczyste okno, jeden wielki redesign backendu, „naprawa" rzeczy, o które nie proszę (patrz 16.6).

---

## 2. STAN FAKTYCZNY REPO

### 2.1 Pliki i ustawienia projektu

`SENTINEL-X.csproj`: `OutputType=WinExe`, `TargetFramework=net9.0-windows`, `UseWPF=true`, `LangVersion=13.0`,
`Nullable=enable`, `ImplicitUsings=enable`, `PlatformTarget=x64`, `RootNamespace=SentinelX`,
`AssemblyName=SentinelX`, `Version=0.93.0`, `EnableWindowsTargeting=true`, `ApplicationIcon=Assets/sentinel.ico`,
`ApplicationManifest=app.manifest`. SDK-style projekt, więc **każdy** nowy `.xaml` w drzewie wejdzie do kompilacji
bez pytania — nie dokładaj plików „tymczasowo".

Pakiety: `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.DependencyInjection 9.0.0`, `OllamaSharp 5.4.18`,
`System.Speech 9.0.0`, `NAudio 2.2.1`, `Whisper.net 1.9.1` + `Whisper.net.Runtime`, `org.k2fsa.sherpa.onnx 1.13.5`,
`SharpCompress 0.50.4`, `System.Diagnostics.PerformanceCounter 9.0.0`. Nic ponad to bez mojej zgody.

`app.manifest`: `asInvoker`, `PerMonitorV2, PerMonitor`, supportedOS Win10/11. Nie ruszaj.

### 2.2 Dwa UI — świadoma współegzystencja

| | nowy shell (TWÓJ ZAKRES) | legacy (ZAKAZ) |
|---|---|---|
| okno | `Views/MainWindow.xaml`, `x:Class="SentinelX.Views.MainWindow"` | `MainWindow.xaml` w root, `x:Class="SentinelX.MainWindow"` |
| rozmiar | 1400×900, min 1100×700, `ThemeMode="Dark"` | `Title="Sentinel X • 0.85"`, 1260×860 |
| nawigacja | `ListBox` na `MainViewModel.NavItems`, `ContentControl` na `CurrentPage`, `DataTemplate` na VM | `Button Style="{StaticResource Nav}"` + `Tag` + `Visibility="Collapsed"` per strona |
| strony | 12 plików `Views/Pages/*Page.xaml` | strony w jednym pliku, 27 KB |
| style | `Themes/*.xaml`, klucze `Sx*` | `Theme.xaml`, klucze `Accent/Muted/GlassCard/Primary/…` |
| start | `App.xaml.cs` → DI → `Views.MainWindow` | `App.xaml.cs` przy `--legacy` i `--self-test` |

`App.xaml.cs` decyduje: `if (selfTest || e.Args.Contains("--legacy")) → new MainWindow(...)`, w przeciwnym razie
`provider.GetRequiredService<Views.MainWindow>()`. Nie „ujednolicaj" tych dwóch światów. Legacy zostaje nietknięte,
łącznie z `Theme.xaml`, `MainWindow.xaml.cs`, `MainWindow.Commands.cs`, `MainWindow.Settings.cs`,
`MainWindow.Voice.cs`, `SettingsEditorView.cs`, `MetricOverlay.cs`, `TrayService.cs`.

### 2.3 Jak działa motywowanie (kluczowe dla każdego nowego stylu)

1. `App.xaml` scala w kolejności: Fluent z `PresentationFramework.Fluent` (wbudowany w .NET 9, bez NuGet),
   `Themes/Colors.xaml`, `Themes/Brushes.xaml`, `Themes/Typography.xaml`, `Themes/Controls.xaml`,
   `Themes/Animations.xaml` + konwertery `ReadinessBrush`, `StatusBrush`, `VoiceBrush`, `RiskBrush`, `BoolToVisibility`.
   Brak `StartupUri`.
2. W działaniu `Services/Desktop/DesktopService.cs` dokleja słownik motywu:
   `light ? "LightTheme" : Theme == "Deep Dark" ? "DeepDarkTheme" : "DarkTheme"` (linia ~126), ustawia
   `window.ThemeMode` (~125) i nadpisuje `Application.Current.Resources["SxAccentCyanColor"]` z
   `Ui.AccentColor` (~131). Animacje: `Application.Current.Resources["SxAnimationsEnabled"] =
   Ui.AnimationsEnabled && nie gram` (~114 i ~132).
3. Dlatego obowiązuje **wzorzec tokenu**: `Themes/Colors.xaml` trzyma parę

       <Color x:Key="SxFooColor">#…</Color>
       <SolidColorBrush x:Key="SxFoo" Color="{DynamicResource SxFooColor}"/>

   a słowniki `DarkTheme`/`DeepDarkTheme`/`LightTheme` nadpisują **tylko** klucze `*Color`.
   Brush z literałem koloru = motyw przestaje działać. To traktuję jak błąd, nie stylistykę.
4. Wiadomo, że `LightTheme` (15 linii) i `DeepDarkTheme` (6 linii) nadpisują tylko podzbiór tokenów.
   Twoim zadaniem jest to domknąć tam, gdzie odziedziczona wartość wygląda źle — patrz K6.

### 2.4 Kontrakty ViewModeli (widok może bindować tylko to)

`MainViewModel`: `Version`, `NavItems` (`NavItem(Key, Icon, Label, ViewModel)`; dokładnie
`command/Centrum`, `memory/Pamięć`, `projects/Projekty`, `settings/Ustawienia`), `SelectedItem`, `CurrentPage`,
`IsStopped`, `DesktopStatus`, `Voice`, `Palette`, `Readiness`, komendy `OpenPaletteCommand`,
`OpenReadinessCommand`, `InitializeCommand`, `EmergencyStopCommand`, `ResumeCommand`, `ExitCommand`.
Mapa `CenterTabByLegacyKey` (plik ~linia 24) kieruje dawne klucze `command, tasks, history, voice, system,
gaming, ai, actions, diagnostics` do zakładek Centrum.

`CommandCenterViewModel`: `record CenterTab(string Key, string Icon, string Label, object ViewModel)` (linia 14),
`Sections` (9 pozycji: rozmowa, zadania, historia, glos, system, gry, ai, akcje, diagnostyka), `SelectedTab`,
`IsChatTab`, `SubTabContent`, `ObservableCollection<ConversationMessage> Messages` (linia 31), `UserInput`,
`Status`, `IsBusy`, `IsStopped`, `HasPermission`, `PermissionSummary`, `ConversationTitle`, `IsPrivateMode`,
`IsStreaming`, `StreamingText`, `SlashOpen`, `SlashIndex`, `SlashItems`, `VoiceActive`, `CurrentAction`,
`InputFocusVersion`, komendy `SendMessageCommand`, `StopGenerationCommand`, `RetryCommand`,
`QuickCommandCommand`, `ApproveCommand`, `CancelCommand`, `TogglePrivateModeCommand`, `ToggleVoiceCommand`,
`SelectTabCommand`.

`SystemViewModel`: jedno pole `Snapshot` typu `Models/SystemSnapshot`:
`Timestamp, Cpu, RamUsed, RamTotal, Gpu, Game, Network, Disks, Processes` + computed
`CpuText, GpuText, RamText, VramText, GamingText`; `SystemSnapshot.Empty` istnieje i ma `NaN`-y, a
`VramText => "Niedostępne"` **na stałe** (komentarz w kodzie: „No fabricated VRAM measurements").
`ProcessSnapshot(Name, Pid, MemoryMb, CpuPercent?)`, `DiskSnapshot(Name, UsedGb, TotalGb)` z `Percent` i `Label`.

`VoiceViewModel`: `State`, `Status`, `Metrics`, `ObservableCollection<string> Microphones`;
`record VoiceMetrics(VoiceState State, double Raw, double Enhanced, double Noise, double Snr, double Gain,
bool SpeechDetected, string Transcript)` (`Services/Voice/IVoiceService.cs:3`);
komendy `RefreshMicrophonesCommand`, `StartCommand` + `StartCancelCommand`, `DownloadModelsCommand` +
`DownloadModelsCancelCommand` (oba z `IncludeCancelCommand = true`), `StopCommand`, `CalibrateCommand`.

`HistoryViewModel`: `Entries`, `Conversation`, `FilteredEntries`, `SelectedEntry`, `StatusFilter`,
`StatusOptions = [Wszystkie, VERIFIED, UNVERIFIED, FAILED, CANCELLED, PENDING, RUNNING, INTERRUPTED]`,
`Search`, `Status`, `ExportSummary`; komendy `RefreshCommand`, `ExportCommand(format)`.

`TaskViewModel`: `Items` (`TaskItemViewModel: Id, Title, Priority, HighPriority, StatusLabel, MetaText,
StatusCycleLabel`), `Reminders` (`Id, Text, MetaText, Awaiting, NotifiedAt, Missed`), `ProjectOptions`,
`FilterOptions = [Dzisiaj, Wszystkie, Przeterminowane, Zrobione]`, `PriorityOptions`, `Banner`, `Status`,
pola edytora (`NewTitle`, `NewDueText`, `NewDuePreview`, `NewPriority`, `Editor*`, `NewReminder*`);
komendy `AddTaskCommand`, `AddReminderCommand`, `CycleStatusCommand`, `DeleteTaskCommand`,
`DeleteReminderCommand`, `SelectTaskCommand`, `SaveSelectedCommand`, `RefreshCommand`.

`MemoryViewModel`: `Items`, `Conversations`, `FilterOptions`, `CategoryOptions`, `CategoryFilter`, `PinnedOnly`,
`Search`, `NewText`, `NewCategory`, `EditorText`, `SelectedItem`, `SelectedConversation`, `RenameTitle`,
`ImportPath`, `ImportSummary`, `PrivacyLine`, `ConflictHint`, `IsPrivateMode`, `Status`;
komendy `AddNoteCommand`, `BeginEditCommand`, `CancelEditCommand`, `TogglePinCommand`, `ToggleStaleCommand`,
`RequestDeleteCommand`, `ExportMemoryCommand`, `RefreshDataCommand`, `NewConversationCommand`,
`RenameConversationCommand`, `ResumeConversationCommand`, `PreviewImportCommand`, `RunImportCommand`.

`ProjectViewModel`: `Cards`, `NewName`, `NewDescription`, `ActiveLine`, `Status`, `SelectedCard`, `EditorName`,
`EditorDescription`, `SummaryText`, `ShowArchived`; komendy `CreateCommand`, `SaveSelectedCommand`,
`ToggleStatusCommand`, `ToggleActiveCommand`, `ToggleArchiveCommand`, `NewConversationCommand`,
`AssignActiveConversationCommand`, `ExportCommand`, `WhereDidWeStopCommand`, `SelectCardCommand`,
`RefreshCommand`.

`DiagnosticViewModel`: `Items` (`SnapshotItemViewModel: Id, Label, Detail`), `FirstSnapshot`, `SecondSnapshot`,
`Label`, `Comparison`, `HasComparison`, `CanCompare`, `Status`; komendy `CaptureCommand` + `CaptureCancelCommand`,
`CompareCommand`, `ExportCommand`, `DeleteCommand`, `RefreshCommand`.

`SettingsViewModel`: `Fields` (`SettingViewModel: Section, Label, Description, Choices, IsChoice, IsToggle,
IsText, IsSlider, Minimum, Maximum, Value, NumericValue, Enabled, Error, SaveCommand`), `FilteredFields`,
`Sections = [Wygląd, Głos, AI, Pamięć, Watch, Zasoby, Ogólne, Developer]`, `SelectedSection`, `Search`, `Status`,
komendy `ResetSectionCommand`, `SearchCommand`-owa filtracja jest w `FilteredFields`.

`AiViewModel`: `Models`, `SelectedModel`, `Status`; komendy `RefreshCommand` + `RefreshCancelCommand`,
`SelectModelCommand` + `SelectModelCancelCommand`.

`GamingViewModel`: `System`, `Report`, `Status`; komendy `ToggleOverlayCommand`, `DiagnoseCommand`,
`CancelCommand`.

`ActionsViewModel`: `Tasks` (`ActionRecord`), `HasPermission`, `PermissionSummary`, `Status`;
komendy `ApproveCommand`, `CancelCommand`.

`OverlayViewModel` (`ViewModels/OverlayViewModel.cs:4`) ma **jedno** pole: `System`. Czyli nakładka może
renderować tylko `System.Snapshot.*`. Wybór metryki (`overlayMetric`) istnieje wyłącznie w legacy
(`MainWindow.xaml.cs:39`) — **nie udawaj** przełączania metryk w nowym shellu, jeśli nie zbudujemy do tego
stanu w ViewModelu.

`ReadinessViewModel`: `Checks` (`ReadinessCheck(Key, Title, State, Detail, PageKey, NextStep)`,
`enum ReadinessState { NotChecked, Ready, NeedsSetup, Unavailable }`, `StateLabel` =
GOTOWE / DO KONFIGURACJI / NIEDOSTĘPNE / NIESPRAWDZONE), `IsOpen`, `Summary`, `LastChecked`;
komendy `RefreshCommand`, `RefreshCancelCommand`, `OpenSectionCommand(pageKey)`, `CloseCommand`.

`CommandPaletteViewModel`: `IsOpen`, `Query`, `SelectedEntry`, `HasNoResults`; komendy `ChooseCommand`,
`NextCommand`, `PreviousCommand`, `CloseCommand`. Wpis to
`record PaletteEntry(Title, Description, Keywords, PageKey?, CommandText?)` z computed
`Kind => PageKey != null ? "STRONA" : "POLECENIE"` oraz `Hint`. **Są tylko dwa rodzaje** — nie wymyślaj
trzeciej grupy „narzędzia", jeśli nie zmienisz rekordu (a on jest w `Models/`, czyli poza zakresem).

### 2.5 Słownik statusów i barw (nie wymyślaj własnego kodowania)

`Converters/StatusConverters.cs` mapuje: `VERIFIED|ROLLEDBACK → SxSuccess`, `FAILED → SxError`,
`WAITINGPERMISSION|PENDING|UNVERIFIED → SxWarning`, `RUNNING|VERIFYING → SxAccentCyan`, else `SxTextSecondary`.
Ryzyko: `Low → SxSuccess`, `Medium → SxWarning`, reszta (HIGH, CRITICAL) `→ SxError`.
Gotowość: `Ready → SxSuccess`, `NeedsSetup → SxWarning`, `Unavailable → SxError`, else `SxTextSecondary`.
Stan głosu: `Active → SxVoiceActive`, `Standby → SxVoiceStandby`, else `SxVoiceOff`.
Trzymaj się tych map. Jeśli uznasz, że `UNVERIFIED` nie powinien być ostrzeżeniem — to **decyzja produktowa**
do przedyskutowania, nie do cichej zmiany konwertera.

### 2.6 Ustawienia, które UI już obsługuje, a których nie

`SettingsCatalog.cs` (linie 29–78) to generator pól `SettingsPage`. Obsługuje m.in. `Motyw`, `Kolor akcentu`,
`Przeglądarka`, trzy pozycje nakładki, `Animacje`, `Zamknij do zasobnika`, cały blok `Głos`, `AI`, `Pamięć`
(sześć przełączników prywatności, retencja, archiwum), `Watch`, `Zasoby`, `Start z Windows`,
`Uruchom zminimalizowany`, `Głos przy uruchomieniu`, `Tryb deweloperski`.

Zweryfikowane braki (pole istnieje i kod go używa, ale nie ma sterowania):

- `Ui.MinimizeToTray` — używany w `DesktopService.cs:88`, **brak** wpisu w `SettingsCatalog`.
  Dodanie `Toggle("Ogólne", "Zminimalizuj do zasobnika", …)` to zmiana w pliku poza Twoim zakresem → zapytaj.
- `Ui.WindowWidth` / `Ui.WindowHeight` / `Ui.SelectedPage` — nie mają i nie powinny mieć pól liczbowych
  w ustawieniach (patrz 2.7).

### 2.7 Pułapka współdzielonego `SelectedPage` (spraw, zanim cokolwiek zapiszesz)

Pole `Ui.SelectedPage` czyta legacy (`MainWindow.xaml.cs:70`) i zapisuje legacy (`:228`). Jego `ShowPage`
(`:192`) ma białą listę `Chat, System, Tools, Voice, Gaming, Memory, Settings, AI, Actions, Programs` i dla
wartości spoza listy **po cichu** wraca do `"Chat"`. Klucze shellu to `command, memory, projects, settings`
— zapisanie ich w tym samym polu nie wyrzuci błędu, tylko ukradnie legacy ostatnią stronę.
Rozwiązanie do wyboru, z opisem w raporcie: (a) w ogóle nie persystuj strony z shellu, (b) persystuj przez
jawne mapowanie w obie strony, (c) nowe pole w `UiSettings` — wymaga mojej zgody, bo to zmiana formatu JSON.

Analogicznie rozmiar okna: legacy clampuje w `Models/Settings/SentinelSettings.cs` do
`WindowWidth ∈ ⟨1000, 3840⟩` i `WindowHeight ∈ ⟨720, 2160⟩`, a shell ma `MinHeight=700`. Nie rozjadą tych liczb.
Legacy ponadto pomija zapis/stosowanie w trybie smoke (`if (!smokeMode)`) — zachowaj tę ochronę, jeśli
dodasz persystencję w shellu, bo `--ui-smoke` renderuje okno i nie może mu zmieniać geometrii.

---

## 3. BRAMKI (każde złamanie odrzuca robotę)

Wyliczone wprost ze `scripts/check-architecture.py`, plus to, co wynika z `UiSmokeTestRunner.cs`.

| # | Reguła | Praktyka |
|---|---|---|
| R1 | brak duplikatów `x:Key` w jednym pliku `Themes/*.xaml` | nowe klucze unikalne w pliku |
| R2 | `ProgressBar` z `Value="{Binding …}"` wymaga `Mode=OneWay` | wszystkie mierniki i paski jednokierunkowo |
| R3 | w `Views/**/*.xaml` tylko `{Static,DynamicResource}` do `Sx\w+` oraz `BoolToVisibility`, `StatusBrush`, `VoiceBrush`, `RiskBrush`, `ReadinessBrush` | każdy nowy zasób = `Sx`+prefiks + definicja w `Themes/*.xaml` lub `App.xaml` (nowy konwerter też: `SxIconGeometry`, nie `IconGeometry`) |
| R4 | zakaz literału koloru: `=" #RRGGBB[AA]"` w `Views/**` | kolor wyłącznie przez token; dotyczy `Fill`, `Stroke`, `Color`, `GradientStop` |
| R5 | `Views/**/*.xaml.cs` istnieje i ma <20 linii | zachowania → attached property w `Utilities/`; `Click=` w shellu zakazane |
| R6 | `ViewModels/*.cs` bez `Process.Start(`, `File.Read`, `File.Write`, `Registry.Current`, `ManagementObjectSearcher` | pomiar przez serwis, nie przez VM |
| R7 | csproj: `net9.0-windows`, brak `UseWindowsForms=true` | nie ruszaj csproj |
| R8 | `Views/Pages/*Page.xaml` jest dokładnie 12 | nowa strona = `ServiceLocator.cs:77-94` + `DataTemplate` + zmiana asercji + docs → pytaj |
| R9 | nie ruszaj: `x:Name="SearchInput"` (`CommandPalette.xaml`), `NavItems`, `CenterTabByLegacyKey`, `Sections`, `Readiness.Checks.Count == 4` | testy są od tego |
| R10 | test renderuje każdy `NavItem` i każdą `Sections` przez `RenderTargetBitmap`, zbiera błędy wiązania, cykluje `Deep Dark → System → Dark` | nie zostawiaj strony, która w Light wygląda na zepsutą |
| R11 | paleta podstawia tekst, nigdy nie wykonuje; STOP zachowuje draft i blokuje akcje; głos nie zatwierdza | to asercje, nie sugestia |
| R12 | `Core/AppConstants.cs`: `Version = "0.93 · PORZĄDKI"` jest asercją (`UiSmokeTestRunner.cs:155`: `wersja → 0.93`, `co nowego → PORZĄDKI`) | bump wersji = osobna zmiana: `AppConstants` + ta asercja + tytuł legacy + csproj, za moją zgodą |
| R13 | etykiety w `SettingsCatalog` to kontrakt: `UpgradeRegressionRunner.cs:35` i `tests/BackendRegression.cs:41` robią `Single(x => x.Label == "Próg VAD")` | dopisuj pozycje, nie zmieniaj istniejących `Label` |
| R14 | `--ui-smoke` nie włącza mikrofonu, nie instaluje modeli, nie rusza autostartu, nie dotyka rejestru | nie dokładaj akcji z efektami ubocznymi do testu |
| R15 | publikacja wydań tylko z tagów `v*.*.*` lub `workflow_dispatch`; zwykły push nie wrzuca EXE | nie ruszaj `.github/workflows/**` dla zielonego znacznika |

**Kompilacji nie sprawdzisz lokalnie** (nie ma .NET SDK). Źródłem prawdy jest CI
`.github/workflows/windows-build.yml`: architektura → restore → build → `--ui-smoke` → regresje → portable →
sumy SHA-256 → smoke portable → Inno → instalacja + smoke. Znany szum: czerwone `upload-artifact` z limitu
kwoty artefaktów nie oznacza błędu kodu (patrz `docs/BACKLOG.md`, sekcja ograniczeń) — odnotuj i idź dalej.

---

## 4. ZAKRES

**Wolno:** `Views/**`, `Themes/**`, `App.xaml`, `Converters/**`, `Utilities/**`, `Assets/**`,
`ViewModels/**` (tylko pola i komendy czysto prezentacyjne, bez IO), `UiSmokeTestRunner.cs` (dokładanie asercji),
`docs/**`.

**Nie wolno bez pisemnej zgody:** `Services/**`, `Core/**`, `Models/**`, `.cs` w root poza
`UiSmokeTestRunner.cs`, `SettingsCatalog.cs`, `ThemeService.cs`, `TrayService.cs`, `MetricOverlay.cs`,
pliki legacy, formaty JSON, `SENTINEL-X.csproj`, `.github/workflows/**`, `app.manifest`.

**Nigdy:** zmiana kolejności warstw `Services/Intent/IntentRouter` (LLM jest na końcu potoku i nic nie wykonuje),
obejście centrum zgód, „skrót" polegający na wywołaniu serwisu z widoku.

Zasada: jeśli coś z „nie wolno" wydaje się konieczne, żeby UI był dobry — to znaczy, że **nie jest konieczne
teraz**. Zgłoś, opisz koszt, czekaj. Nie rób wyjątku „na chwilę, i tak to ogarnę".

---

## 5. SYSTEM PROJEKTOWY

### 5.1 Tokeny do dodania (każdy jako para Color/Brush w `Themes/Colors.xaml`)

`SxSurfaceRaised`, `SxBorderActive`, `SxShadow`, `SxCodeBg`, `SxFocusRing`, `SxMetricGood`, `SxMetricWarn`,
`SxMetricBad`, `SxOverlayBgStrong` (jeśli nakładka potrzebuje drugiej warstwy). Progi stanów weź z tego, co już
liczy `WatchSettings` (`CpuAlertPercent`, `RamAlertPercent`) i z progów AI (`Ai.RamPressurePercent`,
`CpuPressurePercent`, `GpuPressurePercent`) — **nie ustalaj własnych magicznych liczb w widoku**.
Token z alfa: 8 heksadecymalnie, jak `SxUserBubbleColor`.

Uzupełnij `Themes/LightTheme.xaml` i `Themes/DeepDarkTheme.xaml` o klucze, w których odziedziczona wartość jest
błędna w tym motywie (dziś pierwszy nadpisuje ~12, drugi 4). Reguła: jeśli w danym motywie token daje kontrast
< 4,5:1 na tle `SxSurface` lub `SxBackground`, ten motyw musi go nadpisać.

### 5.2 Typografia

Skala już istnieje: `SxFontHeading1 28`, `SxFontHeading2 22`, `SxFontHeading3 18`, `SxFontSubheading 15`,
`SxFontBody 14`, `SxFontCaption 12`, `SxFontSmall 11`, `SxFontMicro 10`, rodzina `SxFontFamily`
(Segoe UI Variable, Segoe UI), style `SxHeading`, `SxCaption`, `SxSectionTitle`.
Zadanie: **podmień literały na te klucze** w `Themes/Controls.xaml` i `Themes/Typography.xaml`, tak, żeby jedna
zmiana przesuwała całą aplikację. Dodaj: `SxMono` (Cascadia Mono/Consolas — dane techniczne, ścieżki, ID akcji),
`SxNumber` (monospace, `FontSize` z klucza, waga SemiBold — wielkie liczby w kafelkach),
`SxLabel` (kapitaliki 11, `SxTextSecondary`, `LetterSpacing` nie istnieje w WPF → nie udawaj odstępów liter
melodramatycznym `TextBlock`-iem).

Hierarchia semantyczna, nie ozdobna: liczba > etykieta wartości > kontekst > timestamp. Jeśli na kafelek
składa się pięć równorzędnych rozmiarów, projekt jest zły.

### 5.3 Rytm, narożniki, granice

Odstępy 4/8/12/16/24/32. Margines strony 28 (jak dziś), odstęp kart 12, padding kart 16 (kompakt) / 20 (ustawienia).
Promień: 10 wnętrz, 12 kontrolek, 16 kart, kierunkowy `4,16,16,16` w dymkach (zostaw). Obramowanie 1px, fokus
2px przez `SxFocusRing`. Nic nie ma `Margin="0,13,0,7"`.

### 5.4 Głębia i światło

Zakaz `DropShadowEffect`, `BlurEffect`, `BitmapEffect`, `ShaderEffect` w szablonach elementów powtarzalnych
i w ogóle — w WPF to koszt rasteryzacji na każdy klatkaż przewijania. Płaskość + granica + jeden gradient to
cały aparat głębi, jaki tu dopuszczam. Karta statyczna: `SxCard`; karta nad treścią (dialog, paleta):
`SxCardElevated`; warstwa dekoracyjna za stronami: wyniesiona z `Views/MainWindow.xaml` do stylu
`SxAuroraLayer` w `Themes/Controls.xaml` (Ellipse + `RadialGradientBrush`, `IsHitTestVisible=False`,
`ClipToBounds=True`, opacity jako osobny token `SxAuroraOpacity` — nie literał).

Efekt szkła z backlogu (P0#6): **bez `AllowsTransparency=True` na oknie głównym**. Jeśli chcesz
`WindowChrome`/Mica/Acrylic — napisz mi koszt (perf, kompatybilność z grami pełnoekranowymi, ryzyko dla
`--ui-smoke`) i czekaj. Nakładka `OverlayWindow` zostaje przezroczysta, bo to inny przypadek (mały region,
brak interakcji).

### 5.5 Ruch

Fade 120–180 ms, ewentualnie translate 4–8 px. Bramka `SxAnimationsEnabled` (obsługuje `Utilities/PageTransitions.cs`)
+ `SystemParameters.ClientAreaAnimation`. Zakaz: animacje nieskończone, `BeginStoryboard` w code-behind,
animowanie `Width`/`Height` (layout pass), animowanie `Opacity` kontenera z `ItemsControl` (offscreen bitmap),
cokolwiek, co konkuruje z grą.

### 5.6 Ikony

Dziś UI żyje z glifów i emoji: `⌘ ▤ ▣ ⚙ 🎤 💬 📓 🕘 ✨ ⚡ 🩺 🖥 🎮 ϟ ◉ ◇ ▦ ⌁ ↻ ✧ 🎤 🔒`. To loteria
glifów i tanie wrażenie. Zrób `Themes/Icons.xaml` z `Geometry` (Path data) dla **każdego** symbolu używanego
w interfejsie i renderuj wektory. Ale: `NavItem.Icon` i `CenterTab.Icon` zostają (używa ich `MainViewModel`,
paleta, `UiSmokeTestRunner` i czytelnik ekranu). Mapowanie klucz → geometria zrób przez `DataTrigger` w
`DataTemplate` albo konwerter `SxIconGeometry` w `Converters/` (R3: nazwa z `Sx`). Nie wgrywaj fontu ikonowego
ani `Segoe Fluent Icons` jako zależności typograficznej bez rozmowy — brak glifu to wtedy pusta kratka.

### 5.7 Rezyliencja okna i DPI

Zostaw `MinWidth/MinHeight`, dodaj tryb szyny: `ActualWidth < ~1180 → sidebar 64 px` (ikona + ToolTip +
`AutomationProperties.Name`), `> ~1600 →` druga kolumna (metryki albo panel szczegółu, zależnie od strony).
Stan zwinięcia trzymaj w jednym polu prezentacyjnym w `MainViewModel` (`IsRail`) albo przez `DataTrigger`
na `ActualWidth` — tak, żeby nie złamać R5/R6. Przetestuj 100/125/150/200% DPI (manifest PerMonitorV2, nie ruszaj)
i sprawdź, że `TextTrimming` + `MaxWidth` ratują 200% bez obcinania przycisków. Persystencja rozmiaru:
patrz 2.7 — tylko rozmiar i pozycja, nie strona, jeśli nie rozwiążesz mapowania.

---

## 6. SMAK: TRZY KIERUNKI (wybierz albo każ mi zaproponować)

Bez tego dostaniesz „poprawny, generyczny dark-mode dashboard". Wybór kierunku to jedna z dwóch decyzji,
które mogą zapaść tylko u mnie.

**A — KONTROLA PULPITU** (najbliższy obecnej tożsamości). Ciemne tło `#0D0F14`, liczby jako bohaterowie
(`SxNumber`, 28–34 px), etykiety cofnięte, akcent cyan jako jedyny kolor interakcji, stan = pasek 2 px
na lewej krawędzi karty. Koszt: mało wyrazisty na pierwszy rzut; zysk: najszybszy skan i najmniejsza różnica
kontrastu w Light.

**B — LABORATORIUM / TERMINAL**. Lekko zielonkawy lub chłodny gradient tła, monospace dla wszystkich danych
maszynowych, karty bez cieni, granice 1px widoczne, surowe ramki przy sekcjach `VERIFIED/UNVERIFIED`.
Koszt: `SxMono` musi wejść w ~40 miejsc, a Light motyw wychodzi drożej (papier vs smugi).
Zysk: najbardziej „Sentinel" — bo to faktycznie narzędzie diagnostyczne.

**C — FLUENT / WINDOWS NATIVE**. Wykorzystanie tego, że `App.xaml` już scala Fluent i `ThemeMode` działa:
zaokrąglenia i odsłony zgodne z systemem, Mica zamiast aurory, `SxCard` jako subtelna warstwa.
Koszt: największe ryzyko regresji w renderze (`--ui-smoke` liczy wiązania, nie wygląd, więc złapiemy to późno)
i uzależnienie od wersji .NET 9 servicing. Zysk: najtańsze utrzymanie i najlepszy tryb jasny.

Niezależnie od wyboru obowiązują: jedna paleta akcentu (`Ui.AccentColor`, dziś sześć wartości w
`SettingsCatalog.cs:31`), max dwa kolory nasycone widoczne jednocześnie na ekranie, semantyka barw wg 2.5,
zero emoji w interfejsie (patrz 5.6), brak gradientu w tekście.

---

## 7. EKRANY

### 7.1 Shell — `Views/MainWindow.xaml`

Belka górna (nowa, dziś jej nie ma jako warstwy): `Version`, `DesktopStatus`, gotowość AI (`AiViewModel.Status`
w skrócie albo badge z `Readiness`), `Voice.State` + `VoiceBrush`, wskaźnik STOP (`IsStopped`), przycisk palety
(`OpenPaletteCommand`, podpis `Ctrl+K`), przełącznik szyny, min/max/close. Nie dubluj tego, co jest w stopce
sidebaru — usuń duplikat, nie zostawiaj dwóch.

Sidebar: 4 pozycje bez zmian. Aktywna = lewy pasek 2 px + `SxSidebarActive`. Dół: badge `Voice.State/Status`,
`ϟ EMERGENCY STOP`, `Wznów Sentinel` (`Visibility` na `IsStopped` + `BoolToVisibility`), `Wyjdź z aplikacji`.
Zostaw `KeyBinding Ctrl+K → OpenPaletteCommand` i `u:UiInteraction.LoadedCommand="{Binding InitializeCommand}"`.

Zakaz: `Frame`/`NavigationService` zamiast obecnego `ContentControl` (traciš stan VM-ów trzymany w DI
jako singletony).

### 7.2 Centrum — `Views/Pages/CommandCenterPage.xaml` (ekran 1. kategorii)

Zakładki: `ItemsSource={Binding Sections}`, `SelectedItem={Binding SelectedTab, Mode=TwoWay}` — zostaw.
Dziś: sama ikona 19 px + `ToolTip`. Cel: ikona + etykieta jednym słowem, `AutomationProperties.Name={Binding Label}`,
wyraźny stan wybrany (dziś `SxSurfaceActive` + cyan border w `CenterTabItem` — to zostaje), nawigacja strzałkami.
Jeśli zostawiasz `Focusable="False"` na `ListBox`, zapewnij strzałki przez `KeyBinding` na poziomie strony
i opisz to w raporcie.

Nagłówek: `SxHeading` „Centrum", `ConversationTitle`, `Tryb prywatny` (`TogglePrivateModeCommand`),
badge `VOICE · {State}` + `ToggleVoiceCommand` z tekstem sterowanym `VoiceActive`, badge `🔒 ROZMOWA PRYWATNA`
→ po 5.6: wektor + napis bez emoji.

Strumień rozmowy: `ItemsControl Messages`, dymek `MaxWidth=740`, `u:ChatScroll.Follow="True"`, blok `ActionRecord`
(`Status` przez `StatusBrush`, `ActionId`, `Phase`, `ElapsedMilliseconds`, `StorageWarning`, `Evidence`),
blok `IsStreaming`/`StreamingText`. Do dodania: separator daty (grupa po dniu, „Wczoraj"), akcje na dymku po
najechaniu (`Kopiuj` — jeśli nie ma komendy w VM, użyj `ApplicationCommands.Copy` na `TextBox`, nie wymyślaj
`CopyMessageCommand`), wskaźnik pracy z `CurrentAction.Phase` + `IsBusy` (bez procentów), oznaczenie
`IsStopped` w obrębie strony (dziś tylko w sidebarze).

Pasek `//`: `SlashOpen`, `SlashItems`, `SlashIndex`, `u:SlashKeys.Enabled="True"` — zachowaj `Display`,
`Entry.Label`, `Entry.Hint` i komunikat „Brak pasującego polecenia…". Nie ruszaj sterowania klawiszami.

Zgoda: `HasPermission`/`PermissionSummary` + `ApproveCommand`/`CancelCommand`, obwódka wg ryzyka przez `RiskBrush`,
jednoznaczne „ta zgoda jest jednorazowa i wygasa po 10 minutach" (reguła z `docs/MEMORY.md`) oraz
`Esc` = `CancelCommand`.

Stopka wejścia: `UserInput` + `u:InputHint.Text` + `u:UiInteraction.FocusVersion`, `Enter` → `SendMessageCommand`,
`Ponów` (`RetryCommand`), `Zatrzymaj generowanie` (`StopGenerationCommand`, `IsEnabled` na `IsBusy`),
`Wyślij` (`SxPrimaryButton`), `Status` pod polem. Zostaw trzy szybkie przyciski (`QuickCommandCommand`).

### 7.3 Strony — wspólny szkielet

Każda strona: nagłówek (`SxHeading`) + jeden wers wyjaśniający (bez marketingu), stany (sekcja 8), wirtualizacja
długich list, treść techniczna zaznaczalna (`TextBox IsReadOnly` + `IsReadOnlyCaretVisible=True`), przycisk
`Kopiuj` tam gdzie wynik idzie dalej, brak `x:Name` z logiką.

- **System** — `Snapshot.CpuText/RamText/GpuText/VramText/Network/Disks/Processes`. Kafle + paski `Mode=OneWay`.
  Vram = „Niedostępne" dosłownie. Do dodania: `SystemSnapshot.Timestamp` — pole istnieje, ale NIE jest zbindowane nigdzie w `Views/` (sprawdź:
  `grep -rn Timestamp Views/`), więc użytkownik nie wie, kiedy był odczyt. `DataGrid` na tej stronie
  (`SystemPage.xaml:67`) ma `Height="360"` na sztywno i **wbudowane sortowanie** (`CanUserSortColumns` domyślnie
  true) — nie dodawaj własnego sortowania przez `ICollectionView`, za to zrób wysokość elastyczną i dodaj
  `AutomationProperties.Name` oraz `DataGridRow` do stylów, jeśli go brak w `Themes/Controls.xaml`. Brak pomiaru nie może wyglądać jak 0.
- **Diagnostics** — `Items`, `First/SecondSnapshot`, `Label`, `Comparison`, `HasComparison`, `CanCompare`.
  `CompareCommand` jest już sterowany `CanCompare` (`DiagnosticsPage.xaml:27`) — nie ruszaj. Do poprawy:
  porównanie jako dwie kolumny + różnica linia po linii (format, jaki zwraca
  serwis — nie przepisuj logiki), `ExportCommand` z widocznym wynikiem i hashem, `DeleteCommand` z potwierdzeniem,
  licznik `Items.Count` / 20.
- **Memory** — `Items`, `FilterOptions`, `CategoryOptions`, `CategoryFilter`, `PinnedOnly`, `Search`,
  `PrivacyLine`, `ConflictHint`, edytor (`NewText`/`NewCategory`/`EditorText`), rozmowy (`Conversations`,
  `SelectedConversation`, `RenameTitle`, `NewConversationCommand`, `ResumeConversationCommand`),
  import/eksport (`ImportPath`, `PreviewImportCommand`, `RunImportCommand`, `ExportMemoryCommand`).
  Wyróżnij `PinnedOnly`, `Superseded` („nieaktualne"), przypnij/odpiń i „oznacz nieaktualne" jako
  `TogglePinCommand`/`ToggleStaleCommand`, usuwanie wyłącznie przez `RequestDeleteCommand` (czyli przez centrum
  zgód HIGH). Jedno zdanie uczciwości: wyszukiwanie treści jest dosłowne, bez fuzzy.
- **Projects** — `Cards`, `ActiveLine`, `ShowArchived`, edytor `EditorName/EditorDescription`,
  `SummaryText`, komendy `Create/SaveSelected/ToggleStatus/ToggleActive/ToggleArchive/NewConversation/
  AssignActiveConversation/Export/WhereDidWeStop`. Aktywny projekt = znacznik, nie sam kolor.
  Izolację kontekstu wyjaśnij jednym zdaniem przy nagłówku (to najważniejsza niezrozumiała rzecz w produkcie).
- **Tasks** — `Items`, `Reminders`, `FilterOptions`, `PriorityOptions`, `Banner`, edytory z `*Preview`
  (parser dat PL zwraca czytelną datę **przed** zapisem — ten podgląd jest już w VM i musi zostać widoczny),
  `CycleStatusCommand` z `StatusCycleLabel`, usuwanie tylko przyciskiem. `Missed`/`Awaiting` wyróżnione
  (przegapione vs czeka na zgodę).
- **History** — `Entries`/`FilteredEntries`, `StatusOptions` (8 wartości, pokaż wszystkie w `ComboBox`),
  `Search`, `SelectedEntry` (`ActionId`, `Evidence`, `RequestId`), `RefreshCommand`, `ExportCommand("json"/"csv")`
  + `ExportSummary` z SHA-256. Dym: to nie jest log systemowy, to audyt akcji.
- **Voice** — `Microphones` + `RefreshMicrophonesCommand`, `State`+`Status`, `Metrics.Raw` i `Metrics.Enhanced`
  jako dwa paski (R2), `Metrics.Noise/Snr/Gain`, `Metrics.SpeechDetected`, `Metrics.Transcript`,
  `CalibrateCommand`, `StartCommand`/`StopCommand`, `DownloadModelsCommand` + **`DownloadModelsCancelCommand`**
  (istnieje przez `IncludeCancelCommand`, dziś nie podpięty — to realna dziura: pobierania ~488 MB nie da się
  przerwać). Jeden miernik RAW, drugi PROCESSED z podpisami; `State` pokaż jako badge na tej stronie, nie tylko
  w sidebarze.
- **AI** — `Models`, `SelectedModel`, `Status`, `RefreshCommand`/`RefreshCancelCommand`,
  `SelectModelCommand` (+ `SelectModelCancelCommand`), link do `Settings → AI` zamiast dublowania suwaków.
  Podgląd kontekstu (etykiety i powody, bez pełnych promptów) — dane z Pamięci, nie wymyślaj.
- **Gaming** — `System.Snapshot.GamingText/CpuText/RamText/GpuText/VramText`, `Report`, `ToggleOverlayCommand`,
  `DiagnoseCommand`, `CancelCommand`, `Status`. Ustawienia nakładki **nie** tutaj — to `Settings → Wygląd`
  (nie dubluj, daj przycisk-przejście). Brak FPS/latencji: jeden wers „Sentinel nie mierzy FPS ani latencji".
- **Actions** — `Tasks` (`ActionRecord`: `Status`, `ActionId`, `UserRequest`, `Phase`, `ElapsedMilliseconds`,
  `Evidence`, `Error`, `StorageWarning`), `ApproveCommand`/`CancelCommand`/`HasPermission`/`PermissionSummary`,
  `Status`. Statusy słowem + barwą wg 2.5.
- **Settings** — `FilteredFields` z grupowaniem po `Section`, `Sections`, `SelectedSection`, `ResetSectionCommand`,
  `Search`, `Status`, `Error` w linii pola. Grupowe nagłówki przyklejone. Bez edycji plików: zapis idzie przez
  `SaveCommand` istniejącego rekordu. Wypisz mi pozycje z 2.6, których nadal nie da się ustawić.

### 7.4 Nakładki i overlay

- `Views/Controls/CommandPalette.xaml`: `Query`, `SelectedEntry`, `HasNoResults`, `NextCommand`, `PreviousCommand`,
  `ChooseCommand`, `CloseCommand`, `x:Name="SearchInput"` nietknięte. Grupowanie **dwoma** `Kind`: STRONA,
  POLECENIE. Podświetlenie dopasowania tylko przez `Run` z tokenem (R4). `Esc` zamyka, fokus wraca tam, skąd
  przyszedł (asercja R9/R10). `ScrollIntoView` wybranego wpisu — attached behavior w `Utilities/`, nie code-behind.
- `Views/Controls/ReadinessPanel.xaml`: 4 `Checks`, `StateLabel`+`ReadinessBrush`, `NextStep` przez
  `OpenSectionCommand(PageKey)`, `Summary`, `LastChecked`, `RefreshCommand`/`RefreshCancelCommand`,
  `CloseCommand` na `Esc` i klik w `SxScrim`, pasek `SxDangerButton` z `EmergencyStopCommand` z
  `DataContext.EmergencyStopCommand, RelativeSource AncestorType=Window` (wzorzec już w pliku — nie wymyślaj
  nowego sposobu dowiązania do shella).
- `Views/Overlay/OverlayWindow.xaml` + `OverlayViewModel.System` tylko. Zostaw `WindowStyle=None`,
  `AllowsTransparency=True`, `Topmost`, `ShowInTaskbar=False`, `ShowActivated=False`, `SizeToContent`,
  `ResizeMode=NoResize`, `MouseLeftButtonDown="DragOverlay"` (code-behind ma <20 linii — R5 obowiązuje też tu).
  Skala/opacity/pozycja przychodzą z `DesktopService.ApplyOverlay()`; nie dubluj sterowania w oknie głównym,
  jeśli nie ma na to stanu w VM.

---

## 8. MACIERZ STANÓW (to jest różnica między narzędziem a makietą)

Każdy ekran musi mieć rozwiązane te pozycje — jeśli dane nie istnieją, wiersz znika zamiast być ozdobiony:

| Stan | Źródło prawdy | Wymagane UI |
|---|---|---|
| pusty | `Messages.Count==0`, `Items.Count==0`, `Checks.Count==0` | jedno zdanie + jedna akcja, bez ilustracji |
| w trakcie | `IsBusy`, `IsStreaming`, `CurrentAction.Phase`, `Readiness.RefreshCommand.IsRunning` | tekst fazy, brak `%`, `IsEnabled` na przyciskach, które przeszkadzają |
| gotowy | `ReadinessState.Ready`, `AiViewModel.Status`, `Voice.State` | badge + co dalej |
| wymaga konfiguracji | `ReadinessState.NeedsSetup` | przycisk `OpenSectionCommand`, nie sam opis |
| niedostępne | `ReadinessState.Unavailable`, `SystemSnapshot.*Text == "Niedostępne"`, `VramText` | słowo „Niedostępne" + powód, nie zero, nie „—" bez kontekstu |
| zatrzymane | `IsStopped` | pasmo przez całą szerokość + `ResumeCommand` w zasięgu klawisza |
| zgoda | `HasPermission`, `PermissionSummary` | karta z ryzykiem wg `RiskBrush`, czas wygaśnięcia, `Esc`=odmowa |
| prywatność | `IsPrivateMode`, `MemoryViewModel.PrivacyLine` | badge na stałe w nagłówku, bez migania |
| błąd | `ActionRecord.Error`, `SettingViewModel.Error`, `AppLog` | tekst błędu w miejscu, które go wywołało + co zrobić |
| brak danych historycznych | `HistoryViewModel.Status`, `Entries.Count==0` | nie „Brak danych", a „Brak zapisanych akcji w tej sesji · historia zaczyna się od pierwszego polecenia" |

Zakazane: ładowanie dla operacji trwającej 5 ms, zielony dla `UNVERIFIED`, zero dla braku licznika, animacja
bez stanu docelowego, „sukces" bez `Verified`, pasek postępu bez licznika, jakikolwiek przycisk bez `Command`.

---

## 9. DOSTĘPNOŚĆ I JĘZYK

- `AutomationProperties.Name` po polsku na każdej interaktywnej kontrolce i na każdej `DataTemplate`-owej pozycji
  listy (wzorzec: `SxSidebarItem`, `CenterTab` z `AutomationProperties.Name="{Binding Label}"`).
- Kontrast: ≥4,5:1 tekst zwykły, ≥3:1 duży i nietekstowy, w trzech motywach. Poprawiaj `*Color` w słowniku
  motywu, nigdy w widoku.
- Stan: barwa + słowo (+ kształt/ikona). Nigdy sama barwa, nigdy sam symbol.
- Fokus: widoczny pierścień 2 px, kolejność TAB zgodna z wzrokową, brak pułapek (np. `ListBox` w środku
  `ScrollViewer` — ustaw `KeyboardNavigation.TabNavigation` świadomie).
- Klawisze: `Ctrl+K` paleta, `Ctrl+Shift+X` STOP, `Ctrl+Shift+S` pokaż okno, `Esc` (paleta, panel gotowości,
  zgoda = odmowa, `//` = zamknij), `Tab`/`Shift+Tab` w `//`, `Enter` wyślij/wybierz, strzałki w palecie i w
  zakładkach Centrum, `Alt+1..4` dla nawigacji (opcjonalnie, przez `KeyBinding`), `Ctrl+Shift+C` kopiowanie
  ostatniej odpowiedzi (tylko jeśli zbudujesz to na `ApplicationCommands`, nie na wymyślonej komendzie VM).
- Skróty widoczne tam, gdzie są: `ToolTip` + podpis przycisku, plus lista skrótów w `Settings → Ogólne`
  albo w panelu gotowości (bez nowego pliku pomocy).
- Polski: `pl-PL`, przecinek dziesiętny (`StringFormat=F1` itd.), spacja przed jednostką, `HH:mm`,
  `yyyy-MM-dd`, bez anglicyzmów w tekstach („Motyw", nie „Theme"), bez tłumaczenia nazw własnych
  (CS2, Discord, Ollama, Steam, SHA-256, VAD, ASR, RMS, SNR).
- Ton: rzeczowy, krótki, techniczny. „Zrobione: X. Potwierdzenie: Y. Niepotwierdzone: Z." Nie „Świetnie!",
  nie „Ups", nie „coś poszło nie tak".

---

## 10. WYDAJNOŚĆ (budżet i uzasadnienia)

1. Lista `Messages` zostaje `ItemsControl` + `StackPanel`, jeśli nie ma powodu do wirtualizacji — ale wtedy
   udokumentuj koszt przy 720 wpisach (limit `docs/MEMORY.md`). Jeśli wirtualizujesz, użyj
   `VirtualizingStackPanel.IsVirtualizing=True` + `ScrollViewer.CanContentScroll=True` i zaakceptuj skok
   przewijania — napisz, że akceptujesz.
2. Żadnego timera w widoku. Odświeżanie: `SystemMonitor.Updated` → `SystemViewModel.Snapshot`,
   `IUiDispatcher.Post`. `DispatcherTimer` w code-behind = złamanie R5/R6.
3. Zero efektów bitowych w pętlach renderowania (5.4). Gradienty statyczne.
4. `Mode=OneWay` dla wszystkich odczytów; `UpdateSourceTrigger=PropertyChanged` tylko tam, gdzie reagujemy
   na znak (szukaj, pola tekstowe ustawień).
5. Nie mnoż konwerterów: 4 istniejące wystarczają. Nowy tylko z `Sx`-ową nazwą i tylko rejestracja w `App.xaml`.
6. Wiązania w `DataTemplate` przez `RelativeSource AncestorType` zamiast `ElementName` (to drugie nie widzi nazw
   z drugiego nonce-contextu — wzorzec `ReadinessPanel.xaml`).
7. `TextBlock` z `TextWrapping=Wrap` w kolumnie `Width="*"` musi mieć `MaxWidth` albo kolumna mu się rozjedzie.
8. Pomiary: `RenderTargetBitmap` w teście kosztuje — nie dodawaj go w pętli „dla każdego elementu".
9. Cel: <1 s od `Show()` do pierwszej ramki przy włączonym `--ui-smoke`, płynne przewijanie listy 720 wpisów,
   brak wzrostu zużycia RAM przy 30 minutach otwartego okna. Zmierz i napisz jak; bez pomiaru napisz
   „nie zmierzone", nie zmyślaj.

---

## 11. UCZCIWOŚĆ UI (reguła produktowa)

- `ActionExecutionResult`: `VerifiedSuccess` (niezależny odczyt zwrotny), `UnverifiedSuccess` (wysłane,
  niepotwierdzone), `Failure`. Historia pokazuje `VERIFIED` tylko dla pierwszego. Widok nie ma prawa
  „domykać" tego kolorem ani słowem „gotowe".
- Dowody (`ActionRecord.Evidence`, `ActionId`, `RequestId`) muszą być widoczne i kopiowalne, nie pod „więcej".
- Zgody: jednorazowe, z wygaśnięciem (10 min wg `docs/MEMORY.md`), zatwierdzane klawiaturą; głos nigdy;
  STOP kasuje oczekiwanie. To UI musi mówi jawnie, nie implikować.
- Operacje na plikach: `duplikaty`, `porządek`, `usuń do kosza`, `zmien nazwy` działają przez podgląd + `potwierdz`.
  W UI podgląd musi być przed przyciskiem zatwierdzenia, nie obok.
- Brakujące funkcje to nie wstyd — są opisane w `docs/BACKLOG.md`. Zmyślona funkcja to wstyd. Jeśli
  nie ma pola w VM, nie ma wiersza w UI ani w tabeli 14.5.
- Modele 3D, samomodyfikacja kodu, waluty bez kursu — to celowe odmowy produktu. Nie zostawiaj po nich
  pustych zakładek ani „wkrótce".

---

## 12. TESTY WIDOKU

W `UiSmokeTestRunner.cs` **dopisuj, nie zdejmuj** (zero usuniętych asercji; każda usunięta = cofnij).

Nowe asercje, które chcę zobaczyć:

1. render shella przy `Width` 1100 i 1800 (oraz przy przełączonym `IsRail`, jeśli je dodasz) nie rzuca,
   a belka górna ma niepuste `DesktopStatus` po `InitializeCommand`;
2. dla każdej `Sections`: `CenterTab.Label` niepuste, a odpowiedni element ma niepusty
   `AutomationProperties.Name` (koniec z ikonami bez podpisu, nieczytelnymi dla czytnika ekranu);
3. po `store.Settings.Ui.Theme = "Light"` (i `Save()`) żaden z oczekiwanych tokenów `Sx*` nie jest
   niezdefiniowany: `Application.Current.TryFindResource(key) != null` dla listy, którą sam wyliczysz
   z `Themes/Colors.xaml` — bez `File.*` (to test, nie ViewModel; R6 dotyczy `ViewModels/*.cs`);
4. `VoiceViewModel.DownloadModelsCancelCommand` jest podpięte i `CanExecute` true tylko podczas pobierania;
5. każda strona ma nagłówek w języku polskim — asercja na liście tytułów (dziś 6 stron ma `Text="Actions"`,
   `"Gaming"`, `"History"`, `"Settings"`, `"System"`, `"Voice"`), plus asercja, że `SxHeading` nie jest użyty
   bez tekstu;
6. `EmergencyStop` → `IsStopped` → pasmo widoczne w `CommandCenterPage` (nie tylko w sidebarze);
7. `--ui-smoke` nadal zapisuje PNG każdej strony i każdego tabu do katalogu wyniku; po zmianach liczba
   plików nie maleje;
8. po zmianach geometrii: `WindowWidth` z `UiSettings` jest stosowane i zapisywane, a legacy nadal
   dostaje poprawną `SelectedPage` (albo — jeśli wybrałeś wariant (a) z 2.7 — assert, że shell nie zapisuje
   tego pola wcale).

Zachowaj neutralność testu: bez mikrofonu, bez instalacji modeli, bez autostartu, bez rejestru (R14).
Wynik CI wrzucaj do raportu 14.6 skopiowanym logiem, nie własną parafrazą.

---

## 13. FAZY I BRAMKI DECYZYJNE

Jeden commit na fazę. `python3 scripts/check-architecture.py` przed każdym commitem. CI po każdej fazie.
Małe kroki — kompilacji i tak nie uruchomisz lokalnie.

| Faza | Zakres | Bramka stop |
|---|---|---|
| A | audyt (patrz 13.1) | **STOP** — akceptujesz kierunek (sekcja 6) i plan |
| B | `Themes/` (tokeny, Light/DeepDark gaps, `SxMono`/`SxNumber`, ikony, `SxAuroraLayer`) | — |
| C | shell: belka górna, szyna, persystencja rozmiaru, skróty | — |
| D | Centrum: zakładki z etykietami, dymki, separatory dni, karta zgody + wygaśnięcie, wskaźnik pracy, `IsStopped` w treści | **STOP** — oceniasz ekran 1. kategorii |
| E | strony w kolejności: System, Diagnostics, Tasks, Memory, History, Projects, Voice, AI, Gaming, Actions, Settings | — |
| F | paleta, panel gotowości, overlay | — |
| G | testy (sekcja 12) | — |
| H | docs: sekcja GUI w `README.md`, `docs/RELEASE-<kolejny>.md`, aktualizacja `docs/BACKLOG.md` (P0#3, P0#6), `docs/ARCHITECTURE.md` jeśli doszły pliki | — |
| I | okruszki: mikrotypografia, skróty w `ToolTip`, trzy przebiegi motywów | — |

### 13.1 Co ma zawierać faza A (bez kodu)

1. tabelę: każdy `Views/**/*.xaml` → użyte klucze → `niezdefiniowany` / `definiowany-nieużyty`;
2. listę realnych dziur, które potwierdziłem w kodzie (zweryfikuj każda własnym grepem i podaj numery linii;
   jeśli którejś nie ma — napisz, że jej nie ma):
   (a) `SystemSnapshot.Timestamp` niezbindowany nigdzie,
   (b) `DownloadModelsCancelCommand` i `StartCancelCommand` istnieją (IncludeCancelCommand) i nie są podpięte,
   (c) `Voice.State` bez wskaźnika na `VoicePage`,
   (d) `Ui.MinimizeToTray` używany w `DesktopService.cs:88`, brak wpisu w `SettingsCatalog`,
   (e) `Themes/LightTheme.xaml` (15 linii) i `DeepDarkTheme.xaml` (6) nadpisują tylko podzbiór tokenów,
   (f) 6 stron ma angielskie tytuły: `Actions, Gaming, History, Settings, System, Voice` (np. `HistoryPage.xaml:4`)
       przy polskich etykietach nawigacji,
   (g) `DataGrid` w `SystemPage.xaml:67` i `HistoryPage.xaml:20` ma `Height` na sztywno (360 i 280) i siedzi
       wewnątrz `ScrollViewer` (podwójne przewijanie);
3. listę rzeczy, które **nie są** do zrobienia, bo nie ma danych: wybór metryki w overlay (legacy-only),
   trzecia grupa w palecie, FPS/latencja, VRAM, waluty;
4. trzy warianty wizualne z sekcji 6, każdy w 3 zdaniach + koszt w tokenach i w liczbie plików;
5. maks. 7 pytań, każde z rekomendacją;
6. plan: kolejność plików, rozmiar zmian, ryzyko na fazę, co może połknąć `--ui-smoke`.

Pytania, które zadajesz **zawsze**, jeśli nie ma odpowiedzi w sekcji 0: kierunek wizualny, czy ruszamy
`SettingsCatalog` (dla `MinimizeToTray`), czy ruszamy `UiSettings` (nowe pole zamiast mapowania `SelectedPage`),
czy zgadzasz się na wektory ikon zamiast emoji (to zmiana widoczna dla użytkownika).

---

## 14. FORMAT ODPOWIEDZI (każdy etap)

1. **Pytania / Potrzebuję decyzji** — najpierw; jeśli brak, napisz „Brak pytań; zakładam: …".
2. **Zweryfikowany kontekst** — które pliki przeczytałeś + 3 fakty, które zmieniły plan (z numerami linii).
3. **Plan fazy** — z jednym zdaniem ryzyka.
4. **Diff plik po pliku** — przy każdym „po co ten plik". Bez zmian spoza zakresu z sekcji 4.
5. **Tabela: stan → pole źródłowe → widget** — dla każdej nowej rzeczy. Nazwa pola, której nie ma w kodzie,
   eliminuje wiersz.
6. **Bramki** — dokładny output `python3 scripts/check-architecture.py`, lista asercji dopisanych/usuniętych
   (usunięte: pusta), status CI (cytat, nie parafraza), czego nie sprawdziłeś i dlaczego.
7. **Świadomie nie zrobione** — z konsekwencją i tym, co byłoby potrzebne.
8. **Ręczna weryfikacja po Twojej stronie** — mikrofon, Ollama z żywym modelem, gra + overlay, DPI i wiele
   ekranów, tray i globalny hotkey, druga instancja, autostart po relogu.
9. **Szczerość:** nie zaczynaj od pochwał, nie streszczaj sekcji 2 bez nowej informacji, nie przepraszaj,
   przy sprzeczności w tym prompcie z kodem — napisz PRZED kodem, z cytatem linii; nie wiesz — napisz
   „nie wiem" i nazwij, co sprawdziłbyś, żeby się dowiedzieć. Zgadywanie w tym repo kosztuje cykl CI
   i godzinę Twojego życia.

---

## 15. DEFINICJA UKOŃCZENIA

- [ ] `python3 scripts/check-architecture.py` → `PASS` (R1–R15).
- [ ] CI zielone: build, `--ui-smoke`, regresje, portable, instalator; limit artefaktów odnotowany jako nieskładkowy.
- [ ] Zero literałów hex i rozmiarów czcionki w `Views/**`; każdy nowy zasób ma `Sx` i definicję w `Themes/`/`App.xaml`.
- [ ] Dark/Deep Dark/Light × 12 stron × 9 zakładek + paleta + gotowość + overlay: bez błędu wiązania, bez kontrastu <4,5:1.
- [ ] 1100×700 / 1400×900 / 2560×1440 i DPI 100/125/150/200 bez obcięcia; rozmiar zapamiętany; `SelectedPage`
      rozwiązane zgodnie z 2.7 (nie po cichu).
- [ ] K1–K6 z sekcji 1 zmierzone i udokumentowane (TAK/NIE przy każdym).
- [ ] Klawiatura: paleta z powrotem fokusa, `//`, zakładki Centrum, `Esc`, `Enter`, `Ctrl+Shift+X`.
- [ ] Macierz stanów z sekcji 8 zrealizowana dla każdej strony albo udokumentowana jako „brak danych".
- [ ] Code-behind każdego widoku <20 linii; zero `Click=` w shellu; nowe zachowania w `Utilities/`.
- [ ] Legacy UI działa i nie zmieniło ani jednego znaku.
- [ ] `UiSmokeTestRunner.cs`: zero usuniętych asercji, ≥8 nowych, wszystkie przechodzą w CI.
- [ ] `README.md`, `docs/RELEASE-*.md`, `docs/BACKLOG.md` zaktualizowane; brak wpisów o nieistniejących funkcjach.
- [ ] Raport w formacie 14, w tym „świadomie nie zrobione".

---

## 16. ANTYWZORCE (każdy wygląda na „progres", każdy jest odrzucany)

1. Nowy NuGet do GUI (WPF-UI, ModernWpf, MaterialDesign, MahApps, HandyControl, SkiaSharp): rośnie
   instalator, znika offline-build, a masz Fluent z .NET 9 i własny zestaw stylów.
2. Logika w code-behind, `Click=`, `x:Name` + manipulacja z C#, eventy zamiast komend.
3. WinForms, `NotifyIcon` z WinForms, `UseWindowsForms=true`, własne P/Invoke tray.
4. `AllowsTransparency=True` lub `WindowChrome` na oknie głównym bez akceptacji kosztu.
5. Hexy, magiczne rozmiary, style bez `Sx`, nowe klucze w `App.xaml` zamiast w `Themes/`.
6. „Przy okazji" poprawione: wersja w `AppConstants`, tytuł legacy, `app.manifest`, `SettingsCatalog.Label`,
   kolejność `IntentRouter`, format JSON, `.editorconfig`-owe spacje w serwisach. Zgłoś zamiast naprawiaj.
7. Wymyślone pola VM, puste `ICommand`, dwukierunkowe bindowanie odczytów, trzecia grupa w palecie,
   przełącznik metryki w overlay, procenty postępu, „AI myśli".
8. Usunięcie strony albo scalenie jej z inną (R8).
9. Zmiana testu zamiast zmiany kodu, w tym „tymczasowe" ominięcie asercji.
10. Opublikowanie EXE, tag, release, zmiana `release.yml`.
11. Angielski w UI, lorem ipsum, „coming soon", ikony emoji zostawione „bo działa".
12. Samochwala w raporcie: „zaprojektowałem", „zoptymalizowałem" bez liczb i bez bramki.

---

## ZAŁĄCZNIK A — komendy samoweryfikacji (uruchom, zanim powiesz cokolwiek o kodzie)

```
python3 scripts/check-architecture.py
grep -rnE '="#[0-9a-fA-F]{6,8}"' Views/                       # musi byc puste (R4)
grep -rnoE 'x:Key="[^"]+"' Themes/*.xaml App.xaml | sort -u   # co istnieje
grep -rnoE '\{(Static|Dynamic)Resource [^}]+\}' Views/ | sort -u | grep -v 'Sx\|BoolToVisibility\|Brush}'   # co odwolane poza kontraktem
grep -nE '\[ObservableProperty\]|\[RelayCommand|public .*\{ get' ViewModels/<VM>.cs   # surface strony
```

Wypisz powierzchnię ViewModelu (nazwy pól i komend) przed napisaniem `<Binding>` — nie po. Nazwy generowane
z atrybutów: pole `foo` → `Foo`, komenda `BarAsync` → `BarCommand`, a
`[RelayCommand(IncludeCancelCommand = true)]` generuje dodatkowo `BarCancelCommand`.

## ZAŁĄCZNIK B — wzorce do naśladowania (bez hexów, bez code-behind)

Token motywu (`Themes/Colors.xaml`):

    <Color x:Key="SxFocusRingColor">#48D8FF</Color>
    <SolidColorBrush x:Key="SxFocusRing" Color="{DynamicResource SxFocusRingColor}"/>

Styl z triggerem stanu (R1, R3, R4):

    <Style x:Key="SxCardRaised" TargetType="Border">
      <Setter Property="Background" Value="{DynamicResource SxSurfaceRaised}"/>
      <Setter Property="BorderBrush" Value="{DynamicResource SxBorderSubtle}"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="CornerRadius" Value="16"/>
      <Setter Property="Padding" Value="16"/>
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsStopped}" Value="True">
          <Setter Property="BorderBrush" Value="{DynamicResource SxError}"/>
        </DataTrigger>
      </Style.Triggers>
    </Style>

Nagłówek strony (spójny dla 12 stron, jeden `StackPanel`, bez kopii stylu w pliku strony):

    <StackPanel Margin="0,0,0,16">
      <TextBlock Text="Nazwa" Style="{StaticResource SxHeading}"/>
      <TextBlock Text="Co tu robisz, jednym zdaniem, bez marketingu" Style="{StaticResource SxCaption}"/>
    </StackPanel>

Miernik odczytu (R2):

    <ProgressBar Value="{Binding Metrics.Raw, Mode=OneWay}" Style="{StaticResource SxProgressBar}" Maximum="100"/>

Warstwa dekoracyjna bez przechwytywania myszy:

    <Grid IsHitTestVisible="False" ClipToBounds="True" Style="{StaticResource SxAuroraLayer}"/>

## ZAŁĄCZNIK C — czego dotknąć, jeśli coś wygląda jak bug

- błąd renderu lub wiązania → `PresentationTraceSources` zbierany w `UiSmokeTestRunner`, log w `Logs/errors.log`
  (`Utilities/CrashLogger`), `--ui-smoke <katalog>` daje PNG i `ui-smoke.txt`;
- „nie mogę zabindować" → patrz 2.4 i Załącznik A (pole nie istnieje = usuń wiersz z projektu, nie dodawaj pola);
- „muszę ruszyć serwis" → sekcja 4, pytaj;
- „test przeszkadza" → sekcja 12 i R11: test opisuje umowę produktu;
- „UI wygląda źle w jasnym motywie" → sekcja 5.1, nie `#FFFFFF` w widoku.

## ZAŁĄCZNIK D — szkielet raportu fazy (wklej i wypełnij)

    FAZA: <A..I>
    KIERUNEK: <A|B|C> (akceptowany: tak/nie)
    PLIKI: <ścieżka — po co>
    STAN → ŹRÓDŁO → WIDGET: <tabela>
    BRAMKI: check-architecture <PASS|output>, CI <run link + cytat>, asercje +<n>/-<n>
    NIE ZROBIONE: <lista + konsekwencja + czego wymaga>
    DO RĘCZNEGO SPRAWDZENIA: <lista>

---

**ZACZNIJ OD FAZY A. Nie pisz kodu, dopóki nie zaakceptuję planu, ani dopóki nie znajdziesz sprzeczności
między tym prompcem a kodem — wtedy sprzeczność jest pierwsza, z cytatem linii.**
