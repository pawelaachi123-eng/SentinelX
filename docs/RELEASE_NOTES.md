Sentinel X Build 1.0 / 1.0.0

Dodano binarny SX4, pinned TLS, challenge/capabilities, szyfrowaną kolejkę PC Agenta z expiry/zgodami/audytem, heartbeat niezależny od mikrofonu i panel Base na Windows/Android. Zachowano dotychczasowy panel PC/PWA, głos, tray i silnik AI.
Dodano lokalny resolver Steam, nadzór Ollamy bez autoload modelu, diagnostykę/naprawę oraz konsumenta zweryfikowanych pakietów ze staging, health i rollback. CI łączy EXE/APK/security przed publikacją.
Manifest aktualizacji nie jest generowany na polecenie właściciela.
APK z wydania verification jest **debug-signed**, Windows jest bez Authenticode. Produkcja wymaga zewnętrznego klucza Androida. Pełna próba z ESP32/relay/LTE i sprzętem użytkownika pozostaje osobnym testem; stock V4.2.1 musi wdrożyć rozszerzenie 201.
