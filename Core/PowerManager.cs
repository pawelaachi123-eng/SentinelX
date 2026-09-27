using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX.Core;

/// <summary>0.96 · JARVIS nad zasilaniem i ekranem.
/// <para>Zasada uzgodniona z użytkownikiem: <b>polecenie jest zgodą</b> — Sentinel nie pyta drugi raz,
/// gdy powiesz „zamknij komputer”. W zamian każda akcja, której nie da się cofnąć jednym ruchem
/// (zamknięcie, restart, uśpienie), ma <b>okno do odwołania</b>: licznik i komenda
/// „anuluj zamknięcie”. Blokada ekranu i wygaszenie monitora działają od razu, bo są odwracalne
/// (hasło albo ruch myszy) i nie niszczą pracy.</para>
/// Nic tu nie omija systemu: zamknięcie i restart idą przez systemowe <c>shutdown.exe</c>,
/// więc Windows może jeszcze zapytać o niezapisane dane.</summary>
public static class PowerManager
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private const int MonitorPowerOff = 2;
    private static readonly IntPtr Broadcast = new(0xFFFF);

    private static readonly object Gate = new();
    private static CancellationTokenSource? pending;
    private static string pendingDescription = "";

    /// <summary>Opis uzbrojonej akcji („” gdy nic nie czeka) — pokazywany w czacie i przy „anuluj zamknięcie”.</summary>
    public static string Pending
    {
        get { lock (Gate) return pendingDescription; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    /// <summary>Blokuje ekran (Windows+L). Odwracalne — hasło albo odcisk palca.</summary>
    public static bool Lock()
    {
        try { return LockWorkStation(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Wygasza monitor. Dowolny ruch myszy lub klawisz przywraca obraz.</summary>
    public static bool MonitorOff()
    {
        try { SendMessageW(Broadcast, WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(MonitorPowerOff)); return true; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Uśpienie (S3). false = system odmówił (np. konto bez prawa zamykania).</summary>
    public static bool Sleep()
    {
        try { return SetSuspendState(false, false, false); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Uzbraja akcję odroczoną. Zwraca opis dla użytkownika; „” = nieznany rodzaj.
    /// Odliczanie działa w tle — odpowiedź w czacie wraca od razu, a „anuluj zamknięcie” je zatrzymuje.</summary>
    public static string Arm(string kind, int seconds)
    {
        if (kind is not ("shutdown" or "restart" or "sleep")) return "";
        if (seconds < 0 || seconds > 3600) seconds = 60;
        lock (Gate)
        {
            pending?.Cancel();
            pending = new CancellationTokenSource();
            var token = pending.Token;
            pendingDescription = Describe(kind, seconds);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), token).ConfigureAwait(false);
                    lock (Gate) { pendingDescription = ""; pending = null; }
                    Execute(kind);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { AppLog.Write(ex); }
            });
            return pendingDescription;
        }
    }

    /// <summary>Odwołuje uzbrojoną akcję. true = było co odwołać.</summary>
    public static bool Abort()
    {
        lock (Gate)
        {
            if (pending == null) return false;
            pending.Cancel();
            pending = null;
            pendingDescription = "";
            return true;
        }
    }

    /// <summary>Użyteczne dla testów i raportów: nazwa akcji bez odliczania.</summary>
    public static string Describe(string kind, int seconds) => kind switch
    {
        "shutdown" => "zamknięcie komputera za " + seconds + " s",
        "restart" => "restart komputera za " + seconds + " s",
        "sleep" => "uśpienie komputera za " + seconds + " s",
        _ => "",
    };

    private static void Execute(string kind)
    {
        if (kind == "sleep") { Sleep(); return; }
        RunShutdown(kind == "restart" ? "/r" : "/s", 5);
    }

    /// <summary>shutdown.exe — systemowe zamknięcie/restart (Windows może jeszcze zapytać o zapis).</summary>
    private static bool RunShutdown(string argument, int seconds)
    {
        try
        {
            var info = new ProcessStartInfo(ShutdownTool())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(argument);
            info.ArgumentList.Add("/t");
            info.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
            using var process = Process.Start(info);
            return process != null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static string ShutdownTool()
    {
        try
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string candidate = Path.Combine(system, "shutdown.exe");
            return File.Exists(candidate) ? candidate : "shutdown.exe";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "shutdown.exe";
        }
    }
}
