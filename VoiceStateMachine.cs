namespace SentinelX;

/// <summary>Stany cyklu życia głosu zgodne ze specyfikacją: OFF … ERROR.</summary>
public enum VoiceStateKind
{
    OFF,
    STANDBY,
    LISTENING,
    TRANSCRIBING,
    THINKING,
    EXECUTING,
    SPEAKING,
    ERROR,
}

/// <summary>Nielegalne przejście między stanami głosu.</summary>
public sealed class VoiceStateTransitionException(string from, string to) : Exception($"Nielegalne przejście głosu: {from} → {to}");

/// <summary>Informacja o bieżącym stanie — gotowa do wyświetlenia w UI i trayu.</summary>
public sealed record VoiceStateInfo(VoiceStateKind State, string Label, string Detail, bool Busy, bool MicrophoneOpen);

/// <summary>
/// Jawna maszyna stanów głosu. Legalne przejścia są sprawdzane; każde przejście trafia do
/// ograniczonej historii. Wywołania są bezpieczne między wątkami, a błąd obserwatora nie
/// przerywa pracy mikrofonu ani rozpoznawania.
/// </summary>
public sealed class VoiceStateMachine
{
    private readonly object syncRoot = new();
    private VoiceStateKind state = VoiceStateKind.OFF;
    private DateTime stateSince = DateTime.Now;
    private string detail = "";
    private readonly Queue<(DateTime At, VoiceStateKind From, VoiceStateKind To, string Reason)> history = new();
    private const int MaxHistory = 64;

    private static readonly Dictionary<VoiceStateKind, string> Labels = new()
    {
        [VoiceStateKind.OFF] = "Mikrofon wyłączony",
        [VoiceStateKind.STANDBY] = "Czuwanie — powiedz „Sentinel”",
        [VoiceStateKind.LISTENING] = "Słucham",
        [VoiceStateKind.TRANSCRIBING] = "Rozpoznaję mowę",
        [VoiceStateKind.THINKING] = "Przetwarzam polecenie",
        [VoiceStateKind.EXECUTING] = "Wykonuję polecenie",
        [VoiceStateKind.SPEAKING] = "Mówię",
        [VoiceStateKind.ERROR] = "Błąd głosu",
    };

    /// <summary>Legalne przejścia ze stanu na inne stany.</summary>
    private static readonly Dictionary<VoiceStateKind, VoiceStateKind[]> Transitions = new()
    {
        [VoiceStateKind.OFF] = [VoiceStateKind.STANDBY, VoiceStateKind.ERROR],
        [VoiceStateKind.STANDBY] = [VoiceStateKind.LISTENING, VoiceStateKind.TRANSCRIBING, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.LISTENING] = [VoiceStateKind.TRANSCRIBING, VoiceStateKind.THINKING, VoiceStateKind.EXECUTING, VoiceStateKind.SPEAKING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.TRANSCRIBING] = [VoiceStateKind.THINKING, VoiceStateKind.EXECUTING, VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.THINKING] = [VoiceStateKind.EXECUTING, VoiceStateKind.SPEAKING, VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.EXECUTING] = [VoiceStateKind.THINKING, VoiceStateKind.SPEAKING, VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.SPEAKING] = [VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.ERROR] = [VoiceStateKind.OFF, VoiceStateKind.STANDBY, VoiceStateKind.LISTENING],
    };

    /// <summary>Górna granica czasu przebywania w stanie przed zwróceniem flagi zalegania (nie wymusza przejścia).</summary>
    private static readonly Dictionary<VoiceStateKind, TimeSpan> StuckLimits = new()
    {
        [VoiceStateKind.TRANSCRIBING] = TimeSpan.FromSeconds(30),
        [VoiceStateKind.THINKING] = TimeSpan.FromSeconds(120),
        [VoiceStateKind.EXECUTING] = TimeSpan.FromSeconds(300),
        [VoiceStateKind.SPEAKING] = TimeSpan.FromSeconds(180),
    };

    public event Action<VoiceStateInfo>? StateChanged;

    public VoiceStateInfo Current
    {
        get { lock (syncRoot) return SnapshotUnsafe(); }
    }

    public bool IsBusy { get { lock (syncRoot) return IsBusyState(state); } }
    /// <summary>The mic remains open in STANDBY for local wake-word detection.</summary>
    public bool IsMicrophoneOpen { get { lock (syncRoot) return state is not (VoiceStateKind.OFF or VoiceStateKind.ERROR); } }
    public string StateLabel { get { lock (syncRoot) return Labels[state]; } }

    /// <summary>Zmiana stanu z walidacją. Rzuca przy nielegalnym przejściu.</summary>
    public void TransitionTo(VoiceStateKind target, string reason = "")
    {
        VoiceStateInfo changed;
        lock (syncRoot)
        {
            ValidateTarget(target);
            if (target == state) return;
            if (!Transitions[state].Contains(target))
                throw new VoiceStateTransitionException(state.ToString(), target.ToString());
            RecordTransition(target, reason);
            changed = SnapshotUnsafe();
        }
        NotifyStateChanged(changed);
    }

    /// <summary>
    /// Synchronizes state with an observed external lifecycle event. Unlike TransitionTo this
    /// intentionally bypasses the graph: recovery, device failure, cancellation and shutdown
    /// must be able to publish the true state without guessing an intermediate state.
    /// </summary>
    public void ForceTransition(VoiceStateKind target, string reason = "")
    {
        VoiceStateInfo changed;
        lock (syncRoot)
        {
            ValidateTarget(target);
            if (target == state && string.Equals(detail, reason, StringComparison.Ordinal)) return;
            RecordTransition(target, reason);
            changed = SnapshotUnsafe();
        }
        NotifyStateChanged(changed);
    }

    /// <summary>Zwraca true, gdy aplikacja siedzi w stanie dłużej niż limit (diagnostyka „utknąłem").</summary>
    public bool IsStuck()
    {
        lock (syncRoot)
            return StuckLimits.TryGetValue(state, out TimeSpan limit) && DateTime.Now - stateSince > limit;
    }

    public TimeSpan TimeInState { get { lock (syncRoot) return DateTime.Now - stateSince; } }

    public IReadOnlyList<string> HistoryLines(int max = 12)
    {
        lock (syncRoot)
        {
            int count = Math.Clamp(max, 0, MaxHistory);
            return [.. history.Reverse().Take(count).Select(h => $"{h.At:HH:mm:ss} {h.From} → {h.To}{(h.Reason.Length > 0 ? " • " + h.Reason : "")}")];
        }
    }

    /// <summary>Reset do OFF (zamknięcie aplikacji, awaryjny stop).</summary>
    public void Reset(string reason = "reset") => ForceTransition(VoiceStateKind.OFF, reason);

    private VoiceStateInfo SnapshotUnsafe()
    {
        bool busy = IsBusyState(state);
        bool microphoneOpen = state is not (VoiceStateKind.OFF or VoiceStateKind.ERROR);
        return new VoiceStateInfo(state, Labels[state], detail, busy, microphoneOpen);
    }

    private static bool IsBusyState(VoiceStateKind value) => value is
        VoiceStateKind.TRANSCRIBING or VoiceStateKind.THINKING or VoiceStateKind.EXECUTING or VoiceStateKind.SPEAKING;

    private static void ValidateTarget(VoiceStateKind target)
    {
        if (!Enum.IsDefined(typeof(VoiceStateKind), target)) throw new ArgumentOutOfRangeException(nameof(target));
    }

    private void RecordTransition(VoiceStateKind target, string reason)
    {
        history.Enqueue((DateTime.Now, state, target, reason ?? ""));
        while (history.Count > MaxHistory) history.Dequeue();
        state = target;
        stateSince = DateTime.Now;
        detail = reason ?? "";
    }

    private void NotifyStateChanged(VoiceStateInfo stateInfo)
    {
        foreach (Action<VoiceStateInfo> observer in StateChanged?.GetInvocationList() ?? [])
        {
            try { observer(stateInfo); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "A voice-state observer failed.", ex); }
        }
    }
}
