# Architektura SentinelX (stan bieżącego drzewa)

Dokument opisuje stan faktyczny kodu w tym checkoutcie, nie plany.
Nazwy typów są rzeczywiste — można ich szukać w repozytorium.

## 1. Warstwy

```
Views/*.xaml            widoki WPF, code-behind ograniczony do okna głównego i overlaya
   │  (bindings, commands)
ViewModels/*            stan widoku i AsyncRelayCommand (CommunityToolkit.Mvvm), bez logiki domenowej
   │  (wywołania serwisów)
Services/* + *.cs       cała logika: pomiary, polecenia, pamięć, zadania, projekty, diagnostyka
   │  (JSON + odczyt zwrotny)
Magazyny lokalne        %LocalAppData%\SentinelX\{Settings,Memory,History,Backups,Logs,Cache,Automations}
                        Logs/sentinel.jsonl: ustrukturyzowany JSONL z redakcją sekretów i rotacją
```

Zasada pilnowana przez `scripts/check-architecture.py` w CI: widok nie sięga do logiki,
ViewModel nie czyta plików bezpośrednio, serwis nie zna WPF.

## 2. Ścieżka polecenia (od klawiatury do dowodu)

```
CommandCenterViewModel.SendMessageCommand
  → IActionEngine (ActionEngine): jedna kolejka, zatrzask STOP, korelacja requestId
    → IIntentRouter (Services/Intent/IntentRouter)
        1. CommandUnderstanding.Repair        literówki i skróty (tylko przepisuje tekst)
        2. ReadOnlyCommandService             pomiary z katalogu aliasów (RAM/CPU/GPU/dyski/uptime/zegar/data)
        3. Services.Files.IFileService        pliki w jawnie nadanym zakresie
        4. SentinelToolboxService             uruchamianie programów, sieć, procesy, raporty
        5. CommandRouter                      pamięć, projekty, zadania, snapshoty, narzędzia, insights
        6. LocalAiService                     dopiero tu model językowy (wbudowany silnik llama.cpp, bez Ollamy — od 0.94)
```

Kluczowa decyzja: **model językowy jest na końcu**, więc żadna odpowiedź modelu nie wykonuje
polecenia. Każda warstwa zwraca `null`/`NotHandled`, gdy polecenie nie jest jej — dlatego kolejność
jest częścią kontraktu, a nie szczegółem implementacji (np. `szukaj wszystkiego: …` jest jawnie
wyłączone z wyszukiwarki WWW w `SentinelToolboxService`).

## 3. Rozumienie poleceń (`CommandUnderstanding` + `Core/IntentCatalog`)

- Odległość Damerau-Levenshteina (transpozycja = 1 edycja), podobieństwo normalizowane długością.
- Trzy ścieżki: dokładne trafienie → brak naprawy; skrót z `IntentCatalog.Abbreviations` → rozwinięcie;
  w przeciwnym razie ranking fraz (`PhraseSimilarity` = lepsze z podobieństwa całości i dopasowania
  słowo po słowie), a dalej naprawa pojedynczych słów ze słownika `IntentCatalog.Vocabulary`.
- Progi: 0,80 dla frazy i dla słowa, margines 0,08 wobec drugiej najlepszej kandydatury.
- Naprawa **wyłącznie przepisuje tekst**. Katalog nie zawiera czasowników niszczących, więc
  literówka nie może wywołać usunięcia danych; `tests/UnderstandingRegression.cs` przegląda katalog
  i pilnuje tej własności przy każdej zmianie.
- Efekt naprawy jest zawsze komunikowany: „Zrozumiałem jako: …”.

## 4. Dowody i statusy

`ActionExecutionResult` rozróżnia `VerifiedSuccess` (niezależny odczyt zwrotny: nazwa procesu z PID,
hash pliku, powtórny pomiar), `UnverifiedSuccess` (polecenie wysłane, skutek niepotwierdzony)
i `Failure`. Status `Verified` w historii pojawia się tylko przy pierwszym wariancie — to reguła
produktowa, nie kosmetyka: `zamknij notatnik` bez potwierdzenia procesu nie może być „zweryfikowany”.

## 5. Dane i prywatność

- Każdy magazyn (`conversation-memory.json`, `tasks.json`, `projects.json`, `snapshots.json`,
  `settings.json`) ma: zapis atomowy przez plik tymczasowy, kopię uszkodzonego pliku zamiast
  nadpisania, odczyt zwrotny i SHA-256 przy eksporcie.
- `MemoryPrivacy` jest czytany **przy każdym wywołaniu** (`PrivacyProvider`), więc przełącznik
  w ustawieniach działa bez restartu. Tryb prywatny jest wyłącznie runtime i nigdy nie jest zapisywany.
- Archiwum rozmów (`MemoryArchiveService`) pisze do `Memory/Archives/RRRR-MM/` i kasuje wpisy
  z magazynu **dopiero po** zgodnym odczycie zwrotnym. Wspomnienia i profil nie podlegają archiwizacji.
- `FileWorkspaceService` ogranicza operacje i odczyt utworzonego pliku do 1 MiB, odrzuca dowiązania i weryfikuje zapis; awaria lub anulowanie nie jest przedstawiane jako sukces.
- `WorkspaceInsightsService.Backup` kopiuje magazyny do `Backups/<data>/` z manifestem hashów
  i sprawdza każdą kopię odczytem zwrotnym.

## 6. Narzędzia deterministyczne (`UtilityToolbox`)

24 narzędzia to czyste funkcje statyczne: wejście → wynik, bez stanu, bez sieci, bez plików.
Dzięki temu `tests/UtilityRegression.cs` sprawdza je bezpośrednio, bez WPF i bez maszyny docelowej.
Kalkulator jest własnym parserem rekurencyjnym (`ExpressionParser`) — nie `DataTable.Compute`,
nie `eval`, nie kod z polecenia użytkownika.

## 7. Testy i CI

`--ui-smoke` uruchamia zestawy regresji (`tests/*.cs`, od 0.96 także `Testing/ForgeRegression.cs`, `Testing/RoutingRegression.cs` i `tests/AutomationRegression.cs`) na prawdziwym DI i prawdziwych stronach,
a następnie renderuje każdą stronę, zbierając błędy wiązań WPF z `PresentationTraceSources`.
`--self-test` odpala te same zestawy bez UI. CI (`windows-build.yml`): checki architektury → restore →
build → smoke UI → regresje → publish portable → sumy kontrolne → smoke portable → instalator Inno →
instalacja i smoke zainstalowanej aplikacji → artefakty.

## 7a. Układ katalogów (od 0.96)

```
Brain/            rozumienie i pamięć: CommandUnderstanding, ConversationMemoryService, LocalAiService, filtry
Brain/Router/     CommandRouter (ostatnia deterministyczna warstwa przed modelem), DecisionPreview („jak to rozumiem:”)
Tools/            UtilityToolbox (+ Extras, ForgeTools), SentinelToolboxService, ProcessToolService
Testing/          UiSmokeTestRunner, SelfTestRunner, runnery AI/ASR, RoutingRegression, ForgeRegression
Services/, Core/, ViewModels/, Views/   bez zmian
```

Przestrzenie nazw nie zmieniły się przy przenosinach (`SentinelX`, `SentinelX.Tests`). `scripts/check-architecture.py`
pilnuje, że w `Brain/` i `Tools/` nie ma `System.Windows`, a `scripts/check-syntax-lite.py` wyłapuje błędy, które
inaczej kosztowałyby cały przebieg Windows CI.

## 8. Znane granice architektury

- `CommandRouter` jest duży (ponad 500 linii) i łączy obsługę pamięci, projektów, zadań, snapshotów
  i narzędzi. Rozbicie go jest na backlogu; celowo nie zrobiono tego w 0.90, bo plik jest gęsto
  pokryty testami i refaktor bez zielonego CI byłby ryzykiem, nie poprawą.
- Naprawa literówek działa na katalogu tekstowym — nie rozumie odmiany ani kontekstu rozmowy.
- Brak bazy danych: magazyny JSON wystarczają przy obecnych limitach (720 wpisów rozmowy,
  500 wspomnień, 500 zadań, 200 projektów, 20 odczytów). Próg, przy którym warto rozważyć SQLite,
  to realne przekroczenie tych limitów przez użytkownika, nie przewidywanie.

## 9. Telefon, silnik AI i opiekun (0.94)

```
Views/Link/*            okno zgody na parowanie (PairingWindow), panel „Telefon” z kodem QR (PhoneLinkWindow), LinkUi — kod w C#, bez XAML
Services/Link/*         serwer HTTPS dla telefonu: LinkService (TLS, routing, parowanie, wykrywanie UDP), LinkApi (endpointy po zalogowaniu),
                        LinkHttp (parser HTTP/1.1 i odpowiedzi, SSE), LinkCertificate (certyfikat + DPAPI), LinkDeviceStore (hasze tokenów), AlertFeed
Services/Engine/*       wbudowany silnik AI: EngineCatalog (przypięte wersje i SHA-256), EngineDownloader (wznawianie), LlamaServerHost (proces potomny),
                        EngineOllamaFacade (dialekt Ollamy w procesie → llama-server), EngineService (automatyczna instalacja i cykl życia)
Services/Care/*         CareService: start i pilnowanie łącza oraz silnika, autopilot (jednorazowo), alerty, linia „Wszystko działa samo”
Phone/web/*             interfejs telefonu (HTML/CSS/JS), osadzony w exe i serwowany przez LinkService
phone-android/*         aplikacja Android (Java, bez bibliotek): powłoka WebView + wykrywanie + przypięty certyfikat + alerty + WoL
```

Polecenie z telefonu: `LinkApi` → `IActionEngine.ExecuteAsync(..., fromVoice: true)` — ta sama kolejka, te same dowody, te same zgody; ryzykowne akcje zatwierdza się wyłącznie na komputerze. `LinkDeviceStore` odrzuca nieprawidłowy i nadmiernie duży magazyn, zachowuje uszkodzony plik w kwarantannie, weryfikuje zapis JSON przed podmianą i nie wydaje tokenu przed trwałym zapisem jego skrótu. Odłączenie jednego lub wszystkich urządzeń zgłasza błąd zamiast udawać trwałe unieważnienie; panel „Telefon” ma przycisk per urządzenie.
Protokół i model bezpieczeństwa: [PHONE-LINK.md](PHONE-LINK.md). Silnik AI: [ENGINE.md](ENGINE.md).

## 10. Automatyzacje i powiadomienia

`Services/Automation/` rozdziela kontrakty reguł, katalog allowlisty akcji i scheduler. Triggerami są wyłącznie: ręczne uruchomienie, start aplikacji oraz harmonogram raz dziennie w lokalnym czasie. Akcje są osobnymi `IAutomationActionHandler`: rozpoznana aplikacja, walidowany adres HTTP/HTTPS i komunikat powiadomienia. Nie ma fallbacku do shell/PowerShell, swobodnej ścieżki procesu ani zdalnego wykonania. Reguły i ograniczona historia 500 wykonań są przechowywane atomowo w `Automations/automations.json`; awaria pliku pozostawia kwarantannę i komunikat.

`DesktopService` uruchamia scheduler po załadowaniu aplikacji. `INotificationService` przekazuje zdarzenia do lokalnego zasobnika i istniejącego `AlertFeed` (także dla sparowanego telefonu). Zmiana reguły w trakcie wykonania jest blokowana; ręczne i zaplanowane wykonanie można anulować, ale ukończone akcje nie są cofane. Regresja używa atrap, testuje walidację URL/aplikacji, kolejność, scheduler, trwałość, awarie, anulowanie oraz limit historii.

## 10a. Fundament 1.0.0: wersje, kontrakty i design tokens

- `SENTINEL-X.csproj` jest autorytatywnym źródłem semver. `Core/AppConstants.Version` identyfikuje wersję runtime (sprawdzaną przez `scripts/check-versions.py`), Android Gradle odczytuje `versionName` z projektu, a workflow nadal wyprowadza nazwy i tag wydania z tego samego XML. `versionCode` Androida jest niezależny i wzrósł z 98 do 100.
- `Services/Link/LinkContracts.cs` definiuje camelCase DTO dla hello/state/tasks/notes/alerts, parowania, sterowania, SSE i listy urządzeń. Numer `api` pozostaje 1; zmiany są addytywne, dotychczasowe nazwy pól i trasy się nie zmieniają. Pole `capabilities` w żądaniu parowania jest opcjonalne dla starszych klientów.
- PC publikuje stałą listę funkcji tylko dla istniejących endpointów. Telefon zgłasza capabilities zależnie od runtime (zgoda i aktywne kanały powiadomień, zainstalowany recognizer, implementacja WoL); serwer waliduje allowlistę, a pola są informacyjne i nie wpływają na autoryzację.
- Companion szyfruje token AES-GCM kluczem Android Keystore, migruje starszą preferencję przy odczycie i podczas aktualizacji przenosi pozostałą kopię WebView `localStorage` do magazynu natywnego przed jej usunięciem. W zwykłej przeglądarce nadal używany jest origin-scoped `localStorage`.
- Palety WPF (`Themes/Colors.xaml`), web (`Phone/web/app.css`) i Android (`values/colors.xml`) mają zgodne semantyczne kolory tła, powierzchni, tekstu i akcentów. Nie oznacza to jeszcze pełnej wspólnej biblioteki komponentów ani testów na fizycznym urządzeniu.
- Strumień odpowiedzi telefonu ma ograniczony bufor 128 fragmentów, a tabele rate-limit — 1024 klucze. Gdy telefon odbiera wolniej, część pośrednich delt może zostać pominięta; końcowe zdarzenie nadal zawiera całą odpowiedź.

## 11. Diagnostyka i dystrybucja

`AppLog` zapisuje ograniczony JSONL w `Logs/sentinel.jsonl`: kategorie, poziomy, redakcja typowych tokenów/kluczy/hasła oraz rotacja przy 2 MiB z czterema archiwami. Logger jest best-effort i nie może zakończyć procesu. Ścieżka `SENTINEL_DATA_DIR` jest używana tylko jako absolutny override, a błędna wartość wraca do katalogu LocalAppData.

Zwykły push publikuje 7-dniowe artefakty instalatora EXE i APK, lecz nie tworzy wydania GitHub. Release buduje Windows i Android osobno, wymaga chronionego trwałego klucza Androida, waliduje podpis oraz sumy kontrolne, a pojedynczy job publikuje dopiero po sukcesie obu buildów. Istniejących tagów i wydań nie nadpisuje. Stary klucz z jawnym hasłem usunięto z bieżącego drzewa, ale pozostaje w historii wcześniejszych commitów i należy traktować go jako ujawniony.
