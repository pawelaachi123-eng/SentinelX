using System.Text.RegularExpressions;

namespace SentinelX;

public sealed record AppLaunchPlan(IReadOnlyList<string> Targets, string? Error)
{
    public bool IsValid => Error == null && Targets.Count > 0;
    public const int MaximumTargets = 6;
    private static readonly Regex separators = new(@"\s+(?:i|oraz|a\s+potem|potem|następnie|nastepnie)\s+|\s*[,;+]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AppLaunchPlan Parse(string input)
    {
        input = input.Trim().TrimEnd('.', '!', '?').Trim();
        if (input.Length is 0 or > 1000) return new([], "Podaj krótką nazwę aplikacji lub listę do sześciu aplikacji.");
        // A quoted name is always one title; conjunctions can be part of an installed application's name.
        if (input.Length >= 2 && ((input[0] == '"' && input[^1] == '"') || (input[0] == '„' && input[^1] == '”')))
            return new([input[1..^1]], null);
        if (AppLauncherService.IsKnownLaunchTarget(input) || AppLauncherService.IsSafeWebUrl(input)) return new([input], null);
        string[] parts = separators.Split(input).Select(CleanStep).Where(x => x.Length > 0).ToArray();
        if (parts.Length <= 1) return new([input], null);
        if (parts.Length > MaximumTargets) return new([], $"Jedna komenda może uruchamiać najwyżej {MaximumTargets} aplikacji.");
        // Never guess an arbitrary split: 'Ratchet i Clank' must remain one Start Menu lookup.
        if (parts.All(x => AppLauncherService.IsKnownLaunchTarget(x) || AppLauncherService.IsSafeWebUrl(x)))
            return new(parts.DistinctBy(AppLauncherService.CanonicalizeLaunchTarget).ToArray(), null);
        bool hasKnownPart = parts.Any(AppLauncherService.IsKnownLaunchTarget);
        return hasKnownPart
            ? new([], "Nie rozpoznano całej listy aplikacji. Nie uruchomiono żadnego kroku. Podaj aplikacje osobno albo pełną nazwę w cudzysłowie.")
            : new([input], null);
    }

    private static string CleanStep(string step) => Regex.Replace(step.Trim(), @"^(?:(?:włącz|wlacz|uruchom|otwórz|otworz|odpal)\s+)?(?:(?:mi|proszę|prosze)\s+)*", "", RegexOptions.IgnoreCase).Trim();

    public static bool TryParseCommand(string command, out string target)
    {
        var match = Regex.Match(command.Trim(), @"^(?:(?:proszę|prosze)\s+)?(?:(?:możesz|mozesz)\s+)?(?:włącz|wlacz|uruchom|otwórz|otworz|odpal)(?:\s+(?:mi|proszę|prosze))*\s+(.+?)\s*[.!?]*$", RegexOptions.IgnoreCase);
        target = match.Success ? match.Groups[1].Value.Trim() : "";
        return target.Length > 0;
    }
}
