# Sentinel X Telefon (Android)

Cienka aplikacja Android (Java, **bez bibliotek zewnętrznych**, minSdk 24, targetSdk 35). Cały interfejs to strona, którą serwuje
komputer (`Phone/web`); aplikacja dokłada to, czego przeglądarka nie potrafi: sama znajduje komputer w sieci, przypina jego certyfikat,
pokazuje alerty jako powiadomienia, rozpoznaje mowę po polsku i może wybudzić komputer. Protokół: [../docs/PHONE-LINK.md](../docs/PHONE-LINK.md).

## Jak dostać APK

- **Z wydania na GitHubie:** plik `SentinelX-Phone-<wersja>.apk` (budowany przez workflow *Release*, job `android`). Zainstaluj, zezwalając na instalację z nieznanego źródła.
- **Z Android Studio:** *Open* → folder `phone-android` → *Build → Build APK*. Gradle pobierze wtyczkę Androida (AGP 8.7.3) z Google Maven.
- **Z wiersza poleceń** (JDK 17, Android SDK, Gradle 8.9): `cd phone-android && gradle assembleRelease` → `app/build/outputs/apk/release/app-release.apk`.

## Podpis

Klucza podpisu ani hasła nie przechowujemy w repozytorium. CI używa chronionych sekretów `SENTINELX_ANDROID_KEYSTORE_BASE64`, `SENTINELX_ANDROID_STORE_PASSWORD`, `SENTINELX_ANDROID_KEY_ALIAS` i `SENTINELX_ANDROID_KEY_PASSWORD`, jeśli są skonfigurowane. W przeciwnym razie workflow tworzy jednorazowy klucz wyłącznie na czas zadania, podpisuje nim APK i zgłasza ostrzeżenie; taki APK można zainstalować, ale aktualizacja wymaga odinstalowania poprzedniej wersji.

Poprzedni klucz był przechowywany w kodzie z hasłem zapisanym w pliku Gradle, więc został usunięty z bieżącego drzewa i nie jest już używany. Traktuj go jako ujawniony: usunięcie pliku nie usuwa go z wcześniejszych commitów ani kopii klonów. Android nie ma mechanizmu unieważniania starego certyfikatu podpisu; starsze instalacje wymagają jednorazowego odinstalowania przed instalacją APK z nowym kluczem. Lokalne `assembleRelease` bez zmiennych podpisu produkuje APK niepodpisany; do lokalnego testu użyj wariantu debug, a gotowy podpisany APK pobierz z artefaktów workflow CI.

## Pliki

| Plik | Rola |
| --- | --- |
| `MainActivity` | WebView z przypiętym certyfikatem, ekran statusu (szukanie, brak komputera, adres ręczny, wybudzanie), most `SXNative`, krawędzie ekranu i klawiatura |
| `PcLocator` | wykrywanie komputera: broadcast UDP `SXLINK1?` na porcie 43181 i sprawdzenie `/api/hello` |
| `PinnedTls` | HTTPS bez urzędu certyfikacji: ufa wyłącznie certyfikatowi o zapamiętanym odcisku SHA-256 |
| `Session` | komputer, odcisk, token, MAC, ostatni alert; token AES-GCM w Android Keystore z migracją wcześniejszej preferencji |
| `AlertJobService`, `Notifier` | co ~15 min pyta o alerty i pokazuje powiadomienia (`JobScheduler`, bez usługi na pierwszym planie) |
| `WakeOnLan` | pakiet „magic packet” (UDP 9) na adresy rozgłoszeniowe sieci |

## Sprawdzone i niesprawdzone

Kod Androida nie był kompilowany lokalnie w tym środowisku (brak Android SDK/JDK); ostatnie poprawki połączenia i sesji nie mają jeszcze wyniku kompilatora. Po pushu workflow CI uruchamia Gradle, weryfikuje podpis APK (`apksigner`) i zachowuje artefakt przez 7 dni. Na prawdziwym telefonie trzeba jeszcze sprawdzić: wykrywanie, parowanie, powiadomienia (Android 13+ pyta o zgodę), Wake-on-LAN.
