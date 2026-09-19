using System.Text.RegularExpressions;

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
/// Maszyna stanów głosu z jawną walidacją przejść i timeoutami aktywności.
/// Wywołania przezodziewane wątkowo; wszystkie przejścia logują się do historii (ostatnie 64 wpisy).
/// </summary>
public sealed class VoiceStateMachine
{
    private readonly object syncRoot = new();
    private VoiceStateKind state = VoiceStateKind.OFF;
    private DateTime stateSince = DateTime.Now;
    private string detail = "";
    private readonly Queue<(DateTime At, VoiceStateKind From, VoiceStateKind To, string Reason)> history = new();
    private static readonly Dictionary<VoiceStateKind, string> Labels = new()
    {
        [VoiceStateKind.OFF] = "Mikrofon wyłączony",
        [VoiceStateKind.STANDBY] = "Czuwanie — powiedz „Sentinel”",
        [VoiceStateKind.LISTENING] = "Słucham",
        [VoiceStateKind.TRANSCRIBING] = "Zapisuję mowę",
        [VoiceStateKind.THINKING] = "Myślę",
        [VoiceStateKind.EXECUTING] = "Wykonuję polecenie",
        [VoiceStateKind.SPEAKING] = "Mówię",
        [VoiceStateKind.ERROR] = "Błąd głosu",
    };

    /// <summary>Legalne przejścia ze stanu na inne stany.</summary>
    private static readonly Dictionary<VoiceStateKind, VoiceStateKind[]> Transitions = new()
    {
        [VoiceStateKind.OFF] = [VoiceStateKind.STANDBY, VoiceStateKind.ERROR],
        [VoiceStateKind.STANDBY] = [VoiceStateKind.LISTENING, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.LISTENING] = [VoiceStateKind.TRANSCRIBING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.TRANSCRIBING] = [VoiceStateKind.THINKING, VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.OFF, VoiceStateKind.ERROR],
        [VoiceStateKind.THINKING] = [VoiceStateKind.EXECUTING, VoiceStateKind.SPEAKING, VoiceStateKind.LISTENING, VoiceStateKind.ERROR],
        [VoiceStateKind.EXECUTING] = [VoiceStateKind.SPEAKING, VoiceStateKind.LISTENING, VoiceStateKind.STANDBY, VoiceStateKind.ERROR],
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
        get
        {
            lock (syncRoot)
            {
                bool micOpen = state is not (VoiceStateKind.OFF or VoiceStateKind.STANDBY);
                bool busy = state is VoiceStateKind.TRANSCRIBING or VoiceStateKind.THINKING or VoiceStateKind.EXECUTING or VoiceStateKind.SPEAKING;
                return new VoiceStateInfo(state, Labels[state], detail, busy, micOpen);
            }
        }
    }

    public bool IsBusy { get { lock (syncRoot) return state is VoiceStateKind.TRANSCRIBING or VoiceStateKind.THINKING or VoiceStateKind.EXECUTING or VoiceStateKind.SPEAKING; } }
    public bool IsMicrophoneOpen { get { lock (syncRoot) return state is not (VoiceStateKind.OFF or VoiceStateKind.STANDBY); } }
    public string StateLabel { get { lock (syncRoot) return Labels[state]; } }

    /// <summary>Zmiana stanu z walidacją. Rzuca przy nielegalnym przejściu.</summary>
    public void TransitionTo(VoiceStateKind target, string reason = "")
    {
        lock (syncRoot)
        {
            if (target == state) return;
            if (!Transitions[state].Contains(target))
                throw new VoiceStateTransitionException(state.ToString(), target.ToString());
            history.Enqueue((DateTime.Now, state, target, reason));
            while (history.Count > 64) history.Dequeue();
            state = target;
            stateSince = DateTime.Now;
            detail = reason;
        }
        StateChanged?.Invoke(Current);
    }

    /// <summary>Spokojna zmiana stanu: gdy przejście jest nielegalne, przechodzi przez legalny przystanek (LISTENING), zamiast rzucać.</summary>
    public void ForceTransition(VoiceStateKind target, string reason = "")
    {
        try { TransitionTo(target, reason); }
        catch (VoiceStateTransitionException)
        {
            TransitionTo(VoiceStateKind.LISTENING, "reset pośredni");
            TransitionTo(target, reason);
        }
    }

    /// <summary>Zwraca true, gdy aplikacja siedzi w stanie dłużej niż limit (diagnostyka „utknąłem").</summary>
    public bool IsStuck()
    {
        lock (syncRoot)
        {
            if (!StuckLimits.TryGetValue(state, out TimeSpan limit)) return false;
            return DateTime.Now - stateSince > limit;
        }
    }

    public TimeSpan TimeInState { get { lock (syncRoot) return DateTime.Now - stateSince; } }

    public IReadOnlyList<string> HistoryLines(int max = 12)
    {
        lock (syncRoot)
        {
            return [.. history.Reverse().Take(max).Select(h => $"{h.At:HH:mm:ss} {h.From} → {h.To}{(h.Reason.Length > 0 ? " • " + h.Reason : "")}")];
        }
    }

    /// <summary>Reset do OFF (zamknięcie aplikacji, awaryjny stop).</summary>
    public void Reset(string reason = "reset")
    {
        lock (syncRoot)
        {
            history.Enqueue((DateTime.Now, state, VoiceStateKind.OFF, reason));
            while (history.Count > 64) history.Dequeue();
            state = VoiceStateKind.OFF;
            stateSince = DateTime.Now;
            detail = reason;
        }
        StateChanged?.Invoke(Current);
    }
}
