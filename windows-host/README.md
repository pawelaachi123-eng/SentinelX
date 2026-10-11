# SENTINEL X · Nexus host (WinUI 3 + WebView2)

> Ten kod **nie był kompilowany** (środowisko Linux). Zweryfikuj na Windows.

## Pliki
Skopiuj `SentinelX.WinUI/UI/Nexus/*.cs` do repozytorium `SentinelX.WinUI/UI/Nexus/`.
WebView2 jest częścią `Microsoft.WindowsAppSDK` — nowe pakiety nie są potrzebne.

## Rejestracja (Core/ServiceLocator.cs)
```csharp
services.AddTransient<SentinelX.WinUI.UI.Nexus.NexusWindow>();
```

## Uruchomienie (App.xaml.cs, w OnLaunched zamiast ShellWindow)
```csharp
Window main = Environment.GetEnvironmentVariable("SENTINEL_UI") == "classic"
    ? provider.GetRequiredService<ShellWindow>()
    : provider.GetRequiredService<SentinelX.WinUI.UI.Nexus.NexusWindow>();
main.Activate();
```

## Interfejs webowy
- Dev: `SENTINEL_NEXUS_URL=https://<twój-preview>` — WebView2 ładuje wskazany URL.
- Produkcja: `cd frontend && yarn build`, skopiuj `build/*` do `<bin>/nexus/` (mapowane jako `https://nexus.sentinel/`).

## Kontrakt zdarzeń (`window.chrome.webview`)
| Kierunek | type | data |
|---|---|---|
| host → UI | `core.state` | `{state: idle/listening/recognizing/thinking/executing/success/warning/error/offline, detail, task}` |
| host → UI | `host.audio` | `{level}` 0..1 (natywny mikrofon) |
| host → UI | `host.visibility` | `{visible}` |
| UI → host | `command.submit` (z `id`) | `{text, fromVoice}` → odpowiedź `host.result {text}` lub `host.error {error}` |
| UI → host | `command.cancel` | — |
| UI → host | `host.hello` (z `id`) | → `host.info {host, state}` |

Gdy UI działa w hoście, pole komend wysyła polecenia do natywnego `CommandExecutor`
(prawdziwe akcje na PC), a stan rdzenia pochodzi z `CoreStateService`.
