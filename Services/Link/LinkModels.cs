namespace SentinelX.Services.Link;

/// <summary>One entry of the phone-facing alert journal.</summary>
public sealed record LinkAlert(long Id, DateTimeOffset At, string Level, string Title, string Text);

/// <summary>A paired phone as shown in the UI. The token itself is never stored, only its SHA-256.</summary>
public sealed record LinkDeviceInfo(string Id, string Name, DateTimeOffset AddedAt, DateTimeOffset LastSeen)
{
    public LinkPhoneCapabilities Capabilities { get; init; } = LinkPhoneCapabilities.None;
    public string CapabilitiesText => Capabilities.Describe();
}

/// <summary>What the user sees on the PC when a phone asks to connect.</summary>
public sealed record PairingRequestInfo(string Id, string DeviceName, string RemoteAddress, string Sas, DateTimeOffset ExpiresAt)
{
    public LinkPhoneCapabilities Capabilities { get; init; } = LinkPhoneCapabilities.None;
}

public enum PairingState { Pending, Approved, Denied, Expired }

/// <summary>Facts the link needs from the rest of the app without knowing the engine or care services.</summary>
public sealed record LinkInfo(string Version, string EngineState, string EngineMessage, double EngineProgress, string EngineModel,
    IReadOnlyList<string> EngineInstalled, bool CareOk, string CareText);

/// <summary>Implemented by the WPF layer: shows the approval window and reports the click back.</summary>
public interface ILinkApprovalUi
{
    void ShowRequest(PairingRequestInfo request, Action<bool> decide);
    void CloseRequest(string requestId);
}
