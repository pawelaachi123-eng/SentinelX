using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SentinelX.Services.Engine;

/// <summary>Runs llama-server.exe as a hidden child process: loopback only, random port, random API key. The process is placed in a
/// Windows job object, so it is killed together with the app even if the app crashes — no orphaned model eating RAM.</summary>
public sealed class LlamaServerHost : IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim startLock = new(1, 1);
    private readonly Queue<string> tail = new();
    private readonly HttpClient health = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
    private Process? process;
    private string loadedModel = "";
    private int loadedContext;
    private int port;
    private string apiKey = "";
    private IntPtr job;
    private bool disposed;

    public string LastError { get; private set; } = "";

    public bool IsRunning { get { lock (gate) return IsAliveLocked(); } }
    public string LoadedModelPath { get { lock (gate) return IsAliveLocked() ? loadedModel : ""; } }

    /// <summary>Returns the endpoint of a server that has <paramref name="modelPath"/> loaded with at least <paramref name="contextSize"/> tokens,
    /// starting (or restarting) the process when needed.</summary>
    public async Task<(int Port, string ApiKey)> EnsureRunningAsync(string serverExe, string modelPath, int contextSize, int threads, CancellationToken token)
    {
        await startLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (IsAliveLocked() && loadedModel == modelPath && loadedContext >= contextSize) return (port, apiKey);
            }
            StopCore();
            for (int profile = 0; profile < 2; profile++)
            {
                token.ThrowIfCancellationRequested();
                int newPort = FreePort();
                string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                IReadOnlyList<string> args = profile == 0 ? FullArguments(modelPath, newPort, contextSize, threads) : MinimalArguments(modelPath, newPort, contextSize);
                Process started = Launch(serverExe, args, key);
                lock (gate) { process = started; port = newPort; apiKey = key; loadedModel = modelPath; loadedContext = contextSize; }
                if (await WaitReadyAsync(started, newPort, TimeSpan.FromSeconds(240), token).ConfigureAwait(false))
                {
                    LastError = "";
                    return (newPort, key);
                }
                StopCore();
            }
            throw new EngineUnavailableException(string.IsNullOrEmpty(LastError) ? "Silnik AI nie uruchomił się." : LastError);
        }
        catch (OperationCanceledException)
        {
            StopCore();
            throw;
        }
        finally { startLock.Release(); }
    }

    internal static IReadOnlyList<string> FullArguments(string model, int port, int context, int threads) =>
    [
        "-m", model, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
        "-c", context.ToString(CultureInfo.InvariantCulture), "-t", threads.ToString(CultureInfo.InvariantCulture),
        "-np", "1", "--no-webui", "--jinja", "--reasoning", "off"
    ];

    /// <summary>Fallback when a future llama.cpp renames an option: the bare minimum every build understands.</summary>
    internal static IReadOnlyList<string> MinimalArguments(string model, int port, int context) =>
    [
        "-m", model, "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
        "-c", context.ToString(CultureInfo.InvariantCulture)
    ];

    private Process Launch(string exe, IReadOnlyList<string> args, string key)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["LLAMA_API_KEY"] = key; // not on the command line: other programs can list command lines
        var started = new Process { StartInfo = psi, EnableRaisingEvents = true };
        started.OutputDataReceived += (_, e) => AppendTail(e.Data);
        started.ErrorDataReceived += (_, e) => AppendTail(e.Data);
        started.Start();
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();
        AssignToJob(started);
        try { started.PriorityClass = ProcessPriorityClass.BelowNormal; } // the PC stays responsive while the model thinks
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return started;
    }

    private async Task<bool> WaitReadyAsync(Process started, int targetPort, TimeSpan timeout, CancellationToken token)
    {
        var uri = new Uri($"http://127.0.0.1:{targetPort}/health");
        DateTime until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            if (HasExited(started)) { LastError = DescribeExit(started); return false; }
            try
            {
                using HttpResponseMessage response = await health.GetAsync(uri, token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return true; // 503 = still loading the model
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !token.IsCancellationRequested) { }
            await Task.Delay(400, token).ConfigureAwait(false);
        }
        LastError = $"Silnik AI nie zdążył wczytać modelu w {timeout.TotalSeconds:0} s.";
        return false;
    }

    private string DescribeExit(Process exited)
    {
        int code;
        try { code = exited.ExitCode; }
        catch (InvalidOperationException) { code = -1; }
        string lines;
        lock (gate) lines = string.Join(" | ", tail.TakeLast(5));
        string hint = code == -1073741515
            ? " Brakuje bibliotek Visual C++ (vcruntime140.dll) — zainstaluj „Microsoft Visual C++ Redistributable x64”."
            : "";
        return $"llama-server zakończył działanie (kod {code}).{hint} {lines}".Trim();
    }

    private void AppendTail(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (gate)
        {
            tail.Enqueue(line.Length <= 300 ? line : line[..300]);
            while (tail.Count > 60) tail.Dequeue();
        }
    }

    /// <summary>Stops the process without racing a start that is in progress.</summary>
    public async Task StopAsync()
    {
        await startLock.WaitAsync().ConfigureAwait(false);
        try { StopCore(); }
        finally { startLock.Release(); }
    }

    /// <summary>Immediate stop for shutdown paths that must not wait.</summary>
    public void Stop() => StopCore();

    private void StopCore()
    {
        Process? old;
        lock (gate)
        {
            old = process;
            process = null;
            loadedModel = "";
            loadedContext = 0;
        }
        Kill(old);
    }

    private bool IsAliveLocked() => process != null && !HasExited(process);

    private static bool HasExited(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Kill(Process? p)
    {
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        finally
        {
            try { p.Dispose(); }
            catch (InvalidOperationException) { }
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopCore();
        health.Dispose();
        startLock.Dispose();
        if (job != IntPtr.Zero) { NativeJob.CloseHandle(job); job = IntPtr.Zero; }
    }

    private void AssignToJob(Process started)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            if (job == IntPtr.Zero) job = NativeJob.CreateKillOnCloseJob();
            if (job != IntPtr.Zero) NativeJob.AssignProcessToJobObject(job, started.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static class NativeJob
    {
        private const uint KillOnJobClose = 0x2000;
        private const int ExtendedLimitInformation = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
        [DllImport("kernel32.dll")]
        internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")]
        internal static extern bool CloseHandle(IntPtr handle);

        internal static IntPtr CreateKillOnCloseJob()
        {
            IntPtr created = CreateJobObject(IntPtr.Zero, null);
            if (created == IntPtr.Zero) return IntPtr.Zero;
            var limits = new ExtendedLimits();
            limits.Basic.LimitFlags = KillOnJobClose;
            int size = Marshal.SizeOf<ExtendedLimits>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (SetInformationJobObject(created, ExtendedLimitInformation, buffer, (uint)size)) return created;
            }
            finally { Marshal.FreeHGlobal(buffer); }
            CloseHandle(created);
            return IntPtr.Zero;
        }
    }
}
