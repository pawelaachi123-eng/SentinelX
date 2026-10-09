# Sentinel X · powłoka WinUI 3 (Windows App SDK)

Nowoczesne centrum sterowania: centralny animowany **Sentinel Core**, pasek komend,
dynamiczne panele (CPU / GPU / RAM / NET / AI / WATCH), wysuwane Ustawienia,
paleta komend (Ctrl+K), Mini Mode, zasobnik i toasty. Zero zakładek.

> Status: powłoka WinUI powstaje **obok** klasycznej aplikacji WPF i korzysta
> z tego samego backendu (te same źródła, jedna logika). Dopóki migracja nie
> zostanie zweryfikowana na Windows, projekt WPF (`SENTINEL-X.csproj`) pozostaje
> głównym wydaniem.

## Architektura

```
┌─────────────────────────────────────────────────────────────┐
│ SentinelX.WinUI (WinUI 3, net8.0-windows, unpackaged)       │
│                                                             │
│  UI/                  XAML + code-behind (tylko widok)       │
│  ├─ ShellWindow       Mica, titlebar, Core, komendy, czipy  │
│  ├─ Controls/         SentinelCore, TelemetryChart, Pill    │
│  ├─ Panels/           DetailPanel, SettingsPanel, History   │
│  ├─ Dialogs/          CommandPalette (Ctrl+K)               │
│  ├─ MiniModeWindow    kompakt + Always On Top               │
│  └─ GamingOverlayWindow  pasek metryk w grach               │
│                                                             │
│  ViewModels/          MVVM (CommunityToolkit.Mvvm)          │
│  ├─ ShellViewModel    agregat: pigułki, komendy, panele     │
│  ├─ CommandBarViewModel  wpisywanie, podpowiedzi, historia │
│  ├─ TelemetryViewModel   snapshot + bufory wykresów        │
│  └─ Detail/Settings/History/Mini                            │
│  + współdzielone VM backendu: System, Voice, Ai, Gaming,    │
│    CommandPalette, Overlay (te same pliki co WPF)           │
│                                                             │
│  Core/                serce niezależne od widoku             │
│  ├─ CoreStateService  CENTRALNY STAN (9 stanów, patrz niżej)│
│  ├─ CommandExecutor   jedno miejsce wykonywania komend      │
│  └─ GamingPolicyService  automatyczny Gaming Mode           │
│                                                             │
│  Services/            dostęp do Windows                      │
│  ├─ Desktop  okna, tray-hide, autostart, motyw, Watch       │
│  ├─ Tray     natywna ikona (P/Invoke, bez zależności)       │
│  ├─ Hotkeys  Ctrl+Shift+S / Ctrl+Shift+X (globalne)         │
│  ├─ Toasts   AppNotifications (alerty, przypomnienia)       │
│  ├─ Speech   synteza Windows.Media (polski głos)            │
│  └─ Link     parowanie telefonu + panel z QR                │
└──────────────────────────┬──────────────────────────────────┘
                           │ linkowanie źródeł (Compile Include)
┌──────────────────────────▼──────────────────────────────────┐
│ Backend (wspólny z WPF — Services, Brain, Models, Tools,    │
│ root services: voice, engine, pamięć, zadania, Watch…)      │
└─────────────────────────────────────────────────────────────┘
```

Zasady:

- **UI nie zawiera logiki.** Code-behind robi tylko: focus, klawisze, animacje
  wejścia, `Translation` dla cieni. Decyzje należą do ViewModels i Core.
- **Jeden silnik komend.** `CommandExecutor` obsługuje pasek komend i Mini Mode;
  wynik trafia do `CoreStateService.ReportResult`, odpowiedź do Historii.
- **Stan centralny.** `CoreStateService` wylicza stan bazowy z prawdziwego stanu
  silnika / głosu / AI i nakłada krótkie nakładki (rozpoznawanie, wyniki,
  alerty Watch). UI tylko go obserwuje.

## Sentinel Core — 9 stanów

| Stan | Co go ustawia | Animacja |
|---|---|---|
| `Idle` | brak pracy, mikrofon wyłączony | wolne orbity, spokojny oddech |
| `Listening` | mikrofon czuwa / słucha | żywsze segmenty, kropka reaguje na poziom mic |
| `Recognizing` | wykryta mowa (VAD/ASR) | szybki puls kropki, szybszy pierścień |
| `Thinking` | model generuje odpowiedź (stream) | przyspieszone pierścienie |
| `Executing` | silnik wykonuje zadanie | łuk aktywnego zadania + nazwa fazy pod Rdzeniem |
| `Success` | zadanie zakończone (krótko) | miękki rozbłysk, powrót do stanu bazowego |
| `Warning` | alert Watch / wynik bez weryfikacji | spowolnienie, bursztyn — bez migania |
| `Error` | błąd zadania (krótko) | zatrzymane orbity, głęboki oddech, przygaszona czerwień |
| `Offline` | STOP awaryjny | bezruch, szarość, przygaszenie całości |

Animacje to wyłącznie transformacje i przezroczystość (GPU). Rdzeń pauzuje,
gdy okno jest zminimalizowane / ukryte albo gdy Gaming Mode / ustawienia
wyłączają animacje.

## Mapowanie funkcji (WPF → WinUI)

| Było (zakładki/strony) | Jest (WinUI) |
|---|---|
| Centrum (czat) | pasek komend + krótkie odpowiedzi + Historia (rozwijana) |
| System | czipy CPU/GPU/RAM/NET + panele szczegółów + wykresy |
| Gaming | badge GRA, Gaming Mode automatyczny, nakładka (paleta → gaming) |
| Voice | pigułka MIC (klik = wł/wył), Ustawienia → Głos |
| AI | pigułka AI + panel AI (modele, postęp, naprawa) |
| Actions/History | Historia (aktualne zadanie, działania, odpowiedzi) |
| Settings | panel ⚙: Ogólne / Głos / AI / Watch / Wygląd / Zaawansowane |
| Ctrl+K | paleta — wstawia do pola komend albo otwiera panele |
| Tools/Memory/Projects/Tasks | przez komendy (`pomoc`, `co pamiętasz`, `projekty`, `zadania`) |
| Telefon/QR | zasobnik → Telefon… (dialog z QR) |
| Głos, wake word, Ollama API, Watch, telemetria, tray, autostart, logi, pamięć, przypomnienia | podłączone do tych samych usług co WPF |

## Budowanie (Windows)

```powershell
# sama powłoka
dotnet build SentinelX.WinUI/SentinelX.WinUI.csproj -c Release

# portable + test + instalator (jak build-local.ps1)
powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1 -Test
```

Wymagania: **Windows 10 1809+ / 11 x64**, do budowania **.NET 10 SDK**.
Środowisko Windows App SDK jest samowystarczalne (`WindowsAppSDKSelfContained`) —
plik EXE działa bez osobnej instalacji runtime.

Test dymny bez okna:

```powershell
.\bin\portable-winui\SentinelX.exe --ui-smoke .\test-results\winui
```

## Migracja (co dalej)

1. Zweryfikować kompilację i smoke test w CI (`winui` w `dotnet-desktop.yml`).
2. Przetestować na żywym Windows: głos, AI, Watch, tray, Mini, gry.
3. Przełączyć `release.yml` i `build-local.ps1` na projekt WinUI.
4. Usunąć projekt WPF (Views, MainWindow.*, Themes WPF, TrayService, WPF Bootstrap)
   i przenieść `SentinelX.WinUI/*` do korzenia.

## Różnice celowe względem WPF

- Brak osobnych stron Tools / Memory / Projects / Tasks — funkcje działają
  przez komendy i paletę; panele pokazują tylko to, co dotyczy sterowania.
- Synteza mowy przez `Windows.Media` zamiast SAPI (`System.Speech`).
- Ustawienia to ten sam katalog pól (`SettingsCatalog`), ale w 6 sekcjach.
- Ikona: nowy motyw Rdzenia (`SentinelX.WinUI/Assets/sentinel.ico`).

## Rozwiązywanie problemów

- **Błąd XAML w runtime** — zwykle literówka w `x:Name`/`TargetName` storyboardu
  albo brak klucza w `Themes/`. Komunikat trafia do `Logs/errors.log`.
- **Brak toastów** — powiadomienia wymagają rejestracji `AppNotificationManager`
  (robi to `ToastService.Start()`); w grach info jest celowo wyciszane.
- **Tray nie startuje** — `WinUiTrayService` rzuca tylko na Windows bez powłoki;
  komunikat widać w linii statusu, aplikacja działa dalej.
- **Głos nie startuje** — jak w WPF: potrzebny mikrofon i pobrane modele
  (Ustawienia → Głos, kalibracja po 2 s ciszy).
