using System.Text.Json.Serialization;

namespace SentinelX.Services.Link;

/// <summary>
/// Wire contracts for the desktop ↔ companion protocol. Keep API v1 additive: older web/Android clients ignore new response fields,
/// and requests from older clients remain valid. A breaking wire change requires a new API version.
/// </summary>
public static class LinkProtocol
{
    public const int CurrentVersion = 1;

    public static IReadOnlyList<LinkHostCapability> HostCapabilities { get; } = Array.AsReadOnly(new LinkHostCapability[]
    {
        LinkHostCapability.SystemMetrics,
        LinkHostCapability.ProcessList,
        LinkHostCapability.AssistantChat,
        LinkHostCapability.AssistantControl,
        LinkHostCapability.Tasks,
        LinkHostCapability.Reminders,
        LinkHostCapability.Notes,
        LinkHostCapability.Alerts,
        LinkHostCapability.DeviceManagement,
    });
}

/// <summary>Capabilities actually served by this PC's v1 link API. Values serialize as lower-camel-case identifiers.</summary>
public enum LinkHostCapability
{
    SystemMetrics,
    ProcessList,
    AssistantChat,
    AssistantControl,
    Tasks,
    Reminders,
    Notes,
    Alerts,
    DeviceManagement,
}

/// <summary>Validated Android/browser capabilities reported during pairing.</summary>
public sealed record LinkPhoneCapabilities(bool Notifications = false, bool VoiceInput = false, bool WakeOnLan = false)
{
    public static LinkPhoneCapabilities None { get; } = new();

    /// <summary>Unknown identifiers are ignored so a future client can still pair with an older PC.</summary>
    public static LinkPhoneCapabilities FromWire(IEnumerable<string>? identifiers)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        if (identifiers != null)
        {
            foreach (string? value in identifiers.Take(16))
            {
                if (value is { Length: > 0 and <= 40 } && value.All(char.IsAsciiLetterOrDigit)) values.Add(value);
            }
        }

        return new LinkPhoneCapabilities(
            Notifications: values.Contains("notifications"),
            VoiceInput: values.Contains("voiceInput"),
            WakeOnLan: values.Contains("wakeOnLan"));
    }

    public IReadOnlyList<string> ToWireValues()
    {
        var values = new List<string>(3);
        if (Notifications) values.Add("notifications");
        if (VoiceInput) values.Add("voiceInput");
        if (WakeOnLan) values.Add("wakeOnLan");
        return values;
    }

    public string Describe()
    {
        var values = new List<string>(3);
        if (Notifications) values.Add("powiadomienia");
        if (VoiceInput) values.Add("rozpoznawanie mowy");
        if (WakeOnLan) values.Add("Wake-on-LAN");
        return values.Count == 0 ? "brak zgłoszonych funkcji" : string.Join(" · ", values);
    }
}

public sealed record LinkHelloResponse(string App, int Api, string Version, string Name, string Fingerprint,
    IReadOnlyList<LinkHostCapability> Capabilities);

public sealed record LinkDiscoveryResponse(string App, int Api, string Name, int Port, string Fingerprint);

public sealed record LinkPairRequest(string? Device = null, string? Nonce = null, string[]? Capabilities = null);
public sealed record LinkPairStartedResponse(string Id, string Nonce, int ExpiresIn, string Sas);
public sealed record LinkPairingStateResponse(string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeviceId = null);

public sealed record LinkChatRequest(string? Text = null);
public sealed record LinkControlRequest(string? Action = null);
public sealed record LinkTaskRequest(string? Title = null, string? Priority = null, string? Due = null);
public sealed record LinkTaskStatusRequest(string? Id = null, string? Status = null);
public sealed record LinkReminderRequest(string? Text = null, string? At = null);
public sealed record LinkIdRequest(string? Id = null);
public sealed record LinkNoteRequest(string? Text = null);

public sealed record LinkErrorResponse(string Error);
public sealed record LinkOkResponse(bool Ok);
public sealed record LinkIdResponse(bool Ok, string Id);
public sealed record LinkControlResponse(bool Ok, bool Stopped);
public sealed record LinkChatStartedResponse();
public sealed record LinkChatDeltaResponse(string Text);
public sealed record LinkChatDoneResponse(string Text, string Status, string Evidence, long ElapsedMs);
public sealed record LinkNoteAddedResponse(bool Ok, string Result);

public sealed record LinkPcState(string Name, string Version, string Uptime, string Time,
    IReadOnlyList<string> Mac, IReadOnlyList<string> Addresses);
public sealed record LinkDiskState(string Name, double? UsedGb, double? TotalGb);
public sealed record LinkProcessState(string Name, int Pid, double? MemoryMb, double? Cpu);
public sealed record LinkMetricsState(double? Cpu, double? RamUsed, double? RamTotal, double? Gpu, string Game, string Network,
    IReadOnlyList<LinkDiskState> Disks, IReadOnlyList<LinkProcessState> Processes);
public sealed record LinkEngineState(string State, string Message, double? Progress, string Model, IReadOnlyList<string> Installed);
public sealed record LinkAssistantState(bool Busy, bool Stopped, bool Pending, string PendingSummary);
public sealed record LinkCareState(bool Ok, string Text);
public sealed record LinkCountsState(int Tasks, int Reminders, int Notes, int Alerts);
public sealed record LinkStateResponse(LinkPcState Pc, LinkMetricsState Metrics, LinkEngineState Engine, LinkAssistantState Assistant,
    LinkCareState Care, LinkCountsState Counts, long AlertsLast, IReadOnlyList<LinkHostCapability> Capabilities);

public sealed record LinkTaskState(string Id, string Title, string Priority, string Status, string? Due, string Project);
public sealed record LinkReminderState(string Id, string Text, string? At, bool Missed);
public sealed record LinkTasksResponse(IReadOnlyList<LinkTaskState> Tasks, IReadOnlyList<LinkReminderState> Reminders);
public sealed record LinkNoteState(string Id, string Text, string Category, bool Pinned, string? At);
public sealed record LinkNotesResponse(IReadOnlyList<LinkNoteState> Notes);
public sealed record LinkAlertState(long Id, string At, string Level, string Title, string Text);
public sealed record LinkAlertsResponse(IReadOnlyList<LinkAlertState> Alerts, long Last);
public sealed record LinkDeviceSummary(string Id, string Name, string AddedAt, string LastSeen, bool Current,
    IReadOnlyList<string> Capabilities);
public sealed record LinkDevicesResponse(IReadOnlyList<LinkDeviceSummary> Devices);
