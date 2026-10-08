# Architektura Build 1.0

Windows: istniejący shell MVVM + BaseViewModel + MaintenanceViewModel. Serwisy oddzielają UI od operacji systemowych.
WindowsBaseService utrzymuje TLS, heartbeat co 15 s, metryki/MAC, polling i osobny worker kolejki. Mikrofon nie kontroluje tych pętli. Granty i allowlista są sprawdzane lokalnie.
AgentQueue zapisuje stan atomowo przez DPAPI CurrentUser, nie powtarza running po restarcie; zadania wygasają do 5 min, wymagają odblokowanej sesji i lokalnej zgody na zasilanie/instalację.
Android: panel PC pozostaje dostępny. BaseActivity jest niezależny od PC: discovery, pinned TLS, challenge, capabilities, status, urządzenia, sceny, WoL, zadania i timery. Sekrety i token PC są szyfrowane kluczem AES-GCM z Keystore.
VerifiedUpdater konsumuje zewnętrzny opis, weryfikuje pakiet, staging, inventory, smoke i zapisuje wskaźnik aktywnej/ostatniej dobrej wersji. Trzy niezdrowe starty w 5 min powodują rollback.
OllamaSupervisor pilnuje jednej lokalnej instancji i nie ładuje modelu. Dotychczasowy silnik llama.cpp pozostaje w paczce.
Base/relay są niezależnymi urządzeniami. Brak fizycznej Base i wdrożonego relayu jest jawnie opisany w BLOCKERS.md.
