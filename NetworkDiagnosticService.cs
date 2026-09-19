using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

public sealed class NetworkDiagnosticService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };

    public Task<ActionExecutionResult> TestInternetAsync() => TestInternetAsync(CancellationToken.None);

    public async Task<ActionExecutionResult> TestInternetAsync(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(9));
        var pingTask = ProbePingAsync("1.1.1.1", budget.Token);
        var dnsTask = ProbeDnsAsync("example.com", budget.Token);
        var httpsTask = ProbeHttpsAsync(budget.Token);
        await Task.WhenAll(pingTask, dnsTask, httpsTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var ping = await pingTask; var dns = await dnsTask; var https = await httpsTask;
        string evidence = $"Czas pomiaru: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n{ping.Evidence}\n{dns.Evidence}\n{https.Evidence}";
        if (https.Success && dns.Success)
            return ActionExecutionResult.VerifiedSuccess("Potwierdzono DNS i połączenie HTTPS z example.com. Wynik dotyczy tych punktów testowych.", evidence);
        if (ping.Success || dns.Success || https.Success)
            return ActionExecutionResult.UnverifiedSuccess("Część testów połączenia działa. Brak ICMP może wynikać z filtrowania; sprawdź szczegóły.", evidence);
        return ActionExecutionResult.Failure("Nie udało się potwierdzić łączności. To nie rozstrzyga, czy cała sieć jest niedostępna.", evidence);
    }

    public async Task<ActionExecutionResult> TestPingAsync(string host, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHost(host, out var normalized)) return ActionExecutionResult.Failure("Podaj samą nazwę hosta lub adres IP, bez portu i ścieżki.");
        var probe = await ProbePingAsync(normalized, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return probe.Success ? ActionExecutionResult.VerifiedSuccess("Host odpowiedział na ICMP.", probe.Evidence)
            : ActionExecutionResult.Failure("Brak potwierdzonej odpowiedzi ICMP; host lub zapora mogą blokować ping.", probe.Evidence);
    }

    public async Task<ActionExecutionResult> TestDnsAsync(string host, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHost(host, out var normalized) || IPAddress.TryParse(normalized, out _))
            return ActionExecutionResult.Failure("Podaj nazwę domeny, np. dns example.com.");
        var probe = await ProbeDnsAsync(normalized, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return probe.Success ? ActionExecutionResult.VerifiedSuccess("Systemowy resolver zwrócił adresy (wynik może pochodzić z pamięci podręcznej).", probe.Evidence)
            : ActionExecutionResult.Failure("Nie udało się rozwiązać nazwy DNS.", probe.Evidence);
    }

    public static bool TryNormalizeHost(string? input, out string host)
    {
        host = (input ?? string.Empty).Trim();
        if (host.Length is 0 or > 253 || host.Any(char.IsWhiteSpace)) return false;
        if (IPAddress.TryParse(host, out var address)) { host = address.ToString(); return true; }
        try { host = new IdnMapping().GetAscii(host.TrimEnd('.')); }
        catch (ArgumentException) { return false; }
        if (host.Length > 253) return false;
        return host.Split('.').All(label => label.Length is > 0 and <= 63 && label[0] != '-' && label[^1] != '-'
            && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    private static async Task<Probe> ProbePingAsync(string host, CancellationToken token)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(4));
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3), cancellationToken: budget.Token).ConfigureAwait(false);
            return new(reply.Status == IPStatus.Success, $"ICMP {host}: {reply.Status}" + (reply.Status == IPStatus.Success ? $", {reply.RoundtripTime} ms" : string.Empty));
        }
        catch (Exception ex) { return new(false, $"ICMP {host}: niedostępny ({FailureReason(ex)})"); }
    }

    private static async Task<Probe> ProbeDnsAsync(string host, CancellationToken token)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(4));
            var timer = Stopwatch.StartNew();
            var addresses = await Dns.GetHostAddressesAsync(host, budget.Token).ConfigureAwait(false);
            return new(addresses.Length > 0, $"DNS {host}: {string.Join(", ", addresses.Take(8).Select(x => x.ToString()))} ({timer.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex) { return new(false, $"DNS {host}: niedostępny ({FailureReason(ex)})"); }
    }

    private static async Task<Probe> ProbeHttpsAsync(CancellationToken token)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://example.com/");
            var timer = Stopwatch.StartNew();
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            bool ok = (int)response.StatusCode is >= 200 and < 400;
            return new(ok, $"HTTPS example.com: HTTP {(int)response.StatusCode}, {timer.ElapsedMilliseconds} ms, poprawna walidacja TLS");
        }
        catch (Exception ex) { return new(false, $"HTTPS example.com: niedostępny ({FailureReason(ex)})"); }
    }

    private static string FailureReason(Exception ex) => ex is OperationCanceledException ? "limit czasu lub anulowanie" : ex.Message;
    private sealed record Probe(bool Success, string Evidence);

    public string GetNetworkSummary()
    {
        var result = new StringBuilder("AKTYWNE INTERFEJSY SIECI\n\n");
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            if (interfaces.Length == 0) return "Brak aktywnego interfejsu sieciowego. To odczyt interfejsów, nie test internetu.";
            foreach (var nic in interfaces.Take(12))
            {
                result.AppendLine(nic.Name);
                try
                {
                    var properties = nic.GetIPProperties();
                    result.AppendLine($"Typ: {nic.NetworkInterfaceType}");
                    result.AppendLine($"Adresy: {string.Join(", ", properties.UnicastAddresses.Select(x => x.Address).Take(8))}");
                    result.AppendLine($"Brama: {string.Join(", ", properties.GatewayAddresses.Select(x => x.Address))}");
                    result.AppendLine($"DNS: {string.Join(", ", properties.DnsAddresses)}");
                    result.AppendLine(nic.Speed > 0 ? $"Szybkość łącza: {nic.Speed / 1_000_000} Mb/s (nie jest pomiarem transferu internetu)." : "Szybkość łącza: nieznana.");
                }
                catch (Exception ex) { result.AppendLine($"Szczegóły niedostępne: {ex.Message}"); }
                result.AppendLine();
            }
            if (interfaces.Length > 12) result.AppendLine($"Pokazano 12 z {interfaces.Length} interfejsów.");
        }
        catch (Exception ex) { return $"Informacje o sieci niedostępne: {ex.Message}"; }
        return result.ToString().Trim();
    }
}
