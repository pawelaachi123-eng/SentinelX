# SENTINEL X — plan rozwoju oparty na researchu

> **Status: PLAN, nie implementacja.** Żadna pozycja poniżej nie oznacza, że funkcja została dostarczona lub przeszła pomiary.
> Zasada nadrzędna: **żadnych udawanych pomiarów, żadnego VERIFIED bez dowodu z narzędzia, żadnego wykonywania dowolnych narzędzi przez LLM z pominięciem polityki i zgód.**
> Oryginalna propozycja: 2026-09-20, runda 1. Aktualizacja: **2026-09-22, audyt rundy 1 / rewizja 1.1**.
> Zakres audytu: bieżące pliki wersji **0.84.0** w drzewie roboczym, nie sam numer commita HEAD. Istniejących zmian aplikacji nie resetowano. W tej rundzie zmieniana jest wyłącznie dokumentacja.

## 0. Jak czytać i aktualizować plan

| Pole | Znaczenie |
|---|---|
| P0 | bezpieczeństwo, pomiary bazowe i fundamenty jakości/latencji |
| P1 | poprawa użyteczności i rozszerzenia po przejściu bramek P0 |
| P2 | eksperymenty i rozszerzenia, które nie blokują podstawowego produktu |
| Koszt | S: ≤1 dzień; M: 2–5 dni; L: 1–3 tygodnie; XL: >3 tygodnie. Szacunek pracy, nie termin ani obietnica; zbieranie korpusu, sprzęt i licencje mogą go zwiększyć. |
| KOD | stwierdzenie sprawdzone w aktualnych plikach; nie zastępuje testu wykonania |
| ŹRÓDŁO | potwierdzenie w dokumentacji/API/model card upstream; nie oznacza zgodności naszej paczki Windows |
| HIPOTEZA | spodziewana korzyść, wymagająca pomiaru porównawczego |
| DO WERYFIKACJI | brak wystarczającego dowodu; nie używać jako argumentu za wydaniem |

„Lepszy niż wszystko na GitHubie” nie jest kryterium odbioru. Porównujemy konkretne wersje i konfiguracje na tym samym korpusie i sprzęcie, publikując wyniki oraz przypadki porażek. Nie ma tutaj przeprowadzonego benchmarku konkurentów.

Każda kolejna runda dopisuje wpis do §7, zachowuje identyfikatory pozycji, podaje datę dostępu do źródeł i jawnie rozróżnia: **proponowane → zaakceptowane → implementowane → zmierzone → wydane**. Odhaczenie samego kodu nie zamyka zadania. Pliki oznaczone jako „nowe” są planowanymi miejscami integracji, nie istniejącymi funkcjami.

## 1. Audyt stanu 0.84

### 1.1 Potwierdzone w kodzie

| Obszar | Ustalenie i miejsce |
|---|---|
| UI i DI | WPF, .NET 9, C# 13, CommunityToolkit.Mvvm 8.4.2; 8 stron, Ctrl+K, gotowość. `Core/ServiceLocator.cs` używa `ValidateOnBuild` i `ValidateScopes`. |
| Wykonanie | `Services/Actions/ActionEngine.cs` dopuszcza jedno aktywne wykonanie; kolejne żądanie podczas zajętości **odrzuca**, a nie kolejkuje. `PermissionCenterService.cs` trzyma jedną oczekującą zgodę. |
| Dowody | `ActionHistoryService.cs`, `Services/History/ActionEvidenceCapture.cs`: `requestId`, dowody zakresu wykonania, agregacja wyników, ochrona przed późnym dopisywaniem; tekst modelu nie wystarcza do VERIFIED. |
| STOP | Zatrzask w `ActionEngine`, anulowanie, integracja z głosem i pulpitem. Nie jest rollbackiem ukończonych operacji. |
| Audio | `VoiceRecognitionService.cs`: NAudio, `AudioEnhancementService.cs`, `SileroVadService.cs`, Qwen3-ASR **0.6B INT8**, opcjonalny fallback Whisper Small PL. `VoiceModelManager.cs` wskazuje pakiet Qwen z 2026-03-25. Qwen jest preferowany, gdy lokalnie dostępny; standardowy przycisk pobierania instaluje Whisper/VAD, nie gwarantuje obecności Qwen. |
| Stan głosu | Domyślne GUI używa `Services/Voice/VoiceService.cs` oraz enum `VoiceState` w `Models/ActionRecord.cs`: Off/Standby/Active. `VoiceStateMachine.cs` istnieje, lecz audyt referencji nie wykazał jej użycia w tym torze. Nie planować integracji wyłącznie w tej niepodłączonej klasie. |
| Wybudzanie i tekst | Filtr działa **po ASR**, w `VoiceAudioUtilities.cs` (`VoiceTranscriptFilter`) i `Services/Voice/VoiceService.cs`; `Core/CommandText.cs` normalizuje komendy. `WakeWordService.cs` nie jest główną bramką obecnego GUI. `PolishTextNormalizer.cs` jest używany przez parser timerów, nie potwierdzono jego podłączenia jako ogólnego etapu ASR. |
| TTS | `SpeechOutputService.cs` używa SAPI, preferuje głos PL, jawnie zgłasza jego brak. `VoiceService.Speak` ustawia `RecognitionSuppressed`; obecna architektura nie zapewnia barge-in przez VAD podczas TTS. |
| AI | `Services/AI/AiService.cs`: OllamaSharp 5.4.18 do discovery. `LocalAiService.cs`: lokalny HTTP, retry/timeout, `/api/chat` i fallback `/api/generate`, `stream=false`, bez `tools`/schematu. **`think=false` już jest dla nazw zaczynających się od `qwen3`.** Gaming ustawia `keep_alive="0"`, normalnie `10m`; obniża `num_ctx`. |
| Routing | `Services/Intent/IntentRouter.cs` → odczyty → `FileWorkspaceService.cs` → `SentinelToolboxService.cs` → `CommandRouter.cs` → LLM. Ostatni router już ma regexy i odmiany, m.in. „ile mam ramu?”. Problemem jest rozproszenie i nierówny audyt dowodów, nie całkowity brak wariantów. |
| Telemetria | `SystemMonitor.cs`: GetSystemTimes, GlobalMemoryStatusEx, liczniki GPU Engine / Utilization Percentage; niedostępne odczyty = NaN. Brak implementacji planowanych NVML/PresentMon. |
| Gaming | `GamingModeService.cs`: 13 nazw procesów, cache 2 s. Nie zmienia Windows Game Mode ani priorytetów procesów. |
| Pliki i pamięć | Wyszukiwanie nazw w `CreatedFiles`. `ConversationMemoryService.cs` zapisuje również trwałe notatki/profil i udostępnia wyszukiwanie tekstowe/czasowe. Brakuje retrievalu semantycznego, ale nie jest prawdą, że asystent w ogóle nie pamięta wcześniejszych faktów. |
| Testy | `tests/BackendRegression.cs`, `tests/ProductRegression.cs`, `UiSmokeTestRunner.cs` i runnery w EXE. **Jest test UI WPF**, brak osobnego projektu testów jednostkowych i pełnego testu interakcji użytkownika. `--asr-test` to obecnie jedna próbka SAPI→Whisper/VAD, nie korpus 200 komend ani test mikrofonu. |
| CI | `.github/workflows/windows-build.yml`: build, UI smoke, regresje, self-contained win-x64 i smoke opublikowanej paczki. To nie test jakości polskiego ASR/TTS, prawdziwego GPU czy gier. |

W tym audycie nie uruchamiano modeli, mikrofonu, gier ani nowego builda Windows. Poprzedni raport CI aplikacji: [run 35516456204](https://github.com/pawelaachi123-eng/SentinelX/actions/runs/35516456204), commit `8131e1cb0eae319ee065df87175f54f86f721501`; nie jest wynikiem nowych eksperymentów z tego planu.

### 1.2 Luki i korekty pierwotnego backlogu

| ID | Luka po weryfikacji | Priorytet / dalszy plan |
|---|---|---|
| L1 | Pełne ASR segmentów przed rozpoznaniem wake word; koszty i false wakes niezmierzone. | P0, §3.1 |
| L2 | Polski SAPI zależy od zainstalowanych głosów; brak niezależnego neural TTS. | P0, §3.2 |
| L3 | Brak streamingu, structured proposals i discovery zdolności; częściowa obsługa `think` już istnieje. | P0, §3.3 |
| L4 | Rozproszone aliasy/regexy i dowody, nie „same sztywne stringi”. | P0 porządkowanie / P1 semantyka, §3.4 |
| L5 | Brak VRAM i temperatur; DXGI budget procesu nie rozwiąże globalnego VRAM. | P1, §3.5 |
| L6 | Brak rzeczywistych FPS/frametime; brak odczytu jest dziś jawny. | P1, §3.6 |
| L7 | Wykrywanie gier zależy od zamkniętej listy. | P1, §3.7 |
| L8 | Brak identyfikacji mówcy; wake word nie jest uwierzytelnieniem. | P2 speaker verification; zabezpieczenie zgód P0, §3.0 i §4 |
| L9 | Gate/gain bez modelowego odszumiania; niezmierzony wpływ na WER. | P1 eksperyment A/B, §4 |
| L10 | Dystrybucja z Actions, brak kontrolowanego aktualizatora. | P1, §4 |
| L11 | Wyszukiwanie tylko nazw w ograniczonym workspace. | P1, §4 |
| L12 | Brak retrievalu semantycznego, mimo istniejącej pamięci trwałej. | P1, §4 |
| L13 | Brak wspólnego, typowanego rejestru narzędzi i MCP. | P0 kontrakty / P1 MCP, §3.9 |
| L14 | Testy w EXE; brak izolowanego projektu. UI smoke już istnieje. | P0 fundament testowy, §3.0 |
| L15 | Nieznany WER naszej konfiguracji Qwen/Whisper; benchmark innego modelu nie jest baseline'em. | P0 pomiary, §3.8 |
| L16 | Potwierdzenia i operacje na pamięci nie są w pełni scentralizowane. Znalezisko poniżej. | **P0 przed rozszerzeniem narzędzi LLM/MCP** |
| L17 | Brak przypiętego manifestu SHA-256 całego zestawu modeli w `VoiceModelManager`; są kontrole plików/rozmiarów/nagłówków. | P0 przed nowymi modelami, §3.0 |

**Ważne znalezisko statyczne L16 — nie deklarować „0 ryzykownych akcji bez zgody, dziś” jako dowiedzionego faktu.** `Core/CommandText.IsApproval` blokuje w głosie `potwierdz`, `potwierdz akcje`, `confirm`. `CommandRouter.TryHandleMemoryCommand` ma osobny stan i frazę **`potwierdz usuniecie wspomnien`**, która po wcześniejszej prośbie w ciągu 60 s wywołuje `memory.ClearAll()`. Ta fraza nie należy do listy blokowanej przez `ActionEngine`, a router nie otrzymuje informacji `fromVoice`. Połączenie tych ścieżek wskazuje lukę w kontroli źródła potwierdzenia; nie przeprowadzano destrukcyjnego testu na danych użytkownika. Sam napis „wpisz” w odpowiedzi nie egzekwuje klawiatury. `memory.Clear()` / `Forget()` też wymagają przeglądu polityki ryzyka. Ponadto osobny stan potwierdzenia w routerze należy objąć STOP/cancel/expiry. Naprawa **nie została wykonana** w tym audycie.

## 2. Mierzalne cele i protokół

Wszystkie wartości „dziś” dla jakości/latencji są **niezmierzone na korpusie SentinelX**. Cele są proponowanymi bramkami 1.0, nie aktualnymi wynikami ani gwarancją na dowolnym PC.

| Metryka | Proponowany cel / sposób odbioru |
|---|---|
| Wake dispatch | P95 <150 ms **od zdarzenia KWS do gotowości przyjmowania audio komendy**. Osobno mierzyć koniec frazy→detekcja KWS→gotowość ASR. Nie utożsamiać z pełną latencją modelu: wybrany KWS dokumentuje 160/320 ms zależnie od chunku [S2]. Pełny budżet ustalić po spike'u. |
| Koszt czuwania | Średnio <3% jednego logicznego rdzenia na zadeklarowanym sprzęcie; `100 × ΔCPU_seconds / Δwall_seconds`, bez dzielenia przez liczbę CPU. Oddzielnie cały proces, tor audio, piki i pamięć. |
| False wakes | <0,125 zdarzenia/h na niezależnym korpusie tła. Rejestrować też false rejects, proponowany cel ≤5% na oznaczonych frazach. Anulowanie w <2 s to **proxy niezadowolenia**, nie etykieta false wake. |
| WER PL | ≤6% na własnym zbiorze testowym; raportować też exact match intencji + argumentów, negacje i nazwy własne. `WER=(S+D+I)/N`; puste referencje liczyć jako osobny test halucynacji na ciszy. |
| Mowa→pierwsze audio | P95 <900 ms dla odpowiedzi narzędziowych, <1,8 s dla pierwszego zdania LLM **w zadeklarowanym profilu warm**. Oddzielne cold-start, ładowanie modeli, endpointing VAD i opóźnienie urządzenia audio. |
| Routing bez LLM | ≥95% exact match intencji i argumentów na odłożonych 200 komendach PL; osobno precision/coverage, odmowy, pytania doprecyzowujące, out-of-domain i negacje. |
| Routing z LLM | Usunięto obietnicę 100% trafności. Cel: poprawa ponad baseline oraz **100% odrzucenia niepoprawnych/niedozwolonych propozycji w skończonym zestawie testów bezpieczeństwa**. To nie gwarancja generalizacji. |
| Zgody i dowody | 0 niedozwolonych wykonań w macierzy testów; niezmienniki mają obowiązywać zawsze. Model, mówca ani `confidence` nie podnoszą uprawnień. Każde wykonanie ma korelację, źródło i sprawdzenie postcondition. |
| GPU | Poprawne jednostki, zakres `device/process`, adapter ID, źródło i wiek odczytu. Porównanie tego samego adaptera i zakresu z narzędziem referencyjnym; brak/stale = jawnie niedostępne. |
| FPS/frametime | Rzeczywiste dane wskazanego PID/swapchain z PresentMon, oznaczone typem klatek; nie estymacja z CPU. Overlay odświeżany 1 Hz, dane zbierane per-frame / z agregatów SDK. |

### 2.1 Wspólny protokół eksperymentów

- Raport zawiera: SHA aplikacji, OS/CPU/GPU/sterownik, urządzenie audio, model i hash plików, runtime/provider, liczbę wątków, ustawienia, typ zasilania i tryb gry. Ten sam sprzęt/limity dla baseline i kandydata.
- Minimum 200 **testowych** komend × 3 mówców × 2 warunki = 1200 prób audio. Prototypy routingu i zbiór kalibracyjny osobno; nie stroić progów na teście. Oznaczyć dystans 1 m, poziom/SNR szumu, ciszę, mowę bez komendy, TV oraz wypowiedzi przeczące. Podział chroni przed przeciekiem fraz i mówców; wyniki rozbić na osoby i warunki.
- Korpus od ochotników z jawną zgodą/licencją, pseudonimowymi ID i możliwością usunięcia. Audio nie trafia automatycznie do repo, logów ani publicznych artefaktów CI. W repo manifest, instrukcja i ewentualnie małe próbki z prawem redystrybucji; większe WAV-y przechowywane poza Git z hashami.
- Normalizacja WER ustalona przed testem: Unicode, wielkość liter, interpunkcja, liczby; nie usuwać po cichu polskich znaków ani korygować hipotez pod referencję. Raportować także tekst surowy na zbiorze testowym, nie na rozmowach użytkownika.
- Monotoniczny zegar (`Stopwatch`) dla opóźnień; sample clock dla oznaczenia końca mowy. Zdarzenia: speech-end, VAD-final, KWS-hit, ASR-ready/final, route-final, tool-end, LLM-first-token, sentence-ready, TTS-first-sample, playback-start. Pierwsza próbka wygenerowana ≠ pierwsza próbka odtworzona; faktyczne słyszalne opóźnienie wymaga pomiaru loopback/hardware.
- Co najmniej 30 powtórzeń cold/warm dla latencji, raport P50/P95, liczby próbek, błędów, timeoutów i zużycia RAM/VRAM; nie wyrzucać porażek z tabeli. Analizować konflikt `keep_alive=0` w gaming z celem szybkiej odpowiedzi — oszczędność VRAM kosztem ładowania modelu.
- 8 h tła to pilotaż, nie dowód rzadkich zdarzeń. Dla 0 zdarzeń górna jednostronna granica Poissona 95% wynosi około `2,996/T` zdarzeń/h: 8 h daje ~0,375/h. Zaplanować np. ≥32 h odłożonego tła (0 zdarzeń → ~0,094/h), wraz z testami pozytywnymi i opisem ograniczeń założenia Poissona.
- TTS→ASR bada spójność pipeline'u, **nie naturalność głosu i nie jakość ASR na ludziach**. Dodatkowo ślepy odsłuch A/B po polsku: zrozumiałość, diakrytyki, liczby/datowanie, skróty, negacje, nazwy aplikacji.

## 3. Decyzje i plan w kodzie

### 3.0 Fundament przed nowymi silnikami — P0, M–L

**Źródło:** audyt KOD (§1, L14/L16/L17), istniejące testy regresji. **Dlaczego:** rozbudowa protokołów nie może utrwalić alternatywnych ścieżek zgód lub fałszywych baseline'ów.

**Gdzie:** `Core/CommandText.cs`, `CommandRouter.cs`, `Services/Actions/ActionEngine.cs`, `PermissionCenterService.cs`, `VoiceModelManager.cs`, `VoiceSegmentMetrics.cs`, `AsrTestRunner.cs`, `tests/ProductRegression.cs`; nowe osobne projekty testów i manifest modeli.

**Plan:**
1. Najpierw test na izolowanej pamięci/fake executor dla L16; scentralizować kontekst pochodzenia żądania i decyzji. Nie wystarczy dopisywanie kolejnych fraz do blacklisty. Zweryfikować oba interfejsy, STOP→resume, timeout, zmianę celu i replay.
2. Wyodrębnić czystą logikę do testów bez WPF/urządzeń. Zachować aktualne testy EXE/portable; przy dodaniu projektów wyłączyć ich źródła z domyślnego globu Compile głównego csproj.
3. Dodać metryki z §2 i read-only benchmark runner; wywołania zmieniające stan zastępuje fake executor.
4. Manifest modeli: niezmienne URL/revision, hash oczekiwany z zaufanego manifestu, rozmiary, licencje komponentów i modeli, wymagany runtime, atomowa instalacja do katalogu tymczasowego. Hash wyliczony dopiero po pobraniu bez zaufanego wzorca nie uwierzytelnia pliku.

**Ryzyko:** regresje w legacy i formacie ustawień, ABI bibliotek native. **Dowód:** 0 wywołań destructive executor przy potwierdzeniu głosowym/LLM, wygasłej lub anulowanej zgodzie; poprawny dowód po dozwolonej operacji; test uszkodzonego/podmienionego modelu, braku miejsca i anulowania pobierania. Migracja bez utraty danych.

### 3.1 Prawdziwe KWS przed pełnym ASR — P0, spike S, integracja M–L

**Źródło:** open-vocabulary KWS i progi [S1], model zh-en 3M [S2], binding `SherpaOnnx.KeywordSpotter` w **v1.13.5** [S3]. **HIPOTEZA:** niższy CPU i mniej transkrypcji tła; mniej false wakes dopiero po kalibracji.

**Gdzie:** nowy `Services/Voice/KeywordSpotterService.cs`; przede wszystkim `VoiceRecognitionService.cs`, `Services/Voice/VoiceService.cs`, `VoiceRuntimeOptions.cs`, `VoiceModelManager.cs`, readiness. `VoiceStateMachine.cs` dopiero po świadomym podłączeniu lub konsolidacji stanów.

**Plan:** 16 kHz mono, bounded audio queue; STANDBY = KWS z właściwym buforem wstecznym, ACTIVE = pełne ASR. VAD nie może uciąć początku wake word/komendy. ASR załadowany vs zimny start jawnie w profilu; niewielki KWS nie oznacza automatycznie małego całkowitego RAM, jeśli ASR zostaje w pamięci. Model posiada warianty chunk-8 (160 ms) i chunk-16 (320 ms), co wymaga korekty pierwotnego celu <150 ms end-to-end [S2].

Tokeny wygenerować narzędziem `text2token` i słownikiem **tego modelu** (`phone+ppinyin`, angielskie fonemy CMU, oryginalna fraza `@...` zgodnie z dokumentacją). Nie wpisywać arbitralnych polskich fonemów spoza `tokens.txt`. „Sentinel” po polsku jest **kandydatem do testów**, nie zagwarantowaną detekcją. Strict/Balanced/Forgiving uzależnić od skalibrowanych progów i boosting score. Brak modelu: jawnie wybrany fallback tekstowy lub push-to-talk; bez cichej zmiany zakresu nasłuchu. W czuwaniu mikrofon pozostaje fizycznie otwarty — UI musi to komunikować.

**Ryzyko:** akcent PL, false wakes/false rejects, utrata komendy tuż po wake word, dodatkowe biblioteki/model, nieprzerwane dekodowanie po STOP. openWakeWord to alternatywa, lecz kod jest Apache-2.0, a dołączone modele CC-BY-NC-SA-4.0, **nie MIT** [S4]; trzeba też odtworzyć frontend/embedding/windowing, nie tylko uruchomić pojedynczy ONNX.

**Dowód:** §2, ciągłe „Sentinel pokaż RAM”, brak transkrypcji tła przez pełne ASR w trybie KWS, brak auto-zapisu audio, STOP zamyka capture; pomiary na tym samym sprzęcie z tekstowym fallbackiem jako baseline.

### 3.2 Lokalny polski neural TTS — P0, M–L

**Źródło:** VITS/Piper w sherpa [S5], polskie paczki w release `tts-models` [S6], karta głosu Gosia [S7], `OfflineTts`/callback w bindingu v1.13.5 [S8]. To potwierdzenie dostępności API/modeli, **nie test działania naszej paczki**.

**Gdzie:** `SpeechOutputService.cs`, `Services/Voice/VoiceService.cs`, nowy `ITtsEngine`, `SapiTtsEngine`, `SherpaTtsEngine`; `VoiceModelManager.cs`, DI, ustawienia i readiness.

**Plan:** najpierw pojedyncza polska odpowiedź z jawnym pobraniem wybranego głosu, zachowanie SAPI jako opcji. Potem ograniczona kolejka zdań i NAudio `WaveOutEvent`, zgodna z sample rate modelu (Gosia: 22050 Hz [S7]); nie zakładać 16 kHz z toru ASR. Adapter tempa ma respektować API `speed` i konfigurację modelu, a nie bezpośrednio kopiować SAPI Rate do `length_scale`. Jawne volume/voice/test sentence. Pliki fonemizatora, tokens/config i zależności native są częścią paczki — sam `.onnx` nie wystarcza.

Barge-in osobno: obecne `RecognitionSuppressed` wyłącza ścieżkę potrzebną VAD. Potrzebny kontrolowany tor wykrywania mowy podczas playback, obsługa echa/feedback i odwoływanie starych buforów oraz wyników syntezy. W pierwszym etapie jawny przycisk przerwania/push-to-talk; nie obiecywać pełnego duplexu samym dodaniem VAD.

**Licencje i ryzyko:** osobno biblioteka, fonemizator, dataset i wagi. Karta Gosia podaje CC0 **datasetu**, nie zwalnia to z audytu całej dystrybucji [S7]. Aktualny Piper `piper1-gpl` ma GPL-3.0 [S9]; sherpa ma integrację espeak-ng [S10]. Nie oznacza to automatycznej oceny licencji aplikacji — trzeba sprawdzić konkretny build i obowiązki redystrybucji. „60–75 MB” i „kilkadziesiąt ms” nie są jeszcze parametrami wybranej paczki na Windows.

**Dowód:** poprawne PL w czystym środowisku bez głosu SAPI, pomiary first-sample/playback z §2, odsłuch, przerwanie bez dalszego playback i bez samozleconej akcji. Supertonic / poprawka #3750 z propozycji: **DO WERYFIKACJI**, nie powód do podbicia zależności z 1.13.5 na podstawie samej strony PyPI.

### 3.3 Ollama: streaming i bezpieczne propozycje narzędzi — P0, łącznie L

**Źródło:** `/api/chat` wspiera streaming, `tools`, `format`, `think` dla wspierających modeli [S11]; `/api/show` zwraca capabilities [S12]. `.NET 9 JsonSchemaExporter` eksportuje schemat [S13], **nie jest automatycznym walidatorem ani polityką uprawnień**.

**Gdzie:** `LocalAiService.cs`, `Services/AI/IAiService.cs`, `Services/AI/AiService.cs`, nowy `Services/AI/IntentSchema.cs`, kontrakty intencji, `ViewModels/CommandCenterViewModel.cs`, TTS i ActionEngine.

**Etapy:**
- **A — tekst (M):** NDJSON przez streaming HTTP, jawne `done`/błąd/niepełny stream; ograniczenia długości, buforowania i czasu; aktualizacje UI porcjami. Dla `/api/chat` czytać `message.content`, dla `/api/generate` odrębne pole `response`. Nie pokazywać ani nie odczytywać `thinking`; nie mieszać go z content. Anulowanie zamyka transport i unieważnia opóźnione fragmenty. Retry po pokazaniu/odczytaniu części odpowiedzi nie może jej powielać.
- **B — propozycja (M):** pełny, zamknięty schemat tool/args lub clarify/decline, `temperature=0`; najpierw registry §3.9A i §3.0. Walidacja nazw, typów, zakresów, dodatkowych pól, długości, argumentów i dozwolonych zasobów po stronie hosta. Rzutowanie string→number tylko jawne i bezstratne w zdefiniowanym kontrakcie; wątpliwe dane odrzucić. `confidence` modelu nie jest skalibrowanym prawdopodobieństwem ani zgodą.
- **C — native tool calling (M):** osobny adapter dla modelu z capabilities tools. Nie zakładać, że jednoczesne `format` i `tools` działa identycznie we wszystkich modelach/wersjach. Nie wykonywać fragmentu argumentów z nieukończonego streamu; tylko kompletna zwalidowana propozycja. Zgoda związana z request/tool/argumentami i aktualnym celem, limit kroków, zakaz powtórnego wykonania po retry, ponowna kontrola zasobu tuż przed efektem.

`think` konfigurować według zdolności modelu i obsługiwanych wartości, nie samego prefiksu nazwy. Brak capabilities w starej Ollama = „nieznane”, nie automatyczne wsparcie. Zachować ograniczenie do lokalnych modeli/endpointu. Model nie ustala ryzyka, statusu Verified ani dowodów. Wypowiedź „wykonano” może być odczytana dopiero z wyniku narzędzia; JSON propozycji nie trafia do TTS.

**Ryzyko:** halucynacje argumentów, prompt injection w plikach/pamięci/odpowiedziach narzędzi, duplikacja efektów, odczyt fałszywego sukcesu na głos. **Dowód:** fake HTTP: pocięte NDJSON/UTF-8, opóźnienia, rozłączenie, błędny JSON, brak done, nieznane narzędzie, nadmiarowe pola, replay, cancel, niedozwolona ścieżka; licznik executor calls musi pozostać 0 dla przypadków niedozwolonych. Smoke Windows oraz test z konkretną lokalną wersją Ollama, nie tylko atrapą.

### 3.4 Routing: konsolidacja, potem semantyka — P0 S–M / P1 M–L

**Źródło:** KOD obu routerów; multilingual-e5-small deklaruje PL i MIT [S14]. Wybór ONNX vs Ollama embeddingów nadal **DO WERYFIKACJI**. BGE-M3 nie traktować bez pomiaru jako małego modelu na słaby laptop.

**Gdzie:** `Core/CommandCatalog.cs`, `CommandRouter.cs`, `Services/Intent/IntentRouter.cs`; nowy `Services/Intent/SemanticIntentIndex.cs`, registry narzędzi, korpus `tests/intent-pl/`.

**Plan:** najpierw zebrać istniejące aliasy bez utraty funkcji i zapewnić te same dowody dla wariantów metryk. „Ile mam RAM” vs „ile używam RAM” muszą pozostać różnymi intencjami; „nie zamykaj” nigdy nie jest close. Kolejność docelowa: reguły i walidacja → klasyfikator z abstencją → zwalidowana propozycja LLM / pytanie. Zaczynać od odczytów. Dla efektów ubocznych podobieństwo wektorowe nie uprawnia do wykonania.

Cache przez `AppPaths.MemoryDirectory` (respektuje `SENTINEL_DATA_DIR`), z fingerprintem modelu, tokenizerem/preprocessingiem, wymiarem embeddingów, normalizacją i wersją katalogu. Model oblicza też embedding **każdego zapytania**, więc „raz i cache” dotyczy prototypów, nie całego kosztu routingu. Prefixy/pooling dobrać według model card. Próg i margines top1–top2 skalibrować; cosine nie jest prawdopodobieństwem. Brak modelu/usługi → istniejące reguły nadal działają.

**Dlaczego / ryzyko:** większe pokrycie wariantów bez pełnej generacji, ale mylenie negacji, obiektów i wieloznaczności może szkodzić bardziej niż odmowa. **Dowód:** odłożony korpus §2, macierz pomyłek, argumenty, odrzucenie OOD, osobny koszt i trafność każdej warstwy. Ujednolicenie dowodów nie może opierać się na parsowaniu tekstu odpowiedzi.

### 3.5 VRAM i telemetria GPU — P1, M (z testami wielu adapterów)

**Źródło:** Microsoft DXGI [S15]; publiczny nagłówek NVML NVIDIA [S16]. **Korekta:** `DXGI_QUERY_VIDEO_MEMORY_INFO.CurrentUsage` to zużycie **aplikacji**, `Budget` to przydział budżetu aplikacji — nie globalne used/total VRAM karty. WDDM i TCC różnią się też semantyką raportów NVML [S16].

**Gdzie:** `SystemMonitor.cs`, `Services/System/SystemMonitorService.cs`, nowy opcjonalny `NvmlGpuProbe`, model telemetry snapshot, System/Gaming/Overlay VM.

**Plan:** kontrakt odczytu z wartością, jednostką, źródłem, adapter LUID/UUID, zakresem i timestampem. DXGI wykorzystać do identyfikacji/pojemności adaptera i osobno budżetu własnego procesu; odczyt device-wide sprawdzić przez właściwy licznik WDDM lub vendor API. Intel UMA/shared memory nie udaje dedykowanego VRAM. NVML tylko opcjonalnie dla NVIDIA; błędy funkcji i brak wsparcia konkretnego sensora = niedostępne. DLL ładować wyłącznie z zaufanej lokalizacji sterownika/SDK, nie z dowolnego bieżącego katalogu.

**Dlaczego / ryzyko:** użyteczna realna telemetria, ale zła semantyka API daje wiarygodnie wyglądający fałszywy wykres. Multi-GPU, różne sterowniki i dostępność temperatur wymagają sprzętu. LibreHardwareMonitor z driverem/adminem pozostaje poza domyślną ścieżką.

**Dowód:** testy NVIDIA/AMD/Intel w zakresie faktycznie posiadanego sprzętu; tabela unsupported, odpięcie/uszkodzony driver, stale, zero vs missing, poprawne mapowanie adaptera. Brak karty w laboratorium = **nieweryfikowane**, nie zielony check.

### 3.6 FPS/frametime przez PresentMon — P1, L

**Źródło:** oficjalne Service/SDK [S17]. Service łączy dane ETW i telemetry; od 2.3.1 instalator dostarcza `PresentMonAPI2.dll`. Kopia DLL niezwiązana z usługą nie gwarantuje zgodności; loader i header są częścią opcjonalnego SDK.

**Gdzie:** nowy `Services/Gaming/PresentMonClient.cs`, Gaming/Overlay VM, readiness, opcjonalne ustawienia integracji.

**Plan:** opt-in klient zainstalowanej usługi, zgodny header/ABI i x64 P/Invoke; błąd wersji/usługi/uprawnień daje jawny brak odczytu. Nie instalować usługi ani nie podnosić uprawnień automatycznie. Zbierać per-frame lub odpowiednie agregaty SDK, a UI odświeżać 1×/s. Wybrać PID/swapchain, filtrować menu/minimalizację, określić displayed vs presented frames i rolling window.

**Definicje:** FPS jako liczba właściwych klatek / okres; osobno P99 frametime. Jeśli „1% low” = `1000 / średnia najwolniejszego 1% frametime_ms`, udokumentować dokładnie tę definicję i okno; nie zamieniać jej bez opisu na `1000/P99`. Za mało klatek = brak wyniku, nie 0. Sparkline bez fikcyjnego interpolowania utraconych odczytów.

**Dlaczego / ryzyko:** realne doświadczenie gracza bez wstrzykiwania hooka, ale **brak hooka nie gwarantuje braku ryzyka ani zgodności z każdym anti-cheat**. Zweryfikować zasady gier i wersje usług; nie promować jako „anti-cheat safe”. Admina/uprawnień ETW nie obiecywać z góry. Optyczny input-to-photon nie jest tym samym co dowolna metryka latencji ETW.

**Dowód:** kontrolowany workload i te same PID/okna czasu w narzędziu referencyjnym; restart usługi, brak uprawnień, wiele swapchains, utrata danych; pomiar narzutu na CPU i frametime z overlayem on/off. Bez sprzętu/usługi pozostaje niezweryfikowane.

### 3.7 Wykrywanie gier z wielu sygnałów — P1, M

**Źródło:** `GamingModeService.cs` i Microsoft `QUERY_USER_NOTIFICATION_STATE` [S18]. `QUNS_BUSY` może oznaczać prezentację; exclusive fullscreen D3D też nie jest dowodem tożsamości gry.

**Gdzie:** `GamingModeService.cs`, `Services/Gaming/IGamingService.cs`, Gaming VM i ustawienia.

**Plan:** znane procesy + foreground/fullscreen na właściwym monitorze + stan notyfikacji + opcjonalny sygnał launchera, identyfikacja PID, debouncing/histereza i override użytkownika. `HKCU\Software\Valve\Steam\RunningAppID` oraz odpowiedniki innych launcherów są **DO WERYFIKACJI**, nie stabilnym wspólnym API. Dwa skorelowane sygnały fullscreen/Busy nie są dwoma niezależnymi dowodami. UI: „prawdopodobna gra / aplikacja pełnoekranowa” z listą sygnałów, nie arbitralne „tak, 2/4”.

**Dlaczego / ryzyko:** mniej ręcznych aktualizacji listy, ale film, PowerPoint lub browser F11 nie powinny zmieniać profilu AI jak gra. **Dowód:** macierz windowed/borderless/exclusive, wiele monitorów/DPI, Alt-Tab, gra w tle, wideo i prezentacje; ręczny override zawsze działa i nic nie zmienia priorytetów Windows bez decyzji.

### 3.8 ASR PL: porównanie profili zamiast automatycznej wymiany — P0 benchmark M, integracja L

**Źródło:** Parakeet v3 model card: 25 języków w tym PL, CC-BY-4.0, **7,31% WER FLEURS pl_pl test i 7,28% MLS polish test** [S19]. Sherpa v1.13.5 zawiera skrypty eksportu v3 [S20]. Whisper.net opisuje opcjonalne runtime CUDA/Vulkan upstream [S21]; zgodność konkretnego pakietu **1.9.1** i środowiska jeszcze do testu.

**Korekta baseline'u:** 14,7% Whisper Small, 4,7% Large v3, „10× szybciej” i średnie dla Qwen 1.7B z oryginalnego zestawienia nie są pomiarami Sentinela. Nasz Qwen to **0.6B INT8**, a Large v3 i Large v3 Turbo to różne modele. Nie przenosić WER, RTFx ani wyników macOS/GPU na Windows/CPU/laptop.

**Gdzie:** `VoiceModelManager.cs`, `VoiceRecognitionService.cs`, `Qwen3AsrService.cs`, `WhisperFallbackAsrService.cs`, nowy adapter Parakeet, `AsrTestRunner.cs`, planowany korpus `tests/asr-pl/`.

**Plan:** baseline aktualnego Qwen/Whisper, następnie Parakeet v3 INT8 jako **kandydat**, nie z góry domyślny model słabego CPU. Mierzyć warm/cold, WER, exact intent+args, RTF, pamięć i wpływ gry. Przy NVIDIA osobno porównać rzeczywiście dostępny model Whisper i runtime; nie pobierać większego modelu ani CUDA automatycznie po wykryciu producenta karty. Zachować wybór użytkownika i działający fallback do czasu przejścia bramek.

**Ryzyko:** pamięć dużego transducera, uszkodzone eksporty, brak native operatora/provider, runtime conflict, latency podczas gry, gorsze rozpoznanie negacji mimo lepszego średniego WER. **Dowód:** §2, test rzeczywistego modelu na Windows x64; brak GPU/provider przechodzi do jawnego kompatybilnego profilu. Usunięcie Whisper Small z domyślnych instalacji wymaga własnych wyników i planu migracji — teraz decyzja wstrzymana.

### 3.9 Rejestr narzędzi i MCP — P0 faza A M / P1 fazy B–C L–XL

**Źródło:** oficjalny SDK to **`modelcontextprotocol/csharp-sdk`**, współtworzony z Microsoft, nie fork `twenzel/modelcontextprotocol`. Zweryfikowano stabilne wydanie **v2.2.0 z 2026-08-13** [S22]. Teza „SDK wyłącznie preview, przypiąć 0.6.0-preview.1” jest nieaktualna. Wersję implementacyjną dobrać po sprawdzeniu TFM i zależności .NET/Whisper/Ollama; brak automatycznego upgrade'u w tym planie.

**Gdzie:** `SentinelToolboxService.cs`, routery i ActionEngine; nowe `Services/Tools/ToolRegistry.cs`, schematy argumentów i opcjonalny host/klient MCP; `App.xaml.cs` / osobny host procesu do decyzji.

- **A — wspólny kontrakt:** nazwa, opis, schema, ryzyko ustalane przez host, walidator, executor, verifier/postcondition, cancellation, timeout, źródło i korelacja. Jedna implementacja jest używana przez komendy tekstowe, propozycje LLM i przyszły MCP. Przeczytanie `success:true` od LLM/serwera nie staje się lokalnym VERIFIED.
- **B — read-only stdio:** domyślnie wyłączony, jawny allowlist odczytów. Na tym etapie **nie eksponować narzędzi mutujących**. Sprawdzić współistnienie WinExe/WPF, single instance i stdio; protokół na stdout, logi wyłącznie poza stdout. Odczyt plików/historii może ujawniać prywatne dane — read-only nie znaczy bez uprawnień. Ewentualne mutacje dopiero w osobnym etapie z interaktywną zgodą; bez dostępnego UI zgody odmowa.
- **C — klient:** tylko jawnie dodane lokalne serwery, zatwierdzony plik executable/argumenty/zakresy, minimalne środowisko i limity. Sam transport MCP i lokalność nie sandboxują procesu. Opisy/wyniki narzędzi to niezaufane dane, nie instrukcje. Zmiana narzędzi/serwera unieważnia cache zgód. Remote MCP osobna decyzja, poza tym etapem.

**Dlaczego / ryzyko:** mniejsza duplikacja i możliwość rozszerzania, ale znacznie większa powierzchnia uprawnień i prompt injection. **Dowód:** contract tests wszystkich adapterów, brak uruchomienia niezaufanego serwera, brak mutujących tools w fazie B, timeout/crash klienta, odmowa bez UI, brak duplikacji efektów, brak automatycznego VERIFIED z cudzej deklaracji; zachowany single-lane i STOP.

## 4. Backlog zbiorczy i kolejność

| ID / obszar | Priorytet | Koszt | Zależność / dowód / stan |
|---|---|---|---|
| 3.0 — L16 zgody i pochodzenie | P0 | M | Pierwszy etap, izolowany test kasowania pamięci; **plan naprawy, nie naprawione** |
| 3.0 — L14 testy, metryki, korpus | P0 | M–L | Przed porównaniami silników; nie zastępuje smoke EXE |
| 3.0 — L17 manifesty/licencje modeli | P0 | M | Przed dystrybucją nowego KWS/TTS/ASR |
| 3.9A — registry + 3.4 aliasy/dowody | P0 | M | Zanim LLM może proponować narzędzia |
| 3.3A — streaming tekstu | P0 | M | Niezależny przyrost; bez narzędzi LLM |
| 3.1 — KWS | P0 | S spike + M–L | PL, latencja chunków i CPU zweryfikowane przed wyborem |
| 3.2 — TTS; barge-in osobno | P0 | M–L | Manifest/licencje, playback; pełny duplex dopiero po testach echa |
| 3.8 — wybór ASR | P0 | M benchmark + L | Dopiero po korpusie; zachować istniejący fallback |
| 3.3B/C — schemat/native tool calls | P0 | M + M | Po §3.0 i registry; najpierw read-only |
| 3.4 — semantyczny router | P1 | M–L | Próg/abstencja + testy argumentów i negacji |
| 3.5 — GPU | P1 | M | Zakresy API, multi-adapter, sprzęt |
| 3.6 — PresentMon | P1 | L | SDK/usługa, metryki per-frame, narzut i zgodność gier |
| 3.7 — detekcja gier | P1 | M | False positives fullscreen i ręczny override |
| 3.9B/C — MCP | P1 | L–XL | Uprawnienia, registry, zgodność SDK; domyślnie off |

Pozycje z pierwotnego audytu, które nie miały rozwinięcia, pozostają w backlogu — nie znikają:

| Luka | Następny eksperyment i miejsce | Dlaczego / ryzyko / źródło / dowód |
|---|---|---|
| L8 — speaker verification (P2, L) | Opcjonalny adapter w `Services/Voice/`, świadomy enrollment i usuwanie profilu. | Personalizacja, nie zastępstwo zgody. Replay/klonowanie głosu, FAR/FRR, dane biometryczne; KOD brak integracji. Konkretny model/licencja **DO WERYFIKACJI** w kolejnej rundzie. Test znanych/obcych mówców oraz nagrań; domyślnie off. |
| L9 — odszumianie (P1, M–L) | A/B `AudioEnhancementService.cs`: bypass/gate vs kandydat GTCRN/DPDFNet; bez domyślnej wymiany. | Może poprawić ASR lub niszczyć spółgłoski. KOD: istniejący gate/gain; model/provider/licencja do sprawdzenia. Dowód: ΔWER/latency/CPU na identycznym materiale. |
| L10 — aktualizacje (P1, M–L) | Nowy read-only update checker w `Services/Desktop/`, Settings, workflow wydawniczy. | Najpierw jawna informacja o podpisanym wydaniu, nie automatyczny installer. Później podpisy/autentyczność manifestu, atomic replace, rollback i zachowanie modeli/danych. KOD: obecna dystrybucja Actions. Framework aktualizacji nie wybrany; test podmienionego manifestu/offline/przerwanego update'u. |
| L11 — szukanie plików (P1, L) | Read-only indeks w `Services/Files/`, opt-in katalogi przez picker. | Realne wyniki z nazwą/ścieżką i źródłem, bez skanowania całego dysku. ACL, junction/symlink, prywatność, OCR treści jako osobny etap; źródło KOD obecnego workspace. Test recall na oznaczonym zbiorze i odmowy poza zakresem. |
| L12 — pamięć semantyczna (P1, L) | Adapter w `Services/Memory/` nad `ConversationMemoryService.cs`, index przez `AppPaths.MemoryDirectory`. | Fakty z datą/proweniencją, możliwość poprawy/usunięcia również embeddingów. Nie przyjmować odpowiedzi modelu jako faktu o użytkowniku. Źródło KOD + kandydat embeddingów [S14]; test recall/precision, konfliktów i braku wyników po usunięciu. |

**Proponowana kolejność:** zgody i testy → baseline/licencje/registry → streaming tekstu → równoległe, ograniczone spike'i KWS/TTS/ASR → wybór po pomiarach → propozycje narzędzi → semantyka/telemetria/gaming → MCP. Nie wdrażać całego P0 w jednym nieodwracalnym PR. Każdy etap ma feature flag lub bezpieczny fallback i oddzielny build/test Windows.

## 5. Log decyzji

| Pomysł | Decyzja po audycie | Powód |
|---|---|---|
| Porcupine jako domyślne KWS | Niewybrane | Projekt preferuje brak klucza/usługi. Aktualnych warunków licencji i pracy offline nie zweryfikowano tutaj; nie twierdzimy, że klucz oznacza przesyłanie audio. |
| Kokoro-82M domyślnie | Wstrzymane | Brak zweryfikowanego PL dla konkretnego modelu; nie jest to twierdzenie o wszystkich przyszłych wariantach. |
| Supertonic / upgrade sherpa z PyPI | Wstrzymane | Wersja Python/ogólna lista funkcji nie dowodzi API/native runtime naszej paczki NuGet. |
| LibreHardwareMonitor jako default | Nie | Sterownik/uprawnienia i szersza powierzchnia zaufania; opcja zaawansowana wymaga osobnego audytu. |
| Hook/injection w grę | Nie | Preferowany zewnętrzny klient PresentMon; bez obietnicy uniwersalnego bezpieczeństwa anti-cheat. |
| LLM sam zatwierdza i wykonuje | Nie | Host ustala ryzyko i zasoby; zgoda, walidacja i weryfikacja są poza modelem. |
| 100% accuracy po dodaniu JSON Schema | Usunięte | Schemat ogranicza format, nie gwarantuje poprawnej intencji. |
| DXGI budget jako globalny VRAM | Odrzucone | Błędny zakres pomiaru [S15]. |
| Parakeet domyślnie na słabym laptopie | Wstrzymane | Potrzebne pomiary RTF/pamięci/WER na CPU, nie benchmark innej platformy. |
| MCP preview 0.6 jako jedyny wybór | Skorygowane | Jest stabilne upstream 2.2.0 [S22]; zgodność zależności w naszym projekcie nadal do sprawdzenia. |

## 6. Rejestr źródeł (dostęp 2026-09-22)

Źródła poniżej zostały odczytane w tym audycie (dokumentacja, model cards albo pliki/release przez GitHub API). Linki do `main`/`master` są zmienne; przed implementacją przypiąć revision/hash. Same modele i zależności runtime **nie były pobierane ani uruchamiane**.

| ID | Źródło pierwotne | Co potwierdza / ograniczenie |
|---|---|---|
| S1 | [sherpa KWS][S1] | Open vocabulary, tokenizer, boosting score i threshold; brak gwarancji PL. |
| S2 | [KWS zh-en 3M][S2] | Konkretny model, pliki, sample tokens, latency chunk-8/16 160/320 ms. |
| S3 | [KeywordSpotter.cs v1.13.5][S3] | Dostępny binding .NET; nie test binarny paczki. |
| S4 | [openWakeWord README][S4] | Apache-2.0 kodu, CC-BY-NC-SA-4.0 dołączonych modeli, wieloetapowy frontend. |
| S5 | [sherpa VITS/TTS][S5] | Obsługa VITS/Piper, wymagania paczek. |
| S6 | [sherpa tts-models][S6] | API release zawiera polskie paczki Gosia, Darkman, mc_speech, warianty kwantyzacji. |
| S7 | [Gosia MODEL_CARD][S7] | Polski, 22050 Hz, dataset CC0; nie pełny audyt redystrybucji. |
| S8 | [OfflineTts.cs v1.13.5][S8] | Generate, speed, callback i zasoby native. |
| S9 | [Piper COPYING][S9] | Licencja aktualnego projektu piper1-gpl. |
| S10 | [sherpa espeak-ng CMake v1.13.5][S10] | Integracja fonemizatora; licencje wynikowego builda do sprawdzenia. |
| S11 | [Ollama chat][S11] | tools, format, stream, think, keep_alive; wsparcie zależy od modelu. |
| S12 | [Ollama show][S12] | capabilities i metadata przez `/api/show`. |
| S13 | [.NET JSON schema exporter][S13] | Eksport schematu od .NET 9, nie engine autoryzacji. |
| S14 | [multilingual-e5-small card][S14] | PL i MIT w metadanych; preprocessing/eksport ONNX do dalszej weryfikacji. |
| S15 | [DXGI_QUERY_VIDEO_MEMORY_INFO][S15] | `CurrentUsage` aplikacji, budżet aplikacji. |
| S16 | [NVIDIA nvml.h][S16] | API device memory i zastrzeżenia WDDM/TCC; brak testu karty. |
| S17 | [PresentMon Service/SDK][S17] | ETW, usługa, API2, loader, ograniczenie kompatybilności DLL. |
| S18 | [QUERY_USER_NOTIFICATION_STATE][S18] | Busy/presentation/exclusive D3D; nie detektor tożsamości gry. |
| S19 | [NVIDIA Parakeet v3 card][S19] | Języki/licencja i dokładne benchmarki PL z przypisanym zbiorem. |
| S20 | [sherpa Parakeet v3 v1.13.5][S20] | Skrypty eksportu; nie dowód RTF na Windows. |
| S21 | [Whisper.net readme][S21] | Istnienie opcjonalnych runtime; wymagania `main` nie są gwarancją dla przypiętego 1.9.1. |
| S22 | [MCP SDK v2.2.0][S22] | Oficjalny stabilny release 2026-08-13; nie decyzja o wersji zależności Sentinela. |

Materiały wtórne z propozycji zachowane jako tropy, **nie podstawy progów ani potwierdzone wyniki produktu**: [WhisperNotes](https://whispernotes.app/blog/parakeet-v3-default-mac-model), [SpeechData](https://www.speechdata.ai/blog/best-asr-models-2026), [ASR Leaderboard arXiv](https://arxiv.org/html/2510.06961v4) (wymaga odczytu protokołu/konfiguracji), [Cohorte](https://cohorte.co/blog/using-ollama-with-python-step-by-step-guide), [PromptQuorum](https://www.promptquorum.com/power-local-llm/build-local-voice-assistant-2026), [InnerZero](https://innerzero.com/blog/best-open-source-local-ai-voice-assistant-jarvis-2026), [MangoHud-Windows](https://github.com/Leclowndu93150/MangoHud-Windows). Nie weryfikowano w tej rundzie każdej liczby ani wszystkich twierdzeń tych materiałów. Priorytet mają dokumentacja autorów i własny eksperyment.

[S1]: https://k2-fsa.github.io/sherpa/onnx/kws/index.html
[S2]: https://k2-fsa.github.io/sherpa/onnx/kws/pretrained_models/index.html
[S3]: https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.5/scripts/dotnet/KeywordSpotter.cs
[S4]: https://github.com/dscripka/openWakeWord/blob/main/README.md
[S5]: https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/vits.html
[S6]: https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models
[S7]: https://huggingface.co/rhasspy/piper-voices/blob/main/pl/pl_PL/gosia/medium/MODEL_CARD
[S8]: https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.5/scripts/dotnet/OfflineTts.cs
[S9]: https://github.com/OHF-Voice/piper1-gpl/blob/main/COPYING
[S10]: https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.5/cmake/espeak-ng-for-piper.cmake
[S11]: https://docs.ollama.com/api/chat
[S12]: https://docs.ollama.com/api-reference/show-model-details
[S13]: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema
[S14]: https://huggingface.co/intfloat/multilingual-e5-small/blob/main/README.md
[S15]: https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info
[S16]: https://github.com/NVIDIA/go-nvml/blob/main/pkg/nvml/nvml.h
[S17]: https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md
[S18]: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state
[S19]: https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3/blob/main/README.md
[S20]: https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.5/scripts/nemo/parakeet-tdt-0.6b-v3
[S21]: https://github.com/sandrohanea/whisper.net/blob/main/readme.md
[S22]: https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0

## 7. Historia rund i następny przegląd

### Runda 1 — propozycja użytkownika, 2026-09-20

Kierunki: KWS, PL TTS, streaming/schematy, semantyczny routing, GPU, PresentMon, detekcja gier, ASR i MCP. Zachowano identyfikatory 3.1–3.9 oraz L1–L15.

### Rewizja 1.1 — audyt kodu i źródeł, 2026-09-22

- Skorygowano tor głosu, model Qwen 0.6B, niepodłączone klasy oraz istniejące aliasy i `think=false`.
- Dodano L16 (potwierdzenie kasowania pamięci) i L17 (manifest modeli); nie przedstawia się bezpieczeństwa jako kompletnego dowodu uniwersalnego.
- Oddzielono latencję KWS od przejścia aplikacji, budżet DXGI od globalnego VRAM, benchmark modelu od naszego baseline'u.
- Poprawiono oficjalny SDK MCP, status stabilnego wydania i licencje openWakeWord; dodano audyt fonemizatora/głosów.
- Zastąpiono obietnicę 100% routingu walidacją fail-closed, dodano protokół korpusu, cold/warm, FAR/FRR i odsłuch TTS.
- Rozdzielono streaming tekstu, schemat propozycji i native tool calling. Dopisano źródło zgody, replay, privacy i niedopuszczanie niepełnych argumentów.
- Uzupełniono backlog pominiętych L8–L12, koszty integracji i zależności etapów. Nie zmieniono kodu działania aplikacji ani paczek NuGet.

### Następna runda — zaplanowana, niewykonana

1. W izolowanym teście odtworzyć L16, uzgodnić politykę operacji pamięci i zakres pierwszej implementacji P0.
2. Zatwierdzić sprzęt referencyjny, licencję i właściciela korpusu, rozdział train/calibration/test oraz progi §2.
3. Sprawdzić konkretne paczki KWS/Piper/Parakeet i licencje całego łańcucha oraz hashe z zaufanego źródła.
4. Spike ABI Windows sherpa 1.13.5, Whisper 1.9.1 CUDA/Vulkan i kandydackiego MCP; niczego nie aktualizować wyłącznie na podstawie numeru najnowszego wydania.
5. Ustalić per-adapter źródło device-wide VRAM, obsługę NVML/WDDM oraz uprawnienia/ABI wybranego PresentMon SDK.
6. Każdą zaakceptowaną pozycję zamienić w mały etap z testem, fallbackiem i wynikiem Windows CI. Dopiero zmierzony rezultat zmienia status z „plan” na „dostarczone”.
