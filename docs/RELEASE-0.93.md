# Sentinel X 0.93 „PORZĄDKI WYKONAWCZE” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.93 · PORZĄDKI WYKONAWCZE`

Ten przyrost domyka pozycję P0.1 z backloga: bezpieczna praca na plikach staje się w pełni wykonywalna, ale nadal dwustopniowa i przywracalna. Zasada nadrzędna pozostaje: **raporty są tylko do odczytu, a każda operacja zmieniająca dane wymaga osobnego, jawnego potwierdzenia dokładnej listy.**

## Nowe polecenia

| Polecenie | Co robi | Zmienia dane? |
| --- | --- | --- |
| `zmien nazwy: <folder> z <old> na <new>` | Skanuje pliki w folderze (top-level, max 200) zawierające `<old>` w nazwie i proponuje zamianę na `<new>`. Podgląd max 20, wykrywa kolizje i nieprawidłowe znaki. | Tak — dopiero po `potwierdz`, bez nadpisywania |
| `zmien nazwy: <folder>: <old> -> <new>` | Alternatywna składnia strzałkowa dla tego samego. | Tak — po potwierdzeniu |
| `uporzadkuj: <folder>` | Znajduje puste pliki 0 B (rekurencyjnie, limit 50k) i proponuje przeniesienie do Kosza (max 100 na raz) z listą. | Tak — po `potwierdz`, do Kosza |
| `usun duplikaty: <folder>` | Skan duplikatów jak `duplikaty:` (SHA-256), proponuje zachowanie pierwszego pliku z grupy (alfabetycznie) i przeniesienie reszty do Kosza (max 100). Pokazuje grupy, ile można odzyskać. | Tak — po `potwierdz`, do Kosza |
| `zmien nazwy` / `uporzadkuj` / `usun duplikaty` (bez folderu) | Podpowiedź użycia zamiast zgadywania. | Nie |

Skróty w palecie `//`: `//zmien-nazwy`, `//uporzadkuj`, `//usun-duplikaty` (uzupełnij folder i wyślij).

## Granice bezpieczeństwa (wbudowane, niekonfigurowalne)

- **Bez ucieczki zakresu**: junction/symlinki nie są przechodzone podczas skanu; pliki-dowiązania nie są usuwane ani przemianowywane; folder startowy będący dowiązaniem jest odrzucany.
- **Limity**: maks. 50 000 plików w skanie rekurencyjnym, hashowane tylko pliki ≤ 256 MB, batch rename max 200, batch delete max 100, podglądy ograniczone (20 nazw, 10 grup).
- **Brak nadpisywania**: rename nie nadpisuje istniejącego pliku; cel już istniejący jest pomijany i raportowany jako kolizja.
- **Tylko do Kosza**: wszystkie usuwania to `RecycleOption.SendToRecycleBin` (przywracalne). Trwałe usuwanie nie istnieje.
- **Jednorazowe potwierdzenie**: każda propozycja wygasa po dowolnej innej wypowiedzi — późniejsze „tak” (np. przy podpowiedzi „Czy chodziło Ci o…”) nigdy nie odpali starej operacji.
- **Jednorazowy pending**: tylko jedna operacja oczekuje na potwierdzenie; nowa komenda czyszcząca poprzednią.
- Każda operacja ma wpis w lokalnym audycie (`FILE_BATCH_RENAME_PREVIEW`, `FILE_BATCH_RENAME`, `FILE_TIDY_EXEC_PREVIEW`, `FILE_TIDY_EXEC`, `FILE_DUPLICATES_CLEANUP_PREVIEW`, `FILE_DUPLICATES_CLEANUP`) z dowodem (VERIFIED/FAILED).

## Weryfikacja

- `tests/FileCleanupRegression.cs` rozszerzony:
  - batch rename: podgląd z listą old→new, brak zmian przed potwierdzeniem, stale „tak” nie odpala, ponowna propozycja → `potwierdz` → zweryfikowane przemianowanie 2 plików, plik niepasujący zostaje, składnia `->`, kolizje wykryte, bare command pokazuje użycie.
  - tidy exec: puste pliki 0 B, podgląd, brak usuwania przed potwierdzeniem, stale „tak” nie odpala, `potwierdz` → pliki w recyclerze, pełny plik zostaje.
  - duplicate batch cleanup: 3 identyczne pliki + unikalny, podgląd grup, brak usuwania przed potwierdzeniem, stale „tak” nie odpala, `potwierdz` → dokładnie jeden duplikat zostaje, unikalny nietknięty.
  - bare commands: `zmien nazwy`, `uporzadkuj`, `usun duplikaty` pokazują użycie.
- Smoke UI: wszystkie nowe komendy przez prawdziwy silnik (ActionEngine → IntentRouter → FileCleanupService) — rename, tidy exec, duplicate cleanup, bare commands — plus pełny zestaw regresji w `--ui-smoke`.
- CI `windows-latest`: check architektury, build, smoke UI, `--self-test`, smoke wersji portable.

## Uczciwie o ograniczeniach

- Batch rename działa tylko na plikach w danym folderze (nie rekurencyjnie), aby uniknąć przypadkowego przemianowania w podfolderach. Rekurencyjny tryb jest poza zakresem tego przyrostu.
- Tidy exec usuwa tylko puste pliki 0 B — to najbezpieczniejszy wykonywalny krok z raportu `porzadki`. Przenoszenie dużych plików czy archiwizacja są poza zakresem.
- Duplicate cleanup zachowuje pierwszy plik alfabetycznie — nie pyta o „najnowszy/najstarszy” w tym przyroście, aby zachować determinizm. Limit 100 plików na potwierdzenie chroni przed masowym czyszczeniem.
- Kosz Windows wymaga działającej powłoki; na systemach bez Kosza operacja zgłosi błąd zamiast usuwać.
- Brak paska postępu w czacie — komenda czeka do końca skanu (limit 50k plików).

## Co dalej (P0)

- Sekwencje działań: nazwane kroki, plan z podglądem, statusy kroków, zatrzymanie na błędzie, wznowienie.
- Panel archiwum w UI: lista archiwów z podglądem Markdowna i usuwaniem.
- Podpisywanie buildów lub jawnie opisana ścieżka bez podpisu.
