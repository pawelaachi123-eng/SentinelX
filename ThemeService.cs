using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SentinelX;

public static class ThemeService
{
    public static void Apply(FrameworkElement view, UiSettings settings)
    {
        bool light = false;
        if (settings.Theme == "System")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        bool deep = settings.Theme == "Deep Dark";
        void Brush(string key, string color) => view.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Brush("ShellBrush", light ? "#EEF2F8" : deep ? "#090B11" : "#101521");
        Brush("SidebarBrush", light ? "#E3EAF4" : deep ? "#0E1019" : "#141B2D");
        Brush("Panel", light ? "#FFFFFF" : deep ? "#121520" : "#192235");
        Brush("Panel2", light ? "#DDE6F4" : deep ? "#1A1D2C" : "#222E45");
        Brush("InputBrush", light ? "#F4F7FD" : deep ? "#0C0F18" : "#111A2B");
        Brush("TextBrush", light ? "#18253D" : "#EDF2FC");
        Brush("Muted", light ? "#50607C" : "#A4B1C9");
        Brush("Soft", light ? "#304664" : "#DCE7F7");
        Brush("BorderBrush", light ? "#C5D0E4" : deep ? "#2A2E42" : "#344360");
        Brush("Accent", settings.AccentColor);
        Brush("PrimaryBrush", settings.AccentColor);
        Brush("UserBubble", light ? "#DEEAFB" : "#1B3351");
        Brush("AssistantBubble", light ? "#FFFFFF" : deep ? "#161B27" : "#1B263B");
        view.SetResourceReference(ControlForegroundProperty, "TextBrush");
    }
    private static DependencyProperty ControlForegroundProperty => System.Windows.Controls.Control.ForegroundProperty;
}
