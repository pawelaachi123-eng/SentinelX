# RELEASE 0.94 · ZROZUMIENIE — zdania, pamięć kontekstu i narzędzia

Data: 2026-09-26. Gałąź: `arena/01a0dda5-sentinelx`. Wersja aplikacji: `0.94 · ZROZUMIENIE`
(`Core/AppConstants.cs`), pakiet `0.94.0` (`SENTINEL-X.csproj`). Bez wydania EXE — publikacja
zostaje wstrzymana do jawnego polecenia użytkownika.

## Co było najważniejsze

Priorytetem użytkownika jest **lepsze rozumienie**: „żeby SentinelX zrozumiał, co chcę mu
powiedzieć”. 0.94 dokłada warstwę rozumienia zdań (nie tylko komend), pamięć kontekstu
dla dopytań i komplet nowych narzędzi offline.

## 1. Rozumienie zdań (`CommandUnderstanding`)

- `Understand()` rozpoznaje: **skróty** (`cs` → `wlacz cs2`), **literówki** (`ile mam ramuu` →
  `ile mam ramu`), **polecenia ukryte w zdaniu** („sprawdź proszę ile mam ramu” → `ile mam ramu`,
  z notą „pominąłem: sprawdz, prosze”) i **synonimy z odmianą** („odpal discorda” → naprawa
  `discorda` → `discord`, katalog folduje „odpal” do formy uruchamiania).
- Wynik zawsze pokazuje się jako **„Zrozumiałem jako: …”** — użytkownik widzi, co zostanie
  wykonane, zanim to się stanie.
- `Extract()` sięga **wyłącznie katalogu bezpiecznych fraz** (~310 pozycji) i odtwarza argumenty
  („ile dni do 24.12”). Katalog świadomie nie zawiera „usun”, „zamknij”, „wylacz”, „czysc”,
  „kill”, „sformatuj”, „potwierdz”, „zatrzymaj”.
- **Granice (świadome, testowane)**: negacja („nie pokazuj ram”), czasowniki prośby o treść
  („napisz mi wiersz o tym ile mam ramu”), słowa zakresu („dziś”, „teraz”) i cokolwiek
  destrukcyjnego **nigdy nie wykonują się automatycznie** — trafiają do „Czy chodziło Ci o…”
  albo do modelu AI. Wyrazy pytające („sprawdz”, „powiedz”, „prosze”) są dekoracją wokół
  polecenia, nie treścią.
- `zrozum: <zdanie>` — suchy pokaz kroków (normalizacja → skrót → literówka → ekstrakcja →
  szara strefa), **zero wykonania**. Przechwytywany w `IntentRouter` przed całą resztą routingu,
  więc nawet „zrozum: zamknij notatnik” nic nie zrobi.
- `Core/CommandLexicon.cs`: fold synonimów, ogierki odmiany (`Stem`), białe listy słów
  dekoracyjnych i pytających, `WordsMatch`/`SearchTokenMatches`.

## 2. Pamięć i kontekst

- **`SessionFactBook`** — skoroszyt faktów sesji: ostatnie odczyty narzędzi (RAM, dysk, godzina…)
  do dopytań w stylu „a ile wolnego?”. Tylko w pamięci procesu (ring 8), bez dysku. Komendy:
  `fakty`, `ostatnie fakty`. Odczyty i narzędzia same dopisują fakty (`RecordResponse`).
- **Kontekst modelu** (`CommandRouter.BuildSystemContext`): data ISO + dzień tygodnia (model
  przestaje zmyślać daty), aktywny projekt + tytuł rozmowy, „Wątek rozmowy: … · pierwsza
  wiadomość: …”, skoroszyt faktów sesji. `AiContextFilter` wybiera linie punktami
  (dopasowanie ogierów + świeżość), profil użytkownika zawsze w budżecie, dopytania
  („a dlaczego tak?”) trzymają cały wątek.
- **Wyszukiwanie nieostre**: `SearchNotes`/`SearchConversation` akceptują odmianę i jedną
  literówkę (≥5 znaków: wspólny ogier albo edycja ≤1). Dokładne zliczenia bez zmian
  (regresja pilnuje: „ustalenie” → dokładnie 3).
- **`co wiesz o mnie`** i **`podsumuj rozmowę`** — wyłącznie z zapisanych danych, bez zmyślania.

## 3. Zadania

- Priorytet z tekstu: „dodaj zadanie: pilne kupić mleko” (wysoki), „… niski priorytet …” (niski).
- `zadanie N priorytet wysoki|niski|normalny` — zmiana priorytetu istniejącego zadania.

## 4. Narzędzia offline (~30 nowych)

- **Finanse**: `znizka 20 80`, `napiwek 50 10`, `raty 10000 5 12` („symulacja — nie jest poradą
  finansową”), `odsetki proste`, `procent skladany`.
- **VAT wielostawkowy**: `vat 8 100` (23/8/5/0%, dopisek `netto`/`brutto`). Domyślnie 23% —
  zamrożony kontrakt „vat 100” → „brutto 123,00” pozostaje nietknięty.
- **Matematyka**: `logarytm`, `potega`, `modulo`, `wartosc bezwzgledna`, `sin/cos/tan`,
  `srednia wazona` (przed ogólnym wzorcem „srednia:”), `rownanie kwadratowe`.
- **Kalendarz**: `kalendarz M RRRR` (siatka miesiąca, poniedziałek pierwszy), `dodaj/odejmij
  N dni|tygodni|miesiecy|lat do daty`, `ile dni miedzy A a B`, `rok przestepny`, `kwartal`,
  `ile dni do konca kwartalu`.
- **Walidatory (lokalnie, bez sieci)**: `ean` (EAN-8/12/13), `isbn` (ISBN-10/13), `luhn`
  (12–19 cyfr), `regon` (9/14 cyfr).
- **Tekst**: `literuj`, `czestotliwosc slow`, `skrable`, `posortuj slowa`, `bez powtorzen`,
  `odwroc slowa`, `tylko cyfry`, `tylko litery`.
- **System**: `nazwa uzytkownika`, `rozdzielczosc ekranu`, `stan baterii`, `strefa czasu`,
  `czas w strefie UTC±N` (przed zegarem świata).
- **Losowość**: `kostka NdM` („rzuc 3k6”), `wylosuj karte`.
- **Zegar świata**: ~10 nowych miast + formy w dopełniaczu („czas w londynie”, „czas w atenach”).

## 5. Testy (dowód, nie deklaracja)

- `tests/UnderstandingRegression.cs` — ~25 nowych asercji: zdania, argumenty, odmiana, refusale,
  `Explain`, ogierki, nowe aliasy `ReadOnlyIntentCatalog`.
- `tests/UtilityRegression.cs` — ~60 asercji na nowych narzędziach + zamrożone kontrakty 0.91
  bez zmian (`vat 100` → „brutto 123,00”, `Password("haslo 24")`, UUID 42 znaki, WorldClock…).
- `tests/ContextRegression.cs` (NOWY) — `SessionFactBook` (ring, podmiana etykiet), wyszukiwanie
  nieostre (dokładne zliczenia + odmiana + literówka), kontekst („Wątek rozmowy”), `AiContextFilter`.
- Rejestracja w `UiSmokeTestRunner` → CI `windows-latest` (`--ui-smoke`).

## Czego 0.94 nie robi (świadomie)

- Rozumienie nadal jest **deterministyczne** (katalog + skróty + ogierki), nie modelem językowym —
  nie rozumie dowolnej polszczyzny. Zdania spoza katalogu trafiają do modelu AI tak jak wcześniej.
- Brak semantycznego wyszukiwania (tylko tekst + ogierki + jedna literówka).
- GUI bez zmian (kolejność ustalona przez użytkownika: najpierw rozumienie/funkcje/pamięć,
  „ładniejsze GUI” później).
- Brak wydania EXE — na wyraźne życzenie użytkownika.

## Weryfikacja

- `scripts/check-architecture.py` — PASS.
- Pełna weryfikacja kompilacji i `--ui-smoke` w CI na `windows-latest` (brak lokalnego
  kompilatora .NET — patrz README).
