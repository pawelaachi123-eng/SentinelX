# Aplikacja pomocnicza Sentinel Companion (Android)

Zwykły, mały klient mostu, dzięki któremu **Sentinel (PC) dzwoni z TWOJEJ karty SIM/eSIM** —
nie z obcego numeru VoIP. To narzędzie Sentinela, nie osobny asystent: bez połączenia z SentinelX
nic nie robi.

## Uczciwa tabela możliwości (przeczytaj — to ważne)

| Funkcja | Czy działa? | Dlaczego |
|---|---|---|
| Wybieranie numeru z **Twojej karty SIM** | ✅ | `Intent.ACTION_CALL` + uprawnienie `CALL_PHONE` — prawdziwe połączenie głosowe, Twój numer w rozmówcy |
| Stan połączenia (wybieranie / odebrane / zakończone) | ✅ | `TelephonyManager.CallStateListener` — `OFFHOOK` to sygnał systemu, na nim opiera się dowód „OFFHOOK” w SentinelX |
| Mowa Sentinela do rozmówcy | ✅ przez **głośnik** | PC liczy TTS → most → `AudioTrack` na głośniku telefonu |
| Słuch rozmówcy | ✅ przez **głośnik** | Mikrofon telefonu zbiera głos rozmówcy z głośnika (relacja). **Android 10+ NIE daje zwykłym aplikacjom dostępu do audio rozmowy SIM** — nie udajemy, że daje. Dlatego podczas rozmowy Sentinela telefon leży głośnikiem do góry, nie przy uchu. |
| SMS, nagrywanie rozmów | ❌ | Świadomie nieobsługiwane (prawo i polityka Google). Sentinel prowadzi tylko tekstowy transkrypt po stronie PC. |

## Budowanie

```
companion/android$ ./gradlew :app:assembleDebug   # lub otwórz folder w Android Studio
```
Wymagania: Android Studio (Koala lub nowsze), minSdk 28 (Android 9), compileSdk 35.

## Parowanie z SentinelX (jednorazowo)

1. Na PC: powiedz Sentinelowi **„włącz most telefoniczny”** — dostaniesz port i token.
2. W aplikacji wpisz: adres IP komputera (sieć WiFi — ta sama!), port i token.
3. Naciśnij **Połącz**, zgódź się na uprawnienia (telefon, mikrofon, stan telefonu).
4. Status „sparowano” → powiedz Sentinelowi: *„zadzwoń do restauracji X i zarezerwuj…”*.

Bezpieczeństwo: połączenie przyjmuje tylko komputer z poprawnym tokenem; token jest w
ustawieniach Twojego SentinelX, nie w chmurze. Most działa TYLKO po Twoim wyraźnym
poleceniu (domyślnie wyłączony) i można go wyłączyć słowami „wyłącz most telefoniczny”.

## Głos Sentinela

Domyślnie: lokalny syntezator Windows (darmowy, natychmiastowy). Własny głos: po włożeniu
próbek i ustawieniu zgody „Zgoda na własny głos” (ustawienia → Telefon). Kluczowe: zgoda
dotyczy WYŁĄCZNIE Twojego głosu — użycie cudzego głosu bez zgody danej osoby jest
zablokowane z założenia.
