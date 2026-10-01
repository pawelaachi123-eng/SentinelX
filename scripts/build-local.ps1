<#
  Plan B: buduje Sentinel X na własnym komputerze, bez GitHub Actions.
  Wymaga tylko .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
  Jeśli jest zainstalowany Inno Setup 6, powstaje też instalator .exe.

      powershell -ExecutionPolicy Bypass -File scripts\build-local.ps1
      powershell -ExecutionPolicy Bypass -File scripts\build-local.ps1 -Test     # dodatkowo uruchamia testy aplikacji
#>
param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

& (Join-Path $PSScriptRoot 'fetch-llama.ps1')
dotnet publish SENTINEL-X.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o bin/portable
if ($LASTEXITCODE -ne 0) { throw 'Budowanie nie powiodło się — komunikaty błędów są powyżej.' }

if ($Test) {
  $output = Join-Path $PWD 'test-results/local'
  $p = Start-Process 'bin/portable/SentinelX.exe' -ArgumentList '--ui-smoke', $output -PassThru
  if (-not $p.WaitForExit(240000)) { $p.Kill(); throw 'Testy aplikacji przekroczyły czas.' }
  if ($p.ExitCode -ne 0) { throw "Testy aplikacji nie przeszły (kod $($p.ExitCode)); szczegóły: $output" }
  Write-Output 'Testy aplikacji: OK'
}

$compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
[xml]$project = Get-Content SENTINEL-X.csproj
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (Test-Path $compiler) {
  & $compiler "/DAppVersion=$version" 'installer/SentinelX.iss'
  if ($LASTEXITCODE -ne 0) { throw 'Kompilacja instalatora nie powiodła się.' }
  $installer = Get-ChildItem 'bin/installer/*.exe' | Select-Object -First 1
  Write-Output "GOTOWE. Instalator: $($installer.FullName)"
} else {
  Write-Output "GOTOWE. Wersja przenośna: $(Resolve-Path 'bin/portable/SentinelX.exe')"
  Write-Output 'Aby zbudować też instalator .exe, zainstaluj Inno Setup 6 (https://jrsoftware.org/isinfo.php) i uruchom skrypt ponownie.'
}
