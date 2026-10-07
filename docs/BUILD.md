# Budowanie

Windows: `dotnet run --project verification/Build1/Build1.csproj -c Release`; `python scripts/check-architecture.py`; `dotnet publish SENTINEL-X.csproj -c Release -r win-x64 --self-contained true -o bin/portable`.
Na Linuxie kompilacja wymaga `-p:EnableWindowsTargeting=true`; uruchomienie WPF wymaga Windows. Silnik: `pwsh scripts/fetch-llama.ps1` na Windows, SHA-256 przypięty w EngineCatalog. Instalator: Inno Setup 6, `ISCC /DAppVersion=1.0.0 installer/SentinelX.iss`.
Android: JDK 17 + SDK/platform/build-tools 35; `cd phone-android && ./gradlew --no-daemon lintDebug testDebugUnitTest assembleDebug`. Wrapper i dystrybucja mają checksum. Lint błędy zatrzymują build.
Produkcja: SX_ANDROID_KEYSTORE, SX_ANDROID_STORE_PASSWORD, SX_ANDROID_KEY_ALIAS, SX_ANDROID_KEY_PASSWORD ze środowiska. Brak klucza daje unsigned release; debug APK jest wyłącznie verification/debug.
TLS integracja: `python scripts/test-sx4-tls.py` (Python + openssl + javac/java + dotnet). Testuje rzeczywiste klienty C# i Java z lokalnym TLS fixture, bez ESP32.
CI: ci-windows.yml, ci-android.yml, security-check.yml; release.yml czeka na wszystkie trzy. Stare nazwy workflow są ręcznymi wrapperami.
