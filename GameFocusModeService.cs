namespace SentinelX;

/// <summary>Sentinel-only focus profile. It never changes Windows power, process, network, or Game Mode settings.</summary>
public sealed class GameFocusModeService
{
    private int active;
    public bool IsActive => Volatile.Read(ref active) != 0;
    public string Enable() => Interlocked.Exchange(ref active, 1) == 1
        ? "Tryb gry Sentinel jest już aktywny. Ustawienia Windows nie zostały zmienione."
        : "Włączono profil skupienia Sentinel: alerty Watch są wstrzymane, a monitoring CPU/RAM/GPU pozostaje aktywny. Nie zmieniono ustawień Windows.";
    public string Disable() => Interlocked.Exchange(ref active, 0) == 0
        ? "Tryb gry Sentinel był już wyłączony."
        : "Wyłączono profil Sentinel. Alerty Watch wróciły do ustawienia sprzed profilu; ustawienia Windows nie były zmieniane.";
}
