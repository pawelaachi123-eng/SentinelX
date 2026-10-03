using SentinelX.Core;

namespace SentinelX.Services.Voice;

public enum VoiceSessionCommand
{
    None,
    On,
    Off
}

/// <summary>Small deterministic, exact-match control vocabulary for spoken Sentinel on/off.</summary>
public static class VoiceSessionCommandParser
{
    public static VoiceSessionCommand Parse(string? command)
    {
        string normalized = CommandText.Normalize(command ?? "");
        return normalized switch
        {
            "on" or "wlacz" or "wlacz glos" or "wlacz nasluch" or "wlacz rozmowe" => VoiceSessionCommand.On,
            "off" or "wylacz" or "wylacz glos" or "wylacz nasluch" or "wylacz rozmowe" => VoiceSessionCommand.Off,
            _ => VoiceSessionCommand.None
        };
    }
}
