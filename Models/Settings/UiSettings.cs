using System.Text.Json.Serialization;

namespace SentinelX;

public enum AnimationLevel
{
    Full = 0,
    Reduced = 1,
    Off = 2
}

public enum SidebarCollapseMode
{
    Expanded = 0,
    IconsOnly = 1
}

public sealed class UiSettings
{
    public bool AnimationsEnabled { get; set; } = true;
    public AnimationLevel AnimationLevel { get; set; } = AnimationLevel.Full;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public string AccentKey { get; set; } = "Cyan";
    public string DefaultBrowserPreference { get; set; } = "Brave";
    public int OverlayOpacityPercent { get; set; } = 85;
    public int OverlayScalePercent { get; set; } = 100;
    public string OverlayPosition { get; set; } = "Lewy górny";
    public double WindowWidth { get; set; } = 1380;
    public double WindowHeight { get; set; } = 900;
    public string SelectedPage { get; set; } = "home";
    public SidebarCollapseMode SidebarMode { get; set; } = SidebarCollapseMode.Expanded;
    public bool GlassEffectsEnabled { get; set; } = true;
    public bool UsePerCoreCharts { get; set; } = true;

    [JsonIgnore]
    public string AccentColor => AccentKey switch
    {
        "Violet" => "#A878FF",
        "Mint" => "#4BEFB9",
        "Rose" => "#FF6B9D",
        "Amber" => "#FFC462",
        "Blue" => "#4C8BFF",
        _ => "#22E0FF" // Cyan (default Aurora)
    };
}
