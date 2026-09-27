# AUDYT 1550 — część automatyczna

Wygenerowane przez `python3 scripts/audit-1550.py`. Liczby pochodzą z kodu:
wyzwalacze poleceń (`Starts`, `text is`, `case`, `StartsWith`) oraz wzorce `Regex @"^…"`.

| Sekcja | Pozycje | Moduł | Wyzwalacze poleceń | Wzorce |
|---|---|---|---|---|
| 1. Rdzeń | 1–45 | `Core/Runtime/RuntimeCommands.cs` | 86 | 0 |
| 2. Modele lokalne (Ollama) | 46–120 | `ModelToolbox.cs` | 23 | 10 |
| 3. Prywatność i dane | 121–185 | `PrivacyToolbox.cs` | 30 | 3 |
| 4. Kodowanie | 186–340 | `DeveloperToolbox.cs` | 12 | 39 |
| 4. Kodowanie (analiza kodu) | 186–340 | `CodeInsightsService.cs` | 3 | 14 |
| 4. Kodowanie (tekst, hasła, liczby) | 186–340 | `UtilityToolbox.cs` | 92 | 87 |
| 13. Produktywność i finanse | 941–1020 | `ProductivityToolbox.cs` | 0 | 12 |
| 14. Komunikacja + zdrowie | 1021–1070, 1201–1250 | `LifeToolbox.cs` | 30 | 1 |
| 15. Analiza danych | 1071–1140 | `AnalysisToolbox.cs` | 48 | 0 |
| 19. Sterowanie systemem (0.96) | 1301–1400 (część) | `JarvisToolkit.cs` | 110 | 4 |
| 11. Automatyzacja (rutyny) | 801–880 (część) | `RoutineCommands.cs` | 0 | 4 |
| 5. Architektura | 341–420 | — | 0 (brak modułu — tylko graf workflow/DAG w rdzeniu) | 0 |
| 6. Full-stack | 421–520 | — | 0 (brak modułu) | 0 |
| 12. Scraping i research | 881–940 | — | 0 (brak modułu — świadomie bez sieci) | 0 |
| 18. Smart home | 1251–1300 | — | 0 (brak modułu) | 0 |
| 20. Agentic | 1401–1500 | — | 0 (częściowo w ActionEngine/permissions — bez własnego modułu poleceń) | 0 |

**Suma wyzwalaczy w modułach obsługi:** 608 · **paleta `//`:** 76 wpisów · **rozumienie języka:** 503 fraz

Werdykt (ludzki, z listą braków per sekcja): [AUDYT-1550.md](AUDYT-1550.md).

