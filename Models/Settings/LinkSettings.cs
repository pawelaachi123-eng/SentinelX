namespace SentinelX;

/// <summary>Connection to the user's phone (Services/Link). On by default: it is part of "nothing to look after". The PC only accepts
/// devices from the local network, and every new phone must be approved with one click on the PC.</summary>
public sealed class LinkSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>First port tried; if it is busy the next nine are used. The phone finds the actual port by itself.</summary>
    public int Port { get; set; } = 43180;
    /// <summary>Answer the Android app's "where is my PC?" broadcast so nobody has to type an address.</summary>
    public bool Discovery { get; set; } = true;
}
