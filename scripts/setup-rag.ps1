# ============================================================
#  SentinelX — przygotowanie RAG na Twojej maszynie
#  JEDNO polecenie (z katalogu repo):
#    powershell -ExecutionPolicy Bypass -File scripts\setup-rag.ps1
#
#  Co robi:  sprawdza .NET 10 SDK i Ollamę, pobiera przez Ollamę model
#            embeddingów (nomic-embed-text) i model rozmowy (qwen3:4b-instruct).
#  Czego NIE robi: NIE buduje EXE, NIE instaluje niczego w systemie,
#            NIE tworzy plików aplikacji, niczego nie usuwa.
# ============================================================
$ErrorActionPreference = "Stop"

Write-Host "== SentinelX RAG — sprawdzenie srodowiska ==" -ForegroundColor Cyan

function Need-Command($name, $hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        Write-Host "BRAK: $name" -ForegroundColor Red
        Write-Host "      $hint"
        exit 1
    }
}

Need-Command "dotnet" "zainstaluj .NET 10 SDK: https://dotnet.microsoft.com/download"
Need-Command "ollama" "zainstaluj Ollame dla Windows: https://ollama.com/download"

Write-Host ("dotnet:  " + (dotnet --version))
Write-Host ("ollama:  " + (ollama --version))

Write-Host "== Pobieranie modeli przez Ollame (do Twojej lokalnej biblioteki; wymaga internetu TYLKO teraz) ==" -ForegroundColor Cyan
ollama pull nomic-embed-text
ollama pull qwen3:4b-instruct

Write-Host ""
Write-Host "== Gotowe. Kolejne kroki (recznie) ==" -ForegroundColor Green
Write-Host "1) dotnet run                                  # uruchom Sentinela z katalogu repo"
Write-Host "2) w czacie:  rag zbuduj: C:\sciezka\folder\z\plikami\.txt\.md"
Write-Host "3) rag szukaj: twoja fraza                     # albo: rag prompt: twoje pytanie"
Write-Host ""
Write-Host "Prywatnosc: wektory liczy lokalna Ollama (127.0.0.1:11434), baza RAG zyje tylko w RAM"
Write-Host "aplikacji, nic nie jest zapisywane na dysk i nic nie wychodzi poza ten komputer."
