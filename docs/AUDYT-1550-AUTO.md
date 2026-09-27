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
| 13. Produktywność i finanse | 941–1020 | `ProductivityToolbox.cs` | 9 | 12 |
| 14. Komunikacja + zdrowie | 1021–1070, 1201–1250 | `LifeToolbox.cs` | 30 | 1 |
| 15. Analiza danych | 1071–1140 | `AnalysisToolbox.cs` | 48 | 1 |
| 19. Sterowanie systemem (0.96) | 1301–1400 (część) | `JarvisToolkit.cs` | 110 | 4 |
| 11. Automatyzacja (rutyny) | 801–880 (część) | `RoutineCommands.cs` | 0 | 4 |
| 5. Architektura | 341–420 | `ArchitectureToolbox.cs` | 14 | 0 |
| 6. Full-stack (generatory offline) | 421–520 | `FullStackToolbox.cs` | 16 | 2 |
| 8. Wiedza i nauka | 591–670 | `KnowledgeToolbox.cs` | 9 | 0 |
| 12. Research (offline, bez sieci) | 881–940 | `ResearchToolbox.cs` | 12 | 0 |
| 18. Smart home (planowanie) | 1251–1300 | `SmartHomeToolbox.cs` | 10 | 1 |
| 20. Agentic (cele i ryzyko) | 1401–1500 | `GoalToolbox.cs` | 8 | 0 |
| 9-10. Głos i wizja (media) | 671–800 | `MediaVisionToolbox.cs` | 8 | 0 |
| 8. Indeks plików tekstowych (RAM) | 591–670 (dopełnienie) | `KnowledgeIndexService.cs` | 6 | 2 |
| 8. RAG (wektory z lokalnej Ollamy) | 591–670 (dopełnienie) | `KnowledgeRagService.cs` | 6 | 1 |
| 12. WiFi i szukanie w sieci | 881–940 (dopełnienie) | `WebAccessService.cs` | 5 | 0 |
| —. Game Dev (Roblox) — nowy obszar | poza 20 sekcjami listy | `GameDevToolbox.cs` | 12 | 6 |
| 20. Agentic (reszta: planowanie przez model, autonomia z budżetem) | 1401–1500 | — | 0 (GoalToolbox pokrywa szablony; modelowe planowanie celów nadal nie istnieje) | 0 |

**Suma wyzwalaczy w modułach obsługi:** 736 · **paleta `//`:** 90 wpisów · **rozumienie języka:** 625 fraz

Werdykt (ludzki, z listą braków per sekcja): [AUDYT-1550.md](AUDYT-1550.md).

