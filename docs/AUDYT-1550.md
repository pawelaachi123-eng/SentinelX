# AUDYT 1550 — co jest w kodzie, a czego nie ma (0.97)

**Data audytu:** 2026-09-27 · **commit:** `88b7478` (branch `arena/01a0e28e-sentinelx`) · **CI:** run `36322692492` i `36322692531` = SUCCESS

## Sprawdź to sam (jedno polecenie)

```bash
python3 scripts/audit-1550.py     # tabela + docs/AUDYT-1550-AUTO.md
python3 scripts/check-architecture.py   # bramki projektu (PASS = zielono)
```

Skrypt liczy wyzwalacze poleceń w kodzie i nie przyjmuje żadnej deklaracji na wiarę.
Jeśli wrzucisz master listę do repo jako `docs/LISTA-1550.md`, ten sam skrypt zrobi
`docs/AUDYT-1550-POZYCJE.md` — listę kontrolną pozycja po pozycji z dopasowaniem.

## Jak liczyłem (żeby to nie była deklaracja)

1. Przeszedłem **kod** i policzyłem realne wyzwalacze poleceń: `Starts(text, …)`, `text is "…"`,
   `case "…"`, `StartsWith("…")` oraz wzorce `Regex.Match(text, @"^…")` w każdym module.
2. Sprawdziłem, co uruchamia każdy moduł w `--ui-smoke` (21 zestawów regresji, każdy z asercjami).
3. Sprawdziłem `IntentCatalog` (490 fraz rozumienia) i paletę `CommandCatalog` (73 wpisy).

**Czego ten audyt NIE może zrobić:** master lista 1550 pozycji **nie leży w repozytorium** (jestem
w stanie sprawdzić tylko to, co jest w kodzie i w `docs/`) — nie ma jej ani w `docs/`, ani w historii
gita, ani na innych branchach. Dlatego werdykt jest **per sekcja** (1–20 i bonus) z liczbami, a nie
„pozycja 1…1550”. Jeśli wkleisz listę raz jeszcze albo wrzucisz ją do repo jako `docs/LISTA-1550.md`,
zrobię mapowanie pozycja-po-pozycji z odhaczeniem każdej.

## Werdykt: NIE — nie ma wszystkich 1550 pozycji. Jest około jednej trzeciej.

| Sekcja | Pozycje | Status | Co realnie jest w kodzie | Czego nie ma |
|---|---|---|---|---|
| **1. Rdzeń** | 1–45 | **ZROBIONE** | `Core/Runtime/` 18 modułów (event bus, kolejka+retry+anulowanie, bezpieczniki, cache LRU/LFU, cron, maszyna stanów, graf workflow, flagi, health, metryki p50/p95, dziennik JSONL, manifest SHA-256, kopie ZIP, sejf AES-256-GCM, serializator SXB1) + `RuntimeCommands` **86 wyzwalaczy** | — |
| **2. Modele lokalne** | 46–120 | **CZĘŚCIOWO (~40%)** | `ModelToolbox` **29 wyzwalaczy**: katalog 17 modeli, karta modelu, dobór do pamięci, role, kwantyzacje (8), presety, 10 szablonów promptów, KV cache, `model kv`, `model pamiec`, `model porownaj`, `model kolejka`, `model polityka`, `model offline` | pobieranie/usuwanie modeli i pasek postępu (wymaga sieci), panel modeli w GUI, benchmark tokenów na sprzęcie, automatyczny dobór modelu do zadania, licencje modeli, indeks embeddingów |
| **3. Prywatność** | 121–185 | **CZĘŚCIOWO (dużo)** | `PrivacyToolbox` **33 wyzwalacze**: mapa danych, duże pliki, wiek danych, **podgląd** retencji, szyfrowanie (co jest AES-256-GCM, a co nie), uprawnienia, `co wysylam`, plan eksportu, minimalizacja; w rdzeniu: sejf, manifest integralności, dziennik zdarzeń | automatyczne kasowanie z potwierdzeniem, menedżer zgód (per uprawnienie), szyfrowanie historii rozmów, menedżer sesji/urządzeń, audyt dostępu mikrofon/kamera |
| **4. Kodowanie** | 186–340 | **CZĘŚCIOWO (~50%)** | `DeveloperToolbox` **53 wyzwalacze** (diff, regex, semver, IP/podsieci, JWT, UUID7/ULID, generatory JSON/SQL/mock, kodowania base32/58, skróty, kody znaków, CSV, szablony, tokeny, kontekst) + `CodeInsightsService` **20** (skan, statystyki, zależności/cykle, audyt wzorców, duplikaty, licencje, funkcje) + `UtilityToolbox` **179** (tekst, liczby, hasła, kodowania, data/czas) | generatory całych projektów, integracja z Gitem, profiler, transpilacja, generowanie testów, refaktoryzacje |
| **5. Architektura** | 341–420 | **BRAK** | tylko graf workflow i `dag:` w rdzeniu (planowanie kolejności) | diagramy architektury, ADR-y, analiza długu technicznego, wykrywanie cykli zależności między modułami, macierze zależności |
| **6. Full-stack** | 421–520 | **BRAK** | — | generowanie API/frontendu, ORM-y, migracje, kontenery, auth |
| **7. Wielojęzyczność** | 521–590 | **CZĘŚCIOWO (mało)** | rozumienie polskich poleceń: `IntentCatalog` **490 fraz**, naprawa literówek, 12–13 kategorii intencji; polskie komunikaty UI | i18n interfejsu (EN/DE/…), tłumaczenie treści lokalnym modelem, wykrywanie języka wejścia |
| **8. Pamięć i wiedza** | 591–670 | **CZĘŚCIOWO (mało)** | rozmowa + wspomnienia + notatki, `SessionFactBook` (fakty sesji), `LearnedPatterns`, dziennik lekcji, archiwum miesięcy, projekty, izolacja kontekstu | baza wektorowa/embeddingi, RAG z dokumentów, import PDF/DOCX, graf wiedzy, wyszukiwanie semantyczne |
| **9. Głos** | 671–730 | **CZĘŚCIOWO (pre-0.97)** | Whisper.net (ASR), synteza mowy, VAD, kalibracja mikrofonu, tryb ciągły, `..voice` | wake-word („Sentinel”), streaming ASR, wielojęzyczny TTS, komendy głosowe nowych sekcji (15/17/3) |
| **10. Wizja** | 731–800 | **CZĘŚCIOWO (mało, pre-0.97)** | Gaming Mode + Watch (FPS, nakładka), odczyt VRAM jako „brak licznika” | OCR ekranu, analiza kamery, YOLO/detekcja obiektów, opis obrazu modelem |
| **11. Automatyzacja** | 801–880 | **CZĘŚCIOWO** | rutyny („sceny”) z podglądem, kolejka zadań z priorytetami/retry/backoff, cron 5-polowy, flagi z procentem, harmonogramy przypomnień | webhooki i wyzwalacze zdarzeń, makra nagrywania akcji, edytor harmonogramów w GUI, łańcuchy między aplikacjami |
| **12. Scraping / research** | 881–940 | **BRAK** | — (świadomie: brak sieci; „brak telemetrii” to deklaracja, nie luka) | pobieranie stron, cytowanie źródeł, research wieloetapowy |
| **13. Produktywność** | 941–1020 | **CZĘŚCIOWO** | `ProductivityToolbox` **13 wyzwalaczy** (kwota słownie, lorem, statystyki tekstu, macierz Eisenhowera, slajdy) + zadania, przypomnienia, projekty, notatki, snapshoty, eksporty | kalendarz z przeciąganiem, Pomodoro, szablony notatek, OCR dokumentów, integracja poczty |
| **14. Komunikacja** | 1021–1070 | **CZĘŚCIOWO (dużo)** | `LifeToolbox` **9 wyzwalaczy komunikacyjnych**: SMS (GSM-7/UCS-2), wpis 280, szkic maila, agenda, protokół, follow up, skracanie, ton, czytelność | realne wysyłanie (świadomie nie), integracja z pocztą/kalendarzem, klienty czatu |
| **15. Analiza danych** | 1071–1140 | **ZROBIONE** | `AnalysisToolbox` **48 wyzwalaczy** (24 rodziny): statystyki, kwartyle/percentyle, wariancja/odchylenie, skośność/kurtoza, CV, przedział ufności, korelacja Pearson+Spearman, regresja+R², prognoza, trend, histogram, normalizacja, odległości, macierz pomyłek (F1/MCC), entropia, Gini, outliery, wygładzanie, rangi, test t | wykresy jako obraz, eksport CSV/JSON wyników |
| **16. Finanse** | 1141–1200 | **CZĘŚCIOWO** | 8 wyzwalaczy: ROI, break-even, amortyzacja, inflacja, oszczędności, cel, budżet 50/30/20, kwota słownie | kursy walut (sieć), podatki, faktury, kredyty, śledzenie portfela |
| **17. Zdrowie** | 1201–1250 | **ZROBIONE (arytmetyka)** | `LifeToolbox` **12 wyzwalaczy**: BMR, TDEE, makro, tętno max + strefy, WHtR, WHR, 1RM, tempo, kroki, woda, cykle snu, plan wagi — każde z jawnym „to nie porada medyczna” | dzienniczek posiłków/treningów, integracja z wagą/smartwatchem, wykresy postępów |
| **18. Smart home** | 1251–1300 | **BRAK** | — | wszystko (MQTT, Home Assistant, sceny domowe) — wymaga sieci i sprzętu |
| **19. GUI** | 1301–1400 | **CZĘŚCIOWO (dużo)** | motyw 0.93+ (tokeny, jasny/ciemny), **13 stron**, Centrum z 9 zakładkami, paleta `//` (73 wpisy), panel gotowości, nakładka metryk, **Studio 0.97** (6 kategorii, 70 poleceń, wyniki, historia) | wykresy (histogram/trend jako obraz), kreator motywów, edytor układu pulpitu, ulubione polecenia, skróty per kategoria |
| **20. Agentic** | 1401–1500 | **CZĘŚCIOWO (mniej niż połowa)** | ActionEngine + zgody wg ryzyka + **dowody wykonania** (VERIFIED tylko z dowodem), planowanie grafu zadań, nauka wzorców i lekcji, auto-naprawa literówek, „pytanie w szarej strefie” | planowanie celów wieloetapowych przez model, autonomia z budżetem, samoocena i wycofywanie zmian, orkiestracja wielu narzędzi w jednym zadaniu |
| **Bonus** | 1501–1550 | **CZĘŚCIOWO** | dokumentacja (RELEASE/BACKLOG/MEMORY), **21 zestawów regresji**, bramki CI (architektura, .NET 10, brak WinForms), tryby offline, uczciwe granice i „czego nie robi” | skrypty instalacyjne poza EXE, telemetria jakości (świadomie brak), marketplace dodatków |

## Liczby z tego audytu

- **Wyzwalacze poleceń w kodzie:** 839 w samych modułach obsługi (ponad 1000 razem z rdzeniem,
  Jarvisem 0.96 i paletą); rozumienie języka: **490 fraz**; paleta: **73 wpisy**; testy: **21 zestawów**.
- **Sekcje zrobione w całości:** 2 (§1 rdzeń, §15 analiza danych) + §17 zdrowie jako arytmetyka.
- **Sekcje częściowe:** 14 (§2, §3, §4, §7, §8, §9, §10, §11, §13, §14, §16, §19, §20, bonus).
- **Sekcje bez implementacji:** 4 (§5 architektura, §6 full-stack, §12 scraping, §18 smart home).
- **Szacunek pokrycia listy:** ~1/3 pozycji.

## Czego potwierdzenie znalazłem w CI (a nie w deklaracji)

- `--ui-smoke`: 21 zestawów, render **13 stron** bez ani jednego błędu wiązania, realne uruchomienia
  narzędzi (BMR → „1780 kcal”, statystyki → „mediana: 4,5”), test Studia i prywatności.
- `--self-test`: 0 niepowodzeń (raport w artefaktach: `self-test.txt`, `ui-smoke.txt`).
- **EXE/instalator/paczka: nie budowane.** W każdym przebiegu kroki `Publish portable Windows application`,
  `Portable package checksums`, `Verify portable package`, `Build per-user EXE installer`,
  `Install EXE and smoke-test installed application` oraz `upload-artifact` = **skipped**
  (bramka `if: github.event_name == 'workflow_dispatch'`). Ostatni przebieg: `36322692531`.

## Co dalej (kolejność, jeśli nie wskażesz inaczej)

1. Dokończyć §2 (pobieranie/usuwanie modeli, panel modeli, dobór do zadania) — bez tego sekcja jest w połowie.
2. §5 architektura i §20 agentic (planowanie celów, budżety, wycofywanie zmian).
3. §8 pamięć/wiedza (embeddingi lokalne, import dokumentów) i §13 produktywność (kalendarz, Pomodoro).
4. §19 UI: wykresy jako obraz, eksport wyniku do pliku, ulubione polecenia w Studiu.
