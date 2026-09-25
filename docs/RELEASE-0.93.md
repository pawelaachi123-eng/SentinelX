# Sentinel X 0.93 „PORZĄDKI” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.93 · PORZĄDKI`

## Co nowego

### `zmien nazwy: <folder> zamien <tekst> na <tekst>` — zbiorcza zmiana nazw z podglądem

- Działa na plikach **bezpośrednio w podanym folderze** (bez podfolderów — świadoma granica).
- Zamienia fragment nazwy (np. `IMG_` → `wakacje_`), niezależnie od wielkości liter.
- Najpierw pokazuje pełną listę `stara → nowa` oraz pominięte (kolizja z istniejącą nazwą,
  niebezpieczna nazwa wynikowa typu CON/NUL, dowiązania).
- Dopiero `potwierdz` zmienia nazwy (`File.Move` bez nadpisywania), wynik weryfikowany per plik
  (VERIFIED/PARTIAL/FAILED) i audytowany jako `FILE_RENAME`.
- Goła komenda pokazuje użycie zamiast zgadywać.

### `usuń puste pliki: <folder>` — sprzątanie plików 0 B

Znajduje pliki o rozmiarze 0 B (rekurencyjnie, z limitami skanu), pokazuje listę i po `potwierdz`
przenosi je do Kosza — tym samym jednorazowym mechanizmem zgody co pozostałe operacje.

### `usuń duplikaty: <folder>` — sprzątanie duplikatów za zgodą

Rozwinięcie raportu `duplikaty:` z 0.92 o krok wykonawczy:

1. Sentinel skanuje folder (te same granice co raport: maks. 50 000 plików, ≤256 MB hashowane,
   junction/symlinki nieprzechodzone).
2. Z każdej grupy identycznych treści (SHA-256) **zostawia 1 plik** — ten o najkrótszej ścieżce
   (zwykle oryginał), pozostałe kopie trafiają na listę „do Kosza”.
3. Pokazuje dokładną listę i pyta. Dopiero `potwierdz` przenosi pliki do Kosza Windows
   (przywracalne); `anuluj` albo jakiekolwiek inne polecenie porzuca operację.
4. Wynik jest weryfikowany per plik (VERIFIED/PARTIAL/FAILED) i zapisywany w audycie
   (`FILE_RECYCLE`) razem z listą usuniętych i pominiętych.

Bez duplikatów komenda uczciwie odpowiada „nie ma czego sprzątać”.

### Publikacja plików — zmiana procesu (decyzja użytkownika)

Automatyczne wydanie „rolling” po każdym pushu zostało **wyłączone** na prośbę użytkownika.
Workflow `release.yml` publikuje EXE/ZIP tylko:

- po pushu tagu milowego `v*.*.*` (np. `v0.91.2`), albo
- na ręczne żądanie (`workflow_dispatch`, tag domyślnie `rolling`).

Zwykły push na gałąź przechodzi pełny build i testy w „Windows build and WPF smoke”, ale nic nie
publikuje. Wydania milowe na GitHubie: `v0.90.0`, `v0.91.2`; pod tagiem `rolling` zostało ostatnie
automatyczne wydanie `0.92.0` — tag nie jest już przesuwany. Wersja 0.93.0 zostanie opublikowana
dopiero tagiem milowym (np. `v0.93.0`), kiedy użytkownik o to poprosi.

## Weryfikacja

- `tests/FileCleanupRegression.cs` rozszerzony: cykl zgody na sprzątanie duplikatów, cykl zgody na
  puste pliki (tylko 0 B ruszone), pełny cykl zmiany nazw (podgląd, kolizja pominięta i ogłoszona,
  brak zmian przed zgodą, dokładna weryfikacja po zgodzie, powtórka bez par odpowiada „Brak plików
  do zmiany”, gołe komendy pokazują użycie). Plus wszystkie testy 0.92 (skany, raporty, pojedynczy
  Kosz, junction).
- Smoke UI + pełna regresja przez `--ui-smoke`, `--self-test` i smoke wersji portable w CI.

## Uczciwie o ograniczeniach

- Zmiany nazw dotyczą tylko plików bezpośrednio w folderze (podfoldery zostają nietknięte); zmiany
  nazw podfolderów są poza zakresem.
- „Zostawiany” plik wybierany jest regułą najkrótszej ścieżki — jeśli oryginał leży głębiej niż
  kopia, to kopia zostanie. Lista w propozycji zawsze pozwala to zobaczyć i anulować.
- Usunięcie wielu plików ma JEDNO potwierdzenie dla całej listy (lista jest jawna w propozycji);
  trwałe usuwanie nadal nie istnieje.
- Kosz Windows testowany w CI na atrapie recyklera — rzeczywisty Kosz potwierdzi dopiero komputer
  użytkownika.
- P0.1 „plików” jest domknięte; dalsze pomysły (np. filtry rozmiaru w `porzadki`) wracają do P1.
