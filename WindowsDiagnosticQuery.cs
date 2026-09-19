using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

internal enum DiagnosticQuery { Security, Services, Events }
internal sealed record DiagnosticQueryResult(bool Available, string Json, string Error);

/// <summary>Only fixed read-only scripts are accepted. Never interprets user input as PowerShell.</summary>
internal static class WindowsDiagnosticQuery
{
    private const int MaxOutputCharacters = 192 * 1024;

    internal static async Task<DiagnosticQueryResult> RunAsync(DiagnosticQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new(false, "", "Ta diagnostyka wymaga Windows.");
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable)) return new(false, "", "Nie znaleziono Windows PowerShell.");
        string script = "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false);\n" + query switch
        {
            DiagnosticQuery.Security => SecurityScript,
            DiagnosticQuery.Services => ServicesScript,
            DiagnosticQuery.Events => EventsScript,
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if (!process.Start()) return new(false, "", "Nie udało się uruchomić zapytania.");
            Task<string> output = ReadBoundedAsync(process.StandardOutput, budget.Token);
            Task<string> errors = ReadBoundedAsync(process.StandardError, budget.Token);
            // A faulted reader must interrupt a child that could be blocked on a full pipe.
            Task completed = Task.WhenAll(process.WaitForExitAsync(budget.Token), output, errors);
            await completed.WaitAsync(budget.Token).ConfigureAwait(false);
            string json = await output;
            string error = await errors;
            if (process.ExitCode != 0) return new(false, "", Limit(error.Length > 0 ? error : $"Kod procesu {process.ExitCode}.", 800));
            if (string.IsNullOrWhiteSpace(json)) return new(false, "", "Zapytanie nie zwróciło danych.");
            return new(true, json.Trim(), string.Empty);
        }
        catch (OperationCanceledException)
        {
            StopOwnedProcess(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, "", "Przekroczono limit 20 sekund. Stan nie został ustalony.");
        }
        catch (Exception ex)
        {
            StopOwnedProcess(process);
            return new(false, "", Limit(ex.Message, 800));
        }
        finally { StopOwnedProcess(process); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        char[] buffer = new char[4096];
        int count;
        // Continue draining after the cap so a noisy child cannot deadlock on its output pipe.
        bool exceeded = false;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            int remaining = MaxOutputCharacters - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(remaining, count));
            if (count > remaining) exceeded = true;
        }
        if (exceeded) throw new InvalidDataException("Przekroczono limit odpowiedzi diagnostycznej.");
        return result.ToString();
    }

    private static void StopOwnedProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
    internal static string Limit(string? text, int length) => string.IsNullOrEmpty(text) ? string.Empty : text.Length <= length ? text : text[..length] + "…";

    private const string SecurityScript = """
        $result = [ordered]@{}
        try {
          $mp = Get-MpComputerStatus
          $result.Defender = [ordered]@{
            Available=$true; AntivirusEnabled=$mp.AntivirusEnabled; RealTimeProtectionEnabled=$mp.RealTimeProtectionEnabled
            BehaviorMonitorEnabled=$mp.BehaviorMonitorEnabled; IoavProtectionEnabled=$mp.IoavProtectionEnabled
            IsTamperProtected=$mp.IsTamperProtected; AMRunningMode=$mp.AMRunningMode
            AntivirusSignatureVersion=$mp.AntivirusSignatureVersion; AntivirusSignatureAge=$mp.AntivirusSignatureAge
            AntivirusSignatureLastUpdated=([string]$mp.AntivirusSignatureLastUpdated)
            QuickScanEndTime=([string]$mp.QuickScanEndTime)
          }
        } catch { $result.Defender = @{Available=$false; Error=$_.Exception.Message} }
        try {
          $profiles = @(Get-NetFirewallProfile | ForEach-Object { [ordered]@{
            Name=$_.Name; Enabled=([string]$_.Enabled); DefaultInboundAction=([string]$_.DefaultInboundAction); DefaultOutboundAction=([string]$_.DefaultOutboundAction)
          } })
          $result.Firewall = @{Available=$true; Profiles=$profiles}
        } catch { $result.Firewall = @{Available=$false; Error=$_.Exception.Message} }
        $result | ConvertTo-Json -Depth 6 -Compress
        """;

    private const string ServicesScript = """
        $all = @(Get-CimInstance Win32_Service -OperationTimeoutSec 12)
        $important = @('WinDefend','WdNisSvc','mpssvc','wuauserv','BITS','EventLog','Winmgmt','Dhcp','Dnscache','WSearch','SysMain','Spooler','w32time')
        $autoStopped = @($all | Where-Object { $_.StartMode -eq 'Auto' -and $_.State -ne 'Running' })
        [ordered]@{
          Total=$all.Count; Running=@($all | Where-Object State -eq 'Running').Count
          AutoStoppedCount=$autoStopped.Count
          Important=@($all | Where-Object {$_.Name -in $important} | Sort-Object Name | Select-Object Name,DisplayName,State,StartMode)
          AutoStopped=@($autoStopped | Sort-Object Name | Select-Object -First 25 Name,DisplayName,State,StartMode)
        } | ConvertTo-Json -Depth 5 -Compress
        """;

    private const string EventsScript = """
        $logs = @()
        foreach ($log in @('System','Application')) {
          try {
            $events = @(Get-WinEvent -FilterHashtable @{LogName=$log; Level=@(1,2); StartTime=(Get-Date).AddHours(-48)} -MaxEvents 20 -ErrorAction Stop | ForEach-Object {
              $message = [string]$_.Message
              if ($message.Length -gt 600) { $message = $message.Substring(0,600) + '...' }
              [ordered]@{Time=$_.TimeCreated.ToString('o'); Id=$_.Id; Level=$_.LevelDisplayName; Provider=$_.ProviderName; Message=$message}
            })
            $logs += @{Name=$log; Available=$true; Entries=$events}
          } catch {
            if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { $logs += @{Name=$log; Available=$true; Entries=@()} }
            else { $logs += @{Name=$log; Available=$false; Error=$_.Exception.Message} }
          }
        }
        @{Hours=48; MaxPerLog=20; Logs=$logs} | ConvertTo-Json -Depth 6 -Compress
        """;
}
