using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class UiSettings
{
    public bool AnimationsEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public string Theme { get; set; } = "Dark";
    public string DefaultBrowserPreference { get; set; } = "Brave";
    public string AccentColor { get; set; } = "#00D4FF";
    public int OverlayOpacityPercent { get; set; } = 85;
    public int OverlayScalePercent { get; set; } = 100;
    public string OverlayPosition { get; set; } = "Lewy górny";
    /// <summary>0.96 · efekt tła okna w Windows 11: Mica (przeszklenie tapety), Akryl albo Brak.
    /// Na Windows 10 system nie udostępnia tych atrybutów DWM — ustawienie jest wtedy ignorowane
    /// i okno zostaje na zwykłym tle z tokenów Sx*.</summary>
    public string WindowBackdrop { get; set; } = "Mica";
    /// <summary>Pasek tytułu w ciemnych kolorach aplikacji (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
    public bool DarkTitleBar { get; set; } = true;
    public double WindowWidth { get; set; } = 1260;
    public double WindowHeight { get; set; } = 860;
    public string SelectedPage { get; set; } = "Chat";
}

