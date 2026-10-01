namespace SentinelX.Services.Link;

/// <summary>Two hooks that let the text command „telefon” in the chat talk to the phone link without the command router knowing
/// about it: <see cref="Describe"/> answers with the current state and address, <see cref="Open"/> shows the phone panel (QR code).
/// Both are set once at start-up by the caretaker and the desktop service.</summary>
public static class PhoneHint
{
    public static Func<string>? Describe { get; set; }
    public static Action? Open { get; set; }
}
