# Telefon: protokół, bezpieczeństwo i ograniczenia (0.94)

Ten dokument jest kontraktem między trzema implementacjami: serwerem na komputerze (`Services/Link`), interfejsem webowym
(`Phone/web`, serwowanym przez komputer i ładowanym w aplikacji na Androida) oraz atrapą do testów (`scripts/phone-mock`).
Jeśli zmieniasz API, zmień wszystkie trzy (test `tests/LinkRegression.cs` i `scripts/phone-mock/ui-test.mjs` to sprawdzają).

## Jak to wygląda

![Interfejs telefonu: parowanie, czat, komputer, zadania, alerty](img/phone-overview.png)

*Zrzuty z testu w prawdziwej przeglądarce (headless Chromium, widok 390×844) na danych demonstracyjnych — `scripts/phone-mock/ui-test.mjs`. Na telefonie z Androidem ten sam interfejs ładuje aplikacja.*

## Jak to działa

```
telefon (aplikacja Android albo przeglądarka)            komputer (Sentinel X)
  UDP "SXLINK1?" (broadcast, port 43181) ─────────────►  odpowiada: nazwa, port, odcisk certyfikatu
  HTTPS (port 43180..43189, certyfikat przypięty) ────►  strona + API (polecenia idą tą samą drogą co z klawiatury)
```

Wszystko dzieje się w sieci lokalnej. Nie ma serwera pośredniczącego, konta ani chmury. Serwer odrzuca połączenia z adresów
innych niż prywatne (10/8, 172.16/12, 192.168/16, 169.254/16, 100.64/10 — ten ostatni to Tailscale, ::1, fe80::/10, fc00::/7).

## Bezpieczeństwo

| Element | Jak jest zrobiony |
| --- | --- |
| Szyfrowanie | TLS z certyfikatem generowanym raz na komputerze (RSA 2048, ważny 10 lat). Klucz prywatny leży tylko w `%LOCALAPPDATA%\SentinelX\Link\cert.dpapi`, zaszyfrowany DPAPI bieżącego użytkownika Windows. |
| Zaufanie do certyfikatu | Brak urzędu certyfikacji. Aplikacja na Androida **przypina odcisk SHA-256** certyfikatu (`PinnedTls`, `onReceivedSslError`). Przeglądarka pokazuje ostrzeżenie — trzeba je zaakceptować raz. |
| Parowanie | Telefon wysyła prośbę; na komputerze pojawia się okno z nazwą urządzenia i **6-cyfrowym kodem potwierdzenia**; dopiero kliknięcie **Zezwól** wydaje token. Przycisk jest nieaktywny przez ok. 2 s, prośba wygasa po 120 s, naraz czeka tylko jedna. |
| Kod potwierdzenia (SAS) | `SHA256(odcisk certyfikatu ‖ nonce telefonu ‖ nonce komputera)`, pierwsze 4 bajty jako liczba big-endian, modulo 1 000 000. Telefon (aplikacja) liczy go z odcisku certyfikatu, **który faktycznie zobaczył**; komputer z własnego. Gdy ktoś podszywa się pod komputer, kody się różnią i telefon ostrzega. W przeglądarce kod pochodzi od serwera, więc tam chroni tylko zgoda na komputerze. Wektor testowy: odcisk `00..1f`, nonce `oKGio6SlpqeoqaqrrK2urw` i `sLGys7S1tre4ubq7vL2-vw` → `078914`. |
| Token | 256 bitów losowych, pokazany telefonowi **jeden raz**. Komputer zapamiętuje tylko SHA-256 tokenu (`devices.json`), porównanie w stałym czasie. Do 10 telefonów; najdawniej używany jest zastępowany. |
| Limity | 6 prób parowania/min na adres, 30 błędnych tokenów/min na adres, 32 równoczesne połączenia, nagłówki ≤ 16 KB, treść ≤ 64 KB, limity czasu. |
| Polecenia z telefonu | Wykonuje je ten sam `ActionEngine` co z klawiatury, ale jako „głos” (`fromVoice: true`): silnik **odmawia potwierdzenia** ryzykownej akcji (zamknięcie programu, usuwanie). Telefon może ją zaproponować, **zatwierdza się ją tylko na komputerze**. |
| Czego to nie chroni | Złośliwego programu już działającego na komputerze, ani osoby, która siedzi przy odblokowanym komputerze i sama kliknie **Zezwól**. Przy wycieku tokenu telefonu: Telefon → Odłącz wszystkie telefony. |

## Wykrywanie (UDP)

Telefon wysyła na port **43181** (broadcast 255.255.255.255 i adresy rozgłoszeniowe interfejsów) ośmiobajtowy tekst `SXLINK1?`.
Komputer odpowiada jednym datagramem JSON: `{"app":"SentinelX","api":1,"name":"PC","port":43180,"fingerprint":"<64 hex>"}`.
Odpowiedź nie zawiera nic tajnego; wykrywanie niczego nie autoryzuje. Limit: 20 odpowiedzi/s.

## API (HTTPS, JSON UTF-8, nazwy camelCase)

Bez uwierzytelnienia: statyczne pliki (`/`, `/app.js`, `/app.css`, `/manifest.webmanifest`, ikony; `ETag` + `If-None-Match`), oraz

| Metoda i ścieżka | Ciało → odpowiedź |
| --- | --- |
| `GET /api/hello` | `{app, api:1, version, name, fingerprint}` |
| `POST /api/pair/request` | `{device, nonce}` (nonce: 16 bajtów base64url) → `{id, nonce, expiresIn, sas}`; 429 gdy czeka inna prośba |
| `GET /api/pair/status?id=` | `{state: pending\|approved\|denied\|expired, token?, deviceId?}`; `token` tylko raz |

Z nagłówkiem `Authorization: Bearer <token>` (401 `{"error":"unauthorized"}` bez niego):

| Metoda i ścieżka | Znaczenie |
| --- | --- |
| `GET /api/state` | `{pc:{name,version,uptime,time,mac[],addresses[]}, metrics:{cpu,ramUsed,ramTotal,gpu,game,network,disks[],processes[]}, engine:{state,message,progress,model,installed[]}, assistant:{busy,stopped,pending,pendingSummary}, care:{ok,text}, counts, alertsLast}` (brak odczytu = `null`) |
| `POST /api/chat` `{text}` | strumień `text/event-stream`: `start`, `delta {text}`…, `done {text,status,evidence,elapsedMs}` albo `error {error}`. `status`: `verified`, `unverified`, `failed`, `cancelled`, `waitingpermission`… |
| `POST /api/control` `{action: cancel\|stop\|resume}` | przerwanie polecenia, STOP awaryjny, wznowienie |
| `GET /api/tasks` · `POST /api/tasks` `{title,priority,due}` · `POST /api/tasks/status` `{id,status}` · `POST /api/tasks/delete` `{id}` | zadania (priorytet: niski/normalny/wysoki; status: otwarte/w toku/zrobione) |
| `POST /api/reminders` `{text,at}` · `POST /api/reminders/delete` `{id}` | przypomnienia (termin w przyszłości, z przesunięciem strefy) |
| `GET /api/notes?q=` · `POST /api/notes` `{text}` | notatki z pamięci; odpowiedź `{result: Added\|Duplicate\|Limit\|Disabled\|Invalid\|StaleDuplicate}` |
| `GET /api/alerts?after=<id>&wait=<0..25>` | alerty nowsze niż `after`; przy `wait>0` serwer czeka na kolejny (long-poll) |
| `GET /api/devices` · `POST /api/unpair` | lista sparowanych telefonów · odłączenie tego telefonu |

Alerty pochodzą z: Watch (długie obciążenie CPU/RAM), przypomnień, silnika AI (gotowy / wymaga uwagi), dysku (mało miejsca) i opiekuna.

## Aplikacja na Androida (`phone-android/`)

Cienka powłoka (Java, bez bibliotek zewnętrznych): ładuje stronę z komputera w WebView, **sama szuka komputera** (UDP), przypina certyfikat,
co ok. 15 minut (minimum Androida) pyta o alerty i pokazuje je jako powiadomienia, rozpoznaje mowę po polsku (`RecognizerIntent`)
i potrafi wybudzić komputer (Wake-on-LAN, adres MAC pobrany z `/api/state`). Most do strony: obiekt `SXNative`
(`tlsFingerprint`, `deviceName`, `saveSession`, `saveMac`, `clearSession`, `hasVoice`, `startVoice`, `canWake`, `wakePc`, `rediscover`).

## Ograniczenia

- Działa w jednej sieci (albo przez własną sieć prywatną, np. Tailscale — adresy 100.64/10 są akceptowane). Nie ma dostępu „z dowolnego miejsca”.
- Komputer musi działać (Wake-on-LAN pomaga, jeśli karta sieciowa i BIOS pozwalają).
- Powiadomienia na Androidzie: sprawdzanie co ~15 min, nie natychmiast; w otwartej aplikacji alerty przychodzą od razu.
- Pierwsze połączenie wymaga zezwolenia w zaporze Windows (okno systemowe) — bez niego telefon nie dotrze do komputera.
- Aplikacja i strona nie były jeszcze sprawdzone na prawdziwym telefonie (tylko w przeglądarce headless i testach w CI).
