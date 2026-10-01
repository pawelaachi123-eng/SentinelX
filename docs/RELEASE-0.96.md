# SENTINEL X 0.96 · KUŹNIA — notatki wydania

Wydanie porządkujące i rozszerzające warsztat z 0.95: **podgląd decyzji**, **szukanie w zadaniach**, **11 nowych narzędzi**
i **porządek w kodzie** (podział na `Brain/`, `Tools/`, `Testing/`). Windows 10/11 x64, .NET 10 · aplikacja na Androida 7+.

## Co nowego

| Obszar | Zmiana |
| --- | --- |
| **Podgląd decyzji** | `jak to rozumiem: <polecenie>` mówi po polsku, co Sentinel by zrobił: naprawa literówki lub skrótu, które narzędzie z katalogu, czy to polecenie wymagające zgody, pytanie doprecyzowujące albo zwykłe pytanie do modelu. **Niczego nie wykonuje, nie zapisuje (podgląd `qr:` nie tworzy PNG), nie czyta plików i nie pyta modelu AI.** |
| **Szukanie w zadaniach** | `szukaj w zadaniach: fraza` (też `szukaj zadan:`, `znajdz zadanie:`) przeszukuje tytuły zadań — także zrobionych — i przypomnień. Wielkość liter i polskie znaki nie mają znaczenia, każde słowo frazy musi wystąpić. Tylko odczyt. **Naprawiony błąd:** to polecenie było opisane w pomocy, ale trafiało do wyszukiwarki internetowej. |
| **Kuźnia: 11 narzędzi** | `nazwa zmiennej:` (camelCase, PascalCase, snake_case, kebab-case, UPPER_SNAKE), `url zakoduj:` / `url odkoduj:`, `unix:` / `na unix:`, `czestosc slow:`, `rata kredytu: kwota lata oprocentowanie`, `porownaj wersje: A ||| B`, `numeruj linie:`, `odwroc linie:`, `popraw odstepy:`. Wszystkie są na stronie **Narzędzia** w kategorii „Kuźnia 0.96” (katalog liczy teraz 93 wpisy). |
| **Porządek w kodzie** | `Brain/` (rozumienie poleceń, pamięć rozmów, lokalny model), `Brain/Router/` (`CommandRouter`, `DecisionPreview`), `Tools/` (`UtilityToolbox`, `ForgeTools`, `SentinelToolboxService`, narzędzia procesów), `Testing/` (smoke UI, self-test, runnery). Przeniesienia nie zmieniają przestrzeni nazw ani zachowania. |
| **Kontrole przed buildem** | Nowy `scripts/check-syntax-lite.py` (nawiasy, napisy, duplikaty typów i plików, trasowanie narzędzi Kuźni) oraz rozszerzony `scripts/check-architecture.py` (układ `Brain/`/`Tools/`, brak WPF w tych katalogach). To siatka bezpieczeństwa, **nie kompilator** — kompiluje CI. |

## Jak to jest zrobione (i dlaczego bezpiecznie)

- **Podgląd to czysta funkcja nad katalogami** (`Core/ToolCatalog.cs`, `Core/IntentCatalog.cs`, `CommandUnderstanding`). Nie ma w niej wywołania narzędzia, więc nie ma skutków ubocznych; dotyczy to także narzędzi, które normalnie piszą na dysk (QR) albo czytają plik (skrót pliku) — podgląd tylko o tym uprzedza.
- **Polecenia niszczące** (usuń, zamknij, wyłącz, wyczyść…) podgląd opisuje jako wymagające świadomej zgody; głos jej nadal nie udziela. Zgody, dowody (`VerifiedSuccess`) i historia akcji — bez zmian.
- **Szukanie w zadaniach** idzie przez `TaskService.Search` (odczyt pod tym samym zamkiem co reszta magazynu) i nie zmienia ani jednego wpisu.
- **Narzędzia Kuźni to czyste funkcje**: bez sieci, bez plików, bez procesów. Rata kredytu jest wyraźnie opisana jako rachunek orientacyjny (raty równe, bez prowizji i ubezpieczeń).
- **Zero nowych zależności.** Zmiany dotyczą wyłącznie kodu w repozytorium.

## Co weryfikuje CI na dokładnie tych plikach, które pobierzesz

1. `scripts/check-architecture.py` i `scripts/check-syntax-lite.py`.
2. Kompilacja i publikacja samodzielnej paczki win-x64 (z wbudowanym .NET i silnikiem AI).
3. **Smoke UI**, a w nim nowe zestawy: `ForgeRegression` (wartości wszystkich 11 narzędzi, w tym znane stałe: `unix: 1700000000` = 14.11.2023 22:13:20 UTC, rata 300 000 zł / 25 lat / 7,5 % = 2 216,97 zł) i `RoutingRegression` (podgląd decyzji w wielu odmianach, brak tworzenia PNG przy podglądzie `qr:`, zgodność katalogu z podglądem dla każdego z 93 wpisów, szukanie w zadaniach przez prawdziwy `IntentRouter`). Dochodzą polecenia przez prawdziwy `ActionEngine` oraz asercje `wersja` → 0.96 i `co nowego` → KUŹNIA.
4. Testy regresji (`--self-test`) i instalator EXE: kompilacja, instalacja per-user, smoke UI zainstalowanej aplikacji.
5. Build aplikacji na Androida (zmieniła się tylko wersja: `0.96.0`, `versionCode 96`).

## Ograniczenia, o których warto wiedzieć

- Podgląd decyzji opisuje **deterministyczne** warstwy (literówki, katalog narzędzi, znane polecenia). Pomiary systemowe, pamięć i projekty rozpoznaje jako „znane polecenie” bez pokazywania wyniku; nie przewiduje odpowiedzi modelu AI.
- Przy poleceniach z liczbą w opisie (`ile dni do …`) podgląd rozpoznaje narzędzie po początku polecenia — pokrewne polecenia o tym samym początku mogą zostać opisane jako najbliższe narzędzie katalogu.
- `czestosc slow:` liczy słowa dosłownie (bez lematyzacji: „kot” i „kota” to dwa słowa).
- `na unix:` bez dopisku `utc` używa strefy czasowej komputera.
- Rata kredytu nie uwzględnia prowizji, ubezpieczeń ani zmiennego oprocentowania.
