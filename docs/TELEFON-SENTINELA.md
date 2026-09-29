# TELEFON SENTINELA — PhoneCallTool (0.99)

Zwykłe narzędzie głównego AI. **Bez osobnej zakładki, bez osobnego asystenta.** Mówisz, czego
chcesz osiągnąć — Sentinel sam rozpoznaje, że potrzebny telefon, i używa PhoneCallTool.

## Co potrafi (uczciwie)

| Krok | Jak to robi naprawdę |
|---|---|
| Rozpoznanie intencji | „zadzwoń…”, „telefon do…”, „zarezerwuj przez telefon…” → narzędzie startuje automatycznie |
| Znalezienie numeru | publiczna wyszukiwarka przez `WebAccessService` (tylko przy włączonej sieci — polecenie „wifi on”); numer zapisywany jako +48…; jeśli wolisz — podaj numer wprost |
| Dzwonienie | **Twoja karta SIM/eSIM** — most TCP + aplikacja pomocnicza na Androidzie (`ACTION_CALL`); rozmówca widzi Twój numer, zero VoIP |
| Słuchanie | mikrofon telefonu przez relację głośnika → STT lokalny (Whisper Small, ten sam model co tryb głosowy) |
| Odpowiadanie | LLM (Twoja konfiguracja AI) → TTS (Windows, darmowy; własny głos — po zgodzie i próbkach) |
| Pamięć rozmowy | pełny kontekst w prompcie + transkrypt na dysku; po ponownym połączeniu Sentinel „dzwoni ponownie” ze stanem |
| Dopełnienie zadania | potwierdzenie celu wykrywa deterministyczny czujnik („zarezerwowane”, „potwierdzam”…) |
| Historia | „Sentinel, historia rozmów” albo `/rozmowy` → lista + „rozmowy otworz ROZ-…” otwiera transkrypt Markdown |

## Przykład z życia

Ty: **„Sentinel, zadzwoń do restauracji Fiesta i zarezerwuj jutro stolik na 18:00 dla 4 osób.”**
Sentinel dzwoni, mówi: „Dzień dobry! Chciałbym zamówić stolik na jutro 18:00 dla 4 osób.”
Restauracja: „18:00 zajęta, możemy 17:00.”
Sentinel: „Dobrze, jeszcze potwierdzę i oddzwonię. Dziękuję bardzo.” → kładzie słuchawkę → do Ciebie:
**„Rozmówca proponuje 17:00 zamiast 18:00. Przyjąć? (tak/nie)”**
Ty: „tak” → Sentinel dzwoni ponownie: „Dzwonię ponownie w tej samej sprawie. 17:00 będzie w porządku,
proszę o potwierdzenie.” → „Potwierdzam.” → **„✅ Gotowe. jutro 18:00, 4 os.”** (o ile faktycznie
doszło — cytat z rozmowy jest w dowodzie).

Dane, których Sentinel NIE pyta ponownie (masz je w ustawieniach → Telefon):
numer kontaktowy **786843433** (domyślnie zapisany) i imię do rozmów.

## Zasady decyzji (mocno pilnowane)

- Zwykłą rozmowę Sentinel prowadzi **sam** — nie pytajnę Cię o znane dane.
- Zmiana **terminu, daty, ceny, opłaty, zaliczki albo ważnego warunku** → ZAWSZE najpierw Ty.
  Czujnik jest deterministyczny (alternatywna godzina, słowa „opłata/zaliczka/kaucja/przedpłata/
  zajęte/brak miejsc…”), a nie „ufam modelowi”.
- „nie” = bez akceptacji zmiany; historia zapisuje to jako ODRZUCONE, nigdy jako sukces.

## Uczciwość (no success = no pass, także w telefonie)

- „Gotowe” pada **wyłącznie** po prawdziwym połączeniu (dowód **OFFHOOK** — stan z systemu
  telefonu, nie wymysł) i potwierdzeniu celu przez rozmówcę.
- Dowód w historii akcji: `DIAL +48… → OFFHOOK po N s · zwroty rozmówcy: N · potwierdzenie: „…” · rozmowa N s`.
- Sąd dowodów (reguła 9): `CALL_` VERIFIED bez OFFHOOK i numeru zostaje OBALONY → FAILED + rada.
- Bez mostu / bez modelu mowy / bez numeru i sieci: **uczciwa odmowa z instrukcją**, nie udawana rozmowa.
- Porażka = rada naprawy (rodzina `CALL_`): sprawdź aplikację pomocniczą, token, zasięg SIM.

## Instalacja (jednorazowo)

1. **Telefon**: zbuduj `companion/android` (Android Studio / `gradlew :app:assembleDebug`),
   zainstaluj, wpisz adres PC + port + token z kroku 2, nadaj uprawnienia (telefon, mikrofon,
   stan telefonu). Pełna uczciwa tabela możliwości: `companion/android/README-ANDROID.md`.
2. **PC**: powiedz **„włącz most telefoniczny”** → dostaniesz port i token (zapisują się w
   ustawieniach). Ten sam token wpisz w aplikacji.
3. **Model mowy**: pobierz Whisper Small (ustawienia Głos / tryb głosowy) — bez niego Sentinel
   uczciwie odmówi dzwonienia, bo nie mógłby słuchać.
4. Wyłączanie: **„wyłącz most telefoniczny”**.

## Prywatność i zasady

- **Zero nagrań.** Zapisujemy wyłącznie transkrypt tekstowy (Markdown) lokalnie w
  `History/PhoneCalls/`. Surowe audio nigdy nie trafia na dysk.
- Klonowanie głosu: wyłącznie Twój własny głos i tylko po ustawieniu zgody
  („Zgoda na własny głos”). Cudzy głos bez zgody — zablokowane z założenia.
- Most: domyślnie WYŁĄCZONY, start tylko na wyraźne polecenie, token parowania, LAN.
- Rozmowy prowadzone uprzejmie i krótko; Sentinel przedstawia się jako asystent
  działający w Twoim imieniu — nigdy nie udaje człowieka, jeśli rozmówca wprost zapyta.

## Ograniczenia, o których mówię wprost

- Android 10+ nie daje zwykłym aplikacjom audio rozmowy SIM — stąd relacja głośnika
  (telefon leży głośnikiem do góry podczas rozmowy Sentinela).
- Bez karty SIM/zasięgu połączenie nie zawiąże się — dowód pokaże stan `IDLE` i porażkę.
- SMS-y: świadomie nieobsługiwane.
