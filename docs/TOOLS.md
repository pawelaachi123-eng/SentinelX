# Narzędzia Sentinel X (0.96)

Strona **🧰 Narzędzia** (pasek boczny albo zakładka w Centrum, skrót `//narzedzia`, wpis w palecie Ctrl+K)
pokazuje wszystko, co Sentinel potrafi zrobić bez chmury: ponad 60 pozycji w kategoriach, z wyszukiwaniem,
przykładem, polem argumentu i wynikiem. Nad przyciskiem „Uruchom” zawsze widzisz **dokładne polecenie**,
które zostanie wysłane — nic nie dzieje się w tle i nic nie uruchamia się samo.

Każde narzędzie działa też z czatu: wystarczy wpisać jego polecenie. Uruchomienie idzie normalną
ścieżką silnika, więc obowiązują te same zasady co wszędzie: STOP awaryjny blokuje akcje, wynik ma dowód,
a polecenia zmieniające stan systemu wymagają zgody.

## Roblox Studio / Luau i modele 3D

| Polecenie | Co robi | Granice i zapis |
| --- | --- | --- |
| `roblox pomoc` | Pokazuje dostępne szablony Luau i zasady audytu. | Offline; bez logowania do Roblox. |
| `roblox kod: leaderstats` | Tekst serwerowego skryptu sesyjnych Coins. | Wklej ręcznie do `ServerScriptService`; Sentinel nie kompiluje ani nie publikuje kodu. |
| `roblox kod: sprint` | Tekst `LocalScript` sprintu na Shift. | Wklej ręcznie do `StarterPlayerScripts`; prędkości dostosuj do gry. Klient nie jest granicą antycheat — w grze rywalizacyjnej serwer musi egzekwować dozwolony ruch. |
| `roblox kod: checkpoint` | Tekst sesyjnych checkpointów obby. | Wymaga `workspace.Checkpoints` z ponumerowanymi częściami; testuj na kopii gry. |
| `roblox kod: remote` | Przykład sklepu: serwer posiada listę dozwolonych produktów, cenę, saldo i limit żądań; klient wysyła tylko klucz produktu. | Tekst źródłowy. Dodaj nagrodę po stronie serwera i testuj w prywatnej sesji Studio. |
| `luau sprawdz: <kod>` | Heurystycznie wykrywa kilka wzorców (np. `loadstring`, `require(AssetId)`, pętle, `RemoteEvent`, DataStore); pomija komentarze i zwykłe/długie literały, skanuje template stringi konserwatywnie i zachowuje numery linii. | To nie parser, type-checker ani kompilator. Brak zgłoszeń nie dowodzi, że kod jest bezpieczny ani poprawny. |
| `model 3d: cube 2 2 2` | Zapisuje tekstową siatkę Wavefront OBJ. Dostępne: `cube`, `plane`, `sphere`, `cylinder`, `cone` (oraz kilka polskich aliasów). | Nowy plik w `%LocalAppData%\SentinelX\CreatedModels`; polecenie nie przyjmuje ścieżki ani nie nadpisuje pliku. Sentinel nie uruchamia Blendera i nie sprawdza renderu. |
| „Stwórz mi grę na Robloxie: kooperacyjny wyścig w neonowym kosmosie; nazwij ją Starfall Rally” | Naturalny opis (bez obowiązkowej składni) uruchamia analizę intencji i lokalny projekt z 12 wpisami: standalone Luau, tekstowy `.rbxlx`, modularne źródła Rojo, briefy oraz dwa skrypty Blender. Profile prototypu obejmują `simulator`, `obby`, `tycoon`, `rounds`, `racing` i `custom`. | Zapis do `%LocalAppData%\SentinelX\CreatedGames`; bez ścieżki od użytkownika i bez nadpisywania. Workflow może uruchomić zainstalowany Blender i przekazać place do Roblox Studio w Windows. Weryfikacja ZIP/hash nie dowodzi, że Luau lub `.rbxlx` otworzy się i zadziała w Studio ani że geometria jest gotowym assetem. Nie loguje się i nie publikuje. |

Kostka przyjmuje bok albo 3 wymiary; płaszczyzna 1–2 wymiary; sfera promień, 8–96 segmentów i 4–96 pierścieni; walec/stożek promień, wysokość i 8–96 segmentów. Wymiary muszą należeć do zakresu 0,001–100000 jednostek. Wynik jest prostą geometrią, bez materiałów, tekstur ani jednostek sceny. Polecenie `jak to rozumiem: model 3d: ...` jedynie opisuje zapis — nie tworzy pliku.

### Projekt gry Roblox / Rojo

Naturalny przykład: „Stwórz mi grę na Robloxie: kooperacyjny wyścig w neonowym kosmosie; nazwij ją Starfall Rally”. Sentinel sam wykrywa zamiar, gatunek, temat, tytuł i urządzenia; nie trzeba wpisywać prefiksu ani znać składni. Dostępne prototypy: `simulator`, `obby`, `tycoon`, `rounds`, `racing`, `custom`. To lokalny punkt startu z samodzielnym skryptem gry, nie gotowa produkcja komercyjna. Serwer weryfikuje progres i zapisuje go przez DataStore, który wymaga ręcznej konfiguracji testowego experience i API Services.

Projekt powstaje jako folder, ZIP, `.lua` i tekstowy `.rbxlx`. Gdy aplikacje są dostępne, workflow próbuje uruchomić Blender w trybie headless, zapisać `.blend` i FBX, otworzyć scenę oraz przekazać place do Roblox Studio (Windows). Proces Studio lub rozmiar pliku nie potwierdza poprawnego importu ani działania gry — sprawdź Output, Errors i Play ręcznie. Modularne źródła Rojo można rozwijać w `rojo serve`; zachowaj kopię przed synchronizacją do testowego place. Skrypt `tools/blender/create_blockout.py` nadal jest ręcznym, nieinwazyjnym blockoutem; `tools/blender/build_scene.py` buduje dodatkową scenę i eksportuje FBX bez usuwania istniejących kolekcji.

To grywalny prototyp do ręcznej weryfikacji, nie pełna gra komercyjna ani obietnica FPS, popularności czy zarobków. Weryfikacja SHA-256 potwierdza wyłącznie odczyt ZIP-a i plików; nie potwierdza składni Luau, poprawności Rojo, akceptacji `.rbxlx` przez Studio, działania gameplayu, importu FBX, wyglądu, wydajności ani zapisu DataStore. Przed wdrożeniem przejrzyj źródła, testuj i ręcznie sprawdź bieżące zasady Roblox.

## Kuźnia 0.96 — nowe narzędzia

| Polecenie | Co robi |
| --- | --- |
| `nazwa zmiennej: liczba użytkowników` | camelCase, PascalCase, snake_case, kebab-case i UPPER_SNAKE; polskie litery → ASCII, camelCase na wejściu jest rozbijany na słowa |
| `url zakoduj: ala ma kota & psa` / `url odkoduj: ala%20ma` | procent-kodowanie UTF-8 w obie strony |
| `unix: 1700000000` / `na unix: 14.11.2023 22:13:20 utc` | czas Unix ↔ data (UTC i czas komputera; wartość powyżej 99 999 999 999 to milisekundy) |
| `czestosc slow: ala ma kota ala` | dziesięć najczęstszych słów z liczbą wystąpień |
| `rata kredytu: 300000 25 7,5` | rata równa: suma, lata, oprocentowanie roczne; rachunek orientacyjny |
| `porownaj wersje: 1.2.10 ||| 1.10.0` | porównanie składowych liczbowych (1.10 jest nowsze niż 1.9) |
| `numeruj linie: a \| b \| c` / `odwroc linie: a \| b \| c` | numeracja i odwrócenie kolejności wierszy |
| `popraw odstepy: ala   ma    kota` | kilka spacji → jedna, z informacją ile usunięto |

Podgląd decyzji: `jak to rozumiem: <polecenie>` pokazuje, które narzędzie by zadziałało — bez uruchamiania.
Szukanie: `szukaj w zadaniach: fraza` (zadania i przypomnienia, tylko odczyt).

## Warsztat 0.95 — nowe narzędzia

| Polecenie | Co robi | Bezpieczeństwo |
| --- | --- | --- |
| `porownaj teksty: A \|\|\| B` | Różnice linia po linii plus podsumowanie. Separator `\|\|\|`, alternatywnie linia z trzech myślników. | Czysta funkcja, bez plików i sieci. |
| `regex: wzorzec \|\|\| tekst` | Dopasowania i grupy. | Limit 500 ms i 20 000 znaków — złożony wzorzec nie zawiesi aplikacji. |
| `sha256 pliku: <ścieżka>` / `md5 pliku:` / `hash pliku:` | Skrót, rozmiar i data zmiany pliku. | Tylko odczyt; pliki do 2 GB; ścieżka musi być wskazana wprost. |
| `wyciagnij: <tekst>` | E-maile, linki, adresy IPv4 i liczby. | Nic nie otwiera i nie wysyła. |
| `posortuj linie: <tekst>` | Porządek alfabetyczny (bez różnicy wielkości liter). Wiersze rozdzielaj `\|` albo znakiem nowej linii — czat spłaszcza entery, więc `\|` jest pewniejsze. | Do 2000 wierszy, pokazuje pierwsze 100. |
| `unikalne linie: <tekst>` | Zostawia pierwsze wystąpienie i mówi, ile powtórzeń pominął (separator `\|`). Nie dotyczy plików — do plików służy `duplikaty: folder`. | Jak wyżej. |
| `kwota slownie: 1234,56` | Polska odmiana złotych i groszy (0 – 999 999 999,99). | Czysta funkcja. |
| `sekundy: 3661` | Sekundy → dni/godziny/minuty/sekundy. | Czysta funkcja. |
| `na sekundy: 2h 15m 10s` | Zapis `2h 15m 10s`, `90min`, `1d` → sekundy. | Czysta funkcja. |
| `moc hasla: <hasło>` | Entropia, typowe słabości, szacowany czas złamania. | Hasła nie pokazuję w odpowiedzi, nie zapisuję i nie wysyłam. |
| `qr: <tekst>` | Kod QR zapisany jako PNG. | Plik trafia do `%LocalAppData%\SentinelX\Qr`. |
| `qr wifi: nazwa\|hasło` | Kod QR konfiguracji Wi-Fi (`WIFI:T:WPA;…`). | Jak wyżej — dane sieci zostają w pliku na Twoim dysku. |

## Pozostałe kategorie (skrót)

- **Tekst i kody** — liczenie słów i znaków, base64 w obie strony, SHA-256 tekstu, JSON, slug, transliteracja, Morse, binarnie, hex, ROT13, tytuł, wielkie/małe litery, odwracanie, palindrom, anagram.
- **Liczby i czas** — kalkulator (nawiasy, potęgi, przecinek), procenty, VAT 23%, pierwiastek, silnia, NWD/NWW, liczby pierwsze, dzielniki, Fibonacci, statystyki, jednostki, cyfry rzymskie, losowanie z zakresu, dni do daty, dzień tygodnia, wiek, dni robocze, wielkanoc, czas na świecie, BMI, tydzień roku.
- **Pliki i foldery** — duplikaty i raport porządkowy (tylko odczyt), folder danych Sentinela, odciski stanu komputera.
- **Dokumenty PL** — PESEL, NIP, IBAN, kolor hex → RGB, RGB → hex (walidacja lokalna).
- **System** — lokalne IP, nazwa komputera, rdzenie, architektura, wersja, diagnostyka, status zabezpieczeń, usługi, autostart, zdarzenia Windows.
- **Losowe i rozrywka** — hasło, PIN, UUID, Lotto, moneta, kości, wybór z listy.

## Jak szukać

Wpisz w polu szukania to, co chcesz zrobić („faktura”, „hash”, „qr”, „procent”, „diff”) — filtr patrzy na nazwę,
opis, polecenie, przykład i kategorię, więc trafisz nawet nie znając składni. Kategoria „Wszystkie” pokazuje
pełny katalog, a „Wyczyść filtry” wraca do początku.

## Czego tu nie ma (świadomie)

- **Zip/rozpakowywanie** — planowane, ale wymaga ścieżki zgód dla zapisu plików; na razie świadomie pominięte.
- **Schowek i zrzuty ekranu** — także planowane; wolimy je dodać razem z widocznym podglądem niż po cichu.
- **Nielegalne działania** — Sentinel nie ma pomagać w nieautoryzowanym dostępie, kradzieży danych, oszustwach, tworzeniu malware, zacieraniu śladów ani wyrządzaniu szkody. Konserwatywna reguła blokuje część jawnych próśb operacyjnych; nie jest pełnym klasyfikatorem prawa. Dozwolone pozostają obrona, edukacja, odzyskiwanie dostępu i testy systemów, do których użytkownik ma upoważnienie.
- **Cokolwiek, co wymaga chmury albo konta** — nie ma i nie będzie: wszystko liczy się na tym komputerze.
