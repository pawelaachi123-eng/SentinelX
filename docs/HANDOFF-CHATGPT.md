# BRIEFING — wklej w całości do ChatGPT (albo innego asystenta)

Poniżej gotowy tekst. Skopiuj wszystko od „--- START ---” do „--- KONIEC ---” i wklej do czatu.

--- START ---

Kontynuujesz pracę nad projektem SentinelX. Przeczytaj całość i trzymaj się tego briefingu.

## Czym jest projekt
SentinelX = lokalny asystent na Windows: WPF, .NET 10 (net10.0-windows), C# 14, MVVM
(CommunityToolkit.Mvvm), AI wyłącznie przez lokalną Ollamę na 127.0.0.1:11434 (OllamaSharp),
ASR przez Whisper.net, NAudio do dźwięku. Polski interfejs. ZERO chmury i telemetrii.
Repo: github.com/pawelaachi123-eng/SentinelX, branch: arena/01a0e28e-sentinelx.

## Zasady nadrzędne (nie do negocjacji)
1. NIGDY nie buduj EXE/instalatora/artefaktów. Tylko gdy użytkownik wprost napisze „zbuduj exe”.
   W CI kroki pakowania są wygaszone bramką workflow_dispatch — tego nie zmieniaj.
2. „Działa” = zbudowane i przetestowane na Windows CI (zielony run). Bez dowodu nie deklarujesz.
3. Uczciwość: nigdy „wszystko gotowe” — zawsze jawne liczby per sekcja i lista braków.
4. Sieć: TYLKO loopback do Ollamy. Żadnych pobierania stron, API zewnętrznych, telemetrii.
5. Operacje destrukcyjne (usuwanie, pobieranie modeli): zgoda dwuetapowa — plan, potem dokładnie
   „… potwierdzam”; zgoda jednorazowa, związana z nazwą, wygasa po 10 minutach.

## Struktura kodu
- CommandRouter.cs — jedyny dispatch poleceń. Kolejność ma znaczenie: snapshoty → UtilityToolbox →
  RuntimeCommands → DeveloperToolbox → ModelToolbox (+ zarządzanie modelami) → ArchitectureToolbox →
  ResearchToolbox → KnowledgeToolbox → KnowledgeIndexService → KnowledgeRagService → GoalToolbox →
  FullStackToolbox → SmartHomeToolbox → MediaVisionToolbox → AnalysisToolbox (na górze dispatch
  „wykres”) → LifeToolbox → PrivacyToolbox → ProductivityToolbox → CodeInsights → meta/Jarvis →
  AI fallback. (Nowe moduły wstawiaj PRZED Analysis/Life — np. „tempo mowy” musi wygrać z „tempo”.)
- Moduł narzędzi = plik XToolbox.cs z `public static string? TryHandle(string command, string text)`.
  Wzorce wewnątrz: `Flat()` (zdejmuje polskie znaki), `Is()`, `Payload()`, `Numbers()` — przed
  podziałem na tokeny normalizuj przecinek dziesiętny: `Regex.Replace(input, @"(?<=\d),(?=\d)", ".")`.
- tests/XRegression.cs — regresje (`Check(cond, msg)`), wpinane w UiSmokeTestRunner.RunAsync.
- Core/IntentCatalog.cs — frazy rozumienia. KONTRAKT: żadna fraza nie może zawierać czasowników
  destrukcyjnych (usun, zamknij, wylacz, czysc, kill, sformatuj, potwierdz, zatrzymaj) — test pilnuje.
- Core/CommandCatalog.cs — paleta „//” (nazwa, opis, słowa kluczowe, CommandText albo PageKey).
- Help i changelog: stringi w CommandRouter.cs — dopisuj wiersz do Help i punkt do „co nowego”.
- scripts/check-architecture.py — lokalna bramka; uruchamiaj PRZED KAŻDYM pushem (PASS obowiązkowy).
- scripts/audit-1550.py — licznik wyzwalaczy poleceń per moduł → docs/AUDYT-1550-AUTO.md.
- Docs przy każdym przyroście: docs/RELEASE-0.97.md, README.md, docs/BACKLOG.md, docs/AUDYT-1550.md.

## Procedura dodawania funkcji (krok po kroku)
1. Moduł z TryHandle; liczby wg wypisanych wzorów; gdy brak argumentów → uczciwe „Użycie: …”;
   nieznane wartości → „nie znam/nie zgaduję”, nigdy zmyślanie.
2. Wpięcie w CommandRouter (przed Analysis/Life).
3. Frazy do IntentCatalog (bez destrukcyjnych!), 1–2 wpisy do CommandCatalog, wiersz w Help, punkt w changelogu.
4. Regresja tests/XRegression.cs z liczbami sprawdzonymi RĘCZNIE (każda asercja policzona).
   Aserty muszą zgadzać się litera w literę z komunikatami modułu (CI przyłapie różnice).
5. Lokalnie: python3 scripts/check-architecture.py (PASS) + audyt klamer/nawiasów w zmienionych plikach.
6. Commit (opis: co i dlaczego) → push → czekaj ~200 s → sprawdź CI:
   gh run list --branch <branch> --json name,status,conclusion,headSha --jq '.[]'
   (zawsze filtruj po branchu i nazwie workflow — inne agenty pushują równolegle).
   Przy FAIL: gh api repos/pawelaachi123-eng/SentinelX/check-runs/<jobId>/annotations --jq '.[] | .message'
7. Aktualizacja docs + osobny commit.

## RAG — już jest w repo, jak uruchomić u użytkownika
Wymagania: .NET 10 SDK (dotnet.microsoft.com) i Ollama (ollama.com/download).
Kroki: 1) pobierz repo (branch arena/01a0e28e-sentinelx), 2) PowerShell w katalogu repo:
`powershell -ExecutionPolicy Bypass -File scripts\setup-rag.ps1` (sprawdza dotnet/ollama, pociąga
modele nomic-embed-text i qwen3:4b-instruct — nie buduje EXE), 3) `dotnet run`,
4) w czacie aplikacji:
- `rag zbuduj: C:\folder\z\plikami` — tnie .txt/.md na fragmenty (≤800 znaków, zakładka 120,
  limit 200 plików/400 fragmentów) i liczy wektory lokalną Ollamą; baza ŻYJE TYLKO W RAM;
- `rag szukaj: fraza` — kosinus, top 5 z procentem i nazwą pliku;
- `rag prompt: pytanie` — gotowiec: pytanie + top 3 fragmenty + źródła, do wklejenia w czat;
- `rag status`, `rag reset`, `rag model: nomic-embed-text`.
Bez Ollamy: jawny komunikat z adresem 127.0.0.1:11434; nieudana budowa nie czyści bazy.
Testy RAG nie potrzebują Ollamy (fejkowe deterministyczne wektory — worki słów w 16 kubełkach).

## Stan projektu (0.97, CI zielone)
Wszystkie 20 sekcji listy funkcji ma moduły: rdzeń (86 wyzwalaczy), modele lokalne z pobieraniem
za zgodą dwuetapową, prywatność, kodowanie+utility, architektura offline (graf/cykle/warstwy/dług/ADR),
full-stack generatory (REST/OpenAPI/SQL/paginacja), wiedza (fiszki/Anki/podobieństwo kosinusowe/indeks
plików w RAM), RAG, media (WCAG/PPI/audio/decybele), research offline (cytowania/wiarygodność),
smart home offline (energia/YAML/MQTT), agentic (cele/ryzyko/wycofanie/SMART), produktywność
(język/i18n/webhook/sesje), GUI: Studio 0.97 z 13 stronami i wykres PNG („wykres: 3 5 8 4 | Sty Lut”).
Liczniki: 713 wyzwalaczy poleceń, 599 fraz, 88 wpisów palety, 33 zestawy regresji.

## Czego świadomie NIE ma (nie udawaj, że jest)
Scraping z sieci, OCR ekranu, wake-word, streaming ASR, panel modeli w GUI, trwałe archiwum RAG
na dysku, sterowanie realnym sprzętem smart home, EXE (zakaz do odwołania).

## Pułapki, które realnie spaliły CI (nie powtarzaj)
- `(?:potwierdzam)` non-capturing nie ma Groups[N] — używaj grup przechwytujących.
- record C#: typ PRZED nazwą (`string Nazwa`, nie `Nazwa string`).
- przed dopasowaniem słów kluczowych zdejmij diakrytyki sam („zdjęć” ≠ „zdjec” bez tego).
- podział po przecinku psuje liczby dziesiętne („1,0”) — najpierw normalizacja (?<=\d),(?=\d).
- KeyValuePair ma .Key, nie .Name; nie istnieje sb.Append(string, int).
- „tempo mowy” vs „tempo”, „status http” vs „http” — kolizje prefiksów: nowy trigger sprawdzaj
  greppem w starych modułach.
- Po każdej edycji pliku zweryfikuj greppem, że zmiana JEST w pliku (środowisko potrafi utracić edycje).
- Push rejected „fetch first”: git fetch → porównaj drzewa → jeśli różnica to tylko twoja praca,
  git reset --soft FETCH_HEAD i commit ponownie. Nigdy force-push.

## Zadanie na start (gdy bierzesz kontynuację)
1. python3 scripts/check-architecture.py → musi być PASS; python3 scripts/audit-1550.py → tabela.
2. Przeczytaj docs/BACKLOG.md (lista braków) i docs/START-RAG.md (uruchomienie RAG u użytkownika).
3. Kandydaci na następny przyrost: trwałe zaszyfrowane archiwum bazy RAG, „rag pytaj:” (automatyczna
   odpowiedź lokalnego modelu z cytowaniami), panel modeli w GUI. Każde z regresją i zielonym CI.
4. Pamiętaj: żadnego EXE, żadnej chmury, żadnego „wszystko zrobione” bez liczb.

--- KONIEC ---
