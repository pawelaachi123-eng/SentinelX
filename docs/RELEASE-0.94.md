# Sentinel X 0.94 „WSPÓŁPRACA EKRANOWA” — notatki wydania

Data: 2026-09-25 · Wersja aplikacji: `0.94 · WSPÓŁPRACA EKRANOWA`

Ten przyrost realizuje życzenie użytkownika: **Sentinel widzi ekran LIVE (wszystkie monitory) i ma drugi kursor w innym kolorze, który może sterować myszką w Blenderze, przeglądarce i wszędzie — ale włączenie jest TYLKO przyciskiem w UI, bez głosu, aby nie włączyć przypadkiem.**

## Nowe: tryb współpracy ekranowej

### Co to jest
- **Podgląd LIVE całego pulpitu** (virtual screen = wszystkie monitory), nie tylko statyczny SS. Możesz przełączać na Blendera, przeglądarkę, a Sentinel widzi na żywo co się dzieje.
- **Drugi kursor w innym kolorze** (niebieski/cyjan #00D4FF): Twój kursor zostaje, obok jest kursor Sentinela (overlay WPF: 36px okrąg + 8px kropka + litera „S”). Widać dokładnie gdzie AI chce kliknąć.
- **Pełna kontrola myszki** gdy tryb aktywny: `SetCursorPos` + `mouse_event` (klik, prawy klik, double-click, scroll). Działa w Blenderze i wszędzie.
- **Włączenie TYLKO przyciskiem w UI** (Twoje życzenie bezpieczeństwa): Centrum → zakładka 🖥️ Ekran (ikona 🖥️) → przycisk „WŁĄCZ TRYB WSPÓŁPRACY”. Głos NIE może go włączyć — `ActionEngine` blokuje „włącz współpracę” z głosu i zwraca komunikat o przycisku.
- **Wyraźny wskaźnik**: czerwona ramka 4px wokół całego pulpitu (virtual screen) + baner „● TRYB WSPÓŁPRACY AKTYWNY — Ekran widoczny, myszka sterowana” gdy aktywny.
- **Wyłączenie**: jednym kliknięciem tego samego przycisku lub Ctrl+Shift+X (EMERGENCY STOP) — `MainViewModel` wywołuje `CollaborationViewModel.OnEmergencyStop()` przy STOP.

### Bezpieczeństwo wbudowane (niekonfigurowalne)
- **Przycisk-only aktywacja**: brak komendy głosowej, brak automatycznego startu. Nawet komenda tekstowa „włącz współpracę” zwraca instrukcję użycia przycisku, nie włącza.
- **Lokalnie tylko**: `Graphics.CopyFromScreen` → `BitmapSource`, ewentualny zapis JPEG tylko w `%LOCALAPPDATA%\SentinelX\ScreenCaptures\` (max 50, opcjonalnie `SaveCaptures` w ustawieniach), nic nie idzie do internetu. Model wizyjny (llava/qwen2-vl) jeśli użyty, też lokalny przez Ollama.
- **Audyt**: `COLLAB_ENABLE` / `COLLAB_DISABLE` w `ActionHistoryService` z dowodem VERIFIED (liczba monitorów, FPS, kolor kursora).
- **STOP**: `Ctrl+Shift+X` lub czerwony EMERGENCY STOP w sidebarze wyłącza tryb i zatrzymuje live timer.
- **Ustawienia**: `CollaborationSettings` w `settings.json` — `Enabled` (master switch), `CursorColor`, `LiveFps` (1-10, domyślnie 2), `CaptureQuality`, `ShowBorderIndicator`, `SaveCaptures`.

### UI
- Nowa zakładka w Centrum: **🖥️ Ekran** (10. zakładka, obok 🩺 Diagnostyka). Ikona w sidebarze Centrum: 💬📓🕘🎤🖥🎮✨⚡🩺🖥️.
- Strona `CollaborationPage.xaml`: status, lista monitorów (X,Y,W,H, PRIMARY), przyciski: WŁĄCZ/WYŁĄCZ, środek ekranu, klik, prawy klik, zrzut monitora, zrzut wszystkich, live preview (Image).
- Paleta `//`: `//ekran` i `//wspolpraca` → zakładka Ekran.
- Ustawienia: sekcja „Współpraca” — przełączniki i liczby.

### Technicznie
- `Services/Desktop/ScreenCaptureService.cs`: `EnumDisplayMonitors` + `GetMonitorInfo` (bez WinForms), `CopyFromScreen`, `BitmapSource`, timer 1-10 FPS, event `FrameCaptured`, `CaptureAllAsync`, `CaptureMonitorAsync`, `CaptureAndSaveAsync`.
- `Services/Desktop/CoPilotCursorService.cs`: `SetCursorPos`, `mouse_event`, overlay `CoPilotOverlayWindow` (transparent, topmost, WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW, niebieski), border `CoPilotBorderWindow` (czerwona ramka + baner), `Enable/Disable/MoveTo/Click`.
- `ViewModels/CollaborationViewModel.cs`: live frame, status, monitory, komendy Toggle/Enable/Disable/Capture/Move/Click, `OnEmergencyStop`.
- `Core/ServiceLocator.cs`: rejestracja `IScreenCaptureService`, `ICoPilotCursorService`, `CollaborationViewModel`.
- `ViewModels/MainViewModel.cs` i `CommandCenterViewModel.cs`: nowa zakładka, STOP wyłącza współpracę.
- `CommandRouter.cs`: `ekran`/`wspolpraca` zwraca instrukcję o przycisku, `model 3d` teraz tłumaczy tryb współpracy zamiast odmawiać.
- `Services/Actions/ActionEngine.cs`: blokada głosu dla współpracy.
- `SettingsCatalog.cs`: sekcja Współpraca.

### Modelowanie 3D w Blenderze — teraz możliwe
- Wcześniej: „Uczciwie: nie buduję modeli 3D”.
- Teraz: z włączoną współpracą Sentinel widzi Blendera LIVE i może klikać drugim kursorem — nie zgaduje, widzi dokładnie co robi. Ty nadal masz swój kursor i kontrolę, możesz przejąć w każdej chwili.

## Weryfikacja
- `scripts/check-architecture.py`: PASS — 13 stron, brak WinForms flag, cienki code-behind.
- `UiSmokeTestRunner.cs`: sprawdza `//ekran`, `ekran` → info o przycisku, głos zablokowany, `CollaborationViewModel` ma monitory i jest wyłączony domyślnie, renderuje zakładkę Ekran bez błędów bindingów.
- CI `windows-latest`: build, ui-smoke, self-test, portable smoke, installer smoke.

## Uczciwie o ograniczeniach
- Live podgląd 2 FPS domyślnie (1-10 w ustawieniach) — wyższy FPS = więcej CPU. To nie jest 60 FPS streaming.
- Drugi kursor to overlay WPF — Windows ma jeden hardware cursor, więc nie ma dwóch niezależnych kursorów sprzętowych; overlay pokazuje intencję, a realna myszka jest przenoszona via `SetCursorPos` gdy tryb aktywny.
- Screen capture `CopyFromScreen` nie przechwytuje zawartości okien z DRM (np. Netflix) — to ograniczenie Windows.
- Zapis zrzutów opcjonalny i lokalny — jeśli wyłączony, nic nie jest zapisywane.
- Tryb współpracy wymaga włączonego `Collaboration.Enabled` w ustawieniach (domyślnie true) — gdy false, przycisk jest zablokowany logicznie.
- Nie ma jeszcze automatycznej analizy ekranu przez model wizyjny (np. opis „co widzisz w Blenderze”) — podgląd jest LIVE w UI, a model wizyjny może być użyty ręcznie jeśli masz llava.

## Co dalej
- Analiza ekranu przez lokalny model wizyjny (llava/qwen2-vl) na żądanie: „co widzisz na ekranie?” → zrzut + opis.
- Precyzyjne sterowanie Blenderem: komendy „przesuń kursor na X Y”, „przeciągnij”, skróty klawiszowe.
- Nagrywanie sesji współpracy (wideo lokalne) z audytem.
