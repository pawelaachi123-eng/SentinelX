# SENTINEL X 0.95 · WARSZTAT — notatki wydania

## Co nowego

| Obszar | Zmiana |
| --- | --- |
| Strona **🧰 Narzędzia** | Ponad 60 narzędzi w jednym katalogu: wyszukiwanie, kategorie, przykład, pole argumentu, „Uruchom”, wynik do skopiowania, historia sesji. Podgląd dokładnego polecenia przed uruchomieniem. |
| Nowe narzędzia | `porownaj teksty:`, `regex:`, `sha256 pliku:`, `md5 pliku:`, `wyciagnij:`, `posortuj linie:`, `unikalne linie:`, `kwota slownie:`, `sekundy:`, `na sekundy:`, `moc hasla:`, `qr:`, `qr wifi:`. |
| Wygląd | Metryki CPU/RAM/GPU na żywo w pasku bocznym, przełącznik motywu na żywo (ciemny · głęboka czerń · jasny · jak Windows), gradientowa marka, pełne palety jasna i głębokiej czerni. |
| Nawigacja | Nowa pozycja **Narzędzia** w pasku bocznym oraz zakładka 🧰 w Centrum; `//narzedzia` i wpis w palecie Ctrl+K prowadzą w to samo miejsce. |
| Pomoce | `pomoc` i `co nowego` opisują warsztat; wyszukiwanie narzędzi działa też przez typowe słowa kluczowe („diff”, „qr”, „faktura”, „hash”). |

## Jak to jest zrobione (i dlaczego bezpiecznie)

- **Katalog jako jedno źródło prawdy.** `Core/ToolCatalog.cs` opisuje każde narzędzie (kategoria, opis, polecenie, przykład). Każdy wpis został sprawdzony względem routera, więc strona nie może zaproponować czegoś, czego Sentinel nie potrafi.
- **Bez nowych uprawnień.** Uruchomienie narzędzia idzie normalną ścieżką silnika (`IActionEngine`), więc obowiązują te same zasady: STOP awaryjny blokuje uruchomienie, wynik trafia do historii i ma dowód, a polecenia niszczące nadal wymagają zgody.
- **Tylko odczyt albo własny folder danych.** Skrót pliku czyta wskazany plik (bez kopiowania i wysyłania), a kod QR zapisuje PNG w `%LocalAppData%\SentinelX\Qr`. Narzędzia czyste (diff, regex, kwota, czas, moc hasła, porządki w tekście) nie dotykają dysku ani sieci.
- **Hasło nigdy nie wraca na ekran.** `moc hasla:` liczy długość, zestaw znaków i entropię, ale nie powtarza hasła i niczego nie zapisuje.

## Co weryfikuje CI na dokładnie tych plikach, które pobierzesz

1. `scripts/check-architecture.py` — spójność XAML, brak kolorów na sztywno w widokach, 13 stron, cienkie code-behind, granice ViewModeli.
2. Kompilacja i publikacja samodzielnej paczki win-x64 (z wbudowanym .NET i silnikiem AI).
3. **Smoke UI** — renderuje każdą stronę, każdą zakładkę Centrum i cztery motywy; nie toleruje ani jednego błędu wiązania WPF. W tej wersji dokłada sprawdzenie, że katalog ma ponad 60 narzędzi, wyszukiwanie działa, a „kwota słownie: 1234,56” z poziomu strony zwraca poprawną odmianę.
4. **Testy regresji** — w tym nowe asercje warsztatu: diff, regex (także błędny wzorzec), wyciąganie danych, sortowanie i deduplikacja wierszy, kwota słownie w kilku wariantach odmiany, przeliczanie sekund w obie strony, ocena hasła (i brak hasła w odpowiedzi) oraz zapis kodu QR jako prawdziwego PNG.
5. Instalator EXE: kompilacja, instalacja per-user i smoke UI na zainstalowanej aplikacji.

## Ograniczenia, o których warto wiedzieć

- **Skrót pliku** obsługuje pliki do 2 GB — większe są odrzucane z wyjaśnieniem, żeby nie zamrażać komputera.
- **Test regex** ma limit 500 ms i 20 000 znaków tekstu; przy bardziej złożonych wzorcach powie wprost, że przekroczył limit.
- **Kod QR** jest zapisywany lokalnie jako PNG — aplikacja nie pokazuje go jeszcze w oknie; plik otworzysz z folderu danych (albo przez „otworz folder sentinel”).
- **Kwota słownie** obsługuje zakres 0 – 999 999 999,99 zł i polską odmianę; innych walut nie ma.
- **Motyw jasny** jest pełną paletą, ale część drobnych elementów (np. paski postępu) dziedziczy akcent z ustawień — kontrast dla akcentu wybierasz sam w Ustawienia → Wygląd.
- Nadal **brak podpisu cyfrowego**: Windows SmartScreen i Android pokażą ostrzeżenie o nieznanym wydawcy.
