# Wbudowany silnik AI (0.94) — bez Ollamy

Od 0.94 Sentinel X nie potrzebuje Ollamy. Model językowy uruchamia **llama.cpp** (`llama-server`, licencja MIT) — ten sam silnik inferencji,
na którym zbudowana jest Ollama — jako ukryty proces potomny. Użytkownik niczego nie instaluje, nie uruchamia ani nie naprawia.

## Jak to jest złożone

```
LocalAiService (bez zmian: routing modeli, strumień, fallbacki, filtr „myślenia”)
   │  HTTP w dialekcie Ollamy: /api/tags, /api/show, /api/chat, /api/generate — ale w procesie, bez portu sieciowego
   ▼
EngineOllamaFacade : HttpMessageHandler  ──►  EngineService (IEngineRuntime)  ──►  LlamaServerHost  ──►  llama-server.exe
   tłumaczy na /v1/chat/completions (SSE)        instalacja, wybór modelu,            127.0.0.1, losowy port,
                                                 zwalnianie pamięci                   losowy klucz w zmiennej środowiskowej
```

Dzięki fasadzie cała istniejąca, przetestowana logika AI została nietknięta (zmieniły się tylko komunikaty dla użytkownika). `OllamaSharp` usunięto z projektu.

## Pliki i miejsca

| Co | Gdzie |
| --- | --- |
| Silnik (llama.cpp b11146, CPU x64) | dołączony do instalatora w `…\SentinelX\Engine\llama\`; jeśli go brak (np. skopiowany sam `SentinelX.exe`), pobierany do `%LOCALAPPDATA%\SentinelX\Engine\llama-b11146\` |
| Modele (GGUF) | `%LOCALAPPDATA%\SentinelX\Engine\models\` |
| Pobieranie w toku | `*.part` obok docelowego pliku (wznawiane HTTP Range) |
| Przypięte wersje i sumy | `Services/Engine/EngineCatalog.cs` (jedno źródło prawdy; `scripts/fetch-llama.ps1` czyta stąd adres i SHA-256) |

## Modele

| Nazwa w aplikacji | Plik | Rozmiar | Kiedy |
| --- | --- | --- | --- |
| `qwen3:1.7b` | Qwen3-1.7B-Q4_K_M.gguf | 1,1 GB | zawsze; używany podczas gry i przy obciążeniu |
| `qwen3:4b-instruct` | Qwen3-4B-Instruct-2507-Q4_K_M.gguf | 2,5 GB | gdy komputer ma ≥ 12 GB RAM i > 5,5 GB wolnego miejsca |

Nazwy są te same co wcześniej w ustawieniach (`Ai.GamingModel`, `Ai.IdleModel`), więc wybór „lekki podczas gry, mocniejszy przy luzie” działa jak dotąd.
Modele: Apache-2.0 (Qwen, repozytoria `unsloth/*-GGUF` przypięte do konkretnych rewizji). Każdy plik trafia pod docelową nazwę dopiero po zgodności rozmiaru i **SHA-256**.

## Cykl życia (wszystko automatyczne)

1. Przy starcie aplikacji opiekun (`CareService`) wywołuje `EngineService.Start()`. Brakujący runtime i modele pobierają się w tle (najpierw lekki, żeby rozmowa ruszyła szybko).
2. Pobieranie **wstrzymuje się na czas gry**; przy błędzie sieci ponawia z rosnącą przerwą (2, 4, 8, 16, 30 min) i nie przeszkadza w pracy.
3. `llama-server` startuje dopiero przy pierwszym pytaniu (z argumentami: loopback, bez interfejsu WWW, wyłączone „myślenie” Qwen3, 1 slot, ~połowa rdzeni, priorytet poniżej normalnego). Gdy jakaś flaga przestałaby być znana w nowszej wersji, host próbuje minimalnego zestawu argumentów.
4. Po odpowiedzi model zostaje w pamięci 10 minut (`keep_alive`) albo **zwalnia się od razu**, gdy działa gra (`keep_alive 0`).
5. Proces jest w **obiekcie zadania Windows** (kill-on-close): ginie razem z aplikacją, nawet po jej awarii.
6. Model, który dwa razy z rzędu nie chce się wczytać *z powodu samego pliku*, jest kasowany i pobierany od nowa (najwyżej dwa razy na uruchomienie; brak bibliotek Visual C++ nie kasuje modeli — instalator dokłada je obok silnika).

## Komendy i ustawienia

`status AI` — stan silnika i modeli · `napraw AI` — sprawdza pliki i dokańcza instalację · zakładka **AI** — stan, pasek postępu, wybór modelu ·
Ustawienia → AI — nazwy modeli, temperatura, kontekst, limity i progi obciążenia (bez zmian).
**Ustawienia → AI → „Pobieraj silnik AI automatycznie”** (domyślnie włączone): na łączu z limitem danych wyłącz — silnik niczego wtedy nie pobiera sam, a „napraw AI” / przycisk **Sprawdź i napraw** pobiera brakujące pliki jednorazowo, na Twoje żądanie.

## Czego nie robi

- Nie korzysta z GPU (wersja CPU jest przewidywalna na każdym komputerze). Odpowiedzi na typowym komputerze: kilka–kilkanaście tokenów na sekundę.
- Nie uruchamia żadnych narzędzi na podstawie tekstu modelu (jak dotąd): model tylko rozmawia, polecenia wykonuje deterministyczny router.
- Testy CI sprawdzają pobieranie, fasadę i argumenty oraz to, że `llama-server.exe --version` się uruchamia na czystym runnerze; **prawdziwa generacja z modelem nie była uruchomiona w CI** (modele mają gigabajty).
