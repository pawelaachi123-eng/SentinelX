# RELEASE 0.96 · JARVIS — agent z narzędziami, wizja, pamięć semantyczna, pogoda, multimedia, dom, sekwencje

Data: 2026-09-26. Gałąź: `arena/01a0df47-sentinelx`. Wersja: `0.96 · JARVIS`, pakiet `0.96.0`.
Bez wydania EXE — publikacja wstrzymana do jawnego polecenia użytkownika.

## Zamówienie użytkownika

„Dodaj funkcje które są w każdym dobrym jarvisie na githubie i nie tylko, i też które mają modele,
AI — wszystkie najważniejsze/najlepsze. I zrób o wiele lepsze UI przez modern UI albo coś takiego”.

Uściślenia z rozmowy: „modern UI” = wygląd Fluent/WinUI **w obecnym WPF, bez nowych pakietów**
(przepis na WinUI 3 i wdrożenie WPF-UI/lepoco były zaproponowane i odrzucone); zakres = duży pakiet:
agent z tool-callingiem (lokalny model wywołuje funkcje), wizja „co jest na ekranie”, pamięć
semantyczna (embeddingi), pogoda (Open-Meteo bez klucza), multimedia (play/pauza/następny, „co gra”),
rutyny/sekwencje, smart home (Home Assistant lokalnie) — z fallbackami offline i regresjami w CI.
Sieć jest dopuszczalna tylko tam, gdzie funkcja mówi „wymaga internetu”, cache'uje i uczciwie
odmawia bez połączenia.

## 1. Co doszło (każda rodzina ma własny rozdział)

| Polecenie | Co robi | Warunki |
|---|---|---|
| `pogoda` · `pogoda Kraków` · `sprawdź pogodę w Krakowie` | temperatura, wiatr, wilgotność, zakres dnia, szansa opadu | **wymaga internetu** (Open-Meteo, bez konta i klucza); cache w `Cache/weather/<miasto>.json` z TTL z ustawień |
| `czy będzie padać` | werdykt opadowy na dziś/jutro z procentem | j.w. |
| `temperatura na zewnątrz` · `ile stopni` | skrót do bieżącego odczytu | j.w. |
| `co gra` | która aplikacja odtwarza (z listy procesów) | offline; tytułu utworu NIE czytamy (brak SMTC bez zmiany TFM) |
| `pauza` · `wznow odtwarzanie` · `nastepny utwor` · `poprzedni utwor` · `stop odtwarzanie` · `wycisz odtwarzacz` | klawisze multimedialne przez `SendInput` (te same co z klawiatury) | offline; sukces = „klawisz wysłany”, bez udawania potwierdzenia |
| `głośniej` · `ciszej 10` | skok głośności z **odczytem zwrotnym** (Core Audio) | offline; rozmiar skoku 1–50%, inaczej odmowa |
| `dom status` · `dom lista` | Home Assistant w LAN: konfiguracja i encje | wymaga włączenia w Ustawienia → Jarvis; token TYLKO ze zmiennej `SENTINEL_HA_TOKEN` |
| `dom: włącz światło salon` · `dom: wyłącz wentylator` · `scena: noc` | `turn_on`/`turn_off` po biały listcie domen | potwierdzenie przez ponowny odczyt `/api/states`; odmowa przy dwóch dopasowaniach |
| `agent: <cel>` | lokalny model pyta o narzędzia i dostaje ich wyniki | tryb musi być włączony; limit kroków 1–8; **tylko odczyty** |
| `agent status` · `narzedzia agenta` | stan trybu i pełna lista dozwolonych narzędzi | bez Ollamy: uczciwy komunikat |
| `co jest na ekranie` · `przeczytaj ekran` · `opisz <coś> na ekranie` | lokalny model wizyjny opisuje zrzut | obraz **nie jest zapisywany na dysk** (bufor w pamięci) |
| `indeks semantyczny: zbuduj` · `szukaj semantycznie: fraza` · `indeks semantyczny: status` · `indeks semantyczny: usuń` | pamięć wektorowa wspomnień i rozmowy | osadzenia liczone lokalnie przez Ollamę |
| `utwórz sekwencję: poranek = który jest dzień; plan dnia` · `sekwencje` · `podgląd sekwencji: X` · `uruchom sekwencję: X` · `usuń sekwencję: X` | nazwane listy kroków z podglądem | kroki niszczące odrzucane przy zapisie i przy odczycie |
| `jarvis` | opis całej warstwy z jej warunkami | offline |

Wszystko powyżej idzie przez **ten sam potok** co pozostałe polecenia: pojedyncza kolejka
`ActionEngine`, STOP awaryjny z zatrzaskiem, centrum zgód ( LOW…CRITICAL ), audyt z `requestId`
i dowodami. Nie ma drugiej, cichej ścieżki wykonania.

## 2. Agent (`Services/Jarvis/AgentService.cs` + `AgentToolCatalog.cs`)

- Model dostaje pole `tools` w kształcie OpenAI (`{"type":"function","function":{…}}`) i odpowiedź
  niestrumieniowaną, bo strumień z równoległymi wywołaniami narzędzi gubi kolejność.
- Pętla: `Math.Clamp(AgentMaxSteps, 1, 8)` kroków. Każde żądanie narzędzia jest tłumaczone na
  **zdanie z katalogu poleceń** (`ile mam ramu`, `top procesy`, `policz …`), a to zdanie idzie przez
  `StepRunner` → `IActionEngine` → `IntentRouter`. Dzięki temu krok agenta ma własny `requestId`,
  własny kontekst zgody (`ApprovalContext`) i podlega STOP-owi dokładnie jak wpisane polecenie.
- Narzędzie spoza katalogu = **odmowa wciśnięta z powrotem do rozmowy z modelem** (np. model poprosi o
  „usun pliki”), nic nie jest wykonywane. Krok zablokowany (oczekująca zgoda / błąd) jest zgłaszany
  jako `Krok zablokowany (STATUS)` i model dostaje go jako treść narzędzia.
- Odpowiedź końcowa ma nagłówek (`model · wywołania · wykonane kroki · limit`), transkrypcję każdego
  kroku z dowodem („dowód: tak” / „dowód: brak (polecenie wysłane, skutek niepotwierdzony)”) i
  przypomnienie, że agent **nie ma dostępu do zgody „potwierdź”**.
- Wybór modelu: `AgentModel` z ustawień → `LocalAiService.LastModel` → pierwszy `qwen3*` → pierwszy
  zainstalowany. HTTP 400 mówiący o braku obsługi narzędzi → osobny komunikat („wybierz model z
  obsługą narzędzi”), nie ogólny błąd.

## 3. Wizja (`VisionService.cs`) i `Core/ScreenCapture.cs`

`CaptureForVision(maxDimension = 1568)` robi zrzut wirtualnego pulpitu, skaluje go przez GDI+ i
zwraca **bajt PNG w pamięci** — bez pliku na dysku (stare „zrzut ekranu” z 0.95 nadal zapisuje plik,
bo tam tego oczekuje użytkownik). Limit 3,5 MB po base64, odpowiedź w 120 s. Dobór modelu:
ustawienie → pierwszy zainstalowany z listy znaczników (`llava`, `llama3.2-vision`, `minicpm`,
`moondream`, `qwen2.5vl`, `qwen2-vl`, `gemma3`, `granite-vision`, `phi4-multimodal`, `glm-4v`,
`pixtral`). Brak modelu = odmowa z gotowymi `ollama pull …` i listą tego, co jest zainstalowane.

## 4. Pamięć semantyczna (`SemanticMemoryIndex.cs`)

- Źródła: **tylko** trwałe wspomnienia i wypowiedzi bieżącej rozmowy (bez plików z dysku), cap 400
  pozycji i 600 znaków na pozycję.
- Żądanie: `/api/embeddings {model,prompt}`, a przy braku końca — `/api/embed {model,input:[…]}`.
- Plik `Memory/semantic-index.json` zapisywany atomowo: plik tymczasowy → **odczyt zwrotny**
  (liczba pozycji musi się zgadzać) → kopia `.previous` → podmiana. Plik nieczytelny jest odkładany
  jako `.corrupt-<ts>` i nigdy nie nadpisywany. `usun indeks` wykonuje `File.Move` na
  `.removed-<ts>` — wspomnienia pozostają.
- Wyszukiwanie: cosinus z progiem 0,20, maks. 20 wyników, z guardami na NaN i niezgodne wymiary.
  **Wyszukiwanie tekstowe („szukaj wszystkiego”, „szukaj w rozmowie”) zostało bez zmian** — indeks
  jest dodatkiem, nie następcą, i wynik zawsze dostaje zastrzeżenie, że podobieństwo wektorowe nie
  jest prawdą o treści.
- Całość wyłączona dopóki `SemanticSearchEnabled` nie zostanie włączone.

## 5. Sekwencje (`RoutineService.cs`) — domyka BACKLOG P0 „sekwencje działań”

- `Memory/routines.json` z tym samym zestawem gwarancji co wyżej (atomowo, odczyt zwrotny,
  `.corrupt-…`).
- Podział kroków **tylko** po `;` i nowym wierszu — `policz 2+2` zostaje jednym krokiem.
- `podgląd sekwencji:` deklaruje „Żadnego kroku nie wykonałem”; `uruchom sekwencję:` leci krok po
  kroku przez kolejkę i **staje** na błędzie, na kroku oczekującym zgody i na kroku niszczącym
  (pozostałe kroki nie są wysyłane); po biegu zapisuje podsumowanie (`1 wykonanych, 1 problematycznych`).
- Lista zakazów (`usuń`, `skasuj`, `wyczyść`, `formatuj`, `nadpisz`, `zamknij`, `wyłącz`, `zabij`,
  `potwierdź`, `reboot`, `shutdown`) obowiązuje **przy zapisie i przy odczycie pliku** — plik można
  otworzyć w edytorze, ale nie da się przez to przemycić kroku do wykonania.

## 6. Interfejs: panel JARVIS i wykończenie okna

- **Jedna nowa zakładka w Centrum**: 🤖 JARVIS (`Views/Controls/JarvisPanel.xaml`, 7 sekcji: pogoda,
  multimedia, dom, sekwencje, agent, ekran, pamięć). Panel nie ma własnej ścieżki wykonania — każdy
  przycisk wysyła tekstowe polecenie przez `IActionEngine`, więc STOP, zgody i audyt działają tak
  samo jak w czacie, a w raporcie widać dokładnie to, co odpowiedział system. Ostatnie 8 wysłań
  zostaje na liście z godziną.
- **Nowa sekcja w Ustawieniach**: „Jarvis” (miasto, pobieranie prognozy, TTL, tryb agenta, limit
  kroków, model narzędzi / wizyjny / osadzeń, indeks semantyczny, integracja z domem, adres HA) —
  przez `SettingsCatalog`, więc dostaje walidację pól i „Przywróć domyślne tej sekcji” za darmo.
- **Wygląd**: `Tło okna` (Mica / Akryl / Brak) i `Ciemny pasek tytułu`
  (`Utilities/WindowEffects.cs`, atrybuty DWM 20/33/38, gatekowane `OperatingSystem.IsWindowsVersionAtLeast`,
  błąd DWM = brak efektu, nigdy wyjątek; podkład liczony z tokenu `SxBackgroundColor`, bez nowych
  pakietów i bez hardkodowanych kolorów w `Views/**`).
- **Skróty `//`**: `//pogoda`, `//pada`, `//gra`, `//pauza`, `//ekran`, `//agent`, `//narzedzia`,
  `//dom`, `//encje`, `//sekwencje`, `//indeks`, `//zbuduj`, `//jarvislist`, `//jarvis` (zakładka).
- Katalog pól wpisywanych (`IntentCatalog.Phrases`) poznał nowe zdania, więc literówki w stylu
  „pogoda2” dostają „Czy chodziło Ci o…”, a nie odpowiedź modelu.

## 7. Czego świadomie NIE ma ta wersja

- **Samomodyfikacja kodu** — odrzucona na stałe; `ulepsz sie` dalej dotyczy tylko danych i wzorców.
- Integracje GitHub/Gmail — nadal poza zakresem (decyzja użytkownika).
- Modele 3D / „pokaż hologram” — dalej uczciwa odmowa, bez udawania.
- Pobieranie modeli: Sentinel nic nie ściąga bez jawnej decyzji; przy braku modelu widać komendę
  `ollama pull …` do wpisania samodzielnie.
- Sterowanie domem przez chmurę producentów — tylko lokalny Home Assistant.
- Agent nie ma narzędzi zmieniających dane i nie ma dostępu do zgody; sekwencje nie przyjmują
  kroków niszczących; „potwierdź” pozostaje kliknięciem w oknie (głos nie zatwierdza).

## 8. Regresje i braki

- `tests/JarvisRegression.cs` (nowa, wpięta w `--ui-smoke`): rozpoznawanie poleceń i **brak kradzieży**
  istniejących („włącz spotify” nadal launcher), granice odmów (brak zgody, brak urządzenia, brak
  modelu), parsowanie Open-Meteo/OpenAI na wstrzykniętym HTTP, cache bez drugiego żądania, zapis/odczyt
  listy sekwencji z odkładaniem uszkodzonego pliku, zatrzymanie sekwencji na błędzie, spójność
  katalogu narzędzi z katalogiem poleceń (każde narzędzie musi prowadzić do zdania, które routing
  rozpoznaje), walidacja ustawień (adres z loginem odrzucony, nazwa modelu odrzucona).
- `UiSmokeTestRunner`: panel JARVIS renderuje się w Centrum (pętla po `chat.Sections` robi zrzut
  każdej zakładki), nowe komendy odpowiadają przez **pełny** silnik, licznik kart gotowości 4 → 6,
  asercja wersji 0.93 → 0.96.
- Nie zweryfikowane lokalnie: nic. W tym sandboksie nie ma .NET SDK ani sieci — cały C# był
  sprawdzany bramką `scripts/check-architecture.py` (PASS) i **kompilacją w CI na Windows**
  (`windows-build.yml` na `arena/**`). Logika z Ollamą, DWM i `SendInput` wymaga sprawdzenia na
  żywym Windows — CI ich nie ma.
