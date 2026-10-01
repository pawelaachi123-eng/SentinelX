namespace SentinelX.Services.Overlay;

public enum OverlayWidgetKind
{
    Cpu,
    Gpu,
    Ram,
    Vram,
    Fps,
    Frametime,
    Ping,
    Network,
    Temperature,
    Clock
}

[Flags]
public enum OverlayWidgets
{
    None = 0,
    Cpu = 1 << 0,
    Gpu = 1 << 1,
    Ram = 1 << 2,
    Vram = 1 << 3,
    Fps = 1 << 4,
    Frametime = 1 << 5,
    Ping = 1 << 6,
    Network = 1 << 7,
    Temperature = 1 << 8,
    Clock = 1 << 9
}

public sealed record OverlayConfiguration(
    OverlayWidgets Visible = OverlayWidgets.Cpu | OverlayWidgets.Gpu | OverlayWidgets.Ram | OverlayWidgets.Fps,
    double Opacity = 0.85,
    double Scale = 1.0,
    int RefreshRateHz = 2,
    double PositionX = 20,
    double PositionY = 20);

/// <summary>
/// Wskazówka czy dane dla widgetu są legalnie dostępne (FPS/Frametime z PresentMon/dxgi wymaga
/// dodatkowej biblioteki — bez niej zwracamy null i UI nie pokazuje widgetu).
/// </summary>
public interface IOverlayDataSource
{
    double? GetCpuPercent();
    double? GetGpuPercent();
    double? GetUsedRamGb();
    double? GetUsedVramGb();
    double? GetFps();            // null = unsupported w tym buildzie
    double? GetFrametimeMs();    // null = unsupported
    double? GetPingMs();
    (long downBps, long upBps)? GetNetworkRates();
    double? GetTemperatureC();
}
