namespace SentinelX.Core;

/// <summary>Execution-scoped human approval capability. Absent/closed context is never approval.</summary>
public sealed class ApprovalContext : IDisposable
{
    private static readonly AsyncLocal<ApprovalContext?> Current = new();
    private readonly ApprovalContext? previous;
    private readonly bool allowed;
    private readonly string? expectedType;
    private bool closed;
    private ApprovalContext(bool allowed, string? expectedType)
    { previous = Current.Value; this.allowed = allowed; this.expectedType = expectedType; Current.Value = this; }
    public static ApprovalContext Begin(string input, bool fromVoice)
    {
        string text = CommandText.Normalize(input);
        return new(!fromVoice && CommandText.IsApproval(text), text == "potwierdz usuniecie wspomnien" ? "MEMORY_CLEAR_ALL" : null);
    }
    public static bool Allows(string actionType) => Current.Value is { } value && !Volatile.Read(ref value.closed)
        && value.allowed && (value.expectedType == null || value.expectedType == actionType);
    public void Dispose() { Volatile.Write(ref closed, true); Current.Value = previous; }
}
