using System.Text.RegularExpressions;
using SentinelX.Services.Apps;
using SentinelX.Services.Notifications;

namespace SentinelX.Services.Automation;

/// <summary>Launches only a known application target; it never accepts a path, arguments, or shell text.</summary>
public sealed class LaunchApplicationAutomationAction(IAppLauncherService launcher) : IAutomationActionHandler
{
    public AutomationActionDescriptor Descriptor { get; } = new(
        "launch-app", "Uruchom aplikację", "Windows", "Otwiera jedną rozpoznaną aplikację bez argumentów.", "Uruchamianie aplikacji (jawnie wybrane przez użytkownika)");

    public bool TryValidate(string? parameter, out string normalized, out string error)
    {
        normalized = AppLauncherService.CanonicalizeLaunchTarget(parameter ?? "");
        if (normalized.Length is > 0 and <= 80 && AppLauncherService.IsKnownLaunchTarget(normalized))
        {
            error = "";
            return true;
        }
        normalized = "";
        error = "Wybierz rozpoznaną aplikację z Sentinel (np. Brave, Discord, Steam lub Ustawienia). Ścieżki i argumenty nie są obsługiwane.";
        return false;
    }

    public async Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken)
    {
        ActionExecutionResult result = await launcher.LaunchAsync(normalizedParameter, cancellationToken).ConfigureAwait(false);
        return new(result.Success, result.Status, result.Message, result.Evidence);
    }
}

/// <summary>Opens only validated HTTP/HTTPS URLs; no URI schemes, credentials, or shell interpolation.</summary>
public sealed class OpenUrlAutomationAction(IAppLauncherService launcher) : IAutomationActionHandler
{
    public AutomationActionDescriptor Descriptor { get; } = new(
        "open-url", "Otwórz adres WWW", "Sieć", "Otwiera pojedynczy adres HTTP lub HTTPS w przeglądarce.", "Otwieranie adresu WWW (jawnie wybranego przez użytkownika)");

    public bool TryValidate(string? parameter, out string normalized, out string error)
    {
        string input = (parameter ?? "").Trim();
        if (input.Length <= 2048 && AppLauncherService.IsSafeWebUrl(input) && Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            normalized = uri.AbsoluteUri;
            error = "";
            return true;
        }
        normalized = "";
        error = "Podaj pełny adres HTTP/HTTPS bez danych logowania (np. https://example.com).";
        return false;
    }

    public async Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken)
    {
        ActionExecutionResult result = await launcher.LaunchAsync(normalizedParameter, cancellationToken).ConfigureAwait(false);
        return new(result.Success, result.Status, result.Message, result.Evidence);
    }
}

/// <summary>Displays a short user-authored message via the app notification service.</summary>
public sealed class ShowNotificationAutomationAction(INotificationService notifications) : IAutomationActionHandler
{
    private static readonly Regex HasVisibleContent = new(@"\S", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public AutomationActionDescriptor Descriptor { get; } = new(
        "notify", "Pokaż powiadomienie", "Sentinel", "Pokazuje krótki komunikat na komputerze i telefonie (jeśli sparowany).", "Powiadomienia Sentinel");

    public bool TryValidate(string? parameter, out string normalized, out string error)
    {
        normalized = (parameter ?? "").Trim();
        if (normalized.Length is > 0 and <= 500 && HasVisibleContent.IsMatch(normalized) && !normalized.Any(char.IsControl))
        {
            error = "";
            return true;
        }
        normalized = "";
        error = "Treść powiadomienia musi mieć od 1 do 500 znaków i nie może zawierać znaków sterujących.";
        return false;
    }

    public Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        notifications.Publish("Automation", "Sentinel · automatyzacja", normalizedParameter);
        return Task.FromResult(new AutomationActionResult(true, "QUEUED", "Powiadomienie przekazano do centrum powiadomień.", "Wysłano do lokalnego zasobnika aplikacji i krótkiej kolejki alertów telefonu."));
    }
}
