<#
  Pobiera przypięty silnik AI (llama.cpp, CPU x64), sprawdza jego SHA-256 i rozpakowuje do Engine/llama,
  żeby trafił do paczki razem z aplikacją (nie trzeba go wtedy pobierać przy pierwszym uruchomieniu).
  Adres, rozmiar i suma kontrolna są czytane z Services/Engine/EngineCatalog.cs — jedno źródło prawdy.
  Dokłada też biblioteki Visual C++ (app-local), żeby silnik działał na komputerach bez „Redistributable”,
  i uruchamia `llama-server --version`: build kończy się błędem, jeśli silnik w ogóle nie startuje.
#>
param([string]$Destination = (Join-Path $PSScriptRoot '..\Engine\llama'))
$ErrorActionPreference = 'Stop'
$catalog = Get-Content (Join-Path $PSScriptRoot '..\Services\Engine\EngineCatalog.cs') -Raw
$url  = [regex]::Match($catalog, 'LlamaZipUrl\s*=\s*"([^"]+)"').Groups[1].Value
$sha  = [regex]::Match($catalog, 'LlamaZipSha256\s*=\s*"([0-9a-f]{64})"').Groups[1].Value
$size = [long][regex]::Match($catalog, 'LlamaZipSize\s*=\s*(\d+)').Groups[1].Value
if (-not $url -or -not $sha -or $size -le 0) { throw 'Nie udało się odczytać przypiętego silnika z EngineCatalog.cs' }

$destination = [System.IO.Path]::GetFullPath($Destination)
$existing = if (Test-Path $destination) { Get-ChildItem $destination -Recurse -Filter 'llama-server.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 } else { $null }
if ($existing -and (Test-Path (Join-Path $destination 'ENGINE.txt')) -and ((Get-Content (Join-Path $destination 'ENGINE.txt') -Raw) -match $sha)) {
  Write-Output "Silnik AI już jest w $destination (sha $($sha.Substring(0, 12))…)"
} else {
  $zip = Join-Path ([System.IO.Path]::GetTempPath()) ('llama-' + [guid]::NewGuid().ToString('N') + '.zip')
  Write-Output "Pobieram $url"
  Invoke-WebRequest -Uri $url -OutFile $zip -MaximumRetryCount 5 -RetryIntervalSec 5
  $actualSize = (Get-Item $zip).Length
  $actualSha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualSize -ne $size) { throw "Zły rozmiar silnika AI: $actualSize zamiast $size" }
  if ($actualSha -ne $sha) { throw "Zła suma SHA-256 silnika AI: $actualSha zamiast $sha" }
  if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
  New-Item -ItemType Directory -Force $destination | Out-Null
  Expand-Archive -Path $zip -DestinationPath $destination -Force
  Remove-Item $zip -Force
  "llama.cpp (MIT) · $url · sha256 $sha" | Set-Content (Join-Path $destination 'ENGINE.txt') -Encoding utf8
}

$server = Get-ChildItem $destination -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
if (-not $server) { throw 'Paczka silnika AI nie zawiera llama-server.exe' }

# Biblioteki Visual C++ obok silnika (dozwolone „app-local deployment”): bez nich czysty Windows nie uruchomi llama-server.exe.
$system32 = Join-Path $env:windir 'System32'
foreach ($name in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll', 'vcomp140.dll', 'concrt140.dll') {
  $source = Join-Path $system32 $name
  $target = Join-Path $server.DirectoryName $name
  if ((Test-Path $source) -and -not (Test-Path $target)) { Copy-Item $source $target }
}

$output = & $server.FullName --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "llama-server.exe nie uruchamia się (kod $LASTEXITCODE): $output" }
$versionLines = ($output -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -First 3) -join ' | '
Write-Output "llama-server działa: $versionLines"
Write-Output "::notice title=Silnik AI::llama.cpp pobrany, zweryfikowany i uruchomiony (--version) w $($server.DirectoryName)"
