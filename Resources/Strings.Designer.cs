namespace SentinelX.Resources;

/// <summary>Typed accessor for the localizable shell strings (EN neutral +
/// pl-PL satellite). Hand-maintained in the resx-generator pattern: dotnet
/// build does not run the Visual Studio resx generator on CI, so this file
/// is committed and must stay in sync with Strings.resx (keys and fallbacks).</summary>
public static class Strings
{
    private static System.Resources.ResourceManager? manager;
    private static System.Resources.ResourceManager Manager =>
        manager ??= new System.Resources.ResourceManager("SentinelX.Resources.Strings", typeof(Strings).Assembly);
    private static string Text(string key, string fallback) =>
        Manager.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? fallback;
    public static string NavCenter => Text(nameof(NavCenter), "Command Hub");
    public static string NavBase => Text(nameof(NavBase), "Base & Agent");
    public static string NavMaintenance => Text(nameof(NavMaintenance), "Diagnostics / updates");
    public static string NavTools => Text(nameof(NavTools), "Tools");
    public static string NavMemory => Text(nameof(NavMemory), "Memory");
    public static string NavProjects => Text(nameof(NavProjects), "Projects");
    public static string NavSettings => Text(nameof(NavSettings), "Settings");
    public static string ThemeDark => Text(nameof(ThemeDark), "Dark");
    public static string ThemeDeepDark => Text(nameof(ThemeDeepDark), "Black");
    public static string ThemeLight => Text(nameof(ThemeLight), "Light");
    public static string ThemeSystem => Text(nameof(ThemeSystem), "Like Windows");
    public static string SearchButton => Text(nameof(SearchButton), "⌕  Search · Ctrl+K");
    public static string SearchHelp => Text(nameof(SearchHelp), "Open command palette");
    public static string ToolsButton => Text(nameof(ToolsButton), "🧰  Tools");
    public static string ToolsHelp => Text(nameof(ToolsHelp), "Open tools catalog");
    public static string NowOnPc => Text(nameof(NowOnPc), "NOW ON THIS PC");
    public static string ThemeCaption => Text(nameof(ThemeCaption), "THEME");
    public static string ReadinessButton => Text(nameof(ReadinessButton), "◇  Configuration & readiness");
    public static string EmergencyStop => Text(nameof(EmergencyStop), "ϟ  EMERGENCY STOP");
    public static string EmergencyStopTip => Text(nameof(EmergencyStopTip), "Ctrl+Shift+X · blocks new actions and mutes voice");
    public static string ResumeButton => Text(nameof(ResumeButton), "Resume Sentinel");
    public static string ExitButton => Text(nameof(ExitButton), "Exit app");
    public static string VersionHelp => Text(nameof(VersionHelp), "Sentinel X version");
}
