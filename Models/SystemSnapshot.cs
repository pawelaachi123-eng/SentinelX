namespace SentinelX.Models;

public sealed record ProcessSnapshot(string Name, int Pid, double MemoryMb, double? CpuPercent);
public sealed record DiskSnapshot(string Name, double UsedGb, double TotalGb)
{
    public double Percent => TotalGb > 0 ? UsedGb / TotalGb * 100 : 0;
    public string Label => $"{Name}   {UsedGb:F1} / {TotalGb:F1} GB";
}
public sealed record CoreSnapshot(double UsagePercent, double ClockMhz)
{
    public int PercentInt => (int)double.Clamp(UsagePercent, 0, 100);
}
public sealed record SystemSnapshot(
    DateTime Timestamp, double Cpu, double RamUsed, double RamTotal, double Gpu,
    string Game, string Network,
    IReadOnlyList<DiskSnapshot> Disks,
    IReadOnlyList<ProcessSnapshot> Processes,
    IReadOnlyList<CoreSnapshot>? Cores = null,
    double? VramUsedGb = null, double? VramTotalGb = null,
    double? CpuTempC = null, double? GpuTempC = null,
    double NetworkDownMbps = 0, double NetworkUpMbps = 0)
{
    public static SystemSnapshot Empty { get; } = new(DateTime.MinValue, double.NaN,
        double.NaN, double.NaN, double.NaN, "", "Oczekiwanie na pomiar…", [], []);
    public string CpuText => Format(Cpu, "%");
    public string GpuText => Format(Gpu, "%");
    public string RamText => double.IsFinite(RamUsed) ? $"{RamUsed:F1} / {RamTotal:F1} GB" : "Niedostępne";
    public string VramText => VramTotalGb is > 0 ? $"{VramUsedGb:F1} / {VramTotalGb:F1} GB" : "Niedostępne";
    public string GamingText => string.IsNullOrEmpty(Game) ? "OFF" : Game;
    public string TempCpuText => CpuTempC is > 0 ? $"{CpuTempC:F0}°C" : "—";
    public string TempGpuText => GpuTempC is > 0 ? $"{GpuTempC:F0}°C" : "—";
    public string NetText => (NetworkDownMbps > 0 || NetworkUpMbps > 0)
        ? $"↓{NetworkDownMbps:F1} ↑{NetworkUpMbps:F1} Mbps"
        : Network;
    public int RamPercent => (int)(RamTotal > 0 ? RamUsed / RamTotal * 100 : 0);
    private static string Format(double value, string unit) => double.IsFinite(value) ? $"{value:F0}{unit}" : "Niedostępne";
}
