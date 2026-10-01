namespace SentinelX.Models.Performance;

public enum SentinelComponent
{
    Total,
    Core,
    Ai,
    Stt,
    Tts,
    Overlay,
    Indexing,
    Plugins,
    Automations,
    Network,
    Other
}

public readonly record struct ComponentSample(
    SentinelComponent Component,
    double CpuPercent,
    long RamBytes,
    double? GpuPercent,
    long? VramBytes,
    int Threads,
    DateTimeOffset At);

public sealed record ResourceBudget(
    double MaxCpuPercent = 30,
    long MaxRamBytes = 512 * 1024 * 1024,
    double? MaxGpuPercent = 20,
    long? MaxVramBytes = 256 * 1024 * 1024);
