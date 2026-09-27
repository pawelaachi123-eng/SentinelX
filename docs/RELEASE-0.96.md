# RELEASE 0.96 · JARVIS — pulpit, multimedia, zasilanie, rutyny

Data: 2026-09-27. Gałąź: `arena/01a0df87-sentinelx`. Wersja: `0.96 · JARVIS`, pakiet `0.96.0`.
Bez budowania EXE — na wyraźne polecenie użytkownika ten przyrost to **funkcje i narzędzia**, nie pakowanie.

## Zamówienie użytkownika

„nie buduje exe ani tylko pierwsze co to będziemy robić funkcje albo toolsy i dodaj mega fajne toolsy
albo funkcje które ma każdy Jarvis albo te najpierwsze Jarvis” → zestaw wybrany w rozmowie:
**sterowanie pulpitem + multimedia + briefing, potem rutyny i reszta**, a sterowanie zasilaniem
działa tak, że **wypowiedzenie polecenia jest zgodą** (bez drugiego potwierdzania).

## 1. Sterowanie pulpitem (`Core/WindowManager.cs`, `Core/InputSender.cs`)

| Polecenie | Co robi |
|---|---|
| `okna` | lista widocznych okien z tytułami i znacznikiem aktywnego (tylko odczyt) |
| `minimalizuj wszystko` | powrót na pulpit (Windows+D) |
| `minimalizuj okno` · `maksymalizuj okno` · `przywróć okno` | stan aktywnego okna |
| `zamknij okno` | WM_CLOSE do aktywnego okna — dokładnie to, co kliknięcie „X” |
| `przełącz okno` | Alt+Tab (Alt trzymany do końca, inaczej system nie pokazuje przełącznika) |
| `przełącz na: chrome` | aktywacja okna po fragmencie tytułu (bez rozróżniania wielkości liter) |
| `okno w lewo` · `okno w prawo` | dokowanie do połowy ekranu (Windows+←/→) |
| `pełny ekran` | F11 |

Zasady:

- **Nie zamykam siebie.** Przed `zamknij okno` sprawdzam, czy aktywne okno należy do procesu
  Sentinela (`GetWindowThreadProcessId` vs `Environment.ProcessId`) — jeśli tak, odmawiam
  z wyjaśnieniem. „zamknij okno” nie potrafi przypadkiem wyłączyć asystenta z Twoimi danymi.
- **Nie zabijam procesów.** `zamknij okno` = WM_CLOSE, więc program może zapytać o zapisanie pracy.
  To inna komenda niż `zamknij <aplikacja>`, która nadal idzie przez Permission Center.
- **Uczciwość zamiast udawania.** Syntetyczne klawisze (UIPI) potrafią zostać odrzucone, gdy aktywne
  okno ma wyższe uprawnienia — wtedy pada wprost „Nie udało się …”, a nie „zrobione”.
- Implementacja: `user32 keybd_event` (wycofane, ale obecne w każdym Windows) — bez WinForms
  i bez nowych pakietów NuGet.

## 2. Multimedia (`Core/InputSender.cs`)

`pauza` · `wznów odtwarzanie` · `następny utwór` · `poprzedni utwór` · `zatrzymaj odtwarzanie` ·
`głośniej` · `ciszej` (±10% przez Core Audio) · `wycisz` · `przywróć dźwięk`.

Klawisze multimediów trafiają do odtwarzacza, który system uznaje za aktywny — Sentinel nie
zgaduje nazwy programu i nie steruje konkretną aplikacją. `pauza` jest przełącznikiem (play/pause),
więc to samo polecenie wznawia odtwarzanie.

## 3. Ekran i zasilanie (`Core/PowerManager.cs`)

| Polecenie | Co robi | Odwracalne? |
|---|---|---|
| `zablokuj ekran` | Windows+L | tak — hasło/PIN |
| `wygasz ekran` | monitor w stan spoczynku | tak — ruch myszy |
| `uspij komputer` | uśpienie po 20 s | tak — przycisk zasilania; 20 s na „anuluj zamknięcie” |
| `zamknij komputer` | `shutdown /s /t 60` | nie — 60 s na „anuluj zamknięcie” |
| `restart komputera` | `shutdown /r /t 60` | nie — 60 s na „anuluj zamknięcie” |
| `anuluj zamknięcie` | `shutdown /a` + odwołanie odliczania | — |

Uzgodniona zasada: **polecenie jest zgodą** — nie pytam drugi raz, gdy powiesz „zamknij komputer”.
W zamian każda akcja nieodwracalna ma okno do odwołania: Sentinel uzbraja odliczanie
(`CancellationTokenSource` + `Task.Delay`) i odpowiada od razu, wypisując sposób odwołania.
Zamknięcie i restart idą przez systemowy `shutdown.exe`, więc Windows nadal może zapytać o
niezapisane dane — tego nie omijam.

## 4. „dzień dobry” i „dobranoc” (`Core/JarvisBriefing.cs`)

Jedno polecenie zamiast pięciu: data i godzina, zadania na dziś i przeterminowane, dzisiejsze
przypomnienia, odczyty CPU/RAM/dyski/czas pracy/bateria oraz przypięte notatki z pamięci.
Wieczorem dodatkowo: co zrobione, co zostaje na jutro i wskazówka, jak zamknąć komputer.

Czysta funkcja (`BriefingData` → tekst) — bez modelu, bez sieci, bez dostępu do dysku,
dzięki czemu jest w pełni testowalna i **niczego nie wykonuje**. Brak danych to pusta sekcja,
nigdy wymyślony wpis.

## 5. Rutyny (sceny) (`RoutineService.cs`, `RoutineCommands.cs`)

| Polecenie | Co robi |
|---|---|
| `rutyny` | lista z krokami (widzisz, co się uruchomi) |
| `uruchom rutynę: poranek` · `rutyna poranek` | wykonuje kroki po kolei, raportując każdy |
| `dodaj rutynę: poranek = która godzina \| plan dnia \| bateria` | zapis (nadpisuje istniejącą) |
| `usuń rutynę: poranek` | usunięcie jednej rutyny |

- Magazyn `Memory/routines.json` z tymi samymi gwarancjami co pamięć i projekty: zapis atomowy
  przez `.tmp`, kopia uszkodzonego pliku (`*.damaged-<czas>`) zamiast nadpisania, odczyt zwrotny
  z SHA-256 (`VerifyPersistedState`, używane też przez `samokontrola`).
- **Rutyna nie przyjmie polecenia niszczącego dane** (`usuń wszystkie wspomnienia`,
  `usuń do kosza`, `potwierdź`, `zmień nazwy`, …) — taki krok jest odrzucany przy zapisie,
  bo rutyna wykonuje się bez pytania o zgodę. Komunikat mówi, żeby wykonać to polecenie samodzielnie.
- Limity: 40 rutyn, 12 kroków, 160 znaków na krok; zagnieżdżanie rutyn ma twardy limit 3 poziomów.
- Trzy rutyny startowe (`poranek`, `praca`, `koniec dnia`) są **wyłącznie do odczytu** — żadna
  nie zmienia danych ani nie wyłącza komputera.
- Wspólna obsługa dla obu interfejsów (`RoutineCommands`), więc nowy shell MVVM i tryb zgodności
  działają identycznie.

## 6. Pozostałe narzędzia

- `pomodoro 25` · `skupienie 30` · `przerwa 5` — licznik pracy jako uczciwe przypomnienie
  z konkretną godziną (działa, gdy aplikacja jest uruchomiona, jak każde moje przypomnienie).
- `historia schowka` · `schowek 2` — pierścień 12 ostatnich tekstów skopiowanych przez Sentinela
  (`kopiuj: …`). **Tylko pamięć sesji**: nic nie trafia na dysk, bo schowek bywa nośnikiem haseł.
- `znajdź plik: raport` — szukanie po fragmencie nazwy w Pulpicie, Dokumentach i Pobranych.
  Tylko odczyt, z twardymi limitami: głębokość 4, 3000 katalogów, 20 wyników, 8 s; katalogi
  systemowe i techniczne pomijane; każde ograniczenie jest wypisane w odpowiedzi
  („· pokaż pierwsze wyniki · część katalogów pominąłem (brak dostępu)”).

## 7. Testy (dowód, nie deklaracja)

- `tests/JarvisRegression.cs` (NOWY, wpięty do `UiSmokeTestRunner`) — rutyny (seed, zapis, trwałość,
  odrzucenie kroków niszczących i limitów, usuwanie, uszkodzony plik odkładany na bok, SHA-256),
  wykonanie rutyny krok po kroku przez podstawiony runner, briefing (rano/wieczór/bez danych),
  historia schowka (deduplikacja, pierścień, brak wpisu), FileFinder (głębokość, wielkość liter,
  limity, brak katalogu), księgowość zasilania (Arm → Pending → Abort, nieznana akcja odrzucona),
  `okna` / `historia schowka` / `znajdź plik` jako rozpoznane polecenia.
- **Testy nie dotykają stanu maszyny CI**: nie blokuję ekranu, nie zamykam komputera, nie puszczam
  multimediów, nie zmieniam głośności. Sprawdzam wyłącznie księgowość uzbrajania i funkcje czyste.
- `Core/WindowManager.ListWindows()` — asercja tylko o tym, że wynik nie rzuca i ma niepuste tytuły.

## 8. Czego 0.96 nie robi (świadomie)

- Nie steruje konkretną aplikacją multimedialną po nazwie (klawisze idą do aktywnego odtwarzacza).
- Nie zabija procesów i nie zamyka aplikacji bez zgody — `zamknij <aplikacja>` nadal wymaga
  Permission Center.
- Nie skanuje dysków („znajdź plik” ma limity i szuka tylko w folderach użytkownika).
- Nie zapisuje historii schowka na dysk.
- Nie buduje EXE — ten przyrost to wyłącznie funkcje i narzędzia.
