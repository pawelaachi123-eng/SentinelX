# SENTINEL X 0.94 · AUTOPILOT — Windows + telefon

Lokalny asystent Windows 10/11 (C# 14, .NET 10, WPF) z aplikacją na telefon. **Nic nie trzeba pilnować:** instalujesz raz, a Sentinel sam startuje z Windows, sam pobiera i naprawia swój silnik AI, sam pilnuje komputera i daje znać na telefon. Poprzedni interfejs jest nadal dostępny przez `--legacy`.

## Szybki start (trzy kroki)

1. **Komputer.** Z [najnowszego wydania](https://github.com/pawelaachi123-eng/SentinelX/releases) pobierz `SentinelX-<wersja>-win-x64-setup.exe` i kliknij go dwa razy (bez uprawnień administratora). Gdy Windows zapyta o zaporę, wybierz **Zezwól** (sieć prywatna) — to potrzebne tylko po to, żeby telefon mógł się połączyć.
2. **Telefon z Androidem 7+.** Z tego samego wydania pobierz `SentinelX-Phone-<wersja>.apk`, zainstaluj (Android poprosi o zgodę na instalację z nieznanego źródła) i otwórz. Aplikacja **sama znajdzie komputer** w sieci Wi‑Fi. Na komputerze pojawi się okno z kodem — kliknij **Zezwól**. Robisz to raz.
3. **iPhone lub dowolna przeglądarka.** W zasobniku Windows wybierz **Telefon…** — zobaczysz kod QR i adres. Zeskanuj aparatem telefonu (przeglądarka ostrzeże o własnym certyfikacie — to normalne, bo działa tylko w Twojej sieci).

Telefon pokazuje **to samo co komputer**: ten sam czat z Sentinelem (polecenia, pytania do AI, odpowiedzi na żywo), stan komputera (CPU, RAM, GPU, dyski, procesy, gra), zadania i przypomnienia, notatki z pamięci oraz alerty. Polecenia z telefonu działają jak polecenia głosowe: potencjalnie ryzykowne akcje (np. zamknięcie programu) można zaproponować z telefonu, ale **zatwierdza się je tylko na komputerze**.

### Co dzieje się samo

- **Start z Windows w tle** (ikona w zasobniku, bez okna) — dzięki temu telefon zawsze ma z kim rozmawiać. Wyłączysz to w Ustawienia → Ogólne.
- **Silnik AI jest wbudowany** (llama.cpp, ten sam silnik, na którym działa Ollama) i **nie wymaga Ollamy**. Sam się pobiera, uruchamia tylko przy pytaniu, zwalnia pamięć, gdy jest bezczynny lub gdy działa gra, a uszkodzone pliki pobiera od nowa. Na słabszych komputerach dostajesz model lekki (Qwen3 1,7B, ok. 1,1 GB), na mocniejszych (od 12 GB RAM) także Qwen3 4B (ok. 2,5 GB). Każdy plik jest sprawdzany sumą SHA-256. Do czasu pobrania wszystkie polecenia systemowe działają normalnie.
- **Pilnowanie komputera:** długie obciążenie CPU/RAM, mało miejsca na dysku i przypomnienia trafiają do powiadomień Windows i na telefon (w aplikacji na Androida jako zwykłe powiadomienia).
- **Opiekun** sprawdza co minutę, czy łącze z telefonem i silnik AI działają, i uruchamia je ponownie, gdy trzeba. Jedna linia statusu mówi „Wszystko działa samo” albo — uczciwie — co wymaga Twojej uwagi.
- **Wake-on-LAN:** aplikacja na Androida może wybudzić komputer (o ile karta sieciowa i BIOS na to pozwalają).

Szczegóły techniczne: [docs/PHONE-LINK.md](docs/PHONE-LINK.md) (protokół i bezpieczeństwo), [docs/ENGINE.md](docs/ENGINE.md) (silnik AI), [docs/RELEASE-0.94.md](docs/RELEASE-0.94.md) (notatki wydania i ograniczenia).

### Plan B: zbuduj instalator u siebie (bez GitHub Actions)

```powershell
# wymaga tylko .NET 10 SDK; jeśli jest Inno Setup 6, powstanie też instalator .exe
powershell -ExecutionPolicy Bypass -File scripts\build-local.ps1 -Test
```

Aplikację na Androida najłatwiej zbudować w wydaniu na GitHubie (workflow **Release**); lokalnie potrzebny jest Android Studio — zob. [phone-android/README.md](phone-android/README.md).


## Nowe w 0.91 — CENTRUM: jeden hub, paleta `//`, głos od startu i ~50 nowych narzędzi

- **Centrum zamiast wielu kart**: sidebar ma 4 pozycje (Centrum, Pamięć, Projekty, Ustawienia), a w Centrum ikony bez podpisów: 💬 rozmowa, 📓 zadania, 🕘 historia, 🎤 głos, 🖥 system, 🎮 gry, ✨ AI, ⚡ akcje, 🩺 diagnostyka. „Command Center” przemianowane na „Centrum” wszędzie.
- **Paleta `//` w czacie**: wpisz `//`, Tab/Shift+Tab wybiera, Enter wykonuje, Esc zamyka. ~40 wpisów (`//diag`, `//ram`, `//backup`, `//lekcje`, `//samokontrola`, …). Wysłanie `//diag` uruchamia diagnostykę komputera.
- **Głos domyślnie nasłuchuje od startu** (świadoma decyzja użytkownika): wskaźnik 🎤 zawsze widoczny, jeden klik wyłącza, ustawienie zostaje w Ogólnych.
- **Pytam zamiast zgadywać**: polecenie podobne do znanego w 62–80% wywołuje „Czy chodziło Ci o: …” — „tak” wykonuje propozycję. Propozycje nigdy nie dotyczą poleceń niszczących.
- **`lekcje` — uczenie się na poprawkach**: każda naprawiona literówka trafia do lokalnego `Memory/Lessons.jsonl`; `lekcje` pokazuje najczęstsze korekty. Deterministycznie, lokalnie, bez modelu.
- **`zrob zadanie: treść`** dodaje zadanie wprost do zakładki 📓 (nie tylko odpowiedź w czacie); `notatka:` to szybki alias „zapamiętaj”; `szukaj w zadaniach:` przeszukuje zadania.
- **`samokontrola`** (spójność własnych plików, tylko odczyt) i **`propozycje`** (lista porządków z „wpisz: …” — nic nie wykona się samo).
- **Uczciwe odmowy wbudowane**: modele 3D, modyfikacja własnego kodu, automatyczne skanowanie dysku — Sentinel wyjaśnia, czego nie robi i dlaczego.
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
- **Tryb prywatny** (przycisk w Command Center / komenda `tryb prywatny`): treść rozmowy nie trafia nigdzie na dysk — ani do rozmów, ani do audytu (audyt zapisuje marker „treść niezapisana”), ani szkic. Sesyjny, nigdy auto-włączany po restarcie.
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
- **Konfiguracja i gotowość** sprawdza ustawienia, wykrycie mikrofonu, obecność lokalnych plików ASR/VAD i stan wbudowanego silnika AI. Nie nagrywa, nie pobiera modeli i nie uruchamia generacji. Błąd Ollama nie blokuje lokalnych narzędzi. Sprawdzenie Ollama ma limit 3 sekund; dostępność plików ASR nie oznacza testu mikrofonu.
- **Identyfikator polecenia jest stały.** Dowody zbierane są w ramach konkretnego wykonania asynchronicznego, nie przez wyszukiwanie „najnowszego” wpisu z podobnym czasem. Historia zawiera `requestId` umożliwiające filtrowanie audytu.
- **Wynik wszystkich kroków**, a nie ostatniego: późniejszy sukces nie zakrywa wcześniejszego błędu. Anulowanie zachowuje dowody ukończonych operacji. Przekroczenie limitu dowodów wyklucza status Verified.
- **Etap i czas wykonania na żywo**, bez wymyślonych procentów. Błąd zapisu audytu jest widoczny i nie blokuje kolejnych zadań.
- **Ochrona szkicu**: zablokowane wysłanie podczas STOP / zajętości nie usuwa tekstu z edytora.
- **Paczka Portable** w GitHub Actions zawiera własny runtime .NET. Jest dodatkowo uruchamiana w teście po publikacji; nie wymaga ręcznej instalacji .NET na komputerze użytkownika.

To konkretne ulepszenia z testami regresji, **nie deklaracja przewagi nad wszystkimi projektami na GitHubie**. Mikrofon, prawdziwe modele i integracje sprzętowe nadal wymagają testów na docelowym komputerze.

## Uruchomienie

Instalator i wersja przenośna: pobierz je z [wydań na GitHubie](https://github.com/pawelaachi123-eng/SentinelX/releases) (artefakty CI mają krótką retencję i bywają niedostępne, gdy magazyn artefaktów jest pełny). Przed aktualizacją zakończ Sentinela przez **Wyjdź z aplikacji** w zasobniku; dane zostają w `%LOCALAPPDATA%\SentinelX`.

Bez instalacji: rozpakuj `SentinelX-<wersja>-win-x64-portable.zip` i uruchom `SentinelX.exe`. Modele głosu (Whisper) pobierasz jawnie w zakładce Głos; silnik AI i jego modele pobierają się same.

Wymagania: **Windows 10/11 x64**. Instalator i paczka przenośna mają runtime .NET 10 w zestawie; do budowania ze źródeł potrzebny jest **.NET 10 SDK**. Nie uruchamiaj WPF na Linuxie ani przez przeglądarkę.

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
.\bin\Release\net10.0-windows\SentinelX.exe --legacy
```

Najpierw zakończ działającą instancję przez **Wyjdź z aplikacji** / menu zasobnika. Aplikacja jest pojedynczą instancją; drugie uruchomienie aktywuje pierwsze okno. Zwykły przycisk X domyślnie chowa je do zasobnika.

## Pierwsze użycie

1. **Command Center**: wpisz `ile mam RAM`, `użycie CPU`, `top procesy`, `uruchom kalkulator` lub `test internetu`.
2. **AI**: nic nie instalujesz — wbudowany silnik pobiera się sam (postęp widać w zakładce AI i w linii statusu). Komenda `status AI` pokazuje stan, `napraw AI` sprawdza pliki od razu. Do czasu pobrania narzędzia lokalne działają normalnie.
3. **Voice**: wybierz mikrofon. Jeśli nie masz lokalnych modeli, kliknij **Pobierz modele i włącz głos** (Whisper Small ~488 MB plus VAD). Pobieranie jest jawne, nie odbywa się przy starcie. Każde polecenie głosowe musi zawierać słowo-klucz „Sentinel” — może ono paść w dowolnym miejscu zdania; bez niego Sentinel niczego nie wykonuje i dalej nasłuchuje. Kalibracja wymaga dwóch sekund ciszy.
4. **Settings**: wyszukaj parametr, zmień go i kliknij **Zapisz** przy danym polu. Niepoprawna wartość nie jest zapisywana. Możesz przywrócić pojedynczą sekcję.
5. **Gaming**: wykrywanie gry działa automatycznie. Overlay włączysz przyciskiem. Możesz przeciągać go myszą. Nie zmieniamy trybu gry Windows ani priorytetów procesów.
6. **Ctrl+Shift+X** lub czerwony **EMERGENCY STOP** anuluje zadania, wyłącza głos i blokuje nowe akcje. **Wznów Sentinel** odblokowuje akcje, ale nie włącza mikrofonu. **Ctrl+Shift+S** pokazuje okno.

### Przykłady pracy z plikami

```text
utwórz plik notatka.txt: Treść mojej notatki
dopisz do niego Kolejna linia
pokaż ten plik
skopiuj ten plik jako kopia.txt
przenieś ten plik jako nowa-nazwa.txt
znajdź plik notatka
```

Edycja, kopiowanie i zmiana nazwy dotyczą ostatniego pliku utworzonego przez asystenta. Nie nadpisują istniejących celów. Edycja zachowuje `.bak`; zapis/kopia mają weryfikację treści i SHA-256. Wyszukiwanie jest ograniczone do katalogu `CreatedFiles`, a nie całego komputera. Anulowanie **nie cofa** ukończonych operacji.

## Architektura i dane

- `Core/ServiceLocator.cs`: walidowany kontener DI, wspólne instancje serwisów i zachowywane przy nawigacji ViewModele.
- `ViewModels/`: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`. Brak operacji plikowych, zapytań systemowych i `Process.Start` w ViewModelach.
- `Views/`: osiem stron, shell i overlay. Code-behind ograniczony do inicjalizacji widoku i przeciągania okna.
- `Services/`: interfejsy oraz integracja z istniejącymi, sprawdzanymi regresyjnie serwisami. Routing najpierw wybiera deterministyczne narzędzia, potem istniejący adaptacyjny transport AI.
- `Themes/`: zasoby kolorów, własne style kontrolek i Fluent .NET 9; Dark, Deep Dark i paleta systemowa. Animacje przejść są wyłączone podczas gry.
- `Models/Settings/`: istniejący format ustawień zachowany bez duplikowania schematu. Migracja starego JSON, walidacja, zapis przez plik tymczasowy i kopia zapasowa.
- Starsze pliki w katalogu głównym są celowo zachowane: część to współdzielone serwisy, część to interfejs zgodności.

Dane pozostają w `%LOCALAPPDATA%\SentinelX\`: `Settings`, `History`, `Memory`, `Logs`, `CreatedFiles` oraz katalogi modeli używane przez istniejący manager. Testy używają odizolowanego `SENTINEL_DATA_DIR`.

### Świadome odstępstwa od master prompta

- Nie obniżano projektu do 0.77 z prompta: migrację rozpoczęto na 0.83, a bieżąca iteracja ma numer **0.86**.
- **Bez OllamaSharp i bez Ollamy (0.94)**: `LocalAiService` nadal mówi dialektem HTTP Ollamy, ale obsługuje go w procesie `EngineOllamaFacade` (bez portu sieciowego), który tłumaczy wywołania na wbudowany `llama-server`. Dzięki temu przetestowane routowanie modeli, strumieniowanie i fallbacki zostały bez zmian.
- **NAudio 2.2.1** z `WaveInEvent` (bez okna callback WinForms) i `WasapiLoopbackCapture`, CommunityToolkit.Mvvm **8.4.2**, DI **9.0.0**; istniejące biblioteki Whisper/Sherpa pozostają.
- Zasobnik korzysta z istniejącego **natywnego Shell_NotifyIcon**, bez `UseWindowsForms`, WPF-UI i H.NotifyIcon. Rozwiązuje to sprzeczność „bez WinForms” vs „NotifyIcon WinForms” w prompcie.
- Zakresy ustawień głosu zachowują bezpieczne limity obecnego pipeline, zamiast wpisywać niewspierane przez niego wartości.

## Zakres i ograniczenia — nie utożsamiaj builda z testem sprzętu

| Obszar | Implementacja / ograniczenie |
|---|---|
| MVVM shell | 8 rzeczywistych stron, stan zachowany przy nawigacji, DI, obsługa błędów i pustych danych |
| System | CPU/RAM z WinAPI, GPU z liczników Windows, dyski, sieć, top procesów; niedostępny pomiar jest jawny |
| VRAM | **Niedostępne** — nie podajemy fikcyjnego zera ani wartości z demonstracji |
| AI | Wbudowany silnik (llama.cpp), lista modeli, wybór, istniejący adaptacyjny czat; **brak autonomicznego wykonywania dowolnych narzędzi przez LLM** |
| Telefon | Serwer HTTPS w sieci domowej, parowanie jednym kliknięciem z kodem potwierdzenia, aplikacja na Androida i strona dla przeglądarek; **nie działa przez internet bez własnej sieci prywatnej** |
| Głos | Istniejący capture/VAD/ASR + MVVM, RMS/SNR/gain, kalibracja, wake word, TTS; detekcja VAD jest flagą, nie zmyślonym confidence |
| Akcje | Jedna kolejka wykonawcza, zgody na zamknięcie aplikacji, anulowanie, historia, dowody; tekst modelu `VERIFIED` nie daje statusu Verified |
| Pliki | Bezpieczny workspace, create/edit/copy/move/search, kopie przed edycją; brak ogólnego edytora dowolnych ścieżek i automatycznego rollbacku |
| Gaming | Wykrywanie istniejącej listy gier, wolniejsze pomiary, lżejszy model, overlay; bez udawanego pomiaru FPS/latencji gry |
| Watch | Alerty długotrwałego CPU/RAM z czasem oczekiwania i cooldownem |
| Tray/autostart | Natywna integracja, close-to-tray, rejestr HKCU, hotkeys; wymagają ręcznego sprawdzenia na desktopie użytkownika |
| Zaawansowane funkcje starego GUI | Kreator programów, dodatkowe komendy `..`, timery i szczegółowe panele diagnostyczne pozostają w `--legacy`; nie wszystkie mają odpowiednik w nowym shellu |
| Modyfikacja źródeł przez AI | **Niezaimplementowana**, domyślnie niedozwolona; nie wystawiamy pozornego działającego przełącznika |

## Testowanie

```powershell
# Windows: prawdziwe widoki, bindingi, motywy, backend i PNG wszystkich stron
.\bin\Release\net10.0-windows\SentinelX.exe --ui-smoke "$PWD\test-results\ui"
# Dotychczasowy zestaw testów regresji
.\bin\Release\net10.0-windows\SentinelX.exe --self-test "$PWD\test-results\regression"
# Ręcznie, po instalacji modeli i polskiego głosu Windows
.\bin\Release\net10.0-windows\SentinelX.exe --asr-test "$PWD\test-results\asr"
```

`--ui-smoke` nie modyfikuje autostartu, nie rejestruje skrótów, nie instaluje modeli i nie uruchamia mikrofonu. Sprawdza renderowanie stron i błędy bindingów, paletę, panel gotowości, izolację dowodów między zadaniami, mieszane wyniki wielu kroków, zachowanie dowodów po anulowaniu, STOP/resume, zakaz potwierdzania głosem (także z wybudzeniem i interpunkcją), konkurencję/anulowanie zadań, odrzucenie fałszywego „VERIFIED”, zapis ustawień i bezpieczeństwo plików.

Workflow **Windows build and WPF smoke** w `.github/workflows/windows-build.yml` uruchamia restore, build, nowy smoke test i istniejący zestaw regresji. Zachowuje raporty/PNG w `sentinel-validation` oraz aplikacje w `SentinelX-windows-x64` i `SentinelX-Portable-win-x64`. Ta druga paczka jest publikowana jako self-contained i przechodzi osobny smoke test. Wynik bieżącego workflow jest źródłem prawdy o kompilacji — nie sama obecność plików.

Ręczne testy wymagane przed uznaniem całości master prompta za DONE:

- [ ] Mikrofon użytkownika, cisza, szum, odległa mowa, odłączenie urządzenia, wake word i timeout.
- [ ] Silnik AI z faktycznie pobranymi modelami (prawdziwa generacja, obciążenie, anulowanie, zwalnianie pamięci podczas gry).
- [ ] Telefon: zapora Windows, parowanie na prawdziwej sieci Wi‑Fi, wybudzanie Wake-on-LAN, powiadomienia na Androidzie.
- [ ] Tray, konflikt globalnego hotkey, druga instancja, wylogowanie, autostart po ponownym logowaniu.
- [ ] CS2, GPU producenta użytkownika, wydajność monitorowania, overlay na kilku ekranach/DPI.
- [ ] Ręczna ocena wszystkich interakcji GUI i wymaganych funkcji jeszcze dostępnych tylko w `--legacy`.
