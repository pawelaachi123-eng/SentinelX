using System.Text;

namespace SentinelX.Core;

/// <summary>0.97 · uczciwa mapa słynnej listy „1550 toolsów prywatnego Jarvisa”.
/// <para>Lista jest wizją z ekosystemu Linux + Python + Ollama. SentinelX to C# / WPF / Windows i
/// żadnego z tych faktów nie przeskoczę: nie udaję, że mam narzędzia, których nie mam, bo
/// najgorsze, co może zrobić asystent, to skłamać o własnych możliwościach. Dlatego każdy blok
/// ma status i powód, a pełna tabela jest w docs/FEATURE-MAP-1550.md.</para></summary>
public static class FeatureMap
{
    public sealed record Block(string Range, string Name, int Count, string Status, string Reason);

    /// <summary>Bloki z podsumowania listy 1550 z uczciwym statusem dla Sentinela.</summary>
    public static IReadOnlyList<Block> Blocks { get; } =
    [
        new("001–045", "Rdzeń systemu", 45, "CZĘŚCIOWO",
            "To, co ma sens w jednej aplikacji desktopowej: harmonogram (007), watchdog folderów (008), dziennik JSON z rotacją (010), kopie z rotacją i kompresją (020), sumy SHA-256 (022), zasoby CPU/RAM/GPU w czasie rzeczywistym (013), tray (037), CLI --self-test/--ui-smoke (038), flagi i kontener DI (041/042). Reszta (RocksDB, MessagePack, hot-reload pluginów, osobny sandbox procesów) to infra serwerowa — w aplikacji, która ma działać u Ciebie na pulpicie, tylko by ją spowolniła."),
        new("046–120", "Ollama i modele", 75, "W DUŻEJ CZĘŚCI",
            "Masz to, co widać na co dzień: menedżer modeli, inwentarz, wybór modelu do zadania, kaskada i fallback (117–120), licznik tokenów, okno kontekstu, strumieniowanie, parametry, szablony promptów i Modelfile (046–081). Nie mam i nie będę miał: treningu LoRA/QLoRA, mergekit, konwersji safetensors→GGUF ani kwantyzacji — to godziny pracy GPU i obce narzędzia (Python/PyTorch), nie funkcja asystenta."),
        new("121–185", "Prywatność i bezpieczeństwo", 65, "CZĘŚCIOWO",
            "Masz twarde podstawy: zero wysyłania danych (121), tryb offline (122), instalacja lokalna bez chmury, generator haseł, analizę siły hasła (142/143), bezpieczny schowek z auto-czyszczeniem (145), szyfrowane kopie (167), retencję danych (174), TOTP (127), audyt akcji (032/170). Nie zrobię narzędzi ofensywnych ani teatru bezpieczeństwa: Tor (136), skaner portów (140), IDS (138), randomizacja MAC (153), anty-keylogger (146), rootkit scanner (148) — bez uprawnień kernela to byłyby tylko ładne napisy, a część z nich służy do ataku, nie obrony."),
        new("186–340", "Kodowanie (Claude level)", 155, "NIE — ZASADA",
            "Ten projekt ma jawną zasadę: Sentinel nie modyfikuje własnego kodu. Może naprawiać i ulepszać swoje DANE (0.95) oraz budować oddzielne, nowe programy z szablonu (0.91: notatnik, kalkulator, pomodoro) — ale nie przepisuje samego siebie. Asystent, który sam siebie poprawia w miejscu, w którym działa, to najszybsza droga do aplikacji, której nie da się uruchomić. Zamiast tego: uczciwa odmowa z wyjaśnieniem."),
        new("341–420", "Architektura i diagramy", 80, "NIE — POZA CEL",
            "Generatory diagramów, C4, ADR, STRIDE, DDD — to narzędzia dla zespołów projektowych, nie dla asystenta na pulpicie. Wyjątek: diagramy Mermaid rendering i Markdown w oknie rozmowy (masz), bo to element czatu, nie produkt."),
        new("421–590", "Full-stack i multi-language", 170, "NIE — POZA CEL",
            "Generatory projektów Next.js/Django/Rust/Go/CI-CD — to scaffolders z internetu (npm, cargo, poetry), każdy wymaga własnego toolchainu i sieci. SentinelX jest lokalny i nie instaluje toolchainów. Do budowania programów masz własny, lokalny szablon („zbuduj program notatnik”)."),
        new("591–670", "Pamięć i wiedza", 80, "W DUŻEJ CZĘŚCI",
            "Masz: pamięć długoterminową i prywatną, archiwum rozmów, notatki z supersede, fakty sesji, wyciągnięte wzorce uczenia (0.95), wyszukiwanie w historii, streszczenia, projektowy kontekst i bazę decyzji. Nie mam lokalnego wektora (Chroma/FAISS/Qdrant) ani embeddingów — wymagałyby modelu embeddingowego w Ollama i setek MB; jeśli go zainstalujesz, wyszukiwanie semantyczne to sensowny następny krok."),
        new("671–730", "Głos i mowa", 60, "W DUŻEJ CZĘŚCI",
            "Masz: wake word, Whisper.cpp lokalnie (671–679), VAD, komendy głosowe, TTS z polskim głosem Windows, tryb dyktowania, mikrofon z kalibracją i korekcją wzmocnienia. Brak: klonowania głosu (XTTS/Bark), emocjonalnego TTS i tłumaczenia mowy na mowę w czasie rzeczywistym — to modele, których nie da się dostarczyć z instalatorem (setki MB–GB)."),
        new("731–800", "Wizja i ekran", 70, "CZĘŚCIOWO",
            "Masz: zrzut ekranu (pełny/wybór/monitor), analizę obrazu w czacie, wykrywanie elementów i tekstu na ekranie, nagrywanie ekranu, kamerę. Brak: OCR (Tesseract/PaddleOCR), YOLO, MediaPipe, Stable Diffusion i Real-ESRGAN — każde z nich to natywne biblioteki albo modele wielkości gigabajtów. Wolę powiedzieć „nie mam”, niż udawać rozpoznawanie twarzy."),
        new("801–880", "Automatyzacja pulpitu", 80, "W DUŻEJ CZĘŚCI",
            "0.96 + 0.97 dały Ci większość: okna (przełączanie, minimalizacja, snap, pełny ekran), pulpity wirtualne (804), multimedia (play/pause, głośność), zasilanie (lock/sleep/shutdown/restart + anulowanie), harmonogram (007), watchdog folderów (008), menedżer plików, uruchamianie aplikacji, czyszczenie plików, skróty i makra, diagnostykę dysku i SMART przez własne odczyty. Brak: sterowania jasnością/Bluetoothem/Wi-Fi — wymaga sterowników i uprawnień, których aplikacja nie dostaje bez instalacji sterownika."),
        new("881–940", "Web scraping i research", 60, "NIE — ZASADA",
            "Scrapowanie stron, SERP, headless przeglądarka i masowe pobieranie treści to albo łamanie regulaminów serwisów, albo sieć, której Sentinel świadomie nie używa do zbierania danych. Lokalna wyszukiwarka (DuckDuckGo HTML) jest w planach jako jawne, pojedyncze zapytanie — bez automatów."),
        new("941–1020", "Produktywność", 80, "W DUŻEJ CZĘŚCI",
            "Masz: zadania z priorytetami i projektami, przypomnienia, pomodoro i przerwy, rutyny (sceny), eksport kalendarza .ics (1002), kalkulator, przelicznik jednostek i walut offline, hashe, base64, QR, daty, minutnik, notatki, szablony dokumentów, eksport rozmów i PDF. Brak: blokady stron (wymaga uprawnień administratora i edycji hosts) i pełnego arkusza kalkulacyjnego."),
        new("1021–1070", "Komunikacja", 50, "NIE — POZA CEL",
            "Klient poczty, Matrix/XMPP/IRC, boty Telegram/Discord — to aplikacje z własnym protokołem i logowaniem do obcych usług. Sentinel nie przechowuje Twoich haseł do poczty ani nie trzyma otwartych sesji w obcych sieciach."),
        new("1071–1140", "Data science", 70, "CZĘŚCIOWO",
            "0.97 dodało profilowanie CSV/TSV (1071/1075): liczba wierszy, kolumny, braki, duplikaty, zakresy, średnie. Trenowanie modeli (KL/DBSCAN/XGBoost), Optuna, SHAP i wykresy naukowe wymagają Pythona i jego stosu — nie będę go instalował po cichu."),
        new("1141–1200", "Finanse", 60, "CZĘŚCIOWO",
            "Masz kalkulatory, które nie potrzebują Twoich danych z banku: inflacja, cel oszczędzania, spłata długu (kula śnieżna vs lawina), ROI, próg rentowności, amortyzacja, odsetki, kredyt, napiwek, rabat, VAT. Nie zrobię parsowania wyciągów bankowych ani prognoz — bez dostępu do kont to byłoby zgadywanie."),
        new("1201–1250", "Zdrowie", 50, "CZĘŚCIOWO",
            "Dodałem w 0.97: BMR, TDEE, makro, nawodnienie, strefy tętna i cykle snu (1203/1217/1218/1227). Nie jestem aplikacją medyczną: nie diagnozuję, nie interpretuję wyników badań i nie prowadzę dziennika leków."),
        new("1251–1300", "Smart home", 50, "NIE — POZA CEL",
            "Hue, Zigbee, Z-Wave, MQTT, Home Assistant: każdy wymaga mostka w Twojej sieci i jego API. Jeśli masz Home Assistant, mogę z nim rozmawiać po REST — ale to konfiguracja po Twojej stronie, nie funkcja instalatora."),
        new("1301–1400", "GUI i interfejs", 100, "W DUŻEJ CZĘŚCI",
            "Masz: czat z historią, centrum sterowania z 12 widokami, paletę Ctrl+K i //, motywy (ciemny/jasny/cyberpunk), tray, nakładkę HUD, przypinanie rozmów, karty odpowiedzi, historię akcji, eksport. Nie zrobię dockowalnych paneli, multi-tabów ani edytora kodu w aplikacji — to koszt utrzymania bez wartości dla sterowania komputerem."),
        new("1401–1494", "Agentic AI", 94, "CZĘŚCIOWO",
            "Masz tę część, która jest bezpieczna: router intencji, kaskadę modeli, narzędzia z jawnego katalogu, rejestr akcji z potwierdzeniami, cofanie nieudanych akcji gdzie możliwe, okno zgody i STOP awaryjny. Nie zrobię samodzielnych agentów uruchamiających kod bez nadzoru: w tej aplikacji człowiek jest w pętli decyzji, zawsze."),
        new("1495–1550", "Rozszerzenia", 56, "CZĘŚCIOWO",
            "Masz: lokalną architekturę serwisów, kontener DI, CLI (--self-test, --ui-smoke), dziennik JSON i eksport/import ustawień oraz danych (backup). IDE, wtyczki do przeglądarki, serwer gRPC i MQTT to osobne produkty; tu świadomie stawiam granicę na Windows i na Twoim pulpicie."),
    ];

    public static string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine("MAPA LISTY 1550 · co Sentinel z tego ma, a czego nie i dlaczego (0.97)");
        builder.AppendLine();
        foreach (Block block in Blocks)
            builder.AppendLine("· " + block.Range + " " + block.Name + " (" + block.Count + ") → " + block.Status);
        builder.AppendLine();
        builder.AppendLine("Zasada jest jedna: funkcja, której nie potrafię zrobić uczciwie, dostaje odmowę z wyjaśnieniem, a nie atrapę.");
        builder.AppendLine("Szczegóły blok po bloku: docs/FEATURE-MAP-1550.md · skrót tej listy: „mapa funkcji”.");
        return builder.ToString().TrimEnd();
    }
}
