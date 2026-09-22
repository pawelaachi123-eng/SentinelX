namespace SentinelX.Models;
public enum ReadinessState { NotChecked, Ready, NeedsSetup, Unavailable }
public sealed record ReadinessCheck(string Key, string Title, ReadinessState State, string Detail, string PageKey, string NextStep)
{
    public string StateLabel => State switch
    {
        ReadinessState.Ready => "GOTOWE", ReadinessState.NeedsSetup => "DO KONFIGURACJI",
        ReadinessState.Unavailable => "NIEDOSTĘPNE", _ => "NIESPRAWDZONE"
    };
}
