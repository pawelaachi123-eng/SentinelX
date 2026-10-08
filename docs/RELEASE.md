# Wydanie 1.0

Semver 1.0.0; Android versionCode 100. Kontrakt: SentinelX-1.0.0-win-x64-setup.exe, portable ZIP, SentinelX-1.0.0-android.apk, wspólne SHA256SUMS.txt. **Manifest aktualizacji pominięty na polecenie użytkownika.**
Publikator jest jeden, needs Windows + Android + security, waliduje wszystkie realne pliki razem. Brak pliku/hash/manifestu APK/runtime/x64/engine lub wynik testu != PASS blokuje publikację.
Ręczne wydanie verification/debug jest prerelease z tagiem zależnym od SHA i nie przesuwa latest. Stabilne v1.0.0 wymaga produkcyjnego podpisu APK i jawnego potwierdzenia sprzętowych testów; dopiero potem latest.
Windows EXE pozostaje bez Authenticode, gdy właściciel nie dostarczy certyfikatu. SHA-256 i testy nie oznaczają podpisu wydawcy.
Źródłem prawdy jest konkretny commit/run i załączone logi, nigdy sama nazwa wersji. Nie deklarować RELEASE READY bez wszystkich gates.
