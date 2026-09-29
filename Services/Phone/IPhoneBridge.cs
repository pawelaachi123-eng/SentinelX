namespace SentinelX.Services.Phone;

/// <summary>Stan połączenia zgłaszany PRAWDZIWIE przez telefon (TelephonyManager) — nigdy nie udawany.</summary>
public enum PhoneCallState
{
    /// <summary>Brak aktywnego połączenia / położona słuchawka.</summary>
    Idle,
    /// <summary>Telefon wybiera numer (stan zgłoszony przez system telefonu).</summary>
    Dialing,
    /// <summary>Połączenie REALNIE nawiązane — druga strona odebrała (OFFHOOK).</summary>
    OffHook,
    /// <summary>Połączenie zakończone.</summary>
    Ended,
    /// <summary>Telefon zgłosił niepowodzenie wybierania.</summary>
    Failed,
}

/// <summary>Most komputer ↔ aplikacja pomocnicza na Androidzie. Telefon dzwoni z WŁASNEJ karty
/// SIM/eSIM (ACTION_CALL), nie z VoIP. Audio rozmówcy dociera przez relację głośnika
/// (Android nie udostępnia aplikacjom audio rozmowy SIM) — most jest uczciwy co do tego ograniczenia.</summary>
public interface IPhoneBridge
{
    bool IsConnected { get; }
    /// <summary>Nazwa urządzenia zgłoszona przy parowaniu (pusta, gdy brak połączenia).</summary>
    string ConnectedDevice { get; }
    PhoneCallState CallState { get; }
    /// <summary>Liczba sekund od OFFHOOK (0, gdy brak połączenia głosowego).</summary>
    int CallDurationSeconds { get; }
    event Action<PhoneCallState>? CallStateChanged;
    /// <summary>Audio od rozmówcy: 16 kHz, mono, float -1..1 (relacja głośnika przez mikrofon telefonu).</summary>
    event Action<float[]>? AudioUp;
    /// <summary>Diagnostyka mostu (łączenie, pary tokenów, błędy gniazda).</summary>
    event Action<string>? Log;
    string Status();
    /// <summary>Włącza nasłuch LAN. Zawsze JAWNE polecenie użytkownika — nigdy automat.</summary>
    void Start(int port, string token);
    void Stop();
    /// <summary>Poleca telefonowi wybranie numeru z jego karty SIM.</summary>
    Task DialAsync(string number, string callId);
    /// <summary>Wysyła syntezowaną mowę (16 kHz mono) do odtworzenia w głośniku telefonu.</summary>
    void SendTtsAudio(float[] samples);
    /// <summary>Poleca telefonowi zakończyć połączenie.</summary>
    void EndCall();
}
