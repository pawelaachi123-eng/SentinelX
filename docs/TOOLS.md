# Narzędzia Sentinel X (0.95)

Strona **🧰 Narzędzia** (pasek boczny albo zakładka w Centrum, skrót `//narzedzia`, wpis w palecie Ctrl+K)
pokazuje wszystko, co Sentinel potrafi zrobić bez chmury: ponad 60 pozycji w kategoriach, z wyszukiwaniem,
przykładem, polem argumentu i wynikiem. Nad przyciskiem „Uruchom” zawsze widzisz **dokładne polecenie**,
które zostanie wysłane — nic nie dzieje się w tle i nic nie uruchamia się samo.

Każde narzędzie działa też z czatu: wystarczy wpisać jego polecenie. Uruchomienie idzie normalną
ścieżką silnika, więc obowiązują te same zasady co wszędzie: STOP awaryjny blokuje akcje, wynik ma dowód,
a polecenia zmieniające stan systemu wymagają zgody.

## Warsztat 0.95 — nowe narzędzia

| Polecenie | Co robi | Bezpieczeństwo |
| --- | --- | --- |
| `porownaj teksty: A \|\|\| B` | Różnice linia po linii plus podsumowanie. Separator `\|\|\|`, alternatywnie linia z trzech myślników. | Czysta funkcja, bez plików i sieci. |
| `regex: wzorzec \|\|\| tekst` | Dopasowania i grupy. | Limit 500 ms i 20 000 znaków — złożony wzorzec nie zawiesi aplikacji. |
| `sha256 pliku: <ścieżka>` / `md5 pliku:` / `hash pliku:` | Skrót, rozmiar i data zmiany pliku. | Tylko odczyt; pliki do 2 GB; ścieżka musi być wskazana wprost. |
| `wyciagnij: <tekst>` | E-maile, linki, adresy IPv4 i liczby. | Nic nie otwiera i nie wysyła. |
| `posortuj linie: <tekst>` | Porządek alfabetyczny (bez różnicy wielkości liter). Wiersze rozdzielaj `\|` albo znakiem nowej linii — czat spłaszcza entery, więc `\|` jest pewniejsze. | Do 2000 wierszy, pokazuje pierwsze 100. |
| `usun duplikaty linii: <tekst>` | Zostawia pierwsze wystąpienie i mówi, ile usunął (separator `\|`). | Jak wyżej. |
| `kwota slownie: 1234,56` | Polska odmiana złotych i groszy (0 – 999 999 999,99). | Czysta funkcja. |
| `sekundy: 3661` | Sekundy → dni/godziny/minuty/sekundy. | Czysta funkcja. |
| `na sekundy: 2h 15m 10s` | Zapis `2h 15m 10s`, `90min`, `1d` → sekundy. | Czysta funkcja. |
| `moc hasla: <hasło>` | Entropia, typowe słabości, szacowany czas złamania. | Hasła nie pokazuję w odpowiedzi, nie zapisuję i nie wysyłam. |
| `qr: <tekst>` | Kod QR zapisany jako PNG. | Plik trafia do `%LocalAppData%\SentinelX\Qr`. |
| `qr wifi: nazwa\|hasło` | Kod QR konfiguracji Wi-Fi (`WIFI:T:WPA;…`). | Jak wyżej — dane sieci zostają w pliku na Twoim dysku. |

## Pozostałe kategorie (skrót)

- **Tekst i kody** — liczenie słów i znaków, base64 w obie strony, SHA-256 tekstu, JSON, slug, transliteracja, Morse, binarnie, hex, ROT13, tytuł, wielkie/małe litery, odwracanie, palindrom, anagram.
- **Liczby i czas** — kalkulator (nawiasy, potęgi, przecinek), procenty, VAT 23%, pierwiastek, silnia, NWD/NWW, liczby pierwsze, dzielniki, Fibonacci, statystyki, jednostki, cyfry rzymskie, losowanie z zakresu, dni do daty, dzień tygodnia, wiek, dni robocze, wielkanoc, czas na świecie, BMI, tydzień roku.
- **Pliki i foldery** — duplikaty i raport porządkowy (tylko odczyt), folder danych Sentinela, odciski stanu komputera.
- **Dokumenty PL** — PESEL, NIP, IBAN, kolor hex → RGB, RGB → hex (walidacja lokalna).
- **System** — lokalne IP, nazwa komputera, rdzenie, architektura, wersja, diagnostyka, status zabezpieczeń, usługi, autostart, zdarzenia Windows.
- **Losowe i rozrywka** — hasło, PIN, UUID, Lotto, moneta, kości, wybór z listy.

## Jak szukać

Wpisz w polu szukania to, co chcesz zrobić („faktura”, „hash”, „qr”, „procent”, „diff”) — filtr patrzy na nazwę,
opis, polecenie, przykład i kategorię, więc trafisz nawet nie znając składni. Kategoria „Wszystkie” pokazuje
pełny katalog, a „Wyczyść filtry” wraca do początku.

## Czego tu nie ma (świadomie)

- **Zip/rozpakowywanie** — planowane, ale wymaga ścieżki zgód dla zapisu plików; na razie świadomie pominięte.
- **Schowek i zrzuty ekranu** — także planowane; wolimy je dodać razem z widocznym podglądem niż po cichu.
- **Cokolwiek, co wymaga chmury albo konta** — nie ma i nie będzie: wszystko liczy się na tym komputerze.
