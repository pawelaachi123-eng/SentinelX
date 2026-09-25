# Sentinel X 0.93 „PORZĄDKI” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.93 · PORZĄDKI`

## Co nowego

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

- `tests/FileCleanupRegression.cs` rozszerzony: propozycja sprzątania wymienia wyłącznie kopię
  (oryginał zostaje), nic nie ginie przed `potwierdz`, po potwierdzeniu dokładnie 1 kopia trafia do
  recyklera, powtórka na czystym folderze odpowiada „nie ma czego sprzątać”, goła komenda pokazuje
  użycie. Plus wszystkie testy 0.92 (skany, raporty, pojedynczy Kosz, junction).
- Smoke UI + pełna regresja przez `--ui-smoke`, `--self-test` i smoke wersji portable w CI.

## Uczciwie o ograniczeniach

- „Zostawiany” plik wybierany jest regułą najkrótszej ścieżki — jeśli oryginał leży głębiej niż
  kopia, to kopia zostanie. Lista w propozycji zawsze pozwala to zobaczyć i anulować.
- Usunięcie wielu plików ma JEDNO potwierdzenie dla całej listy (lista jest jawna w propozycji);
  trwałe usuwanie nadal nie istnieje.
- Kosz Windows testowany w CI na atrapie recyklera — rzeczywisty Kosz potwierdzi dopiero komputer
  użytkownika.
- Zmiany nazw plików z podglądem (ostatnia część P0.1) pozostają w backlogu.
