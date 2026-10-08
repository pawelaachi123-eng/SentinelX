# 1.0.0 — Build 1.0

- Niezależny Agent PC (`--agent`): Base, kolejka, heartbeat i diagnostyka bez otwartego UI; uwierzytelniony potok UI↔Agent, jeden pisarz kolejki, kontrolowany autostart.
- SX4 binarny/TLS: format 49 B, HMAC, pin, challenge/capabilities, granty, revocation, limity; klienty C# i Java; opcode 201 po stronie PC/Android (Base musi wdrożyć rozszerzenie).
- Szyfrowana kolejka zadań z TTL, zgodami i audytem; pliki tylko w zatwierdzonych katalogach (kanonizacja, kopie, zapis atomowy).
- Pełna transakcja aktualizacji: pobieranie HTTPS z resume/retry, weryfikacje (semver/hash/RSA-PSS/arch/miejsce), bezpieczny punkt, kopia stanu, smoke-gate, handoff, realne potwierdzenie zdrowia, last-known-good i rollback.
- Odzyskiwanie: licznik awarii zwykłych uruchomień, czytelny ekran naprawy bez rozparowania, backup uszkodzonych konfiguracji.
- Diagnostyka z rzeczywistymi próbami (DPAPI, ustawienia, router, DHCP, relay, kolejka, Base, logi, aktualizacje), stanami PASS/BLOCKED/SKIPPED, podglądem napraw i redagowanym eksportem.
- Ollama: dedupe, backoff, brak autoload, modele na żądanie ze zwalnianiem po bezczynności; Steam tylko numeryczny AppID.
- Powłoka: motywy, wersja 1.0.0, zasoby EN/pl-PL dla chrome, dostępność i redukcja animacji; Android: bezpośrednie łącze z Base, JobScheduler, uprawnienia runtime.
- Pamięć: zgody, retencja, eksporty bez sekretów, brak telemetrii i nagrywania.
- Bez generowanego manifestu aktualizacji (decyzja użytkownika). Stabilne v1.0.0 wymaga weryfikacji sprzętowej (BLOCKERS.md).
