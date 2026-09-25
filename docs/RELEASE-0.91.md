# Sentinel X 0.91 „CENTRUM” — notatki wydania

Data: 2026-09-24 (tag `v0.91.0`) · aktualizacje 0.91.1/0.91.2: 2026-09-25 · Wersja aplikacji: `0.91 · CENTRUM`

To wydanie przebudowuje interfejs wokół jednej zakładki **Centrum**, dodaje paletę poleceń `//`,
włącza domyślnie nasłuch głosowy (świadoma decyzja użytkownika), wprowadza mechanizm „pytam zamiast
zgadywać” oraz ~50 nowych narzędzi offline. Nic, co poniżej oznaczono jako niezweryfikowane, nie jest
podawane jako działające.

## GUI — Centrum zamiast wielu kart

- **Jedna zakładka Centrum** zamiast 12 kart w sidebarze. Sidebar ma teraz 4 pozycje: Centrum, Pamięć,
  Projekty, Ustawienia. Wewnątrz Centrum: ikony bez podpisów tekstowych — 💬 rozmowa, 📓 zadania,
  🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka (podpowiedź po najechaniu).
- **„Command Center” przemianowane na „Centrum”** we wszystkich miejscach produktu (nagłówek, paleta Ctrl+K, teksty).
- **Paleta `//` w polu czatu**: wpisz `//`, a pojawi się lista poleceń; **Tab/Shift+Tab** wybiera,
  **Enter** wykonuje, **Esc** zamyka, strzałki też działają. ~40 wpisów: `//diag` `//ram` `//cpu`
  `//dysk` `//procesy` `//internet` `//ip` `//pamiec` `//zadania` `//snapshot` `//backup` `//archiwa`
  `//samokontrola` `//propozycje` `//lekcje` `//pomoc` `//plan` `//statystyki` `//godzina` `//haslo`
  `//lotto` i inne. Wysłanie `//diag` Enterem uruchamia pełną diagnostykę komputera.
- **Aurora**: delikatne poświaty (cyjan + fiolet) za treścią stron — warstwa dekoracyjna, nigdy nie
  przechwytuje kliknięć. Okno pozostaje nieprzezroczyste — pełna przezroczystość ramki jest świadomie
  wyłączona (wydajność renderowania i kompatybilność popupów w WPF).
- Szybki przełącznik mikrofonu 🎤 w nagłówku Centrum („Nasłuch: WŁ/WYŁ”) — jeden klik zatrzymuje głos.

## Głos — domyślnie nasłuchuje od startu

- `StartVoiceOnLaunch` jest teraz domyślnie **WŁĄCZONE** (explicit user decision, 2026-09). Sentinel
  słyszy polecenia od razu po uruchomieniu, bez wchodzenia w zakładki.
- Prywatność: wskaźnik stanu głosu jest zawsze widoczny (nagłówek Centrum + sidebar), mikrofon można
  wyłączyć jednym kliknięciem, ustawienie „Głos przy uruchomieniu” zostaje w Ustawieniach → Ogólne.
  Nagrania nie są zapisywane domyślnie (próbki audio to osobny, domyślnie wyłączony przełącznik).
- **0.91.1/0.91.2 · Słowo-klucz „Sentinel” decyduje o wykonaniu.** Polecenie głosowe zostaje wykonane
  tylko wtedy, gdy zdanie zawiera „sentinel” (akceptowane warianty pisowni: sentinel/sentynel/
  centinel/centenel/santinel/sentinelu itd.). Słowo może być w DOWOLNYM miejscu zdania — „ile mam
  ramu, sentinel” działa tak samo jak „Sentinel, ile mam ramu”. Od 0.91.2 zasada jest bezwzględna:
  KAŻDE polecenie musi zawierać słowo-klucz (nie ma okna rozmowy wykonującego zdania bez
  wybudzenia). Bez słowa-klucza Sentinel niczego nie wykonuje i dalej nasłuchuje.

## Rozumienie — pytam zamiast zgadywać

- **Szara strefa**: polecenie podobne do znanego w 62–80% nie jest ani wykonywane, ani ignorowane —
  Sentinel odpowiada „Czy chodziło Ci o: …” i czeka. Odpowiedź „tak” wykonuje pierwszą propozycję.
  Propozycje nigdy nie wskazują poleceń niszczących.
- **Dziennik lekcji** (`lekcje`): każda naprawiona literówka jest dopisywana do lokalnego pliku
  `Memory/Lessons.jsonl` (limit 500 linii). `lekcje` pokazuje licznik, najczęstsze poprawki i ostatnie
  wpisy. To deterministyczna lokalna pamięć korekt — bez modelu i bez wysyłania czegokolwiek.
- 5 nowych skrótów: `sd`→samokontrola, `pp`→propozycje, `lk`→lekcje, `im`→moje ip, `nc`→nazwa komputera
  (łącznie 23). Katalog fraz rozszerzony o wszystkie nowe polecenia — naprawa literówek działa też dla nich.

## Zadania — „zrob zadanie” naprawdę dodaje zadanie

- `zrob zadanie: treść` (oraz `dodaj zadanie:` / `nowe zadanie:`) zapisuje zadanie bezpośrednio
  w zakładce 📓 Zadania w Centrum, z potwierdzeniem i ewentualnym rozpoznanym terminem.
- `notatka: treść` — szybki alias „zapamiętaj”.
- `szukaj w zadaniach: fraza` — przeszukuje zadania łącznie ze zrobionymi.

## Sentinel o sobie — samokontrola, propozycje, uczciwe granice

- `samokontrola` — raport spójności własnych plików (settings.json, magazyny JSON, archiwa, lekcje,
  wolne miejsce na dysku). Tylko odczyt; nic nie jest naprawiane automatycznie.
- `propozycje` — lista porządków wynikająca z realnego stanu (wiek backupu, archiwizacja, otwarte
  zadania, przegapione przypomnienia, nieaktualne wspomnienia). Każda linia kończy się „wpisz: …”;
  nic nie wykonuje się samo (autonomia = propozycja + wyraźna zgoda).
- `wersja`, `co nowego` — wersja aplikacji i skrócona lista zmian.
- **Uczciwe odmowy** (wbudowane odpowiedzi, nie luki): modele 3D („nie buduję modeli 3D — nie mam tu
  silnika graficznego i nie chcę udawać”), modyfikacja własnego kodu („nie modyfikuję własnego kodu —
  to świadoma decyzja; mam samokontrolę i propozycje”), automatyczne skanowanie dysku.

## Nowe narzędzia offline (łącznie ~50; wszystkie bez sieci)

- **Matematyka**: `pierwiastek X` · `silnia N` · `nwd A B` · `nww A B` · `czy pierwsza N` ·
  `dzielniki N` · `fibonacci N` · `srednia: …` · `mediana: …` · `suma: …` · `min: …` · `max: …` ·
  `zaokraglij X do N` · `zmiana z A do B`.
- **Tekst**: `ile znakow: …` · `ile zdan: …` · `palindrom: …` · `anagram: a, b` · `rot13: …` · `tytul: …`.
- **Kodowania**: `morse: …` / `dekoduj morse: …` · `binarnie: …` / `dekoduj binarnie: …` ·
  `hex: …` / `dekoduj hex: …`.
- **Polskie identyfikatory** (walidacja w 100% lokalna, nic nie jest wysyłane): `pesel: 11 cyfr`
  (suma kontrolna + data urodzenia + płeć), `nip: 10 cyfr`, `iban: PL61…` (mod-97).
- **Kolory**: `rgb 31 162 195` → hex (odwrotność `kolor`).
- **Kalendarz**: `tydzien roku` · `dzien roku` · `ile dni do konca roku` · `wiek: data` ·
  `dni robocze A do B` (bez świąt — to jest napisane wprost) · `wielkanoc ROK` ·
  `czas w toki/londyn/berlin/paryz/nowy jork/chicago/los angeles/seoul` (dane stref z systemu).
- **System (odczyt)**: `nazwa komputera` · `ile rdzeni` · `architektura` · `moje ip` (lokalne IPv4;
  publicznego adresu nie sprawdzam bez polecenia).
- **Losowe**: `rzut moneta` · `lotto` (6 z 49, RNG kryptograficzne) · `pin N`.

## Weryfikacja (realne wyniki)

- Pełny zestaw regresji przechodzi w CI na `windows-latest` przy każdym pushu: backend, produkt, release,
  pamięć, projekty, zadania, snapshoty, strumień AI, rozumienie, narzędzia, archiwa + smoke UI.
- Smoke 0.91 dodatkowo renderuje **każdą zakładkę Centrum** (łapanie błędów wiązań), przechodzi pełny
  scenariusz palety `//`, potwierdza domyślne włączenie głosu, przechodzi rundę „pytanie zamiast
  zgadywania” z „tak”, oraz round-trip `zrob zadanie` → zakładka 📓.
- Szczegóły i limity: sekcja „Znane ograniczenia” niżej.

## Znane ograniczenia i uczciwe „nie”

1. **~50 nowych poleceń + przebudowa GUI, nie 100 funkcji** — tyle dało się dostarczyć i przetestować
   w tym przyroście bez obniżania jakości. Reszta jest w `docs/BACKLOG.md`.
2. **Brak samomodyfikacji kodu** — odrzucone świadomo (patrz odmowy). „Samonaprawa” istnieje wyłącznie
   jako `samokontrola` (raport) i `propozycje` (lista do decyzji użytkownika).
3. **Brak modelowania 3D i generowania modeli** — brak narzędzi; odmowa z wyjaśnieniem jest wbudowana.
4. **Brak integracji GitHub/Gmail** — odłożone (decyzja użytkownika); wrócą jako osobny przyrost.
5. **Okno nie jest przezroczyste** — „glass” jest warstwą wizualną; pełna przezroczystość ramki jest
   wyłączona z powodów wydajności/kompatybilności.
6. **Autonomia tylko z zgodą** — nic nie wykonuje się w tle bez polecenia; `propozycje` nigdy nie
   uruchamiają się same. Praca w tle podczas gry pozostaje tym, co było: rzadsze próbkowanie monitora
   i lekki model — bez „czyszczenia RAM” i zabijania usług.
7. **`czas w …` zna 8 miast**; `dni robocze` nie odejmują świąt; `wiek` działa dla dat przeszłych;
   waluty pozostają poza zakresem (wymagałyby kursów z sieci).
8. Aplikacja **nie jest podpisana cyfrowo** — SmartScreen pokaże „nieznany wydawca”.

## Pliki wydania

**Zasada od 0.91.1: każdy upgrade publikuje pliki do pobrania.** Workflow `release.yml` działa
w dwóch trybach:

- `workflow_dispatch` → wydanie **rolling**: tag `rolling` jest przesuwany na najnowszy build, więc
  https://github.com/pawelaachi123-eng/SentinelX/releases/tag/rolling zawsze oferuje aktualne pliki:
  `SentinelX-<wersja>-win-x64-portable.zip`, `SentinelX-<wersja>-win-x64-setup.exe`,
  `SHA256SUMS.txt`, `BUILD.txt`.
- tag `v*.*.*` → wydanie milowe (np. `v0.91.0` publikowało `SentinelX-0.91.0-win-x64-portable.zip`,
  `SentinelX-0.91.0-win-x64-setup.exe`, `SHA256SUMS.txt`, `BUILD.txt`).

Obie ścieżki publikują dopiero po pełnej walidacji: check architektury, build, smoke UI (wszystkie
strony + zero błędów wiązań), pełna regresja i smoke wersji portable.
