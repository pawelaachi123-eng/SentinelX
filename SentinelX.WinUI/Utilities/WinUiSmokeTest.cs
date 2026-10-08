using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Engine;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.Utilities;

/// <summary>
/// Minimal headless smoke test for CI: builds the real DI graph, runs one local
/// read-only command through the engine and samples telemetry. No window, no ports,
/// no model downloads (SENTINEL_UI_SMOKE=1 keeps CareService quiet).
/// Run: SentinelX.exe --ui-smoke &lt;output-dir&gt;
/// </summary>
public static class WinUiSmokeTest
{
    public static async Task<int> RunAsync(IServiceProvider provider, string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var engine = provider.GetRequiredService<IActionEngine>();
            var monitor = provider.GetRequiredService<ISystemMonitorService>();
            var voice = provider.GetRequiredService<IVoiceService>();
            var ai = provider.GetRequiredService<IEngineService>();
            var core = provider.GetRequiredService<ICoreStateService>();
            var settings = provider.GetRequiredService<ISettingsService>();

            monitor.Start();
            IntentResult result = await engine.ExecuteAsync("wersja");
            await Task.Delay(TimeSpan.FromSeconds(3));
            var snapshot = monitor.Current;

            var report = new StringBuilder();
            report.AppendLine("command=wersja");
            report.AppendLine("reply=" + result.Text.Split('\n').FirstOrDefault()?.Trim());
            report.AppendLine("action=" + (result.Action?.Status.ToString() ?? "none"));
            report.AppendLine("core=" + core.State);
            report.AppendLine($"cpu={snapshot.Cpu} ram={snapshot.RamUsed}/{snapshot.RamTotal}");
            report.AppendLine($"voice={voice.State} mics={voice.GetMicrophones().Count}");
            report.AppendLine("engine=" + ai.Status.State);
            report.AppendLine("settingsError=" + (settings.LastError ?? "none"));
            File.WriteAllText(Path.Combine(outputDir(outputDirectory), "SMOKE.txt"), report.ToString());

            if (result.Action?.Status == ActionStatus.Failed)
            {
                File.WriteAllText(Path.Combine(outputDirectory, "FAILED.txt"), result.Text);
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "FAILED.txt"), ex.ToString());
            }
            catch
            {
            }

            return 1;
        }
    }
}
