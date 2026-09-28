using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class UiSettings
{
    public bool AnimationsEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public string Theme { get; set; } = "Dark";
    public string DefaultBrowserPreference { get; set; } = "Brave";
    public string AccentColor { get; set; } = "#7C6BE8";
    public int OverlayOpacityPercent { get; set; } = 85;
    public int OverlayScalePercent { get; set; } = 100;
    public string OverlayPosition { get; set; } = "Lewy górny";
    public double WindowWidth { get; set; } = 1260;
    public double WindowHeight { get; set; } = 860;
    public string SelectedPage { get; set; } = "Chat";
}

