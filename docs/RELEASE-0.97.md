# SENTINEL X 0.97 · AURORA — release notes

Data wydania: 2026-10-02 · nazwa kodowa: **AURORA**

Główny cel tego wydania: **przeprojektowany interfejs** — ciemniejsza paleta nocy polarnej z akcentami cyjanu, fioletu i róży, szklane karty z efektem aurory, lepsze bąbelki czatu, nowe animacje i spójność stylistyczna w całej aplikacji.

## Co nowego w UI

- **Paleta Aurora** — głębsza czerń bazowa (`#07090F`), jaśniejszy akcent cyjan (`#22E0FF`), fiolet (`#A878FF`), mięta (`#4BEFB9`) i róż (`#FF6B9D`).
- **Szklane karty** (`SxGlassCard`) z gradientową przezroczystością i subtelną poświatą dla kart metryk, czatu, głosu i systemu.
- **Efekty aurory w tle** — trzy nakładające się radialne gradienty (cyjan / fiolet / róż) za panelem treści, nigdy nie przechwytują myszy.
- **Przeprojektowany pasek boczny**:
  - Nowy kafelek marki z gradientowym logo „◈” i świecącym cieniem.
  - Kafelek „NA ŻYWO” z trzema metrykami CPU/RAM/GPU w kolorach akcentów.
  - Przełącznik motywu w nowych kafelkach z poświatą przy zaznaczeniu.
  - Badge statusu głosu z lepszym paddingiem i typografią.
  - Przycisk EMERGENCY STOP z czerwonym gradientem i cieniem.
- **Centrum dowodzenia** (`CommandCenterPage`):
  - Tytuł w gradiencie „Centrum dowodzenia”.
  - Cztery karty metryk w szklanym stylu z kolorowymi kropkami statusu i własnym paskiem postępu.
  - **Nowe bąbelki czatu**: użytkownik po prawej w cyjanowo-niebieskim gradiencie z poświatą, asystent po lewej w ciemnym szkle z zaokrąglonymi narożnikami w stylu iMessage/WhatsApp.
  - Etykieta ról w badge'u: „TY” (użytkownik) / „SENTINEL” (asystent) z odpowiednimi kolorami.
  - Animowany pulsujący wskaźnik „generowanie”.
  - Panel zgód z ikoną ostrzeżenia i wyraźniejszymi przyciskami.
  - Pasek wejścia z ikoną mikrofonu, gradientowym przyciskiem Wyślij i oddzielonym przyciskiem Stop.
  - Szybkie akcje w formie chipów (pill).
- **Strona Głosu**: szklane karty z ikonami gradientowymi, lepsze metryki (Noise/SNR/Gain) w osobnych kartach.
- **Strona Systemu**: cztery hero-metryki z paskami postępu, dyski w kafelkach, tabela procesów z naprzemiennymi wierszami.
- **Wszystkie strony** mają ujednolicony margines 32 px i tytuł w gradiencie akcentu.

## Techniczne

- Wszystkie kontrolki (przyciski, przełączniki, paski postępu, ComboBox, Slider, ScrollBar, DataGrid, ToolTip) mają zaktualizowane szablony z subtelnymi cieniami i poświatą przy najechaniu/zaznaczeniu.
- Przycisk główny (`SxPrimaryButton`) używa nowego gradientu `SxSendGradient` i świecącego cienia.
- Przycisk niebezpieczny (`SxDangerButton`) używa gradientu czerwonego.
- Nowe storyboardy w `Themes/Animations.xaml`: `SxSlideUp`, `SxPulse`.
- Nowy zasób czcionki mono (`SxMonoFamily`) dla tekstów konsolowych.
- Nowe brush'y: `SxAuroraBackground`, `SxSidebarGradient`, `SxGlassGradient`, `SxSendGradient`, `SxDangerGradient`, `SxVoiceMeterGradient`, `SxHeaderGlow`, `SxAuroraPanel`, `SxAccentCyanSoft`.
- Wersja zwiększona do **0.97.0** w `SENTINEL-X.csproj` i w stałej `AppConstants.Version` (zmieniona z „0.96 · KUŹNIA” na „0.97 · AURORA”).
- Zaktualizowany tytuł okna i wersja w interfejsie zgodności (legacy) również na 0.97 Aurora.

## Kompatybilność

- Wszystkie powiązania (`Binding`), `x:Name`, komendy (`ICommand`) i szablony danych (`DataTemplate`) pozostały bez zmian — żadna funkcja nie zniknęła.
- Interfejs zgodności (uruchamiany `--legacy`) nadal jest dostępny, ma tylko zaktualizowane numery wersji.
- Motywy Ciemny / Głęboko Czarny / Jasny / System dalej działają; ciemny motyw zyskał bogatszy wygląd aurory.

## Uruchomienie

```powershell
# Wymaga .NET 10 SDK x64 na Windows
powershell -ExecutionPolicy Bypass -File scripts\build-local.ps1 -Test
```

Albo pobierz gotowy instalator `SentinelX-0.97.0-win-x64-setup.exe` z zakładki Releases.
