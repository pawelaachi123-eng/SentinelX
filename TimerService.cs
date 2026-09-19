using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Pojedynczy timer albo przypomnienie zarządzane przez Sentinela.</summary>
public sealed class SentinelTimer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; init; } = "";
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime DueAt { get; init; }
    public bool Reminder { get; init; }
    public bool Completed { get; internal set; }
    public bool Cancelled { get; internal set; }
    public string? CompletionMessage { get; internal set; }
    public TimeSpan OriginalDuration { get; init; }
}

public sealed record TimerTickEvent(Guid Id, string Label, bool Reminder);

/// <summary>
/// Lokalny rejestr timerów i przypomnień. Nie używa wątków: stan licznika jest wyliczany
/// z czasu zegara, a wygasłe timery zwracane są przy każdym odpytaniu (PollDue), co jest
/// odporne na uśpienie systemu i prostsze do przetestowania.
/// </summary>
public sealed class TimerService
{
    private readonly object syncRoot = new();
    private readonly List<SentinelTimer> timers = [];
    private const int MaxTimers = 32;

    public int Count { get { lock (syncRoot) return timers.Count(t => !t.Completed && !t.Cancelled); } }

    /// <summary>Dodaje timer. Zwraca wpis albo null, gdy limit został osiągnięty.</summary>
    public SentinelTimer? Start(string label, TimeSpan duration, bool reminder = false)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)) return null;
        lock (syncRoot)
        {
            if (timers.Count(t => !t.Completed && !t.Cancelled) >= MaxTimers) return null;
            var timer = new SentinelTimer
            {
                Label = string.IsNullOrWhiteSpace(label) ? (reminder ? "Przypomnienie" : "Timer") : label.Trim(),
                DueAt = DateTime.Now + duration,
                Reminder = reminder,
                OriginalDuration = duration,
            };
            timers.Add(timer);
            return timer;
        }
    }

    /// <summary>Anuluje timer po identyfikatorze lub fragmencie etykiety. Zwraca true, gdy anulowano.</summary>
    public bool Cancel(Guid id) => CancelMatching(t => t.Id == id, out _);

    public bool CancelByLabel(string labelFragment)
    {
        if (string.IsNullOrWhiteSpace(labelFragment)) return false;
        string needle = labelFragment.Trim();
        return CancelMatching(t => !t.Completed && !t.Cancelled && t.Label.Contains(needle, StringComparison.OrdinalIgnoreCase), out _);
    }

    /// <summary>Anuluje najnowszy aktywny timer.</summary>
    public bool CancelLatest() => CancelMatching(t => !t.Completed && !t.Cancelled, out _, latestOnly: true);

    private bool CancelMatching(Func<SentinelTimer, bool> predicate, out SentinelTimer? cancelled, bool latestOnly = false)
    {
        lock (syncRoot)
        {
            var candidates = timers.Where(predicate).ToList();
            if (candidates.Count == 0) { cancelled = null; return false; }
            var target = latestOnly ? candidates[^1] : candidates[0];
            target.Cancelled = true;
            cancelled = target;
            return true;
        }
    }

    /// <summary>Zwraca timery, które właśnie wygasły (jednorazowo) i oznacza je jako ukończone.</summary>
    public IReadOnlyList<TimerTickEvent> PollDue()
    {
        List<TimerTickEvent> due = [];
        lock (syncRoot)
        {
            foreach (var timer in timers)
            {
                if (timer.Completed || timer.Cancelled) continue;
                if (DateTime.Now >= timer.DueAt)
                {
                    timer.Completed = true;
                    timer.CompletionMessage = (timer.Reminder ? "Przypomnienie: " : "Czas minął: ") + timer.Label;
                    due.Add(new TimerTickEvent(timer.Id, timer.CompletionMessage, timer.Reminder));
                }
            }
        }
        return due;
    }

    /// <summary>Zwraca listę aktywnych timerów z pozostałym czasem (do `..tasks` i panelu zadań).</summary>
    public IReadOnlyList<string> DescribeActive()
    {
        List<string> lines = [];
        lock (syncRoot)
        {
            foreach (var timer in timers.Where(t => !t.Completed && !t.Cancelled).OrderBy(t => t.DueAt))
            {
                TimeSpan left = timer.DueAt - DateTime.Now;
                lines.Add($"{(timer.Reminder ? "Przypomnienie" : "Timer")} „{timer.Label}” — pozostało {FormatLeft(left)} (start {timer.StartedAt:HH:mm}).");
            }
        }
        return lines;
    }

    public IReadOnlyList<SentinelTimer> Snapshot()
    {
        lock (syncRoot) return [.. timers];
    }

    internal static string FormatLeft(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        if (left.TotalMinutes >= 1)
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} min {1:00} s", left.TotalMinutes, left.Seconds);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} s", left.TotalSeconds);
    }
}

/// <summary>
/// Rozpoznaje polskie polecenia timerów i przypomnień. Współpracuje z PolishTextNormalizer
/// (liczebniki słowne), ale akceptuje też surowy tekst głosowy.
/// </summary>
public static class TimerCommandParser
{
    private static readonly Regex TimerSet = new(
        @"^(?:ustaw\s+)?(?:timer|licznik|minutnik)(?:\s+na)?\s+(?<dur>.+?)(?:\s+(?:na|o|dla|pt\.?|pt|zatytułowany)\s+(?<label>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReminderSet = new(
        @"^(?:przypomnij\s+mi(?:\s+za)?|przypomnij(?:\s+za)?|przypomnij\s+o)\s+(?<dur>.+?)(?:\s+(?:o|ze|że|aby|żeby|pt\.?|pt|na)\s+(?<label>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CancelTimer = new(
        @"^(?:anuluj|stop|skasuj|usun)\s+(?:timer|licznik|minutnik|przypomnienie)(?:\s+(?<label>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public enum TimerIntentKind { None, StartTimer, StartReminder, Cancel, ListActive }

    public sealed record TimerIntent(TimerIntentKind Kind, int? DurationSeconds, string Label, string RawText);

    public static TimerIntent Parse(string command)
    {
        string text = (command ?? "").Trim();
        if (text.Length == 0) return new TimerIntent(TimerIntentKind.None, null, "", text);

        string lowered = PolishTextNormalizer.StripDiacritics(text.ToLowerInvariant());
        string withNumbers = PolishTextNormalizer.ConvertSpokenNumbers(lowered);

        if (Regex.IsMatch(withNumbers, @"^(?:pokaz|lista|jakie)\s+(?:timery|minutniki|przypomnienia|moje timery|aktywne timery)"))
            return new TimerIntent(TimerIntentKind.ListActive, null, "", text);

        Match cancel = CancelTimer.Match(withNumbers);
        if (cancel.Success)
            return new TimerIntent(TimerIntentKind.Cancel, null, cancel.Groups["label"].Success ? cancel.Groups["label"].Value.Trim() : "", text);

        Match reminder = ReminderSet.Match(withNumbers);
        if (reminder.Success)
        {
            int? seconds = PolishTextNormalizer.TryParseDurationSeconds(reminder.Groups["dur"].Value);
            return new TimerIntent(TimerIntentKind.StartReminder, seconds, reminder.Groups["label"].Success ? reminder.Groups["label"].Value.Trim() : "", text);
        }

        Match timer = TimerSet.Match(withNumbers);
        if (timer.Success)
        {
            int? seconds = PolishTextNormalizer.TryParseDurationSeconds(timer.Groups["dur"].Value);
            return new TimerIntent(TimerIntentKind.StartTimer, seconds, timer.Groups["label"].Success ? timer.Groups["label"].Value.Trim() : "", text);
        }

        return new TimerIntent(TimerIntentKind.None, null, "", text);
    }
}
