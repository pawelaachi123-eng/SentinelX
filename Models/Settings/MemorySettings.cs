namespace SentinelX;

/// <summary>Memory and privacy toggles. Each is enforced at the point of use, so edits apply without a restart.</summary>
public sealed class MemorySettings
{
    /// <summary>Persist conversation turns. When off, commands and replies leave no conversation record.</summary>
    public bool SaveConversations { get; set; } = true;
    /// <summary>Allow the model to read recent conversation history (follow-ups). Independent of saving.</summary>
    public bool UseHistoryForAi { get; set; } = true;
    /// <summary>Allow storing explicit memories ("zapamiętaj…", panel). When off, nothing new is remembered.</summary>
    public bool SaveMemories { get; set; } = true;
    /// <summary>Allow the model to read profile and memories. Independent of saving them.</summary>
    public bool UseMemoriesForAi { get; set; } = true;
    /// <summary>Days to keep conversation history. 0 = keep. Explicit memories are never auto-pruned.</summary>
    public int RetentionDays { get; set; }
    /// <summary>Record which memories/conversation slices were sent to the model (labels and reasons, not prompts).</summary>
    public bool ContextPreviewEnabled { get; set; } = true;
    /// <summary>When enabled, Sentinel blocks its own outbound internet requests. Local loopback AI and local tools remain available.</summary>
    public bool LocalOnlyMode { get; set; } = true;
    /// <summary>How many months of conversations stay in the live store. Older months are archived to
    /// Memory/Archives and pruned at startup. 0 = keep everything, nothing is archived automatically.</summary>
    public int ArchiveMonths { get; set; } = 1;
}
