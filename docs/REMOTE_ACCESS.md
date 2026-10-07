# LAN i poza domem

LAN działa bez Internetu po provisionowaniu TLS/sekretów. PC i telefon łączą się wychodząco do skonfigurowanej Base. Tryby lan/vpn/relay korzystają z jednego uwierzytelnionego transportu TLS i pinu.
VPN: podaj prywatny adres Base osiągalny przez własny tunel; fingerprint i sekret pozostają wymagane. Relay: podaj własny kompatybilny TLS endpoint; nie istnieje domyślny publiczny relay.
Nie ustawiaj przekierowania portu Base jako domyślnej konfiguracji. Dostarczony master firmware nie implementuje jeszcze pełnego transportu TLS/relay. Brak wdrożenia i rzeczywistej próby LTE jest BLOCKED.
Zmiana Wi-Fi/LTE zamyka stare połączenie telefonu i ponownie uwierzytelnia. Po stop Activity transport jest zamknięty. Odczyty mogą wrócić; komendy nie są automatycznie ponawiane.
