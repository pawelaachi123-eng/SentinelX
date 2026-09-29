using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace SentinelX;

/// <summary>Small reversible Windows audio control through Core Audio; it does not change output devices.</summary>
public sealed class AudioControlTool
{
    public string? TryProcess(string command, ActionHistoryService history, CancellationToken token = default)
    {
        string normalized = Core.CommandText.Normalize(command).Trim().TrimEnd('.', '!', '?');
        if (normalized is "jaka glosnosc systemu" or "sprawdz glosnosc systemu") return ReadVolume();
        Match match = Regex.Match(normalized, @"^(?:ustaw|zmien) glosnosc(?: systemu)? (?:na )?(?<value>\d{1,3})%?$");
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups["value"].Value, out int percent) || percent is < 0 or > 100)
            return "Głośność systemu musi być liczbą od 0 do 100%.";

        string id = history.CreateActionId();
        history.AddRunning(id, "SET_SYSTEM_VOLUME", command);
        ActionExecutionResult result;
        bool changeAttempted = false;
        string deviceName = "";
        try
        {
            token.ThrowIfCancellationRequested();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            deviceName = device.FriendlyName;
            changeAttempted = true;
            device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
            float actual = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool verified = Math.Abs(actual - percent / 100f) <= 0.01f;
            result = verified
                ? ActionExecutionResult.VerifiedSuccess($"Ustawiono głośność Windows na {actual * 100:0}%.", $"Urządzenie wyjściowe: {device.FriendlyName}; odczyt zwrotny: {actual * 100:0.0}%.")
                : ActionExecutionResult.Failure("Głośność nie przeszła weryfikacji odczytem zwrotnym.", $"Urządzenie: {device.FriendlyName}; żądano {percent}%, odczytano {actual * 100:0.0}%.");
        }
        catch (OperationCanceledException)
        {
            history.AddCancelled(id, "SET_SYSTEM_VOLUME", command, "Polecenie przerwano; sprawdź bieżącą głośność przed ponowieniem.");
            throw;
        }
        catch (Exception ex)
        {
            result = changeAttempted
                ? ActionExecutionResult.UnverifiedSuccess("Wysłano próbę zmiany głośności, ale nie udało się zweryfikować wyniku. Sprawdź suwak systemowy przed ponowieniem.", deviceName + (deviceName.Length > 0 ? "\n" : "") + ex.Message)
                : ActionExecutionResult.Failure("Nie udało się zmienić głośności Windows.", ex.Message);
        }
        history.AddResult(id, "SET_SYSTEM_VOLUME", command, result);
        return result.Message + (string.IsNullOrWhiteSpace(result.Evidence) ? "" : "\n" + result.Evidence);
    }

    private static string ReadVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return $"Głośność systemu: {device.AudioEndpointVolume.MasterVolumeLevelScalar * 100:0}%. Urządzenie: {device.FriendlyName}.";
        }
        catch (Exception ex) { return "Nie udało się odczytać głośności systemu: " + ex.Message; }
    }
}
