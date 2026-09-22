namespace SentinelX.Services.Settings;

/// <summary>One settings store, shared with the compatibility UI; no second JSON schema.</summary>
public sealed class SettingsService(AppSettingsService store) : ISettingsService
{
    public SentinelSettings Current => store.Settings;
    public string? LastError => store.LastError;
    public event Action? Changed { add => store.Changed += value; remove => store.Changed -= value; }
    public void Save() => store.Save();
    public void ResetSection(string section) => store.ResetSection(section);
}
