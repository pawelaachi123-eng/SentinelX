# Applied only in GitHub Actions Windows runner before dotnet publish.
# Writes startup breadcrumbs for UI smoke and avoids a modal dialog that stalls CI.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8=[Text.UTF8Encoding]::new($false)
function ReplaceOne([string]$text,[string]$from,[string]$to) {
  # Normalize line endings on both sides before exact matching. Windows runners
  # may check out CRLF files while PowerShell here-strings have different endings.
  $text = $text.Replace("`r`n","`n").Replace("`r","`n")
  $from = $from.Replace("`r`n","`n").Replace("`r","`n")
  $to = $to.Replace("`r`n","`n").Replace("`r","`n")
  $count = ([regex]::Matches($text,[regex]::Escape($from))).Count
  if ($count -ne 1) { throw "Source drift (expected one occurrence, found $count): $($from.Trim())" }
  return $text.Replace($from,$to)
}
# Program.cs is already self-contained; instrument only App.xaml.cs and the smoke test.
$appFile=Join-Path $root 'SentinelX.WinUI/App.xaml.cs'
$app=[IO.File]::ReadAllText($appFile).Replace("`r`n","`n")
$app=ReplaceOne $app '            Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(cmd[2]), "data"));' @'
            Directory.CreateDirectory(Path.GetFullPath(cmd[2]));
            File.AppendAllText(Path.Combine(Path.GetFullPath(cmd[2]), "BOOTSTRAP.log"), "app.onlaunched " + DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine);
            Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(cmd[2]), "data"));
'@
$app=ReplaceOne $app '            provider = ServiceLocator.Build(DispatcherQueue.GetForCurrentThread());' @'
            if (smoke) File.AppendAllText(Path.Combine(Path.GetFullPath(cmd[2]), "BOOTSTRAP.log"), "di.begin" + Environment.NewLine);
            provider = ServiceLocator.Build(DispatcherQueue.GetForCurrentThread());
            if (smoke) File.AppendAllText(Path.Combine(Path.GetFullPath(cmd[2]), "BOOTSTRAP.log"), "di.ok" + Environment.NewLine);
'@
$app=ReplaceOne $app '                int code = await WinUiSmokeTest.RunAsync(provider, Path.GetFullPath(cmd[2]));' @'
                File.AppendAllText(Path.Combine(Path.GetFullPath(cmd[2]), "BOOTSTRAP.log"), "smoke.begin" + Environment.NewLine);
                int code = await WinUiSmokeTest.RunAsync(provider, Path.GetFullPath(cmd[2]));
                File.AppendAllText(Path.Combine(Path.GetFullPath(cmd[2]), "BOOTSTRAP.log"), "smoke.return=" + code + Environment.NewLine);
'@
[IO.File]::WriteAllText($appFile,$app,$utf8)

$testFile=Join-Path $root 'SentinelX.WinUI/Utilities/WinUiSmokeTest.cs'
$t=[IO.File]::ReadAllText($testFile).Replace("`r`n","`n")
$from3=@'
            Directory.CreateDirectory(outputDirectory);
            var engine = provider.GetRequiredService<IActionEngine>();
'@
$to3=@'
            Directory.CreateDirectory(outputDirectory);
            void Stage(string name) => File.AppendAllText(Path.Combine(outputDirectory, "SMOKE-STAGES.log"), name + Environment.NewLine);
            Stage("resolve.engine.begin");
            var engine = provider.GetRequiredService<IActionEngine>();
            Stage("resolve.engine.ok");
'@
$t=ReplaceOne $t $from3 $to3
$from4=@'
            monitor.Start();
            IntentResult result = await engine.ExecuteAsync("wersja");
'@
$to4=@'
            Stage("monitor.start.begin");
            monitor.Start();
            Stage("monitor.start.ok");
            Stage("command.wersja.begin");
            IntentResult result = await engine.ExecuteAsync("wersja");
            Stage("command.wersja.ok");
'@
$t=ReplaceOne $t $from4 $to4
$t=ReplaceOne $t '            File.WriteAllText(Path.Combine(outputDirectory, "SMOKE.txt"), report.ToString());' @'
            Stage("report.write.begin");
            File.WriteAllText(Path.Combine(outputDirectory, "SMOKE.txt"), report.ToString());
            Stage("report.write.ok");
'@
[IO.File]::WriteAllText($testFile,$t,$utf8)
Write-Host 'Added startup breadcrumbs and non-modal bootstrap error handling for smoke.' -ForegroundColor Green
