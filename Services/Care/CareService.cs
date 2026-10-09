using System.IO;
using System.Net.Sockets;
using SentinelX.Core;
using SentinelX.Services.Engine;
using SentinelX.Services.Link;
using SentinelX.Services.Settings;

namespace SentinelX.Services.Care;

/// <summary>The "nothing to look after" part of the app. It starts the phone link and the AI engine by itself, checks on them every minute
/// and restarts what stopped, turns reminders and problems into alerts (PC notification + phone), and keeps one honest status line
/// ("Wszystko działa samo" or what needs attention). The user is asked for something only when nothing else can fix it.</summary>
public sealed class CareService : IDisposable
{
    private readonly ISettingsService settings;
    private readonly LinkService link;
    private readonly IEngineService engine;
    private readonly AlertFeed alerts;
    private readonly TaskService tasks;
    private readonly Services.Base.OllamaSupervisor? ollama;
    private readonly object gate = new();
    private readonly SemaphoreSlim tick = new(1, 1);
    private Timer? timer;
    private bool started;
    private bool disposed;
    private bool sawInstalling;
    private bool readyAnnounced;
    private int engineErrorTicks;
    private string linkProblem = "";
    private string statusText = "Sentinel pilnuje komputera sam.";
    private bool allGood = true;
    private DateTime lastEngineAlert = DateTime.MinValue;
    private DateTime lastDiskAlert = DateTime.MinValue;

    public CareService(ISettingsService settings, LinkService link, IEngineService engine, AlertFeed alerts, TaskService tasks, Services.Base.OllamaSupervisor? ollama = null)
    {
        this.settings = settings;
        this.link = link;
        this.engine = engine;
        this.alerts = alerts;
        this.tasks = tasks; this.ollama = ollama;
    }

    public event Action? Changed;

    public string StatusText { get { lock (gate) return statusText; } }
    public bool AllGood { get { lock (gate) return allGood; } }

    /// <summary>The facts the phone shows on its "Komputer" screen.</summary>
    public LinkInfo BuildLinkInfo()
    {
        EngineStatus e = engine.Status;
        return new LinkInfo(AppConstants.Version, e.State, e.Message, e.Progress, e.Model, e.Installed, AllGood, StatusText);
    }

    /// <summary>Begins the automatic care. Does nothing in the UI smoke test, so the test never opens ports or downloads models.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed) return;
            started = true;
        }
        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1" || Environment.GetEnvironmentVariable("SENTINEL_NO_CARE") == "1") return;
        try { ApplyAutopilotOnce(); }
        catch (Exception ex) { AppLog.Write(ex); }
        tasks.ReminderFired += OnReminder;
        engine.Changed += OnEngineChanged;
        PhoneHint.Describe = DescribePhone;
        engine.Start();
        timer = new Timer(_ => { _ = TickAsync(); }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));
    }

    /// <summary>First run of a build with the autopilot on an existing settings file: switch on what "no babysitting" needs
    /// (start with Windows, load watch, phone link). Afterwards every switch belongs to the user again.</summary>
    internal void ApplyAutopilotOnce()
    {
        SentinelSettings current = settings.Current;
        if (current.Startup.AutopilotApplied) return;
        current.Startup.StartWithWindows = true;
        current.Watch.Enabled = true;
        current.Link.Enabled = true;
        current.Startup.AutopilotApplied = true;
        settings.Save();
    }

    private string DescribePhone()
    {
        if (!settings.Current.Link.Enabled) return "Połączenie z telefonem jest wyłączone. Włączysz je w Ustawienia → Telefon.";
        if (!link.IsRunning) return "Łącze z telefonem właśnie się uruchamia — spróbuj za chwilę.";
        IReadOnlyList<string> urls = link.Urls;
        string address = urls.Count == 0 ? "Brak adresu w sieci — połącz komputer z Wi‑Fi lub kablem." : "Adres w przeglądarce telefonu: " + string.Join("  ·  ", urls);
        string phones = link.Devices.Count == 0 ? "Żaden telefon nie jest jeszcze sparowany." : $"Sparowane telefony: {string.Join(", ", link.Devices.Select(x => x.Name))}.";
        return "TELEFON\nAndroid: otwórz aplikację „Sentinel X Telefon” — sama znajdzie ten komputer, a tu wystarczy kliknąć „Zezwól”.\niPhone i przeglądarka: zeskanuj kod QR z okna „Telefon” (zasobnik → Telefon…) albo wpisz adres.\n" + address + "\n" + phones;
    }

    private void OnReminder(string line) =>
        alerts.Add("info", "Przypomnienie", line.Replace("⏰", "").Trim());

    private void OnEngineChanged() => RaiseChanged();

    private async Task TickAsync()
    {
        if (!await tick.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            await EnsureLinkAsync().ConfigureAwait(false);
            if (settings.Current.Ai.EnsureOllamaServer && ollama != null) await ollama.EnsureAsync(CancellationToken.None).ConfigureAwait(false);
            engine.Start();
            CheckEngine();
            CheckDisk();
            ComposeStatus();
        }
        catch (Exception ex) { AppLog.Write(ex); }
        finally { tick.Release(); }
        RaiseChanged();
    }

    private async Task EnsureLinkAsync()
    {
        bool wanted = settings.Current.Link.Enabled;
        if (wanted && !link.IsRunning)
        {
            try
            {
                await link.StartAsync().ConfigureAwait(false);
                linkProblem = "";
            }
            catch (Exception ex) when (ex is InvalidOperationException or SocketException or IOException or UnauthorizedAccessException)
            {
                if (linkProblem != ex.Message)
                {
                    linkProblem = ex.Message;
                    alerts.Add("warn", "Telefon: nie mogę się połączyć", "Nie udało się uruchomić łącza z telefonem: " + ex.Message);
                }
            }
        }
        else if (!wanted && link.IsRunning) await link.StopAsync().ConfigureAwait(false);
    }

    private void CheckEngine()
    {
        EngineStatus s = engine.Status;
        if (s.State == "installing") sawInstalling = true;
        if (s.State == "ready" && sawInstalling && !readyAnnounced && s.Installed.Count > 0)
        {
            readyAnnounced = true;
            alerts.Add("info", "Silnik AI gotowy", s.Message);
        }
        if (s.State == "error")
        {
            engineErrorTicks++;
            if (engineErrorTicks >= 3 && DateTime.UtcNow - lastEngineAlert > TimeSpan.FromHours(6))
            {
                lastEngineAlert = DateTime.UtcNow;
                alerts.Add("warn", "Silnik AI wymaga uwagi", s.Message);
            }
        }
        else engineErrorTicks = 0;
    }

    private void CheckDisk()
    {
        if (DateTime.UtcNow - lastDiskAlert < TimeSpan.FromHours(24)) return;
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0) return;
            double freePercent = drive.AvailableFreeSpace * 100.0 / drive.TotalSize;
            if (freePercent < 8 || drive.AvailableFreeSpace < 10L * 1024 * 1024 * 1024)
            {
                lastDiskAlert = DateTime.UtcNow;
                alerts.Add("warn", "Mało miejsca na dysku", $"Na {drive.Name} zostało {drive.AvailableFreeSpace / 1073741824.0:0.0} GB wolnego ({freePercent:0}%).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void ComposeStatus()
    {
        var parts = new List<string>();
        bool good = true;
        if (!settings.Current.Link.Enabled) parts.Add("telefon wyłączony w ustawieniach");
        else if (link.IsRunning) parts.Add(link.Devices.Count == 0 ? "telefon czeka na pierwsze połączenie" : $"telefon: sparowane {link.Devices.Count}");
        else
        {
            parts.Add("telefon: uruchamiam…");
            if (linkProblem.Length > 0) good = false;
        }
        EngineStatus e = engine.Status;
        parts.Add(e.State switch
        {
            "ready" => "silnik AI gotowy",
            "installing" => "silnik AI instaluje się sam",
            "paused" => "instalacja AI czeka na koniec gry",
            "error" => "silnik AI czeka na internet",
            _ => "silnik AI startuje"
        });
        if (e.State == "error") good = false;
        string text = (good ? "Wszystko działa samo" : "Wymaga uwagi") + " · " + string.Join(" · ", parts) + " · ostatnia kontrola " + DateTime.Now.ToString("HH:mm");
        lock (gate)
        {
            statusText = text;
            allGood = good;
        }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Dispose();
        tasks.ReminderFired -= OnReminder;
        engine.Changed -= OnEngineChanged;
        tick.Dispose();
    }
}
