# Sentinel X — niezależny Agent PC (Build 1.0)

Agent to ten sam `SentinelX.exe` uruchomiony z flagą `--agent`: proces bez okna,
bez mikrofonu i bez telefonu, który jest właścicielem połączenia z Base
i kolejki zadań. UI (`SentinelX.exe` bez flag) wykrywa Agenta i przechodzi
w tryb obserwatora — wszystkie operacje Base idą wtedy przez uwierzytelniony
kanał do Agenta (jeden pisarz, brak podwójnej egzekucji).

## Uruchamianie

- Ręcznie: `SentinelX.exe --agent` (zamyka się czysto razem z sesją Windows).
- Autostart: Ustawienia → Ogólne → „Agent w tle (bez okna)” — wpis
  `HKCU\...\Run\Sentinel X Agent` tylko dla bieżącego użytkownika.
  Domyślnie WYŁĄCZONE — użytkownik włącza świadomie.
- Jedna instancja: mutex `Local\SentinelX-Agent-<key>`; druga kopia kończy się
  kodem 2 i wpisem w `Logs/agent.log`.

## Uwierzytelnianie UI ↔ Agent

- Przy starcie Agent losuje 256-bitowy token, zapisuje go w `Agent\token.bin`
  (DPAPI bieżącego użytkownika) i kasuje przy czystym zatrzymaniu.
- UI odczytuje token przez DPAPI i dołącza go do każdego żądania na nazwanym
  potoku `SentinelX-Agent-<key>` (tylko loopback, maks. 4 klientów).
- Zły/brak tokenu → `{"ok":false,"error":"auth"}` i rozłączenie.
- Token jest leniwy po stronie UI: gdy Agent dopiero startuje, UI pokazuje
  „Agent uruchamia się…” i nigdy nie przejmuje kolejki na własność.

## Operacje potoku (JSON, jedno żądanie na połączenie)

`hello` · `status` (status, wersja, pid, gaming, config bez sekretu,
capabilities, metryki) · `tasks` · `config` · `pair` · `unpair` · `request`
· `repair-queue` · `diagnose` · `stop`. Limity: 1 MiB na komunikat, 30 s na
żądanie, 5 s na połączenie. Nieznana operacja → `unknown_op`.

## Heartbeat i metryki

- `Agent\heartbeat.json` (atomowo przez `.tmp`): czas, pid, wersja, status,
  flaga gry, metryki (CPU, RAM, GPU, uptime). Co 15 s, a podczas gry co 60 s
  (ograniczone odpytywanie, heartbeat i zabezpieczenia działają dalej).
- Telemetria do Base (`pc.heartbeat`) bez zmian: wersja, CPU/RAM/GPU, dyski,
  MAC/karty sieciowe, uptime, stan sesji.
- Log: `Logs\agent.log` (rotacja przy 1 MiB, bez sekretów).

## Bezpieczeństwo w trybie headless

- Zgody kolejki są ODRZUCANE (nikt nie może kliknąć „Zatwierdź”) — operacje
  krytyczne czekają na UI; reszta działa normalnie.
- Zadania sesyjne czekają na odblokowaną sesję (`IsUnlocked`, jak w UI).
- `sentinel.show` bez okna jest no-opem. Nowe parowania telefonu wymagają UI.
- Mikrofon nie istnieje w grafie Agenta — wyciszenie nie zatrzymuje zadań.

## Testy

- Kontrakty (`verification/Build1`, Ubuntu + Windows): parse protokołu,
  token roundtrip/odrzucenie, status/aut/nieznana operacja/przeciążenie
  przez prawdziwy potok, sonda proxy.
- `SentinelX.exe --agent-smoke <katalog>` (Windows CI na wysyłanych plikach):
  mutex, token, status/tasks/config, odrzut autoryzacji, heartbeat, diagnostyka
  (≥10 kontroli), stop przez potok. Dowód: `AGENT-SMOKE.txt`.
