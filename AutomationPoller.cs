using System.IO;

namespace SentinelX;

/// <summary>0.97 · jeden tik dla całej automatyzacji: harmonogram (#007), watchdog folderów (#008)
/// i bezpieczny schowek (#145). Wołany z timera interfejsu (nie częściej niż co 20 s).
/// <para>Nic nie robi w tle poza aplikacją: to ten sam tik, który obsługuje przypomnienia.
/// Gdy trwa inne zadanie, harmonogram czeka na następny tik (okno 15 minut od umówionej godziny).</para></summary>
public sealed class AutomationPoller
{
    private readonly SchedulerService? scheduler;
    private readonly WatchdogService? watchdog;
    private readonly Func<string, CancellationToken, Task>? runCommand;
    private readonly Action<string>? report;
    private readonly Func<bool>? isBusy;
    private DateTime lastTick = DateTime.MinValue;

    public AutomationPoller(SchedulerService? scheduler = null, WatchdogService? watchdog = null,
        Func<string, CancellationToken, Task>? runCommand = null, Action<string>? report = null, Func<bool>? isBusy = null)
    {
        this.scheduler = scheduler; this.watchdog = watchdog; this.runCommand = runCommand; this.report = report; this.isBusy = isBusy;
    }

    public static TimeSpan Interval => TimeSpan.FromSeconds(20);

    /// <summary>Wykonuje jeden obieg. Zwraca liczbę zgłoszonych zdarzeń (do testów i logów).</summary>
    public int Tick(DateTime? now = null, CancellationToken cancellationToken = default)
    {
        DateTime moment = now ?? DateTime.Now;
        if (lastTick != DateTime.MinValue && moment - lastTick < Interval) return 0;
        lastTick = moment;
        int reported = 0;

        if (SecureClipboard.Poll(moment))
        {
            report?.Invoke("🔒 Schowek wyczyszczony automatycznie (bezpieczny schowek).");
            JsonLog.Write("clipboard", "Automatyczne czyszczenie schowka.");
            reported++;
        }

        if (watchdog != null)
        {
            IReadOnlyList<WatchEvent> events = watchdog.Poll(moment);
            if (events.Count > 0)
            {
                string head = "📁 " + (events.Count == 1 ? "Zmiana w obserwowanym folderze" : "Zmiany w obserwowanych folderach (" + events.Count + ")") + ":";
                var lines = new List<string> { head };
                lines.AddRange(events.Take(5).Select(x => "· " + x.Kind + " — " + Path.GetFileName(x.Path)));
                report?.Invoke(string.Join("\n", lines));
                JsonLog.Write("watchdog", "Wykryto zmiany w folderach.", events.Count.ToString());
                reported++;
            }
        }

        if (scheduler != null)
        {
            if (isBusy?.Invoke() == true) return reported; // a running task keeps its turn; the schedule stays due
            foreach (ScheduleRecord record in scheduler.TakeDue(moment))
            {
                report?.Invoke("⏰ Harmonogram (" + record.Hour.ToString("00") + ":" + record.Minute.ToString("00") + "): „" + record.Command + "” — wykonuję.");
                JsonLog.Write("schedule", "Uruchomiono zaplanowane polecenie.", record.Command);
                reported++;
                if (runCommand == null) continue;
                string command = record.Command;
                try { _ = Task.Run(async () => { try { await runCommand(command, cancellationToken); } catch (Exception ex) { AppLog.Write("Harmonogram", "Zaplanowane polecenie nie powiodło się: " + command + " · " + ex.Message); } }); }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
            }
        }
        return reported;
    }
}
