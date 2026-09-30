# Tworzenie aplikacji Android w Sentinel X

Sentinel X ma teraz powtarzalny generator trzech małych, lokalnych aplikacji Android: **Counter** (licznik), **Notes** (notatka zachowywana w prywatnych ustawieniach aplikacji) i **Checklist** (lista zadań zachowywana lokalnie). To zatwierdzone szablony, a nie dowolny interpreter poleceń: generator nie uruchamia kodu z promptu i nie daje aplikacji uprawnień sieciowych. Możesz poprosić o nową funkcję/szablon; wtedy zmieniamy i przeglądamy jego źródła w repozytorium.

## Zbuduj APK przez GitHub Actions

To najprostsza droga — nie trzeba instalować Android Studio ani Gradle na komputerze.

1. Otwórz repozytorium SentinelX na GitHubie. Po scaleniu PR zawierającego ten generator przejdź do **Actions**.
2. Wybierz workflow **Generate Android app** i kliknij **Run workflow**.
3. Wybierz szablon `counter`, `notes` albo `checklist`. Wpisz nazwę widoczną pod ikoną (1–32 litery/cyfry/spacje/myślniki/podkreślenia), a następnie uruchom workflow.
4. Poczekaj, aż zadanie **Build debug APK** zakończy się na zielono. Otwórz jego run i pobierz artefakt `android-<szablon>-<run>`. GitHub może poprosić o zalogowanie do Twojego konta.
5. Rozpakuj pobrane ZIP. Zawiera `app-debug.apk`, `source-project.zip` i `SHA256SUMS.txt`. Opcjonalna kontrola na Windows (PowerShell): `(Get-FileHash .\\app-debug.apk -Algorithm SHA256).Hash` — wynik porównaj z linią `app-debug.apk` w `SHA256SUMS.txt`.
6. Przenieś `app-debug.apk` na telefon (np. przewodem USB albo przez własny, zaufany transfer plików) i otwórz go na telefonie.
7. Jeśli Android zapyta, zezwól przeglądarce/plikiom **na instalowanie nieznanych aplikacji**. Zezwalaj tylko dla aplikacji, której używasz do otwarcia APK; po instalacji możesz cofnąć to uprawnienie w Ustawieniach Androida.
8. Potwierdź instalację i uruchom aplikację. Aby usunąć wersję testową, odinstaluj ją z Ustawień telefonu.

**Wymagania i ograniczenia:** APK jest podpisany automatycznym kluczem debugowym Gradle. Jest przeznaczony do testu na własnym urządzeniu, nie jest wydaniem Google Play i nie używa klucza podpisującego użytkownika. Workflow nie publikuje aplikacji ani kodu na Play Store. Domyślnie projekt nie prosi o dostęp do Internetu. Szablony mają różne identyfikatory pakietu, więc mogą być zainstalowane obok siebie. Klucz debugowy jest generowany na runnerze, dlatego przed instalacją kolejnego APK **tego samego szablonu** odinstaluj wcześniejszą kopię (inaczej Android może odmówić aktualizacji z powodu innego podpisu). Do aktualizacji bez odinstalowania i do wydania sklepowego potrzebne jest osobne, trwałe podpisywanie.

Szablon Notes przechowuje tekst w prywatnym magazynie ustawień aplikacji, ale nie szyfruje go samodzielnie — nie wpisuj tam haseł ani danych poufnych.

Workflow można uruchomić dopiero wtedy, gdy plik workflow znajduje się na GitHubie (dla nowego workflow zwykle po scaleniu PR do `main`). Do uruchomienia używasz GitHub Actions UI; Sentinel X nie potrzebuje tokenu GitHub i sam niczego nie publikuje.

## Wygeneruj sam kod źródłowy lokalnie

Python 3.10+ wystarczy; nie uruchamia to Gradle ani kompilatora:

```bash
python scripts/create_mobile_app.py --template counter --app-name "Moja Aplikacja" --output ./MojaAplikacja
```

Obsługiwane szablony: `counter`, `notes`, `checklist`. Katalog wyjściowy musi być pusty; istniejących plików generator nie nadpisuje. Do zbudowania APK lokalnie potrzebne są Java 17, Android SDK 35 i Gradle 8.9; potem w folderze projektu uruchom `gradle --no-daemon :app:assembleDebug`. Bezpośrednio po utworzeniu projektu powstają tylko źródła i metadane.

## Dodawanie nowych rodzajów aplikacji

Szablony Kotlin znajdują się w `scripts/mobile-app-templates/<template>/MainActivity.kt`; wspólna konfiguracja Androida jest w `scripts/mobile-app-templates/base/`. Dodaję nowy wariant jako jawny, testowany szablon (nie dowolny skrypt), dopisuję go do listy w `scripts/create_mobile_app.py` i do wyboru workflow. Po zmianie automat buduje każdy wariant w GitHub Actions. Wygenerowany APK należy samodzielnie sprawdzić przed użyciem.
