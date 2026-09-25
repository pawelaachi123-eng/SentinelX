# Sentinel X 0.92 „BEZPIECZNE PLIKI” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.92 · BEZPIECZNE PLIKI`

Ten przyrost otwiera pozycję P0 z backloga: bezpieczna praca na plikach. Zasada nadrzędna:
**raporty są zawsze tylko do odczytu, a jedyna operacja zmieniająca dane (przeniesienie jednego
pliku do Kosza) wymaga osobnego, jawnego potwierdzenia dokładnej ścieżki.**

## Nowe polecenia

| Polecenie | Co robi | Zmienia dane? |
| --- | --- | --- |
| `duplikaty: <folder>` | Skanuje folder (rekurencyjnie) i grupuje pliki o identycznej treści wg SHA-256. Pokazuje grupy, zajętość i ile można odzyskać. | Nie |
| `porzadki: <folder>` | Raport porządkowy: liczba i łączny rozmiar plików, 5 największych, puste (0 B), podsumowanie duplikatów, 5 najstarszych. | Nie |
| `usuń do kosza: <ścieżka>` | Proponuje przeniesienie JEDNEGO pliku do Kosza Windows. Dopiero po `potwierdz` wykonuje; `anuluj` porzuca. | Tak — tylko po potwierdzeniu |
| `duplikaty` / `porzadki` (bez folderu) | Podpowiedź użycia zamiast zgadywania. | Nie |

Skróty w palecie `//`: `//duplikaty`, `//porzadki` (uzupełnij folder i wyślij).

## Granice bezpieczeństwa (wbudowane, niekonfigurowalne)

- **Bez ucieczki zakresu**: junction/symlinki nie są przechodzone podczas skanu; pliki-dowiązania
  nie są usuwane; folder startowy będący dowiązaniem jest odrzucany.
- **Limity skanu**: maks. 50 000 plików, hashowane tylko pliki ≤ 256 MB, niedostępne pliki są
  pomijane i liczone — skan nigdy nie jest nieograniczony.
- **Usuwanie wyłącznie do Kosza** (przywracalne), jeden plik naraz, po dwustopniowym potwierdzeniu.
  Trwałe usuwanie i usuwanie folderów: świadomie poza zakresem tego przyrostu (patrz backlog).
- **Jednorazowe potwierdzenie**: propozycja usunięcia wygasa po dowolnej innej wypowiedzi — późniejsze
  „tak” (np. przy podpowiedzi „Czy chodziło Ci o…”) nigdy nie odpali usunięcia.
- Każda operacja ma wpis w lokalnym audycie (`FILE_SCAN_DUPLICATES`, `FILE_SCAN_TIDY`, `FILE_RECYCLE`)
  z dowodem (VERIFIED/FAILED) — tak jak pozostałe akcje.

## Weryfikacja

- `tests/FileCleanupRegression.cs`: grupy duplikatów (dokładnie 1 grupa × 2 pliki na fiksturze),
  brak duplikatów w folderze unikalnym, puste pliki poza grupami, raport porządkowy (liczby,
  największe, puste, najstarsze, „tylko do odczytu”), junction nie jest przechodzony
  (test z `mklink /J`), pełny cykl usunięcia (propozycja → nic nie usunięte → „tak” po drodze
  NIE usuwa → ponowna propozycja → `potwierdz` → dokładnie ten plik w recyklerze), anulowanie,
  brak pliku, gołe polecenie.
- Smoke UI: skan duplikatów i gołe `duplikaty` przez prawdziwy silnik (ActionEngine → IntentRouter
  → FileCleanupService) + pełny zestaw regresji w `--ui-smoke`.
- CI `windows-latest`: check architektury, build, smoke UI, `--self-test`, smoke wersji portable.

## Uczciwie o ograniczeniach

- Skan 50 tys. plików na wolnym dysku może potrwać — brak paska postępu w czacie (komenda czeka).
- Kosz Windows wymaga działającej powłoki; na systemach bez Kosza operacja zgłosi błąd zamiast usuwać.
- `usuń do kosza` działa na pojedynczych plikach; foldery i wiele plików naraz są poza zakresem.
- Raporty nie przewidują „automatycznego sprzątania” — każda decyzja należy do użytkownika.
- Trwałe usuwanie (poza Kosz) nie istnieje i wymagałoby osobnej zgody — celowo niezaimplementowane.
