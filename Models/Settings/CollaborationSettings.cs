namespace SentinelX;

public sealed class CollaborationSettings
{
    /// <summary>Master switch — if false, screen capture and co-pilot cursor are completely disabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Second cursor color (hex, e.g. #00D4FF for Sentinel cyan, #FF3B30 for red).</summary>
    public string CursorColor { get; set; } = "#00D4FF";

    /// <summary>Live preview FPS (1-10). Lower = less CPU. Default 2.</summary>
    public int LiveFps { get; set; } = 2;

    /// <summary>Capture quality 30-100 (JPEG). Lower = less RAM.</summary>
    public int CaptureQuality { get; set; } = 70;

    /// <summary>Whether to show red border when collaboration is active.</summary>
    public bool ShowBorderIndicator { get; set; } = true;

    /// <summary>Whether to save captures to disk for audit (local only).</summary>
    public bool SaveCaptures { get; set; } = false;

    /// <summary>Max captures to keep on disk (if SaveCaptures).</summary>
    public int MaxSavedCaptures { get; set; } = 50;
}
