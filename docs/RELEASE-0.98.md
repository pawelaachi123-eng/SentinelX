# Sentinel X 0.98.0 — wydanie przedpremierowe

Notatki wydania dla asystenta Windows i aplikacji towarzyszącej na Androida.

## Co się zmieniło

- Dodano lokalne centrum automatyzacji z wyzwalaczami ręcznymi, przy starcie aplikacji i o codziennej godzinie. Ograniczony katalog akcji może uruchomić rozpoznaną aplikację, otworzyć zweryfikowany adres HTTP/HTTPS albo wyświetlić powiadomienie. Dowolne polecenia powłoki, skrypty i ścieżki plików wykonywalnych są celowo niedostępne.
- Dodano trwałą historię automatyzacji z ograniczonym rozmiarem i rzeczywistymi wynikami wykonania.
- Wzmocniono parowanie i zarządzanie telefonem: trwałe wpisy urządzeń, odwoływanie dostępu, przechowywanie skrótu sekretu oraz ustawienia per urządzenie.
- Ulepszono obsługę katalogów danych Windows, walidację operacji plikowych, bezpieczeństwo pobierania silnika, odzyskiwanie po błędach startu i diagnostykę awarii.
- Dodano workflowy CI budujące i sprawdzające instalator Windows oraz APK Androida. Oficjalny proces wydania nadal wymaga przejścia testów Windows i weryfikacji APK.
- Z repozytorium usunięto historyczny magazyn klucza Androida; nie jest używany.

## Pliki do pobrania

To wydanie `v0.98.0` jest przedpremierowe. Zawiera:

- `SentinelX-0.98.0-win-x64-setup.exe` — instalator Windows x64 dla bieżącego użytkownika.
- `SentinelX-0.98.0-win-x64-portable.zip` — przenośna wersja Windows x64.
- `SentinelX-Phone-0.98.0.apk` — aplikacja na Androida 7+.
- `BUILD.txt` i `SHA256SUMS.txt` — informacje o buildzie oraz sumy kontrolne.

Pliki Windows nie są podpisane certyfikatem wydawcy, więc SmartScreen może pokazać ostrzeżenie „Nieznany wydawca”.

## Ważne: jednorazowy podpis APK

Na wyraźne żądanie to wydanie używa świeżego, jednorazowego klucza utworzonego w GitHub Actions — bez trwałych sekretów i bez użycia historycznego klucza. CI weryfikuje podpis APK przed publikacją. Klucz jednorazowy nie pozwala zainstalować tej wersji jako aktualizacji APK podpisanego innym kluczem ani później aktualizować jej „na miejscu”.

Przed instalacją trzeba odinstalować poprzednią aplikację Sentinel X. **Odinstalowanie może usunąć jej lokalne dane** — najpierw zabezpiecz potrzebne informacje i przygotuj się na ponowne parowanie z komputerem. Kolejna wersja także będzie wymagała odinstalowania, dopóki nie zostanie skonfigurowany trwały klucz w chronionych sekretach GitHub Actions.

## Znane ograniczenia

- Automatyzacje działają tylko wtedy, gdy Sentinel X jest uruchomiony. Wyzwalacze oparte na procesach, urządzeniach i warunkach systemowych nie są dostępne.
- Akcje automatyzacji są celowo ograniczone; nie można uruchamiać dowolnych skryptów, poleceń powłoki ani nieograniczonych procesów.
- Temperatura GPU i FPS nie są gwarantowanymi metrykami; niedostępne wartości nie mogą być przedstawiane jako pomiary.
