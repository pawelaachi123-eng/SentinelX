using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SentinelX.Core;
using SentinelX.Services.Agent;
using SentinelX.Services.Base;
using SentinelX.Services.Link;

namespace SentinelX.Services.Maintenance;

public sealed record RecoveryState(
    int UiFailures, int AgentFailures, int LaunchFailures,
    string? SettingsError, string IdentityState, string? ActiveVersion, string? LastError, string DataRoot);

/// <summary>File-level recovery evaluation and repair. Recovery never unpairs
/// Base on its own: a corrupt identity is archived and the user re-pairs
/// explicitly from the main screen.</summary>
public static class RecoveryService
{
    public static RecoveryState Evaluate()
    {
        string root = AppPaths.Root;
        var ui = RunHealth.Read(root, "ui");
        var agent = RunHealth.Read(root, "agent");
        int launchFailures = 0;
        string? activeVersion = null;
        try
        {
            var journal = new VerifiedUpdater(Path.Combine(root, "Updates"), MaintenanceService.ValidPackage).ReadJournal();
            launchFailures = journal.LaunchFailures;
            activeVersion = journal.Active?.Version;
        }
        catch { }
        string? settingsError = null;
        try
        {
            settingsError = new AppSettingsService().LastError;
        }
        catch (Exception e)
        {
            settingsError = e.Message;
        }
        string identity = "absent";
        try
        {
            string p = Path.Combine(root, "Base", "identity.bin");
            if (File.Exists(p))
            {
                if (new FileInfo(p).Length is <= 0 or > 65536) identity = "corrupt";
                else
                {
                    try
                    {
                        ProtectedData.Unprotect(File.ReadAllBytes(p), null, DataProtectionScope.CurrentUser);
                        identity = "paired";
                    }
                    catch
                    {
                        identity = "corrupt";
                    }
                }
            }
        }
        catch
        {
            identity = "unknown";
        }
        string? lastError = null;
        try
        {
            string log = Path.Combine(root, "Logs", "errors.log");
            if (File.Exists(log))
            {
                var lines = File.ReadAllLines(log);
                if (lines.Length > 0) lastError = lines[^1][..Math.Min(300, lines[^1].Length)];
            }
        }
        catch { }
        return new(ui.ConsecutiveFailures, agent.ConsecutiveFailures, launchFailures,
            settingsError, identity, activeVersion, lastError, root);
    }

    public static bool Needed(RecoveryState state) =>
        state.UiFailures > 0 || state.LaunchFailures > 0 || state.SettingsError != null || state.IdentityState == "corrupt";

    public static string ResetSettings()
    {
        string path = Path.Combine(AppPaths.SettingsDirectory, "settings.json");
        string backup = Path.Combine(AppPaths.BackupsDirectory, "settings-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json");
        Directory.CreateDirectory(AppPaths.BackupsDirectory);
        if (File.Exists(path)) File.Copy(path, backup, true);
        if (File.Exists(path)) File.Delete(path);
        string? error = new AppSettingsService().LastError;
        if (error != null) throw new IOException("settings_reset: " + error);
        return "Ustawienia zresetowane do domyślnych. Kopia: " + backup;
    }

    public static string BackupCorruptIdentity()
    {
        string p = Path.Combine(AppPaths.Root, "Base", "identity.bin");
        if (!File.Exists(p)) return "Brak pliku parowania.";
        string backup = p + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        File.Copy(p, backup, false);
        File.Delete(p);
        return "Uszkodzone parowanie zarchiwizowano (" + backup + "). Sparuj Base ponownie z głównego ekranu.";
    }

    public static MaintenanceService CreateMaintenance()
    {
        var ollama = new OllamaSupervisor();
        return new MaintenanceService(
            new WindowsBaseService(new HeadlessDesktopService(), new SystemMonitor(), ollama, new AlertFeed()), ollama);
    }

    public static string OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
        return "Otwarto katalog logów.";
    }

    public static string RollbackUpdate()
    {
        var updater = new VerifiedUpdater(Path.Combine(AppPaths.Root, "Updates"), MaintenanceService.ValidPackage);
        var version = updater.Rollback();
        return "Przywrócono " + version.Version + ". Uruchom Sentinel ponownie.";
    }
}
