# Architektura Build 1.0

Windows: istniejący shell MVVM + BaseViewModel + MaintenanceViewModel. Serwisy oddzielają UI od operacji systemowych.
WindowsBaseService utrzymuje TLS, heartbeat co 15 s, metryki/MAC, polling i osobny worker kolejki. Mikrofon nie kontroluje tych pętli. Granty i allowlista są sprawdzane lokalnie.
AgentQueue zapisuje stan atomowo przez DPAPI CurrentUser, nie powtarza running po restarcie; zadania wygasają do 5 min, wymagają odblokowanej sesji i lokalnej zgody na zasilanie/instalację.
Android: panel PC pozostaje dostępny. BaseActivity jest niezależny od PC: discovery, pinned TLS, challenge, capabilities, status, urządzenia, sceny, WoL, zadania i timery. Sekrety i token PC są szyfrowane kluczem AES-GCM z Keystore.
Niezależny Agent PC (`--agent`, graf `BuildAgent`) jest właścicielem Base i kolejki; UI łączy się przez uwierzytelniony potok (`IBaseControl`: serwis albo proxy). Heartbeat do pliku co 15 s (60 s w grze), metryki, log, diagnostyka i zwalnianie modeli Ollamy po bezczynności.
VerifiedUpdater konsumuje zewnętrzny opis lub jawne URL-e, weryfikuje pakiet, staging, inventory, smoke i zapisuje wskaźnik aktywnej/ostatniej dobrej wersji. Wersję potwierdza się realnymi kontrolami po 60 s; trzy niezdrowe starty w 5 min powodują rollback. Ekran odzyskiwania (MVVM) i dziennik zwykłych startów (`RunHealth`) domykają pętlę awarii.
Powłoka czyta widoczne napisy z `Resources/Strings` (EN neutral + satelita pl-PL); reszta komunikatów pozostaje po polsku.
OllamaSupervisor pilnuje jednej lokalnej instancji, nie ładuje modelu z góry, ładuje wskazany model na żądanie i zwalnia nieużywane po 30 min. Dotychczasowy silnik llama.cpp pozostaje w paczce.
Base/relay są niezależnymi urządzeniami. Brak fizycznej Base i wdrożonego relayu jest jawnie opisany w BLOCKERS.md.
