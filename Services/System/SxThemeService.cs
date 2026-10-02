using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SentinelX.Services.System;

public static class SentinelThemes
{
    public static IReadOnlyList<string> Accents { get; } = new[] { "Cyan", "Violet", "Mint", "Rose", "Amber", "Blue" };
    public static IReadOnlyList<string> Modes { get; } = new[] { "Dark", "Light", "System" };
}

/// <summary>
/// 2.0 · Przełączanie motywów Dark/Light/System w locie, bez restartu aplikacji.
/// Zamienia wartości kluczowych pędzli w Application.Current.Resources.
/// </summary>
public sealed class SxThemeService
{
    private readonly Func<string> getMode;
    private readonly Func<string> getAccent;
    private string appliedMode = "";
    private string appliedAccent = "";

    public SxThemeService(Func<string> getMode, Func<string> getAccent)
    {
        this.getMode = getMode;
        this.getAccent = getAccent;
    }

    public void Apply()
    {
        string mode = getMode() ?? "Dark";
        string accent = getAccent() ?? "Cyan";
        bool dark = mode == "Dark" || (mode == "System" && IsSystemDark());
        ApplyResources(dark, accent);
        appliedMode = mode;
        appliedAccent = accent;
    }

    public void ApplyIfChanged()
    {
        string m = getMode() ?? "Dark", a = getAccent() ?? "Cyan";
        if (m != appliedMode || a != appliedAccent) Apply();
    }

    private static bool IsSystemDark()
    {
        try
        {
            // Domyślnie Dark na nie-windows; na Windows sprawdzamy rejestr
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch { }
        return true;
    }

    private void ApplyResources(bool dark, string accentKey)
    {
        var r = Application.Current.Resources;
        Color accent = AccentColorFor(accentKey);
        Color accentSoft = Color.FromArgb(60, accent.R, accent.G, accent.B);
        Color accentDim = Color.FromArgb(220, (byte)(accent.R / 4), (byte)(accent.G / 4), (byte)(accent.B / 4));

        if (dark)
        {
            Set(r, "SxWindow", Color.FromRgb(0x0A, 0x0F, 0x17));
            Set(r, "SxWindowBg", Color.FromRgb(0x05, 0x08, 0x0D));
            Set(r, "SxSidebar", Color.FromRgb(0x0B, 0x11, 0x1A));
            Set(r, "SxSurface", Color.FromRgb(0x0F, 0x17, 0x22));
            Set(r, "SxSurfaceHover", Color.FromRgb(0x16, 0x20, 0x30));
            Set(r, "SxSurfaceActive", Color.FromRgb(0x1C, 0x27, 0x37));
            Set(r, "SxAuroraPanel", Color.FromRgb(0x0E, 0x17, 0x24));
            Set(r, "SxText", Color.FromRgb(0xE6, 0xF1, 0xFF));
            Set(r, "SxTextBody", Color.FromRgb(0xB8, 0xC4, 0xD4));
            Set(r, "SxTextMuted", Color.FromRgb(0x6B, 0x7A, 0x8F));
            Set(r, "SxTextFaint", Color.FromRgb(0x44, 0x50, 0x64));
            Set(r, "SxBorderSubtle", Color.FromRgb(0x16, 0x20, 0x30));
            Set(r, "SxBorder", Color.FromRgb(0x1E, 0x2A, 0x3C));
            Set(r, "SxBorderGlow", Color.FromRgb(0x2A, 0x3F, 0x5A));
        }
        else
        {
            Set(r, "SxWindow", Color.FromRgb(0xF5, 0xF7, 0xFB));
            Set(r, "SxWindowBg", Color.FromRgb(0xEB, 0xEE, 0xF5));
            Set(r, "SxSidebar", Color.FromRgb(0xEC, 0xEF, 0xF5));
            Set(r, "SxSurface", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set(r, "SxSurfaceHover", Color.FromRgb(0xEE, 0xF2, 0xF8));
            Set(r, "SxSurfaceActive", Color.FromRgb(0xE3, 0xE8, 0xF0));
            Set(r, "SxAuroraPanel", Color.FromRgb(0xF8, 0xFA, 0xFD));
            Set(r, "SxText", Color.FromRgb(0x0B, 0x12, 0x20));
            Set(r, "SxTextBody", Color.FromRgb(0x32, 0x40, 0x56));
            Set(r, "SxTextMuted", Color.FromRgb(0x66, 0x72, 0x86));
            Set(r, "SxTextFaint", Color.FromRgb(0x95, 0xA0, 0xB2));
            Set(r, "SxBorderSubtle", Color.FromRgb(0xE4, 0xE8, 0xEF));
            Set(r, "SxBorder", Color.FromRgb(0xD3, 0xD9, 0xE3));
            Set(r, "SxBorderGlow", Color.FromRgb(0xB6, 0xD2, 0xDC));
        }
        Set(r, "SxAccent", accent);
        Set(r, "SxAccentSoft", accentSoft);
        Set(r, "SxAccentCyan", accent);
        Set(r, "SxAccentDim", accentDim);
    }

    private static void Set(ResourceDictionary r, string key, Color color)
    {
        r[key] = new SolidColorBrush(color) { Opacity = 1.0 };
        if (r[key] is SolidColorBrush b) b.Freeze();
    }

    public static Color AccentColorFor(string key) => key switch
    {
        "Violet" => Color.FromRgb(0xA8, 0x78, 0xFF),
        "Mint" => Color.FromRgb(0x4B, 0xEF, 0xB9),
        "Rose" => Color.FromRgb(0xFF, 0x6B, 0x9D),
        "Amber" => Color.FromRgb(0xFF, 0xC4, 0x62),
        "Blue" => Color.FromRgb(0x4C, 0x8B, 0xFF),
        _ => Color.FromRgb(0x22, 0xE0, 0xFF)
    };
}
