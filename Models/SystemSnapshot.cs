namespace SentinelX.Models;

public sealed record ProcessSnapshot(string Name, int Pid, double MemoryMb, double? CpuPercent);
public sealed record DiskSnapshot(string Name, double UsedGb, double TotalGb)
{
    public double Percent => TotalGb > 0 ? UsedGb / TotalGb * 100 : 0;
    public string Label => $"{Name}   {UsedGb:F1} / {TotalGb:F1} GB";
}
public sealed record SystemSnapshot(
    DateTime Timestamp, double Cpu, double RamUsed, double RamTotal, double Gpu,
    string Game, string Network, IReadOnlyList<DiskSnapshot> Disks,
    IReadOnlyList<ProcessSnapshot> Processes)
{
    public static SystemSnapshot Empty { get; } = new(DateTime.MinValue, double.NaN,
        double.NaN, double.NaN, double.NaN, "", "Oczekiwanie na pomiar…", [], []) { GamingDetectionAvailable = false };
    /// <summary>Non-fatal telemetry error from the latest sampling attempt; prior measurements remain timestamped.</summary>
    public string? SampleError { get; init; }
    /// <summary>False when Windows process enumeration cannot establish whether a game is running.</summary>
    public bool GamingDetectionAvailable { get; init; } = true;
    public string MeasurementStatus => SampleError is { Length: > 0 } error
        ? "Ostatni pomiar nie powiódł się: " + error
        : HasSample ? "Dane z " + Timestamp.ToString("HH:mm:ss") : "Oczekiwanie na pierwszy pomiar";
    public string CpuText => Format(Cpu, "%");
    public string GpuText => Format(Gpu, "%");
    public string RamText => double.IsFinite(RamUsed) ? $"{RamUsed:F1} / {RamTotal:F1} GB" : "Niedostępne";
    public string VramText => "Niedostępne"; // No fabricated VRAM measurements.
    public bool HasSample => Timestamp != DateTime.MinValue;
    public string DiskSummaryText => !HasSample
        ? "Oczekiwanie na pomiar"
        : Disks.Count == 0 ? "Brak danych" : $"{Disks.Count} woluminów";
    public string DiskEmptyMessage => HasSample
        ? "Ostatni pomiar nie zwrócił dostępnych woluminów."
        : "Oczekiwanie na pierwszy pomiar woluminów.";
    public string ProcessEmptyMessage => HasSample
        ? "Lista procesów jest pusta lub niedostępna. Sprawdź uprawnienia systemu."
        : "Nie ma jeszcze listy procesów. Poczekaj na pierwszy pomiar systemu.";
    public string GamingText => !GamingDetectionAvailable ? "Niedostępne" : string.IsNullOrEmpty(Game) ? "OFF" : Game;
    private static string Format(double value, string unit) => double.IsFinite(value) ? $"{value:F0}{unit}" : "Niedostępne";
}
