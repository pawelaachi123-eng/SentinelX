using System.IO;
using System.Text;
using System.Text.Json;

namespace SentinelX;

public static class SelfDiagnosticService
{
    public static async Task<string> RunAsync(AppSettingsService settings, LocalAiService ai, string voiceDiagnostics, bool checkAi, CancellationToken token)
    {
        var report = new StringBuilder("SELF DIAGNOSTIC • " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
        void Line(string status, string name, string evidence) => report.AppendLine($"[{status}] {name}: {evidence}");
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            string probe = Path.Combine(AppPaths.Root, ".health-" + Guid.NewGuid().ToString("N"));
            try { await File.WriteAllTextAsync(probe, "sentinel-health", token); Line(await File.ReadAllTextAsync(probe, token) == "sentinel-health" ? "VERIFIED" : "FAILED", "Dane lokalne", AppPaths.Root); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Line("FAILED", "Dane lokalne", ex.Message); }
        if (settings.LastError != null) Line("FAILED", "Ustawienia", settings.LastError);
        else if (File.Exists(settings.SettingsPath))
        {
            try { using var json = JsonDocument.Parse(await File.ReadAllTextAsync(settings.SettingsPath, token)); Line("VERIFIED", "Ustawienia JSON", settings.SettingsPath); }
            catch (JsonException ex) { Line("FAILED", "Ustawienia JSON", ex.Message); }
        }
        else Line("NOT VERIFIED", "Ustawienia", "Aktywne wartości domyślne; plik jeszcze nie był zapisany.");
        foreach (string folder in new[] { "Memory", "History", "Logs", "Voice" })
            Line(Directory.Exists(Path.Combine(AppPaths.Root, folder)) ? "VERIFIED" : "NOT VERIFIED", folder, Directory.Exists(Path.Combine(AppPaths.Root, folder)) ? "Katalog istnieje." : "Katalog nie został jeszcze utworzony.");
        Line("INFO", "Głos", voiceDiagnostics);
        Line("NOT VERIFIED", "Jakość rozpoznawania mikrofonu", "Wymaga próby mowy użytkownika; diagnostyka nie uruchamia nagrywania.");
        if (checkAi)
        {
            try { Line("INFO", "Silnik AI", await ai.GetStatusAsync(token)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Line("NOT VERIFIED", "Silnik AI", ex.Message); }
        }
        else Line("NOT VERIFIED", "Silnik AI", "Pominięta w izolowanym teście aplikacji.");
        return report.ToString();
    }
}
