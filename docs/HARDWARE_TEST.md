# Testy sprzętowe Build 1.0 — instrukcja właściciela (PC + telefon + ESP32)

Stan prawdy: gałąź `arena/31d30c39-sentinelx`, commit `bb531c1` (lub nowszy
zielony z tej gałęzi). Nic w tej instrukcji nie publikuje stabilnego `v1.0.0`
ani nie generuje manifestu aktualizacji. Wyniki wpisuj do macierzy w rozdziale 6.

## 0. Zasady

- Najpierw kopia: `%LOCALAPPDATA%\SentinelX\` (PC) — wystarczy spakować cały katalog.
- Artefakty CI to **verification/debug**: Windows bez Authenticode, APK podpisany
  kluczem debug. Nie nazywaj ich produkcyjnymi.
- Każdy przypadek kończy się dowodem: wpis w logu, zrzut ekranu albo kod błędu.
  Samo „działa” to nie wynik.
- Aktualizację „accept” (prawdziwe stage→smoke→aktywacja) wykonasz dopiero
  z pakietem i opisem od wydawcy. Tutaj testujesz tylko ścieżki odrzutu.

## 1. Pobranie i weryfikacja artefaktów

1. Otwórz przebieg CI dla swojego commita (linki w raporcie): na dole strony są
   `Artifacts`: `windows-1.0.0`, `windows-validation`, `android-1.0.0`,
   `android-validation`. Pobierz wszystkie cztery.
2. Rozpakuj. Oczekiwana zawartość:
   - `windows-1.0.0/`: `SentinelX-1.0.0-win-x64-setup.exe`,
     `SentinelX-1.0.0-win-x64-portable.zip`, `WINDOWS-SHA256SUMS.txt`.
   - `android-1.0.0/`: `SentinelX-1.0.0-android-debug.apk` (albo `-android.apk`
     przy kluczach produkcyjnych), `ANDROID-BADGING.txt`, `ANDROID-BUILD.txt`,
     `ANDROID-SHA256SUMS.txt`.
   - `*-validation/`: dowody `ui-smoke.txt`, `AGENT-SMOKE.txt`, raporty lint/testów.
3. Zweryfikuj skróty (Windows, PowerShell w katalogu artefaktu):
   `Get-Content WINDOWS-SHA256SUMS.txt` i porównaj każdy wiersz z
   `(Get-FileHash .\<plik> -Algorithm SHA256).Hash.ToLowerInvariant()`.
   Na Linuxie: `sha256sum -c ANDROID-SHA256SUMS.txt` w katalogu `android-1.0.0`.
4. Zweryfikuj wersje: we właściwościach EXE (Szczegóły → Wersja produktu)
   ma być `1.0.0`; `ANDROID-BADGING.txt` zawiera `versionName='1.0.0'`
   i `versionCode='100'`.
5. Zapisz skróty — wrócisz do nich w macierzy wyników.

## 2. PC (Windows 10/11, 64-bit)

Przygotowanie: ta sama prywatna sieć co Base/ESP32. Przy pierwszym starcie
zapora zapyta o sieć prywatną — zezwól.

### 2.1 Instalacja i pierwszy start
1. Uruchom `SentinelX-1.0.0-win-x64-setup.exe` (bez administratora) albo rozpakuj
   portable ZIP do pustego katalogu i uruchom `SentinelX.exe`.
2. Oczekiwane: okno Centrum, wersja `1.0.0` na dole po lewej, brak okien błędów.
   Dowód: `%LOCALAPPDATA%\SentinelX\Logs\` zawiera świeże logi bez `errors.log`
   (albo z wpisami wyłącznie informacyjnymi).
3. Ustawienia → Ogólne: włącz/wyłącz autostart UI i sprawdź wpis
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Sentinel X`.

### 2.2 Diagnostyka (panel Diagnostyka/aktualizacje → Uruchom)
Bez sparowanej Base oczekiwane: PASS dla Autostart/Sieć/Gateway/DNS/DHCP/Router/
Dysk/Konfiguracja/DPAPI/Kolejka/Logi/Crash-loop/Aktualizacje/Uruchomienia;
`Base/auth/SX4` = BLOCKED (tekst statusu, nie błąd); `Relay/VPN` i `Android` =
SKIPPED. Dowód: eksport diagnostyki (plik JSON zawiera wyłącznie znaczniki
`<redacted>`/`<hash>` zamiast sekretów — sprawdź tekstowo).

### 2.3 Agent niezależny
1. Zamknij UI. Z wiersza poleceń: `SentinelX.exe --agent` (proces bez okna).
2. Oczekiwane: `Agent\heartbeat.json` odświeża się co ~15 s (`pid`, `version`,
   `status`); `Logs\agent.log` rośnie; druga instancja `--agent` kończy się
   kodem 2, a w logu jest `already_running`.
3. Uruchom UI: panel Base pokazuje status Agenta; zadania wykonują się raz
   (sprawdź `Base\audit.jsonl` — brak zdublowanych `submitted`).
4. Zatrzymanie: z UI polecenie stop Agenta (albo koniec sesji) → `token.bin`
   znika z `Agent\`; UI po restarcie wraca do trybu bezpośredniego.
5. Autostart Agenta (Ustawienia → Ogólne, opt-in): po wylogowaniu/zalogowaniu
   Agent działa bez otwartego UI (sprawdź `heartbeat.json` przed otwarciem UI).

### 2.4 Kolejka i zgody (wymaga Base albo UI-direct)
1. Zadanie `lock` na odblokowanej sesji: ekran blokuje się w ~2 s; w audycie
   `accepted → running → succeeded`.
2. Zadanie `restart`: pojawia się lokalne okno zgody; brak kliknięcia = `cancelled`
   po wygaśnięciu; zgoda głosowa nie istnieje (tylko przycisk).
3. Zablokuj sesję: zadania czekają (nie wykonują się na zablokowanym ekranie).
4. Powiadomienie z Base: toast lokalny; w `audit.jsonl` treść to
   `local_notification`, nie tekst.

### 2.5 Ścieżki odrzutu aktualizacji (bez pakietu wydawcy)
1. Opis z błędnym JSON albo złą wersją (np. `1.0.0` przy bieżącej `1.0.0`):
   `Aktualizacja odrzucona: metadata|semver|downgrade`; aktywna wersja nietknięta.
2. Opis ze złym SHA-256: odrzut `hash` po pobraniu.
3. Jawny adres `http://` (nie `https`): odrzut `url` przed jakimkolwiek
   połączeniem. Adresy z przekierowaniem 302: odrzut `download`, nigdy pobranie
   po `http` (przekierowania są celowo nieobsługiwane).
4. Pełną ścieżkę accept wykonaj tylko z prawdziwym pakietem wydawcy, wtedy:
   poczekaj na brak zadań `running`, sprawdź kopię w `Backups\update-*`
   (ustawienia + Base + `pairing.json`), po restarcie potwierdź zdrowie
   (inwentarz/ustawienia/kolejka/parowanie) — szczegóły w `docs/SELF_HEALING.md`.

### 2.6 Odzyskiwanie po awarii
1. Ubij proces UI (Menedżer zadań → Zakończ). Uruchom ponownie.
2. Oczekiwane: ekran odzyskiwania z czytelnymi opcjami; `Kontynuuj` wraca do UI;
   licznik w diagnostyce (`Uruchomienia`) wzrósł o 1.
3. Krytyczne: parowanie Base jest NIENARUSZONE (brak prosby o ponowne parowanie,
   `identity.bin` istnieje). Reset ustawień tworzy kopię; archiwizacja
   uszkodzonego parowania tworzy plik `.damaged-*`, niczego nie kasuje po cichu.

## 3. Telefon (Android 7+)

1. Odinstaluj poprzednią aplikację Sentinel, jeśli była podpisana innym kluczem
   (Android odrzuci aktualizację przy różnych kluczach — to normalne).
2. Zainstaluj `SentinelX-1.0.0-android-debug.apk`, zezwól na instalację
   z nieznanego źródła, potem na powiadomienia (runtime).
3. Bez Base w sieci: aplikacja pokazuje stan offline, nie crashuje; obrót ekranu,
   Home i powrót nie gubią stanu ani nie duplikują zapytań.
4. Z Base w sieci (rozdział 4): parowanie kodem, potem panel Base BEZ udziału PC:
   status, urządzenia, sceny, WoL, zadania, timery.
5. Przełącz Wi-Fi → dane komórkowe (Base w LAN): połączenie zamyka się czysto
   i wraca po powrocie do Wi-Fi; komendy nie wykonują się podwójnie.
6. Powiadomienia dwoma kanałami: (a) w tej samej sieci Wi-Fi co PC przypomnienie
   z PC dociera przez połączenie telefon↔PC (LinkService); (b) alert pochodzący
   z Base dociera w tle w ~15 min (JobScheduler, bez foreground service).
   Kanał (b) wymaga Base; kanał (a) wymaga PC w tej samej sieci.

## 4. ESP32 (Base) — wymagania i testy protokołu

### 4.1 Co musi umieć firmware (minimum Build 1.0)
- TCP + TLS 1.2/1.3 z certyfikatem; PC i telefon pinują SHA-256 certyfikatu.
- Ramka SX4: nagłówek big-endian `!HBIQH` (device:2, opcode:1, nonce:4,
  timestamp_ms:8, payload_len:2 = 17 B) + MAC 32 B + payload; razem 49 B + N.
  MAC = HMAC-SHA256(klucz 32 B, nagłówek 17 B + payload). Opcode rozszerzenia:
  **201**. Limit payloadu 16384 B.
- Sesja: `session.prove{challenge,role,deviceId}` → `session.proved{challenge}`
  (echo identyczne); `capabilities{}` → `capabilities.result{types[]}` (max 32,
  nazwy `[A-Za-z0-9._]`, bez duplikatów).
- Typy PC: `pc.heartbeat` (WYMAGANY — jego brak to `unsupported` i stop linku),
  `tasks.poll` → `{tasks[]}` (max 32), `tasks.status`, `devices/status`.
  Zapytania telefonu: `status` (co 15 s), `pc.state`, `devices`, `scenes`,
  `notifications`, `diagnostics`, `logs`, `tasks`; akcje: `wol`, `scenes.run`,
  `timers.create`, `tasks.create`.
- Błędy: `{"type":"error","data":{"code":"<kod>"}}`, kody `[a-z_]{1,64}`.
- Rejestr urządzeń: osobne DeviceId/sekrety na PC i telefon; revocation
  unieważnia sekret po obu stronach.
- Zegar: znacznik ramki w ±5 s względem PC; `expiresAt` komunikatu w przyszlosci
  (max +5 min). Różnica zegarów > 5 s = rozłączenie (to nie błąd PC).
- Rate limit: 120 ramek/min na urządzenie; nonce nie powtarza się w 10 s.

### 4.2 Wektor testowy (stały, do weryfikacji implementacji)
Klucz (TYLKO testowy, 32 B): `000102...1f`:
`[HEX_PLACEHOLDER_1]`
Payload (135 B): `{"version":4,"type":"status","requestId":"12345678-1234-1234-1234-123456789abc","correlationId":"","expiresAt":1760000000000,"data":{}}`
Ramka (184 B hex, device=7, opcode=201, nonce=1, ts=1760000000000):
`[HEX_PLACEHOLDER_2]`
Wektor służy do testu OFFLINE implementacji (parser, MAC, tabela nonce):
firmware przechodzi, jeśli parsuje nagłówek, liczy identyczny MAC, odrzuca
ramkę po zmianie dowolnego bajta (`auth`) i odrzuca powtórzony nonce (`replay`).
W teście na żywo PC odrzuci ten wektor oknami czasu (znaczniki z 2025 r.) —
tam firmware musi generować świeże timestampy i `expiresAt`.

### 4.3 Kolejność uruchomienia
1. Wgraj firmware, zanotuj fingerprint SHA-256 certyfikatu i sekret 64-hex.
2. PC: panel Base → wklej Host/Port/Fingerprint/Secret/DeviceId=2/BaseId=1/tryb
   `lan` → Paruj. Oczekiwane: `Base połączona · SX4 · LAN`, heartbeat co 15 s.
3. Telefon: to samo z DeviceId=3 (osobny sekret!), tryb `lan`.
4. Testy negatywne (każdy osobno, z odtworzeniem poprawnej konfiguracji po):
   zły sekret → `auth`; cofnięty zegar ESP32 o 10 s → `timestamp`; powtórzona
   ramka → `replay`; stan Base `error{challenge}` inny niż wysłany → `challenge`.
5. DHCP: restart routera / zmiana IP Base → PC sam odnajduje Base po
   fingerprint (LAN discovery) i wraca do `połączona` w ~1 min.
6. Opcjonalnie relay/VPN: własny endpoint TLS (tryb `relay`/`vpn`), potem test
   Wi-Fi → LTE z rozdziału 3.5. Bez własnego endpointu ten test jest BLOCKED
   z definicji (nie istnieje publiczny relay).

## 5. Kryteria stabilności (odblokowanie `v1.0.0` + `latest`)

Wszystkie poniższe muszą być zielone: instalacja setup+portable (2.1),
diagnostyka (2.2), Agent z proxy (2.3), kolejka+zgody (2.4), odrzuty aktualizacji
(2.5), recovery bez utraty parowania (2.6), telefon z rozdziału 3 (w tym tło),
Base z 4.3 (w tym testy negatywne i DHCP). Osobno: klucz produkcyjny APK
(mutatis mutandis powtórka rozdziału 3 na APK release) oraz próba relay/VPN
albo jawna decyzja, że relay zostaje poza 1.0.

## 6. Macierz wyników (wypełnia właściciel)

| # | Przypadek | Wynik | Dowód (plik/log/zrzut) |
|---|-----------|-------|------------------------|
| 1.3 | Skróty SHA-256 artefaktów | | |
| 1.4 | Wersje 1.0.0 / 100 | | |
| 2.1 | Instalacja setup + portable | | |
| 2.2 | Diagnostyka bez Base | | |
| 2.3 | Agent: heartbeat/proxy/stop/autostart | | |
| 2.4 | Kolejka: lock/zgoda/blokada/audit | | |
| 2.5 | Odrzuty: metadata/downgrade/hash/url | | |
| 2.6 | Recovery: ekran + parowanie całe | | |
| 3 | Telefon: install/lifecycle/tło/reconnect | | |
| 4.2 | Wektor SX4 na firmware | | |
| 4.3 | Parowanie PC+telefon, testy negatywne, DHCP | | |
| 4.3.6 | Relay/VPN (albo BLOCKED) | | |

