<#
  Buduje powłokę WinUI 3 (Windows App SDK) na własnym komputerze.
  Wymaga .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
  Jeśli jest zainstalowany Inno Setup 6, powstaje też instalator .exe.

      powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1
      powershell -ExecutionPolicy Bypass -File scripts\build-winui.ps1 -Test
#>
param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

& (Join-Path $PSScriptRoot 'fetch-llama.ps1')
dotnet publish SentinelX.WinUI/SentinelX.WinUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o bin/portable-winui
if ($LASTEXITCODE -ne 0) { throw 'Budowanie nie powiodło się — komunikaty błędów są powyżej.' }

if ($Test) {
  $output = Join-Path $PWD 'test-results/winui-local'
  $p = Start-Process 'bin/portable-winui/SentinelX.exe' -ArgumentList '--ui-smoke', $output -PassThru
  if (-not $p.WaitForExit(240000)) { $p.Kill(); throw 'Testy aplikacji przekroczyły czas.' }
  if (Test-Path (Join-Path $output 'SMOKE.txt')) { Get-Content (Join-Path $output 'SMOKE.txt') }
  if ($p.ExitCode -ne 0) { throw "Testy aplikacji nie przeszły (kod $($p.ExitCode)); szczegóły: $output" }
  Write-Output 'Testy aplikacji (WinUI): OK'
}

$compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
[xml]$project = Get-Content SentinelX.WinUI/SentinelX.WinUI.csproj
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (Test-Path $compiler) {
  & $compiler "/DAppVersion=$version" 'installer/SentinelX.WinUI.iss'
  if ($LASTEXITCODE -ne 0) { throw 'Kompilacja instalatora nie powiodła się.' }
  $installer = Get-ChildItem 'bin/installer-winui/*.exe' | Select-Object -First 1
  Write-Output "GOTOWE. Instalator: $($installer.FullName)"
} else {
  Write-Output "GOTOWE. Wersja przenośna: $(Resolve-Path 'bin/portable-winui/SentinelX.exe')"
  Write-Output 'Aby zbudować też instalator .exe, zainstaluj Inno Setup 6 (https://jrsoftware.org/isinfo.php) i uruchom skrypt ponownie.'
}
