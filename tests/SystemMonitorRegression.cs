using System.IO;
using SentinelX;
using SentinelX.Models;
using SentinelX.Services.Gaming;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Network;
using SentinelX.Services.Settings;

namespace SentinelX.Tests;

/// <summary>System sampling starts once, publishes failures without discarding prior measurements, and isolates observers.</summary>
internal static class SystemMonitorRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SystemMonitorRegression: " + message);
    }

    private sealed class FakeGaming : IGamingService
    {
        public bool DetectionAvailable { get; set; } = true;
        public bool IsGaming() => false;
        public string GetRunningGame() => "";
    }

    private sealed class FakeNetwork : INetworkService
    {
        public string GetNetworkSummary() => "Test network (no external request)";
        public Task<ActionExecutionResult> TestInternetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ActionExecutionResult.Failure("Not used by the monitor regression."));
        public Task<ActionExecutionResult> TestPingAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Failure("Not used by the monitor regression."));
        public Task<ActionExecutionResult> TestDnsAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Failure("Not used by the monitor regression."));
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var noGameDetected = SystemSnapshot.Empty with { GamingDetectionAvailable = true };
        Check(SystemSnapshot.Empty.GamingText == "Niedostępne" && noGameDetected.GamingText == "OFF",
            "unavailable game detection must not be displayed as a confirmed OFF result");
        var settingsStore = new AppSettingsService(Path.Combine(directory, "settings"));
        settingsStore.Settings.Resources.MonitorIntervalSeconds = 30;
        settingsStore.Settings.Resources.GamingMonitorIntervalSeconds = 30;
        var settings = new SettingsService(settingsStore);
        using var monitor = new SystemMonitor();
        var gaming = new FakeGaming { DetectionAvailable = false };
        using var service = new SystemMonitorService(monitor, gaming, new FakeNetwork(), settings);

        int updates = 0;
        var firstUpdate = new TaskCompletionSource<SystemSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Updated += _ => throw new InvalidOperationException("Intentional telemetry observer fault.");
        service.Updated += snapshot =>
        {
            Interlocked.Increment(ref updates);
            firstUpdate.TrySetResult(snapshot);
        };

        Parallel.For(0, 16, _ => service.Start());
        SystemSnapshot sample = await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(1000);
        Check(Volatile.Read(ref updates) == 1, "concurrent Start calls must create exactly one immediate sample loop");
        Check(sample.HasSample || sample.SampleError is { Length: > 0 }, "the first attempt must publish either a timestamped sample or a visible error");
        Check(sample.GamingText == "Niedostępne", "unknown game detection remains unavailable in the published system snapshot");
        Check(sample.Processes.Count <= 40, "published process history stays capped at forty entries");
        string? currentError = service.Current.SampleError;
        Check(currentError == null || service.Current.MeasurementStatus.Contains(currentError, StringComparison.Ordinal),
            "a failed read is surfaced alongside the timestamped last-good snapshot");

        DateTime timestamp = service.Current.Timestamp;
        service.Dispose();
        service.Dispose();
        service.Start();
        await Task.Delay(100);
        Check(service.Current.Timestamp == timestamp, "disposed monitor cannot restart or publish another sample");
        File.WriteAllText(Path.Combine(directory, "system-monitor-tests.txt"),
            "PASS: thread-safe idempotent Start, single sampler, bounded process rows, visible sample errors, safe observer dispatch, idempotent disposal\n");
    }
}
