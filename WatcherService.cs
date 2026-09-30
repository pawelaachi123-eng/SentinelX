using System.Diagnostics;
using System.IO;

namespace SentinelX;

/// <summary>Opt-in, low-cost watchers. Filesystem events plus a short stability sampler are used for downloads.</summary>
public sealed class WatcherService : IDisposable
{
    private readonly DownloadContextService downloads;
    private readonly GoalMemoryService goals;
    private readonly PerformanceHistoryService performance;
    private readonly GameFocusModeService focusMode;
    private readonly Func<WatchSettings> settingsProvider;
    private readonly WatcherAlertPolicy alertPolicy = new();
    private readonly DateTimeOffset serviceStartedAt = DateTimeOffset.Now;
    private readonly object gate = new();
    private Timer? sampler;
    private Process? watchedProcess;
    private bool performanceWatch;
    private bool disposed;
    public event Action<string>? NoticeRaised;

    public WatcherService(DownloadContextService downloads, GoalMemoryService goals, PerformanceHistoryService performance,
        GameFocusModeService? focusMode = null, Func<WatchSettings>? settingsProvider = null)
    {
        this.downloads = downloads;
        this.goals = goals;
        this.performance = performance;
        this.focusMode = focusMode ?? new GameFocusModeService();
        this.settingsProvider = settingsProvider ?? (() => new WatchSettings());
        downloads.Completed += DownloadCompleted;
        performance.Sampled += PerformanceSampled;
    }

    public bool IsAvailable => downloads.IsAvailable;
    public bool IsPerformanceWatchActive { get { lock (gate) return performanceWatch; } }
    public bool IsCs2ExitWatchActive { get { lock (gate) return watchedProcess != null; } }

    public void Start()
    {
        lock (gate)
        {
            if (disposed) return;
            sampler ??= new Timer(Sample, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
        performance.Start();
        var waiting = goals.GetRecent(20).FirstOrDefault(x => x.Status == "WAITING" && x.Steps.Any(s => s.Name == "stable_download"));
        if (waiting != null && DateTimeOffset.Now - waiting.StartedAt > TimeSpan.FromHours(24))
            goals.Finish(waiting.Id, "EXPIRED", "Obserwator pobrania wygasł po 24 godzinach bez potwierdzenia.", "Ustaw nową obserwację, jeśli nadal jest potrzebna.");
        else if (waiting != null && waiting.StartedAt < serviceStartedAt.AddSeconds(-2))
            downloads.SeedRecentFilesSince(waiting.StartedAt, ExtractRequestedName(waiting.Goal));
    }

    public string BeginDownloadWatch(string? requestedName, bool persist)
    {
        if (!downloads.IsAvailable)
            return "Nie mogę monitorować pobrań: folder Pobrane jest niedostępny. Nie uruchomiłem obserwatora.";
        var existing = goals.GetRecent(20).FirstOrDefault(x => x.Status == "WAITING" && x.Steps.Any(s => s.Name == "stable_download"));
        if (existing != null)
            return $"Już czekam na pobierany plik (cel {existing.Id}).\nNastępny krok: {existing.NextStep}";

        string target = SensitiveDataRedactor.Redact((requestedName ?? "").Trim());
        if (target.Length > 80) target = target[..77] + "…";
        string goalText = target.Length == 0 ? "Powiadom po zakończeniu nowego pobierania" : "Powiadom po zakończeniu pobierania: " + target;
        var goal = goals.Start(goalText, ["stable_download"], persist);
        string filter = target.Length == 0 ? "dowolny nowy plik w Pobrane" : $"plik o nazwie zawierającej „{target}”";
        goals.MarkWaiting(goal.Id, "stable_download", $"Oczekiwanie na {filter}; Sentinel nie otworzy ani nie uruchomi pliku.",
            "Po stabilnym zapisie sprawdzę, czy plik istnieje i można odczytać jego metadane.");
        Start();
        string checkpoint = !persist ? "tylko w pamięci — tryb prywatny lub zapis celów wyłączony ustawieniami pamięci"
            : goals.LastStorageError == null ? "lokalny checkpoint zapisany"
            : "checkpoint lokalny nie został potwierdzony: " + goals.LastStorageError;
        return $"Autopilot czeka na {filter} (cel {goal.Id}). Powiadomię dopiero po zdarzeniu systemu plików i potwierdzeniu stabilności. Nie otworzę ani nie uruchomię pliku.\nCheckpoint: {checkpoint}";
    }

    public string BeginPerformanceWatch()
    {
        lock (gate) performanceWatch = true;
        alertPolicy.Reset();
        Start();
        WatchSettings settings = GetSettings();
        return $"Uruchomiłem obserwację wydajności na Twoją prośbę. Używam lokalnego bufora CPU/RAM/GPU (próbki co 2 s, tylko RAM); alert dopiero po {Math.Clamp(settings.MinSecondsBeforeAlert, 1, 600)} s utrzymującego się przekroczenia progów CPU {Math.Clamp(settings.CpuAlertPercent, 1, 100)}% / RAM {Math.Clamp(settings.RamAlertPercent, 1, 100)}% / GPU 98%. Powiadomienia są wstrzymane w trybie skupienia gry. Brak FPS, frametime, temperatur, VRAM i taktowań. Obserwator działa do zamknięcia aplikacji; nie zmieniam ustawień.";
    }

    public string BeginCs2ExitWatch()
    {
        Process? selected = null;
        try
        {
            foreach (var process in Process.GetProcessesByName("cs2"))
            {
                if (selected == null) selected = process;
                else process.Dispose();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { selected?.Dispose(); return "Nie mogę odczytać listy procesów CS2: " + ex.Message; }
        if (selected == null) return "Nie wykrywam teraz procesu CS2, więc nie rozpocząłem obserwacji jego zakończenia.";
        int pid;
        try { pid = selected.Id; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { selected.Dispose(); return "Proces CS2 zakończył się przed rozpoczęciem obserwacji: " + ex.Message; }
        lock (gate)
        {
            if (disposed) { selected.Dispose(); return "Obserwator został zamknięty."; }
            try
            {
                if (watchedProcess != null && !watchedProcess.HasExited)
                {
                    selected.Dispose();
                    return "Już obserwuję zakończenie procesu CS2.";
                }
                DisposeWatchedProcessLocked();
                watchedProcess = selected;
                selected.Exited += WatchedProcessExited;
                selected.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                if (ReferenceEquals(watchedProcess, selected)) watchedProcess = null;
                selected.Exited -= WatchedProcessExited;
                selected.Dispose();
                return "Nie udało się rozpocząć obserwacji zakończenia CS2: " + ex.Message;
            }
        }
        return $"Obserwuję proces CS2 (PID {pid}) do zamknięcia Sentinel. Jeśli zniknie, powiadomię — nie będę zakładać, czy przyczyną był crash czy zwykłe zamknięcie.";
    }

    public void StopPerformanceWatch()
    {
        lock (gate) performanceWatch = false;
        alertPolicy.Reset();
    }

    public void StopCs2ExitWatch()
    {
        lock (gate) DisposeWatchedProcessLocked();
    }

    public string GetWatchStatus()
    {
        lock (gate)
        {
            var active = new List<string>();
            if (performanceWatch) active.Add("wydajność gry: aktywna");
            if (watchedProcess != null) active.Add("zakończenie CS2: aktywne");
            var waiting = goals.GetRecent(20).FirstOrDefault(x => x.Status == "WAITING" && x.Steps.Any(s => s.Name == "stable_download"));
            if (waiting != null) active.Add("pobieranie: oczekuje (cel " + waiting.Id + ")");
            WatchSettings settings = GetSettings();
            active.Add(settings.Enabled
                ? $"alerty zasobów: włączone (CPU ≥{Math.Clamp(settings.CpuAlertPercent, 1, 100)}%, RAM ≥{Math.Clamp(settings.RamAlertPercent, 1, 100)}%, minimum {Math.Clamp(settings.MinSecondsBeforeAlert, 1, 600)} s; cooldown {Math.Clamp(settings.CooldownMinutes, 1, 240)} min)"
                : "alerty automatyczne zasobów: wyłączone; jawnie uruchomiona obserwacja działa niezależnie od tego przełącznika");
            if (focusMode.IsActive) active.Add("tryb skupienia: aktywny — powiadomienia Watch są wstrzymane");
            return string.Join("\n", active);
        }
    }

    private void Sample(object? _)
    {
        try { downloads.Sample(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private void PerformanceSampled(PerformanceSample sample)
    {
        bool explicitlyRequested;
        lock (gate) explicitlyRequested = performanceWatch;
        string? notice = alertPolicy.Observe(sample, GetSettings(), explicitlyRequested, focusMode.IsActive);
        if (notice != null) RaiseNotice(notice);
    }

    private WatchSettings GetSettings()
    {
        try { return settingsProvider() ?? new WatchSettings(); }
        catch (Exception ex) { AppLog.Write(ex); return new WatchSettings(); }
    }

    private void WatchedProcessExited(object? sender, EventArgs e)
    {
        Process? exited;
        lock (gate)
        {
            exited = watchedProcess;
            if (exited == null || !ReferenceEquals(sender, exited)) return;
            exited.Exited -= WatchedProcessExited;
            watchedProcess = null;
        }
        string name = "CS2";
        try { name = exited.ProcessName; } catch { }
        try { exited.Dispose(); } catch { }
        RaiseNotice($"Proces {name} przestał działać. Nie mam dowodu, czy zamknięcie było awarią, czy działaniem użytkownika.");
    }

    private void DownloadCompleted(RecentDownload item)
    {
        var goal = goals.GetRecent(20).FirstOrDefault(x => x.Status == "WAITING" && x.Steps.Any(s => s.Name == "stable_download"));
        if (goal == null) return;
        string expected = ExtractRequestedName(goal.Goal);
        string fileName = Path.GetFileName(item.Path);
        if (expected.Length > 0 && !fileName.Contains(expected, StringComparison.OrdinalIgnoreCase)) return;

        string verification;
        long length;
        try
        {
            var info = new FileInfo(item.Path);
            if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                return;
            using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            length = stream.Length;
            verification = $"Potwierdzono: plik istnieje, przeszedł próbki stabilności i metadane są czytelne ({length:N0} bajtów).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            verification = "Nie udało się ponownie odczytać pliku po zdarzeniu: " + ex.Message;
            goals.Finish(goal.Id, "UNVERIFIED", verification, "Sprawdź dostęp do pliku i uruchom monitorowanie ponownie, jeśli nadal trzeba.");
            RaiseNotice("Pobieranie wykryte, ale nie potwierdziłem odczytu pliku. Szczegóły w statusie Autopilota.");
            return;
        }

        goals.Finish(goal.Id, "VERIFIED", verification, "Plik nie został otwarty ani uruchomiony. Możesz poprosić o sprawdzenie typu lub dalszy bezpieczny krok.");
        RaiseNotice("Pobieranie zakończone i plik zweryfikowany: " + fileName + " („ + length.ToString("N0") + " B). Niczego nie otworzyłem.");
    }

    private static string ExtractRequestedName(string goal)
    {
        const string prefix = "Powiadom po zakończeniu pobierania: ";
        return goal.StartsWith(prefix, StringComparison.Ordinal) ? goal[prefix.Length..].Trim() : "";
    }

    private void DisposeWatchedProcessLocked()
    {
        if (watchedProcess == null) return;
        watchedProcess.Exited -= WatchedProcessExited;
        watchedProcess.Dispose();
        watchedProcess = null;
    }

    private void RaiseNotice(string text)
    {
        try { NoticeRaised?.Invoke(text); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            sampler?.Dispose(); sampler = null;
            DisposeWatchedProcessLocked();
        }
        downloads.Completed -= DownloadCompleted;
        performance.Sampled -= PerformanceSampled;
    }
}
