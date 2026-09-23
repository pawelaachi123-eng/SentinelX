# SENTINEL X 0.90 — „Rozumienie"

Data: 2026-09-23 · wersja: `0.90.0` · gałąź: `arena/01a0ca99-sentinelx` · platforma: Windows 10/11 x64 · .NET 9 (WPF, MVVM)

## 1. Co odpowiada na zgłoszone potrzeby

| Potrzeba | Stan | Gdzie to jest |
| --- | --- | --- |
| Rozumienie literówek i źle zapisanych zdań | **zrobione** dla poleceń (katalog 184 fraz), nie dla treści wyszukiwania | `CommandUnderstanding.cs`, `Core/IntentCatalog.cs` |
| Skróty | **zrobione**, 18 jawnych skrótów, lista przez `skróty` | `Core/IntentCatalog.cs` |
| Uruchamianie rzeczy (np. CS2) | **zrobione**, 7 tytułów Steam + 20 aplikacji, dowód z nazwy procesu | `AppLauncherService.cs` |
| Minimum 20 nowych użytecznych funkcji | **zrobione**, 24 narzędzia offline + 5 funkcji przekrojowych = 29 | `UtilityToolbox.cs`, `WorkspaceInsightsService.cs` |
| Osobny folder Sentinela + comiesięczne usuwanie rozmów | **zrobione**, archiwum z odczytem zwrotnym przed usunięciem | `MemoryArchiveService.cs` |
| Plik `.exe` do pobrania z GitHuba | **zrobione**, assety wydania na tag `v*.*.*` | `.github/workflows/release.yml` |
| Poprawa architektury i rozumowania | **częściowo**: warstwa rozumienia przed modelem, katalog intencji, czyste funkcje narzędzi, dokument architektury; `CommandRouter` nadal ma 510 linii | `docs/ARCHITECTURE.md` |

## 2. Nowe funkcje (lista)

### Rozumienie poleceń
1. Naprawa literówek w całych frazach: „ststus pamieci” → „status pamięci”, „wlącz discorda” → „włącz discord”, „diagnostyka komputer” → „diagnostyka komputera”.
2. Naprawa pojedynczych słów w poleceniach z argumentem: „ile mam ramuu” → „ile mam ramu”.
3. Transpozycje liczone jako jedna edycja (Damerau-Levenshtein), próg podobieństwa 0,80, wymagana przewaga 0,08 nad drugą kandydaturą — przy niejednoznaczności Sentinel **nie zgaduje**.
4. 18 skrótów: `cs dc st ch nt kl yt mz dk sp sn sns zs tp ti zd pr pm`.
5. Jawny komunikat „Zrozumiałem jako: „…” (poprawiono: …)” — użytkownik zawsze widzi interpretację.
6. Zabezpieczenie: katalog napraw nie zawiera czasowników niszczących (`usun`, `zamknij`, `wyłącz`, `czyść`, `potwierdź`, `zatrzymaj`), test przegląda katalog przy każdej zmianie.

### Uruchamianie
7. Gry Steam przez `steam://rungameid/<id>`: CS2 (730), Dota 2 (570), Cyberpunk 2077 (1091500), Wiedźmin 3 (292030), Elden Ring (1245620), Terraria (105600), Rust (252490).
8. Aplikacje po standardowej ścieżce instalacji: VS Code, VLC, OBS Studio, Firefox, Microsoft Edge, Telegram — brak instalacji jest zgłaszany wprost, bez udawania sukcesu.
9. Systemowe: Paint, Terminal Windows (obok istniejących: Notatnik, Kalkulator, Menedżer zadań, Eksplorator, Ustawienia).
10. Każde uruchomienie potwierdzane nazwą procesu z PID i godziną odczytu; brak procesu w zadanym czasie = „wysłano, niezweryfikowane”.

### Narzędzia offline (24, bez sieci i bez modelu)
11. `policz 12,5*4` — własny parser wyrażeń (`+ - * / % ^`, nawiasy, unary minus), dzielenie przez zero odrzucone, kod nie jest wykonywany.
12. `procent 15 z 240` · 13. `ile to procent 30 z 240` · 14. `vat 100 [brutto]` (stawka 23%).
15. `przelicz 5 km na mile` — długość, masa, objętość, dane (binarnie), prędkość, temperatura (°C/°F/K).
16. `ile dni do 24.12` · 17. `ile dni od 1.1.2020` · 18. `jaki dzien tygodnia 1.1.2030` · 19. `ile zostalo do 18:00`.
20. `haslo 20` — generator `RandomNumberGenerator`, mieszanie klas znaków, podana entropia, nic nie jest zapisywane ani wysyłane.
21. `uuid` · 22. `ile slow: tekst` · 23. `base64: tekst` i `dekoduj base64: …` · 24. `hash tekstu: …` (SHA-256).
25. `json: {…}` — walidacja i pretty-print · 26. `slug: …` · 27. `transliteruj: …` · 28. `wielkie litery: …` · 29. `male litery: …` · 30. `odwroc tekst: …`.
31. `losuj 1-100` · 32. `rzuc kostka` · 33. `wybierz losowo: pizza, sushi` · 34. `bmi 80 180` (z disclaimerem) · 35. `rzymskie 2026` / `z rzymskich MMXXVI` · 36. `kolor 1fa2c3` (RGB, HSL, kontrast z bielą i czernią wg WCAG).

Argumenty zachowują znaki użytkownika (wielkość liter, polskie ogonki) — dopasowanie polecenia jest po normalizacji, treść argumentu nie.

### Funkcje przekrojowe
37. `plan dnia` — przeterminowane, dzisiejsze, najbliższe przypomnienia, przypięte wspomnienia, aktywny projekt, ostatnia rozmowa + jawna informacja, że przypomnienia działają tylko przy uruchomionej aplikacji.
38. `szukaj wszystkiego: fraza` — wspomnienia, aktywna rozmowa, zadania, projekty, odczyty diagnostyczne (do 5 trafień w kategorii).
39. `statystyki` — ile rozmów, wspomnień, zadań, przypomnień, projektów, odczytów i wpisów historii oraz ile to zajmuje na dysku.
40. `backup` — kopia magazynów do `Backups/<data>/` z manifestem SHA-256 i odczytem zwrotnym każdej kopii.
41. `pomoc` / `co umiesz` i `skróty` — odkrywalność poleceń z czatu.

### Pamięć
42. Własny folder archiwum `Memory/Archives/RRRR-MM/` z plikiem JSON i czytelnym Markdownem.
43. Comiesięczna rotacja: ustawienie „Archiwum rozmów (miesiące)” (domyślnie 1, 0 = wyłączone, zakres 0–120) — przy starcie rozmowy starsze niż okno są archiwizowane i usuwane z aktywnego magazynu, a fakt jest zgłaszany w czacie.
44. Usuwanie z magazynu **dopiero po** zgodnym odczycie zwrotnym zapisanego pliku; wspomnienia i profil nie są archiwizowane ani kasowane.
45. `archiwizuj rozmowy` (ręcznie, także konkretny miesiąc), `archiwa` (lista z hashami), `usuń archiwum RRRR-MM` (tylko folder archiwum).
46. W trybie prywatnym archiwizacja odmawia z wyjaśnieniem.

### Dystrybucja
47. `.github/workflows/release.yml`: tag `v*.*.*` → publikacja assetów (portable ZIP, instalator EXE, `SHA256SUMS.txt`, `BUILD.txt`); ręczny dispatch buduje i weryfikuje, ale nie publikuje.

## 3. Jak pobrać

- **Wydanie:** <https://github.com/pawelaachi123-eng/SentinelX/releases> → najnowszy tag `v0.90.0` → pliki w sekcji *Assets*.
- **Portable:** rozpakuj `SentinelX-0.90.0-win-x64-portable.zip` i uruchom `SentinelX.exe` (runtime .NET 9 jest w środku).
- **Instalator:** `SentinelX-0.90.0-win-x64-setup.exe` — instalacja per-user, bez uprawnień administratora.
- **Weryfikacja:** porównaj sumy z `SHA256SUMS.txt` (`Get-FileHash plik -Algorithm SHA256`).
- **Z builda CI** (gałąź, bez tagu): artefakty `SentinelX-Portable-win-x64` i `SentinelX-Setup-win-x64` w podsumowaniu workflow `Windows build and WPF smoke`.

Pliki **nie są podpisane cyfrowo**. SmartScreen pokaże „Nieznany wydawca” — to stan rzeczywisty, nie błąd paczki, i nie jest ukrywany.

## 4. Wymagania

- Windows 10/11 x64.
- Ollama jest **opcjonalne**: bez niego działają polecenia, pomiary, narzędzia, pamięć, projekty, zadania i diagnostyka; odpowiedzi generatywne wymagają lokalnego modelu.
- Gry wymagają zainstalowanego Steam i własności tytułu.

## 5. Wyniki testów

CI: GitHub Actions, `windows-latest`, .NET 9.

| Run | Zakres | Wynik |
| --- | --- | --- |
| `35924089578` | kod: checki architektury, restore, build, smoke UI, regresje, publish portable, sumy, smoke portable, instalator Inno, instalacja + smoke, artefakty | **WYNIKI_PONIŻEJ** |
| `35924096153` | ten sam pipeline na pull requeście | **WYNIKI_PONIŻEJ** |

Zestawy regresji uruchamiane przez `--ui-smoke` (11): Backend, Product, Release, Memory, Project, Task, DiagnosticSnapshot, AiStream, **Understanding**, **Utility**, **MemoryArchive** — plus renderowanie 12 stron z wyłapywaniem błędów wiązań WPF i 31 sprawdzeń end-to-end przez prawdziwy silnik czatu (28 narzędzi i komend w pętli + naprawa literówek w pomiarze RAM, naprawa nazwy aplikacji i `szukaj wszystkiego`).

### Niezweryfikowane w tym wydaniu
- Uruchamianie gier i aplikacji na komputerze z zainstalowanym Steam/tytułami — CI nie ma gier; weryfikowany jest kod ścieżki i protokół `steam://rungameid`, a nie samo uruchomienie.
- Odpowiedzi żywego modelu (brak Ollama w CI) — transport i logika fragmentów są testowane na wstrzykniętym HTTP.
- Mikrofon, rozpoznawanie mowy, tryb gry.
- Podpis cyfrowy — go nie ma.

## 6. Znane ograniczenia 0.90

- Naprawa literówek jest dopasowaniem tekstu do katalogu: bez odmiany, bez synonimów, bez kontekstu rozmowy; przy niejednoznaczności celowo nic nie poprawia.
- Wyszukiwanie **treści** (wspomnienia, rozmowa, `szukaj wszystkiego`) pozostaje tekstowe.
- `przelicz` nie przelicza walut; brak zrzutów ekranu i sterowania głośnością; VAT tylko 23%.
- Archiwum nie ma przywracania do aktywnej rozmowy.
- `CommandRouter` ma 510 linii — rozbicie jest na backlogu P1.
