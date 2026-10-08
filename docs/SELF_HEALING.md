# Diagnostyka, naprawa i aktualizacje

Panel Diagnostyka/aktualizacje sprawdza autostart, sieć, gateway/DNS, dysk, katalog ustawień, stan Base/SX4, DPAPI, relay/VPN, Steam, Ollama, logi i licznik crash-loop. Internet: opcjonalny wskazany przez użytkownika HTTPS HEAD, timeout 3 s; bez adresu SKIPPED. Stan Androida jest dostępny przez devices w Base. Diagnostyka nie wysyła raportów.
Naprawa kolejki: zatrzymanie workera, detect → exact backup → repair → decrypt/readback verify → result. Uszkodzony plik nie jest kasowany po cichu. Naprawa katalogów zachowuje dane użytkownika; autostart zapisuje i odczytuje wpis HKCU.
Aktualizacja: wybierz portable ZIP i **istniejący opis dostarczony przez wydawcę**. Pola opisu: Version, Architecture=win-x64, Sha256, Size, opcjonalne Signature. To wejście konsumenta; żaden build/release nie generuje manifestu aktualizacji zgodnie z decyzją użytkownika.
Podpis: RSA-PSS SHA-256 nad UTF-8: SXUP1 + newline, Version + newline, Architecture + newline, Sha256 lowercase + newline, Size + newline.
Po hash/signature/space/ZIP/path/x64/product-version checks pakiet trafia do staging. Dokładny kandydat wykonuje --ui-smoke; brak sukcesu nie zmienia aktywnej wersji. Inventory wszystkich plików sprawdza także rollback.
Następny start przekierowuje do aktywnej wersji. Po 20 sekundach zdrowego działania licznik się zeruje. Trzy starty bez zdrowia w 5 minut cofają do last-known-good. Ręczny rollback w panelu również weryfikuje inventory.
