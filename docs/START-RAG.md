# RAG w Sentinelu — jak odpalić na swojej maszynie (po pobraniu repo)

## Co dostajesz
RAG (Retrieval-Augmented Generation) **bez chmury**: Sentinel tnie Twoje pliki `.txt`/`.md` na
fragmenty, liczy dla każdego fragmentu **wektor lokalną Ollamą** (`nomic-embed-text`,
wyłącznie `127.0.0.1:11434`), a szukanie to kosinus po wektorach. Baza żyje **tylko w RAM
aplikacji** — po zamknięciu znika, na dysk nie zapisuje się nic, nic nie wychodzi poza komputer.

## Jedno polecenie (po pobraniu repo, na Windows)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-rag.ps1
```

Skrypt **sprawdza** środowisko (dotnet, ollama), **pobiera przez Ollamę** modele
`nomic-embed-text` i `qwen3:4b-instruct` (to jedyny moment z internetem — pobiera Twoja
lokalna Ollama) i wypisuje kolejne kroki. **Nie buduje EXE, nie instaluje niczego w systemie.**

## Polecenia w aplikacji (w czacie Sentinela)

| Polecenie | Co robi |
|---|---|
| `rag zbuduj: C:\folder\z\plikami` | tnie .txt/.md na fragmenty (limit 200 plików, fragmenty ≤ 800 znaków z zakładką) i liczy wektory |
| `rag szukaj: fraza` | wektor frazy → kosinus z bazą → top 5 z procentem podobieństwa i nazwą pliku |
| `rag prompt: pytanie` | składa gotowiec: pytanie + top 3 fragmenty + lista źródeł — do wklejenia w czat |
| `rag model: nomic-embed-text` | zmiana modelu embeddingów (zmiana **nie** przelicza istniejącej bazy) |
| `rag status` | ile plików/fragmentów, źródło, model |
| `rag reset` | czyszczenie bazy (była tylko w RAM) |

## Bez Ollamy
Każda operacja kończy się **jawnym komunikatem** z adresem `127.0.0.1:11434` — żadnych
wyciszonych awarii, żadnego zgadywania. Nieudana budowa **nie czyści** istniejącej bazy.

## Co jest przetestowane bez sieci (CI Windows)
`tests/RagRegression.cs` — cięcie fragmentów, matematyka kosinusa (1 / 0 / nie-NaN),
budowa i ranking na deterministycznych fejkowych wektorach (worki słów w 16 kubełkach),
prompt ze źródłami, uczciwość bez Ollamy, reset. RAG przechodzi przez **realny router**.

## Co dalej (opcje, gdy przetestujesz u siebie)
- trwałe archiwum bazy (zaszyfrowany plik w danych aplikacji — na Twoją zgodę),
- `rag pytaj: …` — automatyczne pytanie do lokalnego modelu rozmowy z gotowym promptem,
- indeksowanie PDF/DOCX (teraz tylko .txt/.md).
