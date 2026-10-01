using System.Net.Http;
using System.Globalization;
using System.IO.Compression;

namespace SentinelX.Services.Engine;

/// <summary>The built-in AI engine, fully automatic: finds or fetches llama-server, downloads the model that fits this PC, checks every file
/// against its pinned SHA-256, starts the engine only when a question arrives and frees the memory again when it is idle or a game runs.
/// There is nothing for the user to install, start or repair — and nothing named Ollama anywhere.</summary>
public sealed class EngineService : IEngineService, IEngineRuntime, IDisposable
{
    private const long Gb = 1024L * 1024 * 1024;
    private readonly EngineStore store;
    private readonly EngineDownloader downloader;
    private readonly LlamaServerHost host = new();
    private readonly Func<bool> isGaming;
    private readonly Func<double> totalRamGb;
    private readonly Func<bool> autoInstall;
    private bool installOnce;
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, int> loadFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> redownloads = new(StringComparer.OrdinalIgnoreCase);
    private Task? setup;
    private TaskCompletionSource wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private EngineStatus status = new("idle", "Silnik AI jeszcze nie wystartował.", 0, "", []);
    private Timer? idleTimer;
    private DateTime lastUse = DateTime.MinValue;
    private DateTime lastReport = DateTime.MinValue;
    private bool disposed;

    public EngineService(EngineStore? store = null, EngineDownloader? downloader = null, Func<bool>? isGaming = null, Func<double>? totalRamGb = null, Func<bool>? autoInstall = null)
    {
        this.autoInstall = autoInstall ?? (() => true);
        this.store = store ?? new EngineStore();
        this.downloader = downloader ?? new EngineDownloader();
        this.isGaming = isGaming ?? (() => false);
        this.totalRamGb = totalRamGb ?? (() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (double)Gb);
        status = Compose("idle", InstalledAny() ? "Silnik AI czeka na pierwsze pytanie." : "Silnik AI jeszcze nie wystartował.", 0);
    }

    public event Action? Changed;

    public EngineStatus Status { get { lock (gate) return status; } }

    public HttpMessageHandler CreateHandler() => new EngineOllamaFacade(this);

    /// <summary>One sentence for chat answers and readiness cards.</summary>
    public string Describe() => "Silnik AI: " + Status.Message;

    // ------------------------------------------------------------------ automatic setup

    public void Start()
    {
        lock (gate)
        {
            if (setup != null || disposed) return;
            setup = Task.Run(() => SetupLoopAsync(lifetime.Token));
        }
    }

    public async Task<string> RepairAsync(CancellationToken token)
    {
        foreach (EngineModelSpec spec in EngineCatalog.Models)
        {
            try
            {
                string path = store.ModelPath(spec);
                if (File.Exists(path) && new FileInfo(path).Length != spec.SizeBytes) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
        }
        lock (gate) { loadFailures.Clear(); installOnce = true; } // an explicit request counts as consent even when automatic downloads are off
        await host.StopAsync().ConfigureAwait(false);
        Start();
        WakeUp();
        token.ThrowIfCancellationRequested();
        return InstalledAny()
            ? "Sprawdzam pliki silnika AI i dokańczam to, czego brakuje — działa w tle, nic nie musisz robić."
            : "Silnik AI instaluje się w tle (kilka minut, zależnie od internetu). Polecenia systemowe działają już teraz, rozmowa z AI ruszy sama.";
    }

    private async Task SetupLoopAsync(CancellationToken token)
    {
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await EnsureAllAsync(token).ConfigureAwait(false);
                failures = 0;
                // later: re-check that nothing was quarantined or deleted; while automatic downloads are off, look again soon so that switching them on takes effect quickly
                await WaitAsync(TimeSpan.FromMinutes(autoInstall() ? 30 : 2), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                failures++;
                AppLog.Write(ex);
                TimeSpan delay = TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Min(failures, 5))));
                bool network = (ex is HttpRequestException or TimeoutException or IOException) && !ex.Message.Contains("miejsca", StringComparison.Ordinal);
                string reason = network ? "Nie mogę pobrać silnika AI (brak internetu?)." : ex.Message;
                SetStatus(Compose(InstalledAny() ? "ready" : "error", $"{reason} Spróbuję sam za {delay.TotalMinutes:0} min — nic nie musisz robić.", 0));
                try { await WaitAsync(delay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            }
        }
    }

    private async Task EnsureAllAsync(CancellationToken token)
    {
        bool allowed;
        lock (gate) { allowed = autoInstall() || installOnce; }
        bool missing = store.FindServerExecutable() == null || WantedModels().Any(x => !store.IsModelInstalled(x));
        if (!allowed && missing)
        {
            SetStatus(Compose(InstalledAny() ? "ready" : "idle", "Automatyczne pobieranie silnika AI jest wyłączone (Ustawienia → AI). Wpisz „napraw AI”, aby pobrać brakujące pliki teraz.", 0));
            return;
        }
        if (store.FindServerExecutable() == null) await InstallRuntimeAsync(token).ConfigureAwait(false);
        foreach (EngineModelSpec spec in WantedModels())
        {
            if (store.IsModelInstalled(spec)) continue;
            await WaitForQuietAsync(token).ConfigureAwait(false);
            await InstallModelAsync(spec, token).ConfigureAwait(false);
        }
        lock (gate) installOnce = false;
        SetStatus(Compose("ready", ReadyMessage(), 1));
    }

    /// <summary>The light model always; the stronger one only when the PC has the memory and disk for it.</summary>
    internal IReadOnlyList<EngineModelSpec> WantedModels()
    {
        var wanted = new List<EngineModelSpec> { EngineCatalog.Lite };
        EngineModelSpec strong = EngineCatalog.Standard;
        if (totalRamGb() >= strong.MinRamGb && (store.IsModelInstalled(strong) || store.FreeBytes() > strong.SizeBytes + 3 * Gb)) wanted.Add(strong);
        return wanted;
    }

    private async Task WaitForQuietAsync(CancellationToken token)
    {
        while (isGaming())
        {
            SetStatus(Compose(InstalledAny() ? "ready" : "paused", "Pobieranie wstrzymane na czas gry — ruszy samo po grze.", 0));
            await WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        }
    }

    private async Task InstallRuntimeAsync(CancellationToken token)
    {
        string zip = Path.Combine(store.DownloadsDirectory, EngineCatalog.LlamaZipFile);
        SetStatus(Compose("installing", "Przygotowuję silnik AI (pobieranie ok. 18 MB)…", 0));
        var progress = new InlineProgress(done => ReportProgress("Przygotowuję silnik AI", done, EngineCatalog.LlamaZipSize));
        await downloader.DownloadAsync(EngineCatalog.LlamaZipUrl, zip, EngineCatalog.LlamaZipSize, EngineCatalog.LlamaZipSha256, progress, token).ConfigureAwait(false);
        SetStatus(Compose("installing", "Rozpakowuję silnik AI…", 1));
        await Task.Run(() =>
        {
            string temp = store.RuntimeDirectory + ".tmp";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            ZipFile.ExtractToDirectory(zip, temp, true);
            if (Directory.Exists(store.RuntimeDirectory)) Directory.Delete(store.RuntimeDirectory, true);
            Directory.Move(temp, store.RuntimeDirectory);
        }, token).ConfigureAwait(false);
        try { File.Delete(zip); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (store.FindServerExecutable() == null) throw new IOException("Paczka silnika AI nie zawiera pliku llama-server.exe.");
    }

    private async Task InstallModelAsync(EngineModelSpec spec, CancellationToken token)
    {
        long needed = spec.SizeBytes - store.PartialBytes(spec) + Gb / 2;
        if (store.FreeBytes() < needed)
            throw new IOException($"Za mało miejsca na dysku na model {spec.Title} (potrzeba około {spec.SizeBytes / 1048576} MB).");
        string label = "Pobieram model " + spec.Title;
        SetStatus(Compose("installing", label + "…", 0));
        var progress = new InlineProgress(done => ReportProgress(label, done, spec.SizeBytes));
        await downloader.DownloadAsync(spec.Url, store.ModelPath(spec), spec.SizeBytes, spec.Sha256, progress, token).ConfigureAwait(false);
        lock (gate) loadFailures.Remove(spec.Name);
        SetStatus(Compose("ready", ReadyMessage(), 1));
    }

    private void ReportProgress(string label, long done, long total)
    {
        if (DateTime.UtcNow - lastReport < TimeSpan.FromMilliseconds(700)) return;
        lastReport = DateTime.UtcNow;
        double fraction = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
        string detail = $"{label}: {fraction * 100:0}% ({done / 1048576} z {total / 1048576} MB). Polecenia systemowe działają już teraz.";
        SetStatus(Compose(InstalledAny() ? "ready" : "installing", InstalledAny() ? ReadyMessage() + " " + detail : detail, fraction));
    }

    private string ReadyMessage()
    {
        EngineModelSpec? best = BestInstalled();
        string model = best?.Title ?? "model";
        bool stillMissing = WantedModels().Any(x => !store.IsModelInstalled(x));
        return $"Gotowy · {model} · działa lokalnie na tym komputerze, bez Ollamy i bez chmury." + (stillMissing ? " Mocniejszy model dociąga się w tle." : "");
    }

    private EngineModelSpec? BestInstalled() =>
        store.IsModelInstalled(EngineCatalog.Standard) ? EngineCatalog.Standard : store.IsModelInstalled(EngineCatalog.Lite) ? EngineCatalog.Lite : null;

    private bool InstalledAny() => EngineCatalog.Models.Any(store.IsModelInstalled);

    private EngineStatus Compose(string state, string message, double progress) =>
        new(state, message, progress, BestInstalled()?.Name ?? "", store.InstalledModels().Select(x => x.Name).ToArray());

    private void SetStatus(EngineStatus next)
    {
        lock (gate) status = next;
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken token)
    {
        Task woken;
        lock (gate) woken = wake.Task;
        await Task.WhenAny(Task.Delay(delay, token), woken).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    private void WakeUp()
    {
        TaskCompletionSource old;
        lock (gate)
        {
            old = wake;
            wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        old.TrySetResult();
    }

    // ------------------------------------------------------------------ IEngineRuntime (used by the facade)

    public IReadOnlyList<EngineModelInfo> InstalledModels() => store.InstalledModels().Select(spec =>
    {
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(store.ModelPath(spec)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { written = DateTime.UtcNow; }
        return new EngineModelInfo(spec.Name, spec.SizeBytes, new DateTimeOffset(written, TimeSpan.Zero), "sha256:" + spec.Sha256, spec.Family, spec.ParameterSize, spec.Quantization);
    }).ToArray();

    public async Task<EngineEndpoint> AcquireAsync(string model, int contextSize, CancellationToken token)
    {
        EngineModelSpec? spec = EngineCatalog.Find(model);
        string? exe = store.FindServerExecutable();
        if (spec == null || !store.IsModelInstalled(spec) || exe == null) throw new EngineUnavailableException(Describe());
        CancelIdleTimer();
        lock (gate) lastUse = DateTime.UtcNow;
        int threads = Math.Max(2, Environment.ProcessorCount / 2);
        try
        {
            (int port, string key) = await host.EnsureRunningAsync(exe, store.ModelPath(spec), Math.Max(contextSize, 2048), threads, token).ConfigureAwait(false);
            lock (gate) loadFailures.Remove(spec.Name);
            return new EngineEndpoint(new Uri($"http://127.0.0.1:{port}/"), key);
        }
        catch (EngineUnavailableException ex)
        {
            bool damaged;
            lock (gate)
            {
                loadFailures[spec.Name] = loadFailures.GetValueOrDefault(spec.Name) + 1;
                // Only a failure that talks about the model file counts (a missing runtime library must never make us throw away gigabytes),
                // and at most two re-downloads per run, so a broken PC can never loop forever.
                damaged = loadFailures[spec.Name] >= 2 && LooksLikeModelProblem(ex.Message) && redownloads.GetValueOrDefault(spec.Name) < 2;
                if (damaged) redownloads[spec.Name] = redownloads.GetValueOrDefault(spec.Name) + 1;
            }
            if (damaged)
            {
                // Twice in a row the model would not load: treat the file as damaged, delete it and let the setup loop fetch it again.
                store.DeleteModel(spec);
                SetStatus(Compose("installing", $"Plik modelu {spec.Title} wyglądał na uszkodzony — pobieram go ponownie.", 0));
                WakeUp();
                throw new EngineUnavailableException("Plik modelu był uszkodzony — pobieram go ponownie, to zajmie chwilę.");
            }
            SetStatus(Compose("error", ex.Message, 0));
            throw;
        }
    }

    internal static bool LooksLikeModelProblem(string message)
    {
        string text = message.ToLowerInvariant();
        return text.Contains("gguf") || text.Contains("invalid magic") || text.Contains("error loading model") || text.Contains("failed to load model")
            || text.Contains("unexpected end") || text.Contains("corrupt");
    }

    public void Release(string model, TimeSpan keepAlive)
    {
        lock (gate) lastUse = DateTime.UtcNow;
        if (keepAlive <= TimeSpan.Zero)
        {
            CancelIdleTimer();
            _ = host.StopAsync(); // free the RAM right now (for example while a game is running)
            return;
        }
        lock (gate)
        {
            idleTimer?.Dispose();
            idleTimer = new Timer(_ => StopWhenIdle(keepAlive), null, keepAlive, Timeout.InfiniteTimeSpan);
        }
    }

    public void Unload()
    {
        CancelIdleTimer();
        _ = host.StopAsync();
    }

    private void StopWhenIdle(TimeSpan keepAlive)
    {
        DateTime last;
        lock (gate) last = lastUse;
        if (DateTime.UtcNow - last < keepAlive - TimeSpan.FromSeconds(1)) return; // used again in the meantime
        _ = host.StopAsync();
    }

    private void CancelIdleTimer()
    {
        lock (gate)
        {
            idleTimer?.Dispose();
            idleTimer = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        CancelIdleTimer();
        host.Dispose();
        downloader.Dispose();
        lifetime.Dispose();
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
