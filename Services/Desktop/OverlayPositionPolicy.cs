namespace SentinelX.Services.Desktop;

/// <summary>Maps the dragged overlay's center to one of the four supported saved corner presets.</summary>
internal static class OverlayPositionPolicy
{
    internal static string GetNearestCorner(double overlayLeft, double overlayTop, double overlayWidth,
        double overlayHeight, double workAreaLeft, double workAreaTop, double workAreaWidth, double workAreaHeight)
    {
        bool right = overlayLeft + overlayWidth / 2 >= workAreaLeft + workAreaWidth / 2;
        bool bottom = overlayTop + overlayHeight / 2 >= workAreaTop + workAreaHeight / 2;
        return (right ? "Prawy " : "Lewy ") + (bottom ? "dolny" : "górny");
    }
}
