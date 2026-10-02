using System.Text.Json.Serialization;

namespace SentinelX;

public enum TaskPriority
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3,
    Background = 4
}

public sealed class ResourceSettings
{
    public int MonitorIntervalSeconds { get; set; } = 1;
    public int GamingMonitorIntervalSeconds { get; set; } = 4;

    /// <summary>Liczba workerów do zadań CPU-bound. Domyślnie max(1, rdzenie - 1) aby zostawić jeden rdzeń dla UI.</summary>
    public int WorkerThreadCount { get; set; } = DefaultWorkerCount;
    public bool RespectGamingMode { get; set; } = true;
    public int GamingCpuBudgetPercent { get; set; } = 25;   // Maks. 25% CPU dla Sentinela w trybie gry
    public int SentinelCpuBudgetPercent { get; set; } = 40; // Maks. budżet w normalnym trybie
    public int MaxBackgroundTasksPerMinute { get; set; } = 6;
    public bool ThrottleOnBattery { get; set; } = true;

    [JsonIgnore]
    public static int DefaultWorkerCount => Math.Max(1, Environment.ProcessorCount - 1);

    public int EffectiveWorkerCount(bool isGaming)
    {
        if (isGaming && RespectGamingMode)
            return Math.Max(1, Environment.ProcessorCount * GamingCpuBudgetPercent / 100);
        int configured = WorkerThreadCount > 0 ? WorkerThreadCount : DefaultWorkerCount;
        return Math.Clamp(configured, 1, Environment.ProcessorCount);
    }
}
