# SENTINEL X — Plan rozwoju oparty na researchu (living document)

> Status: **plan**, nie implementacja. Każda pozycja ma źródło, uzasadnienie „dlaczego to jest lepsze", miejsce w kodzie, ryzyko i priorytet.
> Zasada nadrzędna projektu zostaje: **żadnych udawanych pomiarów, żadnego „VERIFIED" bez dowodu, LLM nie wykonuje dowolnych narzędzi bez bramki zgód.**
> Data ostatniej aktualizacji: 2026-09-20. Rundy researchu: 1–6 (sekcje 12–15 = konsolidacja).

**Spis treści:** [0. Jak czytać](#0-jak-czytać-ten-plan) · [1. Stan obecny](#1-stan-obecny-audyt-repo-084) · [2. Mierzalne cele](#2-mierzalne-cele-definicja-lepiej-niż-reszta-githuba) · [3. Runda 1](#3-runda-1--wyniki-researchu-i-decyzje) · [7. Runda 2](#7-runda-2--automatyzacja-pulpitu-konkurencja-z-githuba-npu-aktualizacje-weryfikacja-mówcy) · [8. Runda 3](#8-runda-3--pamięć-pliki-koniec-tury-wizja-dobór-modeli-testy) · [9. Runda 4](#9-runda-4--weryfikacja-techniczna-asr-po-polsku-w-sherpa-onnx-gpu-dla-whispera-full-duplexaec-app-actions-podpisywanie) · [10. Runda 5](#10-runda-5--model-bezpieczeństwa-na-poziomie-lepszy-niż-prompt-prywatność-powiadomienia) · [11. Runda 6](#11-runda-6--potwierdzenie-wykonalności-w-net-jedna-zależność-wiele-funkcji-i-tts-premium) · **[12. Mapa drogowa](#12-skonsolidowana-mapa-drogowa-po-6-rundach)** · [13. Porównanie z GitHubem](#13-tablica-porównawcza--gdzie-po-realizacji-planu-sentinel-wygrywa-z-projektami-z-githuba) · [14. Ryzyka](#14-rejestr-ryzyk) · [15. Otwarte pytania](#15-otwarte-pytania-na-kolejne-rundy)

**Jak powstaje ten dokument:** pętla *szukaj (GitHub, dokumentacja, benchmarki) → oceń względem stanu repo → dopisz z uzasadnieniem i źródłem → szukaj dalej*. Każda runda ma własną tabelę backlogu i listę źródeł; sekcja 12 scala wszystko w kolejność wydań.

---

## 0. Jak czytać ten plan

| Pole | Znaczenie |
|---|---|
| **P0** | fundament — bez tego reszta nie ma sensu (latencja, jakość ASR/TTS po polsku, bezpieczeństwo akcji) |
| **P1** | wyraźna przewaga nad typowymi asystentami z GitHuba |
| **P2** | „wow", ale dopiero po P0/P1 |
| **Koszt** | S (≤1 dzień), M (2–5 dni), L (1–3 tyg.), XL (>3 tyg.) |
| **Gdzie** | plik/serwis w repo, który ma się zmienić |
| **Dowód** | jak zmierzymy, że jest lepiej (żadne „działa u mnie") |

Kryterium „lepiej niż wszystko na GitHubie" jest tu przetłumaczone na **mierzalne cele** (sekcja 2), a nie deklarację.

---

## 1. Stan obecny (audyt repo, 0.84)

Co już jest i jest dobre (nie ruszamy bez powodu):

- WPF/.NET 9, MVVM, walidowany DI (`Core/ServiceLocator.cs`), 8 stron, paleta Ctrl+K, panel gotowości.
- **Bezpieczeństwo akcji**: jedna kolejka, `PermissionCenterService` (jedna akcja oczekująca), `ActionHistoryService` + dowody (`Services/History/ActionEvidenceCapture.cs`), `requestId`, brak statusu Verified bez dowodu, EMERGENCY STOP (Ctrl+Shift+X).
- **Głos**: NAudio → własny gain/noise-gate (`AudioEnhancementService.cs`) → Silero VAD (`SileroVadService.cs`) → ASR offline: **Qwen3-ASR przez sherpa-onnx 1.13.5** (`Qwen3AsrService.cs`) z fallbackiem **Whisper Small przez Whisper.net 1.9.1** (`WhisperFallbackAsrService.cs`) → normalizacja polska (`PolishTextNormalizer.cs`) → maszyna stanów STANDBY/ACTIVE (`VoiceStateMachine.cs`).
- **AI**: Ollama 127.0.0.1:11434, OllamaSharp 5.4.18 do listy modeli, własny transport HTTP z retry/timeout (`LocalAiService.cs`), `keep_alive` krótszy w trybie gry, `num_ctx` obniżany w trybie gry.
- **Routing**: deterministyczne narzędzia najpierw (`Services/Intent/IntentRouter.cs` → `SentinelToolboxService` → `CommandRouter`), LLM na końcu.
- **System**: CPU z `GetSystemTimes`, RAM z `GlobalMemoryStatusEx`, GPU z liczników PDH; NaN zamiast fałszywego zera.
- **CI**: `windows-build.yml` = build + `--ui-smoke` + `--self-test` + paczka portable self-contained + smoke paczki.

Zidentyfikowane luki (to jest paliwo dla planu):

| # | Luka | Skutek dla użytkownika |
|---|---|---|
| L1 | Wake word = dopasowanie tekstu **po** ASR (`WakeWordService.cs`, `CommandText.StripWakeWord`) | ASR mieli każdą wypowiedź w pokoju → CPU, opóźnienie, fałszywe wybudzenia, prywatność |
| L2 | TTS = `System.Speech` (SAPI 5) — brak dobrego polskiego głosu na czystym Windows | asystent „mówi po polsku z angielskim głosem" lub wcale |
| L3 | Ollama wołana ze `stream=false`, bez `tools`, bez `format` (JSON schema), bez `think` | użytkownik czeka na cały tekst; intencje LLM parsowane heurystycznie |
| L4 | Intencje odczytów = sztywne stringi (`case "ile mam ram"`) | „ile mam ramu?", „pokaż pamięć" → spada do LLM lub nic |
| L5 | VRAM: „niedostępne"; brak temperatur | panel systemowy słabszy niż darmowe narzędzia |
| L6 | Gaming: FPS/latencja „nie udajemy" — ale też nie mierzymy | overlay bez najważniejszej metryki gracza |
| L7 | Wykrywanie gry = lista 13 nazw procesów | nowa gra = brak trybu gry |
| L8 | Brak weryfikacji mówcy | każdy w pokoju może wydawać komendy |
| L9 | Brak redukcji szumu modelem (tylko gate/gain) | gorsze WER przy wentylatorach/klawiaturze |
| L10 | Brak aktualizacji aplikacji (ZIP z Actions) | użytkownik nie wie, że jest nowa wersja |
| L11 | Szukanie plików tylko w `CreatedFiles` | „znajdź fakturę z marca" nie działa |
| L12 | Pamięć rozmowy = plik JSON bez wyszukiwania semantycznego | asystent nie pamięta faktów sprzed tygodnia |
| L13 | Brak standardu narzędzi (każde narzędzie = własny kod w `SentinelToolboxService`) | trudne rozszerzanie, brak ekosystemu |
| L14 | Testy = runnery w EXE; brak izolowanych testów jednostkowych/UI | wolniejsze iteracje, regresje w logice głosu |
| L15 | Whisper Small (≈14,7 % WER PL) jako fallback | polskie komendy z błędami |

---

## 2. Mierzalne cele (definicja „lepiej niż reszta GitHuba")

| Metryka | Dziś (szacunek) | Cel 1.0 | Jak mierzymy |
|---|---|---|---|
| Wake→gotowość nasłuchu | ASR każdej wypowiedzi | < 150 ms, CPU idle < 3 % jednego rdzenia | licznik w `VoiceSegmentMetrics`, log JSONL |
| Fałszywe wybudzenia | nieznane | < 1 / 8 h przy TV/rozmowie | test 8 h nagrania „szum domowy" w `--asr-test` |
| WER polski (komendy, mikrofon 1 m) | Whisper Small ≈ 14,7 % WER PL w benchmarkach ogólnych | ≤ 6 % na własnym zestawie 200 komend | `--asr-test` z korpusem `tests/asr-pl/` |
| Czas od końca mowy do pierwszego słowa TTS | całość odpowiedzi → TTS | < 900 ms dla odpowiedzi narzędziowych, < 1,8 s dla LLM (pierwsze zdanie) | stempel czasu w `ActionHistoryService` |
| Trafność routingu intencji (200 komend PL) | sztywne stringi | ≥ 95 % bez LLM, 100 % z fallbackiem LLM + schemat | test regresji `tests/ProductRegression.cs` |
| Akcje ryzykowne bez zgody | 0 (dziś) | 0 (**utrzymać** — to przewaga) | testy smoke: fałszywe „VERIFIED", głosowe „tak" |
| VRAM / temp. GPU | brak | używane/całkowite VRAM bez admina; temp. gdy dostępne | `SystemMonitor` + panel |
| Prawdziwe FPS w overlayu | brak | frametime/FPS z ETW (PresentMon) gdy usługa dostępna; inaczej jawne „brak" | overlay + log |

---

## 3. Runda 1 — wyniki researchu i decyzje

### 3.1 Głos: wake word jako prawdziwy KWS zamiast ASR całego pokoju — **P0, M**

**Co znaleziono.** sherpa-onnx (już zależność projektu) ma **open-vocabulary keyword spotting**: mały streamingowy Zipformer (3M parametrów), słowa kluczowe definiowane w pliku tekstowym bez retrenowania; model `sherpa-onnx-kws-zipformer-zh-en-3M-2025-12-20` (zh+en) ([dokumentacja KWS](https://k2-fsa.github.io/sherpa/onnx/kws/index.html)). Projekty takie jak qwen-audio-agent używają tego 3M modelu jako lokalnego wake word: aplikacja śpi, mikrofon otwarty, fraza budzi sesję bez chmury ([README sherpa-onnx](https://github.com/csukuangfj/sherpa-onnx)). Alternatywy: **openWakeWord** (MIT, własne słowa przez fine-tuning; standard w społeczności Home Assistant) ([przegląd 2026](https://www.promptquorum.com/power-local-llm/build-local-voice-assistant-2026)); **Porcupine** (komercyjny, darmowy tier osobisty) — odrzucony (licencja, klucz online).

**Dlaczego to jest lepsze.** Zamiast dekodować mowę całego pokoju przez Qwen3-ASR/Whisper, ciągle działa tylko 3M KWS; ASR startuje dopiero po detekcji. Mniej CPU w trybie gry, mniej fałszywych wybudzeń, mniej danych w logach.

**Plan w kodzie.**
- Nowy `Services/Voice/KeywordSpotterService.cs` na `SherpaOnnx.KeywordSpotter` (stream 16 kHz, `keywords.txt` z tokenami dla „sentinel" — model zh-en koduje angielskie fonemy ARPAbet, więc „SENTINEL" da się zapisać; polską wymowę „sentinel" zapisujemy jako dodatkowy wariant fonemów, z osobnym `#threshold`).
- `VoiceStateMachine`: STANDBY = tylko KWS + VAD; po detekcji → ACTIVE (ASR). Tryby `Strict/Balanced/Forgiving` z `VoiceRuntimeOptions.WakeWordMode` mapujemy na próg KWS.
- Zachować tekstowy fallback `WakeWordService` (gdy model KWS niepobrany) — panel gotowości ma to pokazać jawnie.
- Metryki: liczba detekcji, fałszywe (użytkownik anulował w < 2 s), średni czas do ACTIVE.

**Ryzyko.** Model KWS zh-en nie zna polskich fonemów; „Sentinel" jest angielskie, więc OK, ale własne polskie frazy będą gorsze — wtedy openWakeWord z własnym treningiem (osobny ONNX; brak oficjalnego bindingu .NET, ale to zwykły ONNX Runtime + melspektrogram).

### 3.2 TTS: lokalny neuronowy głos polski zamiast SAPI — **P0, M**

**Co znaleziono.** sherpa-onnx ma offline TTS: **Piper (VITS)**, Matcha, Kokoro, ZipVoice, Pocket TTS, a od 2026 także **Supertonic** z poprawką na diakrytyki dla polskiego i innych języków łacińskich (#3750) ([PyPI sherpa-onnx 1.13.8](https://pypi.org/project/sherpa-onnx/)). Piper ma gotowe polskie głosy — `pl_PL-darkman-medium`, `pl_PL-gosia-medium`, `pl_PL-mc_speech-medium` (22,05 kHz) i `pl_PL-mls_6892-low` — publikowane w `rhasspy/piper-voices` na Hugging Face (model `.onnx` + `.onnx.json`); głos darkman jest dostrojony na zbiorze CC0 ([lista głosów Piper](https://github.com/rhasspy/piper/blob/master/VOICES.md), [karta modelu darkman](https://huggingface.co/rhasspy/piper-voices/blob/main/pl/pl_PL/darkman/medium/MODEL_CARD), [pełna lista z URL-ami](https://docs.gladecore.com/files/piper-voice-models)); działają na CPU w czasie rzeczywistym. Kokoro-82M jest lepszy brzmieniowo, ale nie ma polskiego; Piper jest szybszy i mniejszy, choć brzmi bardziej syntetycznie ([porównanie](https://innerzero.com/blog/best-open-source-local-ai-voice-assistant-jarvis-2026)).

**Dlaczego to jest lepsze.** Jedna zależność (już mamy) daje polską mowę na każdym Windows bez instalowania pakietów językowych; opóźnienie kilkadziesiąt ms na zdanie.

**Plan w kodzie.**
- `SpeechOutputService` → interfejs `ITtsEngine` z dwiema implementacjami: `SapiTtsEngine` (dziś) i `SherpaTtsEngine` (`OfflineTts` + Piper `pl_PL-*`), odtwarzanie przez NAudio `WaveOutEvent`.
- **Streaming zdaniami**: TTS syntetyzuje pierwsze zdanie, gdy tylko LLM je domknie (patrz 3.3), reszta w kolejce; barge-in: detekcja mowy użytkownika (VAD) przerywa odtwarzanie.
- `VoiceModelManager`: pobieranie głosu Piper (≈60–75 MB) jawnie, jak dziś ASR; SHA-256 i licencja w manifeście.
- Ustawienia: głos, tempo (`length_scale`), głośność; test „Powiedz zdanie próbne".

**Dowód.** Test `--asr-test` rozszerzony o round-trip: TTS → nasze ASR → porównanie tekstu (self-consistency), plus pomiar czasu do pierwszej próbki audio.

### 3.3 LLM: streaming + structured outputs + tool calling przez bramkę zgód — **P0, M**

**Co znaleziono.** Ollama API: `format` przyjmuje pełny **JSON Schema** (structured outputs, od grudnia 2024), `stream` domyślnie `true`, `think` może być bool lub poziom `"low"/"medium"/"high"/"max"`, `keep_alive` steruje wyładowaniem ([docs.ollama.com/api/generate](https://docs.ollama.com/api/generate)). Od maja 2025 streaming i tool calling działają w jednym wywołaniu; tool calling wymaga modeli z tagiem *tools* (np. llama3.1, gemma4, qwen3.x); zalecenia z oficjalnej biblioteki: rzutować argumenty (model zwraca „10" zamiast 10) i `temperature=0` przy walidacji schematem ([przegląd zmian API](https://cohorte.co/blog/using-ollama-with-python-step-by-step-guide)).

**Dlaczego to jest lepsze.** (a) Pierwsze zdanie odpowiedzi trafia do TTS po kilkuset ms zamiast po całej generacji. (b) Zamiast heurystyk tekstowych LLM zwraca intencję w **naszym schemacie** (`{"tool":"launch_app","args":{"name":"kalkulator"},"confidence":0.93}`) — a wykonanie nadal przechodzi przez `PermissionCenterService` i `ActionEngine`. LLM proponuje, nie wykonuje — to zachowuje obecną zasadę projektu, tylko z niezawodnym formatem.

**Plan w kodzie.**
- `LocalAiService`: tryb `stream=true` (NDJSON), agregacja `message.content`/`message.thinking`/`message.tool_calls`; anulowanie przez token (już jest).
- Nowy `Services/AI/IntentSchema.cs` — schemat JSON generowany z C# rekordów (`System.Text.Json` schema exporter w .NET 9: `JsonSchemaExporter`).
- Routing 3-stopniowy: (1) dokładne narzędzia jak dziś → (2) klasyfikator lokalny (3.4) → (3) LLM z `format=<schemat>` i `temperature=0`; wynik zawsze walidowany, nieznane narzędzie = odmowa, nie „spróbuj".
- `think`: włączony tylko dla zadań „zaplanuj", wyłączony dla komend (latencja); poziom sterowany z ustawień.
- Mapa zdolności modelu (tools/thinking/vision) pobierana z `/api/show` przy wyborze modelu; UI pokazuje, czego dany model nie potrafi, zamiast cicho degradować.

**Ryzyko.** Małe modele halucynują argumenty — dlatego walidacja schematem + lista dozwolonych narzędzi + próg pewności + bramka zgód na ryzykowne akcje.

### 3.4 Routing intencji: warstwa semantyczna przed LLM — **P1, M**

**Co znaleziono.** Wzorzec „deterministyczne narzędzia → mały klasyfikator → LLM" jest standardem w projektach agentowych; lokalne embeddingi przez Ollama (`/api/embed`) lub ONNX (wielojęzyczne modele e5/bge) pozwalają dopasować „ile mam ramu?", „pokaż pamięć", „zajętość RAM" do jednego narzędzia bez LLM.

**Plan w kodzie.**
- `Services/Intent/SemanticIntentIndex.cs`: lista przykładów po polsku dla każdego narzędzia (z `Core/CommandCatalog.cs`), embeddingi liczone raz i cache'owane w `%LOCALAPPDATA%\SentinelX\Memory\intent-index.json`; próg podobieństwa + margines między top-1 a top-2; poniżej progu → LLM (3.3).
- Model embeddingów: mały wielojęzyczny (do potwierdzenia w rundzie 2: e5-small/bge-m3 przez Ollama vs ONNX in-proc).
- Korpus testowy 200 poleceń PL z wariantami (literówki, brak diakrytyków, kolokwializmy) → cel ≥ 95 % bez LLM.

### 3.5 System: VRAM i telemetria GPU bez zgadywania — **P1, S–M**

**Co znaleziono.** **DXGI `IDXGIAdapter3::QueryVideoMemoryInfo`** daje użycie VRAM dla NVIDIA/AMD/Intel bez uprawnień administratora; MangoHud-Windows używa NVML dla NVIDIA (obciążenie, temperatura, VRAM, zegary, moc, throttling), ADL dla AMD i DXGI dla wszystkich jako uzupełnienie ([MangoHud-Windows](https://github.com/Leclowndu93150/MangoHud-Windows)). Temperatura CPU wymaga admina lub LibreHardwareMonitor (sterownik jądra).

**Plan w kodzie.**
- `SystemMonitor.GetVramInfo()` przez DXGI (COM interop w .NET, bez dodatkowych pakietów) — zastępuje „VRAM niedostępne" **tam, gdzie API odpowiada**; gdy nie, dalej NaN.
- Opcjonalny `NvmlGpuProbe` (ładowanie `nvml.dll` dynamicznie; brak = brak funkcji, nie błąd): temperatura, moc, throttling — pokazywane w panelu Gaming.
- LibreHardwareMonitorLib **nie** domyślnie (sterownik, admin); ewentualnie jako opcja „zaawansowane, wymaga admina" — decyzja w rundzie 2.

### 3.6 Gaming: prawdziwe FPS/frametime z PresentMon — **P1, M–L**

**Co znaleziono.** **Intel PresentMon** (open source) śledzi czasy klatek CPU/GPU/Display i latencje z ETW dla DirectX/OpenGL/Vulkan; PresentMon Service udostępnia dane przez PresentMon API/SDK (`PresentMonAPI2Loader.dll`), łącznie z telemetrią GPU ([README-Service](https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md), [opis komponentów](https://github.com/millfi/PresentMon)).

**Dlaczego to jest lepsze.** Zamiast „nie udajemy FPS" pokazujemy **prawdziwe** FPS/1 % low/frametime, z jawnym źródłem (ETW) i jawnym „brak" gdy usługa nie działa. Żaden hook w procesie gry (bez ryzyka anti-cheat).

**Plan w kodzie.**
- `Services/Gaming/PresentMonClient.cs`: P/Invoke do `PresentMonAPI2Loader.dll`, jeśli zainstalowany PresentMon Service; alternatywnie własny konsument ETW `Microsoft-Windows-DxgKrnl` (wymaga admina — tylko jako opcja).
- Overlay: FPS, 1 % low, frametime sparkline, VRAM (3.5). Pomiar aktualizowany 1×/s w grze (dziś: „wolniejsze pomiary").
- Panel gotowości: „PresentMon: nie wykryto — pobierz z GitHub" (link, bez auto-instalacji).

### 3.7 Gaming: wykrywanie gry bez listy nazw — **P1, S**

**Plan w kodzie.** Trzy sygnały zamiast jednego: (1) `SHQueryUserNotificationState` = `QUNS_RUNNING_D3D_FULL_SCREEN`/`QUNS_BUSY`; (2) okno pierwszoplanowe pełnoekranowe/borderless na monitorze; (3) `HKCU\Software\Valve\Steam\RunningAppID` ≠ 0 i analogiczne dla innych launcherów. Lista znanych gier zostaje jako sygnał 4. Wynik: „gra: tak (źródła: 2/4)" — jawnie.

### 3.8 ASR: lepszy polski model w tej samej bibliotece — **P0, S–M**

**Co znaleziono.** Benchmark 25 języków: Whisper Small **14,7 % WER PL**, Whisper Large v3 **4,7 %**, Parakeet TDT 0.6B v3 **7,3 %** przy ok. 10× większej szybkości od Whisper Turbo ([porównanie Parakeet v3 vs Whisper](https://whispernotes.app/blog/parakeet-v3-default-mac-model)); Parakeet-TDT-0.6B-v3 obsługuje 25 języków europejskich, licencja CC-BY-4.0, najszybszy wielojęzyczny RTFx ([przegląd ASR 2026](https://www.speechdata.ai/blog/best-asr-models-2026)). Qwen3-ASR-1.7B: średnio 5,11 % WER na językach europejskich w benchmarku wielojęzycznym vs Whisper Large v3 4,81 % i Parakeet v3 4,81 % ([ASR Leaderboard, arXiv 2510.06961](https://arxiv.org/html/2510.06961v4)).

**Plan w kodzie.**
- Profil sprzętowy w `VoiceModelManager`: **CPU-only / słaby laptop** → Parakeet TDT 0.6B v3 int8 (sherpa-onnx, offline transducer) jako model komend; **GPU NVIDIA** → Whisper Large v3 Turbo przez Whisper.net z runtime CUDA (do zweryfikowania w rundzie 2: pakiety `Whisper.net.Runtime.Cuda`/`Vulkan`); Qwen3-ASR zostaje jako opcja jakościowa.
- Whisper Small przestaje być domyślnym fallbackiem (zostaje tylko dla zgodności starych instalacji).
- Własny korpus `tests/asr-pl/` (200 komend, 3 mówców, 2 poziomy szumu) i WER liczony w `--asr-test`.

### 3.9 Standard narzędzi: MCP (Model Context Protocol) — **P1, L**

**Co znaleziono.** Oficjalny **MCP C# SDK** (Microsoft, MIT): pakiety `ModelContextProtocol` (hosting + DI), `ModelContextProtocol.Core` (klient/niskopoziomowy serwer), `ModelContextProtocol.AspNetCore`; narzędzia rejestrowane atrybutami `[McpServerToolType]`/`[McpServerTool]`; projekt jest w preview i może wprowadzać zmiany łamiące ([NuGet 0.6.0-preview.1](https://www.nuget.org/packages/ModelContextProtocol/0.6.0-preview.1), [repo](https://github.com/twenzel/modelcontextprotocol)).

**Dlaczego to jest lepsze.** Narzędzia Sentinela (odczyty systemu, pliki, uruchamianie aplikacji) opisane raz jako MCP mogą być używane przez nasz LLM **i** przez zewnętrzne klienty (Claude Desktop, VS Code), a Sentinel może konsumować cudze serwery MCP (np. kalendarz) — nadal przez naszą bramkę zgód.

**Plan w kodzie.**
- Faza A (wewnętrzna): `SentinelToolboxService` → rejestr narzędzi z opisem JSON Schema (ten sam schemat karmi 3.3). Bez zależności na SDK.
- Faza B: `--mcp-server` (stdio) eksponuje **tylko** narzędzia odczytowe; akcje zmieniające stan wymagają zgody w oknie Sentinela (MCP nie omija PermissionCenter).
- Faza C: klient MCP dla zaufanych, lokalnych serwerów z listą dozwolonych w ustawieniach. Zależność przypięta do konkretnej wersji preview + test kompilacji w CI.

---

## 4. Backlog wg obszarów (skrót; szczegóły w sekcji 3 i kolejnych rundach)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Głos | KWS zamiast ASR całego pokoju | P0 | M | 1 |
| Głos | Neuronowy polski TTS (Piper via sherpa-onnx), streaming zdaniami, barge-in | P0 | M | 1 |
| Głos | Parakeet TDT 0.6B v3 / Whisper Large v3 Turbo wg profilu sprzętu | P0 | S–M | 1 |
| AI | Streaming Ollama + JSON Schema + tool calling przez bramkę zgód | P0 | M | 1 |
| AI | Semantyczny router intencji (embeddingi) | P1 | M | 1 |
| System | VRAM przez DXGI, NVML opcjonalnie | P1 | S–M | 1 |
| Gaming | PresentMon: FPS/frametime | P1 | M–L | 1 |
| Gaming | Wykrywanie gry z 4 sygnałów | P1 | S | 1 |
| Narzędzia | MCP (rejestr narzędzi → serwer → klient) | P1 | L | 1 |

---

## 5. Log decyzji (co odrzucono i dlaczego)

| Pomysł | Decyzja | Powód |
|---|---|---|
| Porcupine (Picovoice) jako wake word | odrzucone | licencja komercyjna, klucz dostępu; mamy KWS w sherpa-onnx |
| Kokoro-82M jako TTS domyślny | odrzucone na teraz | brak polskiego; wraca, gdy pojawi się polski głos |
| LibreHardwareMonitor domyślnie | wstrzymane | sterownik jądra + admin; tylko jako opcja zaawansowana |
| Hook w procesie gry (overlay in-game) | odrzucone | ryzyko anti-cheat; PresentMon działa z zewnątrz przez ETW |
| Autonomiczne wykonywanie narzędzi przez LLM | odrzucone (utrzymane z 0.84) | LLM proponuje w schemacie, ActionEngine + zgody wykonują |

---

## 6. Źródła rundy 1

- sherpa-onnx — funkcje (KWS, speaker verification, speech enhancement GTCRN/DPDFNet, TTS): https://github.com/csukuangfj/sherpa-onnx , https://pypi.org/project/sherpa-onnx/
- sherpa-onnx — keyword spotting: https://k2-fsa.github.io/sherpa/onnx/kws/index.html
- Ollama API (format/JSON schema, think, keep_alive): https://docs.ollama.com/api/generate
- Zmiany Ollama (streaming + tools, structured outputs): https://cohorte.co/blog/using-ollama-with-python-step-by-step-guide
- MCP C# SDK: https://www.nuget.org/packages/ModelContextProtocol/0.6.0-preview.1
- MangoHud-Windows (NVML/ADL/DXGI): https://github.com/Leclowndu93150/MangoHud-Windows
- PresentMon Service/SDK: https://github.com/GameTechDev/PresentMon/blob/main/README-Service.md
- Parakeet v3 vs Whisper, WER PL: https://whispernotes.app/blog/parakeet-v3-default-mac-model
- ASR Leaderboard (wielojęzyczny): https://arxiv.org/html/2510.06961v4
- Przegląd ASR 2026: https://www.speechdata.ai/blog/best-asr-models-2026
- Stack lokalnego asystenta 2026 (openWakeWord, Piper, Kokoro): https://www.promptquorum.com/power-local-llm/build-local-voice-assistant-2026 , https://innerzero.com/blog/best-open-source-local-ai-voice-assistant-jarvis-2026
- Głosy Piper pl_PL: https://github.com/rhasspy/piper/blob/master/VOICES.md , https://docs.gladecore.com/files/piper-voice-models

---

## 7. Runda 2 — automatyzacja pulpitu, konkurencja z GitHuba, NPU, aktualizacje, weryfikacja mówcy

### 7.1 Co robią najlepsi konkurenci (i co robimy lepiej)

| Projekt | Co ma wartościowego | Czego nie ma / gdzie wygrywamy |
|---|---|---|
| **Ari-VoiceCommand** (Python/PySide6) — wake word, wielojęzyczne STT/TTS, autonomiczna pętla agenta (plan → wykonaj → **weryfikuj OCR** → popraw), MCP, pluginy, **SKILL.md**, pamięć strategii, zdalne komendy z Telegrama przez ten sam pipeline ([repo](https://github.com/DO0OG/Ari-VoiceCommand)) | Python + venv (instalacja `setup.bat`), autonomiczne wykonywanie kodu Python/Shell przez LLM — dokładnie to, czego Sentinel **świadomie nie robi**. My: jedna paczka EXE, zgody i dowody, ale możemy wziąć: weryfikację OCR, skille, MCP |
| **WinVoice / windows_voice_assistant** (Python, sherpa-onnx) — KWS Zipformer zawsze włączony, **weryfikacja mówcy CAM++ z trzema poziomami uprawnień (pełny / gość / odrzuć)**, 3-stopniowy routing (reguły → mały model lokalny → LLM), biała lista narzędzi, **automatyczny snapshot przed operacją destrukcyjną z możliwością cofnięcia**, osobny głos dla gościa ([repo](https://github.com/luoxiaguwu9394/windows_voice_assistant)) | Half-duplex (ASR zatrzymane podczas TTS), brak AEC, brak pamięci między sesjami, „poniżej 7B tool-calling jest zbyt słaby". My: .NET in-proc (bez GIL/IPC), pełny audyt; bierzemy: weryfikację mówcy z poziomami, snapshot+cofnij, 3-stopniowy routing (już w planie 3.3/3.4) |
| **Nox** (Electron+Python) — Ollama (Gemma 4), faster-whisper, Kokoro, openWakeWord, rozpoznawanie muzyki ([topics openwakeword](https://github.com/topics/openwakeword?o=desc&s=updated)) | Electron + Python = ciężka instalacja; brak modelu zgód |
| **Microsoft UFO²** („Windows AgentOS") — HostAgent + AppAgent-y, **hybryda UIA + wizja** do wykrywania kontrolek, akcje GUI + natywne API (Win32/COM), spekulacyjne wykonanie wielu akcji (51 % mniej wywołań LLM), RAG z dokumentacji i śladów wykonań, wirtualny pulpit „picture-in-picture" bez przeszkadzania użytkownikowi; 27,9 % WAA vs 20,8 % Operator ([overview](https://microsoft.github.io/UFO/ufo2/overview/), [arXiv 2504.14603](https://arxiv.org/html/2504.14603v1)) | Wymaga GPT-4o/o1 (chmura), Python; nie jest asystentem głosowym. Bierzemy: **UIA jako główny kanał obserwacji i dowodów**, weryfikację po akcji, wykonanie na osobnym pulpicie dla długich zadań |

**Wniosek strategiczny.** Żaden z tych projektów nie łączy: (a) jednego natywnego EXE bez Pythona, (b) głosu po polsku z KWS i weryfikacją mówcy, (c) twardej bramki zgód z dowodami, (d) prawdziwej telemetrii gry. To jest nasza nisza — plan poniżej domyka brakujące elementy.

### 7.2 Weryfikacja mówcy i poziomy uprawnień głosowych — **P1, M**

**Co znaleziono.** sherpa-onnx ma ekstraktor embeddingów mówcy (modele 3D-Speaker ERes2Net/CAM++, WeSpeaker ResNet/ECAPA) i porównanie kosinusowe; typowe progi ≈ 0,45–0,6 przy embeddingach WeSpeaker ([speakeronnx](https://github.com/TigreGotico/speakeronnx), [sherpa-onnx docs](https://k2-fsa.github.io/sherpa/onnx/index.html)). WinVoice używa tego samego pomysłu: pełny / gość / odrzuć, gość nie może wykonywać akcji wrażliwych i słyszy inny głos.

**Plan w kodzie.**
- `Services/Voice/SpeakerGateService.cs`: rejestracja właściciela (3× zdanie, embeddingi uśrednione, zapis w `%LOCALAPPDATA%\SentinelX\Voice\owner.json`); przy każdej wypowiedzi po KWS: score + decyzja.
- Poziomy: **właściciel** (wszystko wg dotychczasowych zgód), **gość** (tylko odczyty: RAM/CPU/pogoda…; brak plików, brak zamykania aplikacji), **nieznany przy Strict** (ignoruj). Poziom widoczny w UI i w `ActionRecord` (nowe pole `SpeakerLevel`).
- Zgoda głosowa nadal **zabroniona** (utrzymujemy zasadę 0.84); weryfikacja mówcy nie zastępuje kliknięcia „Zatwierdź" dla akcji HIGH.
- Kalibracja progu na podstawie 20 próbek właściciela + 20 próbek „obcych" z TTS (Piper różne głosy) — automatyczny test w `--asr-test`.

### 7.3 Redukcja szumu modelem przed VAD/ASR — **P1, S**

**Co znaleziono.** sherpa-onnx wspiera speech enhancement **GTCRN** (0,54 MB, 16 kHz, 23,7 k parametrów — ultralekki, MIT) i **DPDFNet** (streaming, warianty 8/16/48 kHz, 8,7–14,9 MB, Apache-2.0, jeden stanowy graf ONNX na ramkę STFT) ([audiosronnx — porównanie](https://pypi.org/project/audiosronnx/0.6.0a1/), [react-native-sherpa-onnx — modele](https://github.com/XDcobra/react-native-sherpa-onnx)); DeepFilterNet3-LL osiąga ~10 ms opóźnienia w trybie strumieniowym ([deepfilter-rt](https://github.com/shimondoodkin/deepfilter-rt)).

**Plan w kodzie.** `AudioEnhancementService` dostaje krok `IDenoiser` (GTCRN domyślnie, DPDFNet jako opcja „lepsza jakość"). Włączany **tylko** gdy SNR z `VoiceSegmentMetrics` < próg (np. 15 dB) — brak szumu = brak dodatkowego CPU. A/B w `--asr-test`: WER z/bez denoisera na korpusie z szumem wentylatora i klawiatury.

### 7.4 Weryfikacja wyniku akcji przez UI Automation i OCR — **P1, M**

**Dlaczego.** Dziś dowód „uruchomiono kalkulator" to PID/proces. UFO² i Ari pokazują, że **obserwacja UI po akcji** (drzewo UIA, w ostateczności OCR zrzutu) jest tym, co odróżnia „wysłałem polecenie" od „zadziałało".

**Plan w kodzie.**
- `Services/History/UiEvidenceProbe.cs`: po `launch_app`/`close_app` odczyt okna pierwszoplanowego przez `System.Windows.Automation` (UIA, jest w WPF) — tytuł, klasa, PID, stan; zapis do dowodów bez zrzutów ekranu domyślnie.
- Opcjonalny OCR zrzutu (Windows.Media.Ocr — wbudowany w Windows 10+, wymaga TFM `net9.0-windows10.0.19041.0`; skrypt `check-architecture.py` trzeba zaktualizować) — tylko za zgodą użytkownika w ustawieniach, bo zrzut ekranu = dane wrażliwe.
- Status `Verified` dla akcji UI tylko, gdy sonda potwierdzi (np. istnieje okno procesu, który uruchomiliśmy); inaczej `Executed (unverified)`.

### 7.5 Migawka przed operacją destrukcyjną + „Cofnij" — **P1, M**

**Co znaleziono.** WinVoice: automatyczny snapshot plików przed operacją destrukcyjną i możliwość cofnięcia (ograniczenie: tylko pliki, nie rejestr). My mamy `.bak` przy edycji.

**Plan w kodzie.** `Services/Files/UndoJournal.cs`: każda operacja modyfikująca w workspace (edit/move/copy-overwrite) zapisuje wpis `{requestId, before: hash+kopia w Trash/, after}`; komenda „cofnij ostatnią zmianę" i przycisk w Historii; retencja 7 dni / 200 MB; cofnięcie samo jest akcją z dowodem. **Nie** obiecujemy cofania zamknięcia aplikacji ani zmian systemowych — UI mówi to wprost.

### 7.6 Skille w formacie SKILL.md (otwarty standard) — **P2, M**

**Co znaleziono.** Agent Skills: folder z `SKILL.md` (YAML: `name`, `description`; opcjonalnie `scripts/`, `references/`, `assets/`, eksperymentalne `allowed-tools`), standard opublikowany przez Anthropic 18 grudnia 2025, wspierany w 12 tygodni przez ok. 30 produktów (Copilot, Cursor, JetBrains, Gemini CLI, Goose, Claude Code), działa też z lokalnymi modelami przez Ollama ([anomity.ai](https://anomity.ai/blog/agent-skills-open-standard-adoption-governance/), [gokhshtein.com](https://gokhshtein.com/news/2026-09-18-anthropics-skillmd-standard-how-a-markdown-format-changes)).

**Plan w kodzie.** `%LOCALAPPDATA%\SentinelX\Skills\<nazwa>\SKILL.md` — Sentinel ładuje tylko frontmatter (progressive disclosure), a pełną instrukcję dopiero po dopasowaniu intencji; `scripts/` **nie są wykonywane** (bez zgody i bez sandboxa — patrz 7.8). Skill = wiedza + procedura + lista dozwolonych narzędzi z naszego rejestru. Przykładowe skille w repo: `diagnoza-sieci`, `porzadki-w-pobranych`.

### 7.7 Drugi backend LLM: Foundry Local (NPU, in-proc) obok Ollamy — **P2, M**

**Co znaleziono.** Foundry Local osiągnął GA na Build 2026 (2 czerwca 2026): SDK C#/Python/JS/Rust, runtime **w procesie aplikacji** (bez demona), automatyczny wybór CPU/GPU/NPU ([byteiota](https://byteiota.com/microsoft-foundry-local-ga/)). Działa na każdym Windows z GPU DirectX 12 (nie wymaga Copilot+), ma OpenAI-zgodne REST API, po pobraniu modelu działa offline; wybór EP: QNN (Qualcomm NPU), WinML/DirectML (AMD/Intel/NVIDIA/Qualcomm), CUDA, CPU ([FAQ Windows AI](https://learn.microsoft.com/en-us/windows/ai/faq)). Katalog obejmuje LLM (Phi-4-mini, Llama 3, Mistral), **Whisper tiny/base** i Moondream ([przegląd](https://deepwiki.com/MicrosoftDocs/windows-ai-docs/4.1-getting-started-with-foundry-local)). Ograniczenia z praktyki: tylko modele z katalogu, port zmienia się przy restarcie (przy REST), NPU realnie tylko dla części modeli ([gist](https://gist.github.com/casaout/5a651aa2f1373b66153fc6be1b81b2ac)).

**Plan w kodzie.** Interfejs `ILlmBackend` z implementacjami `OllamaBackend` (domyślna, jak dziś) i `FoundryLocalBackend` (SDK C#, przypięta wersja). Panel gotowości pokazuje oba; użytkownik wybiera. Korzyść: laptopy z NPU (Copilot+) dostają lokalny LLM bez instalowania Ollamy i bez obciążania GPU w grze.

### 7.8 Aktualizacje: Velopack zamiast ręcznego ZIP-a — **P1, S–M**

**Co znaleziono.** Velopack: instalator + auto-update z jednego polecenia, pakiety **delta** (użytkownik pobiera tylko różnicę), napisany w Rust, migracja ze Squirrela, 39 języków; działa z GitHub Releases jako źródłem ([repo](https://github.com/velopack/velopack), [NuGet](https://www-0.nuget.org/packages/Velopack/1.2.110-ge826545)).

**Plan w kodzie.** `UpdateService` (sprawdzenie **na żądanie i raz dziennie**, bez pobierania w tle podczas gry — `GamingModeService` blokuje), kanały `stable`/`preview`, pokaz changelogu przed instalacją, podpis paczek. Workflow: `vpk pack` w `windows-build.yml` na tagu `v*`, publikacja do GitHub Releases. Paczka portable zostaje (Velopack ma tryb portable self-updating).

### 7.9 Sandbox dla „kreatora programów" i skryptów — **P2, L**

**Kontekst.** `ProgramBuilderService` generuje kod; Ari wykonuje kod LLM bezpośrednio — my nie. Jeśli kiedyś uruchamiamy wygenerowany kod, to tylko: (a) **Windows Sandbox** (`.wsb`, jednorazowa maszyna) albo (b) proces z ograniczonym tokenem/AppContainer i bez sieci, z limitem czasu i katalogiem tymczasowym. Decyzja i szczegóły w rundzie 3 po researchu API AppContainer w .NET.

### 7.10 Aktualizacja backlogu (runda 2)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Głos | Weryfikacja mówcy, poziomy właściciel/gość | P1 | M | 2 |
| Głos | Denoiser GTCRN/DPDFNet przed VAD | P1 | S | 2 |
| Akcje | Dowody z UIA (+ opcjonalny OCR) | P1 | M | 2 |
| Pliki | Dziennik cofania (snapshot + undo) | P1 | M | 2 |
| Dystrybucja | Velopack, kanały, delta | P1 | S–M | 2 |
| AI | Foundry Local jako drugi backend (NPU) | P2 | M | 2 |
| Rozszerzenia | SKILL.md (tylko wiedza + procedury, bez wykonywania skryptów) | P2 | M | 2 |
| Bezpieczeństwo | Sandbox dla kodu generowanego | P2 | L | 2 |

### 7.11 Źródła rundy 2

- Ari-VoiceCommand: https://github.com/DO0OG/Ari-VoiceCommand
- WinVoice (sherpa-onnx, speaker verification, snapshot/undo): https://github.com/luoxiaguwu9394/windows_voice_assistant
- Microsoft UFO² / UFO³: https://microsoft.github.io/UFO/ufo2/overview/ , https://github.com/microsoft/UFO , https://arxiv.org/html/2504.14603v1
- speakeronnx (progi, modele WeSpeaker): https://github.com/TigreGotico/speakeronnx
- Speech enhancement (GTCRN/DPDFNet/DeepFilterNet): https://pypi.org/project/audiosronnx/0.6.0a1/ , https://github.com/shimondoodkin/deepfilter-rt , https://github.com/XDcobra/react-native-sherpa-onnx
- Agent Skills (SKILL.md): https://anomity.ai/blog/agent-skills-open-standard-adoption-governance/ , https://gokhshtein.com/news/2026-09-18-anthropics-skillmd-standard-how-a-markdown-format-changes
- Foundry Local GA / FAQ: https://byteiota.com/microsoft-foundry-local-ga/ , https://learn.microsoft.com/en-us/windows/ai/faq , https://deepwiki.com/MicrosoftDocs/windows-ai-docs/4.1-getting-started-with-foundry-local
- Velopack: https://github.com/velopack/velopack
- Windows.Media.Ocr z aplikacji desktop: https://stackoverflow.com/questions/52537944/is-using-windows-media-ocr-only-support-for-uwp-app

---

## 8. Runda 3 — pamięć, pliki, koniec tury, wizja, dobór modeli, testy

### 8.1 Pamięć długoterminowa: SQLite + FTS5 + sqlite-vec — **P1, M**

**Co znaleziono.** Wzorzec „pamięć agenta lokalnie": SQLite (jeden plik), **FTS5** do dokładnych słów, **sqlite-vec** (rozszerzenie in-proc, tabela `vec0`) do podobieństwa semantycznego, embeddingi z Ollamy (`/api/embed`) — bez osobnej bazy wektorowej i bez chmury ([dev.to](https://dev.to/syed_anzar/your-agents-memory-is-a-lie-a-durable-queryable-memory-layer-for-local-llm-agents-2ca), [gist: FTS5 + sqlite-vec + Ollama](https://gist.github.com/betoneh/79006333939dc2433e5e2f30428dd615)). Modele embeddingów w Ollamie: **embeddinggemma** (300M, 622 MB, 100+ języków, Matryoshka — wektor 768 można obciąć do 256/128), **bge-m3** (1,2 GB, wielojęzyczny, gęste + rzadkie wektory), **qwen3-embedding:0.6b** (639 MB, MTEB multilingual 64,33) ([cheat sheet](https://computingforgeeks.com/ollama-models-cheat-sheet/), [benchmark](https://www.morphllm.com/ollama-embedding-models)).

**Dlaczego to jest lepsze.** `ConversationMemoryService` dziś przechowuje JSON bez wyszukiwania po znaczeniu. Z FTS5 + wektorami asystent odpowie na „co ustalaliśmy o kopii zapasowej w zeszłym tygodniu" — lokalnie, z cytatem źródła (data, `requestId`).

**Plan w kodzie.**
- `Services/Memory/MemoryStore.cs`: `Microsoft.Data.Sqlite` + ładowanie `vec0.dll` (sqlite-vec) — jeśli rozszerzenie niedostępne, działa **samo FTS5** (jawny status w panelu gotowości).
- Embeddingi: **embeddinggemma z obcięciem do 256 wymiarów** (mały indeks, wielojęzyczny) przez Ollamę; ten sam model zasila router intencji z 3.4 — jeden model, dwa zastosowania. Gdy Ollama niedostępna → tylko FTS5.
- Co zapisujemy: fakty jawnie potwierdzone („zapamiętaj, że…"), podsumowania zadań z Historii, preferencje. **Nie** zapisujemy surowych transkrypcji bez zgody (`IncludeTranscriptInLogs` już steruje logami — rozszerzamy na pamięć).
- Komendy: „zapamiętaj…", „co pamiętasz o…", „zapomnij o…" (usunięcie = akcja z dowodem).

### 8.2 Wyszukiwanie plików: Everything SDK jako opcjonalny akcelerator — **P1, S–M**

**Co znaleziono.** voidtools **Everything** indeksuje MFT NTFS i daje natychmiastowe wyniki; SDK to `Everything.dll` (C API) — w .NET przez P/Invoke lub bibliotekę EverythingNet; prosty fallback: `es.exe` (CLI), a gdy Everything nie jest zainstalowane — brak funkcji, nie błąd ([EverythingNet](https://github.com/ju2pom/EverythingNet), [popMax #22](https://github.com/khaledtf19/popMax/issues/22)). Wyliczanie plików przez API systemowe jest zawsze wolne (ACL), dlatego Raycast-podobne narzędzia na Windows integrują Everything ([SO](https://stackoverflow.com/questions/62264202/is-there-a-faster-way-to-find-all-files)).

**Plan w kodzie.**
- `Services/Files/IFileIndex` z implementacjami: `WorkspaceIndex` (dziś: `CreatedFiles`), `EverythingIndex` (gdy wykryto `Everything.dll`/usługę), `WindowsSearchIndex` (OLE DB `Search.CollatorDSO`, wolniejszy, bez instalacji — do decyzji w rundzie 4).
- **Odczyt tylko** poza workspace: „znajdź fakturę z marca" zwraca listę ścieżek; otwarcie pliku = akcja LOW z dowodem; **zapis/usuwanie poza workspace nadal niedozwolone** (zasada 0.84 utrzymana).
- Wyniki filtrowane przez listę wykluczeń (profile przeglądarek, `AppData\Local\Temp`, katalogi z hasłami) zanim trafią do LLM.

### 8.3 Koniec tury: Smart Turn v3.2 zamiast stałych 650 ms ciszy — **P1, S**

**Co znaleziono.** **Smart Turn v3.2** (Pipecat, BSD-2): 8M parametrów, wejście = ostatnie 8 s audio 16 kHz, wyjście = prawdopodobieństwo zakończenia tury; 23 języki **w tym polski**; ONNX fp32 33 MB / int8 11,1 MB; działa na prozodii i tempie, nie na transkrypcie ([opis modelu](https://soniqo.audio/guides/turn)); trwa PR dodający Smart Turn do sherpa-onnx (#3956) ([PR](https://github.com/k2-fsa/sherpa-onnx/pull/3956)); referencyjna implementacja lokalna w Pipecat ([docs](https://reference-server.pipecat.ai/en/stable/api/pipecat.audio.turn.smart_turn.local_smart_turn_v3.html)).

**Dlaczego to jest lepsze.** Dziś `EndOfSpeechMilliseconds = 650` to kompromis: za krótko = ucinanie w pół zdania („otwórz… eee… kalkulator"), za długo = wolna reakcja. Model decyduje po pauzie VAD: skończone → natychmiast do ASR; niedokończone → czekaj do maks. 1,8 s.

**Plan w kodzie.** `Services/Voice/TurnDetector.cs` (ONNX Runtime już jest w procesie przez sherpa-onnx; jeśli PR #3956 wejdzie do wydania, używamy natywnego API sherpa). Wywoływany tylko przy pauzie VAD ≥ 200 ms; metryka: średni czas od końca mowy do decyzji oraz liczba „ucięć" (użytkownik powtarza polecenie w < 3 s).

### 8.4 Dobór modelu LLM wg sprzętu (2026) i mapa zdolności — **P0, S**

**Co znaleziono.** Rekomendacje 2026: **8 GB VRAM** → Qwen3.5-9B (6,6 GB w Ollamie, tekst + obraz) lub Gemma 4 8B; **16 GB** → Gemma 4 12B (multimodalny) lub gpt-oss-20b (rozumowanie/tool-use, MoE 3,6B aktywnych); **CPU-only 16 GB RAM** → Qwen3.5 4B Q4, Gemma 4 E4B, Phi-4-mini ([omidsaffari.com](https://omidsaffari.com/blog/best-local-llms-2026), [popularai.org](https://www.popularai.org/p/best-cpu-only-local-llm-2026)). Qwen3.6 podniósł niezawodność function-calling (Terminal-Bench 2.0: 59,3 %) ([haimaker.ai](https://haimaker.ai/blog/best-local-models-for-openclaw/)). Gemma 4 ma natywną wizję i tool calling w każdym rozmiarze; Qwen3-VL prowadzi w OCR/zrzutach ekranu ([promptquorum — vision](https://www.promptquorum.com/prompt-bites/which-ollama-models-support-vision)).

**Plan w kodzie.**
- `Services/AI/ModelAdvisor.cs`: odczyt VRAM (3.5) + RAM → propozycja 3 modeli (mały/średni/z wizją) z komendą `ollama pull`; użytkownik klika, nic nie pobiera się samo.
- Po wyborze: `/api/show` → zdolności (`tools`, `thinking`, `vision`, długość kontekstu); UI oznacza „ten model nie obsługuje narzędzi — routing intencji przez LLM będzie wyłączony".
- README: zamiast `qwen3:4b` → tabela wg sprzętu; utrzymujemy zasadę: **poniżej ~7–9B nie włączamy propozycji akcji przez LLM** (tylko rozmowa i odczyty), bo małe modele mylą argumenty (potwierdza to WinVoice w 7.1).

### 8.5 „Co jest na ekranie?" — wizja lokalna za zgodą — **P2, M**

**Co znaleziono.** Qwen3-VL 8B (~8 GB Q4) najlepszy do OCR/zrzutów/wielojęzyczności; Gemma 4 12B (~8 GB) łączy wizję z tool callingiem; poniżej 4 GB VRAM: Moondream 2, SmolVLM ([promptquorum — vision 2026](https://www.promptquorum.com/power-local-llm/local-vision-models-llava-ollama-2026)). UFO² pokazuje, że **UIA + wizja** razem są skuteczniejsze niż samo jedno.

**Plan w kodzie.** Komenda „co jest na ekranie / przeczytaj ten błąd": (1) zgoda jednorazowa lub stała w ustawieniach, (2) zrzut **tylko aktywnego okna** (nie całego pulpitu), (3) najpierw tekst z UIA/OCR (tanio), (4) model wizyjny tylko gdy użytkownik pyta o treść wizualną; obraz nie jest zapisywany do Historii (tylko hash + wymiary). W trybie gry funkcja wyłączona.

### 8.6 Testy: osobny projekt xUnit + FlaUI obok obecnych runnerów — **P1, M**

**Co znaleziono.** Standardowy układ: projekt xUnit z testami logiki (STA gdzie trzeba) + projekt FlaUI (UIA3) uruchamiający EXE i klikający po `AutomationId`, z `Retry` zamiast `Thread.Sleep`; działa na `windows-latest` w GitHub Actions ([wpf-testing-mwe](https://github.com/MinimalWindowsDev/wpf-testing-mwe), [wpf-ui-testing-demo](https://github.com/MinimalWindowsDev/wpf-ui-testing-demo)).

**Plan w kodzie.**
- `tests/SentinelX.Tests/` (xUnit): `PolishTextNormalizer`, `VoiceStateMachine`, `IntentRouter` (korpus 200 komend), walidacja schematu intencji, `UndoJournal`, `SpeakerGate` (na nagraniach syntetycznych).
- `tests/SentinelX.UiTests/` (FlaUI): paleta Ctrl+K, panel zgód (odrzucenie głosowego „tak"), STOP/resume, nawigacja 8 stron. `AutomationProperties.AutomationId` we wszystkich widokach (przy okazji: dostępność dla czytników ekranu).
- Obecne `--ui-smoke`/`--self-test` zostają (szybkie, bez zależności) — nowe projekty uzupełniają, nie zastępują. `check-architecture.py` sprawdza, że każdy przycisk akcji ma `AutomationId`.

### 8.7 Aktualizacja backlogu (runda 3)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Pamięć | SQLite + FTS5 + sqlite-vec, embeddinggemma@256 | P1 | M | 3 |
| Pliki | Everything SDK (odczyt), fallback Windows Search | P1 | S–M | 3 |
| Głos | Smart Turn v3.2 (koniec tury) | P1 | S | 3 |
| AI | ModelAdvisor wg VRAM/RAM + mapa zdolności z `/api/show` | P0 | S | 3 |
| AI | Wizja lokalna dla aktywnego okna, za zgodą | P2 | M | 3 |
| Jakość | xUnit + FlaUI, AutomationId wszędzie | P1 | M | 3 |

### 8.8 Źródła rundy 3

- Pamięć lokalna (SQLite/FTS5/sqlite-vec/Ollama): https://dev.to/syed_anzar/your-agents-memory-is-a-lie-a-durable-queryable-memory-layer-for-local-llm-agents-2ca , https://gist.github.com/betoneh/79006333939dc2433e5e2f30428dd615
- Modele embeddingów: https://computingforgeeks.com/ollama-models-cheat-sheet/ , https://www.morphllm.com/ollama-embedding-models , https://builderai.tools/blog/best-open-source-embedding-models-2026
- Everything SDK: https://github.com/ju2pom/EverythingNet , https://github.com/khaledtf19/popMax/issues/22
- Smart Turn v3.2: https://soniqo.audio/guides/turn , https://github.com/k2-fsa/sherpa-onnx/pull/3956
- Modele LLM 2026 wg sprzętu: https://omidsaffari.com/blog/best-local-llms-2026 , https://www.popularai.org/p/best-cpu-only-local-llm-2026 , https://haimaker.ai/blog/best-local-models-for-openclaw/ , https://huggingface.co/blog/daya-shankar/open-source-llm-models-to-run-locally
- Modele wizyjne: https://www.promptquorum.com/prompt-bites/which-ollama-models-support-vision , https://www.promptquorum.com/power-local-llm/local-vision-models-llava-ollama-2026
- Testy WPF (xUnit + FlaUI): https://github.com/MinimalWindowsDev/wpf-testing-mwe , https://github.com/MinimalWindowsDev/wpf-ui-testing-demo

---

## 9. Runda 4 — weryfikacja techniczna: ASR po polsku w sherpa-onnx, GPU dla Whispera, full-duplex/AEC, App Actions, podpisywanie

### 9.1 Potwierdzone modele ASR z polskim w sherpa-onnx (zależność już w repo) — **P0, S**

**Co znaleziono (potwierdzone w dokumentacji sherpa-onnx).**
- `sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8` — 25 języków europejskich, w tym **Polski**, pobierany z GitHub Releases sherpa-onnx (`asr-models`), typ `nemo_transducer` ([docs](https://github.com/k2-fsa/sherpa/blob/master/docs/source/onnx/pretrained_models/offline-transducer/nemo-transducer-models.rst)).
- **Cohere Transcribe (03-2026)** — 2B parametrów, Apache 2.0, 14 języków w tym **polski**, „best-in-class" WER na tych 14 językach i RTF do 3× lepszy niż inne dedykowane modele tej wielkości; sherpa-onnx ma konwersję offline i **przykład w C#**; przy dekodowaniu podaje się język (`pl`) ([sherpa-onnx — Cohere Transcribe](https://k2-fsa.github.io/sherpa/onnx/cohere_transcribe/index.html), [docs.cohere.com](https://docs.cohere.com/docs/transcribe), [ONNX na HF](https://huggingface.co/onnx-community/cohere-transcribe-03-2026-ONNX)). Na leaderboardzie STT 2026 Cohere Transcribe ma średni WER 5,42 % (2. miejsce), Qwen3-ASR-1.7B 5,76 %, Whisper Large v3 7,44 % ([codesota STT leaderboard](https://www.codesota.com/speech/stt-leaderboard)).
- Konfiguracja `OfflineRecognizerConfig` w bieżącej wersji obejmuje też `canary`, `omnilingual` (Meta, 1600 języków), `cohere_transcribe`, `qwen3_asr` z polem **`hotwords`** ([zrzut konfiguracji w docs](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/offline-transducer/nemo-transducer-models.html)).

**Decyzja — trzy profile ASR (użytkownik widzi WER/rozmiar/czas i wybiera; domyślny profil dobierany po RAM/CPU):**

| Profil | Model | Kiedy | Uwagi |
|---|---|---|---|
| **Szybki** | Parakeet TDT 0.6B v3 int8 (≈7,3 % WER PL) | laptop, CPU, tryb gry | najszybszy; komendy krótkie |
| **Dokładny** | Cohere Transcribe 2B int8 (język `pl`) | desktop z mocnym CPU/GPU | najlepszy WER; dłuższe dyktowanie |
| **Zgodność** | Qwen3-ASR (dziś) + Whisper (dziś) | już zainstalowane | Qwen3-ASR zyskuje `hotwords` |

**Plan w kodzie.** `VoiceModelManager` dostaje manifest modeli (URL z GitHub Releases sherpa-onnx, SHA-256, licencja, rozmiar, języki); `IAsrEngine` z fabryką po typie modelu; **hotwords** (nazwy aplikacji z `AppLauncherService`, „Sentinel", nazwy plików z workspace) przekazywane do Qwen3-ASR/transducerów — tanie, znaczące zmniejszenie błędów na nazwach własnych. `--asr-test` liczy WER dla każdego profilu na tym samym korpusie i zapisuje do raportu.

### 9.2 Whisper na GPU: Whisper.net ma gotowe runtime'y — **P1, S**

**Co znaleziono.** Whisper.net wybiera runtime automatycznie w kolejności: `Whisper.net.Runtime.Cuda` (NVIDIA, wymaga CUDA Toolkit ≥ 13.0.1 dla 1.9.0) → `Vulkan` (Windows x64) → `CoreML` → `OpenVino` (Intel) → `Runtime` (CPU) → `NoAvx`; kolejność można wymusić przez `RuntimeOptions.RuntimeLibraryOrder`; obsługuje własne binaria ułożone w `./runtimes` ([NuGet Whisper.net](https://www.nuget.org/packages/Whisper.net/1.9.0-preview2), [Whisper.net.Runtime.Cuda](https://www.nuget.org/packages/Whisper.net.Runtime.Cuda/)).

**Plan w kodzie.** Dodać `Whisper.net.Runtime.Vulkan` (działa na NVIDIA/AMD/Intel bez instalowania CUDA Toolkit — mniejsze tarcie niż CUDA) jako opcjonalny pakiet w paczce „GPU"; panel gotowości pokazuje, który runtime załadowano. Model: `ggml-large-v3-turbo` (≈1,6 GB, 4,7–7,8 % WER PL wg benchmarków) dla profilu „Dokładny–GPU". Rozmiar paczki portable rośnie — dlatego osobny artefakt `SentinelX-Portable-win-x64-gpu` w CI.

### 9.3 Full-duplex i barge-in bez własnego AEC: użyć AEC systemu Windows 11 — **P1, M**

**Co znaleziono.** Windows 11 udostępnia w WASAPI interfejs `IAcousticEchoCancellationControl` (przez `IAudioClient::GetService`): jeśli punkt końcowy przechwytywania wspiera AEC, można wskazać urządzenie odtwarzania jako **strumień referencyjny**; `NULL` = Windows sam wybiera loopback; brak wsparcia = `E_NOINTERFACE` (nie błąd krytyczny) ([learn.microsoft.com — IAcousticEchoCancellationControl](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iacousticechocancellationcontrol), [SetEchoCancellationRenderEndpoint](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iacousticechocancellationcontrol-setechocancellationrenderendpoint)). Alternatywy .NET (porty WebRTC AEC, Speex) są stare/archiwalne ([WebRTC_VoiceEngine](https://github.com/garyyu/WebRTC_VoiceEngine)) — nie warto ich utrzymywać.

**Plan w kodzie.**
- Cienka warstwa interop nad NAudio: `IAudioClient2::SetClientProperties` (kategoria *Communications*) + `GetService(IAcousticEchoCancellationControl)` → AEC systemowe z referencją = urządzenie, na którym gra TTS.
- Gdy AEC dostępne: **pełny duplex** — VAD nasłuchuje podczas mówienia TTS, mowa użytkownika przerywa syntezę (barge-in). Gdy niedostępne: **half-duplex** jak WinVoice (przerwać można tylko słowem-kluczem z KWS, który jest odporny na echo dzięki progowi) + bramka energetyczna z `WasapiLoopbackCapture` (już używane).
- Metryka: liczba fałszywych barge-in wywołanych własnym TTS (test automatyczny: TTS mówi 60 s, mikrofon słyszy głośnik, VAD nie może wywołać ACTIVE).

### 9.4 App Actions on Windows — odłożone (wymaga tożsamości pakietu) — **P3**

**Co znaleziono.** App Actions on Windows pozwala aplikacji rejestrować akcje, które Windows i inne aplikacje (w tym agenci) mogą proponować użytkownikowi; wymaga **package identity (MSIX)**, wpisu `uap3:AppExtension` (`com.microsoft.windows.ai.actions`), pliku JSON z definicją akcji i serwera COM ([Get started](https://learn.microsoft.com/en-us/windows/ai/app-actions/actions-get-started), [samples](https://github.com/microsoft/App-Actions-On-Windows-Samples/blob/main/README.md)). Konflikt z paczką portable/Velopack (brak tożsamości) — możliwe przez „package with external location", ale to osobny projekt. Decyzja: **po 1.0**, tylko akcje odczytowe.

### 9.5 Podpisywanie i łańcuch dostaw — **P1, S**

**Co znaleziono.** SmartScreen „Windows protected your PC" to miejsce, gdzie nietechniczni użytkownicy rezygnują; opcje: **SignPath Foundation** (bezpłatne certyfikaty dla OSS; wymaga publicznego repo, istniejących wydań, udokumentowanej funkcjonalności; nazwa wydawcy „SignPath Foundation") lub Azure Trusted Signing (~10 USD/mies., osoby fizyczne tylko US/CA) ([przegląd 2026](https://comparecheapssl.com/free-code-signing-certificate-and-how-to-get-it/), [dyskusja w OSS](https://github.com/balazsotakomaiya/pupil/issues/40)).

**Plan.** (1) Najpierw regularne wydania z GitHub Releases (warunek SignPath), (2) wniosek do SignPath, (3) podpis w CI również dla paczki Velopack; (4) `dotnet list package --vulnerable` i Dependabot w CI; (5) przypięcie akcji GitHub do SHA; (6) SBOM (CycloneDX) w artefaktach; (7) `SECURITY.md` z modelem zagrożeń (mikrofon, zrzuty ekranu, pliki, LLM prompt injection z treści plików/ekranu — treść z narzędzi nigdy nie jest traktowana jak polecenie użytkownika; schemat intencji przyjmuje **tylko** narzędzia z rejestru).

### 9.6 Aktualizacja backlogu (runda 4)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Głos | 3 profile ASR (Parakeet v3 / Cohere Transcribe / zgodność) + hotwords | P0 | S | 4 |
| Głos | Whisper.net Vulkan/CUDA jako paczka GPU | P1 | S | 4 |
| Głos | AEC systemowe Windows 11 → full-duplex i barge-in; half-duplex jako fallback | P1 | M | 4 |
| Bezpieczeństwo | Podpisywanie (SignPath), SBOM, Dependabot, SECURITY.md, ochrona przed prompt injection | P1 | S | 4 |
| Integracja | App Actions on Windows (MSIX) | P3 | L | 4 |

### 9.7 Źródła rundy 4

- Parakeet TDT 0.6B v3 w sherpa-onnx: https://github.com/k2-fsa/sherpa/blob/master/docs/source/onnx/pretrained_models/offline-transducer/nemo-transducer-models.rst
- Cohere Transcribe: https://k2-fsa.github.io/sherpa/onnx/cohere_transcribe/index.html , https://docs.cohere.com/docs/transcribe , https://huggingface.co/onnx-community/cohere-transcribe-03-2026-ONNX
- STT leaderboard 2026: https://www.codesota.com/speech/stt-leaderboard
- Whisper.net runtime'y: https://www.nuget.org/packages/Whisper.net/1.9.0-preview2 , https://www.nuget.org/packages/Whisper.net.Runtime.Cuda/
- AEC w WASAPI (Windows 11): https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iacousticechocancellationcontrol
- App Actions on Windows: https://learn.microsoft.com/en-us/windows/ai/app-actions/actions-get-started
- Podpisywanie OSS: https://comparecheapssl.com/free-code-signing-certificate-and-how-to-get-it/ , https://github.com/balazsotakomaiya/pupil/issues/40

---

## 10. Runda 5 — model bezpieczeństwa na poziomie „lepszy niż prompt", prywatność, powiadomienia

### 10.1 Egzekwowanie uprawnień deterministycznie, nie promptem — **P0, S–M**

**Co znaleziono.** screenpipe (YC S26, lokalna „pamięć komputera") egzekwuje uprawnienia AI w trzech warstwach: **skill gating (AI nigdy nie dowiaduje się o zabronionych endpointach)**, przechwycenie na poziomie agenta (blokada przed wykonaniem), middleware z tokenami per-plugin — „not prompt-based" ([screenpipe](https://github.com/shawnxie94/screenpipe)). Microsoft w „Windows 11 security book" formułuje zasady agentów: odrębne konta agentów, **agent workspace** (izolowany pulpit), **user transparency** (autoryzacja, monitorowanie, przejęcie kontroli), Copilot Actions **domyślnie wyłączone**, dostęp tylko do znanych folderów bez dodatkowej zgody ([learn.microsoft.com — agentic security](https://learn.microsoft.com/en-us/windows/security/book/operating-system-agentic-security)).

**Dlaczego to jest lepsze.** Sentinel ma już bramkę zgód *po* decyzji LLM. Brakuje warstwy *przed*: LLM widzi wszystkie narzędzia niezależnie od kontekstu. Ograniczając **widoczność** narzędzi per żądanie, eliminujemy całą klasę błędów i ataków (prompt injection z pliku/ekranu nie może wywołać `close_app`, jeśli tego narzędzia nie ma w schemacie).

**Plan w kodzie.**
- `Services/Permissions/CapabilityScope.cs`: dla każdego żądania liczymy zbiór dozwolonych narzędzi z kontekstu: poziom mówcy (7.2), tryb gry, stan STOP, źródło (klawiatura / głos / MCP / skill), ustawienia użytkownika. Schemat JSON dla LLM (3.3) ma `enum` narzędzi = **dokładnie ten zbiór**.
- Druga warstwa (istniejąca): `PermissionCenterService` + `ActionEngine` odrzucają wszystko spoza zbioru, nawet gdy schemat zawiódł.
- Trzecia warstwa: treść zwracana przez narzędzia (plik, OCR, wynik wyszukiwania) trafia do LLM jako **dane** w osobnej roli/ogranicznikach z adnotacją „to nie jest polecenie"; testy regresji z próbkami prompt injection (plik `tests/injection/*.txt`).
- Zgodność z zasadami Microsoftu: mapujemy je 1:1 w `SECURITY.md` (konto użytkownika, zgoda, transparentność = Historia + dowody, wyłączone domyślnie: wizja, OCR, MCP-klient, skille).

### 10.2 Redakcja danych wrażliwych zanim cokolwiek trafi do LLM/Historii — **P1, S**

**Plan w kodzie.** `Services/Privacy/Redactor.cs` używany przez OCR/UIA (7.4, 8.5), wyszukiwanie plików (8.2), pamięć (8.1): pola hasła z UIA (`IsPassword`) nigdy nie są czytane; regexy dla PESEL, NIP, numerów kart (Luhn), IBAN, e-maili i tokenów API; ścieżki z nazwą użytkownika skracane (jak dziś w `SpeechOutputService`). Tryb „pokaż, co zostało zredagowane" w Historii. screenpipe stosuje analogiczne filtry (okna, aplikacje, hasła, PII) ([screenpipe — specs](https://github.com/ppatel26/screenpipe)).

### 10.3 Powiadomienia systemowe i szacunek dla skupienia — **P2, S**

**Co znaleziono.** `CommunityToolkit.WinUI.Notifications` (`ToastContentBuilder().AddText(...).Show()`) działa w **niespakowanych** aplikacjach Win32/.NET bez skrótu w Start ([Windows Community Toolkit 7.0](https://thewincentral.com/microsoft-announces-windows-community-toolkit-v7-0-with-new-net-standard-mvvm-library-easy-to-use-toast-notification-helpers-and-more-download/), [ToastContentBuilder](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.winui.notifications.toastcontentbuilder?view=win-comm-toolkit-dotnet-7.0)).

**Plan w kodzie.** Toast dla: alertu Watch (CPU/RAM), oczekującej zgody gdy okno schowane, zakończenia długiego zadania; przyciski „Pokaż" / „Odrzuć" (odrzucenie tak, **zatwierdzenie tylko w oknie**). W trybie gry i przy `QUNS_BUSY`/`QUNS_PRESENTATION_MODE` — brak toastów, tylko overlay.

### 10.4 Czego świadomie nie robimy (dopisane do logu decyzji)

| Pomysł | Decyzja | Powód |
|---|---|---|
| Ciągłe nagrywanie ekranu („pamięć komputera" jak screenpipe/Recall) | **nie** | ~5–20 GB/mies., ogromne ryzyko prywatności; nasza „pamięć" to jawne fakty + historia akcji (8.1) |
| Wykonywanie kodu/skryptów generowanych przez LLM (jak Ari) | **nie** bez sandboxa 7.9 | sprzeczne z modelem dowodów i zgód |
| Zgoda głosowa na akcje HIGH | **nie** (utrzymane) | ASR może się pomylić; weryfikacja mówcy nie jest uwierzytelnieniem |
| Osobne konto/pulpit agenta (jak Windows agent workspace) | **później (P3)** | wymaga wielu uprawnień; dla naszych akcji (odczyty, pliki w workspace, uruchamianie aplikacji) wystarczy konto użytkownika + zgody |

### 10.5 Aktualizacja backlogu (runda 5)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Bezpieczeństwo | CapabilityScope — widoczność narzędzi per kontekst + testy prompt injection | P0 | S–M | 5 |
| Prywatność | Redactor dla OCR/UIA/plików/pamięci | P1 | S | 5 |
| UX | Toasty (odrzuć/pokaż), cisza w grze/prezentacji | P2 | S | 5 |

### 10.6 Źródła rundy 5

- screenpipe (uprawnienia deterministyczne, filtry PII): https://github.com/shawnxie94/screenpipe , https://github.com/ppatel26/screenpipe
- Windows 11 security book — Securing AI agents on Windows: https://learn.microsoft.com/en-us/windows/security/book/operating-system-agentic-security
- Agent workspace (przegląd): https://windowsforum.com/threads/agent-workspace-for-copilot-on-windows-11-ai-actions-in-a-separated-session.391071
- Toasty w aplikacjach niespakowanych: https://thewincentral.com/microsoft-announces-windows-community-toolkit-v7-0-with-new-net-standard-mvvm-library-easy-to-use-toast-notification-helpers-and-more-download/

---

## 11. Runda 6 — potwierdzenie wykonalności w .NET (jedna zależność, wiele funkcji) i TTS „premium"

### 11.1 sherpa-onnx w C#: wszystko, czego potrzebuje plan, ma oficjalny przykład .NET — **potwierdzone**

Aktualna wersja NuGet `org.k2fsa.sherpa.onnx` to **1.13.8** (repo: 1.13.5) ([NuGet](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx)). Katalog `dotnet-examples` w repozytorium zawiera gotowe przykłady C# dla dokładnie tych elementów planu ([dotnet-examples](https://github.com/k2-fsa/sherpa-onnx/tree/master/dotnet-examples)):

| Element planu | Przykład C# w sherpa-onnx |
|---|---|
| 3.1 KWS (wake word) | `keyword-spotting-from-microphone`, `keyword-spotting-from-files` |
| 3.2 TTS polski | `offline-tts`, `offline-tts-play` (Piper/VITS), `supertonic-tts`, `zipvoice-tts`, `kokoro-tts`, `pocket-tts-zero-shot` |
| 7.2 Weryfikacja mówcy | `speaker-identification` |
| 7.3 Denoiser | `speech-enhancement-gtcrn`, `speech-enhancement-dpdfnet`, warianty `streaming-*` |
| 9.1 ASR Cohere / Qwen3 | `non-streaming-cohere-transcribe-decode-files`, `vad-non-streaming-qwen3-asr`, `non-streaming-canary-decode-files` |
| Dyktowanie z interpunkcją | `offline-punctuation`, `online-punctuation` |
| Rozpoznanie języka (PL/EN) | `spoken-language-identification` |

**Konsekwencja dla planu.** Podbicie `org.k2fsa.sherpa.onnx` 1.13.5 → 1.13.8 jest **pierwszym krokiem 0.85** (jedna zmiana w `SENTINEL-X.csproj`, test `--asr-test` musi przejść). Nie dodajemy osobnych bibliotek do KWS/TTS/denoise/speaker — mniejsza paczka, jeden runtime ONNX, jedna polityka wątków.

### 11.2 TTS „premium" po polsku: Supertonic 3 — opcja z zastrzeżeniami — **P2, S**

**Co znaleziono.** Supertonic 3 obsługuje **31 języków w tym polski**, 66M parametrów, RTF ≈ 0,006 na M4 Pro (dziesiątki razy szybciej niż czas rzeczywisty), 10 głosów, ONNX ([README](https://github.com/supertone-inc/supertonic/blob/main/README.md), [ogłoszenie v2](https://www.reddit.com/r/LocalLLaMA/comments/1q5e010/supertonic2_lightning_fast_ondevice_multilingual/)). **Ale:** licencja OpenRAIL-M (ograniczenia użycia) i repozytorium **archiwizowane 23 lipca 2026** — brak dalszego rozwoju ([notice](https://github.com/codingwatching/supertonic)). sherpa-onnx ma wsparcie i poprawkę diakrytyków dla polskiego (#3750).

**Decyzja.** Domyślnie **Piper `pl_PL-*`** (MIT, aktywnie utrzymywany). Supertonic 3 jako opcjonalny głos „premium" do pobrania ręcznie po akceptacji licencji, z jawną informacją „projekt archiwalny". Test odsłuchowy A/B (10 zdań PL, 3 osoby) decyduje, czy w ogóle go wystawiamy.

### 11.3 Dyktowanie do dowolnego okna (bonus wynikający z 11.1) — **P2, M**

Skoro mamy ASR + interpunkcję (`offline-punctuation`), naturalna funkcja: **„dyktuj"** → tekst z interpunkcją wklejany do aktywnego pola przez `SendInput` (Unicode), z podglądem i Esc = anuluj. Bez LLM. Wzorzec z projektu Speed of Sound (voice typing) korzystającego z sherpa-onnx ([pub.dev — projekty](https://pub.dev/packages/sherpa_onnx)). To akcja LOW, ale wklejanie do okien haseł jest blokowane (UIA `IsPassword`, 10.2).

### 11.4 Aktualizacja backlogu (runda 6)

| Obszar | Pozycja | Prio | Koszt | Runda |
|---|---|---|---|---|
| Fundament | Podbicie sherpa-onnx do 1.13.8 + smoke | P0 | S | 6 |
| Głos | Supertonic 3 jako opcja „premium" (po A/B) | P2 | S | 6 |
| Głos | Dyktowanie z interpunkcją do aktywnego okna | P2 | M | 6 |

### 11.5 Źródła rundy 6

- NuGet org.k2fsa.sherpa.onnx 1.13.8: https://www.nuget.org/packages/org.k2fsa.sherpa.onnx
- Przykłady C#: https://github.com/k2-fsa/sherpa-onnx/tree/master/dotnet-examples
- Supertonic: https://github.com/supertone-inc/supertonic/blob/main/README.md , https://github.com/codingwatching/supertonic

---

## 12. Skonsolidowana mapa drogowa (po 6 rundach)

> Kolejność wynika z zależności i z zasady „najpierw to, co codziennie słychać i widać": polski głos, wake word, latencja, potem przewagi.

### 0.85 — „Słychać różnicę" (P0, ~3–4 tyg.)
1. sherpa-onnx → 1.13.8 (11.1).
2. KWS „Sentinel" zamiast ASR całego pokoju (3.1) + metryki wybudzeń.
3. Piper `pl_PL` przez sherpa-onnx, streaming zdaniami (3.2).
4. Profile ASR: Parakeet v3 / Cohere Transcribe / zgodność + hotwords (9.1); korpus `tests/asr-pl/` i WER w `--asr-test` (3.8).
5. Ollama: streaming + JSON Schema + `think` + mapa zdolności modelu (3.3, 8.4); ModelAdvisor.
6. CapabilityScope — widoczność narzędzi per kontekst + testy prompt injection (10.1).

### 0.86 — „Rozumie i pamięta" (P1, ~3–4 tyg.)
7. Semantyczny router intencji na embeddinggemma@256 (3.4) — cel ≥ 95 % bez LLM.
8. Smart Turn v3.2 (8.3) i denoiser GTCRN włączany po SNR (7.3).
9. Pamięć SQLite + FTS5 + sqlite-vec (8.1) z komendami „zapamiętaj / co pamiętasz / zapomnij".
10. Dziennik cofania w workspace (7.5) + Everything SDK do wyszukiwania (8.2).
11. xUnit + FlaUI, AutomationId (8.6); Velopack + kanały (7.8); Dependabot/SBOM/SECURITY.md (9.5).

### 0.90 — „Widzi system i grę" (P1, ~4 tyg.)
12. VRAM przez DXGI, NVML opcjonalnie (3.5); wykrywanie gry z 4 sygnałów (3.7).
13. PresentMon: FPS / 1 % low / frametime w overlayu (3.6).
14. Weryfikacja mówcy z poziomami właściciel/gość (7.2) — również jako wejście do CapabilityScope.
15. Dowody z UIA po akcjach, opcjonalny OCR z Redactorem (7.4, 10.2).
16. AEC systemowe → full-duplex/barge-in, fallback half-duplex (9.3); paczka GPU z Whisper Vulkan (9.2).

### 1.0 — „Otwarty i podpisany" (P1/P2)
17. MCP: rejestr narzędzi → serwer stdio (odczyty) → klient dla zaufanych serwerów (3.9).
18. SKILL.md jako wiedza/procedury bez wykonywania skryptów (7.6).
19. Foundry Local jako drugi backend (7.7); wizja aktywnego okna za zgodą (8.5); dyktowanie (11.3); toasty (10.3).
20. Podpis SignPath po serii wydań (9.5). Zamknięcie listy testów ręcznych z README.

### Po 1.0 (P3)
- App Actions on Windows (MSIX) (9.4); osobny pulpit dla długich zadań (10.4); sandbox dla kodu generowanego (7.9).

---

## 13. Tablica porównawcza — gdzie po realizacji planu Sentinel wygrywa z projektami z GitHuba

| Cecha | Ari-VoiceCommand | WinVoice | Nox | UFO² | **Sentinel X (plan 1.0)** |
|---|---|---|---|---|---|
| Jedna paczka bez Pythona/Electrona | ✗ | ✗ | ✗ | ✗ | **✓** (.NET self-contained, Velopack) |
| Wake word lokalny (KWS, nie ASR) | ✓ | ✓ | ✓ (openWakeWord) | – | **✓** (sherpa KWS) |
| Polski ASR z mierzonym WER | ? | ✗ (zh/en) | ? | – | **✓** (Parakeet v3 / Cohere Transcribe, korpus PL) |
| Polski neuronowy TTS | ? | ✗ | ✗ (Kokoro) | – | **✓** (Piper pl_PL; opcja Supertonic) |
| Weryfikacja mówcy z poziomami | ✗ | ✓ | ✗ | – | **✓** |
| Koniec tury modelem (Smart Turn) | ✗ | ✗ | ✗ | – | **✓** |
| Full-duplex/barge-in | ? | ✗ (half-duplex) | ? | – | **✓** gdy AEC systemowe; jawny fallback |
| LLM proponuje w schemacie, wykonuje bramka zgód | ✗ (autonomiczne wykonanie kodu) | częściowo (biała lista) | ✗ | ✗ | **✓** + widoczność narzędzi per kontekst |
| Dowody po akcji (UIA/OCR) | ✓ (OCR) | ✗ | ✗ | ✓ | **✓** (UIA domyślnie, OCR za zgodą) |
| Cofnij operację plikową | ✗ | ✓ | ✗ | ✗ | **✓** |
| Prawdziwe FPS/VRAM w grze | ✗ | ✗ | ✗ | ✗ | **✓** (PresentMon, DXGI) |
| MCP / SKILL.md | ✓ / ✓ | ✗ | ✗ | ✓ (MCP) | **✓ / ✓** (bez wykonywania skryptów) |
| Pamięć semantyczna lokalnie | ✓ | ✗ | ✗ | ✓ (RAG) | **✓** (FTS5 + sqlite-vec) |
| Testy automatyczne UI w CI | ? | ✗ | ✗ | ✗ | **✓** (smoke + FlaUI) |
| Podpisane wydania | ✗ | ✗ | ✗ | – | **✓** (SignPath) |

„?" = nie zweryfikowano w źródłach; „–" = nie dotyczy (UFO² nie jest asystentem głosowym).

---

## 14. Rejestr ryzyk

| Ryzyko | Prawdopodobieństwo | Skutek | Mitygacja |
|---|---|---|---|
| Model KWS zh-en słabo łapie polską wymowę „Sentinel" | średnie | fałszywe/nieudane wybudzenia | wiele wariantów fonemów + progi per wariant; fallback tekstowy; opcja openWakeWord |
| Cohere Transcribe 2B za wolny na słabym CPU | wysokie na laptopach | opóźnienia | profil „Szybki" (Parakeet) domyślny na CPU; pomiar RTF w panelu gotowości |
| MCP C# SDK w preview zmienia API | wysokie | koszt utrzymania | przypięta wersja, adapter, faza A bez SDK |
| Small LLM myli argumenty narzędzi | wysokie | błędne propozycje | schemat + `temperature=0` + progi + próg wielkości modelu + zgody |
| AEC systemowe niedostępne na danym sprzęcie | średnie | brak barge-in | half-duplex, jawny komunikat |
| Whisper Vulkan powiększa paczkę | pewne | większy ZIP | osobny artefakt GPU |
| PresentMon nie zainstalowany | wysokie | brak FPS | jawne „brak", link; nigdy zmyślone wartości |
| Zmiana TFM na `net9.0-windows10.0.19041.0` (OCR) | średnie | regresje build/CI | osobny PR, aktualizacja `check-architecture.py`, smoke na paczce portable |
| Prompt injection przez treść plików/ekranu | średnie | nieautoryzowana propozycja akcji | CapabilityScope + rola „dane" + testy `tests/injection/` |

---

## 15. Otwarte pytania na kolejne rundy

1. Które głosy Piper `pl_PL` (gosia / darkman / mc_speech) brzmią najlepiej przy `length_scale` 0,9–1,0 — test odsłuchowy.
2. Czy Smart Turn trafi do wydania sherpa-onnx (PR #3956) — wtedy zero dodatkowego kodu ONNX.
3. Windows Search (OLE DB) jako fallback dla Everything — czy latencja < 300 ms na typowym dysku?
4. Foundry Local: czy katalog zawiera model z tool callingiem i polskim na poziomie Qwen3.5-4B? (test po GA).
5. Koszt CPU KWS + VAD + (opcjonalnie) denoiser w trybie gry — budżet: < 3 % jednego rdzenia; zmierzyć na i5/Ryzen 5.
6. Czy `System.Windows.Automation` (UIA2 w WPF) wystarcza do dowodów, czy potrzebny UIA3 (FlaUI.UIA3) także w produkcie.
