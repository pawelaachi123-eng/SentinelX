# Zewnętrzne blokery stabilnego wydania

1. Brak fizycznego ESP32/Base z wdrożonym transportem TLS i OpCode 201. Dostarczony master 4.2.1 zostawia transport jako TODO; binary compatibility i fixture nie dowodzą działania sprzętu.
2. Brak wdrożonego relayu/VPN endpointu i próby Wi-Fi → LTE. Nie ma domyślnego publicznego relayu.
3. Brak zewnętrznego produkcyjnego klucza Androida. Stary P12/hasła były w repo i nie są używane do nowych produkcyjnych buildów. Debug APK jest instalowalnym buildem weryfikacyjnym.
4. Prawdziwy mikrofon PL, tray/logowanie/autostart, WoL i Android hardware muszą zostać sprawdzone na sprzęcie właściciela. Windows CI smoke nie jest tym samym testem.

Nie generujemy update manifestu — to świadome ograniczenie zakresu użytkownika, nie błąd buildu.
