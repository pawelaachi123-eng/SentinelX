# SENTINEL X 0.97 · JARVIS — automatyzacja i narzędzia z listy 1550

Data: 2026-09-27 · Wersja: `0.97 · JARVIS` · Pakiet: `0.97.0`

Dostałem listę „1550 toolsów prywatnego Jarvisa” z prośbą: dodaj te funkcje, a te, które już masz —
ulepsz. Zrobiłem to, co da się zrobić **lokalnie, po polsku i z testami**, a każdy blok, którego nie
ma, dostał uczciwy powód w [docs/FEATURE-MAP-1550.md](FEATURE-MAP-1550.md). W aplikacji skrót do
tej mapy to `mapa funkcji`.

## Nowe polecenia

### Automatyzacja (rdzeń 007/008/010/020/022)
- **Harmonogram (#007)** — `zaplanuj: 7:30 dzień dobry`, `zaplanuj w dni robocze 8:00 zadania`,
  `zaplanuj w weekend 9:00 plan dnia`, `zaplanowane`, `usuń zaplanowane 1`, `usuń wszystkie zaplanowane`.
  Magazyn `Memory/schedules.json` (zapis atomowy + kopia uszkodzonego pliku + odczyt zwrotny SHA-256,
  jak pamięć, zadania i rutyny). Działa **gdy aplikacja jest uruchomiona**: nie rejestruję nic
  w harmonogramie zadań Windows, więc nic nie uruchomi Ci się bez otwartego Sentinela.
  Okno wyrównania: 15 minut — nie doganiam w nocy terminów sprzed wielu godzin, tylko liczę je
  jako pominięte i pokazuję to na liście. **Harmonogram odrzuca z góry** polecenia niszczące dane
  i zasilanie (zamykanie, restart, uśpienie), bo wykonuje się bez pytania o zgodę.
- **Watchdog folderów (#008)** — `obserwuj: C:\Users\Ty\Pobrane`, `co nowego w folderze`,
  `obserwowane`, `przestań obserwować 1`. Porównuję obraz folderu z poprzednim co 20 s i mówię,
  co jest nowe, zmienione albo usunięte. **Tylko podgląd**: nic nie przenoszę, nie kasuję i nie
  uruchamiam akcji po wykryciu zmiany. Zdarzenia żyją w sesji, limity: 6 folderów, 200 zdarzeń,
  800 plików w obrazie folderu.
- **Spójność danych (#022)** — `spójność danych` liczy SHA-256 każdego pliku Sentinela
  (pamięć, zadania, projekty, snapshoty, ustawienia, wzorce, rutyny, harmonogram) i pokazuje stan.
  Niczego nie naprawia sama, bo nie wiemy, która wersja jest prawdziwa.
- **Dziennik zdarzeń (#010)** — obok `errors.log` powstaje strukturalny `Logs/sentinel.jsonl`
  (kategoria, czas, treść, szczegóły) z rotacją po 512 KB i trzema starszymi kopiami. `dziennik`
  pokazuje ostatnie zdarzenia. `errors.log` rotuje teraz przez 3 kopie zamiast jednej.
- **Kopie z kompresją i rotacją (#020)** — każda kopia to też jeden plik ZIP (do przeniesienia
  na pendrive); trzymam 5 archiwów i 3 foldery, starsze usuwa rotacja przy tworzeniu nowej kopii.
  `kopie zapasowe` pokazuje listę z rozmiarami i datami.

### Pulpit i głos
- **Pulpity wirtualne (#804)** — `nowy pulpit` (Ctrl+Win+D), `pulpit w lewo` / `pulpit w prawo`
  (Ctrl+Win+←/→), `zamknij pulpit` (Ctrl+Win+F4), `pulpity`. Uczciwie: Microsoft nie daje publicznego
  API do liczenia pulpitów, więc nie potrafię powiedzieć, ile ich masz — mówię o tym wprost.
- **Wyciszanie tła (#696)** — gdy czytam odpowiedź głosem, ściszam system do 25% i przywracam
  głośność po skończeniu. Nie dotykam wyciszenia (mute), tylko poziomu, i niczego nie zapisuję.

### Bezpieczeństwo i dane (143/145/811/1002/1020/1075)
- **Siła hasła (#143)** — `siła hasła: twoje-haslo`: długość, klasy znaków, entropia, słownik,
  ciągi (abc/123/qwerty), powtórzenia, daty. Hasło jest liczone w pamięci i natychmiast zapominane.
- **Bezpieczny schowek (#145)** — `schowek auto 30`: to, co sam skopiuję (`kopiuj: …`), znika
  ze schowka po 30 s. `wyczyść schowek` robi to od razu, `bezpieczny schowek` pokazuje status.
  Czyszczenie działa na tikui aplikacji — Sentinel nie steruje schowkiem po zamknięciu.
- **Szukanie w treści (#811)** — `przeszukaj pliki: fraza` (albo `grep: fraza`): tylko Pulpit/Dokumenty/Pobrane,
  tylko pliki tekstowe do 1 MB, głębokość 3, 6 s, 15 trafień (1 na plik). Wyłącznie odczyt.
- **Profil CSV/TSV (#1071/1075)** — `analizuj csv: plik.csv`: wiersze, kolumny, braki (%),
  unikalne, typ liczbowy z min/max/średnią, powtarzające się wiersze. Separator wykrywam sam
  (`,` `;` TAB). Do 4 MB i 20 tys. wierszy — resztę zgłaszam wprost.
- **Porównanie plików (#1020)** — `porównaj pliki: A | B` (albo `porównaj tekst: A | B`):
  różnice linia po linii (LCS), licznik +/−, pierwsze 60 zmian. Pliki tylko odczytywane.
- **Eksport kalendarza (#1002)** — `eksportuj kalendarz` tworzy `sentinel-kalendarz.ics` na Pulpicie
  z Twoich zadań z terminem i przypomnień. Zwykły plik .ics: otworzysz go w Kalendarzu Windows,
  Outlooku albo telefonie.

### Kalkulatory (finanse 1141–1200, zdrowie 1201–1250)
`inflacja: 1000 5 3` · `cel oszczędzania: 20000 1500 4` · `spłata długu: 5000 200 12`
(albo kilka długów) · `roi: 5000 7500` · `próg rentowności: 40 15 3000` · `deprecjacja: 12000 5`
· `bmr: 80 180 30 m` · `tdee: 80 180 30 m 3` · `makro: 2400 80` · `woda: 80` · `tetno: 30`
· `cykle snu: 23:00` (albo `cykle snu: 23:00 7:00`).
Każda odpowiedź podaje **wzór i założenia** — to kalkulator, nie doradca.

## Ulepszone funkcje, które już były
- **Kopie zapasowe** (0.92) → ZIP + rotacja + lista kopii.
- **Dziennik błędów** → kategorie + strukturalny JSON + rotacja 3 kopii.
- **Schowek** (0.95/0.96) → auto-czyszczenie i lepszy opis; `kopiuj:` idzie przez bezpieczny schowek.
- **Głośność przy mówieniu** → wyciszanie tła.
- **Pomoc i palety**: `co nowego`, „pomoc”, Ctrl+K (`CommandCatalog`) i `//` (`SlashCatalog`)
  dostały wszystkie nowe pozycje.

## Bezpieczeństwo i uczciwość (bez zmian)
- Harmonogram **nie przyjmuje** poleceń niszczących dane ani zasilania — taką komendę wykonuj sam.
- Watchdog **tylko obserwuje**: żadnych automatycznych akcji po wykryciu zmiany.
- Spójność danych **niczego nie naprawia**: pokazuje stan i odsyła do kopii zapasowej.
- Nic nie jest rejestrowane w systemie (żadnych wpisów w harmonogramie Windows, w autostarcie,
  w usługach) i nic nie wychodzi do sieci.
- Wszystkie nowe magazyny mają te same gwarancje, co pamięć: zapis atomowy, kopia uszkodzonego
  pliku, odczyt zwrotny SHA-256.

## Testy
`tests/SystemAutomationRegression.cs` (wpięty w `--ui-smoke`) sprawia: harmonogram (dodawanie,
odmowy, maski dni, okno 15 min, pominięcia, usuwanie, trwałość), watchdog (nowy/zmieniony/usunięty,
progi, opis), poller (tik przed godziną nic nie robi, po godzinie uruchamia raz), bezpieczny
schowek, siłę hasła, diff, profil CSV (zwykły i TAB), eksport .ics, szukanie w treści, sumy
kontrolne, kalkulatory (finanse i zdrowie), dziennik zdarzeń oraz uczciwe odmowy, gdy brak serwisu.
Testy nie ruszają stanu maszyny: nie zamykają systemu, nie czyszczą prawdziwego schowka i piszą
tylko do katalogu tymczasowego.

## Ograniczenia (uczciwie, zamiast atrap)
- Harmonogram i watchdog działają **tylko przy uruchomionej aplikacji** — to świadoma różnica
  względem harmonogramu zadań Windows.
- Pulpity wirtualne obsługuję skrótami: nie policzę, ile ich masz (brak publicznego API).
- Profil CSV nie czyta Excela (xlsx) ani plików powyżej 4 MB.
- Kalkulatory zdrowotne to wzory populacyjne, nie porada medyczna; finansowe nie uwzględniają
  podatków, prowizji ani zmiennego oprocentowania, jeśli ich nie podasz.
- Pełna mapa tego, czego z listy 1550 nie ma i dlaczego: [docs/FEATURE-MAP-1550.md](FEATURE-MAP-1550.md).
