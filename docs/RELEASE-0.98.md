# Sentinel X 0.98.0

Notatki wydania dla asystenta Windows i aplikacji towarzyszącej na Androida.

## Co się zmieniło

- Dodano lokalne centrum automatyzacji z wyzwalaczami ręcznymi, przy starcie aplikacji i o codziennej godzinie. Ograniczony katalog akcji może uruchomić rozpoznaną aplikację, otworzyć zweryfikowany adres HTTP/HTTPS albo wyświetlić powiadomienie. Dowolne polecenia powłoki, skrypty i ścieżki plików wykonywalnych są celowo niedostępne.
- Dodano trwałą historię automatyzacji z ograniczonym rozmiarem i rzeczywistymi wynikami wykonania.
- Wzmocniono parowanie i zarządzanie telefonem: trwałe wpisy urządzeń, odwoływanie dostępu, przechowywanie skrótu sekretu oraz ustawienia per urządzenie.
- Ulepszono obsługę katalogów danych Windows, walidację operacji plikowych, bezpieczeństwo pobierania silnika, odzyskiwanie po błędach startu i diagnostykę awarii.
- Dodano workflowy CI budujące i sprawdzające instalator Windows oraz APK Androida na gałęzi tej sesji. Zwykłe buildy gałęzi nie publikują wydania GitHub.
- Usunięto z bieżącego drzewa historyczny magazyn klucza Androida. Oficjalne wydanie wymaga nowego, trwałego klucza w chronionych sekretach; APK z builda gałęzi może używać klucza jednorazowego i przed instalacją kolejnego takiego buildu wymaga odinstalowania poprzedniej aplikacji.

## Pobieranie

Oficjalne pliki są dołączane do wydania GitHub oznaczonego tagiem `v0.98.0`, o ile przejdzie kontrola nowego, chronionego klucza APK:

- `SentinelX-0.98.0-win-x64-setup.exe` — instalator Windows x64 dla bieżącego użytkownika.
- `SentinelX-0.98.0-win-x64-portable.zip` — przenośna wersja Windows x64.
- `SentinelX-Phone-0.98.0.apk` — aplikacja na Androida 7+.
- `SHA256SUMS.txt` — sumy kontrolne plików wydania.

Pliki Windows nie są podpisane certyfikatem wydawcy, więc SmartScreen może pokazać ostrzeżenie „Nieznany wydawca”. Zachowaj klucz użyty do podpisania APK, jeśli mają działać aktualizacje Androida bez odinstalowania. Nie używaj historycznego klucza z dawnych commitów repozytorium.

## Znane ograniczenia

- Automatyzacje działają tylko wtedy, gdy Sentinel X jest uruchomiony. Wyzwalacze oparte na procesach, urządzeniach i warunkach systemowych nie są dostępne.
- Akcje automatyzacji są celowo ograniczone; nie można uruchamiać dowolnych skryptów, poleceń powłoki ani nieograniczonych procesów.
- APK z buildów gałęzi może być podpisany tymczasowym kluczem, gdy chronione sekrety wydania nie są skonfigurowane. Taki plik służy do testów CI i nie jest zamienny z APK podpisanym trwałym kluczem wydania.
- Temperatura GPU i FPS nie są gwarantowanymi metrykami; niedostępne wartości nie mogą być przedstawiane jako pomiary.
