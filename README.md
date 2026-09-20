# SENTINEL X — Windows / MVVM

Lokalny asystent Windows 10/11, C# 13, .NET 9, WPF. Nowe GUI jest domyślnym interfejsem. Poprzedni interfejs i jego funkcje nadal są dostępne przez `--legacy`.

## Uruchomienie

Wymagania: **Windows x64**, .NET **9 Desktop Runtime** dla skompilowanej aplikacji albo **.NET 9 SDK** do budowania. Nie uruchamiaj WPF na Linuxie ani przez przeglądarkę.

```powershell
dotnet restore SENTINEL-X.csproj
dotnet build SENTINEL-X.csproj -c Release
dotnet run --project SENTINEL-X.csproj -c Release
```

Wersja samodzielna (nie wymaga instalacji runtime na komputerze docelowym):

```powershell
dotnet publish SENTINEL-X.csproj -c Release -r win-x64 --self-contained true -o bin/publish
.\bin\publish\SentinelX.exe
```

Zachowany interfejs zgodności:

```powershell
.\bin\Release\net9.0-windows\SentinelX.exe --legacy
```

Najpierw zakończ działającą instancję przez **Wyjdź z aplikacji** / menu zasobnika. Aplikacja jest pojedynczą instancją; drugie uruchomienie aktywuje pierwsze okno. Zwykły przycisk X domyślnie chowa je do zasobnika.

## Pierwsze użycie

1. **Command Center**: wpisz `ile mam RAM`, `użycie CPU`, `top procesy`, `uruchom kalkulator` lub `test internetu`.
2. **AI**: zainstaluj i uruchom lokalną [Ollama](https://ollama.com/), np. `ollama pull qwen3:4b`, następnie **Sprawdź połączenie** i wybierz model. Połączenie jest celowo ograniczone do `127.0.0.1:11434` na komputerze użytkownika. Przy braku Ollama narzędzia lokalne nadal działają.
3. **Voice**: wybierz mikrofon. Jeśli nie masz lokalnych modeli, kliknij **Pobierz modele i włącz głos** (Whisper Small ~488 MB plus VAD). Pobieranie jest jawne, nie odbywa się przy starcie. Powiedz „Sentinel”, aby przejść ze STANDBY do ACTIVE. Kalibracja wymaga dwóch sekund ciszy.
4. **Settings**: wyszukaj parametr, zmień go i kliknij **Zapisz** przy danym polu. Niepoprawna wartość nie jest zapisywana. Możesz przywrócić pojedynczą sekcję.
5. **Gaming**: wykrywanie gry działa automatycznie. Overlay włączysz przyciskiem. Możesz przeciągać go myszą. Nie zmieniamy trybu gry Windows ani priorytetów procesów.
6. **Ctrl+Shift+X** lub czerwony **EMERGENCY STOP** anuluje zadania, wyłącza głos i blokuje nowe akcje. **Wznów Sentinel** odblokowuje akcje, ale nie włącza mikrofonu. **Ctrl+Shift+S** pokazuje okno.

### Przykłady pracy z plikami

```text
utwórz plik notatka.txt: Treść mojej notatki
dopisz do niego Kolejna linia
pokaż ten plik
skopiuj ten plik jako kopia.txt
przenieś ten plik jako nowa-nazwa.txt
znajdź plik notatka
```

Edycja, kopiowanie i zmiana nazwy dotyczą ostatniego pliku utworzonego przez asystenta. Nie nadpisują istniejących celów. Edycja zachowuje `.bak`; zapis/kopia mają weryfikację treści i SHA-256. Wyszukiwanie jest ograniczone do katalogu `CreatedFiles`, a nie całego komputera. Anulowanie **nie cofa** ukończonych operacji.

## Architektura i dane

- `Core/ServiceLocator.cs`: walidowany kontener DI, wspólne instancje serwisów i zachowywane przy nawigacji ViewModele.
- `ViewModels/`: `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`. Brak operacji plikowych, zapytań systemowych i `Process.Start` w ViewModelach.
- `Views/`: osiem stron, shell i overlay. Code-behind ograniczony do inicjalizacji widoku i przeciągania okna.
- `Services/`: interfejsy oraz integracja z istniejącymi, sprawdzanymi regresyjnie serwisami. Routing najpierw wybiera deterministyczne narzędzia, potem istniejący adaptacyjny transport AI.
- `Themes/`: zasoby kolorów, własne style kontrolek i Fluent .NET 9; Dark, Deep Dark i paleta systemowa. Animacje przejść są wyłączone podczas gry.
- `Models/Settings/`: istniejący format ustawień zachowany bez duplikowania schematu. Migracja starego JSON, walidacja, zapis przez plik tymczasowy i kopia zapasowa.
- Starsze pliki w katalogu głównym są celowo zachowane: część to współdzielone serwisy, część to interfejs zgodności.

Dane pozostają w `%LOCALAPPDATA%\SentinelX\`: `Settings`, `History`, `Memory`, `Logs`, `CreatedFiles` oraz katalogi modeli używane przez istniejący manager. Testy używają odizolowanego `SENTINEL_DATA_DIR`.

### Świadome odstępstwa od master prompta

- Zachowano **wersję 0.83**, która była już w repozytorium, zamiast obniżać ją do 0.77.
- **OllamaSharp 5.4.18**, nie 4.0.6. Test uruchomienia wykrył `MissingMethodException`: 4.0.6 wymaga preview `IChatClient`, podczas gdy istniejący Whisper.net 1.9.1 używa stabilnego `Microsoft.Extensions.AI.Abstractions` 10.2.0. Nie można załadować obu ABI do tej samej aplikacji. OllamaSharp obsługuje odkrywanie modeli, a istniejący transport HTTP zachowuje testowane strategie timeout/retry/fallback czatu.
- **NAudio 2.2.1**, CommunityToolkit.Mvvm **8.4.2**, DI **9.0.0**; istniejące biblioteki Whisper/Sherpa pozostają.
- Zasobnik korzysta z istniejącego **natywnego Shell_NotifyIcon**, bez `UseWindowsForms`, WPF-UI i H.NotifyIcon. Rozwiązuje to sprzeczność „bez WinForms” vs „NotifyIcon WinForms” w prompcie.
- Zakresy ustawień głosu zachowują bezpieczne limity obecnego pipeline, zamiast wpisywać niewspierane przez niego wartości.

## Zakres i ograniczenia — nie utożsamiaj builda z testem sprzętu

| Obszar | Implementacja / ograniczenie |
|---|---|
| MVVM shell | 8 rzeczywistych stron, stan zachowany przy nawigacji, DI, obsługa błędów i pustych danych |
| System | CPU/RAM z WinAPI, GPU z liczników Windows, dyski, sieć, top procesów; niedostępny pomiar jest jawny |
| VRAM | **Niedostępne** — nie podajemy fikcyjnego zera ani wartości z demonstracji |
| AI | Lokalna Ollama, lista modeli, wybór, istniejący adaptacyjny czat; **brak autonomicznego wykonywania dowolnych narzędzi przez LLM** |
| Głos | Istniejący capture/VAD/ASR + MVVM, RMS/SNR/gain, kalibracja, wake word, TTS; detekcja VAD jest flagą, nie zmyślonym confidence |
| Akcje | Jedna kolejka wykonawcza, zgody na zamknięcie aplikacji, anulowanie, historia, dowody; tekst modelu `VERIFIED` nie daje statusu Verified |
| Pliki | Bezpieczny workspace, create/edit/copy/move/search, kopie przed edycją; brak ogólnego edytora dowolnych ścieżek i automatycznego rollbacku |
| Gaming | Wykrywanie istniejącej listy gier, wolniejsze pomiary, lżejszy model, overlay; bez udawanego pomiaru FPS/latencji gry |
| Watch | Alerty długotrwałego CPU/RAM z czasem oczekiwania i cooldownem |
| Tray/autostart | Natywna integracja, close-to-tray, rejestr HKCU, hotkeys; wymagają ręcznego sprawdzenia na desktopie użytkownika |
| Zaawansowane funkcje starego GUI | Kreator programów, dodatkowe komendy `..`, timery i szczegółowe panele diagnostyczne pozostają w `--legacy`; nie wszystkie mają odpowiednik w nowym shellu |
| Modyfikacja źródeł przez AI | **Niezaimplementowana**, domyślnie niedozwolona; nie wystawiamy pozornego działającego przełącznika |

## Testowanie

```powershell
# Windows: prawdziwe widoki, bindingi, motywy, backend i PNG wszystkich stron
.\bin\Release\net9.0-windows\SentinelX.exe --ui-smoke "$PWD\test-results\ui"
# Dotychczasowy zestaw testów regresji, nie korzysta z prawdziwej Ollama
.\bin\Release\net9.0-windows\SentinelX.exe --self-test "$PWD\test-results\regression"
# Ręcznie, po instalacji modeli i polskiego głosu Windows
.\bin\Release\net9.0-windows\SentinelX.exe --asr-test "$PWD\test-results\asr"
```

`--ui-smoke` nie modyfikuje autostartu, nie rejestruje skrótów, nie instaluje modeli i nie uruchamia mikrofonu. Sprawdza renderowanie stron i błędy bindingów, STOP/resume, zakaz potwierdzania głosem, konkurencję/anulowanie zadań, odrzucenie fałszywego „VERIFIED”, zapis ustawień i bezpieczeństwo plików.

Workflow **Windows build and WPF smoke** w `.github/workflows/windows-build.yml` uruchamia restore, build, nowy smoke test i istniejący zestaw regresji. Zachowuje raporty/PNG w `sentinel-validation` oraz aplikację w `SentinelX-windows-x64`. Wynik bieżącego workflow jest źródłem prawdy o kompilacji — nie sama obecność plików.

Ręczne testy wymagane przed uznaniem całości master prompta za DONE:

- [ ] Mikrofon użytkownika, cisza, szum, odległa mowa, odłączenie urządzenia, wake word i timeout.
- [ ] Ollama z faktycznie zainstalowanymi modelami, obciążenie i anulowanie generacji.
- [ ] Tray, konflikt globalnego hotkey, druga instancja, wylogowanie, autostart po ponownym logowaniu.
- [ ] CS2, GPU producenta użytkownika, wydajność monitorowania, overlay na kilku ekranach/DPI.
- [ ] Ręczna ocena wszystkich interakcji GUI i wymaganych funkcji jeszcze dostępnych tylko w `--legacy`.
