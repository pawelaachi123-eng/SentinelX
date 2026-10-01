# Sentinel X Telefon (Android)

Cienka aplikacja Android (Java, **bez bibliotek zewnętrznych**, minSdk 24, targetSdk 35). Cały interfejs to strona, którą serwuje
komputer (`Phone/web`); aplikacja dokłada to, czego przeglądarka nie potrafi: sama znajduje komputer w sieci, przypina jego certyfikat,
pokazuje alerty jako powiadomienia, rozpoznaje mowę po polsku i może wybudzić komputer. Protokół: [../docs/PHONE-LINK.md](../docs/PHONE-LINK.md).

## Jak dostać APK

- **Z wydania na GitHubie:** plik `SentinelX-Phone-<wersja>.apk` (budowany przez workflow *Release*, job `android`). Zainstaluj, zezwalając na instalację z nieznanego źródła.
- **Z Android Studio:** *Open* → folder `phone-android` → *Build → Build APK*. Gradle pobierze wtyczkę Androida (AGP 8.7.3) z Google Maven.
- **Z wiersza poleceń** (JDK 17, Android SDK, Gradle 8.9): `cd phone-android && gradle assembleRelease` → `app/build/outputs/apk/release/app-release.apk`.

## Podpis

`app/sentinelx-phone.p12` (hasło `sentinelx`) to stały klucz, dzięki któremu kolejne wersje instalują się na poprzednie bez odinstalowywania.
To nie jest klucz do sklepu Play; repozytorium jest prywatne. Workflow sprawdza klucz `keytool`-em i tylko gdy nie da się go odczytać, tworzy nowy (wtedy raz trzeba odinstalować starą aplikację).

## Pliki

| Plik | Rola |
| --- | --- |
| `MainActivity` | WebView z przypiętym certyfikatem, ekran statusu (szukanie, brak komputera, adres ręczny, wybudzanie), most `SXNative`, krawędzie ekranu i klawiatura |
| `PcLocator` | wykrywanie komputera: broadcast UDP `SXLINK1?` na porcie 43181 i sprawdzenie `/api/hello` |
| `PinnedTls` | HTTPS bez urzędu certyfikacji: ufa wyłącznie certyfikatowi o zapamiętanym odcisku SHA-256 |
| `Session` | co aplikacja pamięta: komputer, odcisk, token, MAC, ostatni alert |
| `AlertJobService`, `Notifier` | co ~15 min pyta o alerty i pokazuje powiadomienia (`JobScheduler`, bez usługi na pierwszym planie) |
| `WakeOnLan` | pakiet „magic packet” (UDP 9) na adresy rozgłoszeniowe sieci |

## Sprawdzone i niesprawdzone

Kod nie był kompilowany w środowisku, w którym powstał (brak Android SDK); składnię sprawdzono parserem Javy. Pierwszy build wykona CI. Na prawdziwym telefonie trzeba jeszcze sprawdzić: wykrywanie, parowanie, powiadomienia (Android 13+ pyta o zgodę), Wake-on-LAN.
