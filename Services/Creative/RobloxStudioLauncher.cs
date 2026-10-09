using System.Diagnostics;
using SentinelX.Services.History;

namespace SentinelX.Services.Creative;

public sealed record RobloxStudioOpenResult(bool Attempted, bool ProcessDetected, string Message);

/// <summary>Opens only a newly generated, locally verified .rbxlx through its Windows file association.
/// It never connects to Roblox accounts, publishes a place, or claims that Studio loaded the project.</summary>
public sealed class RobloxStudioLauncher
{
    public async Task<RobloxStudioOpenResult> OpenGeneratedPlaceAsync(string placePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new(false, false, "Automatyczne otwieranie Roblox Studio wymaga Windows; plik place pozostaje zapisany.");
        if (!TryValidateGeneratedPlace(placePath, out string fullPath, out string error))
            return new(false, false, "Nie otwarto place: " + error);

        string[] before = GetStudioProcessIds();
        try
        {
            using Process? launch = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            if (launch == null)
                return new(false, false, "Windows nie potwierdził przekazania pliku do aplikacji. Otwórz go ręcznie w Roblox Studio: " + fullPath);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            while (!timeout.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                string[] after = GetStudioProcessIds();
                string[] newlyStarted = after.Except(before, StringComparer.Ordinal).ToArray();
                if (newlyStarted.Length > 0)
                {
                    string evidence = $"Plik: {fullPath}\nProcesy przed: {string.Join(",", before)}\nProcesy po: {string.Join(",", after)}\nNowy PID: {string.Join(",", newlyStarted)}\nShellExecute zaakceptował plik. Nowy proces nie potwierdza, że Roblox Studio zakończyło odczyt, poprawnie otworzyło place ani wykonało skrypt.";
                    ActionEvidenceCapture.Record(new ActionHistoryEntry
                    {
                        ActionId = "SX-STUDIO-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                        Timestamp = DateTime.Now,
                        ActionType = "ROBLOX_STUDIO_OPEN",
                        Command = "Otwórz wygenerowany prototyp w Roblox Studio",
                        Status = "UNVERIFIED",
                        Message = "Wykryto proces Roblox Studio po przekazaniu wygenerowanego place.",
                        Evidence = evidence,
                        RecoveryAdvice = "Sprawdź okno Roblox Studio, Errors/Output i uruchom Play na lokalnej kopii. Sentinel nie potwierdził kompilacji ani działania."
                    });
                    return new(true, true, "Wysłano plik do Roblox Studio i wykryto proces. Sprawdź okno — wykrycie procesu nie potwierdza, że place się otworzył.");
                }
                await Task.Delay(250, timeout.Token).ConfigureAwait(false);
            }
            return new(true, false, "Windows przyjął prośbę o otwarcie pliku, ale w ciągu 8 sekund nie wykryto nowego procesu Roblox Studio. Jeśli Studio było już otwarte, sprawdź jego okno; w przeciwnym razie otwórz place ręcznie: " + fullPath);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new(true, false, "Oczekiwanie na proces Studio przekroczyło 8 sekund. Plik został zapisany i można otworzyć go ręcznie: " + fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new(false, false, "Nie udało się otworzyć Roblox Studio; place pozostaje zapisany. " + ex.Message);
        }
    }

    private static bool TryValidateGeneratedPlace(string input, out string fullPath, out string error)
    {
        fullPath = error = "";
        try
        {
            fullPath = Path.GetFullPath(input ?? "");
            if (!string.Equals(Path.GetExtension(fullPath), ".rbxlx", StringComparison.OrdinalIgnoreCase))
            { error = "do Studio przekazywany jest wyłącznie place .rbxlx."; return false; }
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length is < 256 or > 16L * 1024 * 1024)
            { error = "plik place nie istnieje albo ma nieoczekiwany rozmiar."; return false; }
            string? placeDirectory = Path.GetDirectoryName(fullPath);
            string? projectDirectory = placeDirectory == null ? null : Directory.GetParent(placeDirectory)?.FullName;
            string? generatedRoot = projectDirectory == null ? null : Directory.GetParent(projectDirectory)?.FullName;
            if (!string.Equals(Path.GetFileName(placeDirectory), "place", StringComparison.OrdinalIgnoreCase) ||
                !(Path.GetFileName(projectDirectory ?? "") ?? "").StartsWith("sentinel-roblox-", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(generatedRoot), "CreatedGames", StringComparison.OrdinalIgnoreCase))
            { error = "automatycznie otwierany jest tylko place z nowego folderu SentinelX/CreatedGames/sentinel-roblox-*/place."; return false; }
            if ((File.GetAttributes(generatedRoot!) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(projectDirectory!) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            { error = "plik place jest dowiązaniem systemu plików."; return false; }
            string xml = File.ReadAllText(fullPath);
            if (!RobloxPlaceFileBuilder.TryReadServerSource(xml, out _, out error)) return false;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { error = ex.Message; return false; }
    }

    private static string[] GetStudioProcessIds()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("RobloxStudioBeta");
            return processes.Select(process => process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return []; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }
}
