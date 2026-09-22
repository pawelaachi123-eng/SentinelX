namespace SentinelX.Services.Settings;

public interface ISettingsService
{
    SentinelSettings Current { get; }
    string? LastError { get; }
    event Action? Changed;
    void Save();
    void ResetSection(string section);
}
