# Mapa listy „1550 toolsów prywatnego Jarvisa” — co z tego ma SentinelX

Stan: **0.97 · JARVIS** (2026-09-27). Skrót w aplikacji: `mapa funkcji`.

Lista, którą dostałem, jest **wizją ekosystemu Linux + Python + Ollama + Docker**. SentinelX to
jedna aplikacja C# / WPF na Windows, bez chmury i bez instalowania cudzych runtime'ów. Zamiast
udawać 1550 ikonek, każdy blok dostaje status i powód. Zasada jest niezmienna od 0.86:

> Funkcja, której nie potrafię zrobić **uczciwie**, dostaje odmowę z wyjaśnieniem, a nie atrapę.

Statusy: **TAK** (działa) · **CZĘŚCIOWO** (działa część, z wymienionymi brakami) ·
**NIE — ZASADA** (świadomie odrzucone, bo narusza zasady projektu) · **NIE — POZA CEL**
(poza celem aplikacji na pulpit Windows).

| Blok | Zakres | Liczba | Status | Dlaczego |
|---|---|---:|---|---|
| Rdzeń systemu | 001–045 | 45 | CZĘŚCIOWO | Zrobiłem to, co ma sens w jednej aplikacji: harmonogram (007), watchdog folderów (008), dziennik JSON z rotacją (010), kopie z kompresją i rotacją (020), sumy SHA-256 (022), zasoby CPU/RAM/GPU w czasie rzeczywistym (013), tray (037), CLI `--self-test` / `--ui-smoke` (038), flagi i kontener DI (041/042). Reszta (RocksDB 015, MessagePack 036, hot-reload pluginów 002, sandbox procesów 030) to infrastruktura serwerowa, która tu tylko spowalnia start. |
| Ollama i modele | 046–120 | 75 | W DUŻEJ CZĘŚCI | Masz menedżer modeli, inwentarz, wybór modelu do zadania, kaskadę i fallback (117–120), licznik tokenów, okno kontekstu, strumieniowanie, parametry, szablony promptów i Modelfile (046–081). Brak: trening LoRA/QLoRA (106–110), mergekit (112), konwersja safetensors→GGUF (114), kwantyzacja (113) — to godziny pracy GPU i obcy toolchain (Python/PyTorch), nie funkcja asystenta. |
| Prywatność i bezpieczeństwo | 121–185 | 65 | CZĘŚCIOWO | Masz podstawy: zero wysyłania danych (121), tryb offline (122), instalacja lokalna, generator haseł (142), **analiza siły hasła (143)**, **bezpieczny schowek z auto-czyszczeniem (145)**, szyfrowane kopie (167), retencję danych (174), TOTP (127), audyt akcji (032/170). Nie zrobię narzędzi ofensywnych ani teatru: Tor (136), skaner portów (140), IDS (138), randomizacja MAC (153), anty-keylogger (146), rootkit scanner (148) — bez uprawnień jądra to atrapy, a część służy atakowi, nie obronie. |
| Kodowanie („Claude level”) | 186–340 | 155 | NIE — ZASADA | Sentinel **nie modyfikuje własnego kodu**. Naprawia i ulepsza swoje DANE (0.95) i potrafi zbudować oddzielny, nowy program z szablonu (0.91). Asystent, który przepisuje sam siebie w miejscu, w którym działa, to najszybsza droga do aplikacji, której nie da się uruchomić. |
| Architektura i diagramy | 341–420 | 80 | NIE — POZA CEL | C4, ADR, STRIDE, DDD — narzędzia dla zespołów projektowych. Wyjątek: Mermaid i Markdown w oknie rozmowy (to element czatu). |
| Full-stack i multi-language | 421–590 | 170 | NIE — POZA CEL | Generatory projektów Next/Django/Rust/Go/CI wymagają cudzych toolchainów i sieci. SentinelX jest lokalny i nie instaluje npm/cargo/poetry po cichu. |
| Pamięć i wiedza | 591–670 | 80 | W DUŻEJ CZĘŚCI | Masz pamięć długoterminową, prywatną, archiwum rozmów, notatki z supersede, fakty sesji, wzorce uczenia (0.95), wyszukiwanie, streszczenia, kontekst projektu i bazę decyzji. Brak wektorów (Chroma/FAISS/Qdrant) i embeddingów — wymagają modelu w Ollama i setek MB; jeśli go zainstalujesz, wyszukiwanie semantyczne to sensowny następny krok. |
| Głos i mowa | 671–730 | 60 | W DUŻEJ CZĘŚCI | Wake word, Whisper.cpp lokalnie, VAD, komendy głosowe, TTS z polskim głosem Windows, dyktowanie, kalibracja mikrofonu. **Nowe w 0.97: wyciszanie tła przy czytaniu odpowiedzi (696).** Brak: klonowanie głosu (688), TTS emocjonalny (691), tłumaczenie mowy na mowę (723) — modele wielkości gigabajtów. |
| Wizja i ekran | 731–800 | 70 | CZĘŚCIOWO | Zrzuty ekranu, analiza obrazu w czacie, elementy i tekst na ekranie, nagrywanie, kamera. Brak: OCR (731), YOLO (738), MediaPipe (739/742), Stable Diffusion (775), Real-ESRGAN (757) — natywne biblioteki albo modele wielkości GB. |
| Automatyzacja pulpitu | 801–880 | 80 | W DUŻEJ CZĘŚCI | 0.96 dało okna, multimedia, zasilanie; **0.97 dokłada pulpity wirtualne (804), harmonogram (007) i watchdog folderów (008)**. Plus uruchamianie aplikacji, pliki, czyszczenie, skróty, diagnostyka. Brak: jasność (823), Bluetooth (827), Wi-Fi (828) — wymagają sterowników i uprawnień, których aplikacja nie dostaje. |
| Web scraping i research | 881–940 | 60 | NIE — ZASADA | Scrapowanie, SERP, headless przeglądarka i masowe pobieranie treści to łamanie regulaminów albo sieć, której Sentinel nie używa do zbierania danych. Pojedyncze, jawne zapytanie do lokalnej wyszukiwarki jest do rozważenia — automaty nie. |
| Produktywność | 941–1020 | 80 | W DUŻEJ CZĘŚCI | Zadania, projekty, przypomnienia, pomodoro/przerwy, rutyny, **eksport kalendarza .ics (1002)**, kalkulator, jednostki, hashe, base64, QR, daty, minutnik, notatki, szablony, eksport rozmów i PDF. Brak: blokady stron (wymaga uprawnień administratora i edycji hosts) i pełnego arkusza. |
| Komunikacja | 1021–1070 | 50 | NIE — POZA CEL | Poczta, Matrix/XMPP/IRC, boty Telegram/Discord — protokoły i logowanie do obcych usług. Sentinel nie trzyma Twoich haseł do poczty. |
| Data science | 1071–1140 | 70 | CZĘŚCIOWO | **0.97 dodało profilowanie CSV/TSV (1071/1075)**: wiersze, kolumny, braki, duplikaty, zakresy, średnie. Modele (1105–1117), Optuna, SHAP i wykresy naukowe wymagają Pythona. |
| Finanse | 1141–1200 | 60 | CZĘŚCIOWO | **0.97: inflacja (1193), cel oszczędzania (1146), spłata długu — kula śnieżna vs lawina (1152), ROI (1191), próg rentowności (1190), amortyzacja (1189)** + wcześniejsze: odsetki, kredyt, VAT, rabat, napiwek. Nie zrobię parsowania wyciągów bankowych ani prognoz — bez dostępu do kont to zgadywanie. |
| Zdrowie | 1201–1250 | 50 | CZĘŚCIOWO | **0.97: BMR i TDEE (1217), makro (1202), nawodnienie (1203), strefy tętna (1218), cykle snu (1227)**. Nie jestem aplikacją medyczną: nie diagnozuję i nie interpretuję wyników badań. |
| Smart home | 1251–1300 | 50 | NIE — POZA CEL | Hue, Zigbee, Z-Wave, MQTT, Home Assistant wymagają mostka w Twojej sieci. Masz HA? Mogę rozmawiać po REST, ale to konfiguracja po Twojej stronie. |
| GUI i interfejs | 1301–1400 | 100 | W DUŻEJ CZĘŚCI | Czat, Centrum (12 widoków), paleta Ctrl+K i `//`, motywy, tray, nakładka HUD, przypinanie, karty, historia akcji, eksport. Nie zrobię dockowalnych paneli, multi-tabów ani edytora kodu w aplikacji. |
| Agentic AI | 1401–1494 | 94 | CZĘŚCIOWO | Masz bezpieczną część: router intencji, kaskada modeli, narzędzia z katalogu, rejestr akcji, potwierdzenia, okno zgody, STOP awaryjny. Nie zrobię agentów uruchamiających kod bez nadzoru — człowiek jest w pętli decyzji, zawsze. |
| Rozszerzenia | 1495–1550 | 56 | CZĘŚCIOWO | Masz architekturę serwisów, DI, CLI, eksport/import ustawień i danych. IDE, wtyczki przeglądarkowe, serwer gRPC i MQTT to osobne produkty. |

## Co z tego wynika w praktyce (0.97)

Realnie **dodałem** w tym przyroście: harmonogram, watchdog folderów, kompresję i rotację kopii,
spójność danych SHA-256, strukturalny dziennik JSON, pulpity wirtualne, bezpieczny schowek,
wyciszanie tła, siłę hasła, szukanie w treści plików, profil CSV, porównanie plików, eksport .ics
i 12 kalkulatorów (finanse + zdrowie). **Ulepszyłem**: kopie zapasowe (ZIP + rotacja), dziennik
błędów (kategorie + rotacja 3 kopii), schowek (auto-czyszczenie), odczyty i odpowiedzi głosowe.

Największe pozycje, których **świadomie nie ma**: samomodyfikacja kodu (186–340), scraping
(881–940), komunikacja (1021–1070), trening modeli (106–115) i smart home (1251–1300). To łącznie
~700 pozycji z listy i żadna z nich nie zniknie przez dopisanie ładnego napisu.
