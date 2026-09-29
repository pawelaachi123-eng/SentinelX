namespace SentinelX;

/// <summary>Ustawienia narzędzia telefonicznego. Most jest WYŁĄCZONY domyślnie i startuje
/// wyłącznie na wyraźne polecenie („włącz most telefoniczny”). Token parowania generuje Sentinel.</summary>
public sealed class PhoneSettings
{
    /// <summary>Numer kontaktowy właściciela — Sentinel podaje go rozmówcom (nie pyta ponownie).</summary>
    public string CallbackNumber { get; set; } = "786843433";

    /// <summary>Imię/nazwisko, którym Sentinel przedstawia się w rozmowie.</summary>
    public string OwnerName { get; set; } = "";

    public int BridgePort { get; set; } = 7877;

    /// <summary>Token parowania mostu — generowany przy pierwszym włączeniu, znika z logów, nigdy do chmury.</summary>
    public string BridgeToken { get; set; } = "";

    /// <summary>Zgoda na użycie próbek WŁASNEGO głosu (klonowanie cudzego głosu bez zgody jest zablokowane).</summary>
    public bool AllowOwnVoiceClone { get; set; } = false;
}
