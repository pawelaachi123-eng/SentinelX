# Lokalny RAG w SentinelX — .NET 10 / Windows

Baza robocza działa w pamięci. Opcjonalne archiwum jest szyfrowane Windows DPAPI i powstaje dopiero po planie oraz potwierdzeniu użytkownika. Aplikacja łączy się tylko z Ollamą na `127.0.0.1:11434`.

## Wymagania i przygotowanie

Zainstalowane .NET 10 SDK, lokalna Ollama, model embeddingów `nomic-embed-text` oraz lokalny model rozmowy. Ta zmiana nie instaluje ani nie pobiera modeli.

Istniejący `scripts/setup-rag.ps1` uruchamia pobieranie modeli przez Ollamę. Nie uruchamiaj go wyłącznie w celu sprawdzenia środowiska. Pobieranie wymaga osobnej świadomej zgody; panel AI i polecenie `model pobierz: nazwa` zapewniają plan oraz jednorazowe potwierdzenie.

## Polecenia

| Polecenie | Działanie |
|---|---|
| `rag zbuduj: C:\dokumenty` | .txt/.md → fragmenty do 800 znaków, zakładka 120 w długich akapitach, do 200 plików / 400 fragmentów |
| `rag szukaj: fraza` | do 5 trafień kosinusowych; procent to podobieństwo, nie pewność odpowiedzi |
| `rag prompt: pytanie` | gotowy prompt z maksymalnie 3 fragmentami i źródłami |
| `rag pytaj: pytanie` | odpowiedź lokalnego modelu, odsyłacze oraz literalne cytaty z fragmentów |
| `rag model: nomic-embed-text` | model dla kolejnej budowy; istniejąca baza zachowuje model swoich wektorów |
| `rag status` | liczba plików/fragmentów, źródło, model ustawiony i model bazy |
| `rag archiwa` | lista do 200 nazw zapisanych kopii |
| `rag zapisz: notatki` | plan zapisu zaszyfrowanej kopii |
| `rag zapisz: notatki potwierdzam` | zapis po planie, bez nadpisywania istniejącego pliku |
| `rag wczytaj: notatki` | plan zastąpienia RAM zawartością archiwum |
| `rag wczytaj: notatki potwierdzam` | walidacja i przywrócenie bazy oraz modelu embeddingów |
| `rag reset` | wyczyszczenie RAM; zapisane archiwa pozostają na dysku |

Nazwa archiwum: 1–60 liter ASCII, cyfr, `_` lub `-`, bez ścieżki i nazw urządzeń Windows. Zgoda wygasa po 10 minutach; zmiana bazy lub modelu unieważnia plan. Nie można jej powtórnie wykorzystać. Zapis jest blokowany w trybie prywatnym.

## Co chroni archiwum

Pliki leżą w `RagArchives` pod katalogiem danych SentinelX (domyślnie `%LOCALAPPDATA%\SentinelX`). Zaszyfrowane są fragmenty, wektory, model i źródło bazy. Nazwa pliku archiwum pozostaje jawna. DPAPI wymaga kontekstu tego konta Windows; to nie jest format przenośnej kopii między kontami. Nie chroni przed procesem działającym już z uprawnieniami tego użytkownika.

Zapis używa pliku tymczasowego z samym szyfrogramem i przeniesienia bez nadpisania. Wczytanie sprawdza wersję formatu, rozmiar (64 MiB), limit fragmentów i skończone wektory o zgodnych wymiarach przed zmianą RAM. Błąd odszyfrowania pozostawia obecną bazę.

Archiwum nie obejmuje historii rozmów. Wynik `rag pytaj` może trafić do zwykłej historii czatu zgodnie z ustawieniami prywatności — samo szyfrowanie archiwum nie szyfruje historii.

## Uczciwość odpowiedzi i ograniczenia

Puste zapytanie, pusta baza, brak trafień lub niedostępna Ollama dają jawny komunikat. Brak trafień nie uruchamia generowania. Model otrzymuje polecenie traktowania dokumentów jako danych i ignorowania instrukcji zawartych w nich. Sprawdzamy zakres numerów odsyłaczy, ale nie automatyczną zgodność każdego zdania z cytatem. Cytaty pokazujemy, aby użytkownik mógł to sprawdzić.

Brak importu PDF/DOCX, automatycznego zapisu bazy, usuwania archiwów z czatu i niezależnego hasła przenośnej kopii. Nie testowano jakości odpowiedzi na rzeczywistej Ollamie.

## Weryfikacja

Kod `845f4b6`: [Windows build and WPF smoke](https://github.com/pawelaachi123-eng/SentinelX/actions/runs/36347938885) oraz [Build Windows app (.NET 10)](https://github.com/pawelaachi123-eng/SentinelX/actions/runs/36347938810) — **success**.

`RagRegression`: dzielenie, kosinus i ranking; `RagArchiveRegression`: szyfrowanie, odczyt nową instancją magazynu, uszkodzenie, format, wymiary i zgody; `RagAnswerRegression`: odpowiedź przez router, cytaty, model bazy, brak trafień, awaria i anulowanie. Wszystko bez pobierania modeli. `ModelPanelRegression` sprawdza plan oraz dokładne potwierdzenie w VM; WPF smoke renderuje stronę AI.
