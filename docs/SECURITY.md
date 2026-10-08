# Bezpieczeństwo

Sekrety Windows: DPAPI CurrentUser; Android: AES-256-GCM, losowy IV i AAD nazwy wpisu, klucz nieeksportowalny Android Keystore. Legacy token migruje i jest usuwany ze zwykłych preferences. Natywny WebView korzysta z Keystore, nie duplikuje tokena w localStorage.
Certyfikat Base jest pinned i musi być ważny; nie ma publicznego nieuwierzytelnionego endpointu. LAN discovery jest tylko listą kandydatów. Role/granty Base muszą pochodzić z provisionowania urządzenia.
Allowlista: lock, sleep/restart/shutdown ze zgodą, Sentinel show, notification, Ollama ensure, Steam run/install przez kanoniczny uint AppID. Brak arbitralnego shell. Stałe lokalne programy i argumenty nie są komendą otrzymaną z sieci.
Usunięcie/re-pair/revocation anuluje starą kolejkę. Audit zapisuje czas, request, device, operację, cel i wynik; treść notification jest redagowana. Limity ramki, czasu, nonce/rate i liczby zadań chronią zasoby.
P12 z dawnej wersji usunięty. Klucz nadal może występować w historii; nie wolno ufać mu jako sekretowi produkcyjnemu. Nowy klucz produkcyjny dostarcza właściciel poza repo. Nie zmieniamy historii Git.
Updater: zewnętrzny opis o ścisłych polach, RSA-PSS SHA-256 (domyślnie wymagany), hash całego ZIP, semver, x64, brak downgrade/traversal/symlink i limity. Publiczny klucz trzeba uzyskać z zaufanego źródła. Wyłączenie wymagania podpisu pozostawia obowiązkowy SHA-256.
Nie ma analityki zewnętrznej, haseł Windows/Steam, nagrywania mikrofonu domyślnie ani root Androida.
