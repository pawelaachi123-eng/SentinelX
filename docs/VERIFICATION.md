# Build 1.0 — macierz weryfikacji (12 sekcji specyfikacji)

Stan na gałęzi roboczej Build 1.0. Legenda: **PASS** — zaimplementowane
i pokryte automatycznym gate; **BLOCKED** — zależne od sprzętu/usług
zewnętrznych (szczegóły w `BLOCKERS.md`); pliki i testy to dowody.

## 1. Poprawki kompilacji Android — PASS
- `BaseSession.save` deklaruje `throws Sx4.Error`; kodowanie SX4 w workerze
  przed `ui.post`, a nie w lambdzie UI (`BaseActivity.java`).
- Dowód: `ci-android.yml` (`lintDebug testDebugUnitTest assembleDebug`).

## 2. Zielony pipeline APK — PASS
- JDK 17, Gradle wrapper 8.9 (checksum), SDK/build-tools 35, żadnych
  wyłączonych kontroli. `versionName=1.0.0`, `versionCode=100` (asercje
  `aapt badging`), `apksigner verify`, struktura ZIP + CRC.
- Bez produkcyjnego klucza: jawnie nazwany `SentinelX-1.0.0-android-debug.apk`
  (`production=false` w `ANDROID-BUILD.txt`); produkcja wymaga sekretów
  `SX_ANDROID_*` (BLOCKED, poz. 3 w `BLOCKERS.md`).
- Dowód: job `build` workflowu `CI Android 1.0` + `check-release-artifacts.py`.

## 3. Niezależny Agent PC — PASS
- `SentinelX.exe --agent`: proces bez okna/mikrofonu; Base + kolejka + heartbeat
  + metryki + diagnostyka. Jedna instancja (mutex), token 256-bit (DPAPI),
  potok nazwany z uwierzytelnianiem każdego żądania, heartbeat co 15 s
  (60 s w grze), log `Logs/agent.log`.
- Jeden pisarz kolejki: UI przy żywym Agencie pracuje przez proxy
  (`IBaseControl`); zgody headless odrzucane; zadania sesyjne czekają na
  odblokowanie. Autostart Agenta: opt-in w Ustawieniach (osobny wpis Run).
- Pliki: `Services/Agent/*`, `Services/Base/IBaseControl.cs`, `Testing/AgentSmokeRunner.cs`, `docs/AGENT.md`.
- Dowód: kontrakty agent (parse/token/pipe/proxy) na Ubuntu i Windows oraz
  `--agent-smoke` na wysyłanych plikach (`AGENT-SMOKE.txt`).

## 4. Zgodność SX4/Base — PASS (strona PC) / BLOCKED (sprzęt)
- Format binarny, nagłówek 49 B, HMAC, pin TLS, challenge, capabilities,
  role/granty, revocation, ID urządzeń, nonce/timestamp, limity i odrzut
  złych ramek — po stronie PC/Android zaimplementowane; kontrakty: roundtrip
  unicode, replay, auth, size/format, rate_limit, duplicate_key, missing_field,
  utf8, partial frame, guard `unsupported`, testy TLS C#+Java (`test-sx4-tls.py`).
- Restart/DHCP: re-discovery w LAN; backoff z jitterem (PC i Android).
- Opcode 201 wymaga wdrożenia rozszerzenia w Base — stock firmware 4.2.1 go
  nie zawiera. Próba na żywo z fizycznym ESP32: BLOCKED (poz. 1).
- Dowód: `verification/Build1`, `scripts/test-sx4-tls.py`, `docs/PROTOCOL_SX4.md`.

## 5. Kolejka / bezpieczeństwo / zdalny dostęp — PASS / BLOCKED (relay)
- Stany accepted/running/succeeded/failed/expired/cancelled, TTL 5 min,
  dedupe, brak powtórek po restarcie, zgody wygasające, audit JSONL (8 MiB,
  treść powiadomień redagowana), allowlista operacji, granty per-device,
  DPAPI/Keystore, rate limit 120/min, brak podwójnej egzekucji (Agent).
- Pliki tylko w zatwierdzonych katalogach: safe-name, brak nadpisywania przy
  kopiowaniu, kopia `.bak` + zapis atomowy + odczyt zwrotny, kanonizacja
  z dowodem przynależności (`FileWorkspaceService.Contain`).
- Relay/VPN: wyłącznie skonfigurowany własny endpoint TLS; brak domyślnego
  publicznego relayu; LAN offline; próba Wi-Fi→LTE: BLOCKED (poz. 2).
- Dowód: kontrakty kolejki/audytu + diagnostyka `Relay/VPN`, `docs/SECURITY.md`, `docs/REMOTE_ACCESS.md`.

## 6. Pełna transakcja aktualizacji — PASS
- Kanał HTTPS z resume (Range) i ponowieniami z backoff+jitter
  (`UpdateDownloader`; jawne URL-e, brak domyślnego serwera i manifestu
  z decyzji użytkownika). Weryfikacje: semver, brak downgrade, arch,
  rozmiar, SHA-256, RSA-PSS (domyślnie wymagany), miejsce na dysku,
  traversal/symlinki, wersja produktu.
- Czekanie na bezpieczny punkt (brak `running`, 60 s), zatrzymanie Agenta,
  kopia Settings+Base+`pairing.json`, staging→smoke (`--ui-smoke`)→aktywacja,
  handoff przy starcie, potwierdzenie zdrowia prawdziwymi kontrolami
  (inwentarz/ustawienia/kolejka/parowanie, 60 s), `NoteUnhealthy`,
  last-known-good, rollback po 3 niezdrowych startach.
- Dowód: `MaintenanceContract` (odrzuty, staging, health-gate, rollback,
  sick-launch, download/retry/resume) + `docs/SELF_HEALING.md`.

## 7. Tryb awaryjny / odzyskiwanie — PASS
- Licznik nieczystych startów UI i Agenta (`RunHealth`, obejmuje zwykłe
  uruchomienia), czytelny ekran odzyskiwania (MVVM) z: kontynuacją, rollbackiem,
  naprawą katalogów/autostartu, resetem ustawień (z kopią), archiwizacją
  uszkodzonego parowania, logami. Brak auto-restartów, brak rozparowania
  po timeout, migracja schematu ustawień (`MigrateLegacyJson`).
- Dowód: kontrakty `RunHealth`, gate architektury (thin code-behind),
  `Views/RecoveryWindow.*`, `ViewModels/RecoveryViewModel.cs`.

## 8. Diagnostyka i naprawy — PASS
- Rzeczywiste próby: DPAPI roundtrip + deszyfracja parowania, parsowanie
  i walidacja ustawień, ping gateway, wykrywanie APIPA, TCP-probe relay,
  zapytanie `devices/status`, próba zapisu logów, inwentarz aktualizacji,
  kolejka, Steam, Ollama (loopback, bez modelu), crash-loop, uruchomienia.
- Stany PASS/BLOCKED/SKIPPED (awaria vs nie dotyczy); sekwencja
  wykrycie→kopia→naprawa→weryfikacja→rollback/wynik z dziennikiem;
  podgląd naprawy i eksport diagnostyki po redakcji (`DiagnosticRedaction`).
- Dowód: `--agent-smoke` (`diagnose` ≥ 10 kontroli), kontrakty redakcji,
  `MaintenanceService.DiagnoseAsync/PreviewRepairAsync/ExportDiagnosticsAsync`.

## 9. Funkcje — PASS / BLOCKED (sprzęt audio)
- Głos PL, wake word „Sentinel” (Strict/Balanced/Forgiving), komendy tekstowe,
  router lokalny-first (AI ostatnią deską), limity odpowiedzi (w grze 260
  tokenów / 4k kontekstu), audyt kosztów w `docs/VERIFICATION.md`.
- Router domowy: ping + DHCP/APIPA w diagnostyce. Ollama: dedupe, backoff 30 s,
  `AutoLoadModel=false`, ładowanie modelu na żądanie + zwalnianie po 30 min
  bezczynności (sweep w heartbeat Agenta). Steam: tylko numeryczny AppID
  (kanoniczny uint, odrzut `00042`/`42;calc`).
- Podstawy działają bez AI (Narzędzia/Kuźnia/Warsztat lokalnie). Prawdziwy
  mikrofon: BLOCKED (poz. 4).
- Dowód: kontrakty Ollama/Steam/routera + `AiTestRunner`/`AsrTestRunner`.

## 10. Dopracowanie Windows + Android — PASS
- Pulpit (Centrum), ciemny motyw, nawigacja z Ustawieniami, wersja 1.0.0
  na dole po lewej (80%), klawiatura (Ctrl+K, Ctrl+Shift+X), skalowanie,
  motywy/kontrast, redukcja animacji (ustawienie + auto w grze),
  `AutomationProperties` w powłoce.
- Zasoby: `Resources/Strings.*` (EN neutral + satelita pl-PL) dla chrome
  powłoki; pełne tłumaczenie VM poza zakresem 1.0.
- Android: uprawnienia runtime (powiadomienia), lifecycle (epoki, reconnect),
  JobScheduler co 15 min (bez foreground service), `BaseActivity` łączy się
  z Base bezpośrednio bez PC.
- Dowód: `UiSmokeTestRunner` (strony, bindingi), lint/testy Androida.

## 11. Pamięć i wydajność — PASS
- Krótkoterminowa (polecenia/zadania/kontekst) i długoterminowa tylko za zgodą
  (`MemoryPrivacy`); podgląd/import z walidacją i deduplikacją; usuwanie;
  eksporty po redakcji sekretów (`TextScrubber`, kształt JSON zachowany);
  retencja; tryb prywatny; lokalnie, bez telemetrii; brak nagrywania mikrofonu
  (domyślnie OFF, transkrypcje w logach opt-in).
- Tryb gry: dłuższe interwały próbkowania, heartbeat i zabezpieczenia działają.
- Dowód: kontrakty scrubbera, `docs/MEMORY.md`, ustawienia `Zasoby`.

## 12. Wydanie weryfikowalne — PASS (potok) / BLOCKED (stabilność)
- `release.yml` czeka na security+windows+android; tag `v1.0.0` wymaga
  `hardware_verified`; w przeciwnym razie prerelease verification/debug
  (`--latest=false`); SHA256SUMS; notatki z `docs/RELEASE_NOTES.md`.
- Testy ESP32 poza CI (`test-sx4-tls.py` używa fixture). Flagę `latest`
  i tag `v1.0.0` wolno ustawić dopiero po spełnieniu warunków stabilnych
  (sprzęt) — do tego czasu BLOCKED.
- Dowód: ten plik, `VERSION`, `CHANGELOG.md`, `BLOCKERS.md`, artefakty CI.

## Audyt 2026-10-08 (po zielonym 56bf739, naprawy zielone na 84619ed)

Ręczny audyt kodu (Agent, SX4, aktualizacje, UI, Android) + testy sprzętowe
w `docs/HARDWARE_TEST.md`. Znaleziska i dyspozycje:

- F1 (potok): nieograniczone buforowanie wiersza przed autoryzacją → odczyt
  przyrostowy z limitem + ograniczony drain (8 MiB) — naprawione, kontrakt.
- F2 (token): nieatomowy zapis tokenu → tmp+move — naprawione, kontrakt.
- F3 (stop Agenta): niedrenowane handlery — uniewinnione (zapis kolejki atomowy).
- F4 (stale config w UI): uniewinnione (celowe: nie nadpisywać formularza parowania).
- F5 (capabilities): brak walidacji znaków typów → `IsCapabilityType` — naprawione, kontrakt.
- F6 (`GetProperty` bez `Try`): `KeyNotFoundException` zamiast kodu SX4 →
  `RemoteErrorCode` + `TryGetProperty` — naprawione, kontrakt.
- F7 (strumień po dispose): pole nie nullowane → nullowanie w `catch` — naprawione.
- F8 (wyjątki): surowy `JsonException` z opisu/dziennika → `UpdateFailure` —
  naprawione, kontrakt.
- F9 (pobieranie): auto-redirect (downgrade https→http) i nieograniczony odczyt
  opisu → `AllowAutoRedirect=false` + odczyt z limitem — naprawione, kontrakt.
  Silnik AI (EngineDownloader) celowo przekierowuje (CDN), integralność daje
  pinned SHA-256 — bez zmian.
- F10 (link PC): każdy błąd protokołu blokował link do restartu/re-pair →
  `IsFatalLinkFailure` (krytyczne blokują, przejściowe reconnect z backoff) +
  izolacja pojedynczych zadań w `tasks.poll` — naprawione, kontrakt.
- F10b (link Android): jak F10 → `fatalLink` + reconnect przejściowych —
  naprawione, testy KernelContract.
- F11 (HIGH): UI-direct + Agent mogli jednocześnie pisać kolejkę (self-heal UI
  sam tworzył drugiego właściciela) → mutex właściciela pętli Base, przejęcie
  w heartbeat Agenta, self-heal tylko w trybie proxy — naprawione, kontrakt.
- F12 (opis): brakujące pola opisu przechodziły `Parse` → odrzut `metadata` —
  naprawione, kontrakt.
- Handoff aktualizacji, single-instance, sekrety w logach/audycie, bindingi XAML
  (12/12 x:Static, 8/8 komend recovery), kodowanie SX4 w workerze Androida —
  zweryfikowane, bez zmian.

Lekcja CI: nieobsłużony wyjątek w kontraktach to exit 134 bez adnotacji
plikowych — ContractRunner raportuje teraz crash adnotacją `::error::`.
