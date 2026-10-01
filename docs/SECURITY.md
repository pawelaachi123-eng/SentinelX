# Bezpieczeństwo Sentinel X

## Zasady projektowe

1. **Lokalnie-first.** Domyślnie nic nie wychodzi z Twojego PC.
2. **Zgoda użytkownika.** Każda akcja wysokiego ryzyka (zapis pliku, uruchomienie procesu, zdalne sterowanie) wymaga jawnej zgody.
3. **Izolacja AI.** Silnik lokalny (llama.cpp) nie dostaje bezpośredniego dostępu do powłoki systemowej. Wszystko idzie przez warstwę narzędzi z weryfikacją uprawnień.
4. **Bez plaintext secrets.** Tokeny konfiguracyjne, klucze i zapamiętane hasła NIE są zapisywane w eksportach backupu w postaci jawnej.
5. **Wykrycie niebezpiecznego polecenia = ASK.** Jeśli komenda ma niską pewność rozpoznania LUB dotyczy akcji niszczącej, Sentinel pyta zanim wykona.
6. **Phone link.** Połączenie telefon-PC działa tylko w sieci domowej; każde nowe urządzenie wymaga jednorazowej akceptacji na komputerze. Komunikacja po HTTPS z certyfikatem generowanym lokalnie.
7. **Pluginy.** Zewnętrzne DLL nie są ładowane bez podpisu cyfrowego i zgody użytkownika (obecnie unsupported w wersji 0.94-0.97; wbudowane pluginy są safe).
8. **Developer Mode jest oddzielny.** Generowanie i wykonywanie dowolnego kodu działa TYLKO przy włączonym Developer Mode i po uzyskaniu akceptacji dla każdego kroku (search→design→build→test→approval→register).
9. **Safe mode po crashach.** Po 3 crashach z rzędu aplikacja startuje bez pluginów, bez automatyzacji, z włączoną diagnostyką.
10. **Brak automatycznego wznowienia niebezpiecznych operacji.** Po crashu zadanie typu „zapisz plik" lub „uruchom build" NIE jest wznawiane automatycznie.

## Sprawdzone zagrożenia

- Path traversal przy odczycie plików — odrzucamy ścieżki względne `..` i bezwzględne poza wskazanym repozytorium.
- Shell injection w uruchamianiu procesów — używamy `ProcessStartInfo.ArgumentList` / `Arguments` zamiast składni powłoki.
- Nieuwierzytelnione endpointy HTTP — LinkApi wymaga zatwierdzenia urządzenia zanim cokolwiek zrobi.
- Słabe parowanie — telefon otrzymuje jednorazowy kod autoryzacji pokazany na PC.
- Niebezpieczne ustawienia domyślne — autostart, auto-install engine i voice-on-launch są świadomie domyślnie w stanie w jakim są w `SentinelSettings`.

## Jeśli znajdziesz lukę

Nie publikuj jej publicznie. Wyślij opis na adres maintainera przez zakładkę Issues w GitHubie z etykietą `security`.
