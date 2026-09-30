# SENTINEL X 0.91 · CENTRUM — Windows / MVVM

Lokalny asystent Windows 10/11, C# 13, .NET 9, WPF. Nowe GUI jest domyślnym interfejsem. Poprzedni interfejs i jego funkcje nadal są dostępne przez `--legacy`.

## Narzędzia bez dodatkowego UI

Główny router dobiera lokalne narzędzia przed odpowiedzią modelu — uruchamianie aplikacji i znanych URL-i, odczyty systemowe, operacje na plikach, wyszukiwanie internetowe, odczyt stron i bezpieczne żądanie zamknięcia programu. Przykładowo „Uruchom Brave i YouTube” jest planem dwóch kolejnych uruchomień, a „Znajdź dokument o budżecie wakacyjnym” przeszukuje pliki użytkownika; po jednoznacznym trafieniu „przenieś go do Dokumentów” używa zapamiętanego celu. Nieznane polecenia nie dają modelowi uprawnień do dowolnego kodu.

- **Obliczenia i aktualne informacje:** proste działania trafiają do ograniczonego parsera arytmetycznego (bez ewaluacji kodu); mnożenie liczb całkowitych dostaje dodatkową kontrolę `BigInteger`. Pytania pogodowe oraz rozpoznane prośby o aktualne ceny, specyfikacje i instrukcje automatycznie uruchamiają research z cytowanymi adresami źródeł. Skróty stron są ekstrakcyjne, mogą być niepełne i nie są niezależnym potwierdzeniem; aktualny research wymaga dozwolonego dostępu do internetu.
- **Urządzenia (opcjonalnie):** po skonfigurowaniu oficjalnego Home Assistant Sentinel odkrywa wyłącznie encje z tej integracji. Właściciel musi jawnie sparować alias do `light`, `media_player` lub `remote`; włącz/wyłącz, jasność, głośność, wyciszenie i wejście są ograniczone do obsługiwanych encji, a wynik jest weryfikowany odczytem stanu. Przełączniki i sceny są celowo wykluczone. Konfiguracja używa `SENTINELX_HOME_ASSISTANT_URL` i `SENTINELX_HOME_ASSISTANT_TOKEN` poza czatem; Sentinel nie skanuje LAN-u i nie zapisuje tokenu. Nie skonfigurowano żadnego urządzenia domyślnie.
- **Telefon:** opcjonalny Bridge jest wyłączony domyślnie; po ręcznym uruchomieniu w **Ustawienia** nasłuchuje wyłącznie na konkretnych prywatnych IPv4, używa TLS z przypięciem certyfikatu, jednorazowego parowania i DPAPI/Android Keystore. Dostępne teraz: status PC, lista dozwolonych aplikacji, blokowanie ekranu (wynik jawnie niezweryfikowany), ostatnie pobranie, podstawowe akcje TV/światła przez sparowany Home Assistant oraz ograniczone makra JSON; klient Android ze skrótami/widgetem i jawnie uruchamianym rozpoznawaniem mowy znajduje się w `phone-android/`. WOL wymaga ręcznej konfiguracji MAC i nie potwierdza obudzenia. Screenshot/transfer plików, zamykanie, restart/wyłączanie i iOS nie są dostępne. Szczegóły, protokół i ograniczenia: [docs/PHONE-BRIDGE.md](docs/PHONE-BRIDGE.md). Nie otwieraj portu w routerze.
- **Pliki:** wyszukiwanie nazw/tekstu, rozszerzeń i metadanych daty w Pulpicie, Dokumentach, Pobranych, Obrazach, OneDrive i `CreatedFiles` (do 20 tys. plików, bez podążania za dowiązaniami i bez pobierania plików chmurowych); tworzenie plików/folderów, odczyt małych plików tekstowych, otwieranie nie-wykonywalnych plików, zmiana nazwy i przenoszenie do znanych folderów. Wyszukiwanie opisowe jest dopasowaniem leksykalnym, nie semantycznym. Wieloznaczne trafienia wymagają wyboru. Operacje nie nadpisują; ważne dane nie są usuwane.
- **Web:** `wyszukaj ...` zwraca wyniki ze źródłami, `zbadaj temat ...` szuka i próbuje odczytać do trzech stron, `otwórz drugą stronę` / `podsumuj pierwszy wynik` używa kontekstu ostatniego wyszukania, a `znajdź oficjalny numer telefonu firmy X` zwraca kandydatów do ręcznej weryfikacji. `otwórz stronę https://...` otwiera adres w domyślnej przeglądarce; `czytaj stronę https://...` odczytuje tekst, a `podsumuj stronę ...` robi krótkie podsumowanie ekstrakcyjne. Wyszukiwane frazy trafiają do publicznej wyszukiwarki DuckDuckGo; odczyt stron jest tylko GET, ma limity rozmiaru, blokuje loopback/sieci prywatne i nie wysyła formularzy, płatności ani treści w imieniu użytkownika. Wyniki nie są gwarancją aktualności ani oficjalności.
- **Diagnostyka/wydajność:** „CS2 mi ścina”, „co spowodowało tego laga” i „dlaczego mam wysoki ping” kierują do pomiarów CPU/RAM/licznika GPU/procesów i punktowych testów sieci. Bufor próbek działa tylko podczas uruchomienia Sentinela, jest w RAM (do ok. 6 min) i nie dowodzi przyczyny. Progi, czas utrzymania i cooldown automatycznych alertów Watch są konfigurowalne w ustawieniach; profil skupienia wstrzymuje alerty Sentinela, nie zmienia Windows. Temperatury, VRAM, taktowania, aktywność dysku, sterowniki i FPS/frametime nie są obecnie mierzone; brak odczytu jest jawny.
- **Pobrane/schowek/przypomnienia:** podczas sesji obserwowane są stabilne nowe pliki w domyślnym folderze Pobrane; „otwórz to, co przed chwilą pobrałem” używa ostatniego wykrytego celu, ale nie uruchamia instalatorów ani skryptów. Schowek jest czytany wyłącznie po jawnym poleceniu; objaśnienie/tłumaczenie trafia wyłącznie do lokalnej Ollamy na `127.0.0.1`, bez sieciowego wysyłania surowego tekstu. Oczywiste sekrety są blokowane, a obrazów Sentinel nie czyta. Przypomnienia czasowe są przechowywane lokalnie. „Daj mi znać, kiedy pobieranie się skończy” obserwuje nowe stabilne pliki i po restarcie sprawdza dopasowane pliki zmienione od checkpointu; nie otwiera ani nie uruchamia pobrań. Wykrywanie powrotu użytkownika do komputera nie jest obsługiwane.
- **Aplikacje/PC:** plan uruchomienia może łączyć do sześciu znanych programów i stron; „Zamknij program, który się zawiesił” wykrywa nieodpowiadające okno, pyta przy niejednoznaczności i nadal wymaga potwierdzenia. Procesów nie zabija na siłę. Pierwszy etap UI Automation odczytuje wyłącznie ograniczoną listę nazw widocznych kontrolek z ostatniego aktywnego okna spoza Sentinela; nigdy nie pobiera wartości pól edycji. „Kliknij [dokładna etykieta]” wyszukuje tylko przycisk/hiperłącze o dokładnej nazwie i wymaga osobnego potwierdzenia HIGH przez Permission Center; przed wykonaniem ponownie sprawdza okno i identyfikator kontrolki. Wynik Invoke jest jawnie niezweryfikowany — nie potwierdza skutku w aplikacji. Nie wpisuje tekstu, nie klika współrzędnych ani nie wykonuje formularzy. UIA nie działa ze wszystkimi aplikacjami i może być blokowane przez różne poziomy uprawnień. Przełączanie urządzeń audio i przenoszenie okien między monitorami nadal nie są obsługiwane. Głośność master Windows można odczytać i ustawić przez Core Audio; przełączenie wyjścia na słuchawki nie jest obsługiwane.
- **Ekran/obraz:** pytania „co jest na ekranie?”, „co to za błąd?” i podobne używają lokalnie dostępnych tekstowych nazw kontrolek UI Automation; wyjaśnienie trafia do lokalnej Ollamy. To nie jest zrzut ekranu, OCR ani analiza obrazu: Sentinel nie widzi pikseli, grafiki, układu ani treści, której aplikacja nie ujawnia jako nazwę kontrolki. Kontekst jest pobierany jawnie na żądanie, a same etykiety nie są utrwalane w audycie akcji. Brak dostępnych kontrolek lub dostępu jest raportowany zamiast zgadywania.
- **Kodowanie / aplikacje mobilne:** „zbuduj program notatnik/kalkulator/pomodoro” tworzy lokalny projekt źródłowy i manifest SHA-256; Sentinel nie uruchamia lokalnego kompilatora ani nie generuje EXE. Dla Androida generator tworzy bezpieczne szablony Counter/Notes/Checklist, a GitHub Actions buduje debug APK i paczkę źródeł na żądanie. Instrukcja instalacji: [docs/MOBILE-APP-GENERATOR.md](docs/MOBILE-APP-GENERATOR.md). To nie jest dowolne uruchamianie promptowego kodu ani edycja istniejącego repo.
- **Pozostałe integracje:** nie ma jeszcze nasłuchu powiadomień Windows, zdarzeniowych automatyzacji, uwierzytelnionego kanału telefon↔PC, odczytu aktywnego projektu/aplikacji ani globalnego cofania wszystkich zmian.
- **Telefon:** `PhoneCallTool` i komendy `historia rozmów` / `/rozmowy historia` są wpięte do toolboksa, ale repo nie zawiera działającego providera/companion Android. Aplikacja WPF nie może sama użyć SIM/eSIM ani niezawodnie otrzymać obu kanałów audio komórkowego. Dlatego nie wykonuje połączeń, nie wyszukuje numeru do połączenia i jawnie odmawia zamiast symulować. Kolejne próby, IVR, poczta głosowa i wielojęzyczna rozmowa będą możliwe dopiero po rzeczywistej integracji telefonicznej.

## Nowe w 0.91 — CENTRUM: jeden hub, paleta `//`, głos od startu i ~50 nowych narzędzi

- **Centrum zamiast wielu kart**: sidebar ma 4 pozycje (Centrum, Pamięć, Projekty, Ustawienia), a w Centrum ikony bez podpisów: 💬 rozmowa, 📓 zadania, 🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka. „Command Center” przemianowane na „Centrum” wszędzie.
- **Paleta `//` w czacie**: wpisz `//`, Tab/Shift+Tab wybiera, Enter wykonuje, Esc zamyka. ~40 wpisów (`//diag`, `//ram`, `//backup`, `//lekcje`, `//samokontrola`, …). Wysłanie `//diag` uruchamia diagnostykę komputera.
- **Głos domyślnie nasłuchuje od startu** (świadoma decyzja użytkownika): wskaźnik 🎤 zawsze widoczny, jeden klik wyłącza, ustawienie zostaje w Ogólnych.
- **Pytam zamiast zgadywać**: polecenie podobne do znanego w 62–80% wywołuje „Czy chodziło Ci o: …” — „tak” wykonuje propozycję. Propozycje nigdy nie dotyczą poleceń niszczących.
- **`lekcje` — uczenie się na poprawkach**: każda naprawiona literówka trafia do lokalnego `Memory/Lessons.jsonl`; `lekcje` pokazuje najczęstsze korekty. Deterministycznie, lokalnie, bez modelu.
- **`zrob zadanie: treść`** dodaje zadanie wprost do zakładki 📓 (nie tylko odpowiedź w czacie); `notatka:` to szybki alias „zapamiętaj”; `szukaj w zadaniach:` przeszukuje zadania.
- **`samokontrola`** (spójność własnych plików, tylko odczyt) i **`propozycje`** (lista porządków z „wpisz: …” — nic nie wykona się samo).
- **Generator aplikacji Android:** bezpieczne, jawne szablony Counter/Notes/Checklist, generator źródeł i ręcznie uruchamiany GitHub Actions build APK; Sentinel nie wykonuje dowolnego kodu wygenerowanego z promptu. Instrukcja: [docs/MOBILE-APP-GENERATOR.md](docs/MOBILE-APP-GENERATOR.md).
- **Uczciwe odmowy wbudowane**: modele 3D, dowolne samomodyfikowanie aplikacji i automatyczne skanowanie dysku — Sentinel wyjaśnia, czego nie robi i dlaczego.
- **~50 nowych narzędzi offline**: `pierwiastek` · `silnia` · `nwd`/`nww` · `czy pierwsza` · `dzielniki` · `fibonacci` · `srednia/mediana/suma/min/max` · `zaokraglij` · `zmiana z A do B` · `ile znakow` · `ile zdan` · `palindrom` · `anagram` · `rot13` · `tytul` · `morse`/`dekoduj morse` · `binarnie`/`dekoduj binarnie` · `hex`/`dekoduj hex` · `pesel:` (suma kontrolna + data + płeć) · `nip:` · `iban:` · `rgb R G B` · `tydzien roku` · `dzien roku` · `ile dni do konca roku` · `wiek:` · `dni robocze A do B` · `wielkanoc ROK` · `czas w toki/londyn/…` · `nazwa komputera` · `ile rdzeni` · `architektura` · `moje ip` · `rzut moneta` · `lotto` · `pin N`. Wszystkie bez sieci; `pomoc` pokazuje pełną listę.
- **Zakres spoza 0.91** (uczciwie): brak samomodyfikacji kodu (zaprojektowana odmowa), brak modeli 3D, brak integracji GitHub/Gmail (odłożone), okno nie jest przezroczyste (aurora to warstwa dekoracyjna), ~50 poleceń zamiast 100 — reszta w [docs/BACKLOG.md](docs/BACKLOG.md). Szczegóły: [docs/RELEASE-0.91.md](docs/RELEASE-0.91.md).

## Nowe w 0.90 — rozumienie poleceń, narzędzia offline i własny folder pamięci

- **Rozumienie literówek** (`CommandUnderstanding.cs`, odległość Damerau-Levenshteina): „ile mam ramuu” → „ile mam ramu”, „wlącz kalkulaotr” → „włącz kalkulator”, „ststus pamieci” → „status pamięci”. Każda poprawka jest **pokazywana** („Zrozumiałem jako: …”) i dopiero wtedy wykonana — nigdy po cichu. Progi: podobieństwo ≥ 0,80 i przewaga ≥ 0,08 nad drugą najlepszą kandydaturą; słowa krótsze niż 4 znaki, liczby, daty i tekst wieloliniowy nie są poprawiane, więc „policz 12,5*4” i „ile dni do 24.12” przechodzą nietknięte.
- **Katalog napraw nie zawiera poleceń niszczących** (usun / zamknij / wyłącz / czyść / potwierdź / zatrzymaj): literówka nie może zamienić się w usunięcie danych. Test regresji przegląda cały katalog i pilnuje tego wprost.
- **Skróty są jawne, nie zgadywane** (18): `cs` `dc` `st` `ch` `nt` `kl` `yt` `mz` `dk` `sp` `sn` `sns` `zs` `tp` `ti` `zd` `pr` `pm`. Pełna lista: `skróty`.
- **Uruchamianie programów** z dowodem: CS2 (`steam://rungameid/730`), Dota 2, Cyberpunk 2077, Wiedźmin 3, Elden Ring, Terraria, Rust przez Steam; Discord, Steam, Brave, Chrome, Edge, Firefox, Notatnik, Kalkulator, Paint, Menedżer zadań, Terminal, VS Code, VLC, OBS, Telegram, Spotify, YouTube, Gmail, FACEIT, ChatGPT; każda inna nazwa przez skróty menu Start. Sukces jest potwierdzany **nazwą procesu** (PID + godzina odczytu), brak instalacji jest zgłaszany wprost, a przy wielu trafieniach Sentinel prosi o dokładniejszą nazwę.
- **24 narzędzia działające offline, bez sieci i bez modelu**: `policz 12,5*4` · `procent 15 z 240` · `ile to procent 30 z 240` · `vat 100 [brutto]` · `przelicz 5 km na mile` (długość, masa, objętość, dane, prędkość, °C/°F/K) · `ile dni do 24.12` · `ile dni od 1.1.2020` · `jaki dzien tygodnia 1.1.2030` · `ile zostalo do 18:00` · `haslo 20` (generacja stockowym RNG, nigdzie nie zapisywana) · `uuid` · `ile slow: tekst` · `base64: tekst` i `dekoduj base64: …` · `hash tekstu: …` (SHA-256) · `json: {…}` · `slug: ZaŻółć Gęślą Jaźń` · `transliteruj: …` · `wielkie litery: …` · `male litery: …` · `odwroc tekst: …` · `losuj 1-100` · `rzuc kostka` · `wybierz losowo: pizza, sushi` · `bmi 80 180` · `rzymskie 2026` i `z rzymskich MMXXVI` · `kolor 1fa2c3` (RGB, HSL, kontrast WCAG). Kalkulator to własny parser wyrażeń — nie wykonuje kodu.
- **5 funkcji przekrojowych**: `plan dnia` (przeterminowane, dzisiejsze, najbliższe przypomnienia, przypięte wspomnienia, aktywny projekt), `szukaj wszystkiego: fraza` (wspomnienia + aktywna rozmowa + zadania + projekty + odczyty diagnostyczne), `statystyki` (ile czego jest i ile to zajmuje na dysku), `backup` (kopia magazynów do `Backups/<data>/` z manifestem, hashami i odczytem zwrotnym), `pomoc` / `co umiesz`.
- **Własny folder i comiesięczne archiwum pamięci**: `Memory/Archives/RRRR-MM/` z plikiem JSON i czytelnym Markdownem; hash i odczyt zwrotny są sprawdzane **zanim** cokolwiek zostanie usunięte z aktywnego magazynu. Ustawienie „Archiwum rozmów (miesiące)” (domyślnie 1, 0 = wyłączone): rozmowy starsze niż okno są archiwizowane przy starcie i usuwane z magazynu, a fakt ten jest zgłaszany w czacie. **Wspomnienia i profil nie są archiwizowane ani kasowane.** Komendy: `archiwizuj rozmowy`, `archiwa`, `usuń archiwum RRRR-MM`. W trybie prywatnym archiwizacja odmawia, bo nie ma czego zapisywać.
- **Pliki do pobrania z GitHub** — wydania milowe: <https://github.com/pawelaachi123-eng/SentinelX/releases> (np. `v0.90.0`, `v0.91.2`). Każde zawiera `SentinelX-<wersja>-win-x64-portable.zip` (własny runtime .NET 9), `SentinelX-<wersja>-win-x64-setup.exe` (instalator per-user), `SHA256SUMS.txt` i `BUILD.txt`. Publikuje je workflow `release.yml` po pełnej walidacji, tylko dla tagów `v*.*.*` albo na ręczne żądanie; zwykły push na gałąź nic nie publikuje (decyzja z 2026-09-25). Pliki **nie są podpisane cyfrowo** — SmartScreen pokaże ostrzeżenie „nieznany wydawca”.
- **Znane ograniczenia 0.90** (bez owijania): naprawa literówek to dopasowanie tekstu do katalogu — bez modelu językowego i bez pełnej odmiany; `przelicz` nie przelicza walut (potrzebny byłby kurs z sieci); zrzuty ekranu i sterowanie głośnością nie zostały dodane, bo nie da się ich uczciwie dostarczyć w tym przyroście; VAT liczy wyłącznie stawkę 23%; archiwum rozmów jest kopią do odczytu i nie ma przywracania do aktywnej rozmowy; wyszukiwanie „wszystkiego” jest dopasowaniem po normalizacji, bez literówek i synonimów.

## Nowe w 0.89 — strumieniowanie AI, odczyty diagnostyczne i narzędzia rozmowy

- **Odpowiedzi AI płyną na ekran w trakcie generowania** (`/api/chat` z `stream: true`, NDJSON czytany linia po linii). Ukryte rozumowanie modelu (`<think>…</think>`) jest odfiltrowywane **przyrostowo** — także wtedy, gdy znacznik zostanie przecięty między dwoma fragmentami, więc brudnopis modelu nigdy nie trafia do czatu.
- **Przycisk „Zatrzymaj generowanie” jest widoczny zawsze**, nie tylko w trakcie pracy. Zatrzymanie nie usuwa tekstu: to, co model zdążył napisać, zostaje pokazane i oznaczone jako urwane, a odpowiedź **nie** jest oznaczana jako udana.
- **Ponowienie odpowiedzi** (`ponów` albo przycisk): powtarza ostatnie polecenie dosłownie i oznacza wynik jako ponowiony — bez udawania, że to ta sama odpowiedź.
- **Strona „Diagnostyka” (12. w shellu):** zapisywanie odczytów stanu (dyski, usługi, zdarzenia, sieć, metryki), **porównanie dwóch odczytów** linia po linii (zmienione / pojawiło się / zniknęło / sekcja nieodczytana), eksport porównania do Markdown + JSON z odczytem zwrotnym i SHA-256, usuwanie pojedynczego odczytu. Limit 20 odczytów — najstarsze są usuwane automatycznie, magazyn `snapshots.json` ma te same gwarancje co pozostałe (zapis atomowy, kopia uszkodzonego pliku).
- **Porównanie nie wyjaśnia przyczyny** i nie jest diagnozą kondycji sprzętu — ten komunikat jest wbudowany w sam raport, nie tylko w dokumentację.
- **Narzędzia rozmowy:** `szukaj w rozmowie: fraza` (wyłącznie aktywna rozmowa, dopasowanie po normalizacji — bez literówek i odmiany, co jest mówione wprost) oraz `eksportuj rozmowę markdown` (plik lokalny z treścią rozmowy, odczyt zwrotny + SHA-256; w trybie prywatnym eksport jest odmawiany, bo nie ma czego zapisywać).
- **Komendy:** `snapshot`, `snapshoty`, `porównaj snapshoty [N M]`, `eksportuj porównanie`, `usuń snapshot N`.
- **Paleta poleceń (Ctrl+K)** dostała 13 nowych wpisów, w tym brakujące strony (Pamięć, Projekty, Zadania, Diagnostyka) — funkcje, których nie da się znaleźć, praktycznie nie istnieją.
- Nowe zestawy regresji `tests/AiStreamRegression.cs` (kolejność fragmentów, filtrowanie `<think>` przeciętego między fragmentami, zatrzymanie w połowie, awaria strumienia → zapasowy model, linia błędu w strumieniu) i `tests/DiagnosticSnapshotRegression.cs` (porównanie na syntetycznych odczytach, trwałość, limit, eksport, uszkodzony magazyn) — oba odpalane w CI.

Pozostały zakres (pliki: plan porządkowania i duplikaty; sekwencje działań; alerty progów) — w [docs/BACKLOG.md](docs/BACKLOG.md), jako kolejne przyrosty, nie obietnice.

## Nowe w 0.88 — zadania i przypomnienia

- **Nowa strona „Zadania” (11. w shellu):** zadania z priorytetem (niski/normalny/wysoki), terminem, opcjonalnym projektem i statusami `otwarte → w toku → zrobione` (z powrotem); filtry Dzisiaj / Wszystkie / Przeterminowane / Zrobione; edycja tytułu i terminu; kasowanie pojedynczego wpisu, jawnie i nieodwracalnie opisane.
- **Polski parser terminów** działający offline: `za 30 minut`, `za 2 godziny`, `za pół godziny`, `dziś o 19`, `jutro o 18` (bez godziny → 9:00 i tak jest pokazane), `pojutrze`, `w piątek o 15` (najbliższy; ten sam dzień tygodnia bez godziny → następny tydzień), `24.12 o 12` (bez roku → najbliższa taka data), `15:30` samo. Każdy wynik zamieniany jest na **konkretną, widoczną datę i godzinę** przed zapisem.
- **Przypomnienia z czatu są accept-only:** `przypomnij mi jutro o 18 o raporcie` wraca propozycją z rozstrzeloną datą — dopiero odpowiedź „tak” zapisuje; „nie” anuluje; oczekiwanie wygasa po 5 minutach. Z panelu Zadania przypomnienie zapisuje się od razu, pod polem zawsze widać rozstrzeloną datę.
- **Przypomnienia tylko przy uruchomionej aplikacji:** Sentinel nie udaje mechanizmu systemowego. Wszystko przeterminowane podczas zamknięcia zgłasza się przy starcie jako **przegapione** z widocznym oznaczeniem — nigdy po cichu, nigdy z udawaniem, że zadziałało.
- **Komendy:** `dodaj zadanie: treść [termin]`, `zadania`, `zadanie N zrobione`, `przypomnienia`. Usuwanie zadań/przypomnień — wyłącznie przyciskiem w panelu (świadomy klik, nie polecenie tekstowe).
- Własny magazyn `tasks.json` z gwarancjami jak pamięć i projekty: zapis atomowy, kopia uszkodzonego pliku, odczyt zwrotny z SHA-256. Zegar jest testowalnym szwem, więc zestaw regresji `tests/TaskRegression.cs` weryfikuje pełną tabelę parsera wokół środy 16:00.

Pozostały zakres (streaming AI, snapshoty diagnostyczne) — w [docs/BACKLOG.md](docs/BACKLOG.md), jako kolejne przyrosty, nie obietnice.

## Nowe w 0.87 — projekty i izolacja kontekstu

- **Nowa strona „Projekty” (10. w shellu):** tworzenie (z natychmiastową aktywacją), zmiana nazwy/opisu, statusy `aktywny → wstrzymany`, zakończenie, archiwizacja i przywracanie. Archiwizacja nigdy nic nie usuwa — notatki i rozmowy projektu zostają w magazynie.
- **Izolacja kontekstu AI po projekcie:** aktywny projekt ogranicza wbudowane wspomnienia do wpisów projektu i wpisów globalnych; wpisy innych projektów nie trafiają do modelu. Nowe notatki i rozmowy są stemplowane identyfikatorem aktywnego projektu. Wyszukiwanie w panelu Pamięć pozostaje globalne i pokazuje wszystko — izolacja chroni wyłącznie kontekst modelu.
- **Rozmowy przypisane do projektu:** nowa rozmowa dziedziczy aktywny projekt; „Nowa rozmowa w projekcie” i „Przypisz aktywną rozmowę” z karty projektu. `wznów` odmawia (z polskim wyjaśnieniem), gdy rozmowa nie należy do aktywnego projektu — konteksty się nie mieszają.
- **„Gdzie skończyliśmy?”** — karta projektu wznawia ostatnią rozmowę projektu i pokazuje 3 ostatnie notatki, zawsze z zapisanych danych, nigdy wymyślone.
- **Eksport projektu do JSON** (karta projektu + jego notatki + metadane rozmów) do `Exports` pod katalogiem danych.
- **Komendy:** `nowy projekt: nazwa`, `projekty`, `użyj projektu N`, `aktywny projekt`, `wyłącz projekt`.
- Własny magazyn `projects.json` z tymi samymi gwarancjami co pamięć: zapis atomowy, kopia uszkodzonego pliku zamiast nadpisania, odczyt zwrotny z SHA-256. Nowy zestaw regresji `tests/ProjectRegression.cs` (tworzenie, cykl statusów, izolacja, wznowienia, restart, eksport, archiwizacja) uruchamiany w CI.

Pozostały zakres (zadania i przypomnienia, streaming AI, snapshoty diagnostyczne) — w [docs/BACKLOG.md](docs/BACKLOG.md), jako kolejne przyrosty, nie obietnice.

## Nowe w 0.86 — pamięć v3, rozmowy i prywatność

- **Nowa strona „Pamięć”:** trwałe wspomnienia ze stabilnym identyfikatorem, kategorią (`notatka/preferencja/fakt/decyzja/zadanie/narzędzie`), źródłem, datami utworzenia i zmiany. Dodawanie z podpowiedzią o podobnych wpisach, edycja, usuwanie pojedynczego wpisu po ID przez zgodę HIGH (podobne teksty nie giną), przypinanie (zawsze w budżecie kontekstu) i oznaczanie „nieaktualne” (zostaje w panelu, znika z kontekstu AI). Dziennik zmian ważnych ustaleń, wykrywanie duplikatów i sprzecznych wpisów jako wskazówka do decyzji — asystent nigdy sam nie rozstrzyga konfliktu.
- **Nazwane, wznawiane rozmowy:** każda sesja ma tytuł z pierwszej wiadomości (można zmienić), listę i daty. `pokaż rozmowy` / `wznów rozmowę N` albo przyciski w panelu. Przełączenie przeładowuje Command Center i izoluje kontekst — rozmowy nie mieszają się.
- **Cztery niezależne przełączniki prywatności** (Settings → Pamięć): zapis rozmów, czytanie historii przez AI, zapis wspomnień, czytanie wspomnień przez AI — plus retencja rozmów w dniach (wspomnienia nie są kasowane automatycznie) i przełącznik podglądu kontekstu.
- **Tryb prywatny** (przycisk w Command Center / komenda `tryb prywatny`): tekst rozmowy i treść audytu nie są utrwalane w historii (audyt może zachować jedynie marker/stan bez tekstu); szkic również jest pomijany. Wyniki plikowe, które użytkownik jawnie zleci utworzyć, są osobnym zapisem lokalnym. Sesyjny, nigdy auto-włączany po restarcie. Nie blokuje sieci samodzielnie — `Tryb tylko lokalnie` kontroluje ruch wychodzący.
- **Podgląd „co trafiło do modelu i dlaczego”:** panel pokazuje etykiety i powody źródeł kontekstu ostatniego zapytania (komenda `co poszło do modelu`). Pełne prompty nie są rejestrowane.
- **Szkic niewysłanej wiadomości** przeżywa restart aplikacji (poza trybem prywatnym, zapis dławiony do co 2 s).
- **Import pamięci** z podglądem liczebności, walidacją pliku, ochroną przed duplikatami i — celowo — bez importowania historii rozmów. Eksport był; format opisany w [docs/MEMORY.md](docs/MEMORY.md).
- **Migracja formatu pamięci v1/v2 → v3** tworzy datowaną kopię zapasową oryginalnego pliku przed pierwszym zapisem; uszkodzony plik nadal nigdy nie jest nadpisywany pustym stanem.
- Nowy zestaw regresji pamięci (`tests/MemoryRegression.cs`: restart, izolacja rozmów, zgody, retencja, import, migracja, czerwienienie audytu w trybie prywatnym) uruchamiany w CI wraz z pozostałymi zestawami.

Stan na moment wydania 0.86: projekty, zadania, przypomnienia i snapshoty diagnostyczne były jeszcze w [docs/BACKLOG.md](docs/BACKLOG.md). Projekty są gotowe w 0.87 powyżej.

## Plan rozwoju

[Plan oparty na researchu — audyt 2026-09-22](docs/RESEARCH_PLAN.md): zweryfikowane źródła, korekty założeń, priorytety P0/P1/P2 i mierzalne kryteria odbioru. **To plan, nie lista wdrożonych funkcji**; zachowuje historyczny audyt i dopisuje postęp 0.85 i wydanie 0.86, m.in. naprawę L16 dotyczącą potwierdzania operacji na pamięci.

## Nowe w 0.85 — narzędzia, historia i bezpieczne zgody

- **Instalator EXE:** artefakt `SentinelX-Setup-win-x64` zawiera `SentinelX-Setup-0.88.0-win-x64.exe` i `SHA256SUMS.txt`. Instalacja per-user, bez wymagania administratora, do `%LOCALAPPDATA%\Programs\SentinelX`. Runtime .NET jest w zestawie. Brak automatycznego uruchamiania, instalowania modeli czy ustawiania autostartu. Dane użytkownika w `%LOCALAPPDATA%\SentinelX` pozostają po odinstalowaniu. Build nie jest podpisany certyfikatem wydawcy — Windows może wyświetlić ostrzeżenie SmartScreen; nie wyłączaj ochrony systemu.
- **Ponad 50 wariantów odczytów bez LLM:** m.in. `ile mam ramu?`, `wolny RAM`, `procent RAM`, `wolne miejsce na dyskach`, `czas pracy komputera`, `która godzina`, `dzisiejsza data`. Rozpoznawane są polskie znaki, prefiks „proszę”, wake word i interpunkcja. Każdy obsługiwany odczyt dostaje własny dowód; brak pomiaru nie zamienia się w zero. `pokaż pamięć` nadal dotyczy wspomnień, a `pokaż pamięć RAM` — metryk.
- **Eksport historii JSON/CSV:** przyciski na stronie History i komendy `eksportuj historię json` / `eksportuj historię csv`. Migawka do 200 ostatnich stanów akcji (nie cały dziennik, nie cała rozmowa), niezależnie od filtra UI, w `Exports` pod katalogiem danych. Pliki zawierają prywatne komendy/ścieżki. Zapis bez nadpisywania, odczyt zwrotny, SHA-256, ochrona CSV przed formułami. Uszkodzony dziennik przerywa eksport zamiast dawać pozorny sukces.
- **Historia:** filtr statusu, wyszukiwanie bez konieczności wpisywania polskich znaków, automatyczne odświeżenie po wejściu na stronę, kopiowalny wynik eksportu. Więcej propozycji komend w Ctrl+K.
- **Naprawa zgód pamięci (L16):** usunięcie całej pamięci, rozmowy lub pasujących wspomnień trafia do wspólnego Permission Center jako HIGH. Głos może poprosić o operację, ale nie zatwierdzi jej. Bramka sprawdza kontekst jawnej zgody, nie tylko tekstową blacklistę. STOP/anulowanie usuwa oczekiwanie; zgoda jest jednorazowa i wygasa po 10 minutach. Stara fraza `potwierdź usunięcie wspomnień` nie zatwierdzi zamknięcia aplikacji ani innego rodzaju operacji. Kasowanie ma dowód zgodności zapisu; eksporty i audyt akcji nie są kasowane.

CI dodatkowo buduje instalator, instaluje go w katalogu testowym i uruchamia zainstalowane `SentinelX.exe --ui-smoke`. Te testy nie zastępują mikrofonu, rzeczywistych modeli i gier na Twoim komputerze.

## Nowe w 0.84 — codzienna obsługa i wiarygodność

- **Ctrl+K** otwiera paletę komend i nawigacji. Wyszukiwanie rozumie polskie znaki; ↑/↓ wybierają wynik, Enter otwiera stronę lub **wstawia polecenie do edytora**, Esc zamyka. Paleta nigdy sama nie wykonuje polecenia.
- **Konfiguracja i gotowość** sprawdza ustawienia, wykrycie mikrofonu, obecność lokalnych plików ASR/VAD i listę modeli Ollama. Nie nagrywa, nie pobiera modeli i nie uruchamia generacji. Błąd Ollama nie blokuje lokalnych narzędzi. Sprawdzenie Ollama ma limit 3 sekund; dostępność plików ASR nie oznacza testu mikrofonu.
- **Identyfikator polecenia jest stały.** Dowody zbierane są w ramach konkretnego wykonania asynchronicznego, nie przez wyszukiwanie „najnowszego” wpisu z podobnym czasem. Historia zawiera `requestId` umożliwiające filtrowanie audytu.
- **Wynik wszystkich kroków**, a nie ostatniego: późniejszy sukces nie zakrywa wcześniejszego błędu. Anulowanie zachowuje dowody ukończonych operacji. Przekroczenie limitu dowodów wyklucza status Verified.
- **Etap i czas wykonania na żywo**, bez wymyślonych procentów. Błąd zapisu audytu jest widoczny i nie blokuje kolejnych zadań.
- **Ochrona szkicu**: zablokowane wysłanie podczas STOP / zajętości nie usuwa tekstu z edytora.
- **Paczka Portable** w GitHub Actions zawiera własny runtime .NET. Jest dodatkowo uruchamiana w teście po publikacji; nie wymaga ręcznej instalacji .NET na komputerze użytkownika.

To konkretne ulepszenia z testami regresji, **nie deklaracja przewagi nad wszystkimi projektami na GitHubie**. Mikrofon, prawdziwe modele i integracje sprzętowe nadal wymagają testów na docelowym komputerze.

## Uruchomienie

Instalator: w najnowszym udanym przebiegu [Windows build and WPF smoke](https://github.com/pawelaachi123-eng/SentinelX/actions/workflows/windows-build.yml) pobierz **SentinelX-Setup-win-x64**, wypakuj ZIP artefaktu i uruchom plik **SentinelX-Setup-0.88.0-win-x64.exe**. Przed aktualizacją zakończ Sentinel przez **Wyjdź** w zasobniku, nie sam przycisk X.

Bez instalacji: w najnowszym udanym przebiegu [Windows build and WPF smoke](https://github.com/pawelaachi123-eng/SentinelX/actions/workflows/windows-build.yml) pobierz **SentinelX-Portable-win-x64**, wypakuj cały ZIP i uruchom `SentinelX.exe`. Modele AI/ASR nie są częścią ZIP-a — konfigurujesz je osobno.

Wymagania: **Windows x64**. Paczka Portable ma runtime w zestawie. Starsza paczka `SentinelX-windows-x64` nadal wymaga .NET **9 Desktop Runtime**; do budowania źródeł potrzebny jest **.NET 9 SDK**. Nie uruchamiaj WPF na Linuxie ani przez przeglądarkę.

```powershell
dotnet restore SENTINEL-X.csproj
dotnet build SENTINEL-X.csproj -c Release
dotnet run --project SENTINEL-X.csproj -c Release
```

Wersja samodzielna (nie wymaga instalacji runtime na komputerze docelowym):

```powershell
dotnet publish SENTINEL-X.csproj -c Release -r win-x64 --self-contained true -o bin/publish
.\bin\publish\SentinelX.exe
```

Zachowany interfejs zgodności:

```powershell
.\bin\Release\net9.0-windows\SentinelX.exe --legacy
```

Najpierw zakończ działającą instancję przez **Wyjdź z aplikacji** / menu zasobnika. Aplikacja jest pojedynczą instancją; drugie uruchomienie aktywuje pierwsze okno. Zwykły przycisk X domyślnie chowa je do zasobnika.

## Pierwsze użycie

1. **Command Center**: wpisz `ile mam RAM`, `użycie CPU`, `top procesy`, `uruchom kalkulator` lub `test internetu`.
2. **AI**: zainstaluj i uruchom lokalną [Ollama](https://ollama.com/), np. `ollama pull qwen3:4b`, następnie **Sprawdź połączenie** i wybierz model. Połączenie jest celowo ograniczone do `127.0.0.1:11434` na komputerze użytkownika. Przy braku Ollama narzędzia lokalne nadal działają.
3. **Voice**: wybierz mikrofon. Jeśli nie masz lokalnych modeli, kliknij **Pobierz modele i włącz głos** (Whisper Small ~488 MB plus VAD). Pobieranie jest jawne, nie odbywa się przy starcie. Każde polecenie głosowe musi zawierać słowo-klucz „Sentinel” — może ono paść w dowolnym miejscu zdania; bez niego Sentinel niczego nie wykonuje i dalej nasłuchuje. Kalibracja wymaga dwóch sekund ciszy.
4. **Settings**: wyszukaj parametr, zmień go i kliknij **Zapisz** przy danym polu. Niepoprawna wartość nie jest zapisywana. Możesz przywrócić pojedynczą sekcję.
5. **Gaming**: wykrywanie gry działa automatycznie. Overlay włączysz przyciskiem. Możesz przeciągać go myszą. Nie zmieniamy trybu gry Windows ani priorytetów procesów.
6. **Ctrl+Shift+X** lub czerwony **EMERGENCY STOP** anuluje zadania, wyłącza głos i blokuje nowe akcje. **Wznów Sentinel** odblokowuje akcje, ale nie włącza mikrofonu. **Ctrl+Shift+S** pokazuje okno.

### Nauka tematów i umiejętności

Polecenie `naucz się Kung Fu` (także `ucz się o ...`, `learn about ...`) uruchamia ograniczony przepływ: jeśli zezwala na to Tryb tylko lokalnie, pobiera wyniki i do trzech stron przez istniejące narzędzie tylko do odczytu, następnie lokalny Ollama układa z nich brief, bezpieczny plan nauki i listę niepewności. `umiejętności` pokazuje zapisane tematy; briefy trafiają do kategorii `umiejętność` w panelu Pamięć, skąd można je edytować, oznaczyć jako nieaktualne albo usunąć. Gdy sieć jest zablokowana, może powstać jedynie szkic z lokalnego modelu i jest on wyraźnie oznaczony jako nieweryfikowany.

To zapisane, ograniczone notatki kontekstowe — **nie trening modelu ani gwarancja eksperckiej wiedzy**. Model nie zmienia wag i może się mylić; ekstrakcja stron nie jest niezależną weryfikacją. Trwały zapis respektuje ustawienie zapisu wspomnień; tryb prywatny przechowuje nowy brief wyłącznie w ograniczonym RAM-ie do zamknięcia aplikacji. Treści źródeł są traktowane jako niezaufane i nie mogą same uruchamiać działań.

### Pewność pamięci i trafność kontekstu

W formacie pamięci v4 trwałe wpisy niosą źródło, datę utworzenia, ostatnie potwierdzenie i konserwatywną ocenę confidence. Trafność jest obliczana względem konkretnego zapytania, nie jako globalny fakt o wpisie. W kontekście AI Sentinel pokazuje te metadane i ostrzega przy starszych faktach sprzętowych lub briefach umiejętności; ręczne oznaczenie jako nieaktualne obniża confidence. Migracja starszych wspomnień nadaje ostrożne wartości domyślne. Confidence opisuje jakość/pochodzenie, nie matematyczną gwarancję prawdy; gdy istnieje deterministyczny odczyt bieżącego stanu, ma on pierwszeństwo przed starą notatką.

### Autopilot i Watcher w Core

Polecenia przykładowe: `CS2 mi ścina` (historia Black Box, bieżące CPU/RAM/GPU, procesy, punktowy test Internetu), `dlaczego internet laguje` (lokalny kontekst, punktowy ping/DNS/HTTPS i migawka procesów), `napraw CS2` (zaczyna od bezpiecznej diagnostyki; nie zmienia ustawień bez potwierdzonej przyczyny), `monitoruj wydajność gry`, `monitoruj czy CS2 się zamknie`, `monitoruj pobieranie <fragment nazwy>`, `status Autopilota`, `status obserwatorów` i `pamięć zdarzeń`. `Anuluj` zatrzymuje pojedyncze aktywne zadanie lub pyta, jeśli wybór jest niejednoznaczny; `anuluj wszystko` sygnalizuje przerwanie wszystkich aktywnych zadań i bieżącego zapytania AI. Checkpoint oczekiwania na pobranie może przetrwać restart tylko wtedy, gdy lokalna pamięć celów jest włączona; po ponownym uruchomieniu Sentinel czeka na nowe zdarzenie systemu plików i nie odtwarza zakończeń, które nastąpiły przy zamkniętej aplikacji. Po stabilnym zapisie weryfikuje istnienie i czytelność metadanych, ale niczego nie otwiera. Historia wydajności i wzorce zdarzeń są ograniczone do RAM. Dostępne są CPU/RAM i licznik GPU Windows; brak FPS/frametime, temperatur, VRAM, taktowań, aktywności dysku i pomiaru strat do serwera gry jest zgłaszany wprost. Alert o wysokim użyciu zasobów wymaga kilku kolejnych próbek; wyniki są korelacją, nie dowodem przyczyny.

### Tryb tylko lokalnie i prywatność online

Nowa konfiguracja włącza `Tryb tylko lokalnie` domyślnie (Settings → Pamięć); stan jest pokazany jako `TYLKO LOKALNIE` w nagłówku Centrum. Przy blokadzie Sentinel nie wysyła wyszukiwań ani odczytów stron, nie wykonuje zewnętrznych testów DNS/ICMP/HTTPS, nie pobiera modeli głosowych, nie otwiera stron w przeglądarce i nie przekazuje żądań do innych aplikacji przez otwieranie plików/folderów lub UI Automation, nie kopiuje odpowiedzi do schowka Windows ani nie zamyka zewnętrznych procesów. Generowanie szablonu tworzy wyłącznie pliki źródłowe; żadne EXE nie jest kompilowane w tej funkcji. Każda zablokowana operacja informuje o blokadzie; ustawienie działa bez restartu. Lokalny Ollama przez `127.0.0.1` oraz lokalne narzędzia/odczyty pozostają dostępne.

Aby użyć internetu, użytkownik musi wyłączyć blokadę. Przy włączonym `Trybie prywatnym` treść rozmowy nie jest zapisywana lokalnie, ale po wyłączeniu blokady zapytania są wysyłane do wybranych usług online i te usługi, dostawca VPN/proxy lub operator sieci mogą je rejestrować. Sentinel nie zarządza VPN, nie buduje łańcucha kilku proxy ani nie może zagwarantować anonimowości lub braku logów. Aplikacja korzysta z domyślnej konfiguracji sieci systemu; proxy niekoniecznie obejmuje DNS/ICMP, a sama aplikacja nie może zagwarantować, że cały ruch przechodzi przez VPN. Windows i inne procesy pozostają poza kontrolą Sentinela. Zgody i ograniczenia bezpieczeństwa dla akcji wysokiego ryzyka pozostają bez zmian.

### Przykłady pracy z plikami

```text
utwórz plik notatka.txt: Treść mojej notatki
dopisz do niego Kolejna linia
pokaż ten plik
skopiuj ten plik jako kopia.txt
przenieś ten plik jako nowa-nazwa.txt
znajdź plik notatka
znajdź dokument o budżecie wakacyjnym
przenieś go do folderu Dokumenty
zmień jego nazwę na plan.txt
utwórz folder Raporty na pulpicie
```

Ostatnio jednoznacznie wybrany plik pozostaje celem dla kolejnych powiązanych poleceń w bieżącej instancji; lista wieloznacznych trafień jest porzucana przy następnym innym poleceniu. Przy wielu wynikach Sentinel prosi o numer. Przeszukiwanie jest ograniczone do typowych folderów użytkownika, ma limit i nie wchodzi w dowiązania. Nie nadpisuje istniejących celów; zmiana tekstu zachowuje kopię `.bak`, a zapis jest sprawdzany po odczycie. Przeniesienie/anulowanie nie cofa operacji, która już się zakończyła.

## Architektura i dane

- `Core/ServiceLocator.cs`: walidowany kontener DI, wspólne instancje serwisów i zachowywane przy nawigacji ViewModele.
- `ViewModels/`: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`. Brak operacji plikowych, zapytań systemowych i `Process.Start` w ViewModelach.
- `Views/`: osiem stron, shell i overlay. Code-behind ograniczony do inicjalizacji widoku i przeciągania okna.
- `Services/`: interfejsy oraz integracja z istniejącymi, sprawdzanymi regresyjnie serwisami. Routing najpierw wybiera deterministyczne narzędzia, potem istniejący adaptacyjny transport AI.
- `Themes/`: pełny system tokenów designu `Sx*` (kolory, gradienty, promienie, odstępy, typografia, style kontrolek, storyboardy) na Fluent .NET 9; motywy Dark, Deep Dark i Light generuje `python3 scripts/generate-themes.py` (z wbudowaną kontrolą kontrastu WCAG `--check`), a kolor akcentu i bramka animacji są nadpisywane w trakcie działania. `Themes/Animations.xaml` jest scalany przed `Themes/Controls.xaml`, bo szablony sięgają po storyboardy przez `StaticResource` (kolejność sprawdza bramka „resource order”). Animacje są wyłączane ustawieniem Windows, przełącznikiem „Animacje” oraz automatycznie podczas gry.
- `Models/Settings/`: istniejący format ustawień zachowany bez duplikowania schematu. Migracja starego JSON, walidacja, zapis przez plik tymczasowy i kopia zapasowa.
- Starsze pliki w katalogu głównym są celowo zachowane: część to współdzielone serwisy, część to interfejs zgodności.

Dane pozostają w `%LOCALAPPDATA%\SentinelX\`: `Settings`, `History`, `Memory`, `Autopilot` (wyłącznie krótkie checkpointy celów; bez telemetrii), `Logs`, `CreatedFiles` oraz katalogi modeli używane przez istniejący manager. Bufor Black Box i Event Memory pozostają tylko w RAM. Testy używają odizolowanego `SENTINEL_DATA_DIR`.

### Świadome odstępstwa od master prompta

- Nie obniżano projektu do 0.77 z prompta: migrację rozpoczęto na 0.83, a bieżąca iteracja ma numer **0.86**.
- **OllamaSharp 5.4.18**, nie 4.0.6. Test uruchomienia wykrył `MissingMethodException`: 4.0.6 wymaga preview `IChatClient`, podczas gdy istniejący Whisper.net 1.9.1 używa stabilnego `Microsoft.Extensions.AI.Abstractions` 10.2.0. Nie można załadować obu ABI do tej samej aplikacji. OllamaSharp obsługuje odkrywanie modeli, a istniejący transport HTTP zachowuje testowane strategie timeout/retry/fallback czatu.
- **NAudio 2.2.1** z `WaveInEvent` (bez okna callback WinForms) i `WasapiLoopbackCapture`, CommunityToolkit.Mvvm **8.4.2**, DI **9.0.0**; istniejące biblioteki Whisper/Sherpa pozostają.
- Zasobnik korzysta z istniejącego **natywnego Shell_NotifyIcon**, bez `UseWindowsForms`, WPF-UI i H.NotifyIcon. Rozwiązuje to sprzeczność „bez WinForms” vs „NotifyIcon WinForms” w prompcie.
- Zakresy ustawień głosu zachowują bezpieczne limity obecnego pipeline, zamiast wpisywać niewspierane przez niego wartości.

## Zakres i ograniczenia — nie utożsamiaj builda z testem sprzętu

| Obszar | Implementacja / ograniczenie |
|---|---|
| MVVM shell | 8 rzeczywistych stron, stan zachowany przy nawigacji, DI, obsługa błędów i pustych danych |
| System | CPU/RAM z WinAPI, GPU z liczników Windows, dyski, sieć, top procesów; niedostępny pomiar jest jawny |
| VRAM | **Niedostępne** — nie podajemy fikcyjnego zera ani wartości z demonstracji |
| AI | Lokalna Ollama, lista modeli, wybór, istniejący adaptacyjny czat; **brak autonomicznego wykonywania dowolnych narzędzi przez LLM** |
| Głos | Istniejący capture/VAD/ASR + MVVM, RMS/SNR/gain, kalibracja, wake word, TTS; detekcja VAD jest flagą, nie zmyślonym confidence |
| Akcje | Jedna kolejka wykonawcza, zgody na zamknięcie aplikacji, anulowanie, historia, dowody; tekst modelu `VERIFIED` nie daje statusu Verified |
| Pliki | Bezpieczny workspace, create/edit/copy/move/search, kopie przed edycją; brak ogólnego edytora dowolnych ścieżek i automatycznego rollbacku |
| Gaming | Wykrywanie istniejącej listy gier, wolniejsze pomiary, lżejszy model, overlay; bez udawanego pomiaru FPS/latencji gry |
| Autopilot + Watcher | W głównym Core: wieloetapowa, tylko-odczytowa diagnoza ścinek z dowodami; RAM-only Black Box (maks. 6 min); jawne obserwowanie wydajności gry, zakończenia CS2 i stabilnych pobrań. Alerty progowe są rzadkie i kontekstowe. Temperatury oraz utrata pakietów do serwera gry pozostają niedostępne bez providerów. |
| Tray/autostart | Natywna integracja, close-to-tray, rejestr HKCU, hotkeys; wymagają ręcznego sprawdzenia na desktopie użytkownika |
| Zaawansowane funkcje starego GUI | Kreator programów, dodatkowe komendy `..`, timery i szczegółowe panele diagnostyczne pozostają w `--legacy`; nie wszystkie mają odpowiednik w nowym shellu |
| Modyfikacja źródeł przez AI | **Niezaimplementowana**, domyślnie niedozwolona; nie wystawiamy pozornego działającego przełącznika |

## Testowanie

```powershell
# Windows: prawdziwe widoki, bindingi, motywy, backend i PNG wszystkich stron
.\bin\Release\net9.0-windows\SentinelX.exe --ui-smoke "$PWD\test-results\ui"
# Dotychczasowy zestaw testów regresji, nie korzysta z prawdziwej Ollama
.\bin\Release\net9.0-windows\SentinelX.exe --self-test "$PWD\test-results\regression"
# Ręcznie, po instalacji modeli i polskiego głosu Windows
.\bin\Release\net9.0-windows\SentinelX.exe --asr-test "$PWD\test-results\asr"
```

`--ui-smoke` nie modyfikuje autostartu, nie rejestruje skrótów, nie instaluje modeli i nie uruchamia mikrofonu. Sprawdza renderowanie stron i błędy bindingów, paletę, panel gotowości, izolację dowodów między zadaniami, mieszane wyniki wielu kroków, zachowanie dowodów po anulowaniu, STOP/resume, zakaz potwierdzania głosem (także z wybudzeniem i interpunkcją), konkurencję/anulowanie zadań, odrzucenie fałszywego „VERIFIED”, zapis ustawień i bezpieczeństwo plików. Od 0.93 sprawdza też warstwę wizualną: ~120 zasobów (kolory po przełączeniu motywu, promienie i odstępy, style, storyboardy, gradienty, efekty) musi być osiągalnych z `App.Resources` **i mieć właściwy typ** (`Color`, `CornerRadius`/`Thickness`, `Style`, `Storyboard`, `Brush`, `Effect`), a wszystkie konwertery widoków — `ReadinessBrush`, `StatusBrush`, `VoiceBrush`, `RiskBrush`, `BoolToVisibility`, `StringNotEmptyToVisibility`, `SafePercent`, `FiniteToVisibility` — muszą być `IValueConverter`.

Workflow **Windows build and WPF smoke** w `.github/workflows/windows-build.yml` uruchamia restore, build, nowy smoke test i istniejący zestaw regresji. Zachowuje raporty/PNG w `sentinel-validation` oraz aplikacje w `SentinelX-windows-x64` i `SentinelX-Portable-win-x64`. Ta druga paczka jest publikowana jako self-contained i przechodzi osobny smoke test. Wynik bieżącego workflow jest źródłem prawdy o kompilacji — nie sama obecność plików.

Ręczne testy wymagane przed uznaniem całości master prompta za DONE:

- [ ] Mikrofon użytkownika, cisza, szum, odległa mowa, odłączenie urządzenia, wake word i timeout.
- [ ] Ollama z faktycznie zainstalowanymi modelami, obciążenie i anulowanie generacji.
- [ ] Tray, konflikt globalnego hotkey, druga instancja, wylogowanie, autostart po ponownym logowaniu.
- [ ] CS2, GPU producenta użytkownika, wydajność monitorowania, overlay na kilku ekranach/DPI.
- [ ] Ręczna ocena wszystkich interakcji GUI i wymaganych funkcji jeszcze dostępnych tylko w `--legacy`.
