using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

public sealed record DiagnosticSection(string Id, string Title, string Status, string Content)
{
    public string Format() => $"{Title.ToUpperInvariant()}\nStan odczytu: {Status}\n\n{Content}";
}

public sealed record DiagnosticReport(int SchemaVersion, DateTimeOffset CapturedAt, IReadOnlyList<DiagnosticSection> Sections)
{
    public string ToMarkdown()
    {
        var text = new StringBuilder($"# Sentinel X — raport diagnostyczny\n\nUtworzono: {CapturedAt:yyyy-MM-dd HH:mm:ss zzz}\n\n" +
            "Raport jest lokalnym odczytem stanu, nie certyfikatem bezpieczeństwa. Nie zmienia ustawień. Zawiera nazwy urządzeń, lokalne adresy IP, nazwy aplikacji i zdarzenia; sprawdź treść przed udostępnieniem.\n\n");
        foreach (var section in Sections)
        {
            // Treat diagnostic output as literal content, including event messages and registry values.
            text.AppendLine($"## {section.Title}\n\nStan odczytu: {section.Status}\n");
            foreach (string line in section.Content.Replace("\r", "").Split('\n')) text.AppendLine("    " + line);
            text.AppendLine();
        }
        return text.ToString();
    }
}

/// <summary>Local, read-only diagnostics with explicit unavailable and partial states.</summary>
public sealed class PcDiagnosticService
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public async Task<DiagnosticSection> GetSecurityAsync(CancellationToken cancellationToken = default)
    {
        var query = await WindowsDiagnosticQuery.RunAsync(DiagnosticQuery.Security, cancellationToken).ConfigureAwait(false);
        if (!query.Available) return Unavailable("security", "Zabezpieczenia Windows", query.Error);
        try
        {
            using var doc = JsonDocument.Parse(query.Json);
            return ParseSecurity(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return Unavailable("security", "Zabezpieczenia Windows", "Nieprawidłowa odpowiedź systemu: " + ex.Message); }
    }

    internal static DiagnosticSection ParseSecurity(JsonElement root)
    {
        var text = new StringBuilder();
        int read = 0;
        if (root.TryGetProperty("Defender", out var defender) && IsTrue(defender, "Available"))
        {
            read++;
            text.AppendLine("MICROSOFT DEFENDER");
            foreach (var field in new[] { ("AntivirusEnabled", "Antywirus"), ("RealTimeProtectionEnabled", "Ochrona w czasie rzeczywistym"),
                ("BehaviorMonitorEnabled", "Monitorowanie zachowania"), ("IoavProtectionEnabled", "Skanowanie pobranych plików"), ("IsTamperProtected", "Ochrona przed manipulacją") })
                text.AppendLine($"{field.Item2}: {BooleanStatus(defender, field.Item1)}");
            text.AppendLine($"Tryb Defendera: {Value(defender, "AMRunningMode")}");
            text.AppendLine($"Sygnatury: {Value(defender, "AntivirusSignatureVersion")}; wiek: {Value(defender, "AntivirusSignatureAge")} dni");
            text.AppendLine($"Aktualizacja sygnatur: {Value(defender, "AntivirusSignatureLastUpdated")}");
            text.AppendLine($"Ostatni zakończony szybki skan: {Value(defender, "QuickScanEndTime")}");
        }
        else text.AppendLine("Defender: NIEZNANY — " + (defender.ValueKind == JsonValueKind.Object ? Value(defender, "Error") : "brak danych"));
        text.AppendLine();
        if (root.TryGetProperty("Firewall", out var firewall) && IsTrue(firewall, "Available") && firewall.TryGetProperty("Profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Array && profiles.GetArrayLength() > 0)
        {
            read++;
            text.AppendLine("ZAPORA WINDOWS — KONFIGURACJA PROFILI");
            foreach (var profile in profiles.EnumerateArray())
            {
                string enabled = Value(profile, "Enabled");
                text.AppendLine($"{Value(profile, "Name")}: {(enabled.Equals("True", StringComparison.OrdinalIgnoreCase) ? "WŁĄCZONA" : enabled.Equals("False", StringComparison.OrdinalIgnoreCase) ? "WYŁĄCZONA" : "NIEZNANY / " + enabled)}; przychodzące: {Value(profile, "DefaultInboundAction")}; wychodzące: {Value(profile, "DefaultOutboundAction")}");
            }
        }
        else text.AppendLine("Zapora: NIEZNANY — " + (firewall.ValueKind == JsonValueKind.Object ? Value(firewall, "Error") : "brak danych"));
        text.AppendLine("\nBrak danych nie oznacza włączonej ochrony. Inny antywirus może działać obok lub zamiast Defendera. Odczyt konfiguracji nie jest skanem zagrożeń ani testem reguł zapory.");
        return new("security", "Zabezpieczenia Windows", read == 2 ? "dostępny" : read == 1 ? "częściowy" : "niedostępny", text.ToString().Trim());
    }

    public Task<DiagnosticSection> GetStartupAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var lines = new List<string>();
        var errors = new List<string>();
        int total = 0, inspected = 0;
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 })
        foreach (string branch in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = $"{hive}/{view}/{branch.Split('\\')[^1]}";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(branch);
                inspected++;
                if (key == null) continue;
                foreach (string name in key.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested(); total++;
                    if (lines.Count < 100) lines.Add($"• {name} [{source}]\n  {WindowsDiagnosticQuery.Limit(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString(), 600)}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{source}: {ex.Message}"); }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string path = Environment.GetFolderPath(folder);
                inspected++;
                if (!Directory.Exists(path)) continue;
                foreach (string entry in Directory.EnumerateFiles(path).Take(101))
                { total++; if (lines.Count < 100) lines.Add($"• {Path.GetFileName(entry)} [{folder}]\n  {entry}"); }
            }
            catch (Exception ex) { errors.Add($"{folder}: {ex.Message}"); }
        }
        string content = $"Wpisy Run/RunOnce i foldery Autostart; wykryto co najmniej {total}.\n" +
            "To inwentarz wpisów. Wyłączenie w Menedżerze zadań, zasady systemu i opóźnienia mogą zmieniać faktyczne uruchomienie. Nie obejmuje zadań harmonogramu ani usług.\n\n" +
            (lines.Count == 0 ? "Nie odczytano wpisów." : string.Join("\n", lines)) +
            (total > lines.Count ? $"\nPokazano pierwsze {lines.Count} wpisów." : "") +
            (errors.Count > 0 ? "\n\nNiedostępne źródła:\n" + string.Join("\n", errors) : "");
        return new DiagnosticSection("startup", "Autostart — inwentarz", errors.Count == 0 ? "dostępny" : inspected > 0 ? "częściowy" : "niedostępny", content);
    }, cancellationToken);

    public async Task<DiagnosticSection> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        var query = await WindowsDiagnosticQuery.RunAsync(DiagnosticQuery.Services, cancellationToken).ConfigureAwait(false);
        if (!query.Available) return Unavailable("services", "Usługi Windows", query.Error);
        try
        {
            using var doc = JsonDocument.Parse(query.Json); var root = doc.RootElement;
            var text = new StringBuilder($"Wszystkie: {Value(root, "Total")}; uruchomione: {Value(root, "Running")}; automatyczne, obecnie zatrzymane: {Value(root, "AutoStoppedCount")}\n\nWYBRANE USŁUGI\n");
            foreach (var entry in Items(root, "Important")) text.AppendLine($"{Value(entry, "Name")}: {Value(entry, "State")} • start: {Value(entry, "StartMode")}");
            text.AppendLine("\nAUTOMATYCZNE, OBECNIE ZATRZYMANE (do 25)");
            foreach (var entry in Items(root, "AutoStopped")) text.AppendLine($"{Value(entry, "Name")}: {Value(entry, "DisplayName")}");
            text.AppendLine("\nZatrzymana usługa automatyczna nie musi oznaczać błędu: Windows używa uruchamiania na żądanie i wyzwalaczy. Nic nie zostało uruchomione, zatrzymane ani przestawione.");
            return new("services", "Usługi Windows", "dostępny", text.ToString().Trim());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return Unavailable("services", "Usługi Windows", ex.Message); }
    }

    public async Task<DiagnosticSection> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        var query = await WindowsDiagnosticQuery.RunAsync(DiagnosticQuery.Events, cancellationToken).ConfigureAwait(false);
        if (!query.Available) return Unavailable("events", "Ostatnie błędy systemu", query.Error);
        try
        {
            using var doc = JsonDocument.Parse(query.Json);
            var text = new StringBuilder("Ostatnie 48 godzin: krytyczne i błędy, do 20 najnowszych wpisów na dziennik.\n"); int read = 0;
            foreach (var log in Items(doc.RootElement, "Logs"))
            {
                text.AppendLine("\n" + Value(log, "Name"));
                if (!IsTrue(log, "Available")) { text.AppendLine("NIEZNANY: " + Value(log, "Error")); continue; }
                read++;
                var entries = Items(log, "Entries").ToArray();
                if (entries.Length == 0) text.AppendLine("Brak pasujących wpisów w tym przedziale.");
                foreach (var entry in entries)
                    text.AppendLine($"{Value(entry, "Time")} • {Value(entry, "Provider")} • ID {Value(entry, "Id")}\n{Value(entry, "Message")}\n");
            }
            text.AppendLine("Historyczne błędy nie dowodzą, że problem trwa. Zestaw je z czasem wystąpienia objawu.");
            return new("events", "Ostatnie błędy systemu", read == 2 ? "dostępny" : read == 1 ? "częściowy" : "niedostępny", text.ToString().Trim());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return Unavailable("events", "Ostatnie błędy systemu", ex.Message); }
    }

    public Task<DiagnosticSection> GetDisksAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lines = new List<string>(); int total = 0, read = 0;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
            {
                cancellationToken.ThrowIfCancellationRequested(); total++;
                try
                {
                    if (!drive.IsReady) { lines.Add($"{drive.Name}: NIEDOSTĘPNY"); continue; }
                    long size = drive.TotalSize, free = drive.AvailableFreeSpace;
                    if (size <= 0) { lines.Add($"{drive.Name}: NIEZNANA pojemność"); continue; }
                    read++;
                    double percent = 100d * free / size;
                    lines.Add($"{drive.Name} [{drive.DriveFormat}] Wolne: {free / 1073741824d:0.0} / {size / 1073741824d:0.0} GiB ({percent:0.0}%)." + (percent < 10 ? " MAŁO MIEJSCA (<10%)." : ""));
                }
                catch (Exception ex) { lines.Add($"{drive.Name}: NIEZNANY ({ex.Message})"); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Unavailable("disks", "Dyski lokalne", ex.Message); }
        lines.Add("Pojemność woluminów nie jest pomiarem kondycji sprzętu ani odczytem SMART.");
        if (total == 0) lines.Insert(0, "Nie wykryto stałych woluminów lokalnych.");
        return new DiagnosticSection("disks", "Dyski lokalne", total > 0 && read == total ? "dostępny" : read > 0 ? "częściowy" : "niedostępny", string.Join("\n", lines));
    }, cancellationToken);

    public async Task<DiagnosticReport> CollectAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset capturedAt = DateTimeOffset.Now;
        Task<DiagnosticSection>[] tasks = { GetSystemAsync(cancellationToken), GetDisksAsync(cancellationToken), GetSecurityAsync(cancellationToken),
            GetStartupAsync(cancellationToken), GetServicesAsync(cancellationToken), GetEventsAsync(cancellationToken),
            Task.Run(() => new DiagnosticSection("network", "Interfejsy sieci", "odczyt lokalny", new NetworkDiagnosticService().GetNetworkSummary()), cancellationToken) };
        var sections = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(1, capturedAt, sections);
    }

    private static async Task<DiagnosticSection> GetSystemAsync(CancellationToken token)
    {
        using var monitor = new SystemMonitor(); var system = new SystemInfoService();
        await Task.Delay(400, token).ConfigureAwait(false);
        float cpu = monitor.GetCpuUsage(); double used = monitor.GetUsedRamGB(), total = monitor.GetTotalRamGB();
        return new("system", "Komputer i wydajność", float.IsFinite(cpu) && double.IsFinite(total) ? "dostępny" : "częściowy",
            $"System: {system.GetWindowsVersion()}\nCPU: {system.GetCpuName()}\nProcesory logiczne: {Environment.ProcessorCount}\n" +
            $"Uptime: {system.GetUptime()}\nCPU (krótka próbka): {(float.IsFinite(cpu) ? $"{cpu:0.0}%" : "NIEZNANY: " + monitor.LastCpuError)}\n" +
            $"RAM: {(double.IsFinite(total) ? $"{used:0.0} / {total:0.0} GiB ({monitor.GetRamUsagePercent():0.0}%)" : "NIEZNANY: " + monitor.LastMemoryError)}");
    }

    public async Task<ActionExecutionResult> ExportReportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DiagnosticReport report = await CollectAsync(cancellationToken).ConfigureAwait(false);
            string directory = Path.Combine(AppPaths.Root, "Reports");
            Directory.CreateDirectory(directory);
            string basename = $"sentinel-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
            string markdownPath = Path.Combine(directory, basename + ".md"), jsonPath = Path.Combine(directory, basename + ".json");
            await WriteNewFileAsync(markdownPath, report.ToMarkdown(), cancellationToken).ConfigureAwait(false);
            try { await WriteNewFileAsync(jsonPath, JsonSerializer.Serialize(report, ReportJson), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { return ActionExecutionResult.UnverifiedSuccess("Zapisano raport tekstowy; eksport JSON nie powiódł się.", markdownPath + "\n" + ex.Message); }
            return ActionExecutionResult.VerifiedSuccess("Zapisano lokalny raport Markdown i JSON. Nie został wysłany do internetu.", markdownPath + "\n" + jsonPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ActionExecutionResult.Failure("Nie udało się zapisać raportu.", ex.Message); }
    }

    private static async Task WriteNewFileAsync(string path, string text, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch { try { File.Delete(path); } catch { } throw; }
    }

    public async Task<ActionExecutionResult> InspectFileAsync(string input, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            string path = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.AsSpan(2).Contains(':'))
                return ActionExecutionResult.Failure("Podaj pełną ścieżkę do zwykłego pliku na lokalnym dysku, np. C:\\Pobrane\\plik.exe. Ścieżki sieciowe, urządzenia i alternatywne strumienie nie są obsługiwane.");
            path = Path.GetFullPath(path);
            var info = new FileInfo(path);
            if (!info.Exists) return ActionExecutionResult.Failure("Nie znaleziono pliku.", path);
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                return ActionExecutionResult.Failure("Plik jest odsyłaczem lub wymaga pobrania z chmury. Wybierz zwykłą lokalną kopię.");
            long initialLength = info.Length; DateTime modified = info.LastWriteTimeUtc;
            if (initialLength > 2L * 1024 * 1024 * 1024) return ActionExecutionResult.Failure("Limit analizy to 2 GiB; nie odczytano zawartości pliku.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, budget.Token).ConfigureAwait(false);
            info.Refresh();
            if (info.Length != initialLength || info.LastWriteTimeUtc != modified)
                return ActionExecutionResult.Failure("Plik zmienił się podczas odczytu. Wynik sumy nie zostanie uznany za wiarygodny.");
            string metadata;
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                metadata = $"Produkt: {version.ProductName ?? "brak"}\nWersja: {version.FileVersion ?? "brak"}\nDeklarowany producent: {version.CompanyName ?? "brak"}";
            }
            catch (Exception ex) { metadata = "Metadane wersji niedostępne: " + ex.Message; }
            return ActionExecutionResult.VerifiedSuccess("Odczytano metadane i SHA-256. Plik nie został uruchomiony. Suma i deklarowany producent nie potwierdzają bezpieczeństwa ani autentyczności.",
                $"Ścieżka: {path}\nRozmiar: {initialLength:N0} B\nModyfikacja UTC: {modified:O}\nSHA-256: {Convert.ToHexString(hash)}\n{metadata}");
        }
        catch (OperationCanceledException)
        { cancellationToken.ThrowIfCancellationRequested(); return ActionExecutionResult.Failure("Przekroczono limit 20 sekund odczytu pliku. Nie uzyskano pełnego wyniku."); }
        catch (Exception ex) { return ActionExecutionResult.Failure("Nie udało się odczytać pliku.", ex.Message); }
    }

    private static DiagnosticSection Unavailable(string id, string title, string error) => new(id, title, "niedostępny", "NIEZNANY — " + error);
    private static string Value(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : "nieznany";
    private static bool IsTrue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    internal static string BooleanStatus(JsonElement element, string name) => !element.TryGetProperty(name, out var value) ? "NIEZNANY" : value.ValueKind switch { JsonValueKind.True => "WŁĄCZONA", JsonValueKind.False => "WYŁĄCZONA", _ => "NIEZNANY" };
    private static IEnumerable<JsonElement> Items(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
}
